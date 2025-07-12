use std::{
    error::Error,
    sync::{
        atomic::{AtomicBool, Ordering},
        Arc, Mutex,
    },
    thread::{self, JoinHandle},
    time::{Duration, Instant},
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

/// Handler 中持有的状态
struct CaptureHandler {
    start: Instant,
    /// 用于停止信号
    running: Arc<AtomicBool>,
    /// 外部共享的像素缓冲（只存正方形区域）
    buffer: Arc<Mutex<Vec<u8>>>,
    /// 正方形边长（像素）
    square_size: usize,
    frame_count: Arc<Mutex<u32>>, // 新增
    last_fps: Arc<Mutex<u32>>, // 新增
    last_time: Arc<Mutex<Instant>>, // 新增
}

impl GraphicsCaptureApiHandler for CaptureHandler {
    type Flags = (Arc<AtomicBool>, Arc<Mutex<Vec<u8>>>, usize, Arc<Mutex<u32>>, Arc<Mutex<u32>>, Arc<Mutex<Instant>>);
    type Error = Box<dyn Error + Send + Sync>;

    fn new(ctx: Context<Self::Flags>) -> Result<Self, Self::Error> {
        let (running, buffer, square_size, frame_count, last_fps, last_time) = ctx.flags;

        Ok(Self {
            start: Instant::now(),
            running,
            buffer,
            square_size,
            frame_count,
            last_fps,
            last_time,
        })
    }

    fn on_frame_arrived(
        &mut self,
        frame: &mut Frame,
        capture_control: InternalCaptureControl,
    ) -> Result<(), Self::Error> {
        // 1. 获取 FrameBuffer 实例
        let mut fb = frame.buffer()?;
        // 2. 拿到整帧字节 & stride
        let width_full = fb.width() as usize;
        let height_full = fb.height() as usize;
        let stride = width_full * 4; // RGBA/BGRA 每像素4字节
        let src: &mut [u8] = fb.as_nopadding_buffer()?; // 不含 padding，每行紧凑

        // 3. 计算中心正方形区域左上角坐标
        let sq = self.square_size;
        let x_off = (width_full.saturating_sub(sq)) / 2;
        let y_off = (height_full.saturating_sub(sq)) / 2;

        // 4. 拷贝中心正方形到共享缓冲区
        let mut dst = self.buffer.lock().unwrap();
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
            let src_row = &src[src_start..src_start + sq * 4];
            for col in 0..sq {
                let src_idx = col * 4;
                let r = src_row[src_idx];
                let g = src_row[src_idx + 1];
                let b = src_row[src_idx + 2];
                let chw_idx = row * sq + col;
                dst[0 * sq * sq + chw_idx] = r; // R
                dst[1 * sq * sq + chw_idx] = g; // G
                dst[2 * sq * sq + chw_idx] = b; // B
            }
        }

        // 6. 检查停止标志
        if !self.running.load(Ordering::SeqCst) {
            capture_control.stop();
        }
        // 统计帧数
        {
            let mut count = self.frame_count.lock().unwrap();
            *count += 1;
            let mut last = self.last_time.lock().unwrap();
            if last.elapsed().as_secs_f32() >= 1.0 {
                let mut last_fps = self.last_fps.lock().unwrap();
                *last_fps = *count;
                *count = 0;
                *last = Instant::now();
            }
        }
        Ok(())
    }

    fn on_closed(&mut self) -> Result<(), Self::Error> {
        println!("Capture session closed after {}s", self.start.elapsed().as_secs());
        Ok(())
    }
}

/// 对外的封装
pub struct ScreenCapturer {
    buffer: Arc<Mutex<Vec<u8>>>,
    running: Arc<AtomicBool>,
    handle: JoinHandle<()>,
    pub square_size: usize,
    last_fps: Arc<Mutex<u32>>, // 新增
}

impl ScreenCapturer {
    /// 静态方法：创建并启动后台抓取线程
    pub fn start(square_size: usize) -> Result<Self, Box<dyn Error>> {
        // 1. 准备共享数据
        let buf = vec![0u8; square_size * square_size * 3];
        let buffer = Arc::new(Mutex::new(buf));
        let running = Arc::new(AtomicBool::new(true));
        let sq = square_size;
        // let latency = Arc::new(Mutex::new(None)); // 删除延迟
        let frame_count = Arc::new(Mutex::new(0u32));
        let last_fps = Arc::new(Mutex::new(0u32));
        let last_time = Arc::new(Mutex::new(Instant::now()));

        // 2. 构造 capture 设置
        let monitor = Monitor::primary().expect("没有主显示器");
        let settings = Settings::new(
            monitor,
            CursorCaptureSettings::WithoutCursor,
            DrawBorderSettings::WithoutBorder,
            SecondaryWindowSettings::Default,
            MinimumUpdateIntervalSettings::Custom(Duration::from_millis(1)),
            DirtyRegionSettings::Default,
            ColorFormat::Rgba8,
            // 把 running、buffer、sq 打包传给 handler
            (running.clone(), buffer.clone(), sq, frame_count.clone(), last_fps.clone(), last_time.clone()), // 传递latency
        );

        // 3. 启动线程
        let handle = thread::spawn(move || {
            // CaptureHandler::start 会内部轮询 running flag，并不断写入 buffer
            CaptureHandler::start(settings).unwrap();
        });

        println!("屏幕捕获线程已启动");
        Ok(ScreenCapturer { buffer, running, handle, square_size, last_fps })
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

    pub fn fps(&self) -> f32 {
        *self.last_fps.lock().unwrap() as f32
    }
}