use anyhow::Result;
use ndarray::{Array4, ArrayView2, Axis, Ix2};
use ort::{
    execution_providers::DirectMLExecutionProvider,
    session::{builder::GraphOptimizationLevel, Session},
    value::Tensor,
};
use std::sync::{Arc, Mutex, atomic::{AtomicBool, AtomicU64, Ordering}};
use std::thread::{self, JoinHandle};
use std::time::Duration;
use std::path::Path;
use serde::{Deserialize, Serialize};
use fast_image_resize as fir;
use fast_image_resize::images::Image;
use fast_image_resize::{PixelType, ResizeOptions, Resizer};
use std::num::NonZeroU32;

use crate::utils::console_redirect::log_error;

/// 检测配置结构体
#[derive(Debug, Clone, Serialize, Deserialize)]
struct DetectionConfig {
    size: usize,        // 新增：推理尺寸
    conf_thres: f32,
    iou_thres: f32,
    classes: String, // JSON中以字符串形式存储，如"0,1,2"或"0"
}

impl Default for DetectionConfig {
    fn default() -> Self {
        Self {
            size: 320,      // 默认推理尺寸
            conf_thres: 0.4,
            iou_thres: 0.9,
            classes: "0".to_string(),
        }
    }
}

impl DetectionConfig {
    /// 将classes字符串解析为Vec<usize>
    fn parse_classes(&self) -> Vec<usize> {
        self.classes
            .split(',')
            .filter_map(|s| s.trim().parse().ok())
            .collect()
    }
}

/// 从模型路径读取同名的JSON配置文件
fn load_detection_config(model_path: &Path) -> DetectionConfig {
    let config_path = model_path.with_extension("json");
    
    match std::fs::read_to_string(&config_path) {
        Ok(content) => {
            match serde_json::from_str::<DetectionConfig>(&content) {
                Ok(config) => {
                    // println!("从 {:?} 加载配置: {:?}", config_path, config);
                    config
                }
                Err(e) => {
                    log_error(&format!("解析配置文件失败 {:?}: {:?}，使用默认配置", config_path, e));
                    DetectionConfig::default()
                }
            }
        }
        Err(e) => {
            log_error(&format!("读取配置文件失败 {:?}: {:?}，使用默认配置", config_path, e));
            DetectionConfig::default()
        }
    }
}

/// 检测结果结构体，仅供本文件内部和线程推理用
#[derive(Clone, Debug)]
pub struct Detection {
    pub x: f32,
    pub y: f32,
    pub w: f32,
    pub h: f32,
    pub score: f32,
}

/// 封装 ONNX 推理的结构体，仅供DetectorThread内部使用
struct OnnxDetector {
    session: Session,
    input_name: String,
    output_name: String,
    src_size: usize,
    inference_size: usize,  // 新增：推理尺寸
    conf_thres: f32,
    iou_thres: f32,
    classes: Vec<usize>,
}

impl OnnxDetector {
    /// start 时从同名JSON文件读取配置参数
    pub fn new(src_size: usize, model_path: &Path) -> Result<Self> {
        // 从同名JSON文件加载配置
        let config = load_detection_config(model_path);
        
        // 从文件加载模型
        let session = Session::builder()?
            .with_execution_providers([DirectMLExecutionProvider::default().build()])?
            .with_optimization_level(GraphOptimizationLevel::Level3)?
            .with_intra_threads(num_cpus::get_physical() as usize)?
            .commit_from_file(model_path)?;

        let input_name  = session.inputs[0].name.clone();
        let output_name = session.outputs[0].name.clone();

        Ok(Self {
            session,
            input_name,
            output_name,
            src_size,
            inference_size: config.size,  // 使用配置中的推理尺寸
            conf_thres: config.conf_thres,
            iou_thres: config.iou_thres,
            classes: config.parse_classes(),
        })
    }

