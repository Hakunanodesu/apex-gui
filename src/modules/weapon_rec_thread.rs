//! 枪械识别线程：从右下角 ROI 做 Canny 后与模板 bitmask 匹配，返回最相似模板名（无后缀）
//! 模板图片在编译时通过 build.rs 嵌入二进制，无需运行时 gun_template 目录。

include!("../gun_templates.rs");

use std::sync::atomic::{AtomicBool, AtomicU64, Ordering};
use std::sync::{Arc, Mutex};
use std::thread::{self, JoinHandle};
use std::time::{Duration, Instant};

use anyhow::Result;
use fast_image_resize as fir;
use fast_image_resize::images::Image;
use fast_image_resize::{PixelType, ResizeOptions, Resizer};
use image::imageops::FilterType;
use image::{GrayImage, ImageBuffer, Luma};
use imageproc::edges::canny;
use std::num::NonZeroU32;

use crate::utils::console_redirect::log_error;

pub const TARGET_W: u32 = 159;
pub const TARGET_H: u32 = 38;
const CANNY_LOW: f32 = 50.0;
const CANNY_HIGH: f32 = 150.0;
const EDGE_THRESHOLD: u8 = 128;
/// 所有武器模板相似度都低于此阈值时判定为空手
const EMPTY_HAND_SIMILARITY_THRESHOLD: f32 = 0.4;
const EMPTY_HAND_STR: &str = "空手";

/// 从编译时嵌入的 TEMPLATE_FILES 解码出 (文件名无后缀, 灰度图, 边缘像素数)
fn load_embedded_templates() -> Vec<(String, GrayImage, u32)> {
    let mut out = Vec::new();
    for (name, bytes) in TEMPLATE_FILES.iter() {
        let img = match image::load_from_memory(bytes) {
            Ok(i) => i,
            Err(e) => {
                log_error(&format!("解码嵌入模板 {} 失败: {}", name, e));
                continue;
            }
        };
        let luma = img.to_luma8();
        let (w, h) = (luma.width(), luma.height());
        let resized = if w == TARGET_W && h == TARGET_H {
            luma
        } else {
            image::imageops::resize(&luma, TARGET_W, TARGET_H, FilterType::Nearest)
        };
        let edge_count = resized
            .pixels()
            .filter(|p| p[0] >= EDGE_THRESHOLD)
            .count() as u32;
        out.push((name.to_string(), resized, edge_count));
    }
    out
}

/// 将 HWC RGB Vec<u8> 转为 image GrayImage
fn rgb_to_gray(rgb: &[u8], w: usize, h: usize) -> Option<GrayImage> {
    if rgb.len() < w * h * 3 {
        return None;
    }
    let mut buf = ImageBuffer::<Luma<u8>, Vec<u8>>::new(w as u32, h as u32);
    for y in 0..h {
        for x in 0..w {
            let i = (y * w + x) * 3;
            let r = rgb[i] as f32;
            let g = rgb[i + 1] as f32;
            let b = rgb[i + 2] as f32;
            let luma = (0.299 * r + 0.587 * g + 0.114 * b).round() as u8;
            buf.put_pixel(x as u32, y as u32, Luma([luma]));
        }
    }
    Some(buf)
}

/// 使用 fast_image_resize 最近邻缩放到 159×38
fn resize_to_target(rgb: &[u8], src_w: usize, src_h: usize) -> Result<Vec<u8>> {
    let src_width = NonZeroU32::new(src_w as u32).ok_or_else(|| anyhow::anyhow!("width 0"))?;
    let src_height = NonZeroU32::new(src_h as u32).ok_or_else(|| anyhow::anyhow!("height 0"))?;
    let _dst_width = NonZeroU32::new(TARGET_W).ok_or_else(|| anyhow::anyhow!("dst width 0"))?;
    let _dst_height = NonZeroU32::new(TARGET_H).ok_or_else(|| anyhow::anyhow!("dst height 0"))?;

    let src_image = Image::from_vec_u8(src_width.get(), src_height.get(), rgb.to_vec(), PixelType::U8x3)
        .map_err(|e| anyhow::anyhow!("from_vec_u8: {:?}", e))?;
    let mut dst_image = Image::new(TARGET_W, TARGET_H, PixelType::U8x3);

    let mut resizer = Resizer::new();
    let options = ResizeOptions::new().resize_alg(fir::ResizeAlg::Nearest);
    resizer
        .resize(&src_image, &mut dst_image, &options)
        .map_err(|e| anyhow::anyhow!("resize: {:?}", e))?;

    Ok(dst_image.buffer().to_vec())
}

