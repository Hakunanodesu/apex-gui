# 项目常量与配置一览

本文档列出 apex-egui 项目中所有常量配置及其所在位置，便于查阅与修改。

---

## 一、Rust 源码中的常量

### 1. `src/main.rs`

| 常量名 | 类型 | 值 | 说明 |
|--------|------|-----|------|
| `CHARACTER_WIDTH` | `f32` | `12.0` | 英文字体宽度基准（字号） |
| `SPACING` | `f32` | `8.0` | UI 间距 |
| `ROW_HEIGHT` | `f32` | `18.0` | 行高（separator 高度为 ROW_HEIGHT / 3.0） |
| `GREEN` | `egui::Color32` | `rgb(41, 157, 143)` | 绿色（状态指示） |
| `YELLOW` | `egui::Color32` | `rgb(233, 196, 106)` | 黄色（状态指示） |
| `RED` | `egui::Color32` | `rgb(216, 118, 89)` | 红色（状态指示） |
| `LICENSE_CODE` | `&str` | `"forthegloriouspurpose"` | 正确的许可证代码，用于校验及是否在输入框中回显 |

**默认配置（`create_default_config`，约 741–767 行）：**

- `base_inner_diameter`: `60.0`（1440p 基准）
- `base_middle_diameter`: `60.0`
- `base_outer_diameter`: `320.0`
- 基准分辨率高度: `1440.0`
- `aim_height_coefficient`: `0.6`
- `assist_curve.deadzone`: `0.0`
- `assist_curve.hipfire`: `0.5`
- `assist_curve.inner_strength`: `0.72`
- `assist_curve.outer_strength`: `0.36`
- `aa_activate_mode`: `"仅开火"`
- `use_controller`: `false`
- `vertical_strength_coefficient`: `0.4`
- `rapid_fire_mode`: `"不启用连点"`
- `license_code`: `""`（空字符串）

**字体嵌入路径（约 126、133 行，相对 `src/main.rs` 为 `../fonts/`，即项目根下 `fonts/`）：**

- `fonts/JetBrainsMono-Regular.ttf`
- `fonts/NotoSansCJKsc-Regular.otf`

---

### 2. `src/modules/screen_capture_thread.rs`

| 常量名 | 类型 | 值 | 说明 |
|--------|------|-----|------|
| `APEX_WINDOW_TITLE` | `&str` | `"Apex Legends"` | 用于查找 Apex 窗口的标题（需完全匹配） |
| `BASE_HEIGHT` | `f32` | `1080.0` | 基准分辨率高度（用于 ROI 缩放） |
| `WEAPON_ROI_OFFSET_X` | `f32` | `377.0` | 武器 ROI 左上角 X 偏移（1080p 基准） |
| `WEAPON_ROI_OFFSET_Y` | `f32` | `122.0` | 武器 ROI 左上角 Y 偏移 |
| `WEAPON_ROI_CROP_W` | `f32` | `159.0` | 武器 ROI 裁剪宽度 |
| `WEAPON_ROI_CROP_H` | `f32` | `38.0` | 武器 ROI 裁剪高度 |
| `WEAPON_ROI_INTERVAL_MS` | `u64` | `500` | 武器 ROI 采样间隔（毫秒） |

---

### 3. `src/modules/weapon_rec_thread.rs`

| 常量名 | 类型 | 值 | 说明 |
|--------|------|-----|------|
| `TARGET_W` | `u32` | `159` | 模板匹配目标宽度（与 WEAPON_ROI_CROP_W 一致） |
| `TARGET_H` | `u32` | `38` | 模板匹配目标高度（与 WEAPON_ROI_CROP_H 一致） |
| `CANNY_LOW` | `f32` | `20.0` | Canny 边缘检测低阈值 |
| `CANNY_HIGH` | `f32` | `60.0` | Canny 边缘检测高阈值 |
| `EDGE_THRESHOLD` | `u8` | `128` | 边缘二值化阈值 |
| `EMPTY_HAND_SIMILARITY_THRESHOLD` | `f32` | `0.5` | 所有武器模板相似度均低于此值时判定为空手 |
| `EMPTY_HAND_STR` | `&str` | `"空手"` | 空手时的识别结果字符串 |

---

### 4. `src/modules/gamepad_mapping_thread.rs`

| 常量名 | 类型 | 值 | 说明 |
|--------|------|-----|------|
| `RAPID_FIRE_WEAPONS` | `&[&str]` | 见下表 | 连点白名单（枪械识别为列表中武器时始终连点） |
| `MAX_CONSECUTIVE_ERRORS`（局部） | `u32` | `50` | 最大连续错误次数（约 133 行） |

**RAPID_FIRE_WEAPONS 当前列表（约 17–19 行，与 gun_template 文件名无后缀一致）：**

`"3030"`, `"mastiff"`, `"singlep2020"`, `"peacekeeper"`, `"hemlok"`, `"kraber"`, `"tripletake"`, `"sentinel"`, `"wingman"`, `"longbow"`, `"g7"`

---

### 5. `src/modules/enemy_det_thread.rs`

| 常量/默认值 | 位置 | 值 | 说明 |
|-------------|------|-----|------|
| `DetectionConfig::default()` | 约 30–37 行 | `size: 320`, `conf_thres: 0.4`, `iou_thres: 0.9`, `classes: "0"` | 检测模型默认配置（推理尺寸、置信度、IoU、类别） |
| `MAX_CONSECUTIVE_ERRORS`（局部） | 约 331 行 | `10` | 最大连续错误次数 |

---

