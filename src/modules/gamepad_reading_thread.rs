use std::{
    collections::VecDeque,
    io::{self, Write},
    sync::{
        Arc,
        Mutex,
        atomic::{AtomicBool, Ordering},
    },
    thread::{self, JoinHandle},
    time::{Duration, Instant},
};
use hidapi::{DeviceInfo, HidApi, HidDevice};
use vigem_client::{XButtons, XGamepad};
use windows_sys::Win32::Foundation::ERROR_SUCCESS;
use windows_sys::Win32::UI::Input::XboxController::{
    XINPUT_GAMEPAD, XINPUT_GAMEPAD_A, XINPUT_GAMEPAD_B, XINPUT_GAMEPAD_BACK,
    XINPUT_GAMEPAD_DPAD_DOWN, XINPUT_GAMEPAD_DPAD_LEFT, XINPUT_GAMEPAD_DPAD_RIGHT,
    XINPUT_GAMEPAD_DPAD_UP, XINPUT_GAMEPAD_LEFT_SHOULDER, XINPUT_GAMEPAD_LEFT_THUMB,
    XINPUT_GAMEPAD_RIGHT_SHOULDER, XINPUT_GAMEPAD_RIGHT_THUMB, XINPUT_GAMEPAD_START,
    XINPUT_GAMEPAD_X, XINPUT_GAMEPAD_Y, XINPUT_STATE, XInputGetState,
};

use crate::shared_constants::error_limits::GAMEPAD_READING_MAX_CONSECUTIVE_ERRORS;
use crate::shared_constants::hid::{
    DUALSENSE_PIDS,
    DUALSHOCK4_PIDS,
    SONY_VID,
};
use crate::shared_constants::input_device::{
    DUALSENSE as INPUT_DEVICE_DUALSENSE,
    DUALSHOCK4 as INPUT_DEVICE_DUALSHOCK4,
    XBOX as INPUT_DEVICE_XBOX,
};
use crate::utils::console_redirect::log_error;
use crate::utils::controller_probe::first_physical_xinput_slot;

const HID_READ_BUF_SIZE: usize = 128;
const HID_READ_TIMEOUT_MS: i32 = 50;
const DEVICE_RESCAN_INTERVAL_MS: u64 = 1000;

#[derive(Clone, Copy)]
enum PadProfile {
    DualSense,
    DualShock4,
}

impl PadProfile {
    fn name(self) -> &'static str {
        match self {
            PadProfile::DualSense => "DualSense",
            PadProfile::DualShock4 => "DualShock 4",
        }
    }
}

fn input_device_to_profile(input_device: &str) -> Option<PadProfile> {
    if input_device.trim().eq_ignore_ascii_case(INPUT_DEVICE_DUALSENSE) {
        return Some(PadProfile::DualSense);
    }
    if input_device
        .trim()
        .eq_ignore_ascii_case(INPUT_DEVICE_DUALSHOCK4)
    {
        return Some(PadProfile::DualShock4);
    }
    None
}

fn profile_matches_device(profile: PadProfile, d: &DeviceInfo) -> bool {
    if d.vendor_id() != SONY_VID {
        return false;
    }
    match profile {
        PadProfile::DualSense => {
            DUALSENSE_PIDS.contains(&d.product_id())
                || d.product_string()
                    .is_some_and(|s| s.to_ascii_lowercase().contains("dualsense"))
        }
        PadProfile::DualShock4 => {
            DUALSHOCK4_PIDS.contains(&d.product_id())
                || d.product_string().is_some_and(|s| {
                    let s = s.to_ascii_lowercase();
                    s.contains("dualshock") || s.contains("wireless controller")
                })
        }
    }
}

fn open_sony_device(api: &HidApi, profile: PadProfile) -> Option<HidDevice> {
    api.device_list()
        .filter(|d| profile_matches_device(profile, d))
        .find_map(|d| d.open_device(api).ok())
}

