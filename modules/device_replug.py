import ctypes
from ctypes import wintypes

class DeviceReplugCM:
    """
    使用 cfgmgr32 CM API 实现 HID 设备的软拔插（禁用→重新枚举→启用→重新枚举）。
    示例:
        replugger = DeviceReplugCM("HID\\VID_054C&PID_0DF2&MI_03\\B&15D9A947&0&0000")
        replugger.replug()
    """
    # 加载 cfgmgr32.dll
    _cfgmgr32 = ctypes.WinDLL("cfgmgr32", use_last_error=True)

    # CM API 常量
    _CM_LOCATE_DEVNODE_NORMAL = 0x00000000
    _CR_SUCCESS = 0x00000000

    # CM API 函数原型
    _CM_Locate_DevNodeW = _cfgmgr32.CM_Locate_DevNodeW
    _CM_Locate_DevNodeW.argtypes = [ctypes.POINTER(wintypes.DWORD), wintypes.LPCWSTR, wintypes.ULONG]
    _CM_Locate_DevNodeW.restype  = wintypes.ULONG

    _CM_Disable_DevNode = _cfgmgr32.CM_Disable_DevNode
    _CM_Disable_DevNode.argtypes = [wintypes.DWORD, wintypes.ULONG]
    _CM_Disable_DevNode.restype  = wintypes.ULONG

    _CM_Enable_DevNode = _cfgmgr32.CM_Enable_DevNode
    _CM_Enable_DevNode.argtypes = [wintypes.DWORD, wintypes.ULONG]
    _CM_Enable_DevNode.restype  = wintypes.ULONG

    _CM_Reenumerate_DevNode = _cfgmgr32.CM_Reenumerate_DevNode
    _CM_Reenumerate_DevNode.argtypes = [wintypes.DWORD, wintypes.ULONG]
    _CM_Reenumerate_DevNode.restype  = wintypes.ULONG

    def __init__(self):
        self.instance_id = None
        self.devInst = wintypes.DWORD()

    def _locate_devnode(self, instance_id: str):
        """
        定位 DevNode，并存入 self.devInst
        """
        rc = self._CM_Locate_DevNodeW(
            ctypes.byref(self.devInst), 
            instance_id, 
            self._CM_LOCATE_DEVNODE_NORMAL
        )
        if rc != self._CR_SUCCESS:
            raise OSError(f"CM_Locate_DevNodeW failed: 0x{rc:04X}")

    def disable(self):
        """
        禁用设备
        """
        rc = self._CM_Disable_DevNode(self.devInst.value, 0)
        if rc != self._CR_SUCCESS:
            raise OSError(f"CM_Disable_DevNode failed: 0x{rc:04X}")

    def enable(self):
        """
        启用设备
        """
        rc = self._CM_Enable_DevNode(self.devInst.value, 0)
        if rc != self._CR_SUCCESS:
            raise OSError(f"CM_Enable_DevNode failed: 0x{rc:04X}")

    def reenumerate(self):
        """
        重新枚举设备
        """
        rc = self._CM_Reenumerate_DevNode(self.devInst.value, 0)
        if rc != self._CR_SUCCESS:
            raise OSError(f"CM_Reenumerate_DevNode failed: 0x{rc:04X}")

    def replug(self, instance_id: str):
        """
        执行软拔插: 禁用 -> 重新枚举 -> 启用 -> 重新枚举
        """
        self._locate_devnode(instance_id)
        self.disable()
        self.reenumerate()
        self.enable()
        self.reenumerate()

# 如果直接运行此脚本，可接受命令行参数
if __name__ == '__main__':
    instance_id = "HID\\vid_045e&pid_0b13&rev_0509&0c3526101353&ig_00\\d&32dc9e2a&0&0000"
    rp = DeviceReplugCM()
    rp.replug(instance_id)
    print(f"✔ 软拔插完成: {instance_id}")