### 6. `src/modules/gamepad_reading_thread.rs`

| 常量名 | 类型 | 值 | 说明 |
|--------|------|-----|------|
| `DEBUG_PRINT_ENABLED` | `AtomicBool` | `false` | 是否打印调试信息 |
| `DEBUG_TEXT` | `Mutex<String>` | 空字符串 | 调试文本缓存，供 UI 读取显示 |
| `MAX_CONSECUTIVE_ERRORS`（局部） | 约 274 行 | `100` | 最大连续错误次数（手柄断开较常见，故允许更多） |

---

### 7. `src/modules/update_check.rs`

| 常量名 | 类型 | 值 | 说明 |
|--------|------|-----|------|
| `RELEASES_URL` | `&str` | `"https://api.github.com/repos/Hakunanodesu/apex-egui/releases"` | GitHub Release API 地址 |
| `REQUEST_TIMEOUT_MS` | `u64` | `5000` | 请求超时（毫秒） |

---

### 8. `build.rs`（编译时生成）

- **输出模块**：`src/build/gun_templates.rs`（写入源码目录，供 `weapon_rec_thread.rs` 通过 `include!("../build/gun_templates.rs")` 引入）。
- **常量**：`TEMPLATE_FILES: &[(&str, &[u8])]` — 由 `gun_template/*.png` 扫描生成，嵌入 (名称无后缀, PNG 字节)。
- **输入目录**：`gun_template/`（仅 `.png` 文件）。
- **其他**：将 `3mz_ds_ver.png` 转为多尺寸 ICO 并设为 exe 图标；`cargo:rerun-if-changed=gun_template` 与 `3mz_ds_ver.png`。

---

### 9. `src/utils.rs`

**默认值：**

- `ConMappingAxis::default()`：所有轴为 `None`（约 34–36 行）。
- `ConMappingButton::default()`：所有按键为 `None`（约 55–59 行）。
- `ConfigFile` 含 `license_code`，反序列化时 `#[serde(default)]` 为空字符串（约 97–99 行）。

**路径常量（硬编码字符串）：**

- 当前配置索引文件：`configs/.current`（约 104、118 行）。
- 配置 JSON 路径格式：`configs/{config_name}.json`（约 125、132 行）。

---

## 二、配置文件与目录

### 1. 配置目录与文件

| 路径 | 说明 |
|------|------|
| `configs/` | 存放所有配置 JSON 的目录 |
| `configs/.current` | 当前选中的配置名与模型名（JSON：`current_config`, `current_model`） |
| `configs/<名称>.json` | 单份配置（如 `apex_1080p_dse.json`） |

**配置 JSON 字段（以 `configs/apex_1080p_dse.json` 为例）：**

- `aim_height_coefficient`: 瞄准高度系数（如 `0.64`）
- `assist_curve`: 吸附曲线（deadzone, hipfire, inner/middle/outer diameter, inner/outer strength）
- `aa_activate_mode`: 辅助激活模式（如 `"仅开火"`）
- `use_controller`: 是否使用手柄
- `vertical_strength_coefficient`: 垂直强度系数
- `con_mapping`: 手柄轴/按键映射（axis: lx, ly, rx, ry, lt, rt；button: lb, rb, ls, rs, back, start, x, y, a, b）
- `rapid_fire_mode`: 连点模式（如 `"根据枪械自动切换"`）
- `license_code`: 许可证代码（可选，默认空；与 `LICENSE_CODE` 校验通过后决定是否回显）

### 2. 模型配置（检测用）

| 路径 | 说明 |
|------|------|
| `models/<模型名>.onnx` | 检测模型文件 |
| `models/<模型名>.json` | 与 onnx 同名的检测配置（如 `models/apexlegends.json`） |

**模型 JSON 字段（如 `models/apexlegends.json`）：**

- `size`: 推理尺寸（如 `320`）
- `conf_thres`: 置信度阈值（如 `0.4`）
- `iou_thres`: IoU 阈值（如 `0.9`）
- `classes`: 类别字符串（如 `"0"`）

---

## 三、汇总表（按文件）

| 文件 | 常量/配置要点 |
|------|----------------|
| `src/main.rs` | 字号、间距、行高、颜色、许可证常量；默认吸附/连点/手柄/许可证配置；字体路径 |
| `src/modules/screen_capture_thread.rs` | Apex 窗口标题、基准高度、武器 ROI 坐标与间隔 |
| `src/modules/weapon_rec_thread.rs` | 目标宽高、Canny 阈值、边缘阈值、空手判定阈值与字符串 |
| `src/modules/gamepad_mapping_thread.rs` | 连点白名单武器列表、连续错误上限 |
| `src/modules/enemy_det_thread.rs` | 检测默认 size/conf/iou/classes、连续错误上限 |
| `src/modules/gamepad_reading_thread.rs` | 调试开关与调试文本、连续错误上限 |
| `src/modules/update_check.rs` | GitHub Release URL、请求超时 |
| `src/utils.rs` | 手柄映射默认值、ConfigFile（含 license_code）、configs 路径与 .current 路径 |
| `build.rs` | gun_template 目录、生成到 src/build/gun_templates.rs、exe 图标 3mz_ds_ver.png |
| `configs/*.json` | 运行时主配置（吸附曲线、手柄映射、连点模式、license_code 等） |
| `configs/.current` | 当前使用的配置名与模型名 |
| `models/*.json` | 检测模型推理参数（size, conf_thres, iou_thres, classes） |

---

*文档由项目扫描生成，修改常量或配置后请以实际代码与 JSON 为准。*
