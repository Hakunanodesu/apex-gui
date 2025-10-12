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
    event::{Event, EventType},
    joystick::HatState,
};
use vigem_client::{XGamepad, XButtons};

use crate::utils::console_redirect::log_error;


fn scale_to_u8(v: i16) -> u8 {
    (((v as i32 + 32768) * 255 / 65535) as u8).clamp(0, 255)
}

fn map_button(idx: u8, is_ps: bool) -> Option<u16> {
    if is_ps {
        Some(match idx {
            1 => XButtons::A,
            2 => XButtons::B,
            0 => XButtons::X,
            3 => XButtons::Y,
            4 => XButtons::LB,
            5 => XButtons::RB,
            8 => XButtons::BACK,
            9 => XButtons::START,
            10 => XButtons::LTHUMB,
            11 => XButtons::RTHUMB,
            _ => return None,
        })
    } else {
        Some(match idx {
            0 => XButtons::A,
            1 => XButtons::B,
            2 => XButtons::X,
            3 => XButtons::Y,
            4 => XButtons::LB,
            5 => XButtons::RB,
            6 => XButtons::BACK,
            7 => XButtons::START,
            8 => XButtons::LTHUMB,
            9 => XButtons::RTHUMB,
            _ => return None,
        })
    }
}

fn map_axis(state: &mut XGamepad, axis_idx: u8, value: i16, is_ps: bool) {
    if is_ps {
        match axis_idx {
            0 => state.thumb_lx = value,
            1 => state.thumb_ly = value.saturating_neg(),
            2 => state.thumb_rx = value,
            5 => state.thumb_ry = value.saturating_neg(),
            3 => state.left_trigger = scale_to_u8(value),
            4 => state.right_trigger = scale_to_u8(value),
            _ => {}
        }
    } else {
        match axis_idx {
            0 => state.thumb_lx = value,
            1 => state.thumb_ly = value.saturating_neg(),
            3 => state.thumb_rx = value,
            4 => state.thumb_ry = value.saturating_neg(),
            2 => state.left_trigger = scale_to_u8(value),
            5 => state.right_trigger = scale_to_u8(value),
            _ => {}
        }
    }
}

fn apply_event(state: &mut XGamepad, evt: &JoystickEvent, is_ps: bool) {
    match evt {
        JoystickEvent::Axis { axis_idx, value } => {
            map_axis(state, *axis_idx, *value, is_ps)
        }
        JoystickEvent::ButtonDown { button_idx } => {
            if let Some(bit) = map_button(*button_idx, is_ps) {
                *state.buttons.as_mut() |= bit;
            }
        }
        JoystickEvent::ButtonUp { button_idx } => {
            if let Some(bit) = map_button(*button_idx, is_ps) {
                *state.buttons.as_mut() &= !bit;
            }
        }
        JoystickEvent::HatMotion { state: hat } => {
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
    /// 默认读取除0号外的所有手柄（索引0被虚拟手柄占用）
    pub fn start(is_ps_config: bool) -> Self {
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
                for evt in pump.poll_iter() {
                    // 过滤手柄事件
                    match evt {
                        Event::JoyAxisMotion { .. }
                        | Event::JoyButtonDown { .. }
                        | Event::JoyButtonUp { .. }
                        | Event::JoyHatMotion { .. } => {},
                        _ => continue,
                    };

                    // 使用配置的 is_ps 值
                    let is_ps = is_ps_config;

                    // 分发事件到状态
                    match state_clone.lock() {
                        Ok(mut lock) => {
                            match evt {
                                Event::JoyAxisMotion { axis_idx, value, .. } => {
                                    // println!("axis_id: {}, value: {}", axis_idx, value);
                                    apply_event(&mut *lock, &JoystickEvent::Axis { axis_idx, value }, is_ps);
                                }
                                Event::JoyButtonDown { button_idx, .. } => {
                                    // println!("button_down: {}", button_idx);
                                    apply_event(&mut *lock, &JoystickEvent::ButtonDown { button_idx }, is_ps);
                                }
                                Event::JoyButtonUp { button_idx, .. } => {
                                    // println!("button_up: {}", button_idx);
                                    apply_event(&mut *lock, &JoystickEvent::ButtonUp { button_idx }, is_ps);
                                }
                                Event::JoyHatMotion { state, .. } => {
                                    // println!("hat_state: {:?}", state);
                                    apply_event(&mut *lock, &JoystickEvent::HatMotion { state }, is_ps);
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