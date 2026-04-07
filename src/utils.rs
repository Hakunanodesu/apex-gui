use std::fs;
use crate::shared_constants::assist_curve::{INNER_RAMP_LINEAR, INNER_RAMP_SQUARE};
use crate::shared_constants::input_device::{
    DUALSENSE as INPUT_DEVICE_DUALSENSE,
    DUALSHOCK4 as INPUT_DEVICE_DUALSHOCK4,
    XBOX as INPUT_DEVICE_XBOX,
};
use crate::shared_constants::paths::{CONFIGS_DIR, CURRENT_CONFIG_FILE};

fn default_input_device() -> String {
    INPUT_DEVICE_XBOX.to_string()
}

pub fn normalize_input_device(s: &str) -> String {
    let v = s.trim();
    if v.eq_ignore_ascii_case(INPUT_DEVICE_DUALSHOCK4) {
        INPUT_DEVICE_DUALSHOCK4.to_string()
    } else if v.eq_ignore_ascii_case(INPUT_DEVICE_DUALSENSE) {
        INPUT_DEVICE_DUALSENSE.to_string()
    } else if v.eq_ignore_ascii_case(INPUT_DEVICE_XBOX) {
        INPUT_DEVICE_XBOX.to_string()
    } else {
        INPUT_DEVICE_XBOX.to_string()
    }
}

/// 内圈插值模式：仅 `"linear"` / `"square"`（其它输入视为 linear）
pub fn normalize_inner_ramp_mode(s: &str) -> String {
    if s.trim().eq_ignore_ascii_case(INNER_RAMP_SQUARE) {
        INNER_RAMP_SQUARE.to_string()
    } else {
        INNER_RAMP_LINEAR.to_string()
    }
}

/// 当前配置信息
#[derive(serde::Deserialize, serde::Serialize)]
struct CurrentConfig {
    current_config: String,
    current_model: String,
}

/// 吸附曲线配置
#[derive(serde::Deserialize, serde::Serialize, Clone, Debug)]
pub struct AssistCurve {
    pub deadzone: f32,
    pub hipfire: f32,
    pub inner_diameter: f32,
    pub inner_strength: f32,
    pub outer_diameter: f32,
    pub outer_strength: f32,
    #[serde(default = "default_assist_output_ema_alpha")]
    pub assist_output_ema_alpha: f32,
    /// 内圈吸附插值：`"linear"` / `"square"`
    #[serde(default = "default_inner_ramp_mode_str")]
    pub inner_ramp_mode: String,
}

fn default_assist_output_ema_alpha() -> f32 {
    crate::shared_constants::aim_assist::ASSIST_OUTPUT_EMA_ALPHA
}

fn default_inner_ramp_mode_str() -> String {
    INNER_RAMP_LINEAR.to_string()
}

/// 配置文件结构
#[derive(serde::Deserialize, serde::Serialize, Clone, Debug)]
pub struct ConfigFile {
    pub aim_height_coefficient: f32,
    pub assist_curve: AssistCurve,
    pub aa_activate_mode: String,
    pub use_controller: bool,
    pub vertical_strength_coefficient: f32,
    #[serde(default = "default_input_device")]
    pub input_device: String,
    #[serde(default)]
    pub rapid_fire_mode: String,
    /// 许可证代码，首次为空字符串，用户填写后保存
    #[serde(default)]
    pub license_code: String,
    /// 特殊枪械：强制使用“瞄准和开火”模式
    #[serde(default)]
    pub special_weapons_aim_and_fire: Vec<String>,
    /// 特殊枪械：按下不开火，松手开火一次
    #[serde(default)]
    pub special_weapons_release_to_fire: Vec<String>,
}

/// 读取 configs/.current 文件，返回当前配置和模型
pub fn read_current_config() -> Option<(String, String)> {
    if let Ok(content) = fs::read_to_string(CURRENT_CONFIG_FILE) {
        if let Ok(config) = serde_json::from_str::<CurrentConfig>(&content) {
            return Some((config.current_config, config.current_model));
        }
    }
    None
}

/// 保存当前配置和模型到 .current 文件
pub fn save_current_config(config: &str, model: &str) -> Result<(), Box<dyn std::error::Error>> {
    let current = CurrentConfig {
        current_config: config.to_string(),
        current_model: model.to_string(),
    };
    let content = serde_json::to_string_pretty(&current)?;
    fs::write(CURRENT_CONFIG_FILE, content)?;
    Ok(())
}

/// 从配置文件加载配置
pub fn load_config_file(config_name: &str) -> Result<ConfigFile, Box<dyn std::error::Error>> {
    let file_path = format!("{}/{}.json", CONFIGS_DIR, config_name);
    let content = fs::read_to_string(&file_path)?;
    let config: ConfigFile = serde_json::from_str(&content)?;
    Ok(config)
}