fn stick_u8_to_i16(v: u8) -> i16 {
    let vi = v as i32;
    if vi >= 128 {
        (((vi - 128) * 32767) / 127) as i16
    } else {
        -(((128 - vi) * 32768) / 128) as i16
    }
}

fn stick_u8_to_i16_inverted(v: u8) -> i16 {
    let n = stick_u8_to_i16(v) as i32;
    (-n).clamp(-32768, 32767) as i16
}

fn apply_dpad_buttons(buttons: &mut u16, dpad: u8) {
    match dpad {
        0 => *buttons |= XButtons::UP,
        1 => *buttons |= XButtons::UP | XButtons::RIGHT,
        2 => *buttons |= XButtons::RIGHT,
        3 => *buttons |= XButtons::RIGHT | XButtons::DOWN,
        4 => *buttons |= XButtons::DOWN,
        5 => *buttons |= XButtons::DOWN | XButtons::LEFT,
        6 => *buttons |= XButtons::LEFT,
        7 => *buttons |= XButtons::LEFT | XButtons::UP,
        _ => {}
    }
}

fn decode_touch_x(contact: u8, x_lo: u8, xy: u8) -> Option<u16> {
    if (contact & 0x80) != 0 {
        return None;
    }
    let x_hi = xy & 0x0F;
    Some(((x_hi as u16) << 8) | x_lo as u16)
}

fn avg_active_touch_x(x0: Option<u16>, x1: Option<u16>) -> Option<u16> {
    match (x0, x1) {
        (Some(a), Some(b)) => Some(((a as u32 + b as u32) / 2) as u16),
        (Some(a), None) => Some(a),
        (None, Some(b)) => Some(b),
        (None, None) => None,
    }
}

fn map_touchpad_click_to_back_start(buttons: &mut u16, touchpad_btn: bool, touch_x: Option<u16>) {
    if !touchpad_btn {
        return;
    }
    if let Some(x) = touch_x {
        if x <= 959 {
            *buttons |= XButtons::BACK;
        } else {
            *buttons |= XButtons::START;
        }
    }
}

fn parse_dualshock4_common_to_xgamepad(
    p: &[u8],
    touch_x_pair: Option<(Option<u16>, Option<u16>)>,
) -> Option<XGamepad> {
    if p.len() < 7 {
        return None;
    }
    let mut out = XGamepad::default();
    out.thumb_lx = stick_u8_to_i16(p[0]);
    out.thumb_ly = stick_u8_to_i16_inverted(p[1]);
    out.thumb_rx = stick_u8_to_i16(p[2]);
    out.thumb_ry = stick_u8_to_i16_inverted(p[3]);
    out.left_trigger = if p.len() >= 9 { p[7] } else { 0 };
    out.right_trigger = if p.len() >= 9 { p[8] } else { 0 };

    let b0 = p[4];
    let b1 = p[5];
    let b2 = p[6];
    let dpad = b0 & 0x0F;
    let touchpad_btn = (b2 & 0x02) != 0;
    let avg_touch_x = touch_x_pair.and_then(|(x0, x1)| avg_active_touch_x(x0, x1));

    let mut buttons = 0u16;
    apply_dpad_buttons(&mut buttons, dpad);
    if (b0 & 0x20) != 0 {
        buttons |= XButtons::A; // Cross
    }
    if (b0 & 0x40) != 0 {
        buttons |= XButtons::B; // Circle
    }
    if (b0 & 0x10) != 0 {
        buttons |= XButtons::X; // Square
    }
    if (b0 & 0x80) != 0 {
        buttons |= XButtons::Y; // Triangle
    }
    if (b1 & 0x01) != 0 {
        buttons |= XButtons::LB;
    }
    if (b1 & 0x02) != 0 {
        buttons |= XButtons::RB;
    }
    if (b1 & 0x10) != 0 {
        buttons |= XButtons::BACK; // Share
    }
    if (b1 & 0x20) != 0 {
        buttons |= XButtons::START; // Options
    }
    if (b1 & 0x40) != 0 {
        buttons |= XButtons::LTHUMB;
    }
    if (b1 & 0x80) != 0 {
        buttons |= XButtons::RTHUMB;
    }
    map_touchpad_click_to_back_start(&mut buttons, touchpad_btn, avg_touch_x);

    *out.buttons.as_mut() = buttons;
    Some(out)
}

