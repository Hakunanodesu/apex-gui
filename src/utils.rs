use std::fs;
use crate::shared_constants::paths::{CONFIGS_DIR, CURRENT_CONFIG_FILE};

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
}

/// 手柄轴映射（SDL axis index -> 逻辑轴，None 表示未配置）
#[derive(serde::Deserialize, serde::Serialize, Clone, Debug)]
pub struct ConMappingAxis {
    pub lx: Option<u8>,
    pub ly: Option<u8>,
    pub rx: Option<u8>,
    pub ry: Option<u8>,
    pub lt: Option<u8>,
    pub rt: Option<u8>,
}

impl Default for ConMappingAxis {
    fn default() -> Self {
        Self { lx: None, ly: None, rx: None, ry: None, lt: None, rt: None }
    }
}

/// 手柄按键映射（SDL button index -> 逻辑键，None 表示未配置）
#[derive(serde::Deserialize, serde::Serialize, Clone, Debug)]
pub struct ConMappingButton {
    pub lb: Option<u8>,
    pub rb: Option<u8>,
    pub ls: Option<u8>,
    pub rs: Option<u8>,
    pub back: Option<u8>,
    pub start: Option<u8>,
    pub x: Option<u8>,
    pub y: Option<u8>,
    pub a: Option<u8>,
    pub b: Option<u8>,
}

impl Default for ConMappingButton {
    fn default() -> Self {
        Self {
            lb: None, rb: None, ls: None, rs: None,
            back: None, start: None, x: None, y: None, a: None, b: None,
        }
    }
}

/// 手柄键位映射（调试窗口内容，保存到配置；任意为空则不允许启动智慧核心）
#[derive(serde::Deserialize, serde::Serialize, Clone, Debug, Default)]
pub struct ConMapping {
    #[serde(default)]
    pub axis: ConMappingAxis,
    #[serde(default)]
    pub button: ConMappingButton,
}

impl ConMapping {
    /// 是否全部已配置（无空值）才允许启动智慧核心
    pub fn is_complete(&self) -> bool {
        let a = &self.axis;
        let b = &self.button;
        a.lx.is_some() && a.ly.is_some() && a.rx.is_some() && a.ry.is_some()
            && a.lt.is_some() && a.rt.is_some()
            && b.lb.is_some() && b.rb.is_some() && b.ls.is_some() && b.rs.is_some()
            && b.back.is_some() && b.start.is_some()
            && b.x.is_some() && b.y.is_some() && b.a.is_some() && b.b.is_some()
    }
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
    pub con_mapping: Option<ConMapping>,
    #[serde(default)]
    pub rapid_fire_mode: String,
    /// 内圈递增曲线模式：linear / ease-in / ease-in-out
    #[serde(default = "default_inner_ramp_curve")]
    pub inner_ramp_curve: String,
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

fn default_inner_ramp_curve() -> String {
    "ease-in-out".to_string()
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
}

