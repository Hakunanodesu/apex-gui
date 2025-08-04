use std::fs::OpenOptions;
use std::io::Write;
use std::sync::{Arc, Mutex};
use chrono::Local;

/// 简单的控制台错误重定向器
pub struct ConsoleRedirector {
    _log_file: Arc<Mutex<std::fs::File>>,
}

impl ConsoleRedirector {
    /// 初始化控制台错误重定向
    pub fn init() -> Result<Self, Box<dyn std::error::Error>> {
        // 创建logs目录
        std::fs::create_dir_all("logs")?;
        
        // 创建日志文件
        let now = Local::now();
        let log_file_path = format!("logs/console_errors_{}.log", now.format("%Y%m%d"));
        
        let log_file = OpenOptions::new()
            .create(true)
            .append(true)
            .open(&log_file_path)?;
        
        let log_file = Arc::new(Mutex::new(log_file));
        
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
            
            // 写入到文件（主要功能）
            if let Ok(mut file) = log_file_clone.lock() {
                let _ = file.write_all(panic_log.as_bytes());
                let _ = file.flush();
            }
            
            // 如果有控制台，也输出到控制台（可选）
            #[cfg(debug_assertions)]
            eprintln!("{}", panic_log.trim());
        }));
        
        // 只在debug模式下显示启动信息
        #[cfg(debug_assertions)]
        println!("控制台错误输出重定向已启用，日志文件: {}", log_file_path);
        
        // 在日志文件中记录启动信息
        if let Ok(mut file) = log_file.lock() {
            let startup_log = format!("[{}] 程序启动，错误日志记录已初始化\n", 
                now.format("%Y-%m-%d %H:%M:%S%.3f"));
            let _ = file.write_all(startup_log.as_bytes());
            let _ = file.flush();
        }
        
        Ok(ConsoleRedirector {
            _log_file: log_file,
        })
    }
}

 