/// 保存配置到文件
pub fn save_config_file(config_name: &str, config: &ConfigFile) -> Result<(), Box<dyn std::error::Error>> {
    let file_path = format!("{}/{}.json", CONFIGS_DIR, config_name);
    let content = serde_json::to_string_pretty(config)?;
    fs::write(&file_path, content)?;
    Ok(())
}

/// 搜索指定文件夹下的所有 .json 文件，返回文件名列表（不含扩展名）
pub fn find_json_files(folder: &str) -> Vec<String> {
    let mut files = Vec::new();
    
    if let Ok(entries) = fs::read_dir(folder) {
        for entry in entries {
            if let Ok(entry) = entry {
                let path = entry.path();
                if path.is_file() {
                    if let Some(extension) = path.extension() {
                        if extension == "json" {
                            if let Some(file_name) = path.file_stem() {
                                if let Some(name) = file_name.to_str() {
                                    files.push(name.to_string());
                                }
                            }
                        }
                    }
                }
            }
        }
    }
    
    // 按文件名排序
    files.sort();
    files
}

/// 搜索指定文件夹下的所有 .onnx 文件，返回文件名列表（不含扩展名）
pub fn find_onnx_files(folder: &str) -> Vec<String> {
    let mut files = Vec::new();
    
    if let Ok(entries) = fs::read_dir(folder) {
        for entry in entries {
            if let Ok(entry) = entry {
                let path = entry.path();
                if path.is_file() {
                    if let Some(extension) = path.extension() {
                        if extension.eq_ignore_ascii_case("onnx") {
                            if let Some(file_name) = path.file_stem() {
                                if let Some(name) = file_name.to_str() {
                                    files.push(name.to_string());
                                }
                            }
                        }
                    }
                }
            }
        }
    }
    
    // 按文件名排序
    files.sort();
    files
}

/// 获取屏幕高度（像素）
pub fn get_screen_height() -> f32 {
    use winapi::um::winuser::{GetSystemMetrics, SM_CYSCREEN};
    unsafe {
        GetSystemMetrics(SM_CYSCREEN) as f32
    }
}

/// 检测指定路径的文件夹是否存在
pub fn dir_exists(path: &str) -> bool {
    std::path::Path::new(path).exists()
}

pub mod console_redirect {
    use chrono::{Duration, Local, NaiveDate};
    use std::fs::{self, OpenOptions};
    use std::io::Write;
    use std::path::Path;
    use std::sync::{Mutex, Once, OnceLock};

    const LOG_RETENTION_DAYS: i64 = 30;
    static LOG_LOCK: OnceLock<Mutex<()>> = OnceLock::new();
    static LOG_INIT: Once = Once::new();
    static RUN_ID: OnceLock<String> = OnceLock::new();

    fn logs_dir() -> Option<std::path::PathBuf> {
        std::env::current_dir().ok().map(|cwd| cwd.join("logs"))
    }

    fn cleanup_old_logs(dir: &Path, today: NaiveDate) {
        let cutoff = today - Duration::days(LOG_RETENTION_DAYS);
        let entries = match fs::read_dir(dir) {
            Ok(entries) => entries,
            Err(_) => return,
        };

        for entry in entries.flatten() {
            let path = entry.path();
            if path.extension().and_then(|s| s.to_str()) != Some("log") {
                continue;
            }
            let date_str = match path.file_stem().and_then(|s| s.to_str()) {
                Some(s) => s,
                None => continue,
            };
            let file_date = match NaiveDate::parse_from_str(date_str, "%Y-%m-%d") {
                Ok(d) => d,
                Err(_) => continue,
            };
            if file_date < cutoff {
                let _ = fs::remove_file(path);
            }
        }
    }

    fn run_id() -> &'static str {
        RUN_ID.get_or_init(|| {
            let now = Local::now().format("%Y%m%d-%H%M%S");
            let pid = std::process::id();
            format!("{now}-{pid}")
        })
    }

    fn write_log_line(level: &str, message: &str) {
        let now = Local::now();
        let today = now.date_naive();
        let timestamp = now.format("%Y-%m-%d %H:%M:%S");

        LOG_INIT.call_once(|| {
            if let Some(dir) = logs_dir() {
                let _ = fs::create_dir_all(&dir);
                cleanup_old_logs(&dir, today);
            }
        });

        if let Some(dir) = logs_dir() {
            let lock = LOG_LOCK.get_or_init(|| Mutex::new(()));
            if let Ok(_guard) = lock.lock() {
                let _ = fs::create_dir_all(&dir);
                let file_path = dir.join(format!("{}.log", today.format("%Y-%m-%d")));
                if let Ok(mut file) = OpenOptions::new().create(true).append(true).open(file_path) {
                    let _ = writeln!(file, "[{timestamp}] [{level}] [RUN:{}] {message}", run_id());
                }
            }
        }
    }

    pub fn log_error(message: &str) {
        write_log_line("ERROR", message);
        eprintln!("{message}");
    }

    pub fn log_info(message: &str) {
        write_log_line("INFO", message);
        println!("{message}");
    }
}

