use std::{
    sync::{
        Arc,
        Mutex,
        atomic::{AtomicBool, AtomicU8, Ordering},
    },
    thread::{self, JoinHandle},
    time::{Duration, Instant},
};
use vigem_client::{Client, Xbox360Wired, XGamepad};
use crate::modules::enemy_det_thread::Detection;
use crate::shared_constants::aim_assist::ASSIST_OUTPUT_EMA_ALPHA;
use crate::shared_constants::assist_curve::{INNER_RAMP_LINEAR, INNER_RAMP_SQUARE};
use crate::shared_constants::error_limits::GAMEPAD_MAPPING_MAX_CONSECUTIVE_ERRORS;
use crate::shared_constants::rapid_fire_mode;
use crate::shared_constants::trigger_timing::TRIGGER_TIMING_UNIT_MS;
use crate::utils::console_redirect::log_error;

// 当右扳机按下时，基于检测结果对右摇杆进行修正。
// 返回 true 表示本帧在识别区内施加了辅助；false 表示无目标（含检测列表无框、或相对 outer 已超出识别区，强度为 0）。
fn apply_right_trigger_adjustment(
    mapped_state: &mut XGamepad,
    d: &Detection,
    outer_size: f32,
    inner_size: f32,
    outer_str: f32,
    inner_str: f32,
    init_str: f32,
    hipfire: f32,
    vertical_str: f32,
    aim_height: f32,
    left_trigger_pressed: bool,
    ema_alpha: f32,
    ema_xy: &mut [f32; 2],
    inner_ramp_mode: &str,
) -> bool {
    let center = outer_size / 2.0;
    let dx = d.x - center;
    let dy = (d.y + (0.5 - aim_height) * d.h) - center;
    let dist = ((dx * dx + dy * dy).sqrt()).min(center);
    let strength = if 
        dist <= inner_size / 2.0
    {
        // inner 区间：t 为到内圈边界的归一化距离；linear / square
        let t = dist / (inner_size / 2.0);
        let progress = if inner_ramp_mode
            .trim()
            .eq_ignore_ascii_case(INNER_RAMP_SQUARE)
        {
            t * t
        } else {
            t
        };
        init_str * (1.0 - progress) + inner_str * progress
    } else if dist <= d.w / 2.0 {
        // 目标框区间：最强平台（原始值）
        inner_str
    } else if 
        dist <= outer_size / 2.0
    {
        // outer区间：弱平台（原始值）
        outer_str
    } else {
        // 超出 outer：已超出识别区域，等价于无目标，强度为 0
        0.0
    };
    if strength <= 0.0 {
        return false;
    }
    let (x, y) = if dist > 0.0 {
        (strength * dx / dist, -vertical_str * strength * dy / dist)
    } else {
        (0.0, 0.0)
    };

    // 根据扳机状态应用不同的乘数
    let multiplier = if left_trigger_pressed { 1.0 } else { hipfire };

    let adjusted_x = x * multiplier;
    let adjusted_y = y * multiplier;

    let a = ema_alpha.clamp(0.0, 1.0);
    let raw = [adjusted_x, adjusted_y];
    for i in 0..2 {
        ema_xy[i] = a * raw[i] + (1.0 - a) * ema_xy[i];
    }

    // 归一化到 -32768~32767 并叠加到右摇杆
    let rx = (ema_xy[0] * 32767.0).clamp(-32768.0, 32767.0) as i16;
    let ry = (ema_xy[1] * 32767.0).clamp(-32768.0, 32767.0) as i16;

    mapped_state.thumb_rx = mapped_state.thumb_rx.saturating_add(rx);
    mapped_state.thumb_ry = mapped_state.thumb_ry.saturating_add(ry);
    true
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
        inner_size: f32,
        outer_str: f32,
        inner_str: f32,
        init_str: f32,
        vertical_str: f32,
        aim_height: f32,
        hipfire: f32,
        assist_ema_alpha_str: Arc<Mutex<String>>,
        aim_enable: Arc<AtomicBool>,
        assist_inner_ramp: Arc<Mutex<String>>, // "linear" / "square"
        rapid_fire_mode: Arc<AtomicU8>, // 0=关闭, 1=始终连点, 2=半按, 3=完全按下连点, 4=根据枪械自动切换
        weapon_rec_result: Option<Arc<Mutex<String>>>, // 枪械识别结果（模板名无后缀）
        rapid_fire_weapons: Vec<String>,               // 连点白名单
        special_weapons_aim_and_fire: Vec<String>,     // 特殊枪械：强制瞄准和开火
        special_weapons_release_to_fire: Vec<String>,  // 特殊枪械：松手开火
    ) -> Self {
        let stop_flag = Arc::new(AtomicBool::new(false));
        let error_flag = Arc::new(AtomicBool::new(false));
        let stop_clone = stop_flag.clone();
        let state_clone = state.clone();
        let det_result_clone = det_result.clone();
        let error_flag_clone = error_flag.clone();
        let vg_clone = virtual_gamepad.clone();
        let rapid_fire_mode_clone = rapid_fire_mode.clone();
        let weapon_rec_result_clone = weapon_rec_result.clone();
        let assist_ema_alpha_str_clone = assist_ema_alpha_str.clone();
        let assist_inner_ramp_clone = assist_inner_ramp.clone();
        let rapid_fire_weapons = rapid_fire_weapons;
        let special_weapons_aim_and_fire = special_weapons_aim_and_fire;
        let special_weapons_release_to_fire = special_weapons_release_to_fire;

        let handle = thread::spawn(move || {
            // 等待读取线程就绪（Xbox XInput / DualShock 4 / DualSense 统一映射到 XGamepad）
            while !ready_flag.load(Ordering::SeqCst) {
                if stop_clone.load(Ordering::SeqCst) {
                    return; // 如果收到停止信号就退出
                }
                thread::sleep(Duration::from_millis(1));
            }
            
            // println!("手柄映射线程已启动，使用0号虚拟手柄");

            let mut consecutive_errors = 0;
            // 连点模式：按下10ms，松开10ms
            let rapid_fire_half_period = Duration::from_millis(TRIGGER_TIMING_UNIT_MS);
            let mut rapid_high: bool = false;
            let mut rapid_last_toggle_at: Instant = Instant::now();
            // 松手开火：记录上一帧右扳机是否按下
            let mut release_prev_pressed: bool = false;
            // 松手开火：松开后维持“按下”状态的截止时间
            let release_pulse_duration = Duration::from_millis(TRIGGER_TIMING_UNIT_MS);
            let mut release_pulse_until: Option<Instant> = None;
            let mut assist_ema_xy: [f32; 2] = [0.0; 2];

            while !stop_clone.load(Ordering::SeqCst) {
                let orig_state = match state_clone.lock() {
                    Ok(guard) => guard.clone(),
                    Err(e) => {
                        log_error(&format!("手柄映射 - 获取手柄状态失败: {:?}", e));
                        consecutive_errors += 1;
                        if consecutive_errors >= GAMEPAD_MAPPING_MAX_CONSECUTIVE_ERRORS {
                            error_flag_clone.store(true, Ordering::SeqCst);
                            break;
                        }
                        thread::sleep(Duration::from_millis(10));
                        continue;
                    }
                }; // 每次都用原始state
                let mut mapped_state = orig_state.clone();
                
                // 本帧武器名只读一次，避免对 weapon_rec_result 重复加锁、重复白名单判断
                let weapon_name_opt: Option<String> = weapon_rec_result_clone
                    .as_ref()
                    .and_then(|arc| arc.lock().ok().map(|g| g.clone()));
                let is_release_weapon = weapon_name_opt
                    .as_ref()
                    .is_some_and(|n| special_weapons_release_to_fire.contains(n));
                let is_aim_override_weapon = weapon_name_opt
                    .as_ref()
                    .is_some_and(|n| special_weapons_aim_and_fire.contains(n));

                // 预先检查左右扳机是否按下
                let right_trigger_pressed = orig_state.right_trigger > 0;
                let left_trigger_pressed = orig_state.left_trigger > 0;

                // 松手开火逻辑：对特殊武器生效
                if is_release_weapon {
                    if right_trigger_pressed {
                        // 按住阶段不触发开火：强制为 0，并清空后续脉冲
                        mapped_state.right_trigger = 0;
                        release_prev_pressed = true;
                        release_pulse_until = None;
                    } else {
                        // 从按下到松开的瞬间：启动一个持续 50ms 的开火脉冲
                        if release_prev_pressed {
                            release_prev_pressed = false;
                            release_pulse_until = Some(Instant::now() + release_pulse_duration);
                        }

                        if release_pulse_until.is_some_and(|until| Instant::now() < until) {
                            mapped_state.right_trigger = 255;
                        } else {
                            mapped_state.right_trigger = 0;
                            release_pulse_until = None;
                        }
                    }
                } else {
                    // 非松手开火武器时重置状态
                    release_prev_pressed = false;
                    release_pulse_until = None;
                }

                // 控制扳机输出
                let rf_mode = rapid_fire_mode_clone.load(Ordering::SeqCst);
                if rf_mode > rapid_fire_mode::DISABLED && right_trigger_pressed && !is_release_weapon {
                    let should_rapid = match rf_mode {
                        rapid_fire_mode::ALWAYS => true, // 始终连点
                        rapid_fire_mode::HALF_TRIGGER => orig_state.right_trigger < 255, // 半按连点，满值时按住
                        rapid_fire_mode::FULL_TRIGGER => orig_state.right_trigger == 255, // 完全按下才连点，否则按住
                        rapid_fire_mode::AUTO_BY_WEAPON => weapon_name_opt.as_ref().is_some_and(|n| {
                            rapid_fire_weapons.contains(n) && !is_release_weapon
                        }),
                        _ => false,
                    };
                    if should_rapid {
                        let now = Instant::now();
                        if now.duration_since(rapid_last_toggle_at) >= rapid_fire_half_period {
                            // 若循环偶尔卡顿，按半周期步进，避免相位漂移
                            let steps = (now.duration_since(rapid_last_toggle_at).as_millis()
                                / rapid_fire_half_period.as_millis().max(1)) as u32;
                            if steps % 2 == 1 {
                                rapid_high = !rapid_high;
                            }
                            rapid_last_toggle_at += rapid_fire_half_period * steps;
                        }
                        mapped_state.right_trigger = if rapid_high { orig_state.right_trigger } else { 0 };
                    } else {
                        rapid_high = false;
                        rapid_last_toggle_at = Instant::now();
                    }
                } else {
                    rapid_high = false;
                    rapid_last_toggle_at = Instant::now();
                }
                
                // 当右扳机按下时总是可参与辅助；aim_enable 或特殊枪械时左扳机按下也可参与
                let aim_enabled = aim_enable.load(Ordering::SeqCst) || is_aim_override_weapon;
                let assist_eligible =
                    right_trigger_pressed || (aim_enabled && left_trigger_pressed);

                // 处理检测结果并计算xy偏移
                let mut assist_applied = false;
                if let Some(ref det_arc) = det_result_clone {
                    match det_arc.lock() {
                        Ok(det_guard) => {
                            if let Some(detections) = &*det_guard {
                                if let Some(d) = detections.first() {
                                    if assist_eligible {
                                        let ema_alpha = assist_ema_alpha_str_clone
                                            .lock()
                                            .ok()
                                            .and_then(|g| g.trim().parse::<f32>().ok())
                                            .map(|a| a.clamp(0.0, 1.0))
                                            .unwrap_or(ASSIST_OUTPUT_EMA_ALPHA);
                                        let ramp_owned = assist_inner_ramp_clone
                                            .lock()
                                            .ok()
                                            .map(|g| g.clone())
                                            .unwrap_or_else(|| INNER_RAMP_LINEAR.to_string());
                                        assist_applied = apply_right_trigger_adjustment(
                                            &mut mapped_state,
                                            d,
                                            outer_size,
                                            inner_size,
                                            outer_str,
                                            inner_str,
                                            init_str,
                                            hipfire,
                                            vertical_str,
                                            aim_height,
                                            left_trigger_pressed,
                                            ema_alpha,
                                            &mut assist_ema_xy,
                                            ramp_owned.as_str(),
                                        );
                                    }
                                }
                            }
                        }
                        Err(e) => {
                            log_error(&format!("手柄映射 - 获取检测结果失败: {:?}", e));
                            consecutive_errors += 1;
                            if consecutive_errors >= GAMEPAD_MAPPING_MAX_CONSECUTIVE_ERRORS {
                                error_flag_clone.store(true, Ordering::SeqCst);
                                break;
                            }
                        }
                    }
                }
                // 无目标（无检测框 / 超出 outer）、松手或不满足辅助条件时清零 EMA 与等效强度状态
                if !assist_applied {
                    assist_ema_xy = [0.0; 2];
                }

                // 更新虚拟手柄状态
                if let Some(ref mut vg) = *vg_clone.lock().unwrap() {
                    if let Err(e) = vg.update(&mapped_state) {
                        log_error(&format!("手柄映射 - ViGEm更新状态失败: {:?}", e));
                        consecutive_errors += 1;
                        if consecutive_errors >= GAMEPAD_MAPPING_MAX_CONSECUTIVE_ERRORS {
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