    /// detect 里拿到 raw 输出后，用这三个参数来过滤和 NMS
    /// 返回值依次为：检测结果，预处理耗时（ms），推理耗时（ms）
    pub fn detect(&mut self, buffer: &[u8]) -> Result<(Vec<Detection>, f32, f32)> {
        // 预处理：从 CHW u8 buffer 使用 fast_image_resize 做最近邻缩放，再归一化为 NCHW f32
        let preprocess_start = std::time::Instant::now();
        let src_size = self.src_size;
        let inference_size = self.inference_size;

        let src_width = NonZeroU32::new(src_size as u32).unwrap();
        let src_height = NonZeroU32::new(src_size as u32).unwrap();
        let dst_width = NonZeroU32::new(inference_size as u32).unwrap();
        let dst_height = NonZeroU32::new(inference_size as u32).unwrap();

        let src_w_u32 = src_width.get();
        let src_h_u32 = src_height.get();

        // 1. CHW u8 -> HWC u8 (RGB packed)，供 fast_image_resize 使用
        let mut src_rgb: Vec<u8> = vec![0; (src_w_u32 * src_h_u32 * 3) as usize];
        for row in 0..src_size {
            for col in 0..src_size {
                let chw_idx = row * src_size + col;
                let r = buffer[0 * src_size * src_size + chw_idx];
                let g = buffer[1 * src_size * src_size + chw_idx];
                let b = buffer[2 * src_size * src_size + chw_idx];
                let rgb_idx = (row * src_size + col) * 3;
                src_rgb[rgb_idx] = r;
                src_rgb[rgb_idx + 1] = g;
                src_rgb[rgb_idx + 2] = b;
            }
        }

        let src_image = Image::from_vec_u8(
            src_width.get(),
            src_height.get(),
            src_rgb,
            PixelType::U8x3,
        ).expect("创建 fast_image_resize 源图失败");

        let mut dst_image = Image::new(
            dst_width.get(),
            dst_height.get(),
            PixelType::U8x3,
        );

        let mut resizer = Resizer::new();
        let options = ResizeOptions::new().resize_alg(fir::ResizeAlg::Nearest);
        resizer
            .resize(&src_image, &mut dst_image, &options)
            .map_err(|e| anyhow::anyhow!("fast_image_resize 失败: {:?}", e))?;

        let dst_buf = dst_image.buffer().to_vec();

        // 2. HWC u8 (RGB packed) -> NCHW f32（归一化到 [0,1]）
        let mut data: Vec<f32> = vec![0.0; 3 * inference_size * inference_size];
        for row in 0..inference_size {
            for col in 0..inference_size {
                let rgb_idx = (row * inference_size + col) * 3;
                let r_u8 = dst_buf[rgb_idx];
                let g_u8 = dst_buf[rgb_idx + 1];
                let b_u8 = dst_buf[rgb_idx + 2];

                let r = r_u8 as f32 / 255.0;
                let g = g_u8 as f32 / 255.0;
                let b = b_u8 as f32 / 255.0;

                let dst = row * inference_size + col;
                data[0 * inference_size * inference_size + dst] = r;
                data[1 * inference_size * inference_size + dst] = g;
                data[2 * inference_size * inference_size + dst] = b;
            }
        }

        let array: Array4<f32> = Array4::from_shape_vec(
            (1, 3, inference_size, inference_size),
            data,
        )?;

        let input_tensor: Tensor<f32> = Tensor::from_array(array)?;
        let preprocess_ms = preprocess_start.elapsed().as_secs_f32() * 1000.0;

        // 3. 创建 I/O binding —— 这里就需要 &mut self.session
        let mut binding = self.session.create_binding()?;
        binding.bind_input(&self.input_name, &input_tensor)?;
        binding.bind_output_to_device(
            &self.output_name,
            &self.session.allocator().memory_info(),
        )?;

        // 4. 运行推理
        let infer_start = std::time::Instant::now();
        let mut outputs = self.session.run_binding(&mut binding)?;
        let infer_ms = infer_start.elapsed().as_secs_f32() * 1000.0;
        let dv = outputs
            .remove(&self.output_name)
            .expect("模型输出缺失");
        let tensor: Tensor<f32> = dv.downcast()?;
        let array_d = tensor.extract_array();
        let raw: ArrayView2<f32> = array_d.index_axis(Axis(0), 0).into_dimensionality::<Ix2>()?;

        // 1. 先筛置信度
        let mut cand: Vec<Detection> = raw
            .rows()
            .into_iter()
            .filter_map(|row| {
                let score = row[4];
                if score < self.conf_thres {
                    return None;
                }
                let cls = row[5] as usize;
                if !self.classes.is_empty() && !self.classes.contains(&cls) {
                    return None;
                }
                // 还原到原始尺寸
                let scale = self.src_size as f32 / inference_size as f32;
                Some(Detection {
                    x: row[0] * scale,
                    y: row[1] * scale,
                    w: row[2] * scale,
                    h: row[3] * scale,
                    score,
                })
            })
            .collect();

        // 2. 对 cand 做 NMS
        cand.sort_by(|a, b| b.score.partial_cmp(&a.score).unwrap());
        let mut keep = Vec::new();
        while !cand.is_empty() {
            let top = cand.remove(0); // 取第一个（分数最高）
            keep.push(top.clone());
            cand.retain(|d| iou(&top, d) < self.iou_thres);
        }

        Ok((keep, preprocess_ms, infer_ms))
    }
}

