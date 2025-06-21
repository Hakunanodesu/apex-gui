import ctypes
import os
import traceback
import sys
import time

from pywinusb import hid
import XInput

# 导入日志模块
from utils.logger import get_logger


def detect_controller_by_a():
    """
    等待用户按下物理手柄的 A 键，返回该手柄的索引 (0–3)。
    """
    # 加载 user32.dll
    _user32 = ctypes.windll.user32
    VK_ESCAPE = 0x1B
    start_time = time.time()
    timeout = 30  # 30秒超时
    
    while time.time() - start_time < timeout:
        if bool(_user32.GetAsyncKeyState(VK_ESCAPE) & 0x8000):
            return False
        
        for i in range(4):
            try:
                # 添加更安全的XInput调用
                state = XInput.get_state(i)
                if state is None:
                    continue
                gamepad = state.Gamepad
                if gamepad is None:
                    continue
                    
                if (gamepad.wButtons & 0x1000) != 0:
                    # 等待按键释放
                    while True:
                        try:
                            current_state = XInput.get_state(i)
                            if current_state is None or current_state.Gamepad is None:
                                break
                            if (current_state.Gamepad.wButtons & 0x1000) == 0:
                                break
                            time.sleep(0.005)
                        except Exception:
                            break
                    return f"{i}"
            except Exception as e:
                # 记录异常但不中断循环
                logger = get_logger()
                logger.debug(f"XInput检测异常 (控制器 {i}): {e}")
                continue
        time.sleep(0.005)
    
    # 超时返回False
    return False

def handle_exception(e):
    """处理异常并记录到日志"""
    logger = get_logger()
    logger.log_exception(e, "工具函数异常")
    sys.stdout.write(f"\n{traceback.format_exc()}\n")

def list_subdirs(path):
    # 列出 path 下的所有条目，并筛选出目录
    if not os.path.exists(path):
        return []
    return [name for name in os.listdir(path)
            if os.path.isdir(os.path.join(path, name))]

def get_screenshot_region_dxcam(screenshot_size):
    user32 = ctypes.windll.user32
    screen_width = user32.GetSystemMetrics(0)
    screen_height = user32.GetSystemMetrics(1)

    region_left = (screen_width  - screenshot_size) // 2
    region_top  = (screen_height - screenshot_size) // 2

    region = (
        region_left, 
        region_top, 
        region_left + screenshot_size, 
        region_top + screenshot_size
    )
    return region

def get_scaling_factor():
    # 获取当前活动窗口的 DPI（仅支持 Windows 8.1 及以上）
    try:
        # 设置 DPI 感知
        ctypes.windll.shcore.SetProcessDpiAwareness(2)  # PROCESS_PER_MONITOR_DPI_AWARE = 2
    except Exception:
        pass  # 某些旧系统不支持

    # 获取屏幕 DPI
    hdc = ctypes.windll.user32.GetDC(0)
    dpi = ctypes.windll.gdi32.GetDeviceCaps(hdc, 88)  # LOGPIXELSX
    ctypes.windll.user32.ReleaseDC(0, hdc)

    # 计算放大系数
    scaling = dpi / 96  # 96 是默认 DPI
    return scaling

def enum_hid_devices():
    """
    枚举所有 HID 设备，仅返回以下字段：
      - vendor_id    (hex 字符串)
      - product_id   (hex 字符串)
      - device_path  (原始路径)
      - product_name (HID 描述符名称)
    返回：
        List[Dict[str, str]]
    """
    devices = hid.find_all_hid_devices()
    info_set = set()
    for dev in devices:
        name = dev.product_name.split(";")[-1]
        vid = hex(dev.vendor_id)
        pid = hex(dev.product_id)
        instance_id = "HID\\" + "\\".join(dev.device_path.split("#")[1:-1])
        info_set.add((name, vid, pid, instance_id))
    return info_set

def median_of_three(x, max, min): # 比min，max嵌套函数更快
    if x < min:
        return min
    elif x > max:
        return max
    else:
        return x
    
def check_xbox_controller_available(controller_id: int) -> bool:
    """
    检查指定ID的Xbox手柄是否可用
    """
    try:
        state = XInput.get_state(controller_id)
        if state is None or state.Gamepad is None:
            return False
        return True
    except Exception:
        return False

if __name__ == "__main__":
    # for dev in enum_hid_devices():
    #     if dev[1] in ("0x54c", "0x45e"):
    #         print(dev)
    print(list_subdirs("C:/Program Files/Nefarius Software Solutions"))