pub mod controller_probe {
    use hidapi::{DeviceInfo, HidApi};
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
    use crate::shared_constants::xinput::SLOT_COUNT as XINPUT_SLOT_COUNT;

    /// ViGEm 虚拟 Xbox 约定占用 **XInput 用户索引 0**；物理手柄从索引 1 起参与检测与读取。
    pub const VIRTUAL_XINPUT_USER_INDEX: u32 = 0;

    fn is_xinput_controller_connected(user_index: u32) -> bool {
        use windows_sys::Win32::Foundation::ERROR_SUCCESS;
        use windows_sys::Win32::UI::Input::XboxController::{XINPUT_STATE, XInputGetState};

        let mut state: XINPUT_STATE = unsafe { std::mem::zeroed() };
        unsafe { XInputGetState(user_index, &mut state) == ERROR_SUCCESS }
    }

    /// 第一个已连接的**物理**手柄槽位（跳过索引 0 的虚拟 ViGEm）。
    pub fn first_physical_xinput_slot() -> Option<u32> {
        (VIRTUAL_XINPUT_USER_INDEX + 1..XINPUT_SLOT_COUNT).find(|&slot| {
            is_xinput_controller_connected(slot)
        })
    }

    /// 是否存在物理 Xbox/XInput 设备（仅检查索引 1..4，不含 0 号虚拟槽位）。
    fn has_physical_xinput_controller() -> bool {
        (VIRTUAL_XINPUT_USER_INDEX + 1..XINPUT_SLOT_COUNT)
            .any(|slot| is_xinput_controller_connected(slot))
    }

    fn is_dualsense_hid_device(d: &DeviceInfo) -> bool {
        if d.vendor_id() != SONY_VID {
            return false;
        }
        if DUALSENSE_PIDS.contains(&d.product_id()) {
            return true;
        }
        d.product_string()
            .is_some_and(|s| s.to_ascii_lowercase().contains("dualsense"))
    }

    fn is_dualshock4_hid_device(d: &DeviceInfo) -> bool {
        if d.vendor_id() != SONY_VID {
            return false;
        }
        if DUALSHOCK4_PIDS.contains(&d.product_id()) {
            return true;
        }
        d.product_string().is_some_and(|s| {
            let s = s.to_ascii_lowercase();
            s.contains("dualshock") || s.contains("wireless controller")
        })
    }

    fn has_matching_sony_hid_controller(match_fn: impl Fn(&DeviceInfo) -> bool) -> bool {
        HidApi::new()
            .ok()
            .is_some_and(|api| api.device_list().any(match_fn))
    }

    /// 根据输入设备类型检查对应物理手柄是否存在。
    pub fn has_physical_controller_for_input_device(input_device: &str) -> bool {
        let v = input_device.trim();
        if v.eq_ignore_ascii_case(INPUT_DEVICE_XBOX) {
            return has_physical_xinput_controller();
        }
        if v.eq_ignore_ascii_case(INPUT_DEVICE_DUALSENSE) {
            return has_matching_sony_hid_controller(is_dualsense_hid_device);
        }
        if v.eq_ignore_ascii_case(INPUT_DEVICE_DUALSHOCK4) {
            return has_matching_sony_hid_controller(is_dualshock4_hid_device);
        }
        false
    }
}

pub mod monotonic_clock {
    use std::sync::OnceLock;
    use std::time::Instant;

    static START: OnceLock<Instant> = OnceLock::new();

    /// 以进程启动后为基准的单调时钟（纳秒）。
    pub fn now_ns() -> u64 {
        START
            .get_or_init(Instant::now)
            .elapsed()
            .as_nanos()
            .min(u64::MAX as u128) as u64
    }
}

#[cfg(debug_assertions)]
pub mod debug_perf_panel {
    use std::sync::{Mutex, OnceLock};

    static MAPPER_LINE: OnceLock<Mutex<String>> = OnceLock::new();

    pub fn set_mapper_perf_line(line: String) {
        if let Ok(mut lock) = MAPPER_LINE.get_or_init(|| Mutex::new(String::new())).lock() {
            *lock = line;
        }
    }

    pub fn mapper_perf_line() -> String {
        MAPPER_LINE
            .get_or_init(|| Mutex::new(String::new()))
            .lock()
            .map(|s| s.clone())
            .unwrap_or_default()
    }
}

