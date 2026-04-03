use std::{
    sync::{
        Arc,
        Mutex,
        atomic::{AtomicBool, Ordering},
    },
    thread::{self, JoinHandle},
    time::Duration,
};
use vigem_client::{XGamepad, XButtons};
use windows_sys::Win32::Foundation::ERROR_SUCCESS;
use windows_sys::Win32::UI::Input::XboxController::{
    XINPUT_GAMEPAD, XINPUT_GAMEPAD_A, XINPUT_GAMEPAD_B, XINPUT_GAMEPAD_BACK, XINPUT_GAMEPAD_DPAD_DOWN,
    XINPUT_GAMEPAD_DPAD_LEFT, XINPUT_GAMEPAD_DPAD_RIGHT, XINPUT_GAMEPAD_DPAD_UP, XINPUT_GAMEPAD_LEFT_SHOULDER,
    XINPUT_GAMEPAD_LEFT_THUMB, XINPUT_GAMEPAD_RIGHT_SHOULDER, XINPUT_GAMEPAD_RIGHT_THUMB, XINPUT_GAMEPAD_START,
    XINPUT_GAMEPAD_X, XINPUT_GAMEPAD_Y, XINPUT_STATE, XInputGetState,
};

use crate::shared_constants::error_limits::GAMEPAD_READING_MAX_CONSECUTIVE_ERRORS;
use crate::shared_constants::input_device::XBOX as INPUT_DEVICE_XBOX;
use crate::utils::console_redirect::log_error;
use crate::utils::enum_device_tool::first_physical_xinput_slot;

fn xinput_pressed(buttons: u16, mask: u16) -> bool {
    buttons & mask != 0
}

fn xinput_to_vigem_buttons(buttons: u16) -> u16 {
    let mut out = 0u16;
    if xinput_pressed(buttons, XINPUT_GAMEPAD_A) { out |= XButtons::A; }
    if xinput_pressed(buttons, XINPUT_GAMEPAD_B) { out |= XButtons::B; }
    if xinput_pressed(buttons, XINPUT_GAMEPAD_X) { out |= XButtons::X; }
    if xinput_pressed(buttons, XINPUT_GAMEPAD_Y) { out |= XButtons::Y; }
    if xinput_pressed(buttons, XINPUT_GAMEPAD_LEFT_SHOULDER) { out |= XButtons::LB; }
    if xinput_pressed(buttons, XINPUT_GAMEPAD_RIGHT_SHOULDER) { out |= XButtons::RB; }
    if xinput_pressed(buttons, XINPUT_GAMEPAD_BACK) { out |= XButtons::BACK; }
    if xinput_pressed(buttons, XINPUT_GAMEPAD_START) { out |= XButtons::START; }
    if xinput_pressed(buttons, XINPUT_GAMEPAD_LEFT_THUMB) { out |= XButtons::LTHUMB; }
    if xinput_pressed(buttons, XINPUT_GAMEPAD_RIGHT_THUMB) { out |= XButtons::RTHUMB; }
    if xinput_pressed(buttons, XINPUT_GAMEPAD_DPAD_UP) { out |= XButtons::UP; }
    if xinput_pressed(buttons, XINPUT_GAMEPAD_DPAD_DOWN) { out |= XButtons::DOWN; }
    if xinput_pressed(buttons, XINPUT_GAMEPAD_DPAD_LEFT) { out |= XButtons::LEFT; }
    if xinput_pressed(buttons, XINPUT_GAMEPAD_DPAD_RIGHT) { out |= XButtons::RIGHT; }
    out
}

fn poll_xinput_to_gamepad(xg: &XINPUT_GAMEPAD) -> XGamepad {
    let mut mapped_state = XGamepad::default();
    mapped_state.thumb_lx = xg.sThumbLX;
    mapped_state.thumb_ly = xg.sThumbLY;
    mapped_state.thumb_rx = xg.sThumbRX;
    mapped_state.thumb_ry = xg.sThumbRY;
    mapped_state.left_trigger = xg.bLeftTrigger;
    mapped_state.right_trigger = xg.bRightTrigger;
    *mapped_state.buttons.as_mut() = xinput_to_vigem_buttons(xg.wButtons);

    mapped_state
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
    pub fn start(input_device: String) -> Self {
        let stop_flag = Arc::new(AtomicBool::new(false));
        let state = Arc::new(Mutex::new(XGamepad::default()));
        let ready_flag = Arc::new(AtomicBool::new(false));
        let error_flag = Arc::new(AtomicBool::new(false));
        let stop_clone = stop_flag.clone();
        let state_clone = state.clone();
        let ready_clone = ready_flag.clone();
        let error_flag_clone = error_flag.clone();
        let handle = thread::spawn(move || {
            if input_device != INPUT_DEVICE_XBOX {
                log_error("手柄读取 - 当前仅支持 Xbox (XInput) 输入设备");
                error_flag_clone.store(true, Ordering::SeqCst);
                return;
            }

            let mut active_slot = match first_physical_xinput_slot() {
                Some(slot) => slot,
                None => {
                    log_error("手柄读取 - XInputGetState 失败，未检测到 Xbox 手柄");
                    error_flag_clone.store(true, Ordering::SeqCst);
                    return;
                }
            };
            let mut initial_state: XINPUT_STATE = unsafe { std::mem::zeroed() };
            if unsafe { XInputGetState(active_slot, &mut initial_state) } != ERROR_SUCCESS {
                log_error("手柄读取 - XInput 初始化失败，未检测到 Xbox 手柄");
                error_flag_clone.store(true, Ordering::SeqCst);
                return;
            }

            ready_clone.store(true, Ordering::SeqCst);
            let mut consecutive_errors = 0;

            while !stop_clone.load(Ordering::SeqCst) {
                let mut xstate: XINPUT_STATE = unsafe { std::mem::zeroed() };
                let mut result = unsafe { XInputGetState(active_slot, &mut xstate) };
                if result != ERROR_SUCCESS {
                    if let Some(new_slot) = first_physical_xinput_slot() {
                        active_slot = new_slot;
                        xstate = unsafe { std::mem::zeroed() };
                        result = unsafe { XInputGetState(active_slot, &mut xstate) };
                    }
                }
                let polled_state = if result == ERROR_SUCCESS {
                    consecutive_errors = 0;
                    poll_xinput_to_gamepad(&xstate.Gamepad)
                } else {
                    XGamepad::default()
                };

                match state_clone.lock() {
                    Ok(mut lock) => {
                        *lock = polled_state;
                        if result != ERROR_SUCCESS {
                            consecutive_errors += 1;
                            if consecutive_errors >= GAMEPAD_READING_MAX_CONSECUTIVE_ERRORS {
                                log_error(&format!(
                                    "手柄读取 - XInput连续错误超过{}次，设置错误标志",
                                    GAMEPAD_READING_MAX_CONSECUTIVE_ERRORS
                                ));
                                error_flag_clone.store(true, Ordering::SeqCst);
                                break;
                            }
                        }
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