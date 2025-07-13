#![cfg_attr(not(debug_assertions), windows_subsystem = "windows")]

use std::{
    sync::Arc,
    sync::Mutex,
    sync::atomic::AtomicBool,
};
use eframe::{
    {NativeOptions, run_simple_native},
    egui::{
        Color32, TextStyle, Label, vec2,
        FontData, FontDefinitions, FontFamily, 
        TopBottomPanel, Visuals, TextEdit,
        containers::show_tooltip_at_pointer,
        ViewportCommand, WindowLevel,
        ViewportBuilder,
    }
};
use vigem_client::Client;

mod utils;
mod modules;
use crate::utils::{
    tools::{
        driver_path_exist, get_exe_path, enumerate_controllers, 
        get_text_width, UserConfig, load_config, save_config
    }, 
    ui::{
        toggle_switch, DownloadState, download_widget, show_preview_panel, 
        show_square_viewport, show_param_curve
    },
    bg_dl_instl::spawn_download_thread,
};
use crate::modules::{
    bg_con_reading::ConReader,
    bg_con_mapping::ConMapper,
    hidhide::run_hidhidecli,
    bg_screen_cap::ScreenCapturer,
    bg_onnx_dml_od::{DetectorThread},
};

fn main() -> eframe::Result {
    // 添加白名单 & 启动屏蔽 & 重枚举
    run_hidhidecli(&["--app-reg", get_exe_path().unwrap().to_str().unwrap()]).unwrap();
    run_hidhidecli(&["--cloak-on"]).unwrap();

    // 卡密
    let mut kami: String = String::new();
    let mut con_names = enumerate_controllers().unwrap();
    let mut dl_state_vigembus = DownloadState::Idle;
    let mut dl_label_vigembus = String::new();
    let mut dl_cancel_vigembus = Arc::new(AtomicBool::new(false));
    let mut dl_state_hidhide = DownloadState::Idle;
    let mut dl_label_hidhide = String::new();
    let mut dl_cancel_hidhide = Arc::new(AtomicBool::new(false));
    let mut installing = false;

    let vg_client = Arc::new(Client::connect().unwrap());
    let mut con_reader: Option<ConReader> = None;
    let mut con_mapper: Option<ConMapper> = None;
    let mut mapping_active = false; // 映射状态
    let mut show_config = false;
    let mut screen_capturer: Option<ScreenCapturer> = None;
    let mut detector: Option<DetectorThread> = None;
    let mut show_preview = false;

    // ====== 配置文件读取 ======
    let user_config = load_config();
    let outer_size = Arc::new(Mutex::new(user_config.outer_size));
    let mid_size = Arc::new(Mutex::new(user_config.mid_size));
    let inner_size = Arc::new(Mutex::new(user_config.inner_size));
    let outer_str = Arc::new(Mutex::new(user_config.outer_str));
    let mid_str = Arc::new(Mutex::new(user_config.mid_str));
    let inner_str = Arc::new(Mutex::new(user_config.inner_str));
    let deadzone = Arc::new(Mutex::new(user_config.deadzone.unwrap_or_else(|| "0.0".to_string())));
    let hipfire = Arc::new(Mutex::new(user_config.hipfire.unwrap_or_else(|| "0.0".to_string())));
    // =========================

    // 为保存配置克隆变量
    let outer_size_for_save = outer_size.clone();
    let mid_size_for_save = mid_size.clone();
    let inner_size_for_save = inner_size.clone();
    let outer_str_for_save = outer_str.clone();
    let mid_str_for_save = mid_str.clone();
    let inner_str_for_save = inner_str.clone();
    let deadzone_for_save = deadzone.clone();
    let hipfire_for_save = hipfire.clone();

    let mut last_log = String::from("初始化成功"); // 仅保留最新一条日志
    let mut do_resize = true;
    let mut on_top = false;
    let (window_w, window_h) = (280.0, 293.0);
    let options = NativeOptions {
        viewport: ViewportBuilder::default()
            .with_resizable(false),
        ..Default::default()
    };
    run_simple_native(
        "Assisted Pursuit with Effortless eXecution",
        options,
        move |ctx, _frame| {
            if do_resize {
                ctx.send_viewport_cmd(ViewportCommand::InnerSize(vec2(
                    window_w, 
                    window_h
                )));
                do_resize = false;
            }
            // 深色模式
            ctx.set_visuals(Visuals::dark());
            // —— 安装中文字体 —— 
            let mut fonts = FontDefinitions::default();
            fonts.font_data.insert(
                "noto_sans_sc".to_owned(),
                Arc::new(FontData::from_static(
                    include_bytes!("fonts/NotoSansCJKsc-Regular.otf")
                )),
            );
            let prop = fonts
                .families
                .get_mut(&FontFamily::Proportional)
                .unwrap();
            prop.insert(0, "noto_sans_sc".to_owned());
            fonts.font_data.insert(
                "emoji_glyf".to_owned(),
                Arc::new(FontData::from_static(
                    include_bytes!("fonts/OpenMoji-black-glyf.ttf")
                )),
            );
            prop.push("emoji_glyf".to_owned());
            ctx.set_fonts(fonts);

            let total_w = ctx.available_rect().width();
            egui::CentralPanel::default().show(ctx, |ui| {
                let col_0_w = get_text_width(ui, "ViGemBus", TextStyle::Button);
                let right_column_w = (total_w - col_0_w * 2.7).max(0.0);
                installing = !matches!(dl_state_vigembus, DownloadState::Idle)
                    || !matches!(dl_state_hidhide, DownloadState::Idle);
                egui::Grid::new("main_grid")
                    .spacing([10.0, 10.0])
                    .striped(false)
                    .show(ui, |ui| {
                        // —— 第一行：ViGemBus 安装按钮 —— 
                        ui.label("ViGemBus");
                        // 这里原封不动放你 download_widget 的代码
                        if driver_path_exist("ViGEm Bus Driver") {
                            ui.colored_label(Color32::GREEN, "✔");
                            dl_label_vigembus = "重新安装".to_owned();
                        } else {
                            ui.colored_label(Color32::RED, "✖");
                            dl_label_vigembus = "安装".to_owned();
                        }
                        if matches!(dl_state_vigembus, DownloadState::Idle) {
                            ui.add_enabled_ui(!mapping_active, |ui| {
                                if ui.button(&dl_label_vigembus).clicked() {
                                    dl_cancel_vigembus = Arc::new(AtomicBool::new(false));
                                    let arc = spawn_download_thread(
                                        "https://github.com/nefarius/ViGEmBus/releases/download/v1.22.0/ViGEmBus_1.22.0_x64_x86_arm64.exe",
                                        "ViGemBus_installer.exe",
                                        dl_cancel_vigembus.clone(),
                                    );
                                    dl_state_vigembus = DownloadState::Downloading(arc, dl_cancel_vigembus.clone());
                                }
                            });
                        } else if let DownloadState::Downloading(_, cancel_flag) = &mut dl_state_vigembus {
                            if ui.button("取消安装").clicked() {
                                cancel_flag.store(true, std::sync::atomic::Ordering::SeqCst);
                                dl_state_vigembus = DownloadState::Cancelling { cancelled_at: None };
                            }
                            download_widget(
                                ctx, ui, &mut dl_state_vigembus,
                                right_column_w, "ViGemBus_installer.exe"
                            );
                        } else {
                            // 非 Idle/Downloading 时，调用进度渲染
                            download_widget(
                                ctx, ui, &mut dl_state_vigembus,
                                right_column_w, "ViGemBus_installer.exe"
                            );
                        }
                        ui.end_row();
            
                        // —— 第二行：HidHide 安装按钮 —— 
                        ui.label("HidHide");
                        if driver_path_exist("HidHide") {
                            ui.colored_label(Color32::GREEN, "✔");
                            dl_label_hidhide = "重新安装".to_owned();
                        } else {
                            ui.colored_label(Color32::RED,   "✖");
                            dl_label_hidhide = "安装".to_owned();
                        }
                        if matches!(dl_state_hidhide, DownloadState::Idle) {
                            ui.add_enabled_ui(!mapping_active, |ui| {
                                if ui.button(&dl_label_hidhide).clicked() {
                                    dl_cancel_hidhide = Arc::new(AtomicBool::new(false));
                                    let arc = spawn_download_thread(
                                        "https://github.com/nefarius/HidHide/releases/download/v1.5.230.0/HidHide_1.5.230_x64.exe",
                                        "HidHide_installer.exe",
                                        dl_cancel_hidhide.clone(),
                                    );
                                    dl_state_hidhide = DownloadState::Downloading(arc, dl_cancel_hidhide.clone());
                                }
                            });
                        } else if let DownloadState::Downloading(_, cancel_flag) = &mut dl_state_hidhide {
                            if ui.button("取消安装").clicked() {
                                cancel_flag.store(true, std::sync::atomic::Ordering::SeqCst);
                                dl_state_hidhide = DownloadState::Cancelling { cancelled_at: None };
                            }
                            download_widget(
                                ctx, ui, &mut dl_state_hidhide, 
                                right_column_w, "HidHide_installer.exe"
                            );
                        } else {
                            download_widget(
                                ctx, ui, &mut dl_state_hidhide, 
                                right_column_w, "HidHide_installer.exe"
                            );
                        }
                        ui.end_row();
            
                        // —— 第三行：已识别手柄列表 —— 
                        ui.label("存在手柄");
                        if con_names.is_empty() {
                            ui.colored_label(Color32::RED, "✖");
                        } else {
                            ui.colored_label(Color32::GREEN, "✔");
                        }
                        ui.add_enabled_ui(!mapping_active && !installing, |ui| {
                            let btn = ui.button("刷新 ⓘ");
                            if btn.hovered() {
                                show_tooltip_at_pointer(
                                    &ui.ctx(),
                                    ui.layer_id(),
                                    btn.id,
                                    |ui| {
                                        if con_names.is_empty() {
                                            ui.add(
                                                Label::new("未列出手柄，请刷新")
                                            );
                                        } else {
                                            ui.add(
                                                Label::new(
                                                    egui::RichText::new(
                                                        "⚠️\nXbox 系手柄在每次软件打开并检测到后需重新连接手柄\n⚠️"
                                                    ).color(Color32::ORANGE)
                                                )
                                            );
                                            ui.label("当前已检测到的手柄：");
                                            for (i, name) in con_names.iter().enumerate() {
                                                ui.add(
                                                    Label::new(format!("{}) {}", i + 1, name))
                                                );
                                            }
                                        };
                                    }
                                );
                            }
                            if btn.clicked() {
                                con_names = 
                                    enumerate_controllers().unwrap();
                            }
                        });
                        ui.end_row();
            
                        // —— 第四行：智能映射 开关 —— 
                        ui.label("智能映射");
                        ui.add_enabled_ui(!con_names.is_empty() && !installing, |ui| {
                            if ui.add(toggle_switch(&mut mapping_active)).clicked() {
                                if mapping_active {
                                    con_names = enumerate_controllers().unwrap();
                                    if !con_names.is_empty() {
                                        // 1. 启动屏幕抓取（只做一次）
                                        if screen_capturer.is_none() {
                                            let outer_guard = outer_size.clone();
                                            let outer_val = 
                                                outer_guard
                                                    .lock().unwrap().trim()
                                                    .parse::<f32>().unwrap_or(320.0);
                                            let outer_usize = outer_val.round() as usize;
                                            screen_capturer = Some(
                                                ScreenCapturer::start(outer_usize)
                                                    .expect("无法启动 ScreenCapturer")
                                            );
                                        }
                                        
                                        // 2. 初始化 detector（依赖 screen_capturer）
                                        if detector.is_none() {
                                            if let Some(capt) = screen_capturer.as_ref() {
                                                let buffer_arc = capt.buffer();
                                                detector = Some(
                                                    DetectorThread::start(buffer_arc)
                                                        .expect("无法启动 DetectorThread")
                                                );
                                            } else {
                                                last_log = "错误：屏幕捕获器未启动".to_owned();
                                                mapping_active = false;
                                            }
                                        }
                                        
                                        // 3. 启动读取线程
                                        if con_reader.is_none() {
                                            con_reader = Some(ConReader::start());
                                        }
                                        
                                        // 4. 启动映射器（依赖 detector 和 reader）
                                        if con_mapper.is_none() && detector.is_some() {
                                            if let Some(reader) = con_reader.as_ref() {
                                                if let Some(det) = detector.as_ref() {
                                                    let state = reader.state();
                                                    let ready = reader.ready();
                                                    let outer_val = outer_size.lock().unwrap().trim().parse::<f32>().unwrap_or(320.0);
                                                    let mid_val = mid_size.lock().unwrap().trim().parse::<f32>().unwrap_or(200.0);
                                                    let inner_val = inner_size.lock().unwrap().trim().parse::<f32>().unwrap_or(100.0);
                                                    let outer_str_val = outer_str.lock().unwrap().trim().parse::<f32>().unwrap_or(1.0);
                                                    let mid_str_val = mid_str.lock().unwrap().trim().parse::<f32>().unwrap_or(1.0);
                                                    let inner_str_val = inner_str.lock().unwrap().trim().parse::<f32>().unwrap_or(1.0);
                                                    let deadzone_val = deadzone.lock().unwrap().trim().parse::<f32>().unwrap_or(0.0);
                                                    let hipfire_val = hipfire.lock().unwrap().trim().parse::<f32>().unwrap_or(0.0);
                                                    
                                                    con_mapper = Some(ConMapper::start(
                                                        state,
                                                        vg_client.clone(),
                                                        ready,
                                                        Some(det.result()),
                                                        outer_val,
                                                        mid_val,
                                                        inner_val,
                                                        outer_str_val,
                                                        mid_str_val,
                                                        inner_str_val,
                                                        deadzone_val,
                                                        hipfire_val
                                                    ));
                                                    last_log = "启动智能映射".to_owned();
                                                } else {
                                                    last_log = "错误：检测器未启动".to_owned();
                                                    mapping_active = false;
                                                }
                                            } else {
                                                last_log = "错误：手柄读取器未启动".to_owned();
                                                mapping_active = false;
                                            }
                                        } else if con_mapper.is_some() {
                                            last_log = "映射已存在，无需重复启动".to_owned();
                                        }
                                        do_resize = true;
                                        show_config = false;
                                    } else {
                                        mapping_active = false;
                                    }
                                } else {
                                    // 按照启动的反序关闭组件
                                    // 1. 先关闭映射器（最后启动的）
                                    if let Some(mapper) = con_mapper.take() {
                                        mapper.stop();
                                    }
                                    // 2. 关闭读取线程
                                    if let Some(reader) = con_reader.take() {
                                        reader.stop();
                                    }
                                    // 3. 关闭检测器
                                    if let Some(det) = detector.take() {
                                        det.stop();
                                    }
                                    // 4. 最后关闭屏幕捕获器（最先启动的）
                                    if let Some(capt) = screen_capturer.take() {
                                        capt.stop();
                                    }
                                    last_log = "停止智能映射".to_owned();
                                    do_resize = true;
                                    show_preview = false;
                                    on_top = false;
                                }
                            }
                        });
                        ui.end_row();
                        
                        if mapping_active {
                            // 预览开关
                            ui.label("识别预览");
                            if ui.add(toggle_switch(&mut show_preview)).clicked() {
                                if show_preview {
                                    let outer_guard = outer_size.clone();
                                    let outer_val = 
                                        outer_guard
                                            .lock().unwrap().trim()
                                            .parse::<f32>().unwrap_or(320.0);
                                    ctx.send_viewport_cmd(ViewportCommand::InnerSize(vec2(
                                        f32::max(window_w, outer_val / ctx.pixels_per_point() + 16.0), 
                                        window_h + outer_val / ctx.pixels_per_point() + 16.0
                                    )));
                                    last_log = "打开识别内容预览".to_owned();
                                } else {
                                    do_resize = true;
                                    on_top = false;
                                    last_log = "关闭识别内容预览".to_owned();
                                }
                            }
                            // 置顶开关
                            ui.label("窗口置顶");
                            ui.add_enabled_ui(show_preview, |ui| {
                                if ui.add(toggle_switch(&mut on_top)).clicked() {
                                    if on_top {
                                        ctx.send_viewport_cmd(
                                            ViewportCommand::WindowLevel(
                                                WindowLevel::AlwaysOnTop
                                            )
                                        );
                                        last_log = "窗口已置顶".to_owned();
                                    } else {
                                        ctx.send_viewport_cmd(
                                            ViewportCommand::WindowLevel(
                                                WindowLevel::Normal
                                            )
                                        );
                                        last_log = "窗口取消置顶".to_owned();
                                    }
                                }
                            });
                            ui.end_row();
                        };
                    });

                // 识别参数设置
                if !mapping_active {
                    ui.add_space(7.0);
                    let ch = egui::CollapsingHeader::new("参数设置")
                        .default_open(false)
                        .show(ui, |ui| {
                            // 坐标轴曲线
                            show_param_curve(
                                ui,
                                &mid_size.lock().unwrap(),
                                &inner_size.lock().unwrap(),
                                &outer_str.lock().unwrap(),
                                &mid_str.lock().unwrap(),
                                &inner_str.lock().unwrap(),
                                &deadzone.lock().unwrap(),
                            );
                            let monitor_size = 
                                ctx.input(|i| i.viewport().monitor_size)
                                    .unwrap_or(vec2(1920.0, 1080.0));
                            let max_size = monitor_size.y * ctx.pixels_per_point();
                            let mut outer_f32: Option<f32> = None;
                            let mut mid_f32: Option<f32> = None;
                            let mut inner_f32: Option<f32> = None;
                            let mut outer_err = None;
                            let mut mid_err = None;
                            let mut inner_err = None;
                            let mut outer_str_err = None;
                            let mut mid_str_err = None;
                            let mut inner_str_err = None;
                            let mut deadzone_err = None;
                            let mut hipfire_err = None;
                            {
                                let outer_guard = outer_size.lock().unwrap();
                                if !outer_guard.trim().is_empty() {
                                    match outer_guard.trim().parse::<f32>() {
                                        Ok(v) => {
                                            if v > max_size {
                                                outer_err = Some("超出最大值");
                                            } else {
                                                outer_f32 = Some(v);
                                            }
                                        },
                                        Err(_) => outer_err = Some("格式错误"),
                                    }
                                }
                            }
                            {
                                let outer_str_guard = outer_str.lock().unwrap();
                                if !outer_str_guard.trim().is_empty() {
                                    match outer_str_guard.trim().parse::<f32>() {
                                        Ok(v) if v < 0.0 || v > 1.0 => outer_str_err = Some("范围0.0-1.0"),
                                        Ok(_) => {},
                                        Err(_) => outer_str_err = Some("格式错误"),
                                    }
                                }
                            }
                            {
                                let mid_guard = mid_size.lock().unwrap();
                                if !mid_guard.trim().is_empty() {
                                    match mid_guard.trim().parse::<f32>() {
                                        Ok(v) => {
                                            if v > max_size {
                                                mid_err = Some("超出最大值");
                                            } else {
                                                mid_f32 = Some(v);
                                            }
                                        },
                                        Err(_) => mid_err = Some("格式错误"),
                                    }
                                }
                            }
                            {
                                let mid_str_guard = mid_str.lock().unwrap();
                                if !mid_str_guard.trim().is_empty() {
                                    match mid_str_guard.trim().parse::<f32>() {
                                        Ok(v) if v < 0.0 || v > 1.0 => mid_str_err = Some("范围0.0-1.0"),
                                        Ok(_) => {},
                                        Err(_) => mid_str_err = Some("格式错误"),
                                    }
                                }
                            }
                            {
                                let inner_guard = inner_size.lock().unwrap();
                                if !inner_guard.trim().is_empty() {
                                    match inner_guard.trim().parse::<f32>() {
                                        Ok(v) => {
                                            if v > max_size {
                                                inner_err = Some("超出最大值");
                                            } else {
                                                inner_f32 = Some(v);
                                            }
                                        },
                                        Err(_) => inner_err = Some("格式错误"),
                                    }
                                }
                            }
                            {
                                let inner_str_guard = inner_str.lock().unwrap();
                                if !inner_str_guard.trim().is_empty() {
                                    match inner_str_guard.trim().parse::<f32>() {
                                        Ok(v) if v < 0.0 || v > 1.0 => inner_str_err = Some("范围0.0-1.0"),
                                        Ok(_) => {},
                                        Err(_) => inner_str_err = Some("格式错误"),
                                    }
                                }
                            }
                            {
                                let deadzone_guard = deadzone.lock().unwrap();
                                if !deadzone_guard.trim().is_empty() {
                                                                    match deadzone_guard.trim().parse::<f32>() {
                                    Ok(v) if v < 0.0 || v > 1.0 => deadzone_err = Some("范围0.0-1.0"),
                                    Ok(_) => {},
                                    Err(_) => deadzone_err = Some("格式错误"),
                                }
                                }
                            }
                            {
                                let hipfire_guard = hipfire.lock().unwrap();
                                if !hipfire_guard.trim().is_empty() {
                                                                    match hipfire_guard.trim().parse::<f32>() {
                                    Ok(v) if v < 0.0 || v > 1.0 => hipfire_err = Some("范围0.0-1.0"),
                                    Ok(_) => {},
                                    Err(_) => hipfire_err = Some("格式错误"),
                                }
                                }
                            }
                            ui.add_space(4.0);
                            egui::Grid::new("param_grid").spacing([10.0, 5.0]).show(ui, |ui| {
                                let edit_height = ui.text_style_height(&egui::TextStyle::Body);
                                ui.label("外圈大小");
                                let mut outer_guard = outer_size.lock().unwrap();
                                ui.add_sized(
                                    [40.0, edit_height], 
                                    TextEdit::singleline(&mut *outer_guard).hint_text("")
                                );
                                // 外圈强度输入（0.0-1.0）
                                ui.label("外圈强度");
                                let mut outer_str_guard = outer_str.lock().unwrap();
                                ui.add_sized(
                                    [40.0, edit_height],
                                    TextEdit::singleline(&mut *outer_str_guard).hint_text("")
                                );
                                if let Some(err) = outer_err {
                                    ui.colored_label(Color32::RED, err);
                                } else if let Some(err) = outer_str_err {
                                    ui.colored_label(Color32::RED, err);
                                } else {
                                    ui.label("");
                                }
                                ui.end_row();
                                ui.label("中圈大小");
                                let mut mid_guard = mid_size.lock().unwrap();
                                ui.add_sized(
                                    [40.0, edit_height], 
                                    TextEdit::singleline(&mut *mid_guard).hint_text("")
                                );
                                // 中圈强度输入（0.0-1.0）
                                ui.label("中圈强度");
                                let mut mid_str_guard = mid_str.lock().unwrap();
                                ui.add_sized(
                                    [40.0, edit_height],
                                    TextEdit::singleline(&mut *mid_str_guard).hint_text("")
                                );
                                if let Some(err) = mid_err {
                                    ui.colored_label(Color32::RED, err);
                                } else if let Some(err) = mid_str_err {
                                    ui.colored_label(Color32::RED, err);
                                } else {
                                    ui.label("");
                                }
                                ui.end_row();
                                ui.label("内圈大小");
                                let mut inner_guard = inner_size.lock().unwrap();
                                ui.add_sized(
                                    [40.0, edit_height], 
                                    TextEdit::singleline(&mut *inner_guard).hint_text("")
                                );
                                // 内圈强度输入（0.0-1.0）
                                ui.label("内圈强度");
                                let mut inner_str_guard = inner_str.lock().unwrap();
                                ui.add_sized(
                                    [40.0, edit_height],
                                    TextEdit::singleline(&mut *inner_str_guard).hint_text("")
                                );
                                if let Some(err) = inner_err {
                                    ui.colored_label(Color32::RED, err);
                                } else if let Some(err) = inner_str_err {
                                    ui.colored_label(Color32::RED, err);
                                } else {
                                    ui.label("");
                                }
                                ui.end_row();
                                // 腰射和死区（0.0-1.0）
                                ui.label("腰射系数");
                                let mut hipfire_guard = hipfire.lock().unwrap();
                                ui.add_sized(
                                    [40.0, edit_height],
                                    TextEdit::singleline(&mut *hipfire_guard).hint_text("")
                                );
                                ui.label("死区大小");
                                let mut deadzone_guard = deadzone.lock().unwrap();
                                ui.add_sized(
                                    [40.0, edit_height],
                                    TextEdit::singleline(&mut *deadzone_guard).hint_text("")
                                );
                                if let Some(err) = hipfire_err {
                                    ui.colored_label(Color32::RED, err);
                                } else if let Some(err) = deadzone_err {
                                    ui.colored_label(Color32::RED, err);
                                } else {
                                    ui.label("");
                                }
                                ui.end_row();
                            });
                            let outer = outer_f32.unwrap_or(0.0) / ctx.pixels_per_point();
                            let mid = mid_f32.unwrap_or(0.0) / ctx.pixels_per_point();
                            let inner = inner_f32.unwrap_or(0.0) / ctx.pixels_per_point();
                            show_square_viewport(ctx, outer, mid, inner, false);
                            show_square_viewport(ctx, outer, mid, inner, true);
                        });
                    if ch.body_returned.is_some() {
                        ctx.send_viewport_cmd(ViewportCommand::InnerSize(vec2(
                            window_w, 
                            window_h + 255.0
                        )));
                    } else {
                        do_resize = true;
                    }
                }

                ui.add_space(7.0);
                // 预览面板
                if show_preview && mapping_active {
                    let capturer = screen_capturer.as_ref().map(|c| (c.buffer(), c.square_size));
                    let detector = detector.as_ref().map(|d| d.result());
                    show_preview_panel(ui, capturer, detector);
                }                
            });

            // —— 底部延迟展示 —— 
            TopBottomPanel::bottom("lag_show_panel")
                .resizable(false)
                .show(ctx, |ui| {
                    // 新增：显示ConReader采样率
                    if let Some(reader) = con_reader.as_ref() {
                        let fps_arc = reader.hz();
                        if let Ok(fps_guard) = fps_arc.lock() {
                            ui.label(format!("原始采样率: {} hz", *fps_guard));
                        } else {
                            ui.label("原始采样率: 0 hz");
                        }
                    } else {
                        ui.label("原始采样率: 0 hz");
                    }
                    // 新增：显示ConMapper采样率
                    if let Some(mapper) = con_mapper.as_ref() {
                        let fps_arc = mapper.hz();
                        if let Ok(fps_guard) = fps_arc.lock() {
                            ui.label(format!("映射采样率: {} hz", *fps_guard));
                        } else {
                            ui.label("映射采样率: 0 hz");
                        }
                    } else {
                        ui.label("映射采样率: 0 hz");
                    }
                    if let Some(capturer) = screen_capturer.as_ref() {
                        let fps = capturer.fps();
                        ui.label(format!("捕获帧率: {:.0} fps", fps));
                    } else {
                        ui.label("捕获帧率: 0 fps");
                    }
                    if let Some(det) = detector.as_ref() {
                        let fps_arc = det.fps();
                        if let Ok(fps_guard) = fps_arc.lock() {
                            ui.label(format!("推理帧率: {:.0} fps", *fps_guard));
                        } else {
                            ui.label("推理帧率: 0 fps");
                        }
                    } else {
                        ui.label("推理帧率: 0 fps");
                    }
                });
            
            // —— 底部固定单行日志展示 —— 
            TopBottomPanel::bottom("log_panel")
                .resizable(false)
                .show(ctx, |ui| {
                    ui.label("上一次操作：".to_owned() + &last_log);
                });

            // 卡密输入框
            TopBottomPanel::bottom("kami_input")
                .resizable(false) // 禁止拖拽调整大小
                .show(ctx, |ui| {
                    ui.add_enabled_ui(!mapping_active, |ui| {
                        ui.add_sized(
                            [(total_w - 16.0).max(0.0), 0.0],
                            TextEdit::singleline(&mut kami)
                                .hint_text("在此输入卡密..."),
                        );
                    });
                });
        }
    )?;
    // ====== 配置文件写回 ======
    save_config(&UserConfig {
        outer_size: outer_size_for_save.lock().unwrap().clone(),
        mid_size: mid_size_for_save.lock().unwrap().clone(),
        inner_size: inner_size_for_save.lock().unwrap().clone(),
        outer_str: outer_str_for_save.lock().unwrap().clone(),
        mid_str: mid_str_for_save.lock().unwrap().clone(),
        inner_str: inner_str_for_save.lock().unwrap().clone(),
        deadzone: Some(deadzone_for_save.lock().unwrap().clone()),
        hipfire: Some(hipfire_for_save.lock().unwrap().clone()),
    });
    // =========================
    run_hidhidecli(&["--cloak-off"]).unwrap();
    Ok(())
}