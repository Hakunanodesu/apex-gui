use std::{
    error::Error,
    sync::{
        atomic::{AtomicBool, AtomicU64, Ordering},
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

use crate::utils::console_redirect::log_error;

const BASE_HEIGHT: f32 = 1080.0;
const WEAPON_ROI_OFFSET_X: f32 = 377.0;
const WEAPON_ROI_OFFSET_Y: f32 = 122.0;
const WEAPON_ROI_CROP_W: f32 = 159.0;
const WEAPON_ROI_CROP_H: f32 = 38.0;
const WEAPON_ROI_INTERVAL_MS: u64 = 500;

/// Handler 中持有的状态
struct CaptureHandler {
    _start: Instant,
    /// 用于停止信号
    running: Arc<AtomicBool>,
    /// 外部共享的像素缓冲（只存正方形区域）
    buffer: Arc<Mutex<Vec<u8>>>,
    /// 缓冲区版本号，每次更新时递增
    version: Arc<AtomicU64>,
    /// 单次截图耗时（ms）
    capture_latency_ms: Arc<Mutex<f32>>,
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
    /// 右下角武器 ROI：缓冲、版本、是否启用、上次写入时间、裁剪尺寸共享
    buffer2: Arc<Mutex<Vec<u8>>>,
    version2: Arc<AtomicU64>,
    enable_weapon_roi: bool,
    last_weapon_roi_write: Option<Instant>,
    crop_size: Arc<Mutex<(usize, usize)>>,
}

impl GraphicsCaptureApiHandler for CaptureHandler {
    type Flags = (
        Arc<AtomicBool>,
        Arc<Mutex<Vec<u8>>>,
        Arc<AtomicU64>,
        Arc<Mutex<f32>>,
        Arc<Mutex<f32>>,
        Arc<Mutex<u32>>,
        Arc<Mutex<Instant>>,
        usize,
        Arc<AtomicBool>,
        Arc<Mutex<Vec<u8>>>,
        Arc<AtomicU64>,
        bool,
        Arc<Mutex<(usize, usize)>>,
    );
    type Error = Box<dyn Error + Send + Sync>;

    fn new(ctx: Context<Self::Flags>) -> Result<Self, Self::Error> {
        let (
            running, buffer, version, capture_latency_ms, fps, capture_count, last_fps_time,
            square_size, error_flag, buffer2, version2, enable_weapon_roi, crop_size,
        ) = ctx.flags;

        Ok(Self {
            _start: Instant::now(),
            running,
            buffer,
            version,
            capture_latency_ms,
            fps,
            capture_count,
            last_fps_time,
            square_size,
            error_flag,
            buffer2,
            version2,
            enable_weapon_roi,
            last_weapon_roi_write: None,
            crop_size,
        })
    }

    fn on_frame_arrived(
        &mut self,
        frame: &mut Frame,
        capture_control: InternalCaptureControl,
    ) -> Result<(), Self::Error> {
        let start = Instant::now();
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

        // 7b. 右下角武器 ROI（最多每 0.5s 更新一次）
        if self.enable_weapon_roi {
            let should_write = match self.last_weapon_roi_write {
                None => true,
                Some(t) => t.elapsed() >= Duration::from_millis(WEAPON_ROI_INTERVAL_MS),
            };
            if should_write {
                let scale = height_full as f32 / BASE_HEIGHT;
                let x_start = width_full.saturating_sub((WEAPON_ROI_OFFSET_X * scale).round() as usize);
                let y_start = height_full.saturating_sub((WEAPON_ROI_OFFSET_Y * scale).round() as usize);
                let crop_w = (WEAPON_ROI_CROP_W * scale).round() as usize;
                let crop_h = (WEAPON_ROI_CROP_H * scale).round() as usize;
                let crop_w = crop_w.min(width_full.saturating_sub(x_start));
                let crop_h = crop_h.min(height_full.saturating_sub(y_start));
                if crop_w > 0 && crop_h > 0 {
                    let roi_size = crop_w * crop_h * 3;
                    let mut temp_roi = vec![0u8; roi_size];
                    for row in 0..crop_h {
                        let src_row_start = (y_start + row) * stride + x_start * 4;
                        let src_row = &src[src_row_start..src_row_start + crop_w * 4];
                        for col in 0..crop_w {
                            let dst_idx = (row * crop_w + col) * 3;
                            temp_roi[dst_idx] = src_row[col * 4];
                            temp_roi[dst_idx + 1] = src_row[col * 4 + 1];
                            temp_roi[dst_idx + 2] = src_row[col * 4 + 2];
                        }
                    }
                    if let Ok(mut dst2) = self.buffer2.lock() {
                        if dst2.len() != roi_size {
                            dst2.resize(roi_size, 0);
                        }
                        dst2.copy_from_slice(&temp_roi);
                        self.version2.fetch_add(1, Ordering::Relaxed);
                        if let Ok(mut cs) = self.crop_size.lock() {
                            *cs = (crop_w, crop_h);
                        }
                    }
                    self.last_weapon_roi_write = Some(Instant::now());
                }
            }
        }
        
        // 8. 记录单次截图耗时（ms）
        let elapsed_ms = start.elapsed().as_secs_f32() * 1000.0;
        if let Ok(mut guard) = self.capture_latency_ms.lock() {
            *guard = elapsed_ms;
        }

        // 9. 统计 FPS（在锁外进行）
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

        // 10. 检查停止标志
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

/// 单帧捕获 handler，仅获取帧尺寸后立即停止
struct OneShotHandler {
    dimensions: Arc<Mutex<(usize, usize)>>,
}

impl GraphicsCaptureApiHandler for OneShotHandler {
    type Flags = Arc<Mutex<(usize, usize)>>;
    type Error = Box<dyn Error + Send + Sync>;

    fn new(ctx: Context<Self::Flags>) -> Result<Self, Self::Error> {
        Ok(Self { dimensions: ctx.flags })
    }

    fn on_frame_arrived(
        &mut self,
        frame: &mut Frame,
        capture_control: InternalCaptureControl,
    ) -> Result<(), Self::Error> {
        let fb = frame.buffer()?;
        if let Ok(mut dims) = self.dimensions.lock() {
            *dims = (fb.width() as usize, fb.height() as usize);
        }
        capture_control.stop();
        Ok(())
    }

    fn on_closed(&mut self) -> Result<(), Self::Error> {
        Ok(())
    }
}

/// 单帧捕获获取屏幕物理分辨率，与持续捕获使用相同的 windows_capture 路径
pub fn capture_frame_dimensions() -> Result<(usize, usize), Box<dyn Error>> {
    let dimensions = Arc::new(Mutex::new((0usize, 0usize)));
    let monitor = Monitor::primary().expect("没有主显示器");
    let settings = Settings::new(
        monitor,
        CursorCaptureSettings::WithoutCursor,
        DrawBorderSettings::WithoutBorder,
        SecondaryWindowSettings::Default,
        MinimumUpdateIntervalSettings::Default,
        DirtyRegionSettings::Default,
        ColorFormat::Rgba8,
        dimensions.clone(),
    );

    let dims_clone = dimensions.clone();
    let handle = thread::spawn(move || {
        let _ = OneShotHandler::start(settings);
    });
    let _ = handle.join();

    let result = dims_clone.lock().map(|d| *d).unwrap_or((0, 0));
    Ok(result)
}

/// 对外的封装
pub struct ScreenCapturer {
    buffer: Arc<Mutex<Vec<u8>>>,
    version: Arc<AtomicU64>,
    capture_latency_ms: Arc<Mutex<f32>>,
    fps: Arc<Mutex<f32>>,
    running: Arc<AtomicBool>,
    handle: JoinHandle<()>,
    pub square_size: usize,
    error_flag: Arc<AtomicBool>,
    buffer2: Arc<Mutex<Vec<u8>>>,
    version2: Arc<AtomicU64>,
    crop_size: Arc<Mutex<(usize, usize)>>,
}

impl ScreenCapturer {
    /// 静态方法：创建并启动后台抓取线程
    /// `enable_weapon_roi`: 为 true 时每 0.5s 写入右下角武器 ROI 到 buffer2
    pub fn start(square_size: usize, enable_weapon_roi: bool) -> Result<Self, Box<dyn Error>> {
        // 1. 准备共享数据
        let buf = vec![0u8; square_size * square_size * 3];
        let buffer = Arc::new(Mutex::new(buf));
        let version = Arc::new(AtomicU64::new(0));
        let capture_latency_ms = Arc::new(Mutex::new(0.0f32));
        let fps = Arc::new(Mutex::new(0.0f32));
        let capture_count = Arc::new(Mutex::new(0u32));
        let last_fps_time = Arc::new(Mutex::new(Instant::now()));
        let running = Arc::new(AtomicBool::new(true));
        let error_flag = Arc::new(AtomicBool::new(false));
        let sq = square_size;

        let buffer2 = Arc::new(Mutex::new(vec![]));
        let version2 = Arc::new(AtomicU64::new(0));
        let crop_size = Arc::new(Mutex::new((0usize, 0usize)));

        // 2. 构造 capture 设置
        let monitor = Monitor::primary().expect("没有主显示器");
        let settings = Settings::new(
            monitor,
            CursorCaptureSettings::WithoutCursor,
            DrawBorderSettings::WithoutBorder,
            SecondaryWindowSettings::Default,
            MinimumUpdateIntervalSettings::Default,
            DirtyRegionSettings::Default,
            ColorFormat::Rgba8,
            (
                running.clone(),
                buffer.clone(),
                version.clone(),
                capture_latency_ms.clone(),
                fps.clone(),
                capture_count.clone(),
                last_fps_time.clone(),
                sq,
                error_flag.clone(),
                buffer2.clone(),
                version2.clone(),
                enable_weapon_roi,
                crop_size.clone(),
            ),
        );

        // 3. 启动线程
        let error_flag_clone = error_flag.clone();
        let handle = thread::spawn(move || {
            if let Err(e) = CaptureHandler::start(settings) {
                log_error(&format!("屏幕捕获线程启动失败: {:?}", e));
                error_flag_clone.store(true, Ordering::SeqCst);
            }
        });

        Ok(ScreenCapturer {
            buffer,
            version,
            capture_latency_ms,
            fps,
            running,
            handle,
            square_size,
            error_flag,
            buffer2,
            version2,
            crop_size,
        })
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

    /// 获取单次截图耗时（ms）
    pub fn capture_latency_ms(&self) -> Arc<Mutex<f32>> {
        self.capture_latency_ms.clone()
    }

    /// 获取截图帧率（FPS）
    pub fn fps(&self) -> Arc<Mutex<f32>> {
        self.fps.clone()
    }

    /// 获取错误标志
    pub fn error_flag(&self) -> Arc<AtomicBool> {
        self.error_flag.clone()
    }

    /// 右下角武器 ROI 缓冲（HWC RGB），仅当 enable_weapon_roi 时更新
    pub fn buffer2(&self) -> Arc<Mutex<Vec<u8>>> {
        self.buffer2.clone()
    }

    /// 右下角缓冲版本号
    pub fn version2(&self) -> Arc<AtomicU64> {
        self.version2.clone()
    }

    /// 当前右下角裁剪尺寸 (crop_w, crop_h)
    pub fn crop_size(&self) -> Arc<Mutex<(usize, usize)>> {
        self.crop_size.clone()
    }
}
