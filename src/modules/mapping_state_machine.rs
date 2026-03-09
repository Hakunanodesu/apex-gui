use std::{
    sync::{Arc, Mutex, atomic::{AtomicBool, Ordering}},
    time::Instant,
};
use vigem_client::{Client, Xbox360Wired};

use crate::utils::ConMapping;
use crate::modules::{
    bg_con_reading::ConReader,
    bg_con_mapping::ConMapper,
    bg_screen_cap::ScreenCapturer,
    bg_onnx_dml_od::DetectorThread,
    bg_mouse_mapping::MouseMapper,
};
use crate::utils::{
    enum_device_tool::{enumerate_controllers, enumerate_pico},
    console_redirect::log_error,
};

// ====== 状态机定义 ======
#[derive(Debug, Clone)]
pub enum MappingState {
    Idle,                                    // 未启动
    CheckingDevice,                          // 检查设备可用性
    StartingCapture,                         // 正在启动屏幕捕获
    StartingDetector,                        // 正在启动检测器
    StartingReader,                          // 正在启动读取器（仅手柄模式）
    StartingMapper,                          // 正在启动映射器
    Running,                                 // 正常运行
    Stopping,                               // 正在停止
    Error { 
        message: String, 
        from_state: Box<MappingState>,
        _should_retry: bool,
    },                                      // 错误状态
}

pub struct MappingManager {
    state: MappingState,
    mouse_mode: bool,
    
    // 组件实例
    screen_capturer: Option<ScreenCapturer>,
    detector: Option<DetectorThread>,
    con_reader: Option<ConReader>,
    con_mapper: Option<ConMapper>,
    mouse_mapper: Option<MouseMapper>,
    
    // 配置参数
    current_model: String,
    con_mapping: Option<ConMapping>, // 手柄键位映射（启动智慧核心时从配置读取）
    aim_enable: Arc<AtomicBool>, // 瞄准辅助开关
    outer_size: Arc<Mutex<String>>,
    mid_size: Arc<Mutex<String>>,
    inner_size: Arc<Mutex<String>>,
    outer_str: Arc<Mutex<String>>,
    inner_str: Arc<Mutex<String>>,
    init_str: Arc<Mutex<String>>,
    hipfire: Arc<Mutex<String>>,
    vertical_str: Arc<Mutex<String>>,
    aim_height: Arc<Mutex<String>>,
    rt_rapid_fire: Arc<AtomicBool>, // 右扳机连点开关
    
    // 状态标志
    device_available: bool,
    last_error_check: Instant,
}

impl MappingManager {
    pub fn new(
        current_model: String,
        aim_enable: Arc<AtomicBool>, // 瞄准辅助开关
        outer_size: Arc<Mutex<String>>,
        mid_size: Arc<Mutex<String>>,
        inner_size: Arc<Mutex<String>>,
        outer_str: Arc<Mutex<String>>,
        inner_str: Arc<Mutex<String>>,
        init_str: Arc<Mutex<String>>,
        hipfire: Arc<Mutex<String>>,
        vertical_str: Arc<Mutex<String>>,
        aim_height: Arc<Mutex<String>>,
        rt_rapid_fire: Arc<AtomicBool>,
    ) -> Self {
        Self {
            state: MappingState::Idle,
            mouse_mode: false,
            screen_capturer: None,
            detector: None,
            con_reader: None,
            con_mapper: None,
            mouse_mapper: None,
            current_model,
            con_mapping: None,
            aim_enable,
            outer_size,
            mid_size,
            inner_size,
            outer_str,
            inner_str,
            init_str,
            hipfire,
            vertical_str,
            aim_height,
            rt_rapid_fire,
            device_available: false,
            last_error_check: Instant::now(),
        }
    }
    
    pub fn is_active(&self) -> bool {
        !matches!(self.state, MappingState::Idle | MappingState::Error { .. })
    }
    
    pub fn _is_running(&self) -> bool {
        matches!(self.state, MappingState::Running)
    }
    
