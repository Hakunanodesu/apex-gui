use std::{
    sync::{
        Arc,
        Mutex,
        atomic::{AtomicBool, Ordering},
    },
    thread::{self, JoinHandle},
    time::Duration,
};
use vigem_client::{Client, TargetId, Xbox360Wired, XGamepad};
use crate::modules::bg_onnx_dml_od::Detection;

pub struct ConMapper {
    stop_flag: Arc<AtomicBool>,
    handle: JoinHandle<()>,
    hz: Arc<Mutex<u32>>, // 新增采样率字段
}

impl ConMapper {
    /// 启动映射线程
    pub fn start(
        state: Arc<Mutex<XGamepad>>,
        client: Arc<Client>,
        ready_flag: Arc<AtomicBool>,
        det_result: Option<Arc<Mutex<Option<Vec<Detection>>>>>,
        outer_size: f32,
        mid_size: f32,
        inner_size: f32,
        outer_str: f32,
        mid_str: f32,
        inner_str: f32,
        deadzone: f32,
        hipfire: f32,
    ) -> Self {
        let stop_flag = Arc::new(AtomicBool::new(false));
        let hz = Arc::new(Mutex::new(0u32)); // 新增
        let stop_clone = stop_flag.clone();
        let state_clone = state.clone();
        let client_clone = client.clone();
        let det_result_clone = det_result.clone();
        let hz_clone = hz.clone(); // 新增

        let handle = thread::spawn(move || {
            // 等待 SDL 读取线程就绪
            while !ready_flag.load(Ordering::SeqCst) {
                thread::sleep(Duration::from_millis(1));
            }
            println!("ViGEm 映射线程已启动");

            let id = TargetId::XBOX360_WIRED;
            let mut tgt = Xbox360Wired::new(client_clone, id);
            tgt.plugin().expect("plugin failed");
            tgt.wait_ready().expect("wait_ready failed");

            let mut map_count = 0;
            let mut last_time = std::time::Instant::now();
            let mut prev_state = XGamepad::default(); // 新增：记录上一次状态
            while !stop_clone.load(Ordering::SeqCst) {
                let orig_state = state_clone.lock().unwrap().clone(); // 每次都用原始state
                let mut mapped_state = orig_state.clone();
                
                // 处理检测结果并计算xy偏移
                if let Some(ref det_arc) = det_result_clone {
                    if let Ok(det_guard) = det_arc.lock() {
                        // 检查左右扳机是否按下（阈值设为128，即50%）
                        let right_trigger_pressed = orig_state.right_trigger > 128;
                        let left_trigger_pressed = orig_state.left_trigger > 128;
                        
                        if let Some(detections) = &*det_guard {
                            if let Some(d) = detections.first() {
                                // 只有当右扳机按下时才计算并应用结果
                                if right_trigger_pressed {
                                    let center = outer_size / 2.0;
                                    let dx = d.x - center;
                                    let dy = d.y - center;
                                    let dist = ((dx * dx + dy * dy).sqrt()).min(center);
                                    let strength = if dist <= inner_size / 2.0 {
                                        // inner区间，线性递减
                                        let t = dist / (inner_size / 2.0);
                                        deadzone * (1.0 - t) + inner_str * t
                                    } else if dist <= mid_size / 2.0 {
                                        // mid区间
                                        mid_str
                                    } else if dist <= outer_size / 2.0 {
                                        // outer区间
                                        outer_str
                                    } else {
                                        // 超出outer区间
                                        0.0
                                    };
                                    let x = strength * dx / dist;
                                    let y = -strength * dy / dist;
                                    
                                    // 根据扳机状态应用不同的乘数
                                    let multiplier = if left_trigger_pressed {
                                        // 左右扳机同时按下，保持当前输出
                                        1.0
                                    } else {
                                        // 只有右扳机按下，应用hipfire乘数
                                        hipfire
                                    };
                                    
                                    let adjusted_x = x * multiplier;
                                    let adjusted_y = y * multiplier;
                                    
                                    // 归一化到 -32768~32767 并叠加到右摇杆
                                    let rx = (adjusted_x * 32767.0).clamp(-32768.0, 32767.0) as i16;
                                    let ry = (adjusted_y * 32767.0).clamp(-32768.0, 32767.0) as i16;
                                    mapped_state.thumb_rx = mapped_state.thumb_rx.saturating_add(rx);
                                    mapped_state.thumb_ry = mapped_state.thumb_ry.saturating_add(ry);
                                    // println!("[ConMapper] x: {x:.3}, y: {y:.3}, thumb_rx: {}, thumb_ry: {}", mapped_state.thumb_rx, mapped_state.thumb_ry);
                                }
                            }
                        }
                    }
                }
                
                tgt.update(&mapped_state).ok();
                // 只有状态发生变化才计数
                if mapped_state != prev_state {
                    map_count += 1;
                    prev_state = mapped_state.clone();
                }
                if last_time.elapsed().as_secs_f32() >= 1.0 {
                    let mut hz_guard = hz_clone.lock().unwrap();
                    *hz_guard = map_count;
                    map_count = 0;
                    last_time = std::time::Instant::now();
                }
                thread::sleep(Duration::from_millis(1));
            }

            tgt.unplug().ok();
        });

        ConMapper { stop_flag, handle, hz }
    }

    /// 停止映射线程并 join
    pub fn stop(self) {
        self.stop_flag.store(true, Ordering::SeqCst);
        let _ = self.handle.join();
    }

    /// 获取采样率fps
    pub fn hz(&self) -> Arc<Mutex<u32>> {
        self.hz.clone()
    }
}