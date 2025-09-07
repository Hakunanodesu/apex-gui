use std::{
    sync::{
        Arc, Mutex,
        atomic::{AtomicU32, Ordering, AtomicBool},
    },
    time::Instant,
};
use egui::{
    lerp, pos2, vec2, Context, Sense, StrokeKind, Ui, Widget,
    ViewportId, ViewportBuilder, Color32, CentralPanel, Frame, ViewportCommand
};

use crate::utils::bg_dl_instl::spawn_install_thread;
use crate::modules::bg_onnx_dml_od::Detection;

// —— 1. 定义滑块开关 ——
// 返回一个可直接传给 `ui.add(...)` 的 Widget
pub fn toggle_switch(on: &mut bool) -> impl Widget + '_ {
    move |ui: &mut Ui| {
        // 1) 决定尺寸：高为标准交互大小，宽为两倍
        let desired_size = ui.spacing().interact_size.y * vec2(2.0, 1.0);
        // 2) 分配空间并开启点击感知
        let (rect, mut response) = ui.allocate_exact_size(desired_size, Sense::click());
        // 3) 如果点击了就切换状态
        if response.clicked() {
            *on = !*on;
            response.mark_changed();
        }
        // 4) 绘制：依据动画参数平滑地在左右之间插值
        if ui.is_rect_visible(rect) {
            let how_on = ui.ctx().animate_bool_responsive(response.id, *on);
            let visuals = ui.style().interact_selectable(&response, *on);
            let rect = rect.expand(visuals.expansion);
            let radius = rect.height() * 0.5;
            // 背景框
            ui.painter().rect(
                rect,
                radius,
                visuals.bg_fill,
                visuals.bg_stroke,
                StrokeKind::Inside,
            );
            // 圆点
            let center = pos2(
                lerp((rect.left() + radius)..=(rect.right() - radius), how_on),
                rect.center().y,
            );
            ui.painter().circle(center, radius * 0.75, visuals.bg_fill, visuals.fg_stroke);
        }
        response
    }
}

fn show_labeled_progress(
    ui: &mut Ui, progress: f32, width: f32, text: &str, show_pct: bool
) {
    ui.set_max_width(width);
    ui.add_space(2.0);
    if show_pct {
        let percent = (progress * 100.0).round();
        ui.label(format!("{} {:.0}%", text, percent));
    } else {
        ui.label(format!("{}", text));
    }
}

// 状态机
pub enum DownloadState {
    Idle,
    Downloading(Arc<AtomicU32>, Arc<AtomicBool>), // 进度, 取消信号
    Installing(Arc<AtomicU32>), // 0=中，1=成功，2=失败
    Cancelling { cancelled_at: Option<Instant> }, // 改为携带时间
    Done { ok: bool, timestamp: Instant },
}

pub fn download_widget(
    ctx: &Context,
    ui: &mut Ui,
    state: &mut DownloadState,
    width: f32,
    exe_name: &str,
) {
    ui.with_layout(egui::Layout::top_down(egui::Align::Min), |ui| {
        match state {
            DownloadState::Downloading(arc, _cancel_signal) => {
                let pct = arc.load(Ordering::SeqCst) as f32 / 100.0;
                show_labeled_progress(ui, pct, width, "下载中...", true);
                ctx.request_repaint();
                if pct >= 1.0 {
                    // 下载完毕，启动安装线程
                    let exe_path = std::env::current_dir()
                        .unwrap_or_default()
                        .join(match state {
                            DownloadState::Downloading(_, _) => exe_name,
                            _ => unreachable!(),
                        });
                    let install_arc = spawn_install_thread(exe_path);
                    *state = DownloadState::Installing(install_arc);
                }
            }
            DownloadState::Cancelling { cancelled_at } => {
                show_labeled_progress(ui, 0.0, width, "已取消", false);
                ctx.request_repaint();
                let now = Instant::now();
                if cancelled_at.is_none() {
                    *cancelled_at = Some(now);
                } else if let Some(t) = cancelled_at {
                    if t.elapsed().as_secs() > 1 {
                    *state = DownloadState::Idle;
                    }
                }
            }
            DownloadState::Installing(arc) => {
                let code = arc.load(Ordering::SeqCst);
                let msg = match code {
                    1 => "安装成功",
                    2 => "安装失败",
                    _ => "安装中...",
                };
                show_labeled_progress(ui, 1.0, width, msg, false);
                ctx.request_repaint();
                if code == 1 || code == 2 {
                    *state = DownloadState::Done {
                        ok: code == 1,
                        timestamp: Instant::now(),
                    };
                }
            }
            DownloadState::Done { ok, timestamp } => {
                let msg = if *ok { "安装成功" } else { "安装失败" };
                show_labeled_progress(ui, 1.0, width, msg, false);
                ctx.request_repaint();
                if timestamp.elapsed().as_secs() > 3 {
                    *state = DownloadState::Idle;
                }
            }
            // 注意：不再处理 Idle！
            _ => {}
        }
    });
}

