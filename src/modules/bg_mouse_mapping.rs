use std::time::Duration;
use serialport::{SerialPort, SerialPortType};
use std::{
    sync::{
        Arc,
        Mutex,
        atomic::{AtomicBool, Ordering},
    },
    thread::{self, JoinHandle},
};
use windows::Win32::UI::Input::KeyboardAndMouse::{GetAsyncKeyState, VK_LBUTTON, VK_RBUTTON};

use crate::modules::bg_onnx_dml_od::Detection;
use crate::utils::console_redirect::log_error;

// Pico 设备的固定 VID 和 PID
const PICO_VID: u16 = 0x239A;  // Raspberry Pi Pico的VID
const PICO_PID: u16 = 0x80F4;  // Raspberry Pi Pico的PID
const PICO_BAUD_RATE: u32 = 115200;  // 默认波特率

/// 查找并打开指定vid/pid的串口设备
pub fn open_pico_port(vid: u16, pid: u16, baud_rate: u32) -> Option<Box<dyn SerialPort>> {
    let ports = serialport::available_ports().ok()?;
    for port in ports {
        if let SerialPortType::UsbPort(info) = &port.port_type {
            if info.vid == vid && info.pid == pid {
                return serialport::new(&port.port_name, baud_rate)
                    .timeout(Duration::from_millis(100))
                    .open()
                    .ok();
            }
        }
    }
    None
}

/// 向Pico发送鼠标移动指令
pub fn send_mouse_move(port: &mut dyn SerialPort, dx: i32, dy: i32) -> std::io::Result<()> {
    // 格式化为 "dx,dy\n"
    let cmd = format!("{},{}\n", dx, dy);
    port.write_all(cmd.as_bytes())
}

/// 检查鼠标按键状态的函数
fn get_mouse_button_state() -> (bool, bool) {
    // 使用 Windows API 获取鼠标左右键状态
    // GetAsyncKeyState 返回负数表示按键当前被按下
    let left_button_pressed = unsafe { GetAsyncKeyState(VK_LBUTTON.0 as i32) } < 0;
    let right_button_pressed = unsafe { GetAsyncKeyState(VK_RBUTTON.0 as i32) } < 0;
    
    (left_button_pressed, right_button_pressed)
}

/// 当鼠标右键按下时，基于检测结果进行修正
fn apply_left_click_adjustment(
    d: &Detection,
    outer_size: f32,
    mid_size: f32,
    inner_size: f32,
    outer_str: f32,
    inner_str: f32,
    init_str: f32,
    vertical_str: f32,
    aim_height: f32,
    right_button_pressed: bool,
    hipfire: f32,
) -> (f32, f32) {
    let center = outer_size / 2.0;
    let dx = d.x - center;
    let dy = (d.y + (0.5 - aim_height) * d.h) - center;
    let dist = ((dx * dx + dy * dy).sqrt()).min(center);
    
    let mut strength = if 
        dx.abs() <= inner_size / 2.0 && dy.abs() <= inner_size / 2.0
    {
        let t = if inner_size > 0.0 { dist / (inner_size / 2.0) } else { 1.0 };
        let temp = init_str * (1.0 - t) + inner_str * t;
        temp * temp * 20.0
    } else if 
        (dx.abs() <= mid_size / 2.0 && dy.abs() <= mid_size / 2.0)
        || (dx.abs() <= d.w / 2.0 && dy.abs() <= d.h / 2.0) 
    {
        inner_str * inner_str * 20.0
    } else if 
        dx.abs() <= outer_size / 2.0 && dy.abs() <= outer_size / 2.0
    {
        outer_str * outer_str * 20.0
    } else {
        // 超出outer区间，不移动
        0.0
    };

    if !right_button_pressed {
        strength = strength * hipfire;
    }
    
    let (x, y) = (strength * dx / dist, vertical_str * strength * dy / dist);

    (x, y)
}

pub struct MouseMapper {
    stop_flag: Arc<AtomicBool>,
    handle: JoinHandle<()>,
    error_flag: Arc<AtomicBool>,
}

