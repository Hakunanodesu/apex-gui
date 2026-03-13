#![cfg_attr(not(debug_assertions), windows_subsystem = "windows")]

use eframe::egui;
use std::sync::{Arc, Mutex};
use vigem_client::{Client, Xbox360Wired};
use serde::Deserialize;
use std::path::Path;

mod utils;
mod modules;
use utils::{find_json_files, find_onnx_files, read_current_config, get_screen_height, load_config_file, save_config_file, save_current_config, ConfigFile, ConMapping, check_dir_exist};
use utils::enum_device_tool::enumerate_controllers;
use modules::mapping_state_machine::MappingManager;
use std::sync::atomic::{AtomicBool, Ordering};

const CHARACTER_WIDTH: f32 = 12.0; // 英文字体宽度为 CHARACTER_WIDTH * 0.6
const SPACING: f32 = 8.0;
const ROW_HEIGHT: f32 = 18.0; // separator 的高度为 ROW_HEIGHT / 3.0

// 颜色常量
const GREEN: egui::Color32 = egui::Color32::from_rgb(41, 157, 143);
const YELLOW: egui::Color32 = egui::Color32::from_rgb(233, 196, 106);
const RED: egui::Color32 = egui::Color32::from_rgb(216, 118, 89);

fn main() -> Result<(), eframe::Error> {
    let options = eframe::NativeOptions {
        viewport: egui::ViewportBuilder::default()
            .with_inner_size([
                CHARACTER_WIDTH * 32.0, 
                ROW_HEIGHT * 13.0 + ROW_HEIGHT / 3.0 * 7.0 + SPACING * 21.0
            ])
            .with_resizable(false)
            .with_maximize_button(false), // 禁用最大化按钮
        ..Default::default()
    };
    eframe::run_native(
        "apex-egui",
        options,
        Box::new(|cc| {
            // 加载字体
            setup_fonts(&cc.egui_ctx);
            
            cc.egui_ctx.style_mut(|style| {
                // 设置全局字体大小
                for (_text_style, font_id) in style.text_styles.iter_mut() {
                    font_id.size = CHARACTER_WIDTH; // 全局字号
                }

                // 设置间距
                style.spacing.item_spacing.x = SPACING;
                style.spacing.item_spacing.y = SPACING;
            });
            
            // 创建应用实例，屏幕高度将在第一次 update 时获取
            Box::new(MyApp::default())
        }),
    )
}

fn modal_blocker(ctx: &egui::Context) {
    let screen_rect = ctx.screen_rect();
    egui::Area::new(egui::Id::new("modal_blocker"))
        .order(egui::Order::Middle) // 使用 Middle 层，Window 会在 Foreground 层之上
        .fixed_pos(screen_rect.min)
        .show(ctx, |ui| {
            ui.set_min_size(screen_rect.size());
            let rect = ui.max_rect();
            // 半透明遮罩
            ui.painter().rect_filled(
                rect,
                0.0,
                egui::Color32::from_black_alpha(128),
            );
            // 核心：吃掉所有鼠标事件
            ui.allocate_rect(rect, egui::Sense::click_and_drag());
        });
}

fn setup_fonts(ctx: &egui::Context) {
    let mut fonts = egui::FontDefinitions::default();
    
    // 使用 include_bytes! 编译时嵌入英文字体文件 JetBrainsMono
    let jetbrains_font_data = include_bytes!("../fonts/JetBrainsMono-Regular.ttf");
    fonts.font_data.insert(
        "jetbrains_mono".to_owned(),
        egui::FontData::from_owned(jetbrains_font_data.to_vec()),
    );

    // 使用 include_bytes! 编译时嵌入中文字体文件
    let noto_font_data = include_bytes!("../fonts/NotoSansCJKsc-Regular.otf");
    fonts.font_data.insert(
        "noto_cjk".to_owned(),
        egui::FontData::from_owned(noto_font_data.to_vec()),
    );
    
    // 设置字体族顺序：英文字体优先，中文字体作为回退
    if let Some(fonts_in_family) = fonts.families.get_mut(&egui::FontFamily::Proportional) {
        // 清空默认字体，添加自定义字体
        fonts_in_family.clear();
        // 英文字体在前（优先使用）
        if fonts.font_data.contains_key("jetbrains_mono") {
            fonts_in_family.push("jetbrains_mono".to_owned());
        }
        // 中文字体在后（作为中文回退）
        if fonts.font_data.contains_key("noto_cjk") {
            fonts_in_family.push("noto_cjk".to_owned());
        }
    }
    
    ctx.set_fonts(fonts);
}

#[derive(PartialEq, Clone, Copy)]
enum ParamTab {
    SnapFeature,
    InputDevice,
    Accessibility,
}

struct MyApp {
    // 配置数据
    config_selected: String,
    config_items: Vec<String>,
    config_changed: bool, // 配置是否被修改
    
    // 模型数据
    model_selected: String,
    model_items: Vec<String>,
    
    // 删除确认对话框
    delete_config_confirm: Option<(String, String)>, // (文件名, 文件夹路径)
    
    // 添加配置对话框
    add_config_dialog: Option<String>, // 输入的文件名
    
    // 输入设备选择
    use_controller: bool, // 是否使用手柄
    rt_rapid_fire: Arc<AtomicBool>, // 右扳机连点开关（跨线程共享）
    
    // 吸附方式选择
    aa_activate_mode_selected: String,
    aa_activate_mode_items: Vec<String>,
    
    // 屏幕高度（在应用启动时获取）
    screen_height: f32,
    // 当前模型要求的最小外圈直径（来自模型 json 的 size）
    min_outer_diameter: f32,
    
    // 吸附曲线设定
    outer_diameter: f32, // 外圈直径
    outer_strength: f32, // 外圈强度
    middle_diameter: f32, // 中圈直径
    inner_diameter: f32, // 内圈直径
    inner_strength: f32, // 内圈强度
    
    // 其他吸附参数
    start_strength: f32, // 起始吸附强度
    hipfire_strength_factor: f32, // 腰射吸附系数
    vertical_strength_factor: f32, // 垂直吸附系数
    aim_height_factor: f32, // 瞄准高度系数

    // 许可证
    license_key: String,
    
    // 参数设定 tab
    param_tab: ParamTab,
    
    // 预览窗口
    show_preview: bool, // 是否显示预览窗口
    preview_window_created: bool, // 预览窗口是否已创建
    
    // 问号按钮窗口
    show_help_window: bool, // 是否显示帮助窗口
    help_window_vigem_ready: Option<bool>, // ViGEm Bus Driver 检测结果
    help_window_controller_ready: Option<bool>, // 物理手柄检测结果
    
    // 虚拟手柄
    virtual_gamepad: Arc<Mutex<Option<Xbox360Wired<Arc<Client>>>>>, // ViGEmBus 虚拟手柄
    
    // 智慧核心 - 使用 MappingManager 管理
    core_enabled: bool, // 智慧核心是否开启（UI状态）
    mapping_manager: MappingManager, // 状态机管理器
    aim_enable: Arc<AtomicBool>, // 瞄准辅助开关
    // 参数共享（字符串格式，供 MappingManager 使用）
    outer_size: Arc<Mutex<String>>,
    mid_size: Arc<Mutex<String>>,
    inner_size: Arc<Mutex<String>>,
    outer_str: Arc<Mutex<String>>,
    inner_str: Arc<Mutex<String>>,
    init_str: Arc<Mutex<String>>,
    hipfire: Arc<Mutex<String>>,
    vertical_str: Arc<Mutex<String>>,
    aim_height: Arc<Mutex<String>>,
    
    // 设备检测状态
    con_exist: bool, // 物理手柄是否存在
    
    // 推理预览窗口
    show_inference_preview: bool, // 是否显示推理预览窗口
    inference_preview_window_created: bool, // 推理预览窗口是否已创建
    preview_allowed: bool, // 预览是否允许显示（需要智慧核心运行）

    // 调试输出
    debug_enabled: bool,
    show_debug_window: bool, // 手柄键位调试窗口
    // 手柄键位调试窗口输入框
    debug_axis_lx: String,
    debug_axis_ly: String,
    debug_axis_rx: String,
    debug_axis_ry: String,
    debug_axis_lt: String,
    debug_axis_rt: String,
    debug_btn_lb: String,
    debug_btn_rb: String,
    debug_btn_ls: String,
    debug_btn_rs: String,
    debug_btn_back: String,
    debug_btn_start: String,
    debug_btn_x: String,
    debug_btn_y: String,
    debug_btn_a: String,
    debug_btn_b: String,
}

impl Default for MyApp {
    fn default() -> Self {
        // 先加载文件列表
        let config_items = find_json_files("configs");
        let model_items = find_onnx_files("models");
        
        let mut config_selected = String::new();
        let mut model_selected = String::new();
        
        // 读取 .current 文件，设置初始选中的配置和模型
        if let Some((config, model)) = read_current_config() {
            // 只有当配置在文件列表中存在时才设置
            if config_items.contains(&config) {
                config_selected = config;
            }
            // 只有当模型在文件列表中存在时才设置
            if model_items.contains(&model) {
                model_selected = model;
            }
        }
        
        let screen_height = get_screen_height();
        
        // 初始化参数共享结构
        let aim_enable = Arc::new(AtomicBool::new(false));
        let rt_rapid_fire = Arc::new(AtomicBool::new(false));
        let outer_size = Arc::new(Mutex::new(String::new()));
        let mid_size = Arc::new(Mutex::new(String::new()));
        let inner_size = Arc::new(Mutex::new(String::new()));
        let outer_str = Arc::new(Mutex::new(String::new()));
        let inner_str = Arc::new(Mutex::new(String::new()));
        let init_str = Arc::new(Mutex::new(String::new()));
        let hipfire = Arc::new(Mutex::new(String::new()));
        let vertical_str = Arc::new(Mutex::new(String::new()));
        let aim_height = Arc::new(Mutex::new(String::new()));
        
        // 创建 MappingManager
        let mapping_manager = MappingManager::new(
            model_selected.clone(),
            aim_enable.clone(),
            outer_size.clone(),
            mid_size.clone(),
            inner_size.clone(),
            outer_str.clone(),
            inner_str.clone(),
            init_str.clone(),
            hipfire.clone(),
            vertical_str.clone(),
            aim_height.clone(),
            rt_rapid_fire.clone(),
        );
        
        let mut app = Self {
            config_selected: config_selected.clone(),
            config_items,
            config_changed: false,
            model_selected,
            model_items,
            delete_config_confirm: None,
            add_config_dialog: None,
            use_controller: false, // 默认不使用手柄
            rt_rapid_fire,
            aa_activate_mode_selected: String::new(),
            aa_activate_mode_items: vec!["瞄准和开火".to_string(), "仅开火".to_string()],
            screen_height,
            min_outer_diameter: 0.0,
            outer_diameter: 0.0,
            outer_strength: 0.0,
            middle_diameter: 0.0,
            inner_diameter: 0.0,
            inner_strength: 0.0,
            start_strength: 0.0,
            hipfire_strength_factor: 0.0,
            vertical_strength_factor: 0.0,
            aim_height_factor: 0.0,
            license_key: String::new(),
            param_tab: ParamTab::SnapFeature,
            show_preview: false,
            preview_window_created: false,
            show_help_window: false,
            help_window_vigem_ready: None,
            help_window_controller_ready: None,
            virtual_gamepad: Arc::new(Mutex::new(None)),
            core_enabled: false,
            mapping_manager,
            aim_enable,
            outer_size,
            mid_size,
            inner_size,
            outer_str,
            inner_str,
            init_str,
            hipfire,
            vertical_str,
            aim_height,
            con_exist: enumerate_controllers(),
            show_inference_preview: false,
            inference_preview_window_created: false,
            preview_allowed: false,
            debug_enabled: false,
            show_debug_window: false,
            debug_axis_lx: String::new(),
            debug_axis_ly: String::new(),
            debug_axis_rx: String::new(),
            debug_axis_ry: String::new(),
            debug_axis_lt: String::new(),
            debug_axis_rt: String::new(),
            debug_btn_lb: String::new(),
            debug_btn_rb: String::new(),
            debug_btn_ls: String::new(),
            debug_btn_rs: String::new(),
            debug_btn_back: String::new(),
            debug_btn_start: String::new(),
            debug_btn_x: String::new(),
            debug_btn_y: String::new(),
            debug_btn_a: String::new(),
            debug_btn_b: String::new(),
        };
        
        // 根据当前模型加载最小外圈直径（来自模型 json 的 size）
        app.update_min_outer_from_model();

        // 软件打开时创建 vigembus 虚拟手柄
        if let Ok(client) = Client::connect() {
            let client = Arc::new(client);
            let mut target = Xbox360Wired::new(client.clone(), vigem_client::TargetId::XBOX360_WIRED);
            if target.plugin().is_ok() {
                if let Ok(mut vg) = app.virtual_gamepad.lock() {
                    *vg = Some(target);
                }
            }
        }
        
        // 如果选择了配置，加载配置
        if !config_selected.is_empty() {
            if let Ok(config) = load_config_file(&config_selected) {
                app.load_config(&config);
            }
        }
        
        app
    }
}

