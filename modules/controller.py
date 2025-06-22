import sys
import time
import threading
import json

import vgamepad as vg
import XInput
from sdl2 import (
    SDL_Init, SDL_Quit,
    SDL_PumpEvents,
    SDL_NumJoysticks, SDL_IsGameController,
    SDL_GameControllerOpen, SDL_GameControllerClose,
    SDL_GameControllerGetAxis, SDL_GameControllerGetButton,
    SDL_JoystickGetDeviceVendor, SDL_JoystickGetDeviceProduct,
    SDL_INIT_VIDEO, SDL_INIT_GAMECONTROLLER,
    SDL_CONTROLLER_AXIS_LEFTX, SDL_CONTROLLER_AXIS_LEFTY,
    SDL_CONTROLLER_AXIS_RIGHTX, SDL_CONTROLLER_AXIS_RIGHTY,
    SDL_CONTROLLER_AXIS_TRIGGERLEFT, SDL_CONTROLLER_AXIS_TRIGGERRIGHT,
    SDL_CONTROLLER_BUTTON_A, SDL_CONTROLLER_BUTTON_B,
    SDL_CONTROLLER_BUTTON_X, SDL_CONTROLLER_BUTTON_Y,
    SDL_CONTROLLER_BUTTON_LEFTSHOULDER, SDL_CONTROLLER_BUTTON_RIGHTSHOULDER,
    SDL_CONTROLLER_BUTTON_BACK, SDL_CONTROLLER_BUTTON_START,
    SDL_CONTROLLER_BUTTON_LEFTSTICK, SDL_CONTROLLER_BUTTON_RIGHTSTICK,
    SDL_CONTROLLER_BUTTON_DPAD_UP, SDL_CONTROLLER_BUTTON_DPAD_RIGHT,
    SDL_CONTROLLER_BUTTON_DPAD_DOWN, SDL_CONTROLLER_BUTTON_DPAD_LEFT,
    SDL_CONTROLLER_BUTTON_GUIDE, SDL_CONTROLLER_BUTTON_TOUCHPAD
)

from utils.tools import handle_exception, median_of_three
from utils.logger import get_logger


RT_REPEAT_INTERVAL = 0.01  # 连点间隔（秒）