/// ROI 灰度图 min-max 线性拉伸到 0–255，提升亮场景下的边缘对比度
fn linear_stretch_contrast(gray: &GrayImage) -> GrayImage {
    let (min_val, max_val) = gray
        .pixels()
        .fold((255u8, 0u8), |(min_v, max_v), p| (min_v.min(p[0]), max_v.max(p[0])));
    if max_val <= min_val {
        return gray.clone();
    }
    let range = (max_val - min_val) as f32;
    let mut out = GrayImage::new(gray.width(), gray.height());
    for y in 0..gray.height() {
        for x in 0..gray.width() {
            let v = gray.get_pixel(x, y)[0];
            let stretched = ((v - min_val) as f32 / range * 255.0).round().clamp(0.0, 255.0) as u8;
            out.put_pixel(x, y, Luma([stretched]));
        }
    }
    out
}

/// 计算相似度：匹配的边缘像素数 / 模板边缘像素总数
fn similarity(live: &GrayImage, template: &GrayImage, template_edge_count: u32) -> f32 {
    if template_edge_count == 0 {
        return 0.0;
    }
    let mut match_count = 0u32;
    for y in 0..TARGET_H {
        for x in 0..TARGET_W {
            let tx = x as u32;
            let ty = y as u32;
            let t_val = template.get_pixel(tx, ty)[0];
            let l_val = live.get_pixel(tx, ty)[0];
            if t_val >= EDGE_THRESHOLD && l_val >= EDGE_THRESHOLD {
                match_count += 1;
            }
        }
    }
    match_count as f32 / template_edge_count as f32
}

pub struct WeaponRecThread {
    stop_flag: Arc<AtomicBool>,
    handle: Option<JoinHandle<()>>,
    result: Arc<Mutex<String>>,
    match_latency_ms: Arc<Mutex<f32>>,
    /// 最近一帧与最佳模板的相似度 [0, 1]
    best_similarity: Arc<Mutex<f32>>,
    /// 最近一帧的 live canny 图（右下角 ROI 做 Canny 后的灰度），用于预览。尺寸 TARGET_W×TARGET_H。
    canny_pixels: Arc<Mutex<Option<Vec<u8>>>>,
    error_flag: Arc<AtomicBool>,
}