impl MyApp {
    /// 根据当前选中的模型，更新最小外圈直径（来自 models/<model>.json 的 size 字段）
    fn update_min_outer_from_model(&mut self) {
        // 如果没有选择模型，则不限制最小值
        if self.model_selected.is_empty() {
            self.min_outer_diameter = 0.0;
            return;
        }

        // 模型文件一般为 models/foo.onnx，对应的 json 为 models/foo.json
        let models_dir = Path::new("models");
        let stem = Path::new(&self.model_selected)
            .file_stem()
            .and_then(|s| s.to_str())
            .unwrap_or("");
        if stem.is_empty() {
            self.min_outer_diameter = 0.0;
            return;
        }

        #[derive(Deserialize)]
        struct ModelJson {
            size: Option<f32>,
        }

        let json_path = models_dir.join(format!("{stem}.json"));
        if let Ok(content) = std::fs::read_to_string(&json_path) {
            if let Ok(model_cfg) = serde_json::from_str::<ModelJson>(&content) {
                if let Some(size) = model_cfg.size {
                    self.min_outer_diameter = size.max(0.0);
                    // 确保当前外圈及内外圈不小于该最小值
                    if self.outer_diameter < self.min_outer_diameter {
                        self.outer_diameter = self.min_outer_diameter;
                    }
                    if self.middle_diameter < self.min_outer_diameter {
                        self.middle_diameter = self.min_outer_diameter;
                    }
                    if self.inner_diameter < self.min_outer_diameter {
                        self.inner_diameter = self.min_outer_diameter;
                    }
                    return;
                }
            }
        }

        // 如果读取失败或没有 size 字段，则不强制最小值
        self.min_outer_diameter = 0.0;
    }

    /// 从 ConfigFile 加载配置到 UI
    fn load_config(&mut self, config: &ConfigFile) {
        self.use_controller = config.use_controller;
        self.aa_activate_mode_selected = config.aa_activate_mode.clone();
        self.outer_diameter = config.assist_curve.outer_diameter;
        self.outer_strength = config.assist_curve.outer_strength;
        self.middle_diameter = config.assist_curve.middle_diameter;
        self.inner_diameter = config.assist_curve.inner_diameter;
        self.inner_strength = config.assist_curve.inner_strength;
        self.start_strength = config.assist_curve.deadzone;
        self.hipfire_strength_factor = config.assist_curve.hipfire;
        self.vertical_strength_factor = config.vertical_strength_coefficient;
        self.aim_height_factor = config.aim_height_coefficient;
        if let Some(ref m) = config.con_mapping {
            self.debug_axis_lx = m.axis.lx.map(|n| n.to_string()).unwrap_or_default();
            self.debug_axis_ly = m.axis.ly.map(|n| n.to_string()).unwrap_or_default();
            self.debug_axis_rx = m.axis.rx.map(|n| n.to_string()).unwrap_or_default();
            self.debug_axis_ry = m.axis.ry.map(|n| n.to_string()).unwrap_or_default();
            self.debug_axis_lt = m.axis.lt.map(|n| n.to_string()).unwrap_or_default();
            self.debug_axis_rt = m.axis.rt.map(|n| n.to_string()).unwrap_or_default();
            self.debug_btn_lb = m.button.lb.map(|n| n.to_string()).unwrap_or_default();
            self.debug_btn_rb = m.button.rb.map(|n| n.to_string()).unwrap_or_default();
            self.debug_btn_ls = m.button.ls.map(|n| n.to_string()).unwrap_or_default();
            self.debug_btn_rs = m.button.rs.map(|n| n.to_string()).unwrap_or_default();
            self.debug_btn_back = m.button.back.map(|n| n.to_string()).unwrap_or_default();
            self.debug_btn_start = m.button.start.map(|n| n.to_string()).unwrap_or_default();
            self.debug_btn_x = m.button.x.map(|n| n.to_string()).unwrap_or_default();
            self.debug_btn_y = m.button.y.map(|n| n.to_string()).unwrap_or_default();
            self.debug_btn_a = m.button.a.map(|n| n.to_string()).unwrap_or_default();
            self.debug_btn_b = m.button.b.map(|n| n.to_string()).unwrap_or_default();
        } else {
            self.debug_axis_lx.clear();
            self.debug_axis_ly.clear();
            self.debug_axis_rx.clear();
            self.debug_axis_ry.clear();
            self.debug_axis_lt.clear();
            self.debug_axis_rt.clear();
            self.debug_btn_lb.clear();
            self.debug_btn_rb.clear();
            self.debug_btn_ls.clear();
            self.debug_btn_rs.clear();
            self.debug_btn_back.clear();
            self.debug_btn_start.clear();
            self.debug_btn_x.clear();
            self.debug_btn_y.clear();
            self.debug_btn_a.clear();
            self.debug_btn_b.clear();
        }
        self.config_changed = false;
    }
    
    /// 将调试窗口输入框解析为 ConMapping（空字符串为 None，非空解析为 u8）
    fn debug_to_con_mapping(&self) -> ConMapping {
        use utils::{ConMappingAxis, ConMappingButton};
        let parse_u8 = |s: &str| {
            let s = s.trim();
            if s.is_empty() { None } else { s.parse().ok() }
        };
        ConMapping {
            axis: ConMappingAxis {
                lx: parse_u8(&self.debug_axis_lx),
                ly: parse_u8(&self.debug_axis_ly),
                rx: parse_u8(&self.debug_axis_rx),
                ry: parse_u8(&self.debug_axis_ry),
                lt: parse_u8(&self.debug_axis_lt),
                rt: parse_u8(&self.debug_axis_rt),
            },
            button: ConMappingButton {
                lb: parse_u8(&self.debug_btn_lb),
                rb: parse_u8(&self.debug_btn_rb),
                ls: parse_u8(&self.debug_btn_ls),
                rs: parse_u8(&self.debug_btn_rs),
                back: parse_u8(&self.debug_btn_back),
                start: parse_u8(&self.debug_btn_start),
                x: parse_u8(&self.debug_btn_x),
                y: parse_u8(&self.debug_btn_y),
                a: parse_u8(&self.debug_btn_a),
                b: parse_u8(&self.debug_btn_b),
            },
        }
    }

    /// 将当前 UI 状态保存为 ConfigFile
    fn to_config_file(&self) -> ConfigFile {
        ConfigFile {
            aim_height_coefficient: self.aim_height_factor,
            assist_curve: utils::AssistCurve {
                deadzone: self.start_strength,
                hipfire: self.hipfire_strength_factor,
                inner_diameter: self.inner_diameter,
                inner_strength: self.inner_strength,
                middle_diameter: self.middle_diameter,
                outer_diameter: self.outer_diameter,
                outer_strength: self.outer_strength,
            },
            aa_activate_mode: self.aa_activate_mode_selected.clone(),
            use_controller: self.use_controller,
            vertical_strength_coefficient: self.vertical_strength_factor,
            con_mapping: Some(self.debug_to_con_mapping()),
        }
    }
    
    /// 保存当前配置到文件
    fn save_config(&mut self) {
        if !self.config_selected.is_empty() && self.config_changed {
            let config = self.to_config_file();
            if let Err(e) = save_config_file(&self.config_selected, &config) {
                eprintln!("保存配置文件失败: {}", e);
            } else {
                self.config_changed = false;
            }
            
            // 同时保存 .current 文件
            if let Err(e) = save_current_config(&self.config_selected, &self.model_selected) {
                eprintln!("保存 .current 文件失败: {}", e);
            }
        }
    }
    
    /// 标记配置已修改
    fn mark_config_changed(&mut self) {
        self.config_changed = true;
    }
    
    /// 同步参数到 MappingManager（将 f32 转换为 String）
    fn sync_params_to_manager(&self) {
        if let Ok(mut outer) = self.outer_size.lock() {
            *outer = self.outer_diameter.to_string();
        }
        if let Ok(mut mid) = self.mid_size.lock() {
            *mid = self.middle_diameter.to_string();
        }
        if let Ok(mut inner) = self.inner_size.lock() {
            *inner = self.inner_diameter.to_string();
        }
        if let Ok(mut outer_str) = self.outer_str.lock() {
            *outer_str = self.outer_strength.to_string();
        }
        if let Ok(mut inner_str) = self.inner_str.lock() {
            *inner_str = self.inner_strength.to_string();
        }
        if let Ok(mut init_str) = self.init_str.lock() {
            *init_str = self.start_strength.to_string();
        }
        if let Ok(mut hipfire) = self.hipfire.lock() {
            *hipfire = self.hipfire_strength_factor.to_string();
        }
        if let Ok(mut vertical_str) = self.vertical_str.lock() {
            *vertical_str = format!("{:.2}", self.vertical_strength_factor);
        }
        if let Ok(mut aim_height) = self.aim_height.lock() {
            *aim_height = format!("{:.2}", self.aim_height_factor);
        }
    }
    
