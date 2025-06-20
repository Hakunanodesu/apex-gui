import sys
import ctypes
from ctypes import wintypes


# === Class Definition ===
class DeviceReplugCM:
    """
    使用 SetupAPI 执行设备软拔插（禁用 + 启用）。
    """
    # === 常量定义 ===
    DIGCF_PRESENT      = 0x00000002
    DIGCF_ALLCLASSES   = 0x00000004

    DIF_PROPERTYCHANGE = 0x12

    DICS_ENABLE        = 0x00000001
    DICS_DISABLE       = 0x00000002
    DICS_FLAG_GLOBAL   = 0x00000001

    def __init__(self):
        # 加载 setupapi DLL
        self.setupapi = ctypes.WinDLL("setupapi", use_last_error=True)
        # 绑定函数原型
        self._bind_functions()

    def _bind_functions(self):
        self.SetupDiGetClassDevsW = self.setupapi.SetupDiGetClassDevsW
        self.SetupDiGetClassDevsW.argtypes = [ctypes.POINTER(GUID), wintypes.LPCWSTR, wintypes.HWND, wintypes.DWORD]
        self.SetupDiGetClassDevsW.restype  = wintypes.HANDLE

        self.SetupDiEnumDeviceInfo = self.setupapi.SetupDiEnumDeviceInfo
        self.SetupDiEnumDeviceInfo.argtypes = [wintypes.HANDLE, wintypes.DWORD, ctypes.POINTER(SP_DEVINFO_DATA)]
        self.SetupDiEnumDeviceInfo.restype  = wintypes.BOOL

        self.SetupDiGetDeviceInstanceIdW = self.setupapi.SetupDiGetDeviceInstanceIdW
        self.SetupDiGetDeviceInstanceIdW.argtypes = [wintypes.HANDLE, ctypes.POINTER(SP_DEVINFO_DATA),
                                                    wintypes.LPWSTR, wintypes.DWORD, ctypes.POINTER(wintypes.DWORD)]
        self.SetupDiGetDeviceInstanceIdW.restype  = wintypes.BOOL

        self.SetupDiSetClassInstallParamsW = self.setupapi.SetupDiSetClassInstallParamsW
        self.SetupDiSetClassInstallParamsW.argtypes = [wintypes.HANDLE, ctypes.POINTER(SP_DEVINFO_DATA),
                                                      ctypes.POINTER(SP_PROPCHANGE_PARAMS), wintypes.DWORD]
        self.SetupDiSetClassInstallParamsW.restype  = wintypes.BOOL

        self.SetupDiCallClassInstaller = self.setupapi.SetupDiCallClassInstaller
        self.SetupDiCallClassInstaller.argtypes = [wintypes.DWORD, wintypes.HANDLE, ctypes.POINTER(SP_DEVINFO_DATA)]
        self.SetupDiCallClassInstaller.restype  = wintypes.BOOL

    def change_device_state(self, instance_id: str, enable: bool) -> bool:
        """
        对指定实例 ID 的设备执行启用或禁用操作。
        :param instance_id: 设备实例路径，例如 "USB\\VID_XXXX&PID_YYYY\\ZZZZ"
        :param enable: True = 启用，False = 禁用
        :return: 是否找到并执行了操作
        """
        flags = self.DIGCF_PRESENT | self.DIGCF_ALLCLASSES
        hdev = self.SetupDiGetClassDevsW(None, None, None, flags)
        if hdev == wintypes.HANDLE(-1).value:
            raise ctypes.WinError(ctypes.get_last_error())

        idx = 0
        devinfo = SP_DEVINFO_DATA()
        devinfo.cbSize = ctypes.sizeof(SP_DEVINFO_DATA)

        # 枚举所有设备
        buf_len = wintypes.DWORD(0)
        while self.SetupDiEnumDeviceInfo(hdev, idx, ctypes.byref(devinfo)):
            idx += 1
            # 获取设备实例 ID 缓冲区长度
            self.SetupDiGetDeviceInstanceIdW(hdev, ctypes.byref(devinfo), None, 0, ctypes.byref(buf_len))
            buffer = ctypes.create_unicode_buffer(buf_len.value + 1)
            if not self.SetupDiGetDeviceInstanceIdW(hdev, ctypes.byref(devinfo), buffer, buf_len.value + 1, None):
                continue

            dev_id = buffer.value
            if dev_id.lower() == instance_id.lower():
                # 填充 Property change 参数
                params = SP_PROPCHANGE_PARAMS()
                params.ClassInstallHeader.cbSize = ctypes.sizeof(SP_CLASSINSTALL_HEADER)
                params.ClassInstallHeader.InstallFunction = self.DIF_PROPERTYCHANGE
                params.StateChange = self.DICS_ENABLE if enable else self.DICS_DISABLE
                params.Scope     = self.DICS_FLAG_GLOBAL
                params.HwProfile = 0

                if not self.SetupDiSetClassInstallParamsW(hdev, ctypes.byref(devinfo), ctypes.byref(params), ctypes.sizeof(params)):
                    raise ctypes.WinError(ctypes.get_last_error())

                if not self.SetupDiCallClassInstaller(self.DIF_PROPERTYCHANGE, hdev, ctypes.byref(devinfo)):
                    raise ctypes.WinError(ctypes.get_last_error())

                return True
        return False

    def replug(self, instance_id: str):
        """
        对指定实例 ID 的设备执行软拔插（禁用 + 启用）。
        :param instance_id: 设备实例路径
        """
        # sys.stdout.write(f"→ 正在禁用 {instance_id} …")
        if not self.change_device_state(instance_id, enable=False):
            # sys.stdout.write("未找到目标设备，或禁用失败")
            return
        # sys.stdout.write("✔ 禁用成功，等待 1 秒重建枚举…")
        ctypes.windll.kernel32.Sleep(1000)

        # sys.stdout.write(f"→ 正在启用 {instance_id} …")
        if not self.change_device_state(instance_id, enable=True):
            # sys.stdout.write("启用失败")
            return
        # sys.stdout.write("✔ 启用成功，重插完成")


# === 结构体定义 ===
class GUID(ctypes.Structure):
    _fields_ = [
        ("Data1", wintypes.DWORD),
        ("Data2", wintypes.WORD),
        ("Data3", wintypes.WORD),
        ("Data4", wintypes.BYTE * 8),
    ]


class SP_DEVINFO_DATA(ctypes.Structure):
    _fields_ = [
        ("cbSize",     wintypes.DWORD),
        ("ClassGuid",  GUID),
        ("DevInst",    wintypes.DWORD),
        ("Reserved",   ctypes.c_ulonglong),
    ]


class SP_CLASSINSTALL_HEADER(ctypes.Structure):
    _fields_ = [
        ("cbSize",         wintypes.DWORD),
        ("InstallFunction", wintypes.DWORD),
    ]


class SP_PROPCHANGE_PARAMS(ctypes.Structure):
    _fields_ = [
        ("ClassInstallHeader", SP_CLASSINSTALL_HEADER),
        ("StateChange",        wintypes.DWORD),
        ("Scope",              wintypes.DWORD),
        ("HwProfile",          wintypes.DWORD),
    ]


# === 脚本入口 ===
if __name__ == "__main__":
    if len(sys.argv) >= 2:
        instance_id = sys.argv[1]
    else:
        # 示例默认 ID，可根据需要修改
        instance_id = 'HID\\vid_045e&pid_0b12&ig_00\\b&a75d153&0&0000'

    replugger = DeviceReplugCM()
    replugger.replug(instance_id)
