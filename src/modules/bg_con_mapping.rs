use std::{
    sync::{
        Arc,
        Mutex,
        atomic::{AtomicBool, Ordering},
    },
    thread::{self, JoinHandle},
    time::Duration,
};
use vigem_client::{Client, Xbox360Wired, XGamepad};
use crate::modules::bg_onnx_dml_od::Detection;
use crate::utils::console_redirect::log_error;

// 新增：当右扳机按下时，基于检测结果对右摇杆进行修正
fn apply_right_trigger_adjustment(
    mapped_state: &mut XGamepad,
    d: &Detection,
    outer_size: f32,
    mid_size: f32,
    inner_size: f32,
    outer_str: f32,
    inner_str: f32,
    init_str: f32,
    hipfire: f32,
    vertical_str: f32,
    aim_height: f32,
    left_trigger_pressed: bool,
) {
    let center = outer_size / 2.0;
    let dx = d.x - center;
    let dy = (d.y + (0.5 - aim_height) * d.h) - center;
    let dist = ((dx * dx + dy * dy).sqrt()).min(center);
    let strength = if 
        dx.abs() <= inner_size / 2.0 && dy.abs() <= inner_size / 2.0
    {
        // inner区间，线性递减
        let t = if inner_size > 0.0 { dist / (inner_size / 2.0) } else { 1.0 };
        let temp = init_str * (1.0 - t) + inner_str * t;
        temp * temp
    } else if 
        (dx.abs() <= mid_size / 2.0 && dy.abs() <= mid_size / 2.0)
        || (dx.abs() <= d.w / 2.0 && dy.abs() <= d.h / 2.0)
    {
        // outer区间
        inner_str * inner_str
    } else if 
        dx.abs() <= outer_size / 2.0 && dy.abs() <= outer_size / 2.0
    {
        // outer区间
        outer_str * outer_str
    } else {
        // 超出outer区间
        0.0
    };
    let (x, y) = if dist > 0.0 {
        (strength * dx / dist, -vertical_str * strength * dy / dist)
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

    mapped_state.thumb_rx = mapped_state.thumb_rx.saturating_add(rx);
    mapped_state.thumb_ry = mapped_state.thumb_ry.saturating_add(ry);
    // println!("rx: {}, ry: {}", rx, ry);
}

pub struct ConMapper {
    stop_flag: Arc<AtomicBool>,
    handle: JoinHandle<()>,
    error_flag: Arc<AtomicBool>,
}

impl ConMapper {
    /// 启动映射线程，使用已创建的虚拟手柄（通过引用）
    pub fn start(
        state: Arc<Mutex<XGamepad>>,
        virtual_gamepad: Arc<Mutex<Option<Xbox360Wired<Arc<Client>>>>>,
        ready_flag: Arc<AtomicBool>,
        det_result: Option<Arc<Mutex<Option<Vec<Detection>>>>>,
        outer_size: f32,
        mid_size: f32,
        inner_size: f32,
        outer_str: f32,
        inner_str: f32,
        init_str: f32,
        vertical_str: f32, // 新增垂直强度参数
        aim_height: f32,  // 新增瞄准高度参数
        hipfire: f32,
        aim_enable: Arc<AtomicBool>, // 新增瞄准辅助开关
    ) -> Self {
        let stop_flag = Arc::new(AtomicBool::new(false));
        let error_flag = Arc::new(AtomicBool::new(false));
        let stop_clone = stop_flag.clone();
        let state_clone = state.clone();
        let det_result_clone = det_result.clone();
        let error_flag_clone = error_flag.clone();
        let vg_clone = virtual_gamepad.clone();

        let handle = thread::spawn(move || {
            // 等待 SDL 读取线程就绪
            while !ready_flag.load(Ordering::SeqCst) {
                if stop_clone.load(Ordering::SeqCst) {
                    return; // 如果收到停止信号就退出
                }
                thread::sleep(Duration::from_millis(1));
            }
            
            // println!("手柄映射线程已启动，使用0号虚拟手柄");

            let mut consecutive_errors = 0;
            const MAX_CONSECUTIVE_ERRORS: u32 = 50;

            while !stop_clone.load(Ordering::SeqCst) {
                let orig_state = match state_clone.lock() {
                    Ok(guard) => guard.clone(),
                    Err(e) => {
                        log_error(&format!("手柄映射 - 获取手柄状态失败: {:?}", e));
                        consecutive_errors += 1;
                        if consecutive_errors >= MAX_CONSECUTIVE_ERRORS {
                            error_flag_clone.store(true, Ordering::SeqCst);
                            break;
                        }
                        thread::sleep(Duration::from_millis(10));
                        continue;
                    }
                }; // 每次都用原始state
                let mut mapped_state = orig_state.clone();
                
                // 控制扳机输出：只有达到250时才输出扳机值，否则输出0
                if orig_state.right_trigger < 250 {
                    mapped_state.right_trigger = 0;
                }
                
                // 处理检测结果并计算xy偏移
                if let Some(ref det_arc) = det_result_clone {
                    match det_arc.lock() {
                        Ok(det_guard) => {
                            // 检查左右扳机是否按下
                            let right_trigger_pressed = orig_state.right_trigger > 10;
                            let left_trigger_pressed = orig_state.left_trigger > 10;
                            
                            if let Some(detections) = &*det_guard {
                                if let Some(d) = detections.first() {
                                    // 当右扳机按下时总是计算并应用结果
                                    // 当aim_enable为true且左扳机按下时也计算并应用结果
                                    if right_trigger_pressed || (aim_enable.load(Ordering::SeqCst) && left_trigger_pressed) {
                                        // 应用映射（10-254和255都应用）
                                        apply_right_trigger_adjustment(
                                            &mut mapped_state,
                                            d,
                                            outer_size,
                                            mid_size,
                                            inner_size,
                                            outer_str,
                                            inner_str,
                                            init_str,
                                            hipfire,
                                            vertical_str,
                                            aim_height,
                                            left_trigger_pressed,
                                        );
                                    }
                                }
                            }
                        }
                        Err(e) => {
                            log_error(&format!("手柄映射 - 获取检测结果失败: {:?}", e));
                            consecutive_errors += 1;
                            if consecutive_errors >= MAX_CONSECUTIVE_ERRORS {
                                error_flag_clone.store(true, Ordering::SeqCst);
                                break;
                            }
                        }
                    }
                }
                
                // 更新虚拟手柄状态
                if let Some(ref mut vg) = *vg_clone.lock().unwrap() {
                    if let Err(e) = vg.update(&mapped_state) {
                        log_error(&format!("手柄映射 - ViGEm更新状态失败: {:?}", e));
                        consecutive_errors += 1;
                        if consecutive_errors >= MAX_CONSECUTIVE_ERRORS {
                            error_flag_clone.store(true, Ordering::SeqCst);
                            break;
                        }
                    } else {
                        consecutive_errors = 0; // 重置错误计数
                    }
                }
                
                thread::sleep(Duration::from_millis(1));
            }

            // 注意：不在这里 unplug 虚拟手柄，它应该在整个手柄模式期间保持存在
            // 虚拟手柄的生命周期由 main.rs 管理
        });

        ConMapper { stop_flag, handle, error_flag }
    }

    /// 停止映射线程并 join
    pub fn stop(self) {
        self.stop_flag.store(true, Ordering::SeqCst);
        let _ = self.handle.join();
    }

    /// 获取错误标志
    pub fn error_flag(&self) -> Arc<AtomicBool> {
        self.error_flag.clone()
    }
}