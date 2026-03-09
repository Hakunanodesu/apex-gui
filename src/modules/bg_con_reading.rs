use std::{
    cell::RefCell,
    sync::{
        Arc,
        Mutex,
        atomic::{AtomicBool, Ordering},
    },
    thread::{self, JoinHandle},
    time::Duration,
};
use sdl2::{
    event::{Event, EventType},
    joystick::HatState,
};
use vigem_client::{XGamepad, XButtons};

use crate::utils::console_redirect::log_error;
use crate::utils::ConMapping;

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

fn map_button(idx: u8, m: &ConMapping) -> Option<u16> {
    let b = &m.button;
    if b.a == Some(idx) { return Some(XButtons::A); }
    if b.b == Some(idx) { return Some(XButtons::B); }
    if b.x == Some(idx) { return Some(XButtons::X); }
    if b.y == Some(idx) { return Some(XButtons::Y); }
    if b.lb == Some(idx) { return Some(XButtons::LB); }
    if b.rb == Some(idx) { return Some(XButtons::RB); }
    if b.back == Some(idx) { return Some(XButtons::BACK); }
    if b.start == Some(idx) { return Some(XButtons::START); }
    if b.ls == Some(idx) { return Some(XButtons::LTHUMB); }
    if b.rs == Some(idx) { return Some(XButtons::RTHUMB); }
    None
}

fn map_axis(state: &mut XGamepad, axis_idx: u8, value: i16, m: &ConMapping) {
    let a = &m.axis;
    if a.lx == Some(axis_idx) { state.thumb_lx = value; }
    if a.ly == Some(axis_idx) { state.thumb_ly = value.saturating_neg(); }
    if a.rx == Some(axis_idx) { state.thumb_rx = value; }
    if a.ry == Some(axis_idx) { state.thumb_ry = value.saturating_neg(); }
    if a.lt == Some(axis_idx) { state.left_trigger = scale_to_u8(value); }
    if a.rt == Some(axis_idx) { state.right_trigger = scale_to_u8(value); }
}

// 仅用于调试打印，记录 0~5 号轴的最近一次值（线程本地，无需互斥）
thread_local! {
    static AXIS_VALUES: RefCell<[i16; 6]> = RefCell::new([0; 6]);
    // 记录 0~12 号按钮的最近一次状态（0/1）
    static BUTTON_VALUES: RefCell<[u8; 13]> = RefCell::new([0; 13]);
}

fn debug_print_axes_and_buttons() {
    if !DEBUG_PRINT_ENABLED.load(Ordering::Relaxed) {
        return;
    }
    AXIS_VALUES.with(|axis_cell| {
        BUTTON_VALUES.with(|btn_cell| {
            let axes = axis_cell.borrow();
            let btns = btn_cell.borrow();

            let mut s = String::new();
            use std::fmt::Write;

            let _ = writeln!(s, "axis_idx:");
            let _ = writeln!(s, "[0]->{:+06}", axes[0]);
            let _ = writeln!(s, "[1]->{:+06}", axes[1]);
            let _ = writeln!(s, "[2]->{:+06}", axes[2]);
            let _ = writeln!(s, "[3]->{:+06}", axes[3]);
            let _ = writeln!(s, "[4]->{:+06}", axes[4]);
            let _ = writeln!(s, "[5]->{:+06}", axes[5]);
            let _ = writeln!(s, "button_idx:");
            let _ = writeln!(
                s,
                "[0]->{}, [1]->{}, [2]->{}, [3]->{}, [4]->{}, [5]->{}",
                btns[0], btns[1], btns[2], btns[3], btns[4], btns[5]
            );
            let _ = writeln!(
                s,
                "[6]->{}, [7]->{}, [8]->{}, [9]->{}, [10]->{}, [11]->{}",
                btns[6], btns[7], btns[8], btns[9], btns[10], btns[11]
            );

            if let Ok(mut lock) = DEBUG_TEXT.lock() {
                *lock = s;
            }
        });
    });
}

