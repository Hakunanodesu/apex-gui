use serde::{Serialize, Deserialize};
use std::fs;
use std::collections::HashMap;
use std::sync::{Arc, Mutex};

#[derive(Serialize, Deserialize, Clone)]
#[serde(default)]
pub struct DeviceConfig {
    pub outer_size: String,
    pub mid_size: String,
    pub inner_size: String,
    pub outer_str: String,
    pub inner_str: String,
    pub init_str: String,
    pub hipfire: String,
    pub vertical_str: String,
    pub aim_height: String,
}

#[derive(Serialize, Deserialize, Clone)]
#[serde(default)]
pub struct UserConfig {
    pub current_profile: String,    // 当前选择的配置
    pub current_device: String,  // 当前选择的设备类型 ("mouse" 或 "controller")
    pub current_model: String,   // 当前选择的模型文件名
    pub profiles: HashMap<String, HashMap<String, DeviceConfig>>, // profile -> device_type -> config
}

impl DeviceConfig {
    pub fn default_mouse() -> Self {
        Self {
            outer_size: "320".to_string(),
            mid_size: "80".to_string(),
            inner_size: "40".to_string(),
            outer_str: "0.1".to_string(),
            inner_str: "0.2".to_string(),
            init_str: "0.0".to_string(),
            hipfire: "2.0".to_string(),
            vertical_str: "0.4".to_string(),
            aim_height: "0.6".to_string(),
        }
    }

    pub fn default_controller() -> Self {
        Self {
            outer_size: "320".to_string(),
            mid_size: "80".to_string(),
            inner_size: "80".to_string(),
            outer_str: "0.2".to_string(),
            inner_str: "0.4".to_string(),
            init_str: "0.0".to_string(),
            hipfire: "0.6".to_string(),
            vertical_str: "0.4".to_string(),
            aim_height: "0.6".to_string(),
        }
    }
}

impl Default for DeviceConfig {
    fn default() -> Self {
        Self::default_mouse()  // 默认使用鼠标配置作为fallback
    }
}

impl Default for UserConfig {
    fn default() -> Self {
        let mut devices = HashMap::new();
        devices.insert("mouse".to_string(), DeviceConfig::default_mouse());
        devices.insert("controller".to_string(), DeviceConfig::default_controller());

        let mut profiles = HashMap::new();
        profiles.insert("apexlegends".to_string(), devices);

        Self { 
            current_profile: "apexlegends".to_string(),  // 默认选择第一个游戏
            current_device: "mouse".to_string(),
            current_model: "apexlegends.onnx".to_string(), // 默认模型
            profiles,
        }
    }
}

impl UserConfig {
    pub fn add_profile(&mut self, profile_name: String) -> bool {
        if self.profiles.contains_key(&profile_name) {
            return false;
        }

        let mut profile_config = HashMap::new();
        profile_config.insert("mouse".to_string(), DeviceConfig::default_mouse());
        profile_config.insert("controller".to_string(), DeviceConfig::default_controller());
        
        self.profiles.insert(profile_name.clone(), profile_config);
        self.current_profile = profile_name;
        true
    }

    pub fn remove_profile(&mut self, profile_name: &str) -> bool {
        if !self.profiles.contains_key(profile_name) {
            return false;
        }

        self.profiles.remove(profile_name);

        // 如果删除的是当前选中的游戏，切换到其他游戏
        if self.profiles.is_empty() {
            self.current_profile = String::new();
        } else if profile_name == self.current_profile {
            self.current_profile = self.profiles.keys().next().unwrap().clone();
        }
        true
    }
}

const CONFIG_PATH: &str = "config.json";

pub fn load_config() -> UserConfig {
    if std::path::Path::new(CONFIG_PATH).exists() {
        let data = fs::read_to_string(CONFIG_PATH).unwrap_or_default();
        serde_json::from_str(&data).unwrap_or_default()
    } else {
        UserConfig::default()
    }
}

pub fn save_config(config: &UserConfig) {
    if let Ok(data) = serde_json::to_string_pretty(config) {
        let _ = fs::write(CONFIG_PATH, data);
    }
} 

pub fn handle_config_changes(
    selected_profile: &str,
    selected_model: &str,
    current_profile: &mut String,
    current_device: &mut String,
    current_model: &mut String,
    is_mouse_mode: bool,
    outer_size: &Arc<Mutex<String>>,
    mid_size: &Arc<Mutex<String>>,
    inner_size: &Arc<Mutex<String>>,
    outer_str: &Arc<Mutex<String>>,
    inner_str: &Arc<Mutex<String>>,
    init_str: &Arc<Mutex<String>>,
    hipfire: &Arc<Mutex<String>>,
    vertical_str: &Arc<Mutex<String>>,
    aim_height: &Arc<Mutex<String>>,
) -> bool {
    let new_device = if is_mouse_mode { "mouse" } else { "controller" };
    
    // 检查是否有变化
    if selected_profile != *current_profile || new_device != *current_device || selected_model != *current_model {
        // 保存当前参数到旧的配置
        let device_config = DeviceConfig {
            outer_size: outer_size.lock().unwrap().clone(),
            mid_size: mid_size.lock().unwrap().clone(),
            inner_size: inner_size.lock().unwrap().clone(),
            outer_str: outer_str.lock().unwrap().clone(),
            inner_str: inner_str.lock().unwrap().clone(),
            init_str: init_str.lock().unwrap().clone(),
            hipfire: hipfire.lock().unwrap().clone(),
            vertical_str: vertical_str.lock().unwrap().clone(),
            aim_height: aim_height.lock().unwrap().clone(),
        };

        let mut updated_config = load_config();
        // 保存旧配置
        if let Some(profile_config) = updated_config.profiles.get_mut(current_profile) {
            profile_config.insert(current_device.clone(), device_config);
        }
        
        // 更新当前游戏、设备和模型
        updated_config.current_profile = selected_profile.to_string();
        updated_config.current_device = new_device.to_string();
        updated_config.current_model = selected_model.to_string();
        save_config(&updated_config);

        // 加载新的配置
        let user_config = load_config();
        if let Some(profile_config) = user_config.profiles.get(selected_profile) {
            if let Some(device_config) = profile_config.get(new_device) {
                *outer_size.lock().unwrap() = device_config.outer_size.clone();
                *mid_size.lock().unwrap() = device_config.mid_size.clone();
                *inner_size.lock().unwrap() = device_config.inner_size.clone();
                *outer_str.lock().unwrap() = device_config.outer_str.clone();
                *inner_str.lock().unwrap() = device_config.inner_str.clone();
                *init_str.lock().unwrap() = device_config.init_str.clone();
                *hipfire.lock().unwrap() = device_config.hipfire.clone();
                *vertical_str.lock().unwrap() = device_config.vertical_str.clone();
                *aim_height.lock().unwrap() = device_config.aim_height.clone();
            }
        }

        // 更新内存中的当前游戏、设备和模型
        *current_profile = selected_profile.to_string();
        *current_device = new_device.to_string();
        *current_model = selected_model.to_string();
        
        true // 发生了变化
    } else {
        false // 没有变化
    }
} 