fn parse_dualshock4_report_to_xgamepad(report: &[u8]) -> Option<XGamepad> {
    if report.is_empty() {
        return None;
    }
    match (report[0], report.len()) {
        (0x01, n) if n >= 64 => {
            let tx = if report.len() >= 43 {
                let t = &report[34..43];
                Some((decode_touch_x(t[1], t[2], t[3]), decode_touch_x(t[5], t[6], t[7])))
            } else {
                None
            };
            parse_dualshock4_common_to_xgamepad(&report[1..33], tx)
        }
        (0x11, n) if n >= 78 => {
            let tx = if report.len() >= 45 {
                let t = &report[36..45];
                Some((decode_touch_x(t[1], t[2], t[3]), decode_touch_x(t[5], t[6], t[7])))
            } else {
                None
            };
            parse_dualshock4_common_to_xgamepad(&report[3..35], tx)
        }
        (0x01, 10) => parse_dualshock4_common_to_xgamepad(&report[1..10], None),
        _ => None,
    }
}

fn parse_dualsense_report_to_xgamepad(report: &[u8]) -> Option<XGamepad> {
    if report.is_empty() {
        return None;
    }
    let payload_offset = match report[0] {
        0x01 if report.len() >= 64 => 1, // USB
        0x31 if report.len() >= 78 => 2, // Bluetooth
        _ => return None,
    };
    if report.len() < payload_offset + 63 {
        return None;
    }
    let p = &report[payload_offset..payload_offset + 63];
    let mut out = XGamepad::default();
    out.thumb_lx = stick_u8_to_i16(p[0]);
    out.thumb_ly = stick_u8_to_i16_inverted(p[1]);
    out.thumb_rx = stick_u8_to_i16(p[2]);
    out.thumb_ry = stick_u8_to_i16_inverted(p[3]);
    out.left_trigger = p[4];
    out.right_trigger = p[5];

    let b0 = p[7];
    let b1 = p[8];
    let b2 = p[9];
    let dpad = b0 & 0x0F;
    let touchpad_btn = (b2 & 0x02) != 0;
    let avg_touch_x = avg_active_touch_x(
        decode_touch_x(p[32], p[33], p[34]),
        decode_touch_x(p[36], p[37], p[38]),
    );

    let mut buttons = 0u16;
    apply_dpad_buttons(&mut buttons, dpad);
    if (b0 & 0x20) != 0 {
        buttons |= XButtons::A; // Cross
    }
    if (b0 & 0x40) != 0 {
        buttons |= XButtons::B; // Circle
    }
    if (b0 & 0x10) != 0 {
        buttons |= XButtons::X; // Square
    }
    if (b0 & 0x80) != 0 {
        buttons |= XButtons::Y; // Triangle
    }
    if (b1 & 0x01) != 0 {
        buttons |= XButtons::LB;
    }
    if (b1 & 0x02) != 0 {
        buttons |= XButtons::RB;
    }
    if (b1 & 0x10) != 0 {
        buttons |= XButtons::BACK; // Create
    }
    if (b1 & 0x20) != 0 {
        buttons |= XButtons::START; // Options
    }
    if (b1 & 0x40) != 0 {
        buttons |= XButtons::LTHUMB;
    }
    if (b1 & 0x80) != 0 {
        buttons |= XButtons::RTHUMB;
    }
    map_touchpad_click_to_back_start(&mut buttons, touchpad_btn, avg_touch_x);

    *out.buttons.as_mut() = buttons;
    Some(out)
}

fn parse_sony_report_to_xgamepad(profile: PadProfile, report: &[u8]) -> Option<XGamepad> {
    match profile {
        PadProfile::DualSense => parse_dualsense_report_to_xgamepad(report),
        PadProfile::DualShock4 => parse_dualshock4_report_to_xgamepad(report),
    }
}

