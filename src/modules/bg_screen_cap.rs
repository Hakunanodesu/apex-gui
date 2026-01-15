use std::{
    error::Error,
    sync::{
        atomic::{AtomicBool, AtomicU64, Ordering},
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
    /// 缓冲区版本号，每次更新时递增
    version: Arc<AtomicU64>,
    /// 捕获帧率统计
    fps: Arc<Mutex<f32>>,
    /// 捕获计数
    capture_count: Arc<Mutex<u32>>,
    /// 上次统计时间
    last_fps_time: Arc<Mutex<Instant>>,
    /// 正方形边长（像素）
    square_size: usize,
    /// 错误标志
    error_flag: Arc<AtomicBool>,
}

impl GraphicsCaptureApiHandler for CaptureHandler {
    type Flags = (Arc<AtomicBool>, Arc<Mutex<Vec<u8>>>, Arc<AtomicU64>, Arc<Mutex<f32>>, Arc<Mutex<u32>>, Arc<Mutex<Instant>>, usize, Arc<AtomicBool>);
    type Error = Box<dyn Error + Send + Sync>;

    fn new(ctx: Context<Self::Flags>) -> Result<Self, Self::Error> {
        let (running, buffer, version, fps, capture_count, last_fps_time, square_size, error_flag) = ctx.flags;

        Ok(Self {
            _start: Instant::now(),
            running,
            buffer,
            version,
            fps,
            capture_count,
            last_fps_time,
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
        let src: &[u8] = match fb.as_nopadding_buffer() {
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
        
        // 4. 在锁外准备数据，避免长时间持有锁
        let buffer_size = sq * sq * 3;
        let mut temp_buffer = vec![0u8; buffer_size];
        
        // 5. 优化像素拷贝：先验证边界，减少循环内检查
        let max_src_start = (y_off + sq - 1) * stride + (x_off + sq) * 4;
        if max_src_start > src.len() {
            log_error("屏幕捕获 - 源缓冲区越界");
            self.error_flag.store(true, Ordering::SeqCst);
            return Ok(()); // 跳过这一帧
        }
        
        // 6. 直接写入CHW格式，使用更高效的循环
        for row in 0..sq {
            let src_row_start = (y_off + row) * stride + x_off * 4;
            let src_row = &src[src_row_start..src_row_start + sq * 4];
            
            for col in 0..sq {
                let src_pixel = &src_row[col * 4..col * 4 + 3];
                let chw_idx = row * sq + col;
                
                temp_buffer[0 * sq * sq + chw_idx] = src_pixel[0]; // R
                temp_buffer[1 * sq * sq + chw_idx] = src_pixel[1]; // G
                temp_buffer[2 * sq * sq + chw_idx] = src_pixel[2]; // B
            }
        }
        
        // 7. 快速写入锁，最小化锁持有时间
        match self.buffer.lock() {
            Ok(mut dst) => {
                // 确保大小正确
                if dst.len() != buffer_size {
                    dst.resize(buffer_size, 0);
                }
                // 直接拷贝，不需要清零
                dst.copy_from_slice(&temp_buffer);
                // 缓冲区更新后，递增版本号
                self.version.fetch_add(1, Ordering::Relaxed);
            }
            Err(e) => {
                log_error(&format!("屏幕捕获 - 获取缓冲区锁失败: {:?}", e));
                self.error_flag.store(true, Ordering::SeqCst);
            }
        }
        
        // 8. FPS统计移到锁外，避免增加buffer锁持有时间
        if let Ok(mut count_guard) = self.capture_count.lock() {
            *count_guard += 1;
            
            if let Ok(mut time_guard) = self.last_fps_time.lock() {
                if time_guard.elapsed().as_secs_f32() >= 1.0 {
                    if let Ok(mut fps_guard) = self.fps.lock() {
                        *fps_guard = *count_guard as f32;
                        *count_guard = 0;
                        *time_guard = Instant::now();
                    }
                }
            }
        }

        // 9. 检查停止标志
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
    version: Arc<AtomicU64>,
    fps: Arc<Mutex<f32>>,
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
        let version = Arc::new(AtomicU64::new(0));
        let fps = Arc::new(Mutex::new(0.0));
        let capture_count = Arc::new(Mutex::new(0u32));
        let last_fps_time = Arc::new(Mutex::new(Instant::now()));
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
            // MinimumUpdateIntervalSettings::Custom(std::time::Duration::from_millis(5)),
            DirtyRegionSettings::Default,
            ColorFormat::Rgba8,
            // 把 running、buffer、version、fps、capture_count、last_fps_time、sq、error_flag 打包传给 handler
            (running.clone(), buffer.clone(), version.clone(), fps.clone(), capture_count.clone(), last_fps_time.clone(), sq, error_flag.clone()),
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
        Ok(ScreenCapturer { buffer, version, fps, running, handle, square_size, error_flag })
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

    /// 获取缓冲区版本号
    pub fn version(&self) -> Arc<AtomicU64> {
        self.version.clone()
    }

    /// 获取捕获帧率
    pub fn fps(&self) -> Arc<Mutex<f32>> {
        self.fps.clone()
    }

    /// 获取错误标志
    pub fn error_flag(&self) -> Arc<AtomicBool> {
        self.error_flag.clone()
    }
}