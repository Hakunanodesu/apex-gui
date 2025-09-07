// 只在 release 时生效
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
        driver_path_exist, get_exe_path, enumerate_controllers, enumerate_pico,
        get_text_width, enumerate_onnx_files
    }, 
    ui::{
        toggle_switch, DownloadState, download_widget, show_preview_panel, 
        show_square_viewport, show_param_curve, show_add_game_window, truncate_text
    },
    bg_dl_instl::spawn_download_thread,
    console_redirect::{ConsoleRedirector},
    config::{UserConfig, load_config, save_config, DeviceConfig, handle_config_changes},
};
use crate::modules::{
    hidhide::run_hidhidecli,
    mapping_state_machine::MappingManager,
};

fn main() -> eframe::Result {
    // 初始化控制台错误重定向
    let _console_redirector = ConsoleRedirector::init().unwrap();
    
    // 添加白名单 & 启动屏蔽 & 重枚举
    run_hidhidecli(&["--app-reg", get_exe_path().unwrap().to_str().unwrap()]).unwrap();
    run_hidhidecli(&["--cloak-on"]).unwrap();

    // 卡密
    let mut kami: String = String::new();
    let mut con_exist = enumerate_controllers();
    let mut pico_exist = enumerate_pico();
    let mut dl_state_vigembus = DownloadState::Idle;
    let mut dl_label_vigembus = String::new();
    let mut dl_cancel_vigembus = Arc::new(AtomicBool::new(false));
    let mut dl_state_hidhide = DownloadState::Idle;
    let mut dl_label_hidhide = String::new();
    let mut dl_cancel_hidhide = Arc::new(AtomicBool::new(false));
    let mut installing = false;

    let vg_client = Arc::new(Client::connect().unwrap());
    let mut allow_mapping = true;
    let mut show_preview = false;
    let mut on_top = false;
    let mut _show_config = false;

    // ====== 配置文件读取 ======
    let mut user_config = load_config();
    let mut current_profile = user_config.current_profile.clone();
    let mut current_device = user_config.current_device.clone();
    let mut current_model = user_config.current_model.clone();
    let mut selected_profile = current_profile.clone();
    let mut selected_model = current_model.clone();
    let mut show_add_game_dialog = false;
    let mut new_game_name = String::new();
    let mut available_models = enumerate_onnx_files();
    let device_config = user_config.profiles
        .get(&current_profile)
        .and_then(|profile_config| profile_config.get(&current_device))
        .cloned()
        .unwrap_or_default();

    let outer_size = Arc::new(Mutex::new(device_config.outer_size));
    let mid_size = Arc::new(Mutex::new(device_config.mid_size));
    let inner_size = Arc::new(Mutex::new(device_config.inner_size));
    let outer_str = Arc::new(Mutex::new(device_config.outer_str));
    let inner_str = Arc::new(Mutex::new(device_config.inner_str));
    let init_str = Arc::new(Mutex::new(device_config.init_str));
    let hipfire = Arc::new(Mutex::new(device_config.hipfire));
    let vertical_str = Arc::new(Mutex::new(device_config.vertical_str));
    let aim_height = Arc::new(Mutex::new(device_config.aim_height));
    let mouse_mode = Arc::new(Mutex::new(current_device == "mouse"));
    // =========================

    // 创建状态机管理器
    let mut mapping_manager = MappingManager::new(
        vg_client.clone(),
        current_model.clone(),
        outer_size.clone(),
        mid_size.clone(),
        inner_size.clone(),
        outer_str.clone(),
        inner_str.clone(),
        init_str.clone(),
        hipfire.clone(),
        vertical_str.clone(),
        aim_height.clone(),
    );

    // 为保存配置克隆变量
    let outer_size_for_save = outer_size.clone();
    let mid_size_for_save = mid_size.clone();
    let inner_size_for_save = inner_size.clone();
    let outer_str_for_save = outer_str.clone();
    let inner_str_for_save = inner_str.clone();
    let init_str_for_save = init_str.clone();
    let hipfire_for_save = hipfire.clone();
    let vertical_str_for_save = vertical_str.clone();
    let aim_height_for_save = aim_height.clone();
    let mouse_mode_for_save = mouse_mode.clone();

    let mut do_resize = true;
    let (window_w, window_h) = (260.0, 272.0);
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

                // ====== 状态机更新 ======
                let (state_do_resize, _state_show_config, state_show_preview) = 
                    mapping_manager.update(&mut con_exist, &mut pico_exist);
                
                if state_do_resize {
                    do_resize = true;
                }
                if state_show_preview && !show_preview {
                    show_preview = state_show_preview;
                }
                // ========================

                // 检查配置变化
                if handle_config_changes(
                    &selected_profile,
                    &selected_model,
                    &mut current_profile,
                    &mut current_device,
                    &mut current_model,
                    mouse_mode.lock().unwrap().clone(),
                    &outer_size,
                    &mid_size,
                    &inner_size,
                    &outer_str,
                    &inner_str,
                    &init_str,
                    &hipfire,
                    &vertical_str,
                    &aim_height,
                ) {
                    // 参数发生变化，强制重新渲染
                    ctx.request_repaint();
                    // 同步配置到状态机
                    mapping_manager.update_config(current_model.clone());
                }

                // —— 第一行：选择游戏 开关 —— 
                ui.horizontal(|ui| {
                    ui.label("选择游戏");
                    // 下拉框选择游戏，但不修改实际参数，只是切换选择
                    let combo = egui::ComboBox::new("game_select_combobox", "")
                        .selected_text(truncate_text(&selected_profile, 10))
                        .width(100.0)
                        .show_ui(ui, |cb_ui| {
                            for game in user_config.profiles.keys() {
                                let item_response = cb_ui.selectable_value(&mut selected_profile, game.clone(), truncate_text(game, 15));
                                if item_response.hovered() {
                                    show_tooltip_at_pointer(
                                        &cb_ui.ctx(),
                                        cb_ui.layer_id(),
                                        item_response.id,
                                        |ui| {
                                            ui.label(game);
                                        }
                                    );
                                }
                            }
                        });
                    if combo.response.hovered() {
                        show_tooltip_at_pointer(
                            &ui.ctx(),
                            ui.layer_id(),
                            combo.response.id,
                            |ui| {
                                ui.label(&selected_profile);
                            }
                        );
                    }
                    // 添加游戏按钮
                    if ui.button("➕").clicked() {
                        show_add_game_dialog = true;
                        new_game_name.clear();
                    }
                    // 删除游戏按钮
                    ui.add_enabled_ui(!user_config.profiles.is_empty(), |ui| {
                        if ui.button("➖").clicked() {
                            let mut updated_config = user_config.clone();
                            if updated_config.remove_profile(&selected_profile) {
                                save_config(&updated_config);
                                user_config = updated_config;
                                selected_profile = user_config.current_profile.clone();
                            }
                        }
                    });
                });
                // 添加游戏弹窗
                if show_add_game_dialog {
                    if let Some(game_name) = show_add_game_window(ctx, &mut show_add_game_dialog, &mut new_game_name) {
                        let mut updated_config = user_config.clone();
                        if updated_config.add_profile(game_name) {
                            save_config(&updated_config);
                            user_config = updated_config;
                            selected_profile = user_config.current_profile.clone();
                        }
                    }
                }
                ui.add_space(7.0);

                // —— 第二行：选择模型 开关 —— 
                ui.horizontal(|ui| {
                    ui.label("选择模型");
                    // 模型选择下拉框
                    let combo_response = egui::ComboBox::new("model_select_combobox", "")
                        .selected_text(if selected_model.is_empty() {
                            "".to_string()
                        } else {
                            truncate_text(&selected_model, 14)
                        })
                        .width(129.0)
                        .show_ui(ui, |cb_ui| {
                            // 添加所有ONNX文件
                            for model_file in &available_models {
                                let item_response = cb_ui.selectable_value(&mut selected_model, model_file.clone(), truncate_text(model_file, 21));
                                if item_response.hovered() {
                                    show_tooltip_at_pointer(
                                        &cb_ui.ctx(),
                                        cb_ui.layer_id(),
                                        item_response.id,
                                        |ui| {
                                            ui.label(model_file);
                                        }
                                    );
                                }
                            }
                        });
                    // 显示完整模型名的提示
                    if combo_response.response.hovered() && !selected_model.is_empty() {
                        show_tooltip_at_pointer(
                            &ui.ctx(),
                            ui.layer_id(),
                            combo_response.response.id,
                            |ui| {
                                ui.label(&selected_model);
                            }
                        );
                    }
                    // 刷新模型列表按钮
                    ui.add_enabled_ui(!mapping_manager.is_active(), |ui| {
                        if ui.button("🔄️").clicked() {
                            available_models = enumerate_onnx_files();
                        }
                    });
                });
                ui.add_space(7.0);
                
                egui::Grid::new("main_grid")
                    .spacing([10.0, 10.0])
                    .striped(false)
                    .show(ui, |ui| {
                        // —— 第三行：ViGemBus 安装按钮 —— 
                        ui.label("ViGemBus");
                        // 这里原封不动放你 download_widget 的代码
                        if driver_path_exist("ViGEm Bus Driver") {
                            ui.colored_label(Color32::GREEN, "✔");
                            dl_label_vigembus = "重新安装".to_owned();
                        } else {
                            ui.colored_label(Color32::RED, "✖");
                            dl_label_vigembus = "安装".to_owned();
                        }
                        ui.add_enabled_ui(!mouse_mode.lock().unwrap().clone(), |ui| {
                            if matches!(dl_state_vigembus, DownloadState::Idle) {
                                ui.add_enabled_ui(!mapping_manager.is_active(), |ui| {
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
                        });
                        ui.end_row();
            
                        // —— 第四行：HidHide 安装按钮 —— 
                        ui.label("HidHide");
                        if driver_path_exist("HidHide") {
                            ui.colored_label(Color32::GREEN, "✔");
                            dl_label_hidhide = "重新安装".to_owned();
                        } else {
                            ui.colored_label(Color32::RED,   "✖");
                            dl_label_hidhide = "安装".to_owned();
                        }
                        ui.add_enabled_ui(!mouse_mode.lock().unwrap().clone(), |ui| {
                            if matches!(dl_state_hidhide, DownloadState::Idle) {
                                ui.add_enabled_ui(!mapping_manager.is_active(), |ui| {
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
                        });
                        ui.end_row();
            
                        // —— 第五行：已识别手柄列表 —— 
                        if mouse_mode.lock().unwrap().clone() {
                            ui.label("存在Pico");
                            if pico_exist {
                                ui.colored_label(Color32::GREEN, "✔");
                            } else {
                                ui.colored_label(Color32::RED, "✖");
                            }
                        } else {
                            ui.label("存在手柄");
                            if con_exist {
                                ui.colored_label(Color32::GREEN, "✔");
                            } else {
                                ui.colored_label(Color32::RED, "✖");
                            }
                        }
                        ui.add_enabled_ui(!installing && !installing && !mapping_manager.is_active(), |ui| {
                            let btn = ui.button("🔄️ ⓘ");
                            if !mouse_mode.lock().unwrap().clone() {
                                if btn.hovered() {
                                    show_tooltip_at_pointer(
                                        &ui.ctx(),
                                        ui.layer_id(),
                                        btn.id,
                                        |ui| {
                                            ui.add(
                                                Label::new(
                                                    egui::RichText::new(
                                                        "⚠️\nXbox 系手柄在每次软件打开并检测到后需重新连接手柄\n⚠️"
                                                    ).color(Color32::ORANGE)
                                                )
                                            );
                                        }
                                    );
                                }
                            }
                            if btn.clicked() {
                                if mouse_mode.lock().unwrap().clone() {
                                    pico_exist = enumerate_pico();
                                } else {
                                    con_exist = enumerate_controllers();
                                }
                            }
                        });
                        ui.end_row();
            
                        // —— 第六行：智能映射 开关 —— 
                        ui.label("智能映射");
                        ui.add_enabled_ui(
                            (
                                con_exist 
                                && !installing 
                                && allow_mapping 
                                && !mouse_mode.lock().unwrap().clone()
                            ) 
                            || (
                                pico_exist 
                                && mouse_mode.lock().unwrap().clone()
                            ),
                            |ui| {
                            let mut mapping_active = mapping_manager.is_active();
                            if ui.add(toggle_switch(&mut mapping_active)).clicked() {
                                if mapping_active {
                                    let mouse_mode_val = mouse_mode.lock().unwrap().clone();
                                    mapping_manager.request_start(mouse_mode_val);
                                } else {
                                    mapping_manager.request_stop();
                                }
                            }
                        });
                        ui.label("键鼠模式");
                        {
                            let mut mouse_mode_guard = mouse_mode.lock().unwrap();
                            if ui.add(toggle_switch(&mut *mouse_mode_guard)).clicked() {}
                        }
                        ui.end_row();
                        
                        if mapping_manager.is_active() {
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
                                } else {
                                    do_resize = true;
                                    ctx.send_viewport_cmd(
                                        ViewportCommand::WindowLevel(
                                            WindowLevel::Normal
                                        )
                                    );
                                }
                            }
                            // 置顶开关
                            ui.label("窗口置顶");
                            ui.add(toggle_switch(&mut on_top));
                            ui.end_row();
                        };
                        if on_top {
                            ctx.send_viewport_cmd(
                                ViewportCommand::WindowLevel(
                                    WindowLevel::AlwaysOnTop
                                )
                            );
                        } else {
                            ctx.send_viewport_cmd(
                                ViewportCommand::WindowLevel(
                                    WindowLevel::Normal
                                )
                            );
                        }
                    });

                // 识别参数设置
                if !mapping_manager.is_active() {
                    ui.add_space(7.0);
                    let ch = egui::CollapsingHeader::new("参数设置")
                        .default_open(false)
                        .show(ui, |ui| {
                            // 坐标轴曲线
                            show_param_curve(
                                ui,
                                &outer_size.lock().unwrap(),
                                &mid_size.lock().unwrap(),
                                &inner_size.lock().unwrap(),
                                &outer_str.lock().unwrap(),
                                &inner_str.lock().unwrap(),
                                &init_str.lock().unwrap(),
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

                            let mut inner_str_err = None;
                            let mut init_str_err = None;
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
                                let init_str_guard = init_str.lock().unwrap();
                                if !init_str_guard.trim().is_empty() {
                                    match init_str_guard.trim().parse::<f32>() {
                                        Ok(v) if v < 0.0 || v > 1.0 => init_str_err = Some("范围0.0-1.0"),
                                        Ok(_) => {},
                                        Err(_) => init_str_err = Some("格式错误"),
                                    }
                                }
                            }
                            {
                                let hipfire_guard = hipfire.lock().unwrap();
                                if !hipfire_guard.trim().is_empty() {
                                    match hipfire_guard.trim().parse::<f32>() {
                                        Ok(_) => {},
                                        Err(_) => hipfire_err = Some("格式错误"),
                                    }
                                }
                            }

                            ui.add_space(4.0);
                            egui::Grid::new("param_grid").spacing([10.0, 5.0]).show(ui, |ui| {
                                ui.label("外圈大小");
                                let mut outer_guard = outer_size.lock().unwrap();
                                ui.add(TextEdit::singleline(&mut *outer_guard).hint_text(""));
                                // 外圈强度输入（0.0-1.0）
                                ui.label("外圈强度");
                                let mut outer_str_guard = outer_str.lock().unwrap();
                                ui.add(TextEdit::singleline(&mut *outer_str_guard).hint_text(""));
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
                                ui.add(TextEdit::singleline(&mut *mid_guard).hint_text(""));
                                ui.label(""); // 占位符，保持布局对齐
                                ui.label(""); // 占位符，保持布局对齐
                                if let Some(err) = mid_err {
                                    ui.colored_label(Color32::RED, err);
                                } else {
                                    ui.label("");
                                }
                                ui.end_row();
                                ui.label("内圈大小");
                                let mut inner_guard = inner_size.lock().unwrap();
                                ui.add(TextEdit::singleline(&mut *inner_guard).hint_text(""));
                                // 内圈强度输入（0.0-1.0）
                                ui.label("内圈强度");
                                let mut inner_str_guard = inner_str.lock().unwrap();
                                ui.add(TextEdit::singleline(&mut *inner_str_guard).hint_text(""));
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
                                ui.add(TextEdit::singleline(&mut *hipfire_guard).hint_text(""));
                                ui.label("起步强度");
                                let mut init_str_guard = init_str.lock().unwrap();
                                ui.add(TextEdit::singleline(&mut *init_str_guard).hint_text(""));
                                if let Some(err) = hipfire_err {
                                    ui.colored_label(Color32::RED, err);
                                } else if let Some(err) = init_str_err {
                                    ui.colored_label(Color32::RED, err);
                                } else {
                                    ui.label("");
                                }
                                ui.end_row();
                            });
                            // 垂直强度滑块
                            ui.add_space(4.0);
                            ui.horizontal(|ui| {
                                ui.label("垂直强度");
                                let mut vertical_str_val = vertical_str.lock().unwrap().trim().parse::<f32>().unwrap_or(0.5);
                                if ui.add(egui::Slider::new(&mut vertical_str_val, 0.0..=1.0)).changed() {
                                    *vertical_str.lock().unwrap() = format!("{:.2}", vertical_str_val);
                                }
                            });
                            // 瞄准高度滑块
                            ui.add_space(4.0);
                            ui.horizontal(|ui| {
                                ui.label("瞄准高度");
                                let mut aim_height_val = aim_height.lock().unwrap().trim().parse::<f32>().unwrap_or(0.5);
                                if ui.add(egui::Slider::new(&mut aim_height_val, 0.0..=1.0)).changed() {
                                    *aim_height.lock().unwrap() = format!("{:.2}", aim_height_val);
                                }
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
                            window_h + 308.0
                        )));
                        allow_mapping = false;
                    } else {
                        do_resize = true;
                        allow_mapping = true;
                    }
                }

                ui.add_space(7.0);
                // 预览面板
                if show_preview && mapping_manager.is_active() {
                    let capturer = mapping_manager.get_screen_capturer().as_ref().map(|c| (c.buffer(), c.square_size));
                    let detector = mapping_manager.get_detector().as_ref().map(|d| d.result());
                    show_preview_panel(ui, capturer, detector);
                }                
            });

            // —— 底部延迟展示 —— 
            TopBottomPanel::bottom("lag_show_panel")
                .resizable(false)
                .show(ctx, |ui| {
                    ui.add_space(1.0);
                    if let Some(det) = mapping_manager.get_detector().as_ref() {
                        let fps_arc = det.fps();
                        if let Ok(fps_guard) = fps_arc.lock() {
                            ui.label(format!("推理帧率: {:.0} fps", *fps_guard));
                        } else {
                            ui.label("推理帧率: 0 fps");
                        }
                    } else {
                        ui.label("推理帧率: 0 fps");
                    }
                    ui.add_space(0.0);
                });
            
            // 卡密输入框
            TopBottomPanel::bottom("kami_input")
                .resizable(false) // 禁止拖拽调整大小
                .show(ctx, |ui| {
                    ui.add_enabled_ui(!mapping_manager.is_active(), |ui| {
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
    let mut updated_config: UserConfig = load_config();  // 加载当前配置以保留其他游戏的设置
    let device_config = DeviceConfig {
        outer_size: outer_size_for_save.lock().unwrap().clone(),
        mid_size: mid_size_for_save.lock().unwrap().clone(),
        inner_size: inner_size_for_save.lock().unwrap().clone(),
        outer_str: outer_str_for_save.lock().unwrap().clone(),
        inner_str: inner_str_for_save.lock().unwrap().clone(),
        init_str: init_str_for_save.lock().unwrap().clone(),
        hipfire: hipfire_for_save.lock().unwrap().clone(),
        vertical_str: vertical_str_for_save.lock().unwrap().clone(),
        aim_height: aim_height_for_save.lock().unwrap().clone(),
    };

    // 更新当前游戏和设备的配置
    let current_device_final = if mouse_mode_for_save.lock().unwrap().clone() {
        "mouse".to_string()
    } else {
        "controller".to_string()
    };
    
    if let Some(profile_config) = updated_config.profiles.get_mut(&updated_config.current_profile) {
        profile_config.insert(current_device_final.clone(), device_config);
    }
    updated_config.current_device = current_device_final;
    // 模型选择通过handle_config_changes处理，这里保持最新值
    let latest_config = load_config();
    updated_config.current_model = latest_config.current_model;
    
    save_config(&updated_config);
    // =========================
    run_hidhidecli(&["--cloak-off"]).unwrap();
    Ok(())
}