    /// 切换智慧核心状态
    fn toggle_core(&mut self) {
        if self.core_enabled {
            // 关闭智慧核心
            // 先关闭预览窗口
            if self.inference_preview_window_created {
                self.show_inference_preview = false;
                self.inference_preview_window_created = false;
            }
            self.preview_allowed = false;
            
            // 使用 MappingManager 停止
            self.mapping_manager.request_stop();
            self.core_enabled = false;
        } else {
            // 开启智慧核心
            // 检查模型是否已选择
            if self.model_selected.is_empty() {
                eprintln!("请先选择模型");
                return;
            }
            
            // 检测 ViGemBus 和物理手柄是否就绪
            let vigem_ready = check_dir_exist("C:/Program Files/Nefarius Software Solutions/ViGEm Bus Driver");
            let controller_ready = enumerate_controllers();
            
            // 如果有任意项未就绪，打开帮助面板并返回
            if !vigem_ready || !controller_ready {
                self.help_window_vigem_ready = Some(vigem_ready);
                self.help_window_controller_ready = Some(controller_ready);
                self.show_help_window = true;
                return;
            }
            
            // 同步参数到 MappingManager
            self.sync_params_to_manager();
            
            // 更新瞄准辅助开关
            let aim_enable = self.aa_activate_mode_selected == "瞄准和开火";
            self.aim_enable.store(aim_enable, Ordering::Relaxed);
            self.mapping_manager.update_aim_enable(aim_enable);
            
            // 更新模型配置
            self.mapping_manager.update_config(self.model_selected.clone());
            
            // 任意映射为空则打开调试窗口且不允许启动智慧核心
            let mapping = self.debug_to_con_mapping();
            if !mapping.is_complete() {
                self.show_debug_window = true;
                self.debug_enabled = true;
                modules::bg_con_reading::set_debug_print_enabled(true);
                self.mapping_manager.start_con_reader_for_debug();
                return;
            }
            
            // 确保虚拟手柄
            if self.virtual_gamepad.lock().unwrap().is_none() {
                if let Ok(client) = Client::connect() {
                    let client = Arc::new(client);
                    let mut target = Xbox360Wired::new(client.clone(), vigem_client::TargetId::XBOX360_WIRED);
                    if target.plugin().is_ok() {
                        *self.virtual_gamepad.lock().unwrap() = Some(target);
                    }
                }
            }
            
            // 使用 MappingManager 启动
            let con_mapping = Some(self.debug_to_con_mapping());
            self.mapping_manager.request_start(con_mapping);
            self.core_enabled = true;
            self.preview_allowed = false;
        }
    }
    
    /// 创建默认配置（仅在新建时转换，根据屏幕高度转换直径值）
    fn create_default_config(screen_height: f32) -> ConfigFile {
        // 1440p 基准值
        let base_inner_diameter = 60.0;
        let base_middle_diameter = 60.0;
        let base_outer_diameter = 320.0;
        
        // 转换为当前屏幕高度的比例值
        let scale = screen_height / 1440.0;
        
        ConfigFile {
            aim_height_coefficient: 0.6,
            assist_curve: utils::AssistCurve {
                deadzone: 0.0,
                hipfire: 0.5,
                inner_diameter: base_inner_diameter * scale,
                inner_strength: 0.72,
                middle_diameter: base_middle_diameter * scale,
                outer_diameter: base_outer_diameter * scale,
                outer_strength: 0.36,
            },
            aa_activate_mode: "仅开火".to_string(),
            use_controller: false,
            vertical_strength_coefficient: 0.4,
            con_mapping: Some(ConMapping::default()),
        }
    }
}