    pub fn _get_state_description(&self) -> &'static str {
        match &self.state {
            MappingState::Idle => "空闲",
            MappingState::CheckingDevice => "检查设备",
            MappingState::StartingCapture => "启动屏幕捕获",
            MappingState::StartingDetector => "启动检测器",
            MappingState::StartingReader => "启动读取器",
            MappingState::StartingMapper => "启动映射器",
            MappingState::Running => "运行中",
            MappingState::Stopping => "停止中",
            MappingState::Error { .. } => "错误",
        }
    }
    
    // 请求启动映射（con_mapping 从当前配置的手柄键位调试内容读取）
    pub fn request_start(&mut self, mouse_mode: bool, con_mapping: Option<ConMapping>) {
        if matches!(self.state, MappingState::Idle) {
            self.mouse_mode = mouse_mode;
            self.con_mapping = con_mapping;
            self.state = MappingState::CheckingDevice;
        }
    }
    
    // 请求停止映射
    pub fn request_stop(&mut self) {
        if !matches!(self.state, MappingState::Idle | MappingState::Stopping | MappingState::Error { .. }) {
            self.state = MappingState::Stopping;
        }
    }
    
    // 更新状态机
    pub fn update(&mut self, con_exist: &mut bool, pico_exist: &mut bool, virtual_gamepad: Arc<Mutex<Option<Xbox360Wired<Arc<Client>>>>>) -> (bool, bool, bool, bool) {
        let mut do_resize = false;
        let mut show_config = false;
        let mut show_preview = false;
        let mut disable_on_top = false;
        
        match &self.state.clone() {
            MappingState::Idle => {
                // 空闲状态，等待启动请求
                show_preview = false;
            }
            
            MappingState::CheckingDevice => {
                if self.mouse_mode {
                    *pico_exist = enumerate_pico();
                    self.device_available = *pico_exist;
                } else {
                    *con_exist = enumerate_controllers();
                    self.device_available = *con_exist;
                }
                
                if self.device_available {
                    self.state = MappingState::StartingCapture;
                } else {
                    self.state = MappingState::Error {
                        message: if self.mouse_mode { 
                            "未检测到Pico设备".to_string() 
                        } else { 
                            "未检测到手柄设备".to_string() 
                        },
                        from_state: Box::new(MappingState::CheckingDevice),
                        _should_retry: true,
                    };
                }
            }
            
            MappingState::StartingCapture => {
                match self.try_start_capture() {
                    Ok(()) => {
                        self.state = MappingState::StartingDetector;
                    }
                    Err(e) => {
                        self.state = MappingState::Error {
                            message: format!("启动屏幕捕获失败: {}", e),
                            from_state: Box::new(MappingState::StartingCapture),
                            _should_retry: false,
                        };
                    }
                }
            }
            
            MappingState::StartingDetector => {
                match self.try_start_detector() {
                    Ok(()) => {
                        if self.mouse_mode {
                            self.state = MappingState::StartingMapper;
                        } else {
                            self.state = MappingState::StartingReader;
                        }
                    }
                    Err(e) => {
                        self.state = MappingState::Error {
                            message: format!("启动检测器失败: {}", e),
                            from_state: Box::new(MappingState::StartingDetector),
                            _should_retry: false,
                        };
                    }
                }
            }
            
            MappingState::StartingReader => {
                // 仅在手柄模式下
                if !self.mouse_mode {
                    if self.con_reader.is_none() {
                        let mapping = self.con_mapping.clone().unwrap_or_default();
                        self.con_reader = Some(ConReader::start(mapping));
                    }
                }
                self.state = MappingState::StartingMapper;
            }
            
            MappingState::StartingMapper => {
                match self.try_start_mapper(virtual_gamepad.clone()) {
                    Ok(()) => {
                        self.state = MappingState::Running;
                        do_resize = true;
                        show_config = false;
                    }
                    Err(e) => {
                        self.state = MappingState::Error {
                            message: format!("启动映射器失败: {}", e),
                            from_state: Box::new(MappingState::StartingMapper),
                            _should_retry: false,
                        };
                    }
                }
            }
            
            MappingState::Running => {
                // 定期检查错误（避免每帧都检查）
                if self.last_error_check.elapsed().as_millis() > 1000 {
                    if let Some(error_msg) = self.check_runtime_errors() {
                        self.state = MappingState::Error {
                            message: error_msg,
                            from_state: Box::new(MappingState::Running),
                            _should_retry: false,
                        };
                    }
                    self.last_error_check = Instant::now();
                }
                // 当捕获器与检测器都在运行时允许显示预览
                if self.screen_capturer.is_some() && self.detector.is_some() {
                    show_preview = true;
                } else {
                    show_preview = false;
                }
            }
            
            MappingState::Stopping => {
                self.cleanup_all_components(con_exist, pico_exist);
                // 注意：虚拟手柄已通过 request_stop() 返回，不在这里处理
                self.state = MappingState::Idle;
                do_resize = true;
                show_preview = false;
                disable_on_top = true;
            }
            
            MappingState::Error { message, from_state, _should_retry } => {
                log_error(&format!("映射错误: {}", message));
                
                // 根据错误来源决定清理策略
                match from_state.as_ref() {
                    MappingState::CheckingDevice => {
                        // 设备检查失败，无需清理
                    }
                    MappingState::StartingCapture => {
                        // 屏幕捕获启动失败，无需清理（虚拟手柄由main.rs管理）
                    }
                    MappingState::StartingDetector => {
                        // 检测器启动失败，清理屏幕捕获器
                        if let Some(capt) = self.screen_capturer.take() {
                            capt.stop();
                        }
                    }
                    MappingState::StartingMapper => {
                        // 映射器启动失败，清理前面的组件
                        self.cleanup_partial_components();
                    }
                    MappingState::Running => {
                        // 运行时错误，清理所有组件
                        self.cleanup_all_components(con_exist, pico_exist);
                    }
                    _ => {
                        // 其他情况，安全起见清理所有
                        self.cleanup_all_components(con_exist, pico_exist);
                    }
                }
                
                self.state = MappingState::Idle;
                do_resize = true;
                show_preview = false;
            }
        }
        
        (do_resize, show_config, show_preview, disable_on_top)
    }
    
    // 尝试启动屏幕捕获
    fn try_start_capture(&mut self) -> Result<(), String> {
        if self.screen_capturer.is_none() {
            let outer_val = self.outer_size
                .lock().map_err(|e| format!("获取外圈大小锁失败: {}", e))?
                .trim()
                .parse::<f32>().unwrap_or(320.0);
            let outer_usize = outer_val.round() as usize;
            
            match ScreenCapturer::start(outer_usize) {
                Ok(capturer) => {
                    self.screen_capturer = Some(capturer);
                    Ok(())
                }
                Err(e) => Err(format!("{}", e))
            }
        } else {
            Ok(())
        }
    }
    
    // 尝试启动检测器
    fn try_start_detector(&mut self) -> Result<(), String> {
        if self.detector.is_none() {
            let capt = self.screen_capturer.as_ref()
                .ok_or("屏幕捕获器未初始化")?;
            let buffer_arc = capt.buffer();
            let version_arc = capt.version();
            // 构建模型路径，确保添加 .onnx 后缀
            let model_name = if self.current_model.ends_with(".onnx") {
                self.current_model.clone()
            } else {
                format!("{}.onnx", self.current_model)
            };
            let model_path = std::env::current_dir()
                .map_err(|e| format!("获取当前目录失败: {}", e))?
                .join("models")
                .join(&model_name);
            
            match DetectorThread::start(buffer_arc, version_arc, &model_path) {
                Ok(det) => {
                    self.detector = Some(det);
                    Ok(())
                }
                Err(e) => Err(format!("{}", e))
            }
        } else {
            Ok(())
        }
    }
    
    // 尝试启动映射器
    fn try_start_mapper(&mut self, virtual_gamepad_ref: Arc<Mutex<Option<Xbox360Wired<Arc<Client>>>>>) -> Result<(), String> {
        let params = self.extract_mapping_params()?;
        
        if self.mouse_mode {
            if self.mouse_mapper.is_none() {
                let det = self.detector.as_ref()
                    .ok_or("检测线程未初始化")?;
                
                self.mouse_mapper = Some(MouseMapper::start(
                    Some(det.result()),
                    params.0, params.1, params.2, params.3, params.4,
                    params.5, params.6, params.7, params.8, self.aim_enable.clone()
                ));
            }
        } else {
            if self.con_mapper.is_none() {
                let reader = self.con_reader.as_ref()
                    .ok_or("手柄读取器未初始化")?;
                let det = self.detector.as_ref()
                    .ok_or("检测线程未初始化")?;
                
                let state = reader.state();
                let ready = reader.ready();
                
                self.con_mapper = Some(ConMapper::start(
                    state, virtual_gamepad_ref, ready, Some(det.result()),
                    params.0, params.1, params.2, params.3, params.4,
                    params.5, params.6, params.7, params.8, self.aim_enable.clone(), self.rt_rapid_fire.clone()
                ));
            }
        }
        
        Ok(())
    }
    
    // 检查运行时错误
    fn check_runtime_errors(&self) -> Option<String> {
        let mut error_messages = Vec::new();
        
        // 检查屏幕捕获错误
        if let Some(ref capturer) = self.screen_capturer {
            if capturer.error_flag().load(Ordering::SeqCst) {
                error_messages.push("屏幕捕获线程发生错误");
            }
        }
        
        // 检查推理线程错误
        if let Some(ref det) = self.detector {
            if det.error_flag().load(Ordering::SeqCst) {
                error_messages.push("推理线程发生错误");
            }
        }
        
        // 检查映射器错误
        if self.mouse_mode {
            if let Some(ref mapper) = self.mouse_mapper {
                if mapper.error_flag().load(Ordering::SeqCst) {
                    error_messages.push("鼠标映射线程发生错误");
                }
            }
        } else {
            if let Some(ref reader) = self.con_reader {
                if reader.error_flag().load(Ordering::SeqCst) {
                    error_messages.push("手柄读取线程发生错误");
                }
            }
            if let Some(ref mapper) = self.con_mapper {
                if mapper.error_flag().load(Ordering::SeqCst) {
                    error_messages.push("手柄映射线程发生错误");
                }
            }
        }
        
        if error_messages.is_empty() {
            None
        } else {
            Some(error_messages.join("；"))
        }
    }
    
    // 清理所有组件
    fn cleanup_all_components(&mut self, con_exist: &mut bool, pico_exist: &mut bool) {
        // 按照启动的反序关闭组件
        if self.mouse_mode {
            if let Some(mapper) = self.mouse_mapper.take() {
                mapper.stop();
            }
            *pico_exist = enumerate_pico();
        } else {
            if let Some(mapper) = self.con_mapper.take() {
                mapper.stop();
            }
            if let Some(reader) = self.con_reader.take() {
                reader.stop();
            }
            *con_exist = enumerate_controllers();
        }
        
        if let Some(det) = self.detector.take() {
            det.stop();
        }
        
        if let Some(capt) = self.screen_capturer.take() {
            capt.stop();
        }
    }
    
    // 清理部分组件（用于启动失败时的清理）
    fn cleanup_partial_components(&mut self) {
        if self.mouse_mode {
            if let Some(mapper) = self.mouse_mapper.take() {
                mapper.stop();
            }
        } else {
            if let Some(mapper) = self.con_mapper.take() {
                mapper.stop();
            }
            if let Some(reader) = self.con_reader.take() {
                reader.stop();
            }
            // 虚拟手柄由main.rs管理，不在这里释放
        }
        
        if let Some(det) = self.detector.take() {
            det.stop();
        }
        
        if let Some(capt) = self.screen_capturer.take() {
            capt.stop();
        }
    }
    
    // 提取映射参数
    fn extract_mapping_params(&self) -> Result<(f32, f32, f32, f32, f32, f32, f32, f32, f32), String> {
        let outer_val = self.outer_size.lock().map_err(|e| format!("获取外圈大小锁失败: {}", e))?.trim().parse::<f32>().unwrap_or(320.0);
        let mid_val = self.mid_size.lock().map_err(|e| format!("获取中圈大小锁失败: {}", e))?.trim().parse::<f32>().unwrap_or(200.0);
        let inner_val = self.inner_size.lock().map_err(|e| format!("获取内圈大小锁失败: {}", e))?.trim().parse::<f32>().unwrap_or(100.0);
        let outer_str_val = self.outer_str.lock().map_err(|e| format!("获取外圈强度锁失败: {}", e))?.trim().parse::<f32>().unwrap_or(1.0);
        let inner_str_val = self.inner_str.lock().map_err(|e| format!("获取内圈强度锁失败: {}", e))?.trim().parse::<f32>().unwrap_or(1.0);
        let init_str_val = self.init_str.lock().map_err(|e| format!("获取起步强度锁失败: {}", e))?.trim().parse::<f32>().unwrap_or(0.0);
        let vertical_str_val = self.vertical_str.lock().map_err(|e| format!("获取垂直强度锁失败: {}", e))?.trim().parse::<f32>().unwrap_or(0.5);
        let aim_height_val = self.aim_height.lock().map_err(|e| format!("获取瞄准高度锁失败: {}", e))?.trim().parse::<f32>().unwrap_or(0.5);
        let hipfire_val = self.hipfire.lock().map_err(|e| format!("获取腰射系数锁失败: {}", e))?.trim().parse::<f32>().unwrap_or(0.0);
        
        Ok((outer_val, mid_val, inner_val, outer_str_val, inner_str_val, init_str_val, vertical_str_val, aim_height_val, hipfire_val))
    }
    
    // 更新配置
    pub fn update_config(&mut self, current_model: String) {
        self.current_model = current_model;
    }
    
    // 更新瞄准辅助开关
    pub fn update_aim_enable(&mut self, aim_enable: bool) {
        self.aim_enable.store(aim_enable, Ordering::SeqCst);
    }

    // 提供对组件的只读访问，用于UI显示
    pub fn get_screen_capturer(&self) -> &Option<ScreenCapturer> {
        &self.screen_capturer
    }
    
    pub fn get_detector(&self) -> &Option<DetectorThread> {
        &self.detector
    }

    /// 仅用于调试窗口：若当前未运行智慧核心且尚未启动 ConReader，则启动 ConReader（使用默认键位映射）
    pub fn start_con_reader_for_debug(&mut self) {
        if matches!(self.state, MappingState::Idle) && self.con_reader.is_none() {
            self.con_reader = Some(ConReader::start(ConMapping::default()));
        }
    }

    /// 仅用于调试窗口：若当前未运行智慧核心，则停止 ConReader
    pub fn stop_con_reader_for_debug(&mut self) {
        if matches!(self.state, MappingState::Idle) {
            if let Some(reader) = self.con_reader.take() {
                reader.stop();
            }
        }
    }
} 