#[cfg(debug_assertions)]
const DEBUG_RENDER_INTERVAL_MS: u64 = 80;
#[cfg(debug_assertions)]
const POLL_INTERVAL_SAMPLES: usize = 32;

#[cfg(debug_assertions)]
fn dpad_text(v: u8) -> &'static str {
    match v {
        0 => "Up",
        1 => "Up-Right",
        2 => "Right",
        3 => "Down-Right",
        4 => "Down",
        5 => "Down-Left",
        6 => "Left",
        7 => "Up-Left",
        8 => "Neutral",
        _ => "Neutral(Invalid)",
    }
}

#[cfg(debug_assertions)]
fn render_debug_panel(title: &str, lines: &[String], last_render: &mut Instant) {
    if last_render.elapsed() < Duration::from_millis(DEBUG_RENDER_INTERVAL_MS) {
        return;
    }
    print!("\x1B[2J\x1B[H");
    println!("{title}");
    for line in lines {
        println!("{line}");
    }
    let _ = io::stdout().flush();
    *last_render = Instant::now();
}

#[cfg(debug_assertions)]
struct PollRateTracker {
    last_report_at: Option<Instant>,
    last_interval_s: Option<f64>,
    interval_ring: VecDeque<f64>,
}

#[cfg(debug_assertions)]
impl PollRateTracker {
    fn new() -> Self {
        Self {
            last_report_at: None,
            last_interval_s: None,
            interval_ring: VecDeque::with_capacity(POLL_INTERVAL_SAMPLES),
        }
    }

    fn on_report(&mut self, now: Instant) {
        if let Some(prev) = self.last_report_at {
            let dt = now.duration_since(prev).as_secs_f64();
            if dt > 0.0 && dt.is_finite() {
                self.last_interval_s = Some(dt);
                self.interval_ring.push_back(dt);
                if self.interval_ring.len() > POLL_INTERVAL_SAMPLES {
                    self.interval_ring.pop_front();
                }
            }
        }
        self.last_report_at = Some(now);
    }

    fn line(&self) -> String {
        let inst_hz = self.last_interval_s.filter(|dt| *dt > 0.0).map(|dt| 1.0 / dt);
        let avg_hz = (!self.interval_ring.is_empty())
            .then(|| {
                let sum: f64 = self.interval_ring.iter().sum();
                let mean = sum / self.interval_ring.len() as f64;
                if mean > 0.0 { Some(1.0 / mean) } else { None }
            })
            .flatten();

        match (inst_hz, avg_hz) {
            (Some(i), Some(a)) => format!(
                "Poll: Inst {:.1} Hz | Avg {:.1} Hz ({} samples)",
                i,
                a,
                self.interval_ring.len()
            ),
            (Some(i), None) => format!("Poll: Inst {:.1} Hz | Avg --", i),
            (None, Some(a)) => format!(
                "Poll: Inst -- | Avg {:.1} Hz ({} samples)",
                a,
                self.interval_ring.len()
            ),
            (None, None) => "Poll: -- (no reports yet)".to_string(),
        }
    }
}

#[cfg(debug_assertions)]
fn with_poll_line(mut lines: Vec<String>, poll: &PollRateTracker) -> Vec<String> {
    lines.insert(0, poll.line());
    lines
}

#[cfg(debug_assertions)]
fn xinput_debug_lines(xg: &XINPUT_GAMEPAD) -> Vec<String> {
    vec![
        format!(
            "Axes(raw): LX={} LY={} RX={} RY={}",
            xg.sThumbLX, xg.sThumbLY, xg.sThumbRX, xg.sThumbRY
        ),
        format!(
            "Triggers(raw): LT={} RT={}",
            xg.bLeftTrigger, xg.bRightTrigger
        ),
        format!("Buttons(raw): 0x{:04X}", xg.wButtons),
    ]
}

