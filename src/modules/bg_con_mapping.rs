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

// 新增：当右扳机按下时，基于检测结果对右摇杆进行修正
fn apply_right_trigger_adjustment(
    mapped_state: &mut XGamepad,
    d: &Detection,
    outer_size: f32,
    mid_size: f32,
    inner_size: f32,
    outer_str: f32,
    mid_str: f32,
    inner_str: f32,
    deadzone: f32,
    hipfire: f32,
    reverse_coef: f32,
    aim_height: f32,
    left_trigger_pressed: bool,
) {
    let center = outer_size / 2.0;
    let dx = d.x - center;
    let dy = (d.y + (0.5 - aim_height) * d.h) - center;
    let dist = ((dx * dx + dy * dy).sqrt()).min(center);
    let strength = if dist <= inner_size / 2.0 {
        // inner区间，线性递减
        let t = if inner_size > 0.0 { dist / (inner_size / 2.0) } else { 1.0 };
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
    let (x, y) = if dist > 0.0 {
        (strength * dx / dist, -strength * dy / dist)
    } else {
        (0.0, 0.0)
    };

    // 根据扳机状态应用不同的乘数
    let multiplier = if left_trigger_pressed { 1.0 } else { hipfire };

    let adjusted_x = x * multiplier;
    let adjusted_y = y * multiplier;

    // 归一化到 -32768~32767 并叠加到右摇杆
    let rx = (adjusted_x * 32767.0).clamp(-32768.0, 32767.0) as i16;
    let ry = (adjusted_y * 32767.0).clamp(-32768.0, 32767.0) as i16;

    // 检查叠加后是否改变方向，如果改变则应用反向系数
    let new_rx = mapped_state.thumb_rx.saturating_add(rx);
    let new_ry = mapped_state.thumb_ry.saturating_add(ry);

    if (mapped_state.thumb_rx > 0 && new_rx < 0) || (mapped_state.thumb_rx < 0 && new_rx > 0) {
        mapped_state.thumb_rx = (reverse_coef * new_rx as f32) as i16;
    } else {
        mapped_state.thumb_rx = new_rx;
    }

    if (mapped_state.thumb_ry > 0 && new_ry < 0) || (mapped_state.thumb_ry < 0 && new_ry > 0) {
        mapped_state.thumb_ry = (reverse_coef * new_ry as f32) as i16;
    } else {
        mapped_state.thumb_ry = new_ry;
    }
}

pub struct ConMapper {
    stop_flag: Arc<AtomicBool>,
    handle: JoinHandle<()>,
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
        reverse_coef: f32, // 新增反向系数参数
        aim_height: f32,  // 新增瞄准高度参数（暂未使用）
    ) -> Self {
        let stop_flag = Arc::new(AtomicBool::new(false));
        let stop_clone = stop_flag.clone();
        let state_clone = state.clone();
        let client_clone = client.clone();
        let det_result_clone = det_result.clone();

        let handle = thread::spawn(move || {
            // 等待 SDL 读取线程就绪
            while !ready_flag.load(Ordering::SeqCst) {
                thread::sleep(Duration::from_millis(1));
            }
            // println!("ViGEm 映射线程已启动");

            let id = TargetId::XBOX360_WIRED;
            let mut tgt = Xbox360Wired::new(client_clone, id);
            tgt.plugin().expect("plugin failed");
            tgt.wait_ready().expect("wait_ready failed");

            while !stop_clone.load(Ordering::SeqCst) {
                let orig_state = state_clone.lock().unwrap().clone(); // 每次都用原始state
                let mut mapped_state = orig_state.clone();
                
                // 处理检测结果并计算xy偏移
                if let Some(ref det_arc) = det_result_clone {
                    if let Ok(det_guard) = det_arc.lock() {
                        // 检查左右扳机是否按下
                        let right_trigger_pressed = orig_state.right_trigger > 0;
                        let left_trigger_pressed = orig_state.left_trigger > 0;
                        
                        if let Some(detections) = &*det_guard {
                            if let Some(d) = detections.first() {
                                // 只有当右扳机按下时才计算并应用结果
                                if right_trigger_pressed {
                                    // 应用映射（1-254和255都应用）
                                    apply_right_trigger_adjustment(
                                        &mut mapped_state,
                                        d,
                                        outer_size,
                                        mid_size,
                                        inner_size,
                                        outer_str,
                                        mid_str,
                                        inner_str,
                                        deadzone,
                                        hipfire,
                                        reverse_coef,
                                        aim_height,
                                        left_trigger_pressed,
                                    );
                                    
                                    // 控制扳机输出：只有达到255时才输出扳机值，否则输出0
                                    if orig_state.right_trigger < 255 {
                                        mapped_state.right_trigger = 0;
                                    }
                                }
                            }
                        }
                    }
                }
                
                tgt.update(&mapped_state).ok();
                thread::sleep(Duration::from_millis(1));
            }

            tgt.unplug().ok();
        });

        ConMapper { stop_flag, handle }
    }

    /// 停止映射线程并 join
    pub fn stop(self) {
        self.stop_flag.store(true, Ordering::SeqCst);
        let _ = self.handle.join();
    }
}