pub fn show_preview_panel(
    ui: &mut Ui,
    capturer: Option<(Arc<Mutex<Vec<u8>>>, usize)>,
    detector: Option<Arc<Mutex<Option<Vec<Detection>>>>>,
) {
    if let Some((buffer_arc, square_size)) = &capturer {
        let buf = buffer_arc.lock().unwrap();
        let size = [*square_size, *square_size];
        let mut rgba_buf = Vec::with_capacity(size[0] * size[1] * 4);
        for i in 0..(size[0] * size[1]) {
            let r = buf[i];
            let g = buf[i + size[0] * size[1]];
            let b = buf[i + size[0] * size[1] * 2];
            rgba_buf.push(r);
            rgba_buf.push(g);
            rgba_buf.push(b);
            rgba_buf.push(255); // alpha
        }
        let image = egui::ColorImage::from_rgba_unmultiplied(size, &rgba_buf);

        let texture = ui.ctx().load_texture(
            "capture_preview",
            image,
            egui::TextureOptions::default(),
        );

        let px = *square_size as f32;
        let dpi = ui.ctx().pixels_per_point();
        let size_points = egui::Vec2::new(px, px) / dpi;

        // 画图像
        let (rect, _response) = ui.allocate_exact_size(size_points, egui::Sense::hover());
        ui.painter().image(
            texture.id(),
            egui::Rect::from_min_max(rect.left_top(), rect.right_bottom()),
            egui::Rect::from_min_max(egui::Pos2::ZERO, egui::Pos2::new(1.0, 1.0)),
            egui::Color32::WHITE
        );
        // 添加白色边框
        ui.painter().rect_stroke(
            egui::Rect::from_min_max(rect.left_top(), rect.right_bottom()),
            0.0,
            egui::Stroke::new(2.0, egui::Color32::WHITE),
            egui::StrokeKind::Inside,
        );

        // 叠加边框
        if let Some(result_arc) = &detector {
            if let Ok(res_guard) = result_arc.lock() {
                if let Some(detections) = &*res_guard {
                    if let Some(d) = detections.first() {
                        // 坐标归一化到0~1再映射到rect
                        let cx = d.x;
                        let cy = d.y;
                        let w = d.w;
                        let h = d.h;

                        let x1 = rect.left() + (cx - w / 2.0) / px * rect.width();
                        let y1 = rect.top() + (cy - h / 2.0) / px * rect.height();
                        let x2 = rect.left() + (cx + w / 2.0) / px * rect.width();
                        let y2 = rect.top() + (cy + h / 2.0) / px * rect.height();

                        let r = egui::Rect::from_min_max(
                            egui::pos2(x1, y1),
                            egui::pos2(x2, y2),
                        );
                        ui.painter().rect_stroke(
                            r,
                            0.0,
                            egui::Stroke::new(2.0, egui::Color32::RED),
                            egui::StrokeKind::Inside
                        );
                    }
                }
            }
        }
        
        // 强制请求重绘，确保预览在窗口失去焦点时也能正常刷新
        ui.ctx().request_repaint();
    }
}

