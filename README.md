# TargetDetect-for-Controller

## 项目概述

一个基于深度学习的实时目标检测系统，集成了 YOLO 目标检测模型、ONNXRUNTIME 加速和手柄控制映射功能。该项目能够通过屏幕截图进行目标检测和追踪，并将手柄输入映射到虚拟 Xbox 360/DualShock 4 手柄，实现自动化控制。

## 主要特性

- 🎯 **实时目标检测**：基于 YOLO 模型的高效目标识别
- 🎮 **手柄映射**：DualSense 手柄的映射，原始输入将被拦截
- 📸 **屏幕捕获**：实时屏幕截图和目标追踪
- ⚡ **GPU 加速**：只要您的 GPU 支持 DX12 就可以
- 🔧 **易于配置**：初始化和配置工具
- 🎯 **目标追踪**：智能目标跟踪算法

## 系统要求

- Windows 操作系统
- 现代 GPU
- 手柄（目前只支持 DualSense）

## 使用方法

**启动检测和控制**

双击 run.exe 文件即可启动程序。

## 致谢

- [ViGEmBus](https://github.com/nefarius/ViGEmBus) - Windows kernel-mode driver emulating well-known USB game controllers
- [HidHide](https://github.com/nefarius/HidHide) - Gaming Input Peripherals Device Firewall for Windows
- [PySDL2](https://github.com/py-sdl/py-sdl2) - Python ctypes wrapper around SDL2

## ⚠️ **重要提示**：

- 请勿用于游戏辅助用途，仅供学习交流使用

## 技术支持

如遇问题，请：
1. 检查系统要求
2. 查看项目 Issues 页面
3. 提交详细的问题报告
