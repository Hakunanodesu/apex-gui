use std::fs;
use crate::shared_constants::assist_curve::{INNER_RAMP_LINEAR, INNER_RAMP_SQUARE};
use crate::shared_constants::paths::{CONFIGS_DIR, CURRENT_CONFIG_FILE};

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
pub fn check_dir_exist(path: &str) -> bool {
    std::path::Path::new(path).exists()
}

pub mod console_redirect {
    pub fn log_error(message: &str) {
        eprintln!("{message}");
    }
}

pub mod enum_device_tool {
    fn is_virtual_controller_name(name: &str) -> bool {
        let lower = name.to_ascii_lowercase();
        lower.contains("vigem")
            || lower.contains("virtual")
            || lower.contains("vgamepad")
            || lower.contains("xbox 360 controller for windows")
    }

    /// 枚举可用于输入的物理手柄设备（返回 SDL 设备索引 与 显示名）
    pub fn enumerate_controller_devices() -> Vec<(u32, String)> {
        let mut devices = Vec::new();
        if let Ok(ctx) = sdl2::init() {
            if let Ok(joystick) = ctx.joystick() {
                let count = joystick.num_joysticks().unwrap_or(0);
                for index in 0..count {
                    let index_u32 = index as u32;
                    if let Ok(name) = joystick.name_for_index(index_u32) {
                        if !is_virtual_controller_name(&name) {
                            devices.push((index_u32, name));
                        }
                    }
                }
            }
        }
        devices
    }

    pub fn enumerate_controllers() -> bool {
        !enumerate_controller_devices().is_empty()
    }
}

