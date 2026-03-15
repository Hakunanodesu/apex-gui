//! 编译时扫描 gun_template/*.png 并生成嵌入模块，供 weapon_rec_thread 使用。
//! 将 3mz_ds_ver.png 转为 ICO 并设为 exe 图标。

use std::env;
use std::fs;
use std::path::Path;

fn main() {
    let manifest_dir = env::var("CARGO_MANIFEST_DIR").expect("CARGO_MANIFEST_DIR");
    let template_dir = Path::new(&manifest_dir).join("gun_template");
    let out_dir = env::var("OUT_DIR").expect("OUT_DIR");
    // 生成的模板表写到源码目录，方便用相对路径 include!，避免依赖编译期的 OUT_DIR 环境变量。
    let src_out_path = Path::new(&manifest_dir).join("src").join("gun_templates.rs");

    let mut entries: Vec<(String, String)> = Vec::new(); // (stem, filename)

    if template_dir.is_dir() {
        if let Ok(dir) = fs::read_dir(&template_dir) {
            for entry in dir {
                let entry = match entry {
                    Ok(e) => e,
                    Err(_) => continue,
                };
                let path = entry.path();
                if path.extension().map_or(false, |e| e == "png") {
                    let filename = path
                        .file_name()
                        .and_then(|s| s.to_str())
                        .unwrap_or("")
                        .to_string();
                    let stem = path
                        .file_stem()
                        .and_then(|s| s.to_str())
                        .unwrap_or("")
                        .to_string();
                    if !stem.is_empty() {
                        entries.push((stem, filename));
                    }
                }
            }
        } else {
            eprintln!("cargo:warning=gun_template 目录读取失败，将不嵌入模板图片");
        }
    }

    entries.sort_by(|a, b| a.0.cmp(&b.0));

    let mut code = String::from(
        "// 由 build.rs 自动生成，请勿手改\n\n\
         /// 编译时嵌入的枪械模板：(名称无后缀, PNG 字节)\n\
         pub const TEMPLATE_FILES: &[(&str, &[u8])] = &[\n",
    );

    for (stem, filename) in &entries {
        // include_bytes! 路径相对于 CARGO_MANIFEST_DIR 的写法
        code.push_str(&format!(
            "    (r#\"{}\"#, include_bytes!(concat!(env!(\"CARGO_MANIFEST_DIR\"), \"/gun_template/{}\"))),\n",
            stem.replace('\\', "\\\\").replace('"', "\\\""),
            filename.replace('\\', "\\\\").replace('"', "\\\"")
        ));
    }

    code.push_str("];\n");

    fs::write(&src_out_path, code).expect("写入 gun_templates.rs 失败");
    println!("cargo:rerun-if-changed=gun_template");
    println!("cargo:rerun-if-changed=3mz_ds_ver.png");

    // 将 3mz_ds_ver.png 转为多尺寸 ICO 并嵌入为 exe 图标（资源管理器/任务栏）
    let png_path = Path::new(&manifest_dir).join("3mz_ds_ver.png");
    if png_path.exists() {
        if let Ok(img) = image::open(&png_path) {
            const SIZES: &[u32] = &[16, 32, 48, 256]; // 资源管理器常用尺寸
            let mut frames = Vec::new();
            for &size in SIZES {
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
