import pywinusb.hid as hid
import vgamepad as vg
import threading
import time
import sys

from utils.tools import median_of_three, handle_exception


class DualSenseToDS4Mapper:
    """
    DualSenseToDS4Mapper 类：
    - 使用 pywinusb.hid 读取 DualSense 控制器的原始 HID 输入
    - 使用 vgamepad 创建虚拟 DualShock 4 手柄，并将 DualSense 输入映射到虚拟手柄
    - 提供 start() 和 stop() 方法，方便在外部控制映射循环的启动和停止
    """

    def __init__(self, product_id: int, path: str):
        """
        初始化 DualSenseToDS4Mapper。

        :param vendor_id: DualSense 供应商 ID，默认使用 Sony（0x054C）
        :param product_id: DualSense 产品 ID，默认使用 DualSense Edge（0x0DF2）
        :param poll_interval: 映射循环的轮询间隔（秒）
        """
        self.vendor_id = 0x054c
        self.product_id = product_id
        self.path = path
        self.poll_interval = 0.002

        # 存储 DualSense 的实时状态
        self.dual_sense_state = {
            "lx": 0,       # 左摇杆 X 轴 (0-255)
            "ly": 0,       # 左摇杆 Y 轴 (0-255)
            "rx": 0,       # 右摇杆 X 轴 (0-255)
            "ry": 0,       # 右摇杆 Y 轴 (0-255)
            "lt": 0,       # 左扳机 (0-255)
            "rt": 0,       # 右扳机 (0-255)
            "shoulders_sticks_share_options": 0,  # 按钮掩码
            "buttons_dpad": 0,                  # 按钮掩码
            "touchpad_ps": 0,                  # 触控板和PS键状态 (0-3)
        }

        # 支持右摇杆叠加（偏移量）
        self.rx_override = None
        self.ry_override = None

        # 虚拟 Xbox 360 手柄对象（使用 vgamepad）
        self.virtual_gamepad = vg.VDS4Gamepad()

        # DualSense 设备对象（pywinusb.hid.HidDevice），方便后续关闭
        self._hid_device = None

        # 线程控制
        self._mapping_thread = None
        self._stop_event = threading.Event()

    def _input_handler(self, data: bytearray):
        """
        PyWinUSB 的回调函数。当 DualSense 有新输入时调用。

        :param data: bytearray，长度因设备而异
        """
        # 基本检查
        if len(data) < 11:  # 修改为11以包含新的数据位
            return

        # 更新摇杆和扳机状态
        self.dual_sense_state["lx"] = data[1]
        self.dual_sense_state["ly"] = data[2]
        self.dual_sense_state["rx"] = data[3]
        self.dual_sense_state["ry"] = data[4]
        self.dual_sense_state["lt"] = data[5]
        self.dual_sense_state["rt"] = data[6]

        # 更新按钮掩码，高字节在 data[9]，低字节在 data[8]
        self.dual_sense_state["shoulders_sticks_share_options"] = data[9]
        self.dual_sense_state["buttons_dpad"] = data[8]
        
        # 更新触控板和PS键状态
        self.dual_sense_state["touchpad_ps"] = data[10]

    def _find_and_register_dualsense(self) -> bool:
        """
        查找并打开 DualSense 设备，注册输入回调。

        :return: 如果成功找到并注册 DualSense，返回 True；否则返回 False
        """
        all_devices = hid.HidDeviceFilter(
            vendor_id=self.vendor_id,
            product_id=self.product_id,
            path=self.path
        ).get_devices()

        if not all_devices:
            sys.stdout.write("\n>>> 未找到 DualSense 设备，请检查手柄是否连接。")
            return False

        # 只打开第一个匹配的 DualSense 设备
        self._hid_device = all_devices[0]
        self._hid_device.open()
        # 注册回调，将收到的新数据传给 _input_handler 方法
        self._hid_device.set_raw_data_handler(lambda data: self._input_handler(data))
        sys.stdout.write(f"\n>>> 已连接并注册 DualSense (VID:0x{self.vendor_id:04X}, PID:0x{self.product_id:04X})")
        return True

    def add_rx_ry_offset(self, dx: int = 0, dy: int = 0):
        """在原始值上添加偏移量"""
        rx = self.dual_sense_state["rx"]
        ry = self.dual_sense_state["ry"]
        self.rx_override = median_of_three(rx + dx, 255, 0)
        self.ry_override = median_of_three(ry + dy, 255, 0)

    def _map_to_ds4(self):
        """
        将当前 dual_sense_state 中的信息映射到虚拟 DualShock 4 手柄。
        """
        # 1. 左摇杆
        lx = self.dual_sense_state["lx"]
        ly = self.dual_sense_state["ly"]
        self.virtual_gamepad.left_joystick(x_value=lx, y_value=ly)

        # 2. 右摇杆
        rx = int(self.rx_override) if self.rx_override is not None else self.dual_sense_state["rx"]
        ry = int(self.ry_override) if self.ry_override is not None else self.dual_sense_state["ry"]
        self.virtual_gamepad.right_joystick(x_value=rx, y_value=ry)

        # 3. 扳机（vgamepad 接受 0-255）
        self.virtual_gamepad.left_trigger(self.dual_sense_state["lt"])
        self.virtual_gamepad.right_trigger(self.dual_sense_state["rt"])

        # 4. 按钮 & D-Pad
        btns = self.dual_sense_state["buttons_dpad"]
        shoulders = self.dual_sense_state["shoulders_sticks_share_options"] & 0x0F

        # 肩键（L1/R1）- 独立处理，不受扳机影响
        self.virtual_gamepad.release_button(vg.DS4_BUTTONS.DS4_BUTTON_SHOULDER_LEFT)
        self.virtual_gamepad.release_button(vg.DS4_BUTTONS.DS4_BUTTON_SHOULDER_RIGHT)
        # 左肩键 (L1)
        if (shoulders & 0x01) != 0:
            self.virtual_gamepad.press_button(vg.DS4_BUTTONS.DS4_BUTTON_SHOULDER_LEFT)
        # 右肩键 (R1)
        if (shoulders & 0x02) != 0:
            self.virtual_gamepad.press_button(vg.DS4_BUTTONS.DS4_BUTTON_SHOULDER_RIGHT)

        # A 按钮
        if (btns & 0x20) != 0:
            self.virtual_gamepad.press_button(button=vg.DS4_BUTTONS.DS4_BUTTON_CROSS)
        else:
            self.virtual_gamepad.release_button(button=vg.DS4_BUTTONS.DS4_BUTTON_CROSS)
        # B 按钮
        if (btns & 0x40) != 0:
            self.virtual_gamepad.press_button(button=vg.DS4_BUTTONS.DS4_BUTTON_CIRCLE)
        else:
            self.virtual_gamepad.release_button(button=vg.DS4_BUTTONS.DS4_BUTTON_CIRCLE)
        # X 按钮
        if (btns & 0x10) != 0:
            self.virtual_gamepad.press_button(button=vg.DS4_BUTTONS.DS4_BUTTON_SQUARE)
        else:
            self.virtual_gamepad.release_button(button=vg.DS4_BUTTONS.DS4_BUTTON_SQUARE)
        # Y 按钮
        if (btns & 0x80) != 0:
            self.virtual_gamepad.press_button(button=vg.DS4_BUTTONS.DS4_BUTTON_TRIANGLE)
        else:
            self.virtual_gamepad.release_button(button=vg.DS4_BUTTONS.DS4_BUTTON_TRIANGLE)

        # 镜头相关的按键：Back、Start、LeftThumb、RightThumb
        sticks = self.dual_sense_state["shoulders_sticks_share_options"] & 0xF0
        # Back
        if (sticks & 0x10) != 0:
            self.virtual_gamepad.press_button(button=vg.DS4_BUTTONS.DS4_BUTTON_SHARE)
        else:
            self.virtual_gamepad.release_button(button=vg.DS4_BUTTONS.DS4_BUTTON_SHARE)
        # Start
        if (sticks & 0x20) != 0:
            self.virtual_gamepad.press_button(button=vg.DS4_BUTTONS.DS4_BUTTON_OPTIONS)
        else:
            self.virtual_gamepad.release_button(button=vg.DS4_BUTTONS.DS4_BUTTON_OPTIONS)
            
        # Left Thumb（L3）
        if (sticks & 0x40) != 0:
            self.virtual_gamepad.press_button(button=vg.DS4_BUTTONS.DS4_BUTTON_THUMB_LEFT)
        else:
            self.virtual_gamepad.release_button(button=vg.DS4_BUTTONS.DS4_BUTTON_THUMB_LEFT)
        # Right Thumb（R3）
        if (sticks & 0x80) != 0:
            self.virtual_gamepad.press_button(button=vg.DS4_BUTTONS.DS4_BUTTON_THUMB_RIGHT)
        else:
            self.virtual_gamepad.release_button(button=vg.DS4_BUTTONS.DS4_BUTTON_THUMB_RIGHT)

        # —— D-Pad ——  
        # hat 值 0–7 表示 8 个方向，8 表示无输入
        hat = self.dual_sense_state["buttons_dpad"] & 0x0F
        # 完整枚举请看：vg.DS4_DPAD_DIRECTIONS
        # 比如 hat==0 -> DS4_BUTTON_DPAD_NORTH
        direction = {
            0: vg.DS4_DPAD_DIRECTIONS.DS4_BUTTON_DPAD_NORTH,
            1: vg.DS4_DPAD_DIRECTIONS.DS4_BUTTON_DPAD_NORTHEAST,
            2: vg.DS4_DPAD_DIRECTIONS.DS4_BUTTON_DPAD_EAST,
            3: vg.DS4_DPAD_DIRECTIONS.DS4_BUTTON_DPAD_SOUTHEAST,
            4: vg.DS4_DPAD_DIRECTIONS.DS4_BUTTON_DPAD_SOUTH,
            5: vg.DS4_DPAD_DIRECTIONS.DS4_BUTTON_DPAD_SOUTHWEST,
            6: vg.DS4_DPAD_DIRECTIONS.DS4_BUTTON_DPAD_WEST,
            7: vg.DS4_DPAD_DIRECTIONS.DS4_BUTTON_DPAD_NORTHWEST,
            8: vg.DS4_DPAD_DIRECTIONS.DS4_BUTTON_DPAD_NONE,
        }.get(hat, vg.DS4_DPAD_DIRECTIONS.DS4_BUTTON_DPAD_NONE)
        self.virtual_gamepad.directional_pad(direction=direction)

        # —— PS 和触控板 ——  
        touchpad_ps = self.dual_sense_state["touchpad_ps"]
        # PS 键
        if touchpad_ps & 0x01:
            self.virtual_gamepad.press_special_button(special_button=vg.DS4_SPECIAL_BUTTONS.DS4_SPECIAL_BUTTON_PS)
        else:
            self.virtual_gamepad.release_special_button(special_button=vg.DS4_SPECIAL_BUTTONS.DS4_SPECIAL_BUTTON_PS)
        # 触控板键
        if touchpad_ps & 0x02:
            self.virtual_gamepad.press_special_button(special_button=vg.DS4_SPECIAL_BUTTONS.DS4_SPECIAL_BUTTON_TOUCHPAD)
        else:
            self.virtual_gamepad.release_special_button(special_button=vg.DS4_SPECIAL_BUTTONS.DS4_SPECIAL_BUTTON_TOUCHPAD)

        # 最后送出报告
        self.virtual_gamepad.update()

    def start(self):
        """
        启动映射循环：
        1. 查找并注册 DualSense
        2. 创建虚拟手柄（已在 __init__ 中创建）
        3. 启动一个后台线程，不断将 DualSense 输入映射到虚拟手柄
        """
        if not self._find_and_register_dualsense():
            return False

        sys.stdout.write("\n>>> 虚拟 DualShock 4 手柄已创建。DualSense 的输入将直接映射到虚拟手柄上。")

        # 重置（防止多次调用 start）
        self._stop_event.clear()

        # 后台线程负责循环映射
        def run_loop():
            try:
                while not self._stop_event.is_set():
                    self._map_to_ds4()
                    time.sleep(self.poll_interval)
            except Exception as e:
                sys.stdout.write(f">>> 映射循环遇到异常：")
                handle_exception(e)

        self._mapping_thread = threading.Thread(target=run_loop, daemon=True)
        self._mapping_thread.start()
        return True

    def stop(self):
        """
        停止映射循环，并关闭 DualSense 设备、重置虚拟手柄。
        """
        self._stop_event.set()
        if self._mapping_thread:
            self._mapping_thread.join(timeout=0)
        self._cleanup()

    def _cleanup(self):
        """
        清理工作：关闭 DualSense 设备、重置虚拟手柄并提交更新
        """
        if self._hid_device:
            try:
                self._hid_device.close()
                sys.stdout.write("\n>>> DualSense 设备已关闭。")
            except Exception:
                pass
            finally:
                self._hid_device = None

        if self.virtual_gamepad:
            try:
                self.virtual_gamepad.reset()
                self.virtual_gamepad.update()
                if hasattr(self.virtual_gamepad, 'vigem_disconnect'):
                    self.virtual_gamepad.vigem_disconnect()
                sys.stdout.write("\n>>> 虚拟手柄已重置并关闭。")
            except Exception:
                pass
            finally:
                self.virtual_gamepad = None


if __name__ == "__main__":
    import sys
    import time

    # 默认 DualSense Edge 的 PID，可按需修改
    PRODUCT_ID = 0x028E

    # 实例化 mapper
    mapper = DualSenseToDS4Mapper(product_id=PRODUCT_ID)

    # debug 用的回调：先打印原始 data，再交给 _input_handler
    def debug_handler(data: bytearray):
        time.sleep(0.5)
        print("Raw HID data:", list(data))
        mapper._input_handler(data)

    # 找设备并注册
    if not mapper._find_and_register_dualsense():
        sys.exit(1)

    # 覆盖原始 handler 为 debug_handler
    mapper._hid_device.set_raw_data_handler(debug_handler)

    print(">>> 开始读取 DualSense 原始数据，按 Ctrl+C 停止")
    try:
        # 保持主线程存活
        while True:
            time.sleep(1)
    except KeyboardInterrupt:
        print("\n>>> 检测到 Ctrl+C，正在退出…")
    finally:
        mapper.stop()

