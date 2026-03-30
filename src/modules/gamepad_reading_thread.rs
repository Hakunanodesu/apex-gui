use std::{
    sync::{
        Arc,
        Mutex,
        atomic::{AtomicBool, Ordering},
    },
    thread::{self, JoinHandle},
    time::Duration,
};
use sdl2::{
    controller::{Axis as ControllerAxis, Button as ControllerButton},
    event::{Event, EventType},
};
use vigem_client::{XGamepad, XButtons};

use crate::shared_constants::error_limits::GAMEPAD_READING_MAX_CONSECUTIVE_ERRORS;
use crate::utils::console_redirect::log_error;

static DEBUG_PRINT_ENABLED: AtomicBool = AtomicBool::new(false);
static DEBUG_TEXT: Mutex<String> = Mutex::new(String::new());

pub fn set_debug_print_enabled(enabled: bool) {
    DEBUG_PRINT_ENABLED.store(enabled, Ordering::Relaxed);
}

pub fn get_debug_text() -> String {
    DEBUG_TEXT.lock().map(|s| s.clone()).unwrap_or_default()
}

fn scale_to_u8(v: i16) -> u8 {
    (((v as i32 + 32768) * 255 / 65535) as u8).clamp(0, 255)
}

fn scale_trigger_to_u8(v: i16) -> u8 {
    if v >= 0 {
        ((v as i32) * 255 / 32767).clamp(0, 255) as u8
    } else {
        scale_to_u8(v)
    }
}

fn controller_button_bit(button: ControllerButton) -> Option<u16> {
    match button {
        ControllerButton::A => Some(XButtons::A),
        ControllerButton::B => Some(XButtons::B),
        ControllerButton::X => Some(XButtons::X),
        ControllerButton::Y => Some(XButtons::Y),
        ControllerButton::Back => Some(XButtons::BACK),
        ControllerButton::Start => Some(XButtons::START),
        ControllerButton::LeftStick => Some(XButtons::LTHUMB),
        ControllerButton::RightStick => Some(XButtons::RTHUMB),
        ControllerButton::LeftShoulder => Some(XButtons::LB),
        ControllerButton::RightShoulder => Some(XButtons::RB),
        ControllerButton::DPadUp => Some(XButtons::UP),
        ControllerButton::DPadDown => Some(XButtons::DOWN),
        ControllerButton::DPadLeft => Some(XButtons::LEFT),
        ControllerButton::DPadRight => Some(XButtons::RIGHT),
        _ => None,
    }
}

#[derive(Default, Clone, Copy)]
struct DebugState {
    lx: i16,
    ly: i16,
    rx: i16,
    ry: i16,
    lt: u8,
    rt: u8,
    a: bool,
    b: bool,
    x: bool,
    y: bool,
    back: bool,
    guide: bool,
    start: bool,
    ls: bool,
    rs: bool,
    lb: bool,
    rb: bool,
    up: bool,
    down: bool,
    left: bool,
    right: bool,
    misc1: bool,
}

thread_local! {
    static DEBUG_STATE: std::cell::RefCell<DebugState> = std::cell::RefCell::new(DebugState::default());
}

fn set_debug_button(state: &mut DebugState, button: ControllerButton, pressed: bool) {
    match button {
        ControllerButton::A => state.a = pressed,
        ControllerButton::B => state.b = pressed,
        ControllerButton::X => state.x = pressed,
        ControllerButton::Y => state.y = pressed,
        ControllerButton::Back => state.back = pressed,
        ControllerButton::Guide => state.guide = pressed,
        ControllerButton::Start => state.start = pressed,
        ControllerButton::LeftStick => state.ls = pressed,
        ControllerButton::RightStick => state.rs = pressed,
        ControllerButton::LeftShoulder => state.lb = pressed,
        ControllerButton::RightShoulder => state.rb = pressed,
        ControllerButton::DPadUp => state.up = pressed,
        ControllerButton::DPadDown => state.down = pressed,
        ControllerButton::DPadLeft => state.left = pressed,
        ControllerButton::DPadRight => state.right = pressed,
        ControllerButton::Misc1 => state.misc1 = pressed,
        _ => {}
    }
}