#[cfg(debug_assertions)]
fn dualshock4_debug_lines(report: &[u8]) -> Vec<String> {
    if report.is_empty() {
        return vec!["DS4 raw report empty".to_string()];
    }
    let (p, touch) = match (report[0], report.len()) {
        (0x01, n) if n >= 64 => {
            let common = &report[1..33];
            let tx = if report.len() >= 43 {
                let t = &report[34..43];
                Some((
                    decode_touch_x(t[1], t[2], t[3]),
                    decode_touch_x(t[5], t[6], t[7]),
                ))
            } else {
                None
            };
            (common, tx)
        }
        (0x11, n) if n >= 78 => {
            let common = &report[3..35];
            let tx = if report.len() >= 45 {
                let t = &report[36..45];
                Some((
                    decode_touch_x(t[1], t[2], t[3]),
                    decode_touch_x(t[5], t[6], t[7]),
                ))
            } else {
                None
            };
            (common, tx)
        }
        (0x01, 10) => (&report[1..10], None),
        _ => {
            return vec![format!(
                "DS4 unsupported report: id=0x{:02X}, len={}",
                report[0],
                report.len()
            )];
        }
    };
    if p.len() < 7 {
        return vec!["DS4 common too short".to_string()];
    }

    let lx = p[0];
    let ly = p[1];
    let rx = p[2];
    let ry = p[3];
    let b0 = p[4];
    let b1 = p[5];
    let b2 = p[6];
    let l2 = if p.len() >= 9 { p[7] } else { 0 };
    let r2 = if p.len() >= 9 { p[8] } else { 0 };
    let dpad = b0 & 0x0F;
    let touch_btn = (b2 & 0x02) != 0;
    let touch_line = if let Some((x0, x1)) = touch {
        format!(
            "TouchX(raw): Btn={} P0.x={} P1.x={}",
            if touch_btn { "Pressed" } else { "Released" },
            x0.map_or("--".to_string(), |v| v.to_string()),
            x1.map_or("--".to_string(), |v| v.to_string())
        )
    } else {
        format!(
            "TouchX(raw): Btn={} P0.x=-- P1.x=--",
            if touch_btn { "Pressed" } else { "Released" }
        )
    };

    vec![
        format!("Report(raw): id=0x{:02X}, len={}", report[0], report.len()),
        format!(
            "Axes(raw): LX={} LY={} RX={} RY={}",
            lx, ly, rx, ry
        ),
        format!("Triggers(raw): L2={} R2={}", l2, r2),
        format!("DPad(raw): {} ({})", dpad, dpad_text(dpad)),
        format!("Buttons(raw): b0=0x{:02X} b1=0x{:02X} b2=0x{:02X}", b0, b1, b2),
        touch_line,
    ]
}

#[cfg(debug_assertions)]
fn dualsense_debug_lines(report: &[u8]) -> Vec<String> {
    if report.is_empty() {
        return vec!["DS5 raw report empty".to_string()];
    }
    let payload_offset = match report[0] {
        0x01 if report.len() >= 64 => 1,
        0x31 if report.len() >= 78 => 2,
        _ => {
            return vec![format!(
                "DS5 unsupported report: id=0x{:02X}, len={}",
                report[0],
                report.len()
            )];
        }
    };
    if report.len() < payload_offset + 63 {
        return vec!["DS5 payload too short".to_string()];
    }

    let p = &report[payload_offset..payload_offset + 63];
    let lx = p[0];
    let ly = p[1];
    let rx = p[2];
    let ry = p[3];
    let l2 = p[4];
    let r2 = p[5];
    let b0 = p[7];
    let b1 = p[8];
    let b2 = p[9];
    let dpad = b0 & 0x0F;
    let touch_btn = (b2 & 0x02) != 0;
    let x0 = decode_touch_x(p[32], p[33], p[34]);
    let x1 = decode_touch_x(p[36], p[37], p[38]);

    vec![
        format!("Report(raw): id=0x{:02X}, len={}", report[0], report.len()),
        format!(
            "Axes(raw): LX={} LY={} RX={} RY={}",
            lx, ly, rx, ry
        ),
        format!("Triggers(raw): L2={} R2={}", l2, r2),
        format!("DPad(raw): {} ({})", dpad, dpad_text(dpad)),
        format!("Buttons(raw): b0=0x{:02X} b1=0x{:02X} b2=0x{:02X}", b0, b1, b2),
        format!(
            "TouchX(raw): Btn={} P0.x={} P1.x={}",
            if touch_btn { "Pressed" } else { "Released" },
            x0.map_or("--".to_string(), |v| v.to_string()),
            x1.map_or("--".to_string(), |v| v.to_string())
        ),
    ]
}

