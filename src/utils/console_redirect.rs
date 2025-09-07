use std::fs::OpenOptions;
use std::io::Write;
use std::sync::{Arc, Mutex};
use chrono::Local;

// 全局日志文件句柄
static LOG_FILE: std::sync::OnceLock<Arc<Mutex<Option<std::fs::File>>>> = std::sync::OnceLock::new();

/// 简单的控制台错误重定向器
pub struct ConsoleRedirector {
    _marker: (),
}

impl ConsoleRedirector {
    /// 初始化控制台错误重定向
    pub fn init() -> Result<Self, Box<dyn std::error::Error>> {
        // 创建logs目录
        std::fs::create_dir_all("logs")?;
        let now = Local::now();
        let log_file_path = format!("logs/console_errors_{}.log", now.format("%Y%m%d"));
        
        // 创建日志文件
        let file = OpenOptions::new()
            .create(true)
            .append(true)
            .open(&log_file_path)?;
        
        // 初始化全局日志文件句柄
        let log_file = Arc::new(Mutex::new(Some(file)));
        LOG_FILE.set(log_file.clone()).map_err(|_| "Failed to initialize log file")?;
        
        // 设置panic hook来捕获panic信息
        let log_file_clone = log_file.clone();
        std::panic::set_hook(Box::new(move |panic_info| {
            let now = Local::now();
            let timestamp = now.format("%Y-%m-%d %H:%M:%S%.3f");
            
            let location = panic_info.location()
                .map(|l| format!("{}:{}:{}", l.file(), l.line(), l.column()))
                .unwrap_or_else(|| "unknown location".to_string());
            
            let message = if let Some(s) = panic_info.payload().downcast_ref::<&str>() {
                s.to_string()
            } else if let Some(s) = panic_info.payload().downcast_ref::<String>() {
                s.clone()
            } else {
                "Unknown panic message".to_string()
            };
            
            let panic_log = format!("[{}] PANIC at {}: {}\n", timestamp, location, message);
            
            // 写入panic信息到文件
            if let Ok(mut file_guard) = log_file_clone.lock() {
                if let Some(ref mut file) = *file_guard {
                    let _ = file.write_all(panic_log.as_bytes());
                    let _ = file.flush();
                }
            }
            
            // 如果有控制台，也输出到控制台（可选）
            #[cfg(debug_assertions)]
            eprintln!("{}", panic_log.trim());
        }));
        
        // 只在debug模式下显示启动信息
        #[cfg(debug_assertions)]
        println!("控制台错误输出重定向已启用，错误信息将保存到: {}", log_file_path);
        
        Ok(ConsoleRedirector {
            _marker: (),
        })
    }
}

/// 记录错误信息到日志文件
pub fn log_error(message: &str) {
    if let Some(log_file) = LOG_FILE.get() {
        let now = Local::now();
        let timestamp = now.format("%Y-%m-%d %H:%M:%S%.3f");
        let log_entry = format!("[{}] ERROR: {}\n", timestamp, message);
        
        if let Ok(mut file_guard) = log_file.lock() {
            if let Some(ref mut file) = *file_guard {
                let _ = file.write_all(log_entry.as_bytes());
                let _ = file.flush();
            }
        }
    }
    
    // 同时输出到stderr（保持原有行为）
    eprintln!("{}", message);
}

 