# A.P.E.X - Assisted Pursuit with Effortless eXecution

> 🎯 基于Rust开发的高性能智能瞄准辅助系统

[![Rust](https://img.shields.io/badge/rust-2024-orange.svg)](https://www.rust-lang.org/)
[![License](https://img.shields.io/badge/license-MIT-blue.svg)](LICENSE)
[![Platform](https://img.shields.io/badge/platform-Windows-blue.svg)](https://www.microsoft.com/windows)

## 🚀 项目简介

A.P.E.X 是一个基于深度学习和计算机视觉技术的实时目标检测与追踪系统。采用 Rust 语言开发，集成了 YOLO 目标检测模型、ONNX Runtime DirectML GPU 加速，以及精确的输入设备映射功能。

### 核心特性

- 🎯 **智能目标检测** - 基于YOLO模型的实时目标识别与追踪
- 🎮 **双模式输入** - 支持手柄映射和键鼠模式
- 📸 **高效屏幕捕获** - 优化的实时屏幕截图与处理
- ⚡ **GPU 硬件加速** - DirectML 驱动的高性能推理
- 🔧 **一键驱动安装** - 自动下载安装 ViGEmBus 和 HidHide
- 🎛️ **实时参数调节** - 可视化配置界面，支持实时预览
- 🖥️ **现代化界面** - 基于 egui 的直观用户界面
- 💾 **配置持久化** - 自动保存用户偏好设置

## 🛠️ 技术架构

### 核心技术栈
- **语言**: Rust 2024 Edition
- **UI 框架**: egui + eframe
- **图形加速**: ONNX Runtime + DirectML
- **输入处理**: SDL2 + hidapi
- **虚拟设备**: ViGEmBus (vigem-client)
- **屏幕捕获**: windows-capture
- **图像处理**: image + ndarray
- **配置管理**: serde + serde_json

## 📋 系统要求

### 最低配置
- **操作系统**: Windows 10 1903+ / Windows 11
- **CPU**: x64 架构处理器
- **内存**: 4GB RAM
- **显卡**: 支持 DirectX 12 的 GPU
- **存储**: 200MB 可用空间

### 推荐配置
- **显卡**: NVIDIA RTX 20系列+ / AMD RX 6000系列+
- **内存**: 8GB+ RAM
- **CPU**: Intel i5-8400+ / AMD Ryzen 5 2600+

### 外设支持
- **手柄模式**: DualSense、Xbox 手柄等 HID 兼容设备
- **键鼠模式**: 支持 Raspberry Pi Pico 设备

## 📁 项目结构

```
src/
├── main.rs                     # 程序入口和主UI逻辑
├── modules/                    # 核心功能模块
│   ├── bg_con_mapping.rs       # 手柄映射与输出控制
│   ├── bg_con_reading.rs       # 原始输入读取
│   ├── bg_mouse_mapping.rs     # 键鼠映射控制
│   ├── bg_onnx_dml_od.rs      # ONNX推理引擎
│   ├── bg_screen_cap.rs       # 屏幕捕获模块
│   ├── hidhide.rs             # HidHide驱动控制
│   └── mod.rs
├── utils/                      # 工具和辅助功能
│   ├── tools.rs               # 配置管理、设备检测
│   ├── ui.rs                  # UI组件库
│   ├── console_redirect.rs    # 控制台重定向
│   ├── ps_con_reenable.rs     # 设备重枚举
│   ├── bg_dl_instl.rs         # 后台下载安装
│   └── mod.rs
└── fonts/                      # 内嵌字体资源
    ├── NotoSansCJKsc-Regular.otf
    └── OpenMoji-black-glyf.ttf
```

## ⚙️ 配置说明

程序会在根目录自动创建 `config.json` 配置文件：

```json
{
  "outer_size": "320.0",      // 外圈检测范围大小
  "inner_size": "80.0",      // 内圈检测范围大小
  "outer_str": "0.2",         // 外圈响应强度 (0.0-1.0)
  "inner_str": "0.4",         // 内圈响应强度 (0.0-1.0)
  "deadzone": "0.0",          // 摇杆死区大小 (0.0-1.0)
  "hipfire": "0.6",           // 腰射系数 (0.0-1.0)
  "aim_height": "0.6",        // 瞄准高度偏移 (0.0-1.0)
  "mouse_mode": "false"       // 键鼠模式开关
}
```

### 参数说明
- **检测范围**: 控制目标检测的敏感区域大小
- **响应强度**: 调节不同区域的输出强度
- **死区设置**: 防止微小抖动干扰
- **腰射系数**: 非瞄准状态下的响应强度
- **瞄准高度**: 垂直方向的瞄准偏移量

## 🏗️ 构建与运行

### 环境准备
```powershell
# 1. 安装 Rust (如果未安装)
# 访问 https://rustup.rs 下载安装程序

# 2. 验证安装
rustc --version
cargo --version
```

### 编译运行
```powershell
# 克隆项目
git clone https://github.com/your-username/Assisted-Pursuit-with-Effortless-eXecution.git
cd Assisted-Pursuit-with-Effortless-eXecution

# 调试模式运行
cargo run

# 发布版本构建
cargo build --release

# 运行发布版本
./target/release/apex.exe
```

## 📖 使用指南

### 初次使用
1. **驱动安装**
   - 启动程序后，按界面提示安装 ViGEmBus 和 HidHide 驱动
   - 程序会自动下载并引导安装过程

2. **设备连接**
   - **手柄模式**: 连接支持的游戏手柄，程序会自动检测
   - **键鼠模式**: 连接 Raspberry Pi Pico 设备

3. **参数调节**
   - 展开"参数设置"面板
   - 根据使用场景调整检测范围和响应强度
   - 参数会自动保存到配置文件

### 功能操作
1. **启动映射**
   - 确保设备已正确连接且驱动已安装
   - 点击"智能映射"开关启动系统
   - 观察状态指示器确认各模块正常运行

2. **预览模式**
   - 启用"识别预览"查看实时检测结果
   - 启用"窗口置顶"保持界面可见

3. **模式切换**
   - 使用"键鼠模式"开关在手柄和键鼠模式间切换
   - 模式切换会自动调整相关设备检测

## 🔧 故障排除

### 常见问题

**Q: 程序无法启动或崩溃**
- 确保系统满足最低配置要求
- 检查是否安装了最新的 Microsoft Visual C++ 可再发行组件
- 尝试以管理员权限运行

**Q: 手柄无法识别**
- 检查手柄是否正确连接
- 点击"刷新"按钮重新扫描设备
- 确认 HidHide 驱动已正确安装

**Q: 目标检测不准确**
- 调整检测范围参数
- 确保 GPU 驱动为最新版本
- 检查系统资源占用情况

**Q: 映射输出无响应**
- 确认 ViGEmBus 驱动已安装
- 检查目标应用是否支持 Xbox 手柄输入
- 尝试重新启动映射功能

### 日志调试
程序运行时会在控制台输出详细日志信息，可用于问题诊断。

## 📄 许可证

本项目采用 MIT 许可证 - 详见 [LICENSE](LICENSE) 文件

## ⚠️ 免责声明

- 本项目仅供学习、研究和技术交流使用
- 请勿将本软件用于任何可能违反游戏规则或法律法规的用途
- 使用者需自行承担使用本软件的风险和责任
- 开发者不对因使用本软件而产生的任何后果负责

## 🤝 贡献指南

欢迎提交 Issue 和 Pull Request！

1. Fork 本项目
2. 创建特性分支 (`git checkout -b feature/AmazingFeature`)
3. 提交更改 (`git commit -m 'Add some AmazingFeature'`)
4. 推送到分支 (`git push origin feature/AmazingFeature`)
5. 开启 Pull Request

## 📞 技术支持

如遇问题，请：

1. 检查[常见问题](#故障排除)章节
2. 搜索现有 [Issues](https://github.com/your-username/Assisted-Pursuit-with-Effortless-eXecution/issues)
3. 创建新 Issue，请包含：
   - 详细的问题描述
   - 系统环境信息
   - 错误日志和截图
   - 复现步骤

## 🙏 致谢

感谢以下开源项目的支持：

- [ViGEmBus](https://github.com/nefarius/ViGEmBus) - Windows 虚拟游戏控制器驱动
- [HidHide](https://github.com/nefarius/HidHide) - Windows 输入设备防火墙
- [egui](https://github.com/emilk/egui) - 即时模式GUI库
- [ONNX Runtime](https://github.com/microsoft/onnxruntime) - 跨平台机器学习推理加速器

---

<div align="center">
  <b>⭐ 如果这个项目对你有帮助，请考虑给个星标！ ⭐</b>
</div>