#[cfg(debug_assertions)]
fn sony_debug_lines(profile: PadProfile, report: &[u8]) -> Vec<String> {
    match profile {
        PadProfile::DualSense => dualsense_debug_lines(report),
        PadProfile::DualShock4 => dualshock4_debug_lines(report),
    }
}

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
            if input_device.trim().eq_ignore_ascii_case(INPUT_DEVICE_XBOX) {
                let mut active_slot = first_physical_xinput_slot();
                if active_slot.is_some() {
                    ready_clone.store(true, Ordering::SeqCst);
                }
                let mut consecutive_errors = 0;
                let mut last_rescan = Instant::now()
                    .checked_sub(Duration::from_millis(DEVICE_RESCAN_INTERVAL_MS))
                    .unwrap_or_else(Instant::now);
                #[cfg(debug_assertions)]
                let mut last_debug_render = Instant::now()
                    .checked_sub(Duration::from_millis(DEBUG_RENDER_INTERVAL_MS))
                    .unwrap_or_else(Instant::now);
                #[cfg(debug_assertions)]
                let mut latest_debug_lines: Vec<String> = vec!["等待 Xbox 输入...".to_string()];
                #[cfg(debug_assertions)]
                let mut poll_tracker = PollRateTracker::new();

                while !stop_clone.load(Ordering::SeqCst) {
                    if active_slot.is_none()
                        && last_rescan.elapsed() >= Duration::from_millis(DEVICE_RESCAN_INTERVAL_MS)
                    {
                        active_slot = first_physical_xinput_slot();
                        last_rescan = Instant::now();
                        if active_slot.is_some() {
                            ready_clone.store(true, Ordering::SeqCst);
                        }
                    }

                    let polled_state = if let Some(slot) = active_slot {
                        let mut xstate: XINPUT_STATE = unsafe { std::mem::zeroed() };
                        let result = unsafe { XInputGetState(slot, &mut xstate) };
                        if result == ERROR_SUCCESS {
                            #[cfg(debug_assertions)]
                            {
                                poll_tracker.on_report(Instant::now());
                                latest_debug_lines =
                                    with_poll_line(xinput_debug_lines(&xstate.Gamepad), &poll_tracker);
                            }
                            poll_xinput_to_gamepad(&xstate.Gamepad)
                        } else {
                            active_slot = None;
                            last_rescan = Instant::now();
                            #[cfg(debug_assertions)]
                            {
                                latest_debug_lines = with_poll_line(
                                    vec!["Xbox 设备断开，等待重连（每1秒重扫）".to_string()],
                                    &poll_tracker,
                                );
                            }
                            XGamepad::default()
                        }
                    } else {
                        #[cfg(debug_assertions)]
                        {
                            latest_debug_lines = with_poll_line(
                                vec!["未检测到 Xbox 设备，等待连接（每1秒重扫）".to_string()],
                                &poll_tracker,
                            );
                        }
                        XGamepad::default()
                    };

                    match state_clone.lock() {
                        Ok(mut lock) => {
                            *lock = polled_state;
                            consecutive_errors = 0;
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
                    #[cfg(debug_assertions)]
                    render_debug_panel(
                        "Gamepad Reader Debug | Xbox (XInput)",
                        &latest_debug_lines,
                        &mut last_debug_render,
                    );
                    thread::sleep(Duration::from_millis(1));
                }
                return;
            }

            let Some(profile) = input_device_to_profile(&input_device) else {
                log_error("手柄读取 - 不支持的输入设备类型");
                error_flag_clone.store(true, Ordering::SeqCst);
                return;
            };

            let mut api = match HidApi::new() {
                Ok(v) => v,
                Err(e) => {
                    log_error(&format!("手柄读取 - 初始化 HID 失败: {}", e));
                    error_flag_clone.store(true, Ordering::SeqCst);
                    return;
                }
            };
            let mut dev = open_sony_device(&api, profile);
            if dev.is_some() {
                ready_clone.store(true, Ordering::SeqCst);
            }
            let mut consecutive_errors = 0;
            let mut buf = [0u8; HID_READ_BUF_SIZE];
            let mut last_rescan = Instant::now()
                .checked_sub(Duration::from_millis(DEVICE_RESCAN_INTERVAL_MS))
                .unwrap_or_else(Instant::now);
            #[cfg(debug_assertions)]
            let mut last_debug_render = Instant::now()
                .checked_sub(Duration::from_millis(DEBUG_RENDER_INTERVAL_MS))
                .unwrap_or_else(Instant::now);
            #[cfg(debug_assertions)]
            let mut latest_debug_lines: Vec<String> =
                vec![format!("等待 {} 输入...", profile.name())];
            #[cfg(debug_assertions)]
            let mut poll_tracker = PollRateTracker::new();

            while !stop_clone.load(Ordering::SeqCst) {
                let mut polled_state = XGamepad::default();

                if dev.is_none()
                    && last_rescan.elapsed() >= Duration::from_millis(DEVICE_RESCAN_INTERVAL_MS)
                {
                    let _ = api.refresh_devices();
                    dev = open_sony_device(&api, profile);
                    last_rescan = Instant::now();
                    if dev.is_some() {
                        ready_clone.store(true, Ordering::SeqCst);
                    }
                }

                if let Some(ref mut hid_dev) = dev {
                    match hid_dev.read_timeout(&mut buf, HID_READ_TIMEOUT_MS) {
                        Ok(n) => {
                            if n > 0
                                && let Some(parsed) =
                                    parse_sony_report_to_xgamepad(profile, &buf[..n])
                            {
                                polled_state = parsed;
                                #[cfg(debug_assertions)]
                                {
                                    poll_tracker.on_report(Instant::now());
                                    latest_debug_lines = with_poll_line(
                                        sony_debug_lines(profile, &buf[..n]),
                                        &poll_tracker,
                                    );
                                }
                            }
                        }
                        Err(e) => {
                            log_error(&format!("手柄读取 - HID 读取失败: {}", e));
                            dev = None;
                            last_rescan = Instant::now();
                            #[cfg(debug_assertions)]
                            {
                                latest_debug_lines = with_poll_line(
                                    vec![format!(
                                        "{} 读取失败，等待重连（每1秒重扫）",
                                        profile.name()
                                    )],
                                    &poll_tracker,
                                );
                            }
                        }
                    }
                } else {
                    #[cfg(debug_assertions)]
                    {
                        latest_debug_lines = with_poll_line(
                            vec![format!(
                                "未检测到 {}，等待连接（每1秒重扫）",
                                profile.name()
                            )],
                            &poll_tracker,
                        );
                    }
                }

                match state_clone.lock() {
                    Ok(mut lock) => {
                        *lock = polled_state;
                        consecutive_errors = 0;
                    }
                    Err(e) => {
                        log_error(&format!("手柄读取 - 获取状态锁失败: {:?}", e));
                        consecutive_errors += 1;
                    }
                }

                if consecutive_errors >= GAMEPAD_READING_MAX_CONSECUTIVE_ERRORS {
                    log_error(&format!(
                        "手柄读取 - 连续错误超过{}次，设置错误标志",
                        GAMEPAD_READING_MAX_CONSECUTIVE_ERRORS
                    ));
                    error_flag_clone.store(true, Ordering::SeqCst);
                    break;
                }
                #[cfg(debug_assertions)]
                render_debug_panel(
                    &format!("Gamepad Reader Debug | {}", profile.name()),
                    &latest_debug_lines,
                    &mut last_debug_render,
                );
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