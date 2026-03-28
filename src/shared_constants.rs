// 项目公共常量中心。
// 约定：跨文件复用、或容易散落成“魔法数”的值，统一放在这里。

/// 武器识别相关常量
pub mod weapon_rec {
    /// 武器识别模板统一宽度（像素）
    pub const TEMPLATE_W: u32 = 160;
    /// 武器识别模板统一高度（像素）
    pub const TEMPLATE_H: u32 = 40;
    /// SSIM 低于该阈值判定为空手
    pub const EMPTY_HAND_SSIM_THRESHOLD: f32 = 0.4;
    /// 空手输出字符串
    pub const EMPTY_HAND_STR: &str = "empty";
}

/// 屏幕捕获与武器 ROI 相关常量
pub mod capture {
    /// 用于查找 Apex 窗口的标题（需完全匹配）
    pub const APEX_WINDOW_TITLE: &str = "Apex Legends";
    /// ROI 偏移和尺寸的基准分辨率宽度
    pub const BASE_WIDTH: f32 = 1920.0;
    /// 武器 ROI 左上角 X 偏移（基于 BASE_WIDTH）
    pub const WEAPON_ROI_OFFSET_X: f32 = 384.0;
    /// 武器 ROI 左上角 Y 偏移（基于 BASE_WIDTH）
    pub const WEAPON_ROI_OFFSET_Y: f32 = 122.0;
    /// 武器 ROI 宽度（像素，基于模板宽）
    pub const WEAPON_ROI_CROP_W: f32 = super::weapon_rec::TEMPLATE_W as f32;
    /// 武器 ROI 高度（像素，基于模板高）
    pub const WEAPON_ROI_CROP_H: f32 = super::weapon_rec::TEMPLATE_H as f32;
    /// 武器 ROI 更新间隔（毫秒）
    pub const WEAPON_ROI_INTERVAL_MS: u64 = 500;
}

/// 手柄映射/扳机输出相关常量（毫秒）
pub mod trigger_timing {
    /// 统一时序单位（毫秒）
    /// - 连点：按下/松开的半周期（ON/OFF 各 1 个单位）
    /// - 松手开火：松开后维持“按下”的时长（1 个单位）
    pub const TRIGGER_TIMING_UNIT_MS: u64 = 10;
}

/// 吸附强度曲线相关常量
pub mod aim_assist {
    /// 右摇杆辅助修正量 EMA 系数 α（`smoothed = α*raw + (1-α)*smoothed_prev`，两轴共用）
    pub const ASSIST_OUTPUT_EMA_ALPHA: f32 = 0.1;
}

/// UI 外观与交互文案相关常量
pub mod ui {
    pub const CHARACTER_WIDTH: f32 = 12.0;
    pub const SPACING: f32 = 8.0;
    pub const ROW_HEIGHT: f32 = 18.0;

    /// 状态色：绿色
    pub const GREEN_RGB: (u8, u8, u8) = (41, 157, 143);
    /// 状态色：黄色
    pub const YELLOW_RGB: (u8, u8, u8) = (233, 196, 106);
    /// 状态色：红色
    pub const RED_RGB: (u8, u8, u8) = (216, 118, 89);

    pub const RAPID_FIRE_MODE_DISABLED: &str = "不启用连点";
    pub const RAPID_FIRE_MODE_AUTO: &str = "根据枪械自动切换";
    pub const RAPID_FIRE_MODE_HALF_TRIGGER: &str = "半按扳机连点";
    pub const RAPID_FIRE_MODE_FULL_TRIGGER: &str = "完全按下扳机连点";
    pub const RAPID_FIRE_MODE_ALWAYS: &str = "始终连点";

    pub const AA_ACTIVATE_MODE_AIM_AND_FIRE: &str = "瞄准和开火";
    pub const AA_ACTIVATE_MODE_FIRE_ONLY: &str = "仅开火";

    /// 连点模式下拉项（显示顺序）
    pub const RAPID_FIRE_MODE_ITEMS: &[&str] = &[
        RAPID_FIRE_MODE_DISABLED,
        RAPID_FIRE_MODE_AUTO,
        RAPID_FIRE_MODE_HALF_TRIGGER,
        RAPID_FIRE_MODE_FULL_TRIGGER,
        RAPID_FIRE_MODE_ALWAYS,
    ];
    /// 辅助激活模式下拉项
    pub const AA_ACTIVATE_MODE_ITEMS: &[&str] = &[
        AA_ACTIVATE_MODE_AIM_AND_FIRE,
        AA_ACTIVATE_MODE_FIRE_ONLY,
    ];
}

/// 默认配置相关常量
pub mod defaults {
    /// 创建默认配置时使用的基准分辨率高度
    pub const BASE_SCREEN_HEIGHT: f32 = 1440.0;
    pub const BASE_INNER_DIAMETER: f32 = 60.0;
    pub const BASE_OUTER_DIAMETER: f32 = 320.0;
    pub const AIM_HEIGHT_COEFFICIENT: f32 = 0.6;
    pub const DEADZONE: f32 = 0.0;
    pub const HIPFIRE: f32 = 0.5;
    pub const INNER_STRENGTH: f32 = 0.72;
    pub const OUTER_STRENGTH: f32 = 0.36;
    pub const VERTICAL_STRENGTH_COEFFICIENT: f32 = 0.4;
    pub const AA_ACTIVATE_MODE: &str = "仅开火";
    pub const RAPID_FIRE_MODE: &str = "不启用连点";
    /// 右摇杆辅助输出 EMA 默认 α（与 `aim_assist::ASSIST_OUTPUT_EMA_ALPHA` 一致）
    pub const ASSIST_OUTPUT_EMA_ALPHA: f32 = super::aim_assist::ASSIST_OUTPUT_EMA_ALPHA;
}

/// 鉴权相关常量
pub mod auth {
    pub const LICENSE_CODE: &str = "forthegloriouspurpose";
}

/// 更新检查相关常量
pub mod update_check {
    pub const RELEASES_URL: &str = "https://api.github.com/repos/Hakunanodesu/apex-egui/releases";
    pub const REQUEST_TIMEOUT_MS: u64 = 5000;
}

/// 线程错误容忍阈值
pub mod error_limits {
    pub const GAMEPAD_MAPPING_MAX_CONSECUTIVE_ERRORS: u32 = 50;
    pub const ENEMY_DET_MAX_CONSECUTIVE_ERRORS: u32 = 10;
    pub const GAMEPAD_READING_MAX_CONSECUTIVE_ERRORS: u32 = 100;
}

/// 检测模型默认参数
pub mod detection_defaults {
    pub const SIZE: usize = 320;
    pub const CONF_THRES: f32 = 0.4;
    pub const IOU_THRES: f32 = 0.9;
    pub const CLASSES: &str = "0";
}

/// 配置文件路径
pub mod paths {
    pub const CONFIGS_DIR: &str = "configs";
    pub const CURRENT_CONFIG_FILE: &str = "configs/.current";
}

/// 构建脚本（build.rs）相关常量
#[allow(dead_code)]
pub mod build {
    /// 生成 exe 图标时写入的 ico 尺寸
    pub const ICON_SIZES: &[u32] = &[16, 32, 48, 256];
}

// 由 build.rs 扫描 gun_templates 生成，与 weapon_rec 嵌入模板列表一致
include!("build/rapid_fire_weapon_stems.rs");

