use std::{
    error::Error,
    sync::{
        atomic::{AtomicBool, Ordering},
        Arc, Mutex,
    },
    thread::{self, JoinHandle},
    time::Instant,
};

use windows_capture::{
    capture::{Context, GraphicsCaptureApiHandler},
    frame::Frame,
    graphics_capture_api::InternalCaptureControl,
    monitor::Monitor,
    settings::{
        ColorFormat, CursorCaptureSettings, DirtyRegionSettings, DrawBorderSettings,
        MinimumUpdateIntervalSettings, SecondaryWindowSettings, Settings,
    },
};

use crate::utils::console_redirect::log_error;

/// Handler 中持有的状态
struct CaptureHandler {
    _start: Instant,
    /// 用于停止信号
    running: Arc<AtomicBool>,
    /// 外部共享的像素缓冲（只存正方形区域）
    buffer: Arc<Mutex<Vec<u8>>>,
    /// 正方形边长（像素）
    square_size: usize,
    /// 错误标志
    error_flag: Arc<AtomicBool>,
}

impl GraphicsCaptureApiHandler for CaptureHandler {
    type Flags = (Arc<AtomicBool>, Arc<Mutex<Vec<u8>>>, usize, Arc<AtomicBool>);
    type Error = Box<dyn Error + Send + Sync>;

    fn new(ctx: Context<Self::Flags>) -> Result<Self, Self::Error> {
        let (running, buffer, square_size, error_flag) = ctx.flags;

        Ok(Self {
            _start: Instant::now(),
            running,
            buffer,
            square_size,
            error_flag,
        })
    }

    fn on_frame_arrived(
        &mut self,
        frame: &mut Frame,
        capture_control: InternalCaptureControl,
    ) -> Result<(), Self::Error> {
        // 1. 获取 FrameBuffer 实例
        let mut fb = match frame.buffer() {
            Ok(fb) => fb,
            Err(e) => {
                log_error(&format!("屏幕捕获 - 获取帧缓冲失败: {:?}", e));
                self.error_flag.store(true, Ordering::SeqCst);
                return Err(e.into());
            }
        };
        
        // 2. 拿到整帧字节 & stride
        let width_full = fb.width() as usize;
        let height_full = fb.height() as usize;
        let stride = width_full * 4; // RGBA/BGRA 每像素4字节
        let src: &mut [u8] = match fb.as_nopadding_buffer() {
            Ok(src) => src,
            Err(e) => {
                log_error(&format!("屏幕捕获 - 获取缓冲区失败: {:?}", e));
                self.error_flag.store(true, Ordering::SeqCst);
                return Err(e.into());
            }
        }; // 不含 padding，每行紧凑

        // 3. 计算中心正方形区域左上角坐标
        let sq = self.square_size;
        let x_off = (width_full.saturating_sub(sq)) / 2;
        let y_off = (height_full.saturating_sub(sq)) / 2;

        // 4. 拷贝中心正方形到共享缓冲区
        match self.buffer.lock() {
            Ok(mut dst) => {
                // dst 长度应当为 sq * sq * 3
                if dst.len() != sq * sq * 3 {
                    dst.resize(sq * sq * 3, 0);
                }
                // 先清零
                for v in dst.iter_mut() { *v = 0; }
                // 直接写入CHW格式
                // R通道
                for row in 0..sq {
                    let src_start = (y_off + row) * stride + x_off * 4;
                    if src_start + sq * 4 <= src.len() {
                        let src_row = &src[src_start..src_start + sq * 4];
                        for col in 0..sq {
                            let src_idx = col * 4;
                            if src_idx + 2 < src_row.len() {
                                let r = src_row[src_idx];
                                let g = src_row[src_idx + 1];
                                let b = src_row[src_idx + 2];
                                let chw_idx = row * sq + col;
                                if chw_idx < sq * sq {
                                    dst[0 * sq * sq + chw_idx] = r; // R
                                    dst[1 * sq * sq + chw_idx] = g; // G
                                    dst[2 * sq * sq + chw_idx] = b; // B
                                }
                            }
                        }
                    }
                }
            }
            Err(e) => {
                log_error(&format!("屏幕捕获 - 获取缓冲区锁失败: {:?}", e));
                self.error_flag.store(true, Ordering::SeqCst);
            }
        }

        // 6. 检查停止标志
        if !self.running.load(Ordering::SeqCst) {
            capture_control.stop();
        }
        Ok(())
    }

    fn on_closed(&mut self) -> Result<(), Self::Error> {
        // println!("Capture session closed after {}s", self.start.elapsed().as_secs());
        Ok(())
    }
}

/// 对外的封装
pub struct ScreenCapturer {
    buffer: Arc<Mutex<Vec<u8>>>,
    running: Arc<AtomicBool>,
    handle: JoinHandle<()>,
    pub square_size: usize,
    error_flag: Arc<AtomicBool>,
}

impl ScreenCapturer {
    /// 静态方法：创建并启动后台抓取线程
    pub fn start(square_size: usize) -> Result<Self, Box<dyn Error>> {
        // 1. 准备共享数据
        let buf = vec![0u8; square_size * square_size * 3];
        let buffer = Arc::new(Mutex::new(buf));
        let running = Arc::new(AtomicBool::new(true));
        let error_flag = Arc::new(AtomicBool::new(false));
        let sq = square_size;

        // 2. 构造 capture 设置
        let monitor = Monitor::primary().expect("没有主显示器");
        let settings = Settings::new(
            monitor,
            CursorCaptureSettings::WithoutCursor,
            DrawBorderSettings::WithoutBorder,
            SecondaryWindowSettings::Default,
            MinimumUpdateIntervalSettings::Default, // 改为默认设置
            DirtyRegionSettings::Default,
            ColorFormat::Rgba8,
            // 把 running、buffer、sq、error_flag 打包传给 handler
            (running.clone(), buffer.clone(), sq, error_flag.clone()),
        );

        // 3. 启动线程
        let error_flag_clone = error_flag.clone();
        let handle = thread::spawn(move || {
            // CaptureHandler::start 会内部轮询 running flag，并不断写入 buffer
            if let Err(e) = CaptureHandler::start(settings) {
                log_error(&format!("屏幕捕获线程启动失败: {:?}", e));
                error_flag_clone.store(true, Ordering::SeqCst);
            }
        });

        // println!("屏幕捕获线程已启动");
        Ok(ScreenCapturer { buffer, running, handle, square_size, error_flag })
    }

    /// 消费式停止：发出停止信号并等待线程退出
    pub fn stop(self) {
        // 通知线程结束
        self.running.store(false, Ordering::SeqCst);
        // 等待线程真正退出
        let _ = self.handle.join();
    }

    /// 获取当前帧缓冲区句柄
    pub fn buffer(&self) -> Arc<Mutex<Vec<u8>>> {
        self.buffer.clone()
    }

    /// 获取错误标志
    pub fn error_flag(&self) -> Arc<AtomicBool> {
        self.error_flag.clone()
    }
}