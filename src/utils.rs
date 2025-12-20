use std::fs;

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
    pub middle_diameter: f32,
    pub outer_diameter: f32,
    pub outer_strength: f32,
}

/// 配置文件结构
#[derive(serde::Deserialize, serde::Serialize, Clone, Debug)]
pub struct ConfigFile {
    pub aim_height_coefficient: f32,
    pub assist_curve: AssistCurve,
    pub aa_activate_mode: String,
    pub use_controller: bool,
    pub ps_mode: bool,
    pub vertical_strength_coefficient: f32,
}

/// 读取 configs/.current 文件，返回当前配置和模型
pub fn read_current_config() -> Option<(String, String)> {
    if let Ok(content) = fs::read_to_string("configs/.current") {
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
    fs::write("configs/.current", content)?;
    Ok(())
}

/// 从配置文件加载配置
pub fn load_config_file(config_name: &str) -> Result<ConfigFile, Box<dyn std::error::Error>> {
    let file_path = format!("configs/{}.json", config_name);
    let content = fs::read_to_string(&file_path)?;
    let config: ConfigFile = serde_json::from_str(&content)?;
    Ok(config)
}

/// 保存配置到文件
pub fn save_config_file(config_name: &str, config: &ConfigFile) -> Result<(), Box<dyn std::error::Error>> {
    let file_path = format!("configs/{}.json", config_name);
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
    use serialport::SerialPortType;

    const PICO_VID: u16 = 0x239A;
    const PICO_PID: u16 = 0x80F4;

    pub fn enumerate_controllers() -> bool {
        if let Ok(ctx) = sdl2::init() {
            if let Ok(joystick) = ctx.joystick() {
                let count = joystick.num_joysticks().unwrap_or(0);
                if count <= 1 {
                    return false;
                }
                for index in 1..count {
                    if joystick.name_for_index(index as u32).is_ok() {
                        return true;
                    }
                }
            }
        }
        false
    }

    pub fn enumerate_pico() -> bool {
        serialport::available_ports()
            .map(|ports| {
                ports.iter().any(|port| {
                    matches!(
                        &port.port_type,
                        SerialPortType::UsbPort(info)
                            if info.vid == PICO_VID && info.pid == PICO_PID
                    )
                })
            })
            .unwrap_or(false)
    }
}