class XboxWirelessToX360Mapper:
    # ——— XInput 按键掩码 ———
    XINPUT_GAMEPAD_DPAD_UP        = 0x0001
    XINPUT_GAMEPAD_DPAD_DOWN      = 0x0002
    XINPUT_GAMEPAD_DPAD_LEFT      = 0x0004
    XINPUT_GAMEPAD_DPAD_RIGHT     = 0x0008
    XINPUT_GAMEPAD_START          = 0x0010
    XINPUT_GAMEPAD_BACK           = 0x0020
    XINPUT_GAMEPAD_LEFT_THUMB     = 0x0040
    XINPUT_GAMEPAD_RIGHT_THUMB    = 0x0080
    XINPUT_GAMEPAD_LEFT_SHOULDER  = 0x0100
    XINPUT_GAMEPAD_RIGHT_SHOULDER = 0x0200
    XINPUT_GAMEPAD_GUIDE          = 0x0400
    XINPUT_GAMEPAD_A              = 0x1000
    XINPUT_GAMEPAD_B              = 0x2000
    XINPUT_GAMEPAD_X              = 0x4000
    XINPUT_GAMEPAD_Y              = 0x8000

    def __init__(self, physical_id: int = None):
        """
        poll_interval: 每帧轮询 XInput 的间隔（秒）。
        """
        with open("user_config.json", "r") as f:
            config = json.load(f)
        self.controller_name = config["controller"]["Name"]
        self.interval = 0.004
        self._stop = threading.Event()
        self._thread = None
        self.phys_id = physical_id
        self.vpad = None
        self._lock = threading.Lock()  # 添加线程锁

        # 新增：右摇杆偏移量（单位与 XInput 原始值相同，signed 16-bit）
        self.rx_offset = 0
        self.ry_offset = 0
        
        # 新增：连点 RT 相关变量
        self.rt_repeat_enabled = False
        self.rt_repeat_state = False  # True = 按下, False = 释放
        self.rt_repeat_timer = 0
        self.rt_repeat_interval = RT_REPEAT_INTERVAL  # 连点间隔（秒）

    def get_trigger_values(self) -> tuple[int, int]:
        """
        读取并返回当前左右扳机值 (0–255)。
        若读取失败，则返回 (0, 0)。
        """
        try:
            with self._lock:
                state = XInput.get_state(self.phys_id)
                if state is None or state.Gamepad is None:
                    return 0, 0
                s = state.Gamepad
                return s.bLeftTrigger, s.bRightTrigger
        except Exception as e:
            # 记录异常但不抛出
            logger = get_logger()
            logger.debug(f"获取扳机值异常: {e}")
            return 0, 0
    
    def add_rx_ry_offset(self, rx_offset: int, ry_offset: int):
        """
        设置右摇杆 X/Y 轴的偏移值。
          - rx_offset, ry_offset: 要应用的偏移（signed 16-bit）
        """
        with self._lock:
            self.rx_offset = int(rx_offset * 255)
            self.ry_offset = -int(ry_offset * 255)
    
    def set_rt_repeat(self, enabled: bool):
        """
        设置 RT 连点功能
        """
        with self._lock:
            self.rt_repeat_enabled = enabled
            if not enabled:
                self.rt_repeat_state = False
                self.rt_repeat_timer = 0

    def _map_loop(self):
        """
        后台线程函数：不断读取物理手柄状态并映射到虚拟 360 手柄。
        """
        consecutive_errors = 0
        max_consecutive_errors = 10
        
        while not self._stop.is_set():
            try:
                with self._lock:
                    state = XInput.get_state(self.phys_id)
                    if state is None or state.Gamepad is None:
                        consecutive_errors += 1
                        if consecutive_errors > max_consecutive_errors:
                            break
                        time.sleep(self.interval)
                        continue
                    
                    s = state.Gamepad
                    consecutive_errors = 0  # 重置错误计数

                # —— 摇杆 ——  
                self.vpad.left_joystick(x_value=s.sThumbLX, y_value=s.sThumbLY)

                # —— 右摇杆（加偏移 & 截断到 signed 16-bit）——  
                rx = s.sThumbRX + self.rx_offset
                ry = s.sThumbRY + self.ry_offset
                # 截断范围 -32768..32767
                rx = median_of_three(rx, 32767, -32768)
                ry = median_of_three(ry, 32767, -32768)
                self.vpad.right_joystick(x_value=rx, y_value=ry)

                # —— 扳机 ——  
                self.vpad.left_trigger(value=s.bLeftTrigger)
                
                # RT 扳机处理（包含连点逻辑）
                if self.rt_repeat_enabled:
                    current_time = time.time()
                    if current_time - self.rt_repeat_timer >= self.rt_repeat_interval:
                        self.rt_repeat_state = not self.rt_repeat_state
                        self.rt_repeat_timer = current_time
                    
                    if self.rt_repeat_state:
                        self.vpad.right_trigger(value=255)  # 完全按下
                    else:
                        self.vpad.right_trigger(value=0)    # 完全释放
                else:
                    self.vpad.right_trigger(value=s.bRightTrigger)

                # —— 主按钮 ——  
                for mask, btn in [
                    (self.XINPUT_GAMEPAD_A, vg.XUSB_BUTTON.XUSB_GAMEPAD_A),
                    (self.XINPUT_GAMEPAD_B, vg.XUSB_BUTTON.XUSB_GAMEPAD_B),
                    (self.XINPUT_GAMEPAD_X, vg.XUSB_BUTTON.XUSB_GAMEPAD_X),
                    (self.XINPUT_GAMEPAD_Y, vg.XUSB_BUTTON.XUSB_GAMEPAD_Y),
                ]:
                    if s.wButtons & mask:
                        self.vpad.press_button(button=btn)
                    else:
                        self.vpad.release_button(button=btn)

                # —— 其他按钮 ——  
                for mask, btn in [
                    (self.XINPUT_GAMEPAD_LEFT_SHOULDER, vg.XUSB_BUTTON.XUSB_GAMEPAD_LEFT_SHOULDER),
                    (self.XINPUT_GAMEPAD_RIGHT_SHOULDER, vg.XUSB_BUTTON.XUSB_GAMEPAD_RIGHT_SHOULDER),
                    (self.XINPUT_GAMEPAD_BACK,           vg.XUSB_BUTTON.XUSB_GAMEPAD_BACK),
                    (self.XINPUT_GAMEPAD_START,          vg.XUSB_BUTTON.XUSB_GAMEPAD_START),
                    (self.XINPUT_GAMEPAD_LEFT_THUMB,     vg.XUSB_BUTTON.XUSB_GAMEPAD_LEFT_THUMB),
                    (self.XINPUT_GAMEPAD_RIGHT_THUMB,    vg.XUSB_BUTTON.XUSB_GAMEPAD_RIGHT_THUMB),
                    (self.XINPUT_GAMEPAD_GUIDE,          vg.XUSB_BUTTON.XUSB_GAMEPAD_GUIDE),
                    (self.XINPUT_GAMEPAD_DPAD_UP,    vg.XUSB_BUTTON.XUSB_GAMEPAD_DPAD_UP),
                    (self.XINPUT_GAMEPAD_DPAD_RIGHT, vg.XUSB_BUTTON.XUSB_GAMEPAD_DPAD_RIGHT),
                    (self.XINPUT_GAMEPAD_DPAD_DOWN,  vg.XUSB_BUTTON.XUSB_GAMEPAD_DPAD_DOWN),
                    (self.XINPUT_GAMEPAD_DPAD_LEFT,  vg.XUSB_BUTTON.XUSB_GAMEPAD_DPAD_LEFT),
                ]:
                    if s.wButtons & mask:
                        self.vpad.press_button(button=btn)
                    else:
                        self.vpad.release_button(button=btn)

                self.vpad.update()
                
            except Exception as e:
                consecutive_errors += 1
                logger = get_logger()
                logger.debug(f"映射循环异常: {e}")
                if consecutive_errors > max_consecutive_errors:
                    logger.error(f"连续错误过多，停止映射循环: {e}")
                    break
                time.sleep(self.interval)
                continue
                
            time.sleep(self.interval)

    def start(self):
        """
        检测手柄并启动映射：创建虚拟手柄、启动线程。
        """
        try:
            # 创建虚拟手柄实例
            self.vpad = vg.VX360Gamepad()
            self._stop.clear()
            self._thread = threading.Thread(target=self._map_loop, daemon=True)
            self._thread.start()
            sys.stdout.write(f">>> {self.controller_name} → 虚拟 Xbox 360")
            return True
        except Exception as e:
            logger = get_logger()
            logger.error(f"启动Xbox映射器失败: {e}")
            return False

    def stop(self):
        """
        停止映射并移除虚拟设备。
        """
        self._stop.set()
        if self._thread:
            try:
                self._thread.join(timeout=2.0)  # 添加超时
            except Exception:
                pass
        # 删除虚拟手柄对象，触发其析构方法以移除设备
        try:
            if self.vpad:
                del self.vpad
        except Exception:
            pass
        self.vpad = None


