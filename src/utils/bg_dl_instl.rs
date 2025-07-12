use std::{
    fs::File,
    io::{Read, Write},
    path::Path,
    sync::{Arc, atomic::{AtomicU32, Ordering}},
    thread,
};
use std::process::Command;
use reqwest::blocking;
use std::sync::atomic::AtomicBool;

/// 启动一个下载线程，返回一个 AtomicU32 的 Arc 用于跟踪下载进度（0 到 100）
///
/// # 参数
/// - `url`: 要下载文件的 URL
/// - `output_path`: 下载后保存到本地的路径
///
/// # 返回值
/// 一个 Arc<AtomicU32>，可在 UI线程中读取当前下载进度百分比
pub fn spawn_download_thread(url: &str, output_path: &str, cancel_flag: Arc<AtomicBool>) -> Arc<AtomicU32> {
    let progress = Arc::new(AtomicU32::new(0));
    let progress_clone = Arc::clone(&progress);
    let url = url.to_string();
    let output_path = output_path.to_string();
    let cancel_flag_clone = Arc::clone(&cancel_flag);

    thread::spawn(move || {
        // 发起阻塞请求
        let resp = blocking::get(&url);
        if resp.is_err() {
            progress_clone.store(100, Ordering::SeqCst);
            return;
        }
        let mut resp = resp.unwrap();

        let total = resp.content_length().unwrap_or(0);
        if total == 0 {
            progress_clone.store(100, Ordering::SeqCst);
            return;
        }

        let mut out = File::create(Path::new(&output_path)).unwrap();
        let mut buf = [0u8; 8192];
        let mut downloaded = 0u64;

        while let Ok(n) = resp.read(&mut buf) {
            if n == 0 {
                break;
            }
            if cancel_flag_clone.load(Ordering::SeqCst) {
                // 取消下载，直接返回
                return;
            }
            out.write_all(&buf[..n]).unwrap();
            downloaded += n as u64;
            let pct = ((downloaded as f64 / total as f64) * 100.0).min(100.0);
            progress_clone.store(pct as u32, Ordering::SeqCst);
        }

        progress_clone.store(100, Ordering::SeqCst);
    });

    progress
}

// 后台线程执行安装，并把结果写入 AtomicU32
pub fn spawn_install_thread(exe_path: std::path::PathBuf) -> Arc<AtomicU32> {
    let status = Arc::new(AtomicU32::new(0));
    let status_clone = status.clone();
    thread::spawn(move || {
        let result = Command::new(&exe_path)
            .spawn()
            .and_then(|mut child| child.wait());
        let ok = result.map(|st| st.success()).unwrap_or(false);
        status_clone.store(if ok { 1 } else { 2 }, Ordering::SeqCst);
    });
    status
}