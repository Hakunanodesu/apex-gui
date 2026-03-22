//! 编译时扫描 gun_templates/*.png 并生成嵌入模块，供 weapon_rec_thread 使用。
//! 将 3mz_ds_ver.png 转为 ICO 并设为 exe 图标。

use std::env;
use std::fs;
use std::path::Path;

#[allow(dead_code)]
mod shared_constants {
    include!("src/shared_constants.rs");
}
use shared_constants::build::ICON_SIZES;
use shared_constants::weapon_rec::{TEMPLATE_H, TEMPLATE_W};

fn main() {
    let manifest_dir = env::var("CARGO_MANIFEST_DIR").expect("CARGO_MANIFEST_DIR");
    let template_dir = Path::new(&manifest_dir).join("gun_templates");
    let out_dir = env::var("OUT_DIR").expect("OUT_DIR");
    // 生成的模板表写到源码目录，方便用相对路径 include!，避免依赖编译期的 OUT_DIR 环境变量。
    let build_dir = Path::new(&manifest_dir).join("src").join("build");
    let src_out_path = build_dir.join("gun_templates.rs");
    fs::create_dir_all(&build_dir).expect("创建 src/build 目录失败");

    let mut entries: Vec<(String, u32, u32, Vec<u8>)> = Vec::new(); // (stem, width, height, gray_raw)
    let mut size_errors: Vec<String> = Vec::new();

    if template_dir.is_dir() {
        if let Ok(dir) = fs::read_dir(&template_dir) {
            for entry in dir {
                let entry = match entry {
                    Ok(e) => e,
                    Err(_) => continue,
                };
                let path = entry.path();
                if path.extension().map_or(false, |e| e == "png") {
                    let stem = path
                        .file_stem()
                        .and_then(|s| s.to_str())
                        .unwrap_or("")
                        .to_string();
                    if !stem.is_empty() {
                        match image::open(&path) {
                            Ok(img) => {
                                let luma = img.to_luma8();
                                let (w, h) = (luma.width(), luma.height());
                                if w != TEMPLATE_W || h != TEMPLATE_H {
                                    size_errors.push(format!(
                                        "{} => {}x{} (期望 {}x{})",
                                        path.display(),
                                        w,
                                        h,
                                        TEMPLATE_W,
                                        TEMPLATE_H
                                    ));
                                    continue;
                                }
                                entries.push((stem, w, h, luma.into_raw()));
                            }
                            Err(e) => {
                                eprintln!(
                                    "cargo:warning=模板图片解码失败（{}）：{}",
                                    path.display(),
                                    e
                                );
                            }
                        }
                    }
                }
            }
        } else {
            eprintln!("cargo:warning=gun_templates 目录读取失败，将不嵌入模板图片");
        }
    }

    if !size_errors.is_empty() {
        panic!(
            "模板尺寸校验失败：以下文件不是 {}x{}\n{}",
            TEMPLATE_W,
            TEMPLATE_H,
            size_errors.join("\n")
        );
    }

    entries.sort_by(|a, b| a.0.cmp(&b.0));

    let mut code = String::from(
        "// 由 build.rs 自动生成，请勿手改\n\n\
         /// 编译时嵌入的枪械模板：(名称无后缀, 宽, 高, 灰度原始字节)\n\
         pub const TEMPLATE_FILES: &[(&str, u32, u32, &[u8])] = &[\n",
    );

    for (stem, w, h, gray_raw) in &entries {
        let bytes = gray_raw
            .iter()
            .map(|b| b.to_string())
            .collect::<Vec<_>>()
            .join(", ");
        code.push_str(&format!(
            "    (r#\"{}\"#, {}, {}, &[{}]),\n",
            stem.replace('\\', "\\\\").replace('"', "\\\""),
            w,
            h,
            bytes
        ));
    }

    code.push_str("];\n");

    fs::write(&src_out_path, code).expect("写入 gun_templates.rs 失败");

    // 与嵌入模板同源：供 UI「特殊枪械」与连点白名单使用（编译期固定，不依赖运行时目录）
    let stems_out = build_dir.join("rapid_fire_weapon_stems.rs");
    let mut stems_code = String::from(
        "// 由 build.rs 自动生成，请勿手改\n\n\
         /// 连点 / 特殊枪械 UI：与 `gun_templates` 中已通过尺寸校验并嵌入的模板名（无后缀）一致\n\
         pub const RAPID_FIRE_WEAPON_STEMS: &[&str] = &[\n",
    );
    for (stem, _, _, _) in &entries {
        stems_code.push_str(&format!(
            "    r#\"{}\"#,\n",
            stem.replace('\\', "\\\\").replace('"', "\\\"")
        ));
    }
    stems_code.push_str("];\n");
    fs::write(&stems_out, stems_code).expect("写入 rapid_fire_weapon_stems.rs 失败");
    println!("cargo:rerun-if-changed=gun_templates");
    println!("cargo:rerun-if-changed=3mz_ds_ver.png");

    // 将 3mz_ds_ver.png 转为多尺寸 ICO 并嵌入为 exe 图标（资源管理器/任务栏）
    let png_path = Path::new(&manifest_dir).join("3mz_ds_ver.png");
    if png_path.exists() {
        if let Ok(img) = image::open(&png_path) {
            let mut frames = Vec::new();
            for &size in ICON_SIZES {
                let scaled = img.resize(size, size, image::imageops::FilterType::Lanczos3);
                let rgba = scaled.to_rgba8();
                let frame = image::codecs::ico::IcoFrame::as_png(
                    rgba.as_raw(),
                    size,
                    size,
                    image::ExtendedColorType::Rgba8,
                )
                .expect("IcoFrame 创建失败");
                frames.push(frame);
            }
            let ico_path = Path::new(&out_dir).join("app.ico");
            let mut ico_file = fs::File::create(&ico_path).expect("创建 app.ico 失败");
            let encoder = image::codecs::ico::IcoEncoder::new(&mut ico_file);
            encoder.encode_images(&frames).expect("写入 ICO 失败");
            drop(ico_file);
            let mut res = winresource::WindowsResource::new();
            res.set_icon(ico_path.to_str().expect("ico 路径"));
            res.compile().expect(
                "嵌入 exe 图标失败（需安装 Visual Studio 或 Windows SDK 以提供 rc.exe），\
                或请查看上方错误信息",
            );
        } else {
            eprintln!("cargo:warning=无法打开 3mz_ds_ver.png，跳过 exe 图标");
        }
    } else {
        eprintln!("cargo:warning=未找到 3mz_ds_ver.png，跳过 exe 图标");
    }
}