pub fn show_square_viewport(ctx: &Context, outer_size: f32, mid_size: f32, inner_size: f32, open: bool) {
    let vp_id = ViewportId::from_hash_of("rec_size");
    if open {
        let monitor_size = ctx.input(|i| i.viewport().monitor_size).unwrap_or(vec2(1920.0, 1080.0));
        let x = (monitor_size.x - outer_size) / 2.0;
        let y = (monitor_size.y - outer_size) / 2.0;
        ctx.show_viewport_deferred(
            vp_id,
            ViewportBuilder::default()
                .with_inner_size(vec2(outer_size, outer_size))
                .with_resizable(false)
                .with_decorations(false)
                .with_title("正方形")
                .with_position(pos2(x, y))
                .with_always_on_top(),
            move |ctx, _class| {
                CentralPanel::default().frame(
                    Frame::NONE
                ).show(ctx, |ui| {
                    let rect = ui.max_rect();
                    // 画大绿色正方形（铺满）
                    ui.painter().rect_filled(rect, 0.0, Color32::LIGHT_GREEN);
                    // 画中圈黄色正方形（居中，边长为mid_size）
                    let center = rect.center();
                    let mid_rect = egui::Rect::from_center_size(center, egui::vec2(mid_size, mid_size));
                    ui.painter().rect_filled(mid_rect, 0.0, egui::Color32::YELLOW);
                    // 画小红色正方形（居中，边长为inner_size）
                    let inner_rect = egui::Rect::from_center_size(center, egui::vec2(inner_size, inner_size));
                    ui.painter().rect_filled(inner_rect, 0.0, egui::Color32::LIGHT_RED);
                });
            },
        );
    } else {
        ctx.send_viewport_cmd_to(vp_id, ViewportCommand::Close);
    }
}

