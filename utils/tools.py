import ctypes
import os
import traceback
import sys
import time

from pywinusb import hid
import XInput


def detect_controller_by_a():
    """
    等待用户按下物理手柄的 A 键，返回该手柄的索引 (0–3)。
    """
    # 加载 user32.dll
    _user32 = ctypes.windll.user32
    VK_ESCAPE = 0x1B
    while True:
        if bool(_user32.GetAsyncKeyState(VK_ESCAPE) & 0x8000):
            return False
        for i in range(4):
            try:
                state = XInput.get_state(i).Gamepad
            except Exception:
                continue
            if (state.wButtons & 0x1000) != 0:
                while (XInput.get_state(i).Gamepad.wButtons & 0x1000) != 0:
                    time.sleep(0.005)
                return f"{i}"
        time.sleep(0.005)

def handle_exception(e):
    sys.stdout.write(f"\n{traceback.format_exc()}\n")

def list_subdirs(path):
    # 列出 path 下的所有条目，并筛选出目录
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
    

if __name__ == "__main__":
    for dev in enum_hid_devices():
        if dev[1] in ("0x54c", "0x45e"):
            print(dev)