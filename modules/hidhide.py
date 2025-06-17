import sys
import ctypes
from ctypes import wintypes
import win32file, win32con

class HidHideController:
    # —— WinAPI & 常量 定义 —— 
    kernel32 = ctypes.WinDLL('kernel32', use_last_error=True)

    GENERIC_READ     = 0x80000000
    GENERIC_WRITE    = 0x40000000
    FILE_SHARE_READ  = 0x00000001
    FILE_SHARE_WRITE = 0x00000002
    OPEN_EXISTING    = 3
    ERROR_INSUFFICIENT_BUFFER = 122

    # —— 来自 Logic.h 的设备类型 —— 
    IoControlDeviceType = 32769

    METHOD_BUFFERED  = 0
    FILE_READ_DATA   = 1

    @staticmethod
    def CTL_CODE(dev_type, func, method, access):
        return (dev_type << 16) | (access << 14) | (func << 2) | method

    # —— 根据 Logic.h & API 文档 定义的 IOCTL 码 —— 
    IOCTL_GET_WHITELIST  = CTL_CODE.__func__(IoControlDeviceType, 2048, METHOD_BUFFERED, FILE_READ_DATA)
    IOCTL_SET_WHITELIST  = CTL_CODE.__func__(IoControlDeviceType, 2049, METHOD_BUFFERED, FILE_READ_DATA)
    IOCTL_GET_BLACKLIST  = CTL_CODE.__func__(IoControlDeviceType, 2050, METHOD_BUFFERED, FILE_READ_DATA)
    IOCTL_SET_BLACKLIST  = CTL_CODE.__func__(IoControlDeviceType, 2051, METHOD_BUFFERED, FILE_READ_DATA)
    IOCTL_GET_ACTIVE     = CTL_CODE.__func__(IoControlDeviceType, 2052, METHOD_BUFFERED, FILE_READ_DATA)
    IOCTL_SET_ACTIVE     = CTL_CODE.__func__(IoControlDeviceType, 2053, METHOD_BUFFERED, FILE_READ_DATA)

    def __init__(self):
        self.handle = None
        self.open()

    def open(self):
        """打开 \\.\HidHide 控制设备，保存句柄"""
        h = self.kernel32.CreateFileW(
            r'\\.\HidHide',
            self.GENERIC_READ | self.GENERIC_WRITE,
            self.FILE_SHARE_READ | self.FILE_SHARE_WRITE,
            None,
            self.OPEN_EXISTING,
            0,
            None
        )
        if h == wintypes.HANDLE(-1).value:
            raise ctypes.WinError(ctypes.get_last_error())
        self.handle = h
        return h

    def _get_list(self, ioctl):
        """内部：读取 double-null-terminated wide-string 列表"""
        buf_size = 8 * 1024
        max_buf_size = 256 * 1024

        while True:
            out_buf = (ctypes.c_wchar * (buf_size // 2))()
            ret = wintypes.DWORD()
            ok = self.kernel32.DeviceIoControl(
                self.handle, ioctl,
                None, 0,
                out_buf, ctypes.sizeof(out_buf),
                ctypes.byref(ret),
                None
            )
            if ok:
                raw = ''.join(out_buf[: ret.value // 2])
                return [p for p in raw.split('\x00') if p]
            
            err = ctypes.get_last_error()
            if err == self.ERROR_INSUFFICIENT_BUFFER and buf_size < max_buf_size:
                buf_size *= 2
                # print(f"缓冲区不够，扩大到 {buf_size} 字节后重试")
                continue

            # 不是缓冲区不足导致的错误，直接抛异常
            raise ctypes.WinError(err)

    def _set_list(self, ioctl, paths):
        """内部：写入 double-null-terminated wide-string 列表"""
        data = ('\x00'.join(paths) + '\x00\x00').encode('utf-16le')
        ret = wintypes.DWORD()
        ok = self.kernel32.DeviceIoControl(
            self.handle, ioctl,
            data, len(data),
            None, 0,
            ctypes.byref(ret),
            None
        )
        if not ok:
            raise ctypes.WinError(ctypes.get_last_error())

    def get_whitelist(self):
        return self._get_list(self.IOCTL_GET_WHITELIST)

    def set_whitelist(self, paths):
        self._set_list(self.IOCTL_SET_WHITELIST, paths)

    def get_blacklist(self):
        return self._get_list(self.IOCTL_GET_BLACKLIST)

    def set_blacklist(self, paths):
        self._set_list(self.IOCTL_SET_BLACKLIST, paths)

    def set_active(self, on: bool):
        """启用或关闭隐藏功能"""
        buf = (ctypes.c_ubyte * 1)(1 if on else 0)
        ret = wintypes.DWORD()
        ok = self.kernel32.DeviceIoControl(
            self.handle, self.IOCTL_SET_ACTIVE,
            buf, 1,
            None, 0,
            ctypes.byref(ret),
            None
        )
        if not ok:
            raise ctypes.WinError(ctypes.get_last_error())

    def add_this_to_whitelist(self, exe: str):
        exe = self.get_nt_path(exe)
        wl = self.get_whitelist()
        wl.append(exe)
        self.set_whitelist(wl)

    def set_this_to_visible(self, instance: str, state):
        if state:
            bl = self.get_blacklist()
            bl.append(instance)
            self.set_blacklist(bl)
        else:
            bl = [p for p in self.get_blacklist() if p != instance]
            self.set_blacklist(bl)
        self.set_active(state)

    def get_nt_path(self, win32_path):
        h = win32file.CreateFile(
            win32_path,
            win32con.GENERIC_READ,
            win32con.FILE_SHARE_READ|win32con.FILE_SHARE_WRITE|win32con.FILE_SHARE_DELETE,
            None,
            win32con.OPEN_EXISTING,
            win32con.FILE_ATTRIBUTE_NORMAL,
            None
        )
        nt = win32file.GetFinalPathNameByHandle(
            h,
            win32con.VOLUME_NAME_NT  # 或 FILE_NAME_NORMALIZED|VOLUME_NAME_NT
        )
        win32file.CloseHandle(h)
        return nt  # e.g. "\\Device\\HarddiskVolume3\\Users\\white\\…\\python.exe"

    def close(self):
        """关闭句柄（如果需要）"""
        if self.handle:
            self.kernel32.CloseHandle(self.handle)
            self.handle = None

if __name__ == "__main__":
    if len(sys.argv) != 2:
        print("用法: python test_hidhide.py <instance_path>")
        sys.exit(1)
    instance_path = sys.argv[1]
    controller = HidHideController()

    # 打开设备
    controller.open()
    print("✔ 已打开 HidHide 设备")

    # 1) 将当前 Python 解释器加入白名单
    exe = sys.executable
    wl = controller.get_whitelist()
    print("当前白名单:", wl)
    if exe not in wl:
        wl.append(exe)
        controller.set_whitelist(wl)
        print(f"✔ 已将 [{exe}] 添加到白名单")
    else:
        print(f"⚠ [{exe}] 已在白名单中")

    # 2) 更新黑名单
    bl = controller.get_blacklist()
    print("当前黑名单:", bl)
    if instance_path not in bl:
        bl.append(instance_path)
        controller.set_blacklist(bl)
        print(f"✔ 已将 [{instance_path}] 写入黑名单")
    else:
        print(f"⚠ [{instance_path}] 已在黑名单中")

    # 3) 启用隐藏功能
    controller.set_active(True)
    print("✔ 隐藏功能已开启 (active=1)")

    input(">>> 设备已隐藏，按回车继续恢复…")

    # 4) 关闭隐藏 & 清理名单
    controller.set_active(False)
    print("✔ 隐藏功能已关闭 (active=0)")
    bl = [p for p in controller.get_blacklist() if p != instance_path]
    controller.set_blacklist(bl)
    print(f"✔ 已从黑名单移除 [{instance_path}]")
    print("✔ 操作完成，设备已恢复可见")

    controller.close()