fn debug_pressed_buttons_list(state: &DebugState) -> String {
    let mut names = Vec::new();
    if state.a { names.push("A"); }
    if state.b { names.push("B"); }
    if state.x { names.push("X"); }
    if state.y { names.push("Y"); }
    if state.lb { names.push("LB"); }
    if state.rb { names.push("RB"); }
    if state.ls { names.push("LS"); }
    if state.rs { names.push("RS"); }
    if state.back { names.push("Back"); }
    if state.guide { names.push("Guide"); }
    if state.start { names.push("Start"); }
    if state.up { names.push("Up"); }
    if state.down { names.push("Down"); }
    if state.left { names.push("Left"); }
    if state.right { names.push("Right"); }
    if state.misc1 { names.push("Misc1"); }
    if names.is_empty() {
        "(none)".to_string()
    } else {
        names.join(", ")
    }
}

fn debug_print_state() {
    if !DEBUG_PRINT_ENABLED.load(Ordering::Relaxed) {
        return;
    }
    DEBUG_STATE.with(|state_cell| {
            let state = state_cell.borrow();
            let mut s = String::new();
            use std::fmt::Write;

            let _ = writeln!(s, "LX:{:+06}  LY:{:+06}", state.lx, state.ly);
            let _ = writeln!(s, "RX:{:+06}  RY:{:+06}", state.rx, state.ry);
            let _ = writeln!(s, "LT:{:>3}   RT:{:>3}", state.lt, state.rt);
            let _ = writeln!(s, "Pressed: {}", debug_pressed_buttons_list(&state));

            if let Ok(mut lock) = DEBUG_TEXT.lock() {
                *lock = s;
            }
    });
}

fn apply_event(state: &mut XGamepad, evt: &ControllerEvent) {
    let debug_only = DEBUG_PRINT_ENABLED.load(Ordering::Relaxed);

    match evt {
        ControllerEvent::Axis { axis, value } => {
            if debug_only {
                DEBUG_STATE.with(|cell| {
                    let mut dbg = cell.borrow_mut();
                    match axis {
                        ControllerAxis::LeftX => dbg.lx = *value,
                        ControllerAxis::LeftY => dbg.ly = value.saturating_neg(),
                        ControllerAxis::RightX => dbg.rx = *value,
                        ControllerAxis::RightY => dbg.ry = value.saturating_neg(),
                        ControllerAxis::TriggerLeft => dbg.lt = scale_trigger_to_u8(*value),
                        ControllerAxis::TriggerRight => dbg.rt = scale_trigger_to_u8(*value),
                    }
                });
                return;
            }
            match axis {
                ControllerAxis::LeftX => state.thumb_lx = *value,
                ControllerAxis::LeftY => state.thumb_ly = value.saturating_neg(),
                ControllerAxis::RightX => state.thumb_rx = *value,
                ControllerAxis::RightY => state.thumb_ry = value.saturating_neg(),
                ControllerAxis::TriggerLeft => state.left_trigger = scale_trigger_to_u8(*value),
                ControllerAxis::TriggerRight => state.right_trigger = scale_trigger_to_u8(*value),
            }
        }
        ControllerEvent::ButtonDown { button } => {
            if debug_only {
                DEBUG_STATE.with(|cell| {
                    set_debug_button(&mut cell.borrow_mut(), *button, true);
                });
                return;
            }
            if let Some(bit) = controller_button_bit(*button) {
                *state.buttons.as_mut() |= bit;
            }
        }
        ControllerEvent::ButtonUp { button } => {
            if debug_only {
                DEBUG_STATE.with(|cell| {
                    set_debug_button(&mut cell.borrow_mut(), *button, false);
                });
                return;
            }
            if let Some(bit) = controller_button_bit(*button) {
                *state.buttons.as_mut() &= !bit;
            }
        }
    }
}