fn apply_event(state: &mut XGamepad, evt: &JoystickEvent, mapping: &ConMapping) {
    let debug_only = DEBUG_PRINT_ENABLED.load(Ordering::Relaxed);

    match evt {
        JoystickEvent::Axis { axis_idx, value } => {
            if debug_only {
                AXIS_VALUES.with(|cell| {
                    let mut arr = cell.borrow_mut();
                    if (*axis_idx as usize) < 6 {
                        arr[*axis_idx as usize] = *value;
                    }
                });
                return;
            }
            map_axis(state, *axis_idx, *value, mapping);
        }
        JoystickEvent::ButtonDown { button_idx } => {
            if debug_only {
                BUTTON_VALUES.with(|cell| {
                    let mut arr = cell.borrow_mut();
                    if (*button_idx as usize) < 13 {
                        arr[*button_idx as usize] = 1;
                    }
                });
                return;
            }
            if let Some(bit) = map_button(*button_idx, mapping) {
                *state.buttons.as_mut() |= bit;
            }
        }
        JoystickEvent::ButtonUp { button_idx } => {
            if debug_only {
                BUTTON_VALUES.with(|cell| {
                    let mut arr = cell.borrow_mut();
                    if (*button_idx as usize) < 13 {
                        arr[*button_idx as usize] = 0;
                    }
                });
                return;
            }
            if let Some(bit) = map_button(*button_idx, mapping) {
                *state.buttons.as_mut() &= !bit;
            }
        }
        JoystickEvent::HatMotion { state: hat } => {
            if debug_only {
                return;
            }
            let dpad_mask = XButtons::UP | XButtons::DOWN | XButtons::LEFT | XButtons::RIGHT;
            *state.buttons.as_mut() &= !dpad_mask;
            match hat {
                HatState::Centered => {}
                HatState::Up => *state.buttons.as_mut() |= XButtons::UP,
                HatState::RightUp => { *state.buttons.as_mut() |= XButtons::UP; *state.buttons.as_mut() |= XButtons::RIGHT; }
                HatState::Right => *state.buttons.as_mut() |= XButtons::RIGHT,
                HatState::RightDown => { *state.buttons.as_mut() |= XButtons::RIGHT; *state.buttons.as_mut() |= XButtons::DOWN; }
                HatState::Down => *state.buttons.as_mut() |= XButtons::DOWN,
                HatState::LeftDown => { *state.buttons.as_mut() |= XButtons::DOWN; *state.buttons.as_mut() |= XButtons::LEFT; }
                HatState::Left => *state.buttons.as_mut() |= XButtons::LEFT,
                HatState::LeftUp => { *state.buttons.as_mut() |= XButtons::LEFT; *state.buttons.as_mut() |= XButtons::UP; }
            }
        }
    }
}

