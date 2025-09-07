use std::env;
use std::path::{PathBuf, Path};
use hidapi::HidApi;
use egui::{Ui, TextStyle};

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
/// - bool: 操作是否成功
pub fn enumerate_controllers() -> bool {
    // 获取 HID 设备实例
    let hid_instances = get_hid_instance();
    
    // 如果没有找到任何 HID 设备，返回 false
    if hid_instances.is_empty() {
        return false;
    }
    
    // 隐藏所有通过 get_hid_instance() 获取到的设备
    for path in hid_instances {
        // 这里根据你的 run_hidhidecli 签名调整参数传递
        run_hidhidecli(&["--dev-hide", &path]).unwrap();
    }
    reenumerate();

    true
}

/// 检查是否存在 VID = "239A"，PID = "80F4" 的设备（Pico）
/// 返回 true 表示存在，false 表示不存在
pub fn enumerate_pico() -> bool {
    use hidapi::HidApi;

    // 初始化 HIDAPI
    let api = match HidApi::new() {
        Ok(api) => api,
        Err(_) => return false,
    };

    // 目标 VID/PID
    let target_vid = 0x239A;
    let target_pid = 0x80F4;

    // 遍历所有设备
    for device in api.device_list() {
        if device.vendor_id() == target_vid && device.product_id() == target_pid {
            return true;
        }
    }
    false
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

/// 枚举models目录下的所有ONNX文件
/// 返回文件名列表（不包含路径）
pub fn enumerate_onnx_files() -> Vec<String> {
    let current_dir = match env::current_dir() {
        Ok(dir) => dir,
        Err(_) => return Vec::new(),
    };

    let models_dir = current_dir.join("models");
    let mut onnx_files = Vec::new();
    
    if let Ok(entries) = std::fs::read_dir(&models_dir) {
        for entry in entries {
            if let Ok(entry) = entry {
                let path = entry.path();
                if path.is_file() {
                    if let Some(extension) = path.extension() {
                        if extension.to_string_lossy().to_lowercase() == "onnx" {
                            if let Some(file_name) = path.file_name() {
                                onnx_files.push(file_name.to_string_lossy().to_string());
                            }
                        }
                    }
                }
            }
        }
    }

    // 按文件名排序
    onnx_files.sort();
    onnx_files
}