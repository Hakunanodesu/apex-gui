use std::env;
use std::path::{PathBuf, Path};
use std::error::Error;
use sdl2::joystick::Joystick;
use hidapi::HidApi;
use egui::{Ui, TextStyle};
use serde::{Serialize, Deserialize};
use std::fs;

#[derive(Serialize, Deserialize, Default)]
pub struct UserConfig {
    pub outer_size: String,
    pub mid_size: String,
    pub inner_size: String,
    pub outer_str: String,
    pub mid_str: String,
    pub inner_str: String,
    pub deadzone: Option<String>,
    pub hipfire: Option<String>,
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

use crate::modules::hidhide::run_hidhidecli;
use crate::utils::ps_con_reenable::reenumerate;

/// 返回所有 vendor ID 为指定值的 HID 设备实例列表
pub fn get_hid_instance() -> Vec<String> {
    // 初始化 HID API
    let api = HidApi::new().expect("初始化 HidApi 失败");
    let mut results = Vec::new();

    // 遍历所有设备，匹配 vendor_id
    for device in api.device_list() {
        if device.vendor_id() == 0x054c 
            || device.vendor_id() == 0x045e 
        {
            let raw = device.path().to_string_lossy();
            // 拆分
            let parts: Vec<&str> = raw.split('#').collect();

            // 取索引 1 和 2，再组合
            let combined = format!("HID\\{}", parts[1..3].join("\\"));
            results.push(combined);
        }
    }

    results
}

/// 返回当前可执行文件的完整路径（包括文件名），不对路径做任何父级目录提取
pub fn get_exe_path() -> std::io::Result<PathBuf> {
    env::current_exe()
}

pub fn driver_path_exist(target: &str) -> bool {
    let path = format!(
        "{}{}", 
        r"C:\Program Files\Nefarius Software Solutions\", 
        target
    );
    Path::new(&path).is_dir()
}

/// 同步枚举所有 XInput 手柄，并隐藏指定设备。
/// 
/// # 返回
/// - Vec<String>: 手柄名称列表，格式为 `"name (id)"`
/// - Vec<GamepadId>: 对应的 gilrs `GamepadId` 列表
pub fn enumerate_controllers() -> Result<Vec<String>, Box<dyn Error>> {
    // 初始化 SDL
    let sdl_ctx = sdl2::init()?;
    // 打开原始 Joystick 子系统
    let js = sdl_ctx.joystick()?;

    let count = js.num_joysticks()?;
    let mut name_list = Vec::with_capacity(count as usize);

    // println!("检测到以下手柄：");
    for id in 0..count {
        let joystick: Joystick = js.open(id)?;
        let name = joystick.name();
        name_list.push(format!("{} ({})", name, id));
        // println!("{} ({})", name, id);
    }

    // 隐藏所有通过 get_hid_instance() 获取到的设备
    for path in get_hid_instance() {
        // 这里根据你的 run_hidhidecli 签名调整参数传递
        run_hidhidecli(&["--dev-hide", &path]).unwrap();
    }
    reenumerate();

    Ok(name_list)
}

pub fn get_text_width(ui: &Ui, text: impl Into<String>, text_style: TextStyle) -> f32 {
    // 获取对应的 FontId
    let font_id = text_style.resolve(ui.style());
    // 当前主题下文字颜色
    let color = ui.visuals().text_color();
    // 只做排版，不会绘制／占位
    let galley = ui.painter().layout_no_wrap(text.into(), font_id, color);
    galley.size().x
}