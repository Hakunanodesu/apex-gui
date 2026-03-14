//! 编译时扫描 gun_template/*.png 并生成嵌入模块，供 weapon_rec_thread 使用。

use std::env;
use std::fs;
use std::path::Path;

fn main() {
    let manifest_dir = env::var("CARGO_MANIFEST_DIR").expect("CARGO_MANIFEST_DIR");
    let template_dir = Path::new(&manifest_dir).join("gun_template");
    let out_dir = env::var("OUT_DIR").expect("OUT_DIR");
    let out_path = Path::new(&out_dir).join("gun_templates.rs");

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

    fs::write(&out_path, code).expect("写入 gun_templates.rs 失败");
    println!("cargo:rerun-if-changed=gun_template");
}
