# Assisted Pursuit with Effortless eXecution

为了追求更高的性能，我们从 Python 全面转向了 Rust！

## 项目概述

A.P.E.X.，又称 Assisted Pursuit with Effortless eXecution，一个基于深度学习的实时目标检测系统，使用 Rust 语言开发，集成了 YOLO 目标检测模型、ONNXRUNTIME DirectML 加速和手柄控制映射功能。该项目能够通过屏幕截图进行目标检测和追踪，并将手柄输入映射到虚拟 Xbox 360 手柄，实现自动化控制。

## 主要特性

- 🎯 **实时目标检测**：基于 YOLO 模型的高效目标识别
- 🎮 **手柄映射**：DualSense 手柄的映射，原始输入将被拦截
- 📸 **屏幕捕获**：实时屏幕截图和目标追踪
- ⚡ **GPU 加速**：使用 DirectML 进行 GPU 加速推理
- 🔧 **易于配置**：初始化和配置工具
- 🎯 **目标追踪**：智能目标跟踪算法
- 🦀 **Rust 开发**：高性能、内存安全的系统级编程语言
- 🖥️ **现代 UI**：基于 egui 的现代化用户界面

## 技术栈

- **语言**: Rust 2024 Edition
- **UI 框架**: egui + eframe
- **输入设备**: SDL2 + hidapi
- **虚拟手柄**: ViGEmBus（crate: `vigem-client`）
- **屏幕捕获**: `windows-capture`
- **AI 推理**: `ort`（ONNX Runtime with DirectML）
- **图像/数值**: `image`, `ndarray`
- **配置/序列化**: `serde`, `serde_json`
- **HTTP 请求**: `reqwest`
- **Windows API**: `windows`

## 系统要求

- Windows 10/11（x64）
- 支持 DirectX 12 的 GPU
- DualSense 或 Xbox 手柄（原始输入将被 HidHide 隐藏）
- ViGEmBus 与 HidHide 驱动（程序内可下载与安装）
- Rust 工具链（用于本地构建）

## 项目结构

```
src/
├── main.rs                  # 主程序入口与 UI 逻辑
├── modules/                 # 核心模块
│   ├── bg_con_mapping.rs    # 手柄映射/输出
│   ├── bg_con_reading.rs    # 手柄原始读取 + HidHide 设备隐藏
│   ├── bg_onnx_dml_od.rs    # ONNX 推理（DirectML）
│   ├── bg_screen_cap.rs     # 屏幕捕获
│   ├── hidhide.rs           # HidHide CLI 调用封装
│   └── mod.rs
├── utils/                   # 工具与辅助
│   ├── tools.rs             # 配置、驱动检测、文本测量等
│   ├── ui.rs                # 自定义 UI 组件与下载进度部件
│   ├── console_redirect.rs  # 控制台重定向
│   ├── ps_con_reenable.rs   # 设备重枚举
│   ├── bg_dl_instl.rs       # 后台下载与安装
│   └── mod.rs
└── fonts/                   # 字体文件（内嵌加载）
```

## 配置说明

- 配置文件: 根目录 `config.json`（若不存在则自动使用默认值并在保存时创建）
- 结构与默认值：

```jsonc
{
  "outer_size": "320",
  "mid_size": "80",
  "inner_size": "80",
  "outer_str": "0.2",
  "mid_str": "0.4",
  "inner_str": "0.4",
  "deadzone": "0.0",
  "hipfire": "0.6",
  "reverse_coef": "0.5",
  "aim_height": "0.7"
}
```

- **含义简述**：
  - **outer_size/mid_size/inner_size**: 不同圈层大小参数
  - **outer_str/mid_str/inner_str**: 各圈层强度系数
  - **deadzone**: 摇杆死区
  - **hipfire**: 腰射/基础跟随强度
  - **reverse_coef**: 反冲系数（模拟人类反应时间）
  - **aim_height**: 准星高度偏置（0~1）

## 构建与运行

```powershell
# 安装 Rust（若未安装）
# 访问 https://rustup.rs 获取安装方式

# 构建发布版
cargo build --release

# 运行调试
cargo run
```

## 使用方法

1. 打开程序后，按界面提示安装/重新安装：**ViGEmBus**、**HidHide**。
2. 连接手柄，列表中会显示检测到的设备，原始输入会被 HidHide 隐藏。
3. 在 UI 中调整参数（可实时保存到 `config.json`）。
4. 启用屏幕捕获和目标检测，开始映射输出至虚拟 Xbox 360 手柄。

## 致谢

- [ViGEmBus](https://github.com/nefarius/ViGEmBus) - Windows kernel-mode driver emulating well-known USB game controllers
- [HidHide](https://github.com/nefarius/HidHide) - Gaming Input Peripherals Device Firewall for Windows
- [egui](https://github.com/emilk/egui) - Simple, fast, and highly portable immediate mode GUI library
- [ONNX Runtime](https://github.com/microsoft/onnxruntime) - Cross-platform, high performance ML inferencing and training accelerator

## ⚠️ **重要提示**

- 本项目仅供学习和研究目的使用
- 请勿用于游戏辅助或其他违规用途
- 使用前请确保遵守相关法律法规和平台规则

## 技术支持

如遇问题，请：

1. 检查系统要求和依赖项
2. 查看项目 [Issues](https://github.com/Hakunanodesu/Assisted-Pursuit-with-Effortless-eXecution/issues) 页面
3. 提交详细的问题报告，包含：
   - 操作系统版本
   - Rust 版本 (`rustc --version`)
   - 错误信息和日志
   - 复现步骤