fn matches_or_bind_active_device(active_which: &mut Option<u32>, which: u32) -> bool {
    match *active_which {
        Some(active) => which == active,
        None => {
            *active_which = Some(which);
            true
        }
    }
}

#[derive(Debug)]
pub enum ControllerEvent {
    Axis { axis: ControllerAxis, value: i16 },
    ButtonDown { button: ControllerButton },
    ButtonUp { button: ControllerButton },
}

pub struct ConReader {
    stop_flag: Arc<AtomicBool>,
    state: Arc<Mutex<XGamepad>>,
    ready_flag: Arc<AtomicBool>,
    handle: JoinHandle<()>,
    error_flag: Arc<AtomicBool>,
}

impl ConReader {
    /// 启动线程，返回一个实例
    pub fn start(preferred_device_index: Option<u32>) -> Self {
        let stop_flag = Arc::new(AtomicBool::new(false));
        let state = Arc::new(Mutex::new(XGamepad::default()));
        let ready_flag = Arc::new(AtomicBool::new(false));
        let error_flag = Arc::new(AtomicBool::new(false));
        let stop_clone = stop_flag.clone();
        let state_clone = state.clone();
        let ready_clone = ready_flag.clone();
        let error_flag_clone = error_flag.clone();

        let handle = thread::spawn(move || {
            let sdl_ctx = match sdl2::init() {
                Ok(ctx) => ctx,
                Err(e) => {
                    log_error(&format!("手柄读取 - SDL初始化失败: {}", e));
                    error_flag_clone.store(true, Ordering::SeqCst);
                    return;
                }
            };
            
            let gc_sub = match sdl_ctx.game_controller() {
                Ok(sub) => sub,
                Err(e) => {
                    log_error(&format!("手柄读取 - SDL GameController 子系统初始化失败: {}", e));
                    error_flag_clone.store(true, Ordering::SeqCst);
                    return;
                }
            };
            
            let mut pump = match sdl_ctx.event_pump() {
                Ok(pump) => pump,
                Err(e) => {
                    log_error(&format!("手柄读取 - SDL事件泵初始化失败: {}", e));
                    error_flag_clone.store(true, Ordering::SeqCst);
                    return;
                }
            };

            // 获取设备数量（按 SDL joystick 索引遍历 game controller）
            let js_count = match gc_sub.num_joysticks() {
                Ok(count) => count,
                Err(e) => {
                    log_error(&format!("手柄读取 - 获取手柄数量失败: {}", e));
                    error_flag_clone.store(true, Ordering::SeqCst);
                    return;
                }
            };
            
            // 打开 GameController 设备。
            // 若提供了首选设备索引，则仅打开该设备；否则打开全部并在运行时自动锁定首个产生输入的设备。
            let mut controllers = Vec::new();
            let mut active_which: Option<u32> = None;
            if let Some(preferred_idx) = preferred_device_index {
                if preferred_idx >= js_count {
                    log_error(&format!(
                        "手柄读取 - 首选手柄索引{}超出范围(当前设备数={})",
                        preferred_idx, js_count
                    ));
                    error_flag_clone.store(true, Ordering::SeqCst);
                    return;
                }
                if !gc_sub.is_game_controller(preferred_idx) {
                    log_error(&format!(
                        "手柄读取 - 首选设备索引{}不是 SDL GameController 设备",
                        preferred_idx
                    ));
                    error_flag_clone.store(true, Ordering::SeqCst);
                    return;
                }
                match gc_sub.open(preferred_idx) {
                    Ok(controller) => {
                        active_which = Some(controller.instance_id());
                        controllers.push(controller);
                    }
                    Err(e) => {
                        log_error(&format!("手柄读取 - 打开首选手柄索引{}失败: {}", preferred_idx, e));
                        error_flag_clone.store(true, Ordering::SeqCst);
                        return;
                    }
                }
            } else {
                for dev_idx in 0..js_count {
                    if !gc_sub.is_game_controller(dev_idx) {
                        continue;
                    }
                    match gc_sub.open(dev_idx) {
                        Ok(controller) => {
                            controllers.push(controller);
                        }
                        Err(e) => {
                            log_error(&format!("手柄读取 - 打开手柄索引{}失败: {}", dev_idx, e));
                            // 单个手柄打开失败不影响其他手柄
                        }
                    }
                }
            }
            
            if controllers.is_empty() {
                log_error("手柄读取 - 没有可用的 GameController 设备");
                error_flag_clone.store(true, Ordering::SeqCst);
                return;
            }

            // 标记就绪
            ready_clone.store(true, Ordering::SeqCst);
            // println!("SDL 事件读取线程已启动");

            // 启用事件
            for ev in &[
                EventType::ControllerAxisMotion,
                EventType::ControllerButtonDown,
                EventType::ControllerButtonUp,
            ] {
                let _ = pump.enable_event(*ev);
            }

            let mut consecutive_errors = 0;
            // 循环处理
            while !stop_clone.load(Ordering::SeqCst) {
                let mut had_controller_event = false;
                for evt in pump.poll_iter() {
                    // 过滤手柄事件
                    match evt {
                        Event::ControllerAxisMotion { .. }
                        | Event::ControllerButtonDown { .. }
                        | Event::ControllerButtonUp { .. } => {},
                        _ => continue,
                    };

                    // 分发事件到状态
                    match state_clone.lock() {
                        Ok(mut lock) => {
                            match evt {
                                Event::ControllerAxisMotion { which, axis, value, .. } => {
                                    if !matches_or_bind_active_device(&mut active_which, which) {
                                        continue;
                                    }
                                    apply_event(&mut *lock, &ControllerEvent::Axis { axis, value });
                                    had_controller_event = true;
                                }
                                Event::ControllerButtonDown { which, button, .. } => {
                                    if !matches_or_bind_active_device(&mut active_which, which) {
                                        continue;
                                    }
                                    apply_event(&mut *lock, &ControllerEvent::ButtonDown { button });
                                    had_controller_event = true;
                                }
                                Event::ControllerButtonUp { which, button, .. } => {
                                    if !matches_or_bind_active_device(&mut active_which, which) {
                                        continue;
                                    }
                                    apply_event(&mut *lock, &ControllerEvent::ButtonUp { button });
                                    had_controller_event = true;
                                }
                                _ => {}
                            }
                            consecutive_errors = 0; // 重置错误计数
                        }
                        Err(e) => {
                            log_error(&format!("手柄读取 - 获取状态锁失败: {:?}", e));
                            consecutive_errors += 1;
                            if consecutive_errors >= GAMEPAD_READING_MAX_CONSECUTIVE_ERRORS {
                                log_error(&format!(
                                    "手柄读取 - 连续错误超过{}次，设置错误标志",
                                    GAMEPAD_READING_MAX_CONSECUTIVE_ERRORS
                                ));
                                error_flag_clone.store(true, Ordering::SeqCst);
                                break;
                            }
                        }
                    }
                }
                if had_controller_event {
                    debug_print_state();
                }
                thread::sleep(Duration::from_millis(1));
            }
        });

        ConReader { stop_flag, state, ready_flag, handle, error_flag }
    }

    /// 停止线程并等待 join 完成
    pub fn stop(self) {
        self.stop_flag.store(true, Ordering::SeqCst);
        // join 时会阻塞直到线程退出
        let _ = self.handle.join();
    }

    /// 获取共享的手柄状态
    pub fn state(&self) -> Arc<Mutex<XGamepad>> {
        self.state.clone()
    }

    /// 获取 ready_flag，用于映射线程等待
    pub fn ready(&self) -> Arc<AtomicBool> {
        self.ready_flag.clone()
    }

    /// 获取错误标志
    pub fn error_flag(&self) -> Arc<AtomicBool> {
        self.error_flag.clone()
    }
}