impl WeaponRecThread {
    /// 启动枪械识别线程。模板在编译时已嵌入二进制，常驻内存。
    pub fn start(
        buffer2: Arc<Mutex<Vec<u8>>>,
        version2: Arc<AtomicU64>,
        crop_size: Arc<Mutex<(usize, usize)>>,
    ) -> Result<Self> {
        let templates = load_embedded_templates();
        if templates.is_empty() {
            log_error("未嵌入任何武器模板，枪械识别将始终返回空");
        }

        let stop_flag = Arc::new(AtomicBool::new(false));
        let result = Arc::new(Mutex::new(String::new()));
        let match_latency_ms = Arc::new(Mutex::new(0.0f32));
        let best_similarity = Arc::new(Mutex::new(0.0f32));
        let canny_pixels = Arc::new(Mutex::new(None));
        let error_flag = Arc::new(AtomicBool::new(false));

        let stop_clone = stop_flag.clone();
        let result_clone = result.clone();
        let match_latency_ms_clone = match_latency_ms.clone();
        let best_similarity_clone = best_similarity.clone();
        let canny_pixels_clone = canny_pixels.clone();
        let _error_flag_clone = error_flag.clone();

        let handle = thread::spawn(move || {
            let mut last_version = 0u64;
            while !stop_clone.load(Ordering::SeqCst) {
                let match_start = Instant::now();
                let current_version = version2.load(Ordering::Acquire);
                if current_version == last_version {
                    thread::sleep(Duration::from_millis(10));
                    continue;
                }
                let (roi_copy, crop_w, crop_h) = {
                    let buf = match buffer2.lock() {
                        Ok(g) => g,
                        Err(_) => {
                            thread::sleep(Duration::from_millis(5));
                            continue;
                        }
                    };
                    let (cw, ch) = match crop_size.lock() {
                        Ok(g) => *g,
                        Err(_) => {
                            thread::sleep(Duration::from_millis(5));
                            continue;
                        }
                    };
                    if buf.is_empty() || cw == 0 || ch == 0 {
                        last_version = current_version;
                        continue;
                    }
                    (buf.clone(), cw, ch)
                };
                last_version = current_version;

                let resized = match resize_to_target(&roi_copy, crop_w, crop_h) {
                    Ok(r) => r,
                    Err(e) => {
                        log_error(&format!("武器 ROI 缩放失败: {}", e));
                        continue;
                    }
                };
                let gray = match rgb_to_gray(&resized, TARGET_W as usize, TARGET_H as usize) {
                    Some(g) => g,
                    None => continue,
                };
                let gray = linear_stretch_contrast(&gray);
                let live_canny = canny(&gray, CANNY_LOW, CANNY_HIGH);
                if let Ok(mut guard) = canny_pixels_clone.lock() {
                    *guard = Some(live_canny.as_raw().to_vec());
                }

                let (best_name, best_sim) = templates
                    .iter()
                    .map(|t| (t.0.clone(), similarity(&live_canny, &t.1, t.2)))
                    .max_by(|a, b| a.1.partial_cmp(&b.1).unwrap_or(std::cmp::Ordering::Equal))
                    .unwrap_or((String::new(), 0.0));

                let result_str = if best_sim < EMPTY_HAND_SIMILARITY_THRESHOLD {
                    EMPTY_HAND_STR.to_string()
                } else {
                    best_name
                };

                if let Ok(mut res) = result_clone.lock() {
                    *res = result_str;
                }
                if let Ok(mut guard) = best_similarity_clone.lock() {
                    *guard = best_sim;
                }
                let elapsed_ms = match_start.elapsed().as_secs_f32() * 1000.0;
                if let Ok(mut guard) = match_latency_ms_clone.lock() {
                    *guard = elapsed_ms;
                }
            }
        });

        Ok(Self {
            stop_flag,
            handle: Some(handle),
            result,
            match_latency_ms,
            best_similarity,
            canny_pixels,
            error_flag,
        })
    }

    pub fn result(&self) -> Arc<Mutex<String>> {
        self.result.clone()
    }

    /// 最近一帧的 live canny 图像素（TARGET_W×TARGET_H 灰度），用于推理预览窗口。
    pub fn canny_pixels(&self) -> Arc<Mutex<Option<Vec<u8>>>> {
        self.canny_pixels.clone()
    }

    /// 单次武器匹配耗时（ms）
    pub fn match_latency_ms(&self) -> Arc<Mutex<f32>> {
        self.match_latency_ms.clone()
    }

    /// 最近一帧与最佳模板的相似度 [0, 1]
    pub fn best_similarity(&self) -> Arc<Mutex<f32>> {
        self.best_similarity.clone()
    }

    pub fn error_flag(&self) -> Arc<AtomicBool> {
        self.error_flag.clone()
    }

    pub fn stop(mut self) {
        self.stop_flag.store(true, Ordering::SeqCst);
        if let Some(h) = self.handle.take() {
            let _ = h.join();
        }
    }
}
