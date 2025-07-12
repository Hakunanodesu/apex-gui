use std::process::{Command, Stdio};
use std::os::windows::process::CommandExt;
use std::io;

const CREATE_NO_WINDOW: u32 = 0x0800_0000;

/// Run HidHideCLI.exe, hiding the console window. 
/// Returns a RunResult with success flag, stdout and stderr.
pub fn run_hidhidecli(args: &[&str]) -> io::Result<String> {
    let output = Command::new(r"C:\Program Files\Nefarius Software Solutions\HidHide\x64\HidHideCLI.exe")
        .args(args)
        .stdout(Stdio::piped())
        .stderr(Stdio::piped())
        .creation_flags(CREATE_NO_WINDOW)
        .output()?;  // 如果启动失败或等待子进程出错，这里会返回 Err

    let stdout = String::from_utf8_lossy(&output.stdout).into_owned();

    Ok(stdout)
}