impl MouseMapper {
    /// 启动映射线程
    pub fn start(
        det_result: Option<Arc<Mutex<Option<Vec<Detection>>>>>,
        outer_size: f32,
        mid_size: f32,
        inner_size: f32,
        outer_str: f32,
        inner_str: f32,
        init_str: f32,
        vertical_str: f32, // 新增垂直强度参数
        aim_height: f32,  // 新增瞄准高度参数（暂未使用）
        hipfire: f32,
        aim_enable: Arc<AtomicBool>, // 新增瞄准辅助开关
    ) -> Self {
        let stop_flag = Arc::new(AtomicBool::new(false));
        let error_flag = Arc::new(AtomicBool::new(false));
        let stop_clone = stop_flag.clone();
        let det_result_clone = det_result.clone();
        let error_flag_clone = error_flag.clone();

        // 尝试打开串口设备，使用固定的 Pico 设备参数
        let serial_port = open_pico_port(PICO_VID, PICO_PID, PICO_BAUD_RATE);

        let handle = thread::spawn(move || {
            // 获取串口设备的可变引用
            let mut port_guard = if let Some(port) = serial_port {
                port
            } else {
                log_error(&format!("无法打开 Pico 串口设备 VID: 0x{:04X}, PID: 0x{:04X}", PICO_VID, PICO_PID));
                error_flag_clone.store(true, Ordering::SeqCst);
                return;
            };

            // 累积误差变量
            let mut error_x: f32 = 0.0;
            let mut error_y: f32 = 0.0;
            let mut consecutive_errors = 0;
            const MAX_CONSECUTIVE_ERRORS: u32 = 20;

            while !stop_clone.load(Ordering::SeqCst) {
                // 处理检测结果并计算xy偏移
                if let Some(ref det_arc) = det_result_clone {
                    match det_arc.lock() {
                        Ok(det_guard) => {
                            // 检查鼠标左右键是否按下
                            let (left_button_pressed, right_button_pressed) = get_mouse_button_state();
                            
                            if let Some(detections) = &*det_guard {
                                if let Some(d) = detections.first() {
                                    // 当鼠标左键按下时总是计算并应用结果
                                    // 当aim_enable为true且右键按下时也计算并应用结果
                                    if left_button_pressed || (aim_enable.load(Ordering::SeqCst) && right_button_pressed) {
                                        // 应用映射
                                        let (x, y) = apply_left_click_adjustment(
                                            d,
                                            outer_size,
                                            mid_size,
                                            inner_size,
                                            outer_str,
                                            inner_str,
                                            init_str,
                                            vertical_str,
                                            aim_height,
                                            right_button_pressed,
                                            hipfire,
                                        );
                                        
                                        // 累积误差
                                        error_x += x;
                                        error_y += y;
                                        
                                        // 计算整数移动量
                                        let move_x = error_x as i32;
                                        let move_y = error_y as i32;
                                        
                                        // 更新误差（减去已移动的整数部分）
                                        error_x -= move_x as f32;
                                        error_y -= move_y as f32;
                                        
                                        // 只有当累积误差足够大时才发送移动指令
                                        if move_x != 0 || move_y != 0 {
                                            // println!("x: {}, y: {}, error_x: {:.3}, error_y: {:.3}", 
                                            //         move_x, move_y, error_x, error_y);
                                            // 发送鼠标移动指令到串口设备
                                            if let Err(e) = send_mouse_move(&mut *port_guard, move_x, move_y) {
                                                log_error(&format!("发送鼠标移动指令失败: {}", e));
                                                consecutive_errors += 1;
                                                if consecutive_errors >= MAX_CONSECUTIVE_ERRORS {
                                                    log_error(&format!("鼠标映射 - 连续发送失败超过{}次，设置错误标志", MAX_CONSECUTIVE_ERRORS));
                                                    error_flag_clone.store(true, Ordering::SeqCst);
                                                    break;
                                                }
                                            } else {
                                                consecutive_errors = 0; // 重置错误计数
                                            }
                                        }
                                    }
                                }
                            }
                        }
                        Err(e) => {
                            log_error(&format!("鼠标映射 - 获取检测结果失败: {:?}", e));
                            consecutive_errors += 1;
                            if consecutive_errors >= MAX_CONSECUTIVE_ERRORS {
                                error_flag_clone.store(true, Ordering::SeqCst);
                                break;
                            }
                        }
                    }
                }
                
                thread::sleep(Duration::from_millis(1));
            }

            // 线程结束时关闭串口设备
            drop(port_guard);
        });

        MouseMapper { stop_flag, handle, error_flag }
    }

    /// 停止映射线程并 join
    pub fn stop(self) {
        self.stop_flag.store(true, Ordering::SeqCst);
        let _ = self.handle.join();
    }

    /// 获取错误标志
    pub fn error_flag(&self) -> Arc<AtomicBool> {
        self.error_flag.clone()
    }
}