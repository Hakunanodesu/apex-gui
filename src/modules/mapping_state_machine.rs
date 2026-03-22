use std::{
    sync::{Arc, Mutex, atomic::{AtomicBool, AtomicU8, Ordering}},
    time::Instant,
};
use vigem_client::{Client, Xbox360Wired};

use crate::utils::ConMapping;
use crate::modules::{
    gamepad_reading_thread::ConReader,
    gamepad_mapping_thread::ConMapper,
    screen_capture_thread::ScreenCapturer,
    enemy_det_thread::DetectorThread,
    weapon_rec_thread::WeaponRecThread,
};
use crate::shared_constants::RAPID_FIRE_WEAPON_STEMS;
use crate::utils::{
    enum_device_tool::enumerate_controllers,
    console_redirect::log_error,
};

// ====== 状态机定义 ======
#[derive(Debug, Clone)]
pub enum MappingState {
    Idle,                                    // 未启动
    CheckingDevice,                          // 检查设备可用性
    StartingCapture,                         // 正在启动屏幕捕获
    StartingDetector,                        // 正在启动检测器
    StartingWeaponRec,                       // 正在启动枪械识别（仅当连点模式=根据枪械自动切换）
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
    
    // 组件实例
    screen_capturer: Option<ScreenCapturer>,
    detector: Option<DetectorThread>,
    weapon_rec: Option<WeaponRecThread>,
    con_reader: Option<ConReader>,
    con_mapper: Option<ConMapper>,
    
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
    rapid_fire_mode: Arc<AtomicU8>, // 连点模式：0=关闭, 1=始终连点, 2=半按扳机连点
    // 特殊枪械设定
    special_weapons_aim_and_fire: Vec<String>,
    special_weapons_release_to_fire: Vec<String>,
    
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
        rapid_fire_mode: Arc<AtomicU8>,
    ) -> Self {
        Self {
            state: MappingState::Idle,
            screen_capturer: None,
            detector: None,
            weapon_rec: None,
            con_reader: None,
            con_mapper: None,
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
            rapid_fire_mode,
            special_weapons_aim_and_fire: Vec::new(),
            special_weapons_release_to_fire: Vec::new(),
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
            MappingState::StartingWeaponRec => "启动枪械识别",
            MappingState::StartingReader => "启动读取器",
            MappingState::StartingMapper => "启动映射器",
            MappingState::Running => "运行中",
            MappingState::Stopping => "停止中",
            MappingState::Error { .. } => "错误",
        }
    }
    
    // 请求启动映射（con_mapping 从当前配置的手柄键位调试内容读取）
    pub fn request_start(
        &mut self,
        con_mapping: Option<ConMapping>,
        special_weapons_aim_and_fire: Vec<String>,
        special_weapons_release_to_fire: Vec<String>,
    ) {
        if matches!(self.state, MappingState::Idle) {
            self.con_mapping = con_mapping;
            self.special_weapons_aim_and_fire = special_weapons_aim_and_fire;
            self.special_weapons_release_to_fire = special_weapons_release_to_fire;
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
    /// 返回 (do_resize, show_config, show_preview, disable_on_top, show_help_for_window_error)
    pub fn update(&mut self, con_exist: &mut bool, virtual_gamepad: Arc<Mutex<Option<Xbox360Wired<Arc<Client>>>>>) -> (bool, bool, bool, bool, bool) {
        let mut do_resize = false;
        let mut show_config = false;
        let mut show_preview = false;
        let mut disable_on_top = false;
        let mut show_help_for_window_error = false;
        
        match &self.state.clone() {
            MappingState::Idle => {
                // 空闲状态，等待启动请求
                show_preview = false;
            }
            
            MappingState::CheckingDevice => {
                *con_exist = enumerate_controllers();
                self.device_available = *con_exist;
                
                if self.device_available {
                    self.state = MappingState::StartingCapture;
                } else {
                    self.state = MappingState::Error {
                        message: "未检测到手柄设备".to_string(),
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
                        self.state = MappingState::StartingWeaponRec;
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

            MappingState::StartingWeaponRec => {
                match self.try_start_weapon_rec() {
                    Ok(()) => {
                        self.state = MappingState::StartingReader;
                    }
                    Err(e) => {
                        self.state = MappingState::Error {
                            message: format!("启动枪械识别失败: {}", e),
                            from_state: Box::new(MappingState::StartingWeaponRec),
                            _should_retry: false,
                        };
                    }
                }
            }
            
            MappingState::StartingReader => {
                if self.con_reader.is_none() {
                    let mapping = self.con_mapping.clone().unwrap_or_default();
                    self.con_reader = Some(ConReader::start(mapping));
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
                self.cleanup_all_components(con_exist);
                // 注意：虚拟手柄已通过 request_stop() 返回，不在这里处理
                self.state = MappingState::Idle;
                do_resize = true;
                show_preview = false;
                disable_on_top = true;
            }
            
            MappingState::Error { message, from_state, _should_retry } => {
                log_error(&format!("映射错误: {}", message));
                if matches!(from_state.as_ref(), MappingState::StartingCapture) {
                    show_help_for_window_error = true;
                }
                // 根据错误来源决定清理策略
                match from_state.as_ref() {
                    MappingState::CheckingDevice => {
                        // 设备检查失败，无需清理
                    }
                    MappingState::StartingCapture => {
                        // 屏幕捕获启动失败，无需清理（虚拟手柄由main.rs管理）
                    }
                    MappingState::StartingDetector => {
                        if let Some(capt) = self.screen_capturer.take() {
                            capt.stop();
                        }
                    }
                    MappingState::StartingWeaponRec => {
                        if let Some(capt) = self.screen_capturer.take() {
                            capt.stop();
                        }
                        if let Some(det) = self.detector.take() {
                            det.stop();
                        }
                    }
                    MappingState::StartingMapper => {
                        // 映射器启动失败，清理前面的组件
                        self.cleanup_partial_components();
                    }
                    MappingState::Running => {
                        // 运行时错误，清理所有组件
                        self.cleanup_all_components(con_exist);
                    }
                    _ => {
                        // 其他情况，安全起见清理所有
                        self.cleanup_all_components(con_exist);
                    }
                }
                
                self.state = MappingState::Idle;
                do_resize = true;
                show_preview = false;
            }
        }
        
        (do_resize, show_config, show_preview, disable_on_top, show_help_for_window_error)
    }
    
    // 尝试启动屏幕捕获
    fn try_start_capture(&mut self) -> Result<(), String> {
        if self.screen_capturer.is_none() {
            let outer_val = self.outer_size
                .lock().map_err(|e| format!("获取外圈大小锁失败: {}", e))?
                .trim()
                .parse::<f32>().unwrap_or(320.0);
            let outer_usize = outer_val.round() as usize;
            
            let enable_weapon_roi = self.rapid_fire_mode.load(Ordering::SeqCst) == 4;
            match ScreenCapturer::start(outer_usize, enable_weapon_roi) {
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
    
    // 尝试启动枪械识别（仅当 rapid_fire_mode == 4 时）
    fn try_start_weapon_rec(&mut self) -> Result<(), String> {
        if self.weapon_rec.is_some() {
            return Ok(());
        }
        if self.rapid_fire_mode.load(Ordering::SeqCst) != 4 {
            return Ok(());
        }
        let capt = self
            .screen_capturer
            .as_ref()
            .ok_or("屏幕捕获器未初始化")?;
        let buffer2 = capt.buffer2();
        let version2 = capt.version2();
        let crop_size = capt.crop_size();
        match WeaponRecThread::start(buffer2, version2, crop_size) {
            Ok(thread) => {
                self.weapon_rec = Some(thread);
                Ok(())
            }
            Err(e) => Err(format!("{}", e)),
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
        
        if self.con_mapper.is_none() {
            let reader = self.con_reader.as_ref()
                .ok_or("手柄读取器未初始化")?;
            let det = self.detector.as_ref()
                .ok_or("检测线程未初始化")?;
            
            let state = reader.state();
            let ready = reader.ready();
            let weapon_rec_result = self.weapon_rec.as_ref().map(|w| w.result());
            let rapid_fire_weapons: Vec<String> = RAPID_FIRE_WEAPON_STEMS
                .iter()
                .map(|s| (*s).to_string())
                .collect();
            let special_aim = self.special_weapons_aim_and_fire.clone();
            let special_release = self.special_weapons_release_to_fire.clone();
            
            self.con_mapper = Some(ConMapper::start(
                state, virtual_gamepad_ref, ready, Some(det.result()),
                params.0, params.1, params.2, params.3, params.4,
                params.5, params.6, params.7, params.8, self.aim_enable.clone(), self.rapid_fire_mode.clone(),
                weapon_rec_result, rapid_fire_weapons, special_aim, special_release,
            ));
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

        // 检查枪械识别线程错误
        if let Some(ref wr) = self.weapon_rec {
            if wr.error_flag().load(Ordering::SeqCst) {
                error_messages.push("枪械识别线程发生错误");
            }
        }
        
        // 检查映射器错误
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
        
        if error_messages.is_empty() {
            None
        } else {
            Some(error_messages.join("；"))
        }
    }
    
    // 清理所有组件
    fn cleanup_all_components(&mut self, con_exist: &mut bool) {
        if let Some(mapper) = self.con_mapper.take() {
            mapper.stop();
        }
        if let Some(reader) = self.con_reader.take() {
            reader.stop();
        }
        *con_exist = enumerate_controllers();

        if let Some(wr) = self.weapon_rec.take() {
            wr.stop();
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
        if let Some(mapper) = self.con_mapper.take() {
            mapper.stop();
        }
        if let Some(reader) = self.con_reader.take() {
            reader.stop();
        }

        if let Some(wr) = self.weapon_rec.take() {
            wr.stop();
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

    pub fn get_weapon_rec(&self) -> &Option<WeaponRecThread> {
        &self.weapon_rec
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