class DualSenseToDS4Mapper:
    """
    使用 SDL2 GameController API 读取指定 VID/PID 的 DualSense 控制器，
    并将输入映射到虚拟 DualShock 4 手柄 (vgamepad)
    """

    def __init__(self):
        with open("user_config.json", "r") as f:
            config = json.load(f)
        self.vendor_id = 0x054C
        self.product_id = int(config["controller"]["Product_ID"], 16)
        self.controller_name = config["controller"]["Name"]
        self.poll_interval = 0.002
        self._sdl_controller = None
        self._mapping_thread = None
        self._stop_event = threading.Event()
        self.virtual_gamepad = vg.VDS4Gamepad()
        self._sdl_inited = False

        self.rx_offset = 0
        self.ry_offset = 0
        
        # 新增：连点 RT 相关变量
        self.rt_repeat_enabled = False
        self.rt_repeat_state = False  # True = 按下, False = 释放
        self.rt_repeat_timer = 0
        self.rt_repeat_interval = RT_REPEAT_INTERVAL  # 连点间隔（秒）

    def _init_sdl(self) -> bool:
        if not self._sdl_inited:
            if SDL_Init(SDL_INIT_VIDEO | SDL_INIT_GAMECONTROLLER) != 0:
                sys.stdout.write("SDL_Init Error: failed to initialize GameController\n")
                return False
            self._sdl_inited = True
        # 确保事件队列最新
        SDL_PumpEvents()
        # 打开对应 VID/PID 的手柄
        count = SDL_NumJoysticks()
        for idx in range(count):
            if SDL_IsGameController(idx):
                vid = SDL_JoystickGetDeviceVendor(idx)
                pid = SDL_JoystickGetDeviceProduct(idx)
                if vid == self.vendor_id and pid == self.product_id:
                    # 关闭上次未关闭的手柄
                    if self._sdl_controller:
                        SDL_GameControllerClose(self._sdl_controller)
                    self._sdl_controller = SDL_GameControllerOpen(idx)
                    sys.stdout.write(f">>> {self.controller_name} → 虚拟 DualShock 4")
                    return True
        sys.stdout.write(f"\n>>> 未找到 VID=0x{self.vendor_id:04X}, PID=0x{self.product_id:04X} 的手柄\n")
        return False

    def get_trigger_values(self) -> tuple[int, int]:
        """
        返回当前左右扳机的映射值，格式 (left_trigger, right_trigger)，范围 0..255。
        如果还没初始化或没找到手柄，则返回 (0, 0)。
        """

        # 刷新输入状态
        SDL_PumpEvents()

        # 读取原始轴值
        lt_raw = SDL_GameControllerGetAxis(self._sdl_controller, SDL_CONTROLLER_AXIS_TRIGGERLEFT)
        rt_raw = SDL_GameControllerGetAxis(self._sdl_controller, SDL_CONTROLLER_AXIS_TRIGGERRIGHT)

        # 转换并返回
        lt = self._axis_to_byte_trigger(lt_raw)
        rt = self._axis_to_byte_trigger(rt_raw)
        return lt, rt

    def add_rx_ry_offset(self, rx_offset: int, ry_offset: int):
        """
        设置右摇杆 X/Y 轴的偏移量（单位：0–255）。
        新的摇杆值 = 归一化后值 + offset，并在 0–255 之间截断。
        """
        self.rx_offset = int(rx_offset)
        self.ry_offset = int(ry_offset)

    # 归一化轴数据：-32768..+32767 -> 0..255
    def _axis_to_byte_signed(self, val):
        return (val + 32768) * 255 // 65535
    
    # 触发器轴：0..32767 -> 0..255
    def _axis_to_byte_trigger(self, val):
        return val * 255 // 32767

    def set_rt_repeat(self, enabled: bool):
        """
        设置 RT 连点功能
        """
        self.rt_repeat_enabled = enabled
        if not enabled:
            self.rt_repeat_state = False
            self.rt_repeat_timer = 0

    def _map_to_ds4(self):
        """将 SDL 控制器状态映射到 DS4 虚拟手柄"""
        # 左摇杆
        lx = self._axis_to_byte_signed(SDL_GameControllerGetAxis(self._sdl_controller, SDL_CONTROLLER_AXIS_LEFTX))
        ly = self._axis_to_byte_signed(SDL_GameControllerGetAxis(self._sdl_controller, SDL_CONTROLLER_AXIS_LEFTY))
        self.virtual_gamepad.left_joystick(x_value=lx, y_value=ly)

        # 右摇杆（加偏移 & 截断到 0–255）
        rx = self._axis_to_byte_signed(
            SDL_GameControllerGetAxis(self._sdl_controller, SDL_CONTROLLER_AXIS_RIGHTX)
        ) + self.rx_offset
        ry = self._axis_to_byte_signed(
            SDL_GameControllerGetAxis(self._sdl_controller, SDL_CONTROLLER_AXIS_RIGHTY)
        ) + self.ry_offset
        # 截断到 [0, 255]
        rx = median_of_three(rx, 255, 0)
        ry = median_of_three(ry, 255, 0)
        self.virtual_gamepad.right_joystick(x_value=rx, y_value=ry)

        # 扳机映射
        lt = self._axis_to_byte_trigger(SDL_GameControllerGetAxis(self._sdl_controller, SDL_CONTROLLER_AXIS_TRIGGERLEFT))
        self.virtual_gamepad.left_trigger(lt)
        
        # RT 扳机处理（包含连点逻辑）
        if self.rt_repeat_enabled:
            current_time = time.time()
            if current_time - self.rt_repeat_timer >= self.rt_repeat_interval:
                self.rt_repeat_state = not self.rt_repeat_state
                self.rt_repeat_timer = current_time
            
            if self.rt_repeat_state:
                self.virtual_gamepad.right_trigger(255)  # 完全按下
            else:
                self.virtual_gamepad.right_trigger(0)    # 完全释放
        else:
            rt = self._axis_to_byte_trigger(SDL_GameControllerGetAxis(self._sdl_controller, SDL_CONTROLLER_AXIS_TRIGGERRIGHT))
            self.virtual_gamepad.right_trigger(rt)

        # 普通按钮映射
        btn_map = [
            (SDL_CONTROLLER_BUTTON_A, vg.DS4_BUTTONS.DS4_BUTTON_CROSS),
            (SDL_CONTROLLER_BUTTON_B, vg.DS4_BUTTONS.DS4_BUTTON_CIRCLE),
            (SDL_CONTROLLER_BUTTON_X, vg.DS4_BUTTONS.DS4_BUTTON_SQUARE),
            (SDL_CONTROLLER_BUTTON_Y, vg.DS4_BUTTONS.DS4_BUTTON_TRIANGLE),
            (SDL_CONTROLLER_BUTTON_LEFTSHOULDER, vg.DS4_BUTTONS.DS4_BUTTON_SHOULDER_LEFT),
            (SDL_CONTROLLER_BUTTON_RIGHTSHOULDER, vg.DS4_BUTTONS.DS4_BUTTON_SHOULDER_RIGHT),
            (SDL_CONTROLLER_BUTTON_BACK, vg.DS4_BUTTONS.DS4_BUTTON_SHARE),
            (SDL_CONTROLLER_BUTTON_START, vg.DS4_BUTTONS.DS4_BUTTON_OPTIONS),
            (SDL_CONTROLLER_BUTTON_LEFTSTICK, vg.DS4_BUTTONS.DS4_BUTTON_THUMB_LEFT),
            (SDL_CONTROLLER_BUTTON_RIGHTSTICK, vg.DS4_BUTTONS.DS4_BUTTON_THUMB_RIGHT),
        ]
        for sdl_btn, ds4_btn in btn_map:
            if SDL_GameControllerGetButton(self._sdl_controller, sdl_btn):
                self.virtual_gamepad.press_button(ds4_btn)
            else:
                self.virtual_gamepad.release_button(ds4_btn)

        # D-Pad 映射
        dpad = vg.DS4_DPAD_DIRECTIONS.DS4_BUTTON_DPAD_NONE
        if SDL_GameControllerGetButton(self._sdl_controller, SDL_CONTROLLER_BUTTON_DPAD_UP): 
            dpad = vg.DS4_DPAD_DIRECTIONS.DS4_BUTTON_DPAD_NORTH
        elif SDL_GameControllerGetButton(self._sdl_controller, SDL_CONTROLLER_BUTTON_DPAD_RIGHT): 
            dpad = vg.DS4_DPAD_DIRECTIONS.DS4_BUTTON_DPAD_EAST
        elif SDL_GameControllerGetButton(self._sdl_controller, SDL_CONTROLLER_BUTTON_DPAD_DOWN): 
            dpad = vg.DS4_DPAD_DIRECTIONS.DS4_BUTTON_DPAD_SOUTH
        elif SDL_GameControllerGetButton(self._sdl_controller, SDL_CONTROLLER_BUTTON_DPAD_LEFT): 
            dpad = vg.DS4_DPAD_DIRECTIONS.DS4_BUTTON_DPAD_WEST
        self.virtual_gamepad.directional_pad(direction=dpad)

        # PS (Guide) 键
        if SDL_GameControllerGetButton(self._sdl_controller, SDL_CONTROLLER_BUTTON_GUIDE):
            self.virtual_gamepad.press_special_button(vg.DS4_SPECIAL_BUTTONS.DS4_SPECIAL_BUTTON_PS)
        else:
            self.virtual_gamepad.release_special_button(vg.DS4_SPECIAL_BUTTONS.DS4_SPECIAL_BUTTON_PS)

        # 触控板点击 (如果 SDL 支持)
        try:
            pressed = SDL_GameControllerGetButton(self._sdl_controller, SDL_CONTROLLER_BUTTON_TOUCHPAD)
            if pressed:
                self.virtual_gamepad.press_special_button(vg.DS4_SPECIAL_BUTTONS.DS4_SPECIAL_BUTTON_TOUCHPAD)
            else:
                self.virtual_gamepad.release_special_button(vg.DS4_SPECIAL_BUTTONS.DS4_SPECIAL_BUTTON_TOUCHPAD)
        except AttributeError:
            pass  # SDL 版本不支持触控板按钮

        self.virtual_gamepad.update()

    def start(self) -> bool:
        if not self._init_sdl():
            return False
        self._stop_event.clear()
        def loop():
            try:
                while not self._stop_event.is_set():
                    SDL_PumpEvents()  # 处理 SDL 事件，保证输入更新
                    self._map_to_ds4()
                    time.sleep(self.poll_interval)
            except Exception as e:
                sys.stdout.write(">>> 映射循环遇到异常：\n")
                handle_exception(e)
        self._mapping_thread = threading.Thread(target=loop, daemon=True)
        self._mapping_thread.start()
        return True

    def stop(self):
        self._stop_event.set()
        if self._mapping_thread:
            self._mapping_thread.join(timeout=0)
        self._cleanup()

    def _cleanup(self):
        if self._sdl_controller:
            SDL_GameControllerClose(self._sdl_controller)
            self._sdl_controller = None
        if self._sdl_inited:
            SDL_Quit()
            self._sdl_inited = False
        if self.virtual_gamepad:
            try:
                del self.virtual_gamepad
            except AttributeError:
                pass
            self.virtual_gamepad = None


if __name__ == "__main__":
    # mapper = DualSenseToDS4Mapper(vendor_id=0x054C, product_id=0x0DF2)
    # if mapper.start():
    #     print(">>> 开始通过 SDL2 读取 DualSense 并映射到 DS4，按 Ctrl+C 退出")
    #     try:
    #         while True:
    #             time.sleep(1)
    #     except KeyboardInterrupt:
    #         print("\n>>> 检测到 Ctrl+C，正在停止…")
    #     finally:
    #         mapper.stop()
    mapper = XboxWirelessToX360Mapper()
    mapper.start()
    try:
        while True:
            time.sleep(1)
    except KeyboardInterrupt:
        mapper.stop()
    finally:
        time.sleep(10)