#[derive(Debug)]
pub enum JoystickEvent {
    Axis { axis_idx: u8, value: i16 },
    ButtonDown { button_idx: u8 },
    ButtonUp { button_idx: u8 },
    HatMotion { state: HatState },
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
    /// mapping 为手柄键位映射（从配置文件读取，智慧核心启动时传入）
    pub fn start(mapping: ConMapping) -> Self {
        let stop_flag = Arc::new(AtomicBool::new(false));
        let state = Arc::new(Mutex::new(XGamepad::default()));
        let ready_flag = Arc::new(AtomicBool::new(false));
        let error_flag = Arc::new(AtomicBool::new(false));
        let stop_clone = stop_flag.clone();
        let state_clone = state.clone();
        let ready_clone = ready_flag.clone();
        let error_flag_clone = error_flag.clone();
        let mapping_clone = mapping.clone();

        let handle = thread::spawn(move || {
            let sdl_ctx = match sdl2::init() {
                Ok(ctx) => ctx,
                Err(e) => {
                    log_error(&format!("手柄读取 - SDL初始化失败: {}", e));
                    error_flag_clone.store(true, Ordering::SeqCst);
                    return;
                }
            };
            
            let js_sub = match sdl_ctx.joystick() {
                Ok(sub) => sub,
                Err(e) => {
                    log_error(&format!("手柄读取 - SDL手柄子系统初始化失败: {}", e));
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

            // 获取手柄数量
            let js_count = match js_sub.num_joysticks() {
                Ok(count) => count,
                Err(e) => {
                    log_error(&format!("手柄读取 - 获取手柄数量失败: {}", e));
                    error_flag_clone.store(true, Ordering::SeqCst);
                    return;
                }
            };
            
            // 打开除0号外的所有手柄
            let mut joysticks = Vec::new();
            for dev_idx in 1..js_count {  // 从1开始，跳过0号虚拟手柄
                match js_sub.open(dev_idx) {
                    Ok(joy) => {
                        // println!("成功打开物理手柄索引{}: {}", dev_idx, joy.name());
                        joysticks.push(joy);
                    }
                    Err(e) => {
                        log_error(&format!("手柄读取 - 打开手柄索引{}失败: {}", dev_idx, e));
                        // 单个手柄打开失败不影响其他手柄
                    }
                }
            }
            
            if joysticks.is_empty() {
                log_error("手柄读取 - 没有可用的物理手柄（0号已被虚拟手柄占用）");
                error_flag_clone.store(true, Ordering::SeqCst);
                return;
            }

            // 标记就绪
            ready_clone.store(true, Ordering::SeqCst);
            // println!("SDL 事件读取线程已启动");

            // 启用事件
            for ev in &[
                EventType::JoyAxisMotion,
                EventType::JoyButtonDown,
                EventType::JoyButtonUp,
                EventType::JoyHatMotion,
            ] {
                let _ = pump.enable_event(*ev);
            }

            let mut consecutive_errors = 0;
            const MAX_CONSECUTIVE_ERRORS: u32 = 100; // 允许更多错误，因为手柄断开连接是常见的
            
            // 循环处理
            while !stop_clone.load(Ordering::SeqCst) {
                let mut had_joy_event = false;
                for evt in pump.poll_iter() {
                    // 过滤手柄事件
                    match evt {
                        Event::JoyAxisMotion { .. }
                        | Event::JoyButtonDown { .. }
                        | Event::JoyButtonUp { .. }
                        | Event::JoyHatMotion { .. } => {},
                        _ => continue,
                    };

                    // 分发事件到状态
                    match state_clone.lock() {
                        Ok(mut lock) => {
                            match evt {
                                Event::JoyAxisMotion { axis_idx, value, .. } => {
                                    apply_event(&mut *lock, &JoystickEvent::Axis { axis_idx, value }, &mapping_clone);
                                    had_joy_event = true;
                                }
                                Event::JoyButtonDown { button_idx, .. } => {
                                    apply_event(&mut *lock, &JoystickEvent::ButtonDown { button_idx }, &mapping_clone);
                                    had_joy_event = true;
                                }
                                Event::JoyButtonUp { button_idx, .. } => {
                                    apply_event(&mut *lock, &JoystickEvent::ButtonUp { button_idx }, &mapping_clone);
                                    had_joy_event = true;
                                }
                                Event::JoyHatMotion { state, .. } => {
                                    apply_event(&mut *lock, &JoystickEvent::HatMotion { state }, &mapping_clone);
                                    had_joy_event = true;
                                }
                                _ => {}
                            }
                            consecutive_errors = 0; // 重置错误计数
                        }
                        Err(e) => {
                            log_error(&format!("手柄读取 - 获取状态锁失败: {:?}", e));
                            consecutive_errors += 1;
                            if consecutive_errors >= MAX_CONSECUTIVE_ERRORS {
                                log_error(&format!("手柄读取 - 连续错误超过{}次，设置错误标志", MAX_CONSECUTIVE_ERRORS));
                                error_flag_clone.store(true, Ordering::SeqCst);
                                break;
                            }
                        }
                    }
                }
                if had_joy_event {
                    debug_print_axes_and_buttons();
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