impl eframe::App for MyApp {
    fn update(&mut self, ctx: &egui::Context, _frame: &mut eframe::Frame) {
        // 确保直径值满足约束关系：内 <= 中 <= 外 <= 屏幕高度
        if self.outer_diameter > self.screen_height {
            self.outer_diameter = self.screen_height;
        }
        if self.middle_diameter > self.outer_diameter {
            self.middle_diameter = self.outer_diameter;
        }
        if self.inner_diameter > self.middle_diameter {
            self.inner_diameter = self.middle_diameter;
        }
        
        // 定期更新 MappingManager 状态（每帧更新）
        let vg_clone = self.virtual_gamepad.clone();
        let mut con_exist_local = self.con_exist;
        let (_resize, _show_config, show_preview_flag, _disable_on_top) =
            self.mapping_manager.update(&mut con_exist_local, vg_clone);
        self.con_exist = con_exist_local;
        
        // 更新预览允许状态
        // 如果用户请求显示预览，且状态机允许显示，且智慧核心正在运行，则允许显示
        // 或者如果窗口已经创建，只要状态机允许且智慧核心正在运行，就继续显示
        self.preview_allowed = (self.show_inference_preview || self.inference_preview_window_created) 
            && show_preview_flag 
            && self.mapping_manager.is_active();
        
        // 如果智慧核心已停止，更新 UI 状态
        if !self.mapping_manager.is_active() && self.core_enabled {
            self.core_enabled = false;
            self.preview_allowed = false;
        }
        
        // 设置窗口边框的 margin
        egui::CentralPanel::default().show(ctx, |ui| {

            // 第一部分
            // 第一行：label | 下拉框 | 添加按钮 | 删除按钮
            ui.horizontal(|ui| {
                ui.set_enabled(!self.core_enabled);

                // Label - 使用固定宽度区域确保对齐
                ui.add_sized(
                    egui::Vec2::new(CHARACTER_WIDTH * 3.0, ROW_HEIGHT),
                    egui::Label::new("配置")
                );
                                
                // 下拉框 - 使用响应式宽度，占据剩余空间
                // 使用保存的初始宽度计算，确保两行一致
                let combo_width = ui.available_width() - (CHARACTER_WIDTH * 6.0 + SPACING * 3.0);
                let old_config = self.config_selected.clone();
                egui::ComboBox::from_id_source("config_combo")
                    .width(combo_width)
                    .selected_text(
                        if self.config_selected.is_empty() {
                            "请选择配置"
                        } else {
                            &self.config_selected
                        }
                    )
                    .show_ui(ui, |ui| {
                        for item in &self.config_items {
                            ui.selectable_value(&mut self.config_selected, item.clone(), item);
                        }
                    });
                
                // 如果配置选择改变了，先保存旧配置，然后加载新配置
                if self.config_selected != old_config {
                    if !old_config.is_empty() && self.config_changed {
                        // 保存旧配置
                        let old_config_data = self.to_config_file();
                        if let Err(e) = save_config_file(&old_config, &old_config_data) {
                            eprintln!("保存配置文件失败: {}", e);
                        }
                    }
                    
                    // 加载新配置
                    if !self.config_selected.is_empty() {
                        if let Ok(config) = load_config_file(&self.config_selected) {
                            self.load_config(&config);
                        }
                    } else {
                        // 清空配置
                        self.config_changed = false;
                    }
                    
                    // 更新 .current 文件
                    if let Err(e) = save_current_config(&self.config_selected, &self.model_selected) {
                        eprintln!("保存 .current 文件失败: {}", e);
                    }
                }
                                
                // 添加按钮 - 使用固定宽度
                if ui.add_sized(
                    egui::Vec2::new(CHARACTER_WIDTH * 3.0, ROW_HEIGHT),
                    egui::Button::new("添加")
                ).clicked() {
                    // 显示添加配置对话框
                    self.add_config_dialog = Some(String::new());
                }
                
                // 删除按钮 - 使用固定宽度
                if ui.add_sized(
                    egui::Vec2::new(CHARACTER_WIDTH * 3.0, ROW_HEIGHT),
                    egui::Button::new("删除")
                ).clicked() {
                    if !self.config_selected.is_empty() {
                        // 显示确认对话框
                        self.delete_config_confirm = Some((self.config_selected.clone(), "configs".to_string()));
                    }
                }
            });
                        
            // 第二行：label | 下拉框 | 添加按钮 | 删除按钮
            ui.horizontal(|ui| {
                ui.set_enabled(!self.core_enabled);

                // Label - 使用固定宽度区域确保对齐
                ui.add_sized(
                    egui::Vec2::new(CHARACTER_WIDTH * 3.0, ROW_HEIGHT),
                    egui::Label::new("模型")
                );
                
                // 下拉框 - 使用响应式宽度，占据剩余空间
                // 使用保存的初始宽度计算，确保与第一行一致
                let combo_width = ui.available_width() - (CHARACTER_WIDTH * 6.0 + SPACING * 3.0);
                let old_model = self.model_selected.clone();
                egui::ComboBox::from_id_source("model_combo")
                    .width(combo_width)
                    .selected_text(
                        if self.model_selected.is_empty() {
                            "请选择模型"
                        } else {
                            &self.model_selected
                        }
                    )
                    .show_ui(ui, |ui| {
                        for item in &self.model_items {
                            ui.selectable_value(&mut self.model_selected, item.clone(), item);
                        }
                    });
                
                // 如果模型选择改变了，更新 .current 文件
                if self.model_selected != old_model {
                    if let Err(e) = save_current_config(&self.config_selected, &self.model_selected) {
                        eprintln!("保存 .current 文件失败: {}", e);
                    }
                    // 同时根据新模型更新最小外圈直径
                    self.update_min_outer_from_model();
                }
                
                // 打开文件夹按钮 - 使用固定宽度
                if ui.add_sized(
                    egui::Vec2::new(CHARACTER_WIDTH * 6.0 + SPACING, ROW_HEIGHT),
                    egui::Button::new("打开文件夹")
                ).clicked() {
                    // 打开文件夹逻辑
                    {
                        std::process::Command::new("explorer")
                            .arg("models")
                            .spawn()
                            .ok();
                    }
                }
            });
            
            ui.separator();

            // 第二部分
            ui.horizontal(|ui| {
                ui.set_enabled(!self.core_enabled);

                ui.add_sized(
                    egui::Vec2::new(
                        CHARACTER_WIDTH * 3.0,
                        ROW_HEIGHT
                    ),
                    egui::Label::new("设定")
                );

                let snap_color = if self.param_tab == ParamTab::SnapFeature { Some(GREEN) } else { None };
                let mut snap_btn = egui::Button::new("吸附相关");
                if let Some(color) = snap_color {
                    snap_btn = snap_btn.fill(color);
                }
                if ui.add_sized(
                    egui::Vec2::new(CHARACTER_WIDTH * 5.0, ROW_HEIGHT),
                    snap_btn
                ).clicked() {
                    self.param_tab = ParamTab::SnapFeature;
                }

                let other_color = if self.param_tab == ParamTab::InputDevice { Some(GREEN) } else { None };
                let mut other_btn = egui::Button::new("输入设备");
                if let Some(color) = other_color {
                    other_btn = other_btn.fill(color);
                }
                if ui.add_sized(
                    egui::Vec2::new(CHARACTER_WIDTH * 5.0, ROW_HEIGHT),
                    other_btn
                ).clicked() {
                    self.param_tab = ParamTab::InputDevice;
                }

                let aux_color = if self.param_tab == ParamTab::Accessibility { Some(GREEN) } else { None };
                let mut aux_btn = egui::Button::new("辅助功能");
                if let Some(color) = aux_color {
                    aux_btn = aux_btn.fill(color);
                }
                if ui.add_sized(
                    egui::Vec2::new(CHARACTER_WIDTH * 5.0, ROW_HEIGHT),
                    aux_btn
                ).clicked() {
                    self.param_tab = ParamTab::Accessibility;
                }
            });

            ui.horizontal(|ui| {
                ui.set_enabled(!self.core_enabled);

                ui.add_sized(
                    egui::Vec2::new(CHARACTER_WIDTH * 1.0, ROW_HEIGHT),
                    egui::Label::new("")
                );

                ui.vertical(|ui| {
                    ui.separator();

                    match self.param_tab {
                    ParamTab::SnapFeature => {

                    ui.horizontal(|ui| {
                        ui.add_sized(
                            egui::Vec2::new(CHARACTER_WIDTH * 5.0, ROW_HEIGHT),
                            egui::Label::new("吸附方式")
                        );

                        let combo_width = ui.available_width() - SPACING;
                        let old_aa_mode = self.aa_activate_mode_selected.clone();
                        egui::ComboBox::from_id_source("aa_combo")
                            .width(combo_width)
                            .selected_text(
                                if self.aa_activate_mode_selected.is_empty() {
                                    "请选择吸附方式"
                                } else {
                                    &self.aa_activate_mode_selected
                                }
                            )
                            .show_ui(ui, |ui| {
                                for item in &self.aa_activate_mode_items {
                                    ui.selectable_value(&mut self.aa_activate_mode_selected, item.clone(), item);
                                }
                            });
                        
                        if self.aa_activate_mode_selected != old_aa_mode {
                            self.mark_config_changed();
                            self.save_config();
                        }
                    });

                    ui.separator();

                    ui.horizontal(|ui| {
                        ui.add_sized(
                            egui::Vec2::new(CHARACTER_WIDTH * 7.0, ROW_HEIGHT),
                            egui::Label::new("吸附曲线设定")
                        );

                        ui.add_sized(
                            egui::Vec2::new(ui.available_width() - CHARACTER_WIDTH * 3.0 - SPACING * 2.0, ROW_HEIGHT),
                            egui::Label::new("")
                        );

                        let preview_button_color = if self.preview_window_created { Some(GREEN) } else { None };
                        let mut preview_button = egui::Button::new("预览");
                        if let Some(color) = preview_button_color {
                            preview_button = preview_button.fill(color);
                        }
                        if ui.add_sized(
                            egui::Vec2::new(CHARACTER_WIDTH * 3.0, ROW_HEIGHT),
                            preview_button
                        ).clicked() {
                            self.show_preview = true;
                        }
                    });

                    ui.horizontal(|ui| {
                        ui.add_sized(
                            egui::Vec2::new(CHARACTER_WIDTH, ROW_HEIGHT),
                            egui::Label::new("")
                        );

                        ui.vertical(|ui| {
                            ui.separator();

                            ui.horizontal(|ui| {
                                ui.vertical(|ui| {
                                    ui.horizontal(|ui| {
                                        ui.add_sized(
                                            egui::Vec2::new(CHARACTER_WIDTH * 2.0, ROW_HEIGHT),
                                                egui::Label::new("")
                                        );

                                        ui.add_sized(
                                            egui::Vec2::new(CHARACTER_WIDTH * 4.0, ROW_HEIGHT),
                                                egui::Label::new("吸附范围")
                                        );

                                        ui.add_sized(
                                            egui::Vec2::new(CHARACTER_WIDTH * 4.0, ROW_HEIGHT),
                                                egui::Label::new("吸附强度")
                                        );
                                    });

                                    ui.horizontal(|ui| {
                                        ui.add_sized(
                                            egui::Vec2::new(CHARACTER_WIDTH * 2.0, ROW_HEIGHT),
                                            egui::Label::new("外")
                                        );

                                        let old_outer = self.outer_diameter;
                                        if ui.add_sized(
                                            egui::Vec2::new(CHARACTER_WIDTH * 4.0, ROW_HEIGHT),
                                            egui::DragValue::new(&mut self.outer_diameter)
                                                .clamp_range(self.min_outer_diameter..=self.screen_height)
                                                .speed(1.0)
                                        ).changed() {
                                            self.mark_config_changed();
                                            self.save_config();
                                        }
                                        
                                        // 如果外直径减小，自动调整中直径和内直径
                                        if self.outer_diameter < old_outer {
                                            if self.middle_diameter > self.outer_diameter {
                                                self.middle_diameter = self.outer_diameter;
                                            }
                                            if self.inner_diameter > self.middle_diameter {
                                                self.inner_diameter = self.middle_diameter;
                                            }
                                        }

                                        if ui.add_sized(
                                            egui::Vec2::new(CHARACTER_WIDTH * 4.0, ROW_HEIGHT),
                                            egui::DragValue::new(&mut self.outer_strength)
                                                .clamp_range(0.0..=1.0)
                                                .speed(0.01)
                                        ).changed() {
                                            self.mark_config_changed();
                                            self.save_config();
                                        }
                                    });

                                    ui.horizontal(|ui| {
                                        ui.add_sized(
                                            egui::Vec2::new(CHARACTER_WIDTH * 2.0, ROW_HEIGHT),
                                            egui::Label::new("中")
                                        );

                                        let old_middle = self.middle_diameter;
                                        if ui.add_sized(
                                            egui::Vec2::new(CHARACTER_WIDTH * 4.0, ROW_HEIGHT),
                                            egui::DragValue::new(&mut self.middle_diameter)
                                                .clamp_range(0.0..=self.outer_diameter)
                                                .speed(1.0)
                                        ).changed() {
                                            self.mark_config_changed();
                                            self.save_config();
                                        }
                                        
                                        // 如果中直径减小，自动调整内直径
                                        if self.middle_diameter < old_middle {
                                            if self.inner_diameter > self.middle_diameter {
                                                self.inner_diameter = self.middle_diameter;
                                            }
                                        }
                                    });

                                    ui.horizontal(|ui| {
                                        ui.add_sized(
                                            egui::Vec2::new(CHARACTER_WIDTH * 2.0, ROW_HEIGHT),
                                            egui::Label::new("内")
                                        );

                                        if ui.add_sized(
                                            egui::Vec2::new(CHARACTER_WIDTH * 4.0, ROW_HEIGHT),
                                            egui::DragValue::new(&mut self.inner_diameter)
                                                .clamp_range(0.0..=self.middle_diameter)
                                                .speed(1.0)
                                        ).changed() {
                                            self.mark_config_changed();
                                            self.save_config();
                                        }

                                        if ui.add_sized(
                                            egui::Vec2::new(CHARACTER_WIDTH * 4.0, ROW_HEIGHT),
                                            egui::DragValue::new(&mut self.inner_strength)
                                                .clamp_range(0.0..=1.0)
                                                .speed(0.01)
                                        ).changed() {
                                            self.mark_config_changed();
                                            self.save_config();
                                        }
                                    });
                                });

                                // 插入坐标图
                                {
                                    // 计算绘图区域大小
                                    let plot_height = ui.available_height();
                                    let plot_width = ui.available_width();
                                    
                                    // 创建绘图区域
                                    let (rect, _response) = ui.allocate_exact_size(
                                        egui::Vec2::new(plot_width, plot_height),
                                        egui::Sense::hover()
                                    );
                                    
                                    // 绘制背景（使用窗口背景颜色）
                                    ui.painter().rect_filled(
                                        rect,
                                        0.0,
                                        ui.style().visuals.panel_fill,
                                    );
                                    
                                    // 坐标轴范围
                                    let x_min = 0.0;
                                    // 如果没有选择配置，使用屏幕高度的一半作为默认最大值
                                    let x_max = if self.outer_diameter > 0.0 {
                                        self.outer_diameter / 2.0
                                    } else {
                                        self.screen_height / 2.0
                                    };
                                    let y_min = 0.0;
                                    let y_max = 1.0;
                                    
                                    // 计算坐标转换函数（最小内边距）
                                    let margin = 8.0;
                                    let x_scale = (rect.width() - margin * 2.0) / (x_max - x_min);
                                    let y_scale = rect.height() / (y_max - y_min);
                                    let x_offset = rect.min.x + margin;
                                    let y_offset = rect.max.y;
                                    
                                    let to_screen_x = |x: f32| x_offset + (x - x_min) * x_scale;
                                    let to_screen_y = |y: f32| y_offset - (y - y_min) * y_scale;
                                    
                                    // 绘制坐标轴
                                    let axis_color = egui::Color32::from_gray(150);
                                    let axis_stroke = egui::Stroke::new(1.0, axis_color);
                                    
                                    // X轴
                                    ui.painter().line_segment(
                                        [
                                            egui::pos2(to_screen_x(x_min), to_screen_y(y_min)),
                                            egui::pos2(to_screen_x(x_max), to_screen_y(y_min)),
                                        ],
                                        axis_stroke,
                                    );
                                    
                                    // Y轴
                                    ui.painter().line_segment(
                                        [
                                            egui::pos2(to_screen_x(x_min), to_screen_y(y_min)),
                                            egui::pos2(to_screen_x(x_min), to_screen_y(y_max)),
                                        ],
                                        axis_stroke,
                                    );
                                    
                                    // 绘制刻度线
                                    let tick_length = 5.0;
                                    let tick_stroke = egui::Stroke::new(1.0, axis_color);
                                    
                                    // Y轴刻度：在 y=1 的位置
                                    let y_tick_y = to_screen_y(1.0);
                                    ui.painter().line_segment(
                                        [
                                            egui::pos2(to_screen_x(x_min), y_tick_y),
                                            egui::pos2(to_screen_x(x_min) + tick_length, y_tick_y),
                                        ],
                                        tick_stroke,
                                    );
                                    
                                    // X轴刻度：在 0, inner_diameter/2, middle_diameter/2, outer_diameter/2 的位置
                                    // 如果没有选择配置，只显示 0 刻度
                                    let x_ticks = if self.outer_diameter > 0.0 {
                                        vec![
                                            0.0,
                                            self.inner_diameter / 2.0,
                                            self.middle_diameter / 2.0,
                                            self.outer_diameter / 2.0,
                                        ]
                                    } else {
                                        vec![0.0]
                                    };
                                    
                                    for &x_tick_value in &x_ticks {
                                        let x_tick_x = to_screen_x(x_tick_value);
                                        let y_tick_bottom = to_screen_y(y_min);
                                        ui.painter().line_segment(
                                            [
                                                egui::pos2(x_tick_x, y_tick_bottom),
                                                egui::pos2(x_tick_x, y_tick_bottom - tick_length),
                                            ],
                                            tick_stroke,
                                        );
                                    }
                                    
                                    // 只有在有有效配置数据时才绘制折线和点
                                    if self.outer_diameter > 0.0 {
                                        // 第一段折线：(0, start_strength) -> (inner_diameter/2, inner_strength) - 红色
                                        let line1_color = RED;
                                        let line1_stroke = egui::Stroke::new(2.0, line1_color);
                                        
                                        let p1 = egui::pos2(to_screen_x(0.0), to_screen_y(self.start_strength));
                                        let p2 = egui::pos2(to_screen_x(self.inner_diameter / 2.0), to_screen_y(self.inner_strength));
                                        
                                        ui.painter().line_segment([p1, p2], line1_stroke);
                                        
                                        // 第二段折线：(inner_diameter/2, inner_strength) -> (middle_diameter/2, inner_strength) - 黄色
                                        let line2_color = YELLOW;
                                        let line2_stroke = egui::Stroke::new(2.0, line2_color);
                                        
                                        let p3 = egui::pos2(to_screen_x(self.middle_diameter / 2.0), to_screen_y(self.inner_strength));
                                        
                                        ui.painter().line_segment([p2, p3], line2_stroke);
                                        
                                        // 第三段折线：(middle_diameter/2, outer_strength) -> (outer_diameter/2, outer_strength) - 绿色
                                        let line3_color = GREEN;
                                        let line3_stroke = egui::Stroke::new(2.0, line3_color);
                                        
                                        let p4 = egui::pos2(to_screen_x(self.middle_diameter / 2.0), to_screen_y(self.outer_strength));
                                        let p5 = egui::pos2(to_screen_x(self.outer_diameter / 2.0), to_screen_y(self.outer_strength));
                                        
                                        ui.painter().line_segment([p4, p5], line3_stroke);
                                        
                                        // 绘制关键点
                                        let point_radius = 3.0;
                                        ui.painter().circle_filled(p1, point_radius, line1_color);
                                        ui.painter().circle_filled(p2, point_radius, line1_color);
                                        ui.painter().circle_filled(p3, point_radius, line2_color);
                                        ui.painter().circle_filled(p4, point_radius, line3_color);
                                        ui.painter().circle_filled(p5, point_radius, line3_color);
                                    }
                                }
                            });

                            ui.separator();

                            ui.horizontal(|ui| {
                                ui.add_sized(
                                    egui::Vec2::new(CHARACTER_WIDTH * 7.0, ROW_HEIGHT),
                                    egui::Label::new("起始吸附强度")
                                );

                                if ui.add_sized(
                                    egui::Vec2::new(CHARACTER_WIDTH * 4.0, ROW_HEIGHT),
                                    egui::DragValue::new(&mut self.start_strength)
                                        .clamp_range(0.0..=1.0)
                                        .speed(0.01)
                                ).changed() {
                                    self.mark_config_changed();
                                    self.save_config();
                                }

                                ui.add_sized(
                                    egui::Vec2::new(CHARACTER_WIDTH * 7.0, ROW_HEIGHT),
                                    egui::Label::new("腰射吸附系数")
                                );

                                if ui.add_sized(
                                    egui::Vec2::new(CHARACTER_WIDTH * 4.0, ROW_HEIGHT),
                                    egui::DragValue::new(&mut self.hipfire_strength_factor)
                                        .clamp_range(0.0..=1.0)
                                        .speed(0.01)
                                ).changed() {
                                    self.mark_config_changed();
                                    self.save_config();
                                }
                            });

                            ui.horizontal(|ui| {
                                ui.add_sized(
                                    egui::Vec2::new(CHARACTER_WIDTH * 7.0, ROW_HEIGHT),
                                    egui::Label::new("垂直吸附系数")
                                );

                                if ui.add_sized(
                                    egui::Vec2::new(CHARACTER_WIDTH * 4.0, ROW_HEIGHT),
                                    egui::DragValue::new(&mut self.vertical_strength_factor)
                                        .clamp_range(0.0..=1.0)
                                        .speed(0.01)
                                ).changed() {
                                    self.mark_config_changed();
                                    self.save_config();
                                }

                                ui.add_sized(
                                    egui::Vec2::new(CHARACTER_WIDTH * 7.0, ROW_HEIGHT),
                                    egui::Label::new("瞄准高度系数")
                                );

                                if ui.add_sized(
                                    egui::Vec2::new(CHARACTER_WIDTH * 4.0, ROW_HEIGHT),
                                    egui::DragValue::new(&mut self.aim_height_factor)
                                        .clamp_range(0.0..=1.0)
                                        .speed(0.01)
                                ).changed() {
                                    self.mark_config_changed();
                                    self.save_config();
                                }
                            });
                        });
                    });

                    } // SnapRelated
                    ParamTab::InputDevice => {

                    ui.horizontal(|ui| {
                        ui.add_sized(
                            egui::Vec2::new(CHARACTER_WIDTH * 5.0, ROW_HEIGHT),
                            egui::Label::new("输入设备")
                        );

                        // 键鼠 radiobutton（未实现，禁用）
                        ui.add_enabled_ui(false, |ui| {
                            ui.add_sized(
                                egui::Vec2::new(CHARACTER_WIDTH * 9.0, ROW_HEIGHT),
                                egui::RadioButton::new(!self.use_controller, "键鼠（未实现）")
                            );
                        });
                    });

                    ui.horizontal(|ui| {
                        ui.add_sized(
                            egui::Vec2::new(CHARACTER_WIDTH * 5.0, ROW_HEIGHT),
                            egui::Label::new("")
                        );

                        // 手柄 radiobutton
                        if ui.add_sized(
                            egui::Vec2::new(CHARACTER_WIDTH * 4.0, ROW_HEIGHT),
                            egui::RadioButton::new(self.use_controller, "手柄")
                        ).clicked() {
                            self.use_controller = true;
                            self.mark_config_changed();
                            self.save_config();
                        }
                    });

                    ui.separator();

                    ui.horizontal(|ui| {
                        if ui.button("手柄键位调试").clicked() {
                            if !enumerate_controllers() {
                                self.help_window_vigem_ready = Some(check_dir_exist("C:/Program Files/Nefarius Software Solutions/ViGEm Bus Driver"));
                                self.help_window_controller_ready = Some(false);
                                self.show_help_window = true;
                                return;
                            }
                            self.show_debug_window = true;
                            self.debug_enabled = true;
                            modules::bg_con_reading::set_debug_print_enabled(true);
                            self.mapping_manager.start_con_reader_for_debug();
                        }
                    });

                    // ui.add_sized(
                    //     egui::Vec2::new(0.0, ROW_HEIGHT * 5.0 + ROW_HEIGHT / 3.0 + SPACING * 6.0),
                    //     egui::Label::new("")
                    // );

                    }
                    ParamTab::Accessibility => {

                    ui.horizontal(|ui| {
                        // 右扳机连点开关（仅在手柄模式下可用）
                        let mut rapid_enabled = self.rt_rapid_fire.load(Ordering::Relaxed);
                        let enabled_before = rapid_enabled;
                        ui.add_enabled_ui(self.use_controller, |ui| {
                            ui.checkbox(&mut rapid_enabled, "连点");
                        });
                        if rapid_enabled != enabled_before {
                            self.rt_rapid_fire.store(rapid_enabled, Ordering::Relaxed);
                        }
                    });

                    ui.separator();

                    ui.horizontal(|ui| {
                        if ui.add_sized(
                            egui::Vec2::new(CHARACTER_WIDTH * 1.6, ROW_HEIGHT),
                            egui::Button::new("?")
                        ).clicked() {
                            self.help_window_vigem_ready = Some(check_dir_exist("C:/Program Files/Nefarius Software Solutions/ViGEm Bus Driver"));
                            self.help_window_controller_ready = Some(enumerate_controllers());
                            self.show_help_window = true;
                        }
                    });

                    }
                    }

                });
            });

            let bottom_section_height =
                ROW_HEIGHT * 2.0 + ROW_HEIGHT / 3.0 * 2.0 + SPACING * 3.0;
            let remaining = ui.available_height() - bottom_section_height;
            if remaining > 0.0 {
                ui.add_space(remaining);
            }

            ui.separator();

            ui.horizontal(|ui| {                
                ui.add_sized(
                    egui::Vec2::new((ui.available_width() - CHARACTER_WIDTH * 5.0) / 2.0 - SPACING, ROW_HEIGHT),
                    egui::Label::new("")
                );

                let button_color = if self.core_enabled { Some(GREEN) } else { None };
                
                let mut button = egui::Button::new("智慧核心");
                if let Some(color) = button_color {
                    button = button.fill(color);
                }
                
                if ui.add_sized(
                    egui::Vec2::new(CHARACTER_WIDTH * 5.0, ROW_HEIGHT),
                    button
                ).clicked() {
                    self.toggle_core();
                }

                ui.add_sized(
                    egui::Vec2::new(ui.available_width() - CHARACTER_WIDTH * 3.0 - SPACING * 2.0, ROW_HEIGHT),
                    egui::Label::new("")
                );

                let inference_preview_button_color = if self.inference_preview_window_created { Some(GREEN) } else { None };
                let mut inference_preview_button = egui::Button::new("预览");
                if let Some(color) = inference_preview_button_color {
                    inference_preview_button = inference_preview_button.fill(color);
                }
                ui.set_enabled(self.core_enabled);
                if ui.add_sized(
                    egui::Vec2::new(CHARACTER_WIDTH * 3.0, ROW_HEIGHT),
                    inference_preview_button
                ).clicked() {
                    self.show_inference_preview = true;
                }
                ui.set_enabled(true); // 恢复启用状态
            });

            ui.separator();
            
            ui.horizontal(|ui| {
                ui.set_enabled(!self.core_enabled);
                
                ui.add_sized(
                    egui::Vec2::new(CHARACTER_WIDTH * 4.0, ROW_HEIGHT),
                    egui::Label::new("许可证")
                );
                
                // 输入框
                ui.add_sized(
                    egui::Vec2::new(ui.available_width(), ROW_HEIGHT),
                    egui::TextEdit::singleline(&mut self.license_key)
                        .hint_text("请输入许可证")
                );
            });
        });
        
        // 显示删除确认对话框
        if let Some((file_name, folder)) = self.delete_config_confirm.clone() {
            // 创建模态遮罩层，阻止主窗口交互
            modal_blocker(ctx);
            
            let mut should_delete = false;
            let mut should_cancel = false;
            
            // 计算对话框大小和位置（居中显示）
            let text_width = CHARACTER_WIDTH * (12.0 + (file_name.chars().count() + 2) as f32 * 0.6);
            let dialog_width = text_width + SPACING * 2.5;
            let dialog_height = ROW_HEIGHT * 2.0 + SPACING * 3.0;
            let dialog_pos = egui::pos2(
                ctx.screen_rect().center().x - dialog_width / 2.0,
                ctx.screen_rect().center().y - dialog_height / 2.0,
            );
            
            // 使用 Area 创建确认对话框
            egui::Area::new(egui::Id::new("confirm_dialog"))
                .order(egui::Order::Foreground)
                .fixed_pos(dialog_pos)
                .show(ctx, |ui| {
                    // 使用 Frame 绘制窗口样式
                    egui::Frame::popup(ui.style())
                        .fill(ctx.style().visuals.window_fill())
                        .show(ui, |ui| {
                            
                            ui.horizontal(|ui| {
                                ui.add_sized(
                                    egui::Vec2::new(
                                        (dialog_width - text_width - SPACING * 3.5) / 2.0, 
                                        ROW_HEIGHT
                                    ),
                                    egui::Label::new("")
                                );

                                ui.add_sized(
                                    egui::Vec2::new(
                                        text_width, 
                                        ROW_HEIGHT
                                    ),
                                    egui::Label::new(format!("确定要删除配置文件 {} 吗？", file_name))
                                );
                            });

                            ui.horizontal(|ui| {
                                ui.add_sized(
                                    egui::Vec2::new(
                                        (dialog_width - 6.0 * CHARACTER_WIDTH - SPACING * 4.5) / 2.0, 
                                        ROW_HEIGHT
                                    ),
                                    egui::Label::new("")
                                );
                                
                                if ui.add_sized(
                                    egui::Vec2::new(CHARACTER_WIDTH * 3.0, ROW_HEIGHT),
                                    egui::Button::new("确认")
                                        .fill(egui::Color32::from_rgb(180, 60, 60)) // 红色背景
                                ).clicked() {
                                    should_delete = true;
                                }
                                
                                if ui.add_sized(
                                    egui::Vec2::new(CHARACTER_WIDTH * 3.0, ROW_HEIGHT),
                                    egui::Button::new("取消")
                                ).clicked() {
                                    should_cancel = true;
                                }
                            });
                        });
                });
            
            if should_cancel {
                self.delete_config_confirm = None;
            } else if should_delete {
                // 删除文件
                let file_path = format!("{}/{}.json", folder, file_name);
                if std::fs::remove_file(&file_path).is_ok() {
                    // 更新文件列表
                    if folder == "configs" {
                        self.config_items = find_json_files("configs");
                        if self.config_selected == file_name {
                            // 清空配置选择
                            self.config_selected.clear();
                            // 重置所有配置数据到默认值
                            self.use_controller = false;
                            self.aa_activate_mode_selected = String::new();
                            self.outer_diameter = 0.0;
                            self.outer_strength = 0.0;
                            self.middle_diameter = 0.0;
                            self.inner_diameter = 0.0;
                            self.inner_strength = 0.0;
                            self.start_strength = 0.0;
                            self.hipfire_strength_factor = 0.0;
                            self.vertical_strength_factor = 0.0;
                            self.aim_height_factor = 0.0;
                            self.config_changed = false;
                            // 更新 .current 文件
                            if let Err(e) = save_current_config(&self.config_selected, &self.model_selected) {
                                eprintln!("保存 .current 文件失败: {}", e);
                            }
                        }
                    } else if folder == "models" {
                        self.model_items = find_onnx_files("models");
                        if self.model_selected == file_name {
                            self.model_selected.clear();
                            // 更新 .current 文件
                            if let Err(e) = save_current_config(&self.config_selected, &self.model_selected) {
                                eprintln!("保存 .current 文件失败: {}", e);
                            }
                        }
                    }
                }
                self.delete_config_confirm = None;
            }
        }
        
        // 显示添加配置对话框
        if let Some(ref mut file_name) = self.add_config_dialog {
            // 创建模态遮罩层，阻止主窗口交互
            modal_blocker(ctx);
            
            let mut should_create = false;
            let mut should_cancel = false;
            
            // 计算对话框大小和位置（居中显示）
            let dialog_width = CHARACTER_WIDTH * 16.0 + SPACING * 1.5;
            let dialog_height = ROW_HEIGHT * 3.0 + SPACING * 4.0;
            let dialog_pos = egui::pos2(
                ctx.screen_rect().center().x - dialog_width / 2.0,
                ctx.screen_rect().center().y - dialog_height / 2.0,
            );
            
            // 使用 Area 创建添加配置对话框
            egui::Area::new(egui::Id::new("add_config_dialog"))
                .order(egui::Order::Foreground)
                .fixed_pos(dialog_pos)
                .show(ctx, |ui| {
                    // 使用 Frame 绘制窗口样式
                    egui::Frame::popup(ui.style())
                        .fill(ctx.style().visuals.window_fill())
                        .show(ui, |ui| {
                            
                            ui.horizontal(|ui| {
                                ui.add_sized(
                                    egui::Vec2::new(
                                        (dialog_width - 8.0 * CHARACTER_WIDTH - SPACING * 3.5) / 2.0, 
                                        ROW_HEIGHT
                                    ),
                                    egui::Label::new("")
                                );

                                ui.add_sized(
                                    egui::Vec2::new(CHARACTER_WIDTH * 8.0, ROW_HEIGHT),
                                    egui::Label::new("请输入配置名称")
                                );
                            });
                            
                            // 输入框
                            ui.add_sized(
                                egui::Vec2::new(CHARACTER_WIDTH * 16.0, ROW_HEIGHT),
                                egui::TextEdit::singleline(file_name)
                                    .hint_text("")
                            );

                            ui.horizontal(|ui| {
                                ui.add_sized(
                                    egui::Vec2::new(
                                        (dialog_width - 6.0 * CHARACTER_WIDTH - SPACING * 4.5) / 2.0, 
                                        ROW_HEIGHT
                                    ),
                                    egui::Label::new("")
                                );
                                
                                if ui.add_sized(
                                    egui::Vec2::new(CHARACTER_WIDTH * 3.0, ROW_HEIGHT),
                                    egui::Button::new("确认")
                                ).clicked() {
                                    should_create = true;
                                }
                                
                                if ui.add_sized(
                                    egui::Vec2::new(CHARACTER_WIDTH * 3.0, ROW_HEIGHT),
                                    egui::Button::new("取消")
                                ).clicked() {
                                    should_cancel = true;
                                }
                            });
                        });
                });
            
            if should_cancel {
                self.add_config_dialog = None;
            } else if should_create {
                // 创建配置文件
                let config_name = file_name.trim();
                if !config_name.is_empty() {
                    // 检查文件名是否已存在
                    if self.config_items.contains(&config_name.to_string()) {
                        eprintln!("配置文件 {} 已存在", config_name);
                    } else {
                        // 创建默认配置并保存（仅在新建时转换）
                        let default_config = Self::create_default_config(self.screen_height);
                        if let Err(e) = save_config_file(config_name, &default_config) {
                            eprintln!("创建配置文件失败: {}", e);
                        } else {
                            // 更新文件列表
                            self.config_items = find_json_files("configs");
                            // 选择新创建的配置
                            self.config_selected = config_name.to_string();
                            // 加载新配置到UI
                            self.load_config(&default_config);
                            // 更新 .current 文件
                            if let Err(e) = save_current_config(&self.config_selected, &self.model_selected) {
                                eprintln!("保存 .current 文件失败: {}", e);
                            }
                        }
                    }
                }
                self.add_config_dialog = None;
            }
        }
        
        // 检查预览窗口是否已关闭
        if self.preview_window_created {
            if let Some(closed) = ctx.data(|data| data.get_temp::<bool>(egui::Id::new("preview_window_closed"))) {
                if closed {
                    self.preview_window_created = false;
                    ctx.data_mut(|data| {
                        data.remove::<bool>(egui::Id::new("preview_window_closed"));
                    });
                }
            }
        }
        
        // 检查推理预览窗口是否已关闭
        if self.inference_preview_window_created {
            if let Some(closed) = ctx.data(|data| data.get_temp::<bool>(egui::Id::new("inference_preview_window_closed"))) {
                if closed {
                    self.inference_preview_window_created = false;
                    ctx.data_mut(|data| {
                        data.remove::<bool>(egui::Id::new("inference_preview_window_closed"));
                    });
                }
            }
        }
        
        // 显示预览窗口（仅在智慧核心未开启时）
        // 如果智慧核心开启，则强制关闭吸附曲线预览窗口
        if self.core_enabled && self.preview_window_created {
            let viewport_id = egui::ViewportId::from_hash_of("preview_window");
            ctx.send_viewport_cmd_to(viewport_id, egui::ViewportCommand::Close);
            self.preview_window_created = false;
            self.show_preview = false;
        }

        // 如果用户点击了预览按钮，设置标志
        // 如果窗口已创建，持续显示窗口（需要每帧都调用 show_viewport_immediate）
        if !self.core_enabled && (self.show_preview || self.preview_window_created) {
            // 如果用户点击了预览按钮，重置创建标志（允许重新创建）
            if self.show_preview && self.preview_window_created {
                let viewport_id = egui::ViewportId::from_hash_of("preview_window");
                ctx.send_viewport_cmd_to(viewport_id, egui::ViewportCommand::Close);
                self.preview_window_created = false;
            }
            
            // 创建或持续显示窗口
            // 获取屏幕缩放比例，将逻辑像素转换为物理像素
            let pixels_per_point = ctx.pixels_per_point();
            let outer_diameter_logical = if self.outer_diameter > 0.0 {
                self.outer_diameter
            } else {
                // 如果没有设置外圈直径，使用默认值
                320.0
            };
            // with_inner_size 接受逻辑像素，egui 会自动转换为物理像素
            // 要让窗口的实际物理大小是 outer_diameter_logical 物理像素，
            // 需要传入 outer_diameter_logical / pixels_per_point 逻辑像素
            let outer_diameter_for_window = outer_diameter_logical / pixels_per_point;
            
            // 复制需要的数据到闭包中
            let inner_diameter = self.inner_diameter;
            let middle_diameter = self.middle_diameter;
            let outer_diameter_copy = self.outer_diameter;
            // 将物理像素转换为逻辑像素用于绘制
            let pixels_per_point_clone = pixels_per_point;
            // 保存 outer_diameter_logical 用于绘制背景圆
            let outer_diameter_logical_clone = outer_diameter_logical;
            
            let viewport_id = egui::ViewportId::from_hash_of("preview_window");
            
            // 持续调用 show_viewport_deferred 来保持窗口显示
            ctx.show_viewport_deferred(
                viewport_id,
                egui::ViewportBuilder::default()
                    .with_title("预览")
                    .with_inner_size([outer_diameter_for_window, outer_diameter_for_window])
                    .with_resizable(false) // 不允许调整大小
                    .with_always_on_top() // 置顶
                    .with_decorations(false) // 有标准窗口装饰（最小化、最大化、关闭按钮）
                    .with_maximize_button(false), // 禁用最大化按钮
                move |ctx, _class| {
                    egui::CentralPanel::default()
                        .frame(egui::Frame::none())
                        .show(ctx, |ui| {
                            let rect = ui.max_rect();
                            let center = rect.center();
                            // 窗口大小是物理像素，需要转换为逻辑像素进行绘制
                            // outer_diameter_logical_clone 是物理像素，需要除以 pixels_per_point 转换为逻辑像素
                            let radius_logical = (outer_diameter_logical_clone / 2.0) / pixels_per_point_clone;
                            
                            // 绘制窗口边框
                            ui.painter().rect_stroke(
                                rect,
                                0.0,
                                egui::Stroke::new(2.0, egui::Color32::from_rgb(100, 100, 100)),
                            );
                            
                            // 绘制圆形背景
                            ui.painter().circle_filled(
                                center,
                                radius_logical,
                                egui::Color32::from_rgb(30, 30, 30),
                            );
                            
                            // 绘制圆形边框
                            ui.painter().circle_stroke(
                                center,
                                radius_logical,
                                egui::Stroke::new(2.0, egui::Color32::from_rgb(100, 100, 100)),
                            );
                            
                            // 绘制三个同心圆（外、中、内）
                            // 绘制顺序：从外到内，这样内层会覆盖外层（红色在最上层，绿色在最下层）
                            // 所有直径都是物理像素，需要转换为逻辑像素
                            if outer_diameter_copy > 0.0 {
                                // 外圈 - 绿色（对应第三段）- 最下层，最先绘制
                                let outer_radius_logical = (outer_diameter_copy / 2.0) / pixels_per_point_clone;
                                ui.painter().circle_stroke(
                                    center,
                                    outer_radius_logical,
                                    egui::Stroke::new(1.0, GREEN),
                                );
                                
                                // 中圈 - 黄色（对应第二段）- 中间层
                                if middle_diameter > 0.0 {
                                    let middle_radius_logical = (middle_diameter / 2.0) / pixels_per_point_clone;
                                    ui.painter().circle_stroke(
                                        center,
                                        middle_radius_logical,
                                        egui::Stroke::new(1.0, YELLOW),
                                    );
                                }
                                
                                // 内圈 - 红色（对应第一段）- 最上层，最后绘制
                                if inner_diameter > 0.0 {
                                    let inner_radius_logical = (inner_diameter / 2.0) / pixels_per_point_clone;
                                    ui.painter().circle_stroke(
                                        center,
                                        inner_radius_logical,
                                        egui::Stroke::new(1.0, RED),
                                    );
                                }
                            }
                            
                            // 处理窗口拖动：检测鼠标拖拽，允许通过点击窗口内部拖动窗口
                            let response = ui.interact(rect, egui::Id::new("preview_drag_area"), egui::Sense::drag());
                            if response.drag_started() {
                                ctx.send_viewport_cmd_to(ctx.viewport_id(), egui::ViewportCommand::StartDrag);
                            }
                            
                            // 处理关闭窗口的逻辑
                            // 检测用户点击关闭按钮或按 ESC 键
                            if ui.input(|i| i.viewport().close_requested()) || ui.input(|i| i.key_pressed(egui::Key::Escape)) {
                                // 标记窗口已关闭
                                ctx.data_mut(|data| {
                                    data.insert_temp(egui::Id::new("preview_window_closed"), true);
                                });
                            }
                            
                        });
                    
                    // 请求延迟刷新（约60fps），避免阻塞主窗口
                    ctx.request_repaint_after(std::time::Duration::from_millis(20));
                },
            );
            
            // 标记窗口已创建，重置 show_preview
            self.preview_window_created = true;
            self.show_preview = false;
        }
        
        // 显示推理预览窗口
        if (self.show_inference_preview || self.inference_preview_window_created) && self.preview_allowed {
            // 如果用户点击了预览按钮，重置创建标志（允许重新创建）
            if self.show_inference_preview && self.inference_preview_window_created {
                let viewport_id = egui::ViewportId::from_hash_of("inference_preview_window");
                ctx.send_viewport_cmd_to(viewport_id, egui::ViewportCommand::Close);
                self.inference_preview_window_created = false;
            }
            
            // 从 MappingManager 获取屏幕捕获器和检测器的引用
            let square_size = self.mapping_manager.get_screen_capturer()
                .as_ref()
                .map(|c| c.square_size)
                .unwrap_or(320);
            
            let detections_ref = self.mapping_manager.get_detector()
                .as_ref()
                .map(|d| d.result());
            let buffer_ref = self.mapping_manager.get_screen_capturer()
                .as_ref()
                .map(|c| c.buffer());
            let capture_latency_ref = self.mapping_manager.get_screen_capturer()
                .as_ref()
                .map(|c| c.capture_latency_ms());
            let capture_fps_ref = self.mapping_manager.get_screen_capturer()
                .as_ref()
                .map(|c| c.fps());
            let infer_latency_ref = self.mapping_manager.get_detector()
                .as_ref()
                .map(|d| d.infer_latency_ms());
            let preprocess_latency_ref = self.mapping_manager.get_detector()
                .as_ref()
                .map(|d| d.preprocess_latency_ms());
            let square_size_clone = square_size;
            
            // 获取屏幕缩放比例，将逻辑像素转换为物理像素
            let pixels_per_point = ctx.pixels_per_point();
            // 窗口大小使用物理像素，需要转换为逻辑像素
            // 为下方的label留出空间（约30逻辑像素）
            let label_height = ROW_HEIGHT * 4.0;
            let window_size_logical = (square_size as f32) / pixels_per_point;
            let window_height_logical = window_size_logical + label_height;
            
            let viewport_id = egui::ViewportId::from_hash_of("inference_preview_window");
            
            // 持续调用 show_viewport_deferred 来保持窗口显示
            ctx.show_viewport_deferred(
                viewport_id,
                egui::ViewportBuilder::default()
                    .with_title("推理预览")
                    .with_inner_size([window_size_logical, window_height_logical])
                    .with_resizable(false) // 不允许调整大小
                    .with_always_on_top()
                    .with_decorations(false)
                    .with_maximize_button(false),
                move |ctx, _class| {
                    egui::CentralPanel::default()
                        .frame(egui::Frame::none())
                        .show(ctx, |ui| {
                            let rect = ui.max_rect();
                            
                            // 绘制背景
                            ui.painter().rect_filled(
                                rect,
                                0.0,
                                egui::Color32::from_rgb(20, 20, 20),
                            );
                            
                            // 计算图像显示区域（上方）和label区域（下方）
                            let image_rect = egui::Rect::from_min_size(
                                rect.min,
                                egui::Vec2::new(rect.width(), rect.height() - label_height),
                            );
                            let label_rect = egui::Rect::from_min_size(
                                egui::pos2(rect.min.x, rect.max.y - label_height),
                                egui::Vec2::new(rect.width(), label_height),
                            );
                            
                            // 显示图像和检测框
                            // 直接从屏幕捕获缓冲区读取并转换为图像
                            if let Some(ref buffer_ref) = buffer_ref {
                                if let Ok(buf) = buffer_ref.lock() {
                                    if buf.len() == square_size_clone * square_size_clone * 3 {
                                        // CHW -> HWC 并创建 egui 图像
                                        let mut pixels = Vec::new();
                                        for row in 0..square_size_clone {
                                            for col in 0..square_size_clone {
                                                let chw_idx = row * square_size_clone + col;
                                                let r = buf[0 * square_size_clone * square_size_clone + chw_idx];
                                                let g = buf[1 * square_size_clone * square_size_clone + chw_idx];
                                                let b = buf[2 * square_size_clone * square_size_clone + chw_idx];
                                                pixels.push(egui::Color32::from_rgb(r, g, b));
                                            }
                                        }
                                        
                                        let img_size = egui::Vec2::new(square_size_clone as f32, square_size_clone as f32);
                                        
                                        // 计算缩放比例以适应图像显示区域
                                        let scale_x = image_rect.width() / img_size.x;
                                        let scale_y = image_rect.height() / img_size.y;
                                        let scale = scale_x.min(scale_y);
                                        
                                        let scaled_size = img_size * scale;
                                        let offset = (image_rect.size() - scaled_size) / 2.0;
                                        let img_rect = egui::Rect::from_min_size(image_rect.min + offset, scaled_size);
                                        
                                        // 将图像转换为 egui 纹理
                                        let color_image = egui::ColorImage {
                                            size: [square_size_clone, square_size_clone],
                                            pixels,
                                        };
                                        
                                        let texture = ctx.load_texture("inference_preview", color_image, Default::default());
                                        ui.put(img_rect, egui::Image::new(&texture).fit_to_exact_size(scaled_size));
                                        
                                        // 绘制检测框
                                        if let Some(ref detections_ref) = detections_ref {
                                            if let Ok(detections_guard) = detections_ref.lock() {
                                                if let Some(ref detections) = *detections_guard {
                                                    for det in detections {
                                                        // x, y 是中心点坐标，需要转换为左上角坐标
                                                        // 左上角 = (x - w/2, y - h/2)
                                                        // 右下角 = (x + w/2, y + h/2)
                                                        let center_x = det.x;
                                                        let center_y = det.y;
                                                        let half_w = det.w / 2.0;
                                                        let half_h = det.h / 2.0;
                                                        
                                                        let x1 = center_x - half_w;
                                                        let y1 = center_y - half_h;
                                                        let x2 = center_x + half_w;
                                                        let y2 = center_y + half_h;
                                                        
                                                        // 检测框坐标是相对于原始图像尺寸的，需要缩放到显示尺寸
                                                        let x1_scaled = (x1 / img_size.x) * scaled_size.x + img_rect.min.x;
                                                        let y1_scaled = (y1 / img_size.y) * scaled_size.y + img_rect.min.y;
                                                        let x2_scaled = (x2 / img_size.x) * scaled_size.x + img_rect.min.x;
                                                        let y2_scaled = (y2 / img_size.y) * scaled_size.y + img_rect.min.y;
                                                        
                                                        let bbox_rect = egui::Rect::from_min_max(
                                                            egui::pos2(x1_scaled, y1_scaled),
                                                            egui::pos2(x2_scaled, y2_scaled),
                                                        );
                                                        
                                                        // 绘制检测框（红色，细边框）
                                                        ui.painter().rect_stroke(
                                                            bbox_rect,
                                                            0.0,
                                                            egui::Stroke::new(1.0, egui::Color32::from_rgb(255, 0, 0)),
                                                        );
                                                    }
                                                }
                                            }
                                        }
                                    }
                                }
                            }
                            
                            // 在下方显示耗时 label
                            ui.allocate_ui_at_rect(label_rect, |ui| {
                                ui.with_layout(egui::Layout::left_to_right(egui::Align::Center), |ui| {
                                    // 获取耗时数据（ms）
                                    let infer_ms = infer_latency_ref.as_ref()
                                        .and_then(|val_ref| val_ref.lock().ok())
                                        .map(|v| *v)
                                        .unwrap_or(0.0);

                                    let capture_ms = capture_latency_ref.as_ref()
                                        .and_then(|val_ref| val_ref.lock().ok())
                                        .map(|v| *v)
                                        .unwrap_or(0.0);

                                    let preprocess_ms = preprocess_latency_ref.as_ref()
                                        .and_then(|val_ref| val_ref.lock().ok())
                                        .map(|v| *v)
                                        .unwrap_or(0.0);

                                    let capture_fps = capture_fps_ref.as_ref()
                                        .and_then(|val_ref| val_ref.lock().ok())
                                        .map(|v| *v)
                                        .unwrap_or(0.0);

                                    // 格式化显示文本：垂直排列的四行（截图帧率 + 三项耗时）
                                    let label_text = if infer_ms > 0.0 || capture_ms > 0.0 || preprocess_ms > 0.0 || capture_fps > 0.0 {
                                        format!(
                                            " {:.0} FPS\n {:.1} ms\n {:.1} ms\n {:.1} ms",
                                            capture_fps, capture_ms, preprocess_ms, infer_ms
                                        )
                                    } else {
                                        " 等待数据...".to_string()
                                    };

                                    ui.label(label_text);
                                });
                            });

                            // 绘制窗口边框
                            ui.painter().rect_stroke(
                                rect,
                                0.0,
                                egui::Stroke::new(2.0, egui::Color32::from_rgb(100, 100, 100)),
                            );
                            
                            // 处理窗口拖动：检测鼠标拖拽，允许通过点击窗口内部拖动窗口
                            let response = ui.interact(rect, egui::Id::new("inference_preview_drag_area"), egui::Sense::drag());
                            if response.drag_started() {
                                ctx.send_viewport_cmd_to(ctx.viewport_id(), egui::ViewportCommand::StartDrag);
                            }
                            
                            // 处理关闭窗口的逻辑
                            if ui.input(|i| i.viewport().close_requested()) || ui.input(|i| i.key_pressed(egui::Key::Escape)) {
                                ctx.data_mut(|data| {
                                    data.insert_temp(egui::Id::new("inference_preview_window_closed"), true);
                                });
                            }
                        });
                    
                    // 请求延迟刷新（约60fps），避免阻塞主窗口
                    ctx.request_repaint_after(std::time::Duration::from_millis(20));
                },
            );
            
            // 标记窗口已创建，重置 show_inference_preview
            self.inference_preview_window_created = true;
            self.show_inference_preview = false;
        }
        
        // 手柄键位调试窗口：覆盖整个主窗口，下方展示调试输出
        if self.show_debug_window {
            // 强制连续刷新，保证调试输出实时更新
            ctx.request_repaint();

            let screen_rect = ctx.screen_rect();
            let dialog_width = screen_rect.width();
            let dialog_height = screen_rect.height();
            let dialog_pos = screen_rect.min;
            
            let input_w = CHARACTER_WIDTH * 2.6;
            let label_w = CHARACTER_WIDTH * 4.0;
            
            egui::Area::new(egui::Id::new("debug_dialog"))
                .order(egui::Order::Foreground)
                .fixed_pos(dialog_pos)
                .show(ctx, |ui| {
                    egui::Frame::popup(ui.style())
                        .fill(ctx.style().visuals.window_fill())
                        .show(ui, |ui| {
                            ui.set_min_size(egui::Vec2::new(dialog_width, dialog_height));

                            // 上半部分：映射输入
                            ui.add_sized(egui::Vec2::new(CHARACTER_WIDTH * 3.4, ROW_HEIGHT), egui::Label::new("axis"));
                            ui.horizontal(|ui| {
                                ui.add_sized(egui::Vec2::new(label_w, ROW_HEIGHT), egui::Label::new("lx"));
                                ui.add_sized(egui::Vec2::new(input_w, ROW_HEIGHT), egui::TextEdit::singleline(&mut self.debug_axis_lx).hint_text(""));
                                ui.add_sized(egui::Vec2::new(label_w, ROW_HEIGHT), egui::Label::new("ly"));
                                ui.add_sized(egui::Vec2::new(input_w, ROW_HEIGHT), egui::TextEdit::singleline(&mut self.debug_axis_ly).hint_text(""));
                            });
                            ui.horizontal(|ui| {
                                ui.add_sized(egui::Vec2::new(label_w, ROW_HEIGHT), egui::Label::new("rx"));
                                ui.add_sized(egui::Vec2::new(input_w, ROW_HEIGHT), egui::TextEdit::singleline(&mut self.debug_axis_rx).hint_text(""));
                                ui.add_sized(egui::Vec2::new(label_w, ROW_HEIGHT), egui::Label::new("ry"));
                                ui.add_sized(egui::Vec2::new(input_w, ROW_HEIGHT), egui::TextEdit::singleline(&mut self.debug_axis_ry).hint_text(""));
                            });
                            ui.horizontal(|ui| {
                                ui.add_sized(egui::Vec2::new(label_w, ROW_HEIGHT), egui::Label::new("lt"));
                                ui.add_sized(egui::Vec2::new(input_w, ROW_HEIGHT), egui::TextEdit::singleline(&mut self.debug_axis_lt).hint_text(""));
                                ui.add_sized(egui::Vec2::new(label_w, ROW_HEIGHT), egui::Label::new("rt"));
                                ui.add_sized(egui::Vec2::new(input_w, ROW_HEIGHT), egui::TextEdit::singleline(&mut self.debug_axis_rt).hint_text(""));
                            });
                            
                            ui.add_sized(egui::Vec2::new(CHARACTER_WIDTH * 4.6, ROW_HEIGHT), egui::Label::new("button"));
                            ui.horizontal(|ui| {
                                ui.add_sized(egui::Vec2::new(label_w, ROW_HEIGHT), egui::Label::new("lb"));
                                ui.add_sized(egui::Vec2::new(input_w, ROW_HEIGHT), egui::TextEdit::singleline(&mut self.debug_btn_lb).hint_text(""));
                                ui.add_sized(egui::Vec2::new(label_w, ROW_HEIGHT), egui::Label::new("rb"));
                                ui.add_sized(egui::Vec2::new(input_w, ROW_HEIGHT), egui::TextEdit::singleline(&mut self.debug_btn_rb).hint_text(""));
                            });
                            ui.horizontal(|ui| {
                                ui.add_sized(egui::Vec2::new(label_w, ROW_HEIGHT), egui::Label::new("ls"));
                                ui.add_sized(egui::Vec2::new(input_w, ROW_HEIGHT), egui::TextEdit::singleline(&mut self.debug_btn_ls).hint_text(""));
                                ui.add_sized(egui::Vec2::new(label_w, ROW_HEIGHT), egui::Label::new("rs"));
                                ui.add_sized(egui::Vec2::new(input_w, ROW_HEIGHT), egui::TextEdit::singleline(&mut self.debug_btn_rs).hint_text(""));
                            });
                            ui.horizontal(|ui| {
                                ui.add_sized(egui::Vec2::new(label_w, ROW_HEIGHT), egui::Label::new("back"));
                                ui.add_sized(egui::Vec2::new(input_w, ROW_HEIGHT), egui::TextEdit::singleline(&mut self.debug_btn_back).hint_text(""));
                                ui.add_sized(egui::Vec2::new(label_w, ROW_HEIGHT), egui::Label::new("start"));
                                ui.add_sized(egui::Vec2::new(input_w, ROW_HEIGHT), egui::TextEdit::singleline(&mut self.debug_btn_start).hint_text(""));
                            });
                            ui.horizontal(|ui| {
                                ui.add_sized(egui::Vec2::new(label_w, ROW_HEIGHT), egui::Label::new("X"));
                                ui.add_sized(egui::Vec2::new(input_w, ROW_HEIGHT), egui::TextEdit::singleline(&mut self.debug_btn_x).hint_text(""));
                                ui.add_sized(egui::Vec2::new(label_w, ROW_HEIGHT), egui::Label::new("Y"));
                                ui.add_sized(egui::Vec2::new(input_w, ROW_HEIGHT), egui::TextEdit::singleline(&mut self.debug_btn_y).hint_text(""));
                            });
                            ui.horizontal(|ui| {
                                ui.add_sized(egui::Vec2::new(label_w, ROW_HEIGHT), egui::Label::new("A"));
                                ui.add_sized(egui::Vec2::new(input_w, ROW_HEIGHT), egui::TextEdit::singleline(&mut self.debug_btn_a).hint_text(""));
                                ui.add_sized(egui::Vec2::new(label_w, ROW_HEIGHT), egui::Label::new("B"));
                                ui.add_sized(egui::Vec2::new(input_w, ROW_HEIGHT), egui::TextEdit::singleline(&mut self.debug_btn_b).hint_text(""));
                            });

                            // 中间：调试输出（固定区域，无滚动条）
                            ui.add_sized(
                                egui::Vec2::new(CHARACTER_WIDTH * 5.0, ROW_HEIGHT),
                                egui::Label::new("调试输出"),
                            );

                            let available_for_scroll = ui.available_height() - ROW_HEIGHT * 2.0 - SPACING * 2.0;
                            let text_area_height = available_for_scroll.max(ROW_HEIGHT * 3.0);

                            let (rect, _) = ui.allocate_exact_size(
                                egui::Vec2::new(dialog_width - SPACING * 2.0, text_area_height),
                                egui::Sense::hover(),
                            );

                            let text = modules::bg_con_reading::get_debug_text();
                            ui.painter().text(
                                rect.min,
                                egui::Align2::LEFT_TOP,
                                text,
                                egui::TextStyle::Monospace.resolve(ui.style()),
                                ui.visuals().text_color(),
                            );
                                                        
                            // 底部：关闭按钮
                            ui.horizontal(|ui| {
                                ui.add_sized(egui::Vec2::new((dialog_width - CHARACTER_WIDTH * 3.0) / 2.0 - SPACING * 1.75, ROW_HEIGHT), egui::Label::new(""));
                                if ui.add_sized(egui::Vec2::new(CHARACTER_WIDTH * 3.0, ROW_HEIGHT), egui::Button::new("关闭")).clicked() {
                                    self.show_debug_window = false;
                                    self.debug_enabled = false;
                                    modules::bg_con_reading::set_debug_print_enabled(false);
                                    self.mapping_manager.stop_con_reader_for_debug();
                                    self.mark_config_changed();
                                    self.save_config();
                                }
                            });
                        });
                });
        }
        
        // 显示帮助窗口
        if self.show_help_window {
            // 创建模态遮罩层，阻止主窗口交互
            modal_blocker(ctx);
            
            // 使用缓存的检测结果
            let vigem_ready = self.help_window_vigem_ready.unwrap_or(false);
            let controller_ready = self.help_window_controller_ready.unwrap_or(false);
            
            // 计算对话框大小和位置（居中显示）
            let dialog_width = CHARACTER_WIDTH * 12.8 + SPACING * 3.5;
            let dialog_height = ROW_HEIGHT * 3.0 + SPACING * 4.0;
            let dialog_pos = egui::pos2(
                ctx.screen_rect().center().x - dialog_width / 2.0,
                ctx.screen_rect().center().y - dialog_height / 2.0,
            );
            
            // 使用 Area 创建帮助对话框
            egui::Area::new(egui::Id::new("help_dialog"))
                .order(egui::Order::Foreground)
                .fixed_pos(dialog_pos)
                .show(ctx, |ui| {
                    // 使用 Frame 绘制窗口样式（无标题）
                    egui::Frame::popup(ui.style())
                        .fill(ctx.style().visuals.window_fill())
                        .show(ui, |ui| {
                            // 第一行：ViGemBus | 就绪/未就绪 | 下载按钮
                            ui.horizontal(|ui| {
                                ui.add_sized(
                                    egui::Vec2::new(CHARACTER_WIDTH * 5.8, ROW_HEIGHT),
                                    egui::Label::new("ViGemBus")
                                );
                                
                                ui.add_sized(
                                    egui::Vec2::new(CHARACTER_WIDTH * 4.0, ROW_HEIGHT),
                                    egui::Label::new(
                                        egui::RichText::new(if vigem_ready { "就绪" } else { "未就绪" })
                                            .color(if vigem_ready { GREEN } else { RED })
                                    )
                                );
                                
                                if ui.add_sized(
                                    egui::Vec2::new(CHARACTER_WIDTH * 3.0, ROW_HEIGHT),
                                    egui::Button::new("下载")
                                ).clicked() {
                                    // 打开下载网页
                                    let url = "https://github.com/nefarius/ViGEmBus/releases/download/v1.22.0/ViGEmBus_1.22.0_x64_x86_arm64.exe";
                                    if let Err(e) = open::that(url) {
                                        eprintln!("无法打开网页: {}", e);
                                    }
                                }
                            });
                            
                            // 第二行：物理手柄 | 就绪/未就绪 | （留空）
                            ui.horizontal(|ui| {
                                ui.add_sized(
                                    egui::Vec2::new(CHARACTER_WIDTH * 5.8, ROW_HEIGHT),
                                    egui::Label::new("物理手柄")
                                );
                                
                                ui.add_sized(
                                    egui::Vec2::new(CHARACTER_WIDTH * 4.0, ROW_HEIGHT),
                                    egui::Label::new(
                                        egui::RichText::new(if controller_ready { "就绪" } else { "未就绪" })
                                            .color(if controller_ready { GREEN } else { RED })
                                    )
                                );
                                
                                // 留空
                                ui.add_sized(
                                    egui::Vec2::new(CHARACTER_WIDTH * 3.0, ROW_HEIGHT),
                                    egui::Label::new("")
                                );
                            });
                            
                            // 关闭按钮
                            ui.horizontal(|ui| {
                                ui.add_sized(
                                    egui::Vec2::new(
                                        (dialog_width - CHARACTER_WIDTH * 3.0) / 2.0 - SPACING * 1.75, 
                                        ROW_HEIGHT
                                    ),
                                    egui::Label::new("")
                                );
                                
                                if ui.add_sized(
                                    egui::Vec2::new(CHARACTER_WIDTH * 3.0, ROW_HEIGHT),
                                    egui::Button::new("关闭")
                                ).clicked() {
                                    self.show_help_window = false;
                                    // 关闭时清空检测结果，下次打开时重新检测
                                    self.help_window_vigem_ready = None;
                                    self.help_window_controller_ready = None;
                                }
                            });
                        });
                });
        }
    }
}

