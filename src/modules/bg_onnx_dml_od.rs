use anyhow::Result;
use ndarray::{Array4, ArrayView2, Axis, Ix2};
use ort::{
    execution_providers::DirectMLExecutionProvider,
    session::{builder::GraphOptimizationLevel, Session},
    value::Tensor,
};
use std::sync::{Arc, Mutex, atomic::{AtomicBool, Ordering}};
use std::thread::{self, JoinHandle};
use std::time::Duration;
use std::path::Path;
use image::{RgbImage, imageops::FilterType};
use serde::{Deserialize, Serialize};

use crate::utils::console_redirect::log_error;

/// 检测配置结构体
#[derive(Debug, Clone, Serialize, Deserialize)]
struct DetectionConfig {
    conf_thres: f32,
    iou_thres: f32,
    classes: String, // JSON中以字符串形式存储，如"0,1,2"或"0"
}

impl Default for DetectionConfig {
    fn default() -> Self {
        Self {
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
            conf_thres: config.conf_thres,
            iou_thres: config.iou_thres,
            classes: config.parse_classes(),
        })
    }

    /// detect 里拿到 raw 输出后，用这三个参数来过滤和 NMS
    pub fn detect(&mut self, buffer: &[u8]) -> Result<(Vec<Detection>, f64)> {
        let start = std::time::Instant::now();
        // resize 到 320x320
        let src_size = self.src_size;
        let mut img = RgbImage::new(src_size as u32, src_size as u32);
        // CHW -> HWC
        for row in 0..src_size {
            for col in 0..src_size {
                let chw_idx = row * src_size + col;
                let r = buffer[0 * src_size * src_size + chw_idx];
                let g = buffer[1 * src_size * src_size + chw_idx];
                let b = buffer[2 * src_size * src_size + chw_idx];
                img.put_pixel(col as u32, row as u32, image::Rgb([r, g, b]));
            }
        }
        let resized = image::imageops::resize(&img, 320, 320, FilterType::Triangle);
        // HWC -> CHW
        let mut chw: Vec<u8> = vec![0; 3 * 320 * 320];
        for row in 0..320 {
            for col in 0..320 {
                let pixel = resized.get_pixel(col, row).0;
                let chw_idx = (row * 320 + col) as usize;
                chw[0 * 320 * 320 + chw_idx] = pixel[0];
                chw[1 * 320 * 320 + chw_idx] = pixel[1];
                chw[2 * 320 * 320 + chw_idx] = pixel[2];
            }
        }
        // 归一化并送入 ONNX
        let array: Array4<f32> = Array4::from_shape_vec(
            (1, 3, 320, 320),
            chw.iter().map(|&b| b as f32 / 255.0).collect(),
        )?;

        // 2. 转成 Ort Tensor
        let input_tensor: Tensor<f32> = Tensor::from_array(array)?;

        // 3. 创建 I/O binding —— 这里就需要 &mut self.session
        let mut binding = self.session.create_binding()?;
        binding.bind_input(&self.input_name, &input_tensor)?;
        binding.bind_output_to_device(
            &self.output_name,
            &self.session.allocator().memory_info(),
        )?;

        // 4. 运行推理
        let mut outputs = self.session.run_binding(&mut binding)?;
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
                let scale = self.src_size as f32 / 320.0;
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

        let elapsed = start.elapsed();
        let ms = elapsed.as_secs_f64() * 1000.0;
        // println!("detect 耗时: {:.3} ms", ms);
        Ok((keep, ms))
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
    fps: Arc<Mutex<f32>>,
    error_flag: Arc<AtomicBool>,
}

impl DetectorThread {
    /// 启动检测线程
    pub fn start(
        buffer: Arc<Mutex<Vec<u8>>>,
        model_path: &Path,
    ) -> Result<Self> {
        let stop_flag = Arc::new(AtomicBool::new(false));
        let result = Arc::new(Mutex::new(None));
        let fps = Arc::new(Mutex::new(0.0));
        let error_flag = Arc::new(AtomicBool::new(false));

        let stop_flag_clone = stop_flag.clone();
        let result_clone = result.clone();
        let fps_clone = fps.clone();
        let error_flag_clone = error_flag.clone();

        // 创建检测器
        let detector = match OnnxDetector::new(
            320, // 固定输入尺寸
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
            let mut infer_count = 0;
            let mut last_time = std::time::Instant::now();
            let mut last_buffer: Vec<u8> = Vec::new();
            let mut consecutive_errors = 0;
            const MAX_CONSECUTIVE_ERRORS: u32 = 10;
            
            while !stop_flag_clone.load(Ordering::SeqCst) {
                // 1. 拿到最新一帧
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
                
                let slice: &[u8] = &buf_guard[..];
                // 2. 只有 buffer 变化且时间间隔大于8ms才推理
                if slice != last_buffer.as_slice() {
                    let last_infer_time = std::time::Instant::now(); // 推理开始前就更新时间
                    match detector.detect(slice) {
                        Ok((detections, _ms)) => {
                            match result_clone.lock() {
                                Ok(mut res) => {
                                    *res = Some(detections);
                                    consecutive_errors = 0; // 重置错误计数
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
                    last_buffer.clear();
                    last_buffer.extend_from_slice(slice);
                    infer_count += 1;
                    let elapsed = last_infer_time.elapsed();
                    if elapsed < Duration::from_millis(8) {
                        thread::sleep(Duration::from_millis(8) - elapsed);
                    }
                }
                if last_time.elapsed().as_secs_f32() >= 1.0 {
                    match fps_clone.lock() {
                        Ok(mut fps_guard) => {
                            *fps_guard = infer_count as f32;
                        }
                        Err(e) => {
                            log_error(&format!("检测线程 - 设置FPS失败: {:?}", e));
                        }
                    }
                    infer_count = 0;
                    last_time = std::time::Instant::now();
                }
                drop(buf_guard);
            }
        });

        // println!("智慧核心线程已启动");
        Ok(Self { stop_flag, handle: Some(handle), result, fps, error_flag })
    }

    /// 获取最新推理结果
    pub fn result(&self) -> Arc<Mutex<Option<Vec<Detection>>>> {
        self.result.clone()
    }

    /// 获取推理采样率（fps）
    pub fn fps(&self) -> Arc<Mutex<f32>> {
        self.fps.clone()
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