// 私有IoU计算，仅供NMS用
fn iou(a: &Detection, b: &Detection) -> f32 {
    let x = a.x.max(b.x);
    let y = a.y.max(b.y);
    let w = a.w.min(b.w);
    let h = a.h.min(b.h);
    let inter = (w - x).max(0.0) * (h - y).max(0.0);
    let area_a = (a.w - a.x) * (a.h - a.y);
    let area_b = (b.w - b.x) * (b.h - b.y);
    inter / (area_a + area_b - inter)
}

/// 检测线程封装
pub struct DetectorThread {
    stop_flag: Arc<AtomicBool>,
    handle: Option<JoinHandle<()>>,
    result: Arc<Mutex<Option<Vec<Detection>>>>,
    /// 单次推理耗时（ms）
    infer_latency_ms: Arc<Mutex<f32>>,
    /// 单次截图到输入模型 resize 耗时（ms）
    preprocess_latency_ms: Arc<Mutex<f32>>,
    error_flag: Arc<AtomicBool>,
}

impl DetectorThread {
    /// 启动检测线程
    pub fn start(
        buffer: Arc<Mutex<Vec<u8>>>,
        version: Arc<AtomicU64>,
        model_path: &Path,
    ) -> Result<Self> {
        let stop_flag = Arc::new(AtomicBool::new(false));
        let result = Arc::new(Mutex::new(None));
        let infer_latency_ms = Arc::new(Mutex::new(0.0f32));
        let preprocess_latency_ms = Arc::new(Mutex::new(0.0f32));
        let error_flag = Arc::new(AtomicBool::new(false));

        let stop_flag_clone = stop_flag.clone();
        let result_clone = result.clone();
        let infer_latency_ms_clone = infer_latency_ms.clone();
        let preprocess_latency_ms_clone = preprocess_latency_ms.clone();
        let error_flag_clone = error_flag.clone();
        let version_clone = version.clone();

        // 从缓冲区大小计算实际的输入尺寸
        let src_size = {
            let buf = buffer.lock().unwrap();
            let total_size = buf.len();
            // CHW格式，所以总大小是 size * size * 3
            let size = ((total_size as f64) / 3.0).sqrt() as usize;
            size
        };

        // 创建检测器
        let detector = match OnnxDetector::new(
            src_size, // 使用实际的输入尺寸
            model_path,
        ) {
            Ok(detector) => detector,
            Err(e) => {
                log_error(&format!("创建检测器失败: {:?}", e));
                error_flag.store(true, Ordering::SeqCst);
                return Err(e);
            }
        };

        let handle = thread::spawn(move || {
            let mut detector = detector;
            let mut last_version: u64 = 0;
            let mut consecutive_errors = 0;
            const MAX_CONSECUTIVE_ERRORS: u32 = 10;
            
            while !stop_flag_clone.load(Ordering::SeqCst) {
                // 1. 先检查版本号，避免不必要的锁和克隆
                let current_version = version_clone.load(Ordering::Acquire);
                
                // 2. 只有版本号变化时才克隆缓冲区
                if current_version != last_version {
                    let current_buffer = {
                        let buf_guard = match buffer.lock() {
                            Ok(guard) => guard,
                            Err(e) => {
                                log_error(&format!("检测线程 - 获取缓冲区锁失败: {:?}", e));
                                consecutive_errors += 1;
                                if consecutive_errors >= MAX_CONSECUTIVE_ERRORS {
                                    error_flag_clone.store(true, Ordering::SeqCst);
                                    break;
                                }
                                thread::sleep(Duration::from_millis(10));
                                continue;
                            }
                        };
                        // 立即克隆数据并释放锁
                        buf_guard.clone()
                    };
                    
                    // 3. 进行推理
                    match detector.detect(&current_buffer) {
                        Ok((detections, preprocess_ms, infer_ms)) => {
                            match result_clone.lock() {
                                Ok(mut res) => {
                                    *res = Some(detections);
                                    // 更新延迟统计
                                    if let Ok(mut guard) = preprocess_latency_ms_clone.lock() {
                                        *guard = preprocess_ms;
                                    }
                                    if let Ok(mut guard) = infer_latency_ms_clone.lock() {
                                        *guard = infer_ms;
                                    }
                                    consecutive_errors = 0;
                                }
                                Err(e) => {
                                    log_error(&format!("检测线程 - 设置结果失败: {:?}", e));
                                    consecutive_errors += 1;
                                    if consecutive_errors >= MAX_CONSECUTIVE_ERRORS {
                                        error_flag_clone.store(true, Ordering::SeqCst);
                                        break;
                                    }
                                }
                            }
                        }
                        Err(e) => {
                            log_error(&format!("推理失败: {:?}", e));
                            consecutive_errors += 1;
                            if consecutive_errors >= MAX_CONSECUTIVE_ERRORS {
                                log_error(&format!("连续推理失败超过{}次，设置错误标志", MAX_CONSECUTIVE_ERRORS));
                                error_flag_clone.store(true, Ordering::SeqCst);
                                break;
                            }
                        }
                    }
                    last_version = current_version; // 更新版本号
                }
                thread::sleep(Duration::from_millis(1));
            }
        });

        // println!("智慧核心线程已启动");
        Ok(Self { stop_flag, handle: Some(handle), result, infer_latency_ms, preprocess_latency_ms, error_flag })
    }

    /// 获取最新推理结果
    pub fn result(&self) -> Arc<Mutex<Option<Vec<Detection>>>> {
        self.result.clone()
    }

    /// 获取单次推理耗时（ms）
    pub fn infer_latency_ms(&self) -> Arc<Mutex<f32>> {
        self.infer_latency_ms.clone()
    }

    /// 获取单次截图到输入模型 resize 耗时（ms）
    pub fn preprocess_latency_ms(&self) -> Arc<Mutex<f32>> {
        self.preprocess_latency_ms.clone()
    }

    /// 获取错误标志
    pub fn error_flag(&self) -> Arc<AtomicBool> {
        self.error_flag.clone()
    }

    /// 停止线程
    pub fn stop(mut self) {
        self.stop_flag.store(true, Ordering::SeqCst);
        if let Some(handle) = self.handle.take() {
            let _ = handle.join();
        }
    }
}