pub fn show_param_curve(
    ui: &mut egui::Ui,
    outer_size: &str,
    mid_size: &str,
    inner_size: &str,
    outer_str: &str,
    inner_str: &str,
    deadzone: &str,
) {
    ui.horizontal(|ui| {
        let (response, painter) = ui.allocate_painter(egui::Vec2::new(230.0, 140.0), egui::Sense::hover());
        let rect = response.rect;
        let padding = 20.0;
        let inner_rect = rect.shrink(padding);
        let to_screen = |x: f32, y: f32| {
            let px = inner_rect.left() + x * inner_rect.width();
            let py = inner_rect.bottom() - y * inner_rect.height();
            egui::pos2(px, py)
        };
        // 坐标轴
        let axis_color = egui::Color32::GRAY;
        painter.line_segment([
            to_screen(0.0, 0.0),
            to_screen(1.0, 0.0)
        ], egui::Stroke::new(1.0, axis_color)); // x轴
        painter.line_segment([
            to_screen(0.0, 0.0),
            to_screen(0.0, 1.0)
        ], egui::Stroke::new(1.0, axis_color)); // y轴
        // 只显示0.0和1.0刻度
        for &y in &[0.0, 1.0] {
            let p1 = to_screen(0.0, y);
            let p2 = to_screen(-0.04, y); // 刻度稍微往左
            painter.line_segment([p1, p2], egui::Stroke::new(1.0, axis_color));
            painter.text(
                egui::pos2(inner_rect.left() - 20.0, p1.y - 7.0),
                egui::Align2::LEFT_TOP,
                format!("{:.1}", y),
                egui::TextStyle::Small.resolve(ui.style()),
                axis_color
            );
        }
        // x轴刻度：0、innersize、midsize、outersize
        let outer_size_val = outer_size.trim().parse::<f32>().unwrap_or(320.0);
        let mid_size_val = mid_size.trim().parse::<f32>().unwrap_or(200.0);
        let inner_size_val = inner_size.trim().parse::<f32>().unwrap_or(80.0);
        let inner_x = if outer_size_val > 0.0 {
            (inner_size_val / outer_size_val).clamp(0.0, 1.0)
        } else {
            0.0
        };
        let mid_x = if outer_size_val > 0.0 {
            (mid_size_val / outer_size_val).clamp(0.0, 1.0)
        } else {
            0.5
        };
        for &(x, label) in &[
            (0.0, "0"),
            (inner_x, &format!("{:.0}", inner_size_val)),
            (mid_x, &format!("{:.0}", mid_size_val)),
            (1.0, &format!("{:.0}", outer_size_val)),
        ] {
            let p1 = to_screen(x, 0.0);
            let p2 = to_screen(x, -0.04); // 刻度稍微往下
            painter.line_segment([p1, p2], egui::Stroke::new(1.0, axis_color));
            painter.text(
                egui::pos2(p1.x - 8.0, inner_rect.bottom() + 8.0),
                egui::Align2::LEFT_TOP,
                label,
                egui::TextStyle::Small.resolve(ui.style()),
                axis_color
            );
        }
        let inner_y = inner_str.trim().parse::<f32>().unwrap_or(0.0).clamp(0.0, 1.0);
        let outer_y = outer_str.trim().parse::<f32>().unwrap_or(0.0).clamp(0.0, 1.0);
        let deadzone_y = deadzone.trim().parse::<f32>().unwrap_or(0.0).clamp(0.0, 1.0);
        // 四个点
        let p0 = to_screen(0.0, deadzone_y);
        let p1 = to_screen(inner_x, inner_y);
        let p2 = to_screen(mid_x, inner_y);  // 中圈点使用内圈强度
        let p3_start = to_screen(mid_x, outer_y);
        let p3_end = to_screen(1.0, outer_y);
        // 连线
        painter.line_segment([p0, p1], egui::Stroke::new(2.0, egui::Color32::LIGHT_GRAY));
        painter.line_segment([p1, p2], egui::Stroke::new(2.0, egui::Color32::LIGHT_GRAY));
        painter.circle_filled(p0, 5.0, egui::Color32::LIGHT_RED);
        painter.circle_filled(p1, 5.0, egui::Color32::LIGHT_RED);
        painter.circle_filled(p2, 5.0, egui::Color32::YELLOW);
        // 画白色水平线从中圈大小到外圈大小
        painter.line_segment([p3_start, p3_end], egui::Stroke::new(2.0, egui::Color32::WHITE));
        // 在水平线两端加上绿色端点
        painter.circle_filled(p3_start, 5.0, egui::Color32::LIGHT_GREEN);
        painter.circle_filled(p3_end, 5.0, egui::Color32::LIGHT_GREEN);
    });
}

pub fn show_add_game_window(
    ctx: &egui::Context,
    show_window: &mut bool,
    new_game_name: &mut String,
) -> Option<String> {
    let mut result = None;
    
    if *show_window {
        egui::Window::new("添加新游戏")
            .collapsible(false)
            .resizable(false)
            .title_bar(false)
            .show(ctx, |ui| {
                let response = ui.add(
                    egui::TextEdit::singleline(new_game_name)
                        .hint_text("输入游戏名称（回车确认）")
                );
                
                if response.lost_focus() && ui.input(|i| i.key_pressed(egui::Key::Enter)) {
                    if !new_game_name.is_empty() {
                        result = Some(new_game_name.clone());
                        *show_window = false;
                        new_game_name.clear();
                    }
                }
            });
    }
    
    result
}

/// 截断文本并添加省略号
/// 
/// # Arguments
/// 
/// * `text` - 要截断的文本
/// * `max_chars` - 最大字符数（包括省略号）
/// 
/// # Returns
/// 
/// 如果文本长度超过max_chars，返回截断后带省略号的文本；否则返回原文本
pub fn truncate_text(text: &str, max_chars: usize) -> String {
    if text.chars().count() <= max_chars {
        text.to_string()
    } else {
        let mut truncated: String = text.chars().take(max_chars - 1).collect();
        truncated.push('.');
        truncated.push('.');
        truncated.push('.');
        truncated
    }
}
