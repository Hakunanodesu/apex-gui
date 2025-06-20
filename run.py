import os
import sys
import json
import time
import threading
import tkinter as tk
from tkinter import scrolledtext
from tkinter import messagebox

import numpy as np

from modules.device_replug import DeviceReplugCM
from modules.hidhide import HidHideController
from modules.onnx import APV5Experimental
from modules.controller import DualSenseToDS4Mapper, XboxWirelessToX360Mapper
from modules.initialize import InitApp
from modules.aim_configurate import CFGApp
from utils.grab_screen import ScreenGrabber
from utils.delay_stdout import DelayedStdoutRedirector
from utils.tools import (
    get_screenshot_region_dxcam, 
    list_subdirs, enum_hid_devices, 
    handle_exception, detect_controller_by_a
)


class App:
    def __init__(self, root):
        self.root = root
        self.root.title("TGC v1.1.2")

        self.running = False
        self.mapper_running = False
        self.logic_started = False  # 标记 run_logic 是否已成功启动
        self.thread = None
        self.mapper = None

        try:
            with open("user_config.json", "r") as f:
                config = json.load(f)
            self.vendor_id = int(config["controller"]["Vendor_ID"], 16)
        except Exception:
            self.vendor_id = None

        # 日志输出区
        self.output = scrolledtext.ScrolledText(root, height=15, width=80, state='disabled')
        self.output.pack(fill='both', expand=True)
        sys.stdout = DelayedStdoutRedirector(self.output, interval_ms=50)

        self.model_path  = os.path.join(os.path.dirname(__file__), "dependencies", "apv5.onnx")
        sys.stdout.write("\n>>> 已识别到模型路径。")
        
        self.missing_drivers_link = []
        driver_ready = self.check_resources()
        instance_exist = False
        if driver_ready:
            instance_exist = self.check_instance()
        instance_exist = instance_exist and driver_ready

        # 按钮容器 Frame
        button_frame = tk.Frame(root)
        button_frame.pack(pady=(10, 0))

        # InitApp control button
        self.init_button = tk.Button(
            button_frame, 
            state='normal' if driver_ready else 'disabled', 
            text="初始化配置", 
            command=self.start_init
        )
        self.init_button.pack(side='left', padx=5)

        # CFGApp control button
        self.cfg_button = tk.Button(
            button_frame, 
            text="修改识别配置", 
            state='normal' if instance_exist else 'disabled', 
            command=self.open_cfg
        )
        self.cfg_button.pack(side='left', padx=5)

        # Mapper control button
        self.mapper_button = tk.Button(
            button_frame, 
            text="启动手柄映射", 
            state='normal' if instance_exist else 'disabled', 
            command=self.toggle_mapper
        )
        self.mapper_button.pack(side='left', padx=5)

        # Main logic button
        self.button = tk.Button(button_frame, text="启动智慧核心", state='disabled', command=self.toggle)
        self.button.pack(side='left', padx=5)

        # 延迟信息标签
        self.latency_str = (
            "[Latency] full cycle: waiting...\n"
            "[Latency] screen grab: waiting...\n"
            "[Latency] inference: waiting..."
        )
        self.latency_label = tk.Label(root, text=self.latency_str, font=("Arial", 10), justify=tk.LEFT)
        self.latency_label.pack(side="left", padx=10, pady=(5, 5))

        if not driver_ready:
            if_download = messagebox.askyesno("跳转下载", "是否下载缺失的驱动？")
            if if_download:
                sys.stdout.write("\n>>> 请在下载完成后安装到默认路径，然后重启软件（下载慢请使用代理）。如果安装完成后依旧检测不到，可能是因为您在非默认路径安装过该软件，请卸载后重新安装。")
                for link in self.missing_drivers_link:
                    os.startfile(link)
            else:
                sys.exit()
        else:
            self.hidhide = HidHideController()
            self.replugger = DeviceReplugCM()
            self.hidhide.add_this_to_whitelist(sys.executable)

        self.root.protocol("WM_DELETE_WINDOW", self.on_close)

    def check_instance(self):
        if os.path.exists("user_config.json"):
            with open("user_config.json", "r") as f:
                instance_id = json.load(f)["controller"]["Instance_ID"] 
            for device in enum_hid_devices():
                if device[3] == instance_id:
                    return True
            sys.stdout.write("\n>>> 检测到手柄实例变动，可能是手柄未插入或使用了新的手柄。若手柄未插入，请插入手柄后重新打开软件。若使用了新的手柄，请重新初始化。")
        else:
            sys.stdout.write("\n>>> 未检测到用户配置，请先初始化。")
        return False

    def check_resources(self):
        drivers = {
            "ViGEm Bus Driver": "https://github.com/nefarius/ViGEmBus/releases/download/v1.22.0/ViGEmBus_1.22.0_x64_x86_arm64.exe",
            "HidHide": "https://github.com/nefarius/HidHide/releases/download/v1.5.230.0/HidHide_1.5.230_x64.exe"
        }
        resources_dir = "C:/Program Files/Nefarius Software Solutions"
        essential_drivers = list_subdirs(resources_dir)
        owned_drivers = []
        missing_drivers = []
        if "ViGEm Bus Driver" in essential_drivers:
            owned_drivers.append("ViGEm Bus Driver")
        else:
            missing_drivers.append("ViGEm Bus Driver")
        if "HidHide" in essential_drivers:
            owned_drivers.append("HidHide")
        else:
            missing_drivers.append("HidHide")
        sys.stdout.write(f"\n>>> 已检测到 {'，'.join(owned_drivers)} 驱动。")
        if missing_drivers != []:
            sys.stdout.write(f"\n>>> 未检测到 {'，'.join(missing_drivers)} 驱动。")
            for driver in missing_drivers:
                self.missing_drivers_link.append(drivers[driver])
        else:
            sys.stdout.write("\n>>> 驱动已就绪。")
            return True
        return False

    def update_latency_label(self, text):
        self.latency_label.config(text=text)

    def open_cfg(self):
        try:
            sys.stdout.write("\n>>> 正在修改识别配置...")
            cfg_root = tk.Toplevel(self.root)
            cfg_root.grab_set()  # 使子窗口获得焦点，主窗口无法操作
            cfg_app = CFGApp(cfg_root)
            cfg_root.wait_window()
            cfg_root.grab_release()  # 释放焦点
            sys.stdout = DelayedStdoutRedirector(self.output, interval_ms=50)
            if cfg_app.force_quit:
                sys.stdout.write("\n>>> 识别配置修改未完成。")
                return
            sys.stdout.write("\n>>> 识别配置修改完成。")
        except Exception as e:
            if 'cfg_root' in locals():
                cfg_root.grab_release()  # 确保在发生异常时也释放焦点
            sys.stdout.write(f"\n>>> 识别配置修改时出错: {e}")

    def start_init(self):
        try:
            self.set_exclusive(state=False, verbose=False)
            sys.stdout.write("\n>>> 开始初始化配置...")
            init_root = tk.Toplevel(self.root)
            init_root.grab_set()  # 使子窗口获得焦点，主窗口无法操作
            init_app = InitApp(init_root)
            init_root.wait_window()
            init_root.grab_release()  # 释放焦点
            sys.stdout = DelayedStdoutRedirector(self.output, interval_ms=50)
            if init_app.force_quit:
                sys.stdout.write("\n>>> 初始化未完成。")
                return
            sys.stdout.write("\n>>> 配置初始化完成。")
            with open("user_config.json", "r") as f:
                config = json.load(f)
            self.vendor_id = int(config["controller"]["Vendor_ID"], 16)
            self.mapper_button.config(state='normal')
            self.cfg_button.config(state='normal')
        except Exception as e:
            if 'init_root' in locals():
                init_root.grab_release()  # 确保在发生异常时也释放焦点
            sys.stdout.write(f"\n>>> 初始化配置时出错: {e}")

    def toggle_mapper(self):
        with open("user_config.json", "r") as f:
            config = json.load(f)
        vendor_id = int(config["controller"]["Vendor_ID"], 16)
        if not self.mapper_running:
            try:
                button_states = {
                    "init": self.init_button.cget("state"),
                    "cfg": self.cfg_button.cget("state"),
                    "mapper": self.mapper_button.cget("state"),
                    "button": self.button.cget("state"),
                }
                sys.stdout.write("\n>>> 正在启动手柄映射...")
                self.set_exclusive(True)
                threading.Thread(target=self._wrap_toggle_mapper, args=(vendor_id, button_states)).start()
            except Exception as e:
                sys.stdout.write("\n>>> 启动映射时出错: ")
                handle_exception(e)
        else:
            sys.stdout.write("\n>>> 正在停止手柄映射...")
            self.set_exclusive(False)
            self.mapper.stop()
            self.mapper_running = False
            self.set_exclusive(False, verbose=False)
            sys.stdout.write("\n>>> 手柄映射已停止。")
            self.mapper_button.config(text="启动手柄映射")
            self.button.config(state='disabled')
            self.init_button.config(state='normal')
            if self.running:
                self.toggle()

    def _wrap_toggle_mapper(self, vendor_id, button_states: dict):
        try:
            if vendor_id == 0x054c:
                self.mapper = DualSenseToDS4Mapper()
            elif vendor_id == 0x045e:
                sys.stdout.write("\n>>> 请按下物理手柄上的 A 键进行检测（按 ESC 键取消）...\n")
                physical_id = detect_controller_by_a()
                if not physical_id:
                    sys.stdout.write("\n>>> 手柄映射已取消。")
                    return
                self.mapper = XboxWirelessToX360Mapper(physical_id=int(physical_id))
            status = self.mapper.start()
            if status:
                self.mapper_running = True
                self.mapper_button.config(text="停止手柄映射")
                self.button.config(state='normal')
                self.init_button.config(state='disabled')
                sys.stdout.write("\n>>> 手柄映射已启动。")
            else:
                self.set_exclusive(False)
                self.mapper.stop()
                self.set_exclusive(False)
                sys.stdout.write("\n>>> 手柄映射启动失败，请检查设备。")
        except Exception as e:
            self.init_button.config(state=button_states["init"])
            self.mapper_button.config(state=button_states["mapper"])
            self.cfg_button.config(state=button_states["cfg"])
            self.button.config(state=button_states["button"])
            sys.stdout.write("\n>>> 启动映射时出错: ")
            handle_exception(e)

    def toggle(self):
        if not self.running and self.mapper_running:
            self.running = True
            self.logic_started = False
            sys.stdout.write("\n>>> 正在启动智慧核心...")
            self.button.config(text="关闭智慧核心")
            self.mapper_button.config(state="disabled")
            self.cfg_button.config(state='disabled')
            # 启动线程
            self.thread = threading.Thread(target=self._logic_wrapper, daemon=True)
            self.thread.start()
            # 检测线程是否启动成功
            self.root.after(1000, self._check_logic_started)
        else:
            self.running = False
            sys.stdout.write("\n>>> 正在关闭智慧核心...")
            self.button.config(text="启动智慧核心")
            self.mapper_button.config(state='normal')
            self.cfg_button.config(state='normal')
            self.update_latency_label(self.latency_str)

    def _logic_wrapper(self):
        try:
            self.logic_started = True
            self.run_logic()
        except Exception as e:
            sys.stdout.write("\n>>> 智慧核心启动失败。")
            handle_exception(e)
            self.running = False
            self.root.after(0, self._handle_logic_failure)
        finally:
            self.logic_started = False

    def _handle_logic_failure(self):
        self.button.config(text="启动智慧核心")
        self.mapper_button.config(state='normal')
        self.cfg_button.config(state='normal')

    def _check_logic_started(self):
        if not self.running:
            return
            
        if self.logic_started:
            sys.stdout.write("\n>>> 智慧核心已启动。")
        else:
            if not self.thread.is_alive():
                sys.stdout.write("\n>>> 智慧核心启动失败，请检查配置。")
                self._handle_logic_failure()
            else:
                # 如果线程仍然在运行但未设置标志，继续检查
                self.root.after(100, self._check_logic_started)

    def run_logic(self):
        try:
            def map_range(x: float, a: float, scale: list[float] = [0.2, 0.8]):
                normalized = x / a
                out_min = 127 * scale[0]
                out_max = 127 * scale[1]
                value = out_min + (out_max - out_min) * abs(normalized)
                return value

            with open("user_config.json", "r") as f:
                config = json.load(f)
            hipfire_scale = config["detect_settings"]["hipfire_scale"]
            strong_size = config["detect_settings"]["range"]["inner"]
            weak_size = config["detect_settings"]["range"]["middle"]
            ident_size = config["detect_settings"]["range"]["outer"]
            strong_center = strong_size / 2
            weak_center = weak_size / 2
            ident_center = ident_size / 2
            curve_inner = config["detect_settings"]["curve"]["inner"]
            curve_outer = config["detect_settings"]["curve"]["outer"]

            camera = ScreenGrabber(region=get_screenshot_region_dxcam(ident_size))
            model = APV5Experimental(self.model_path)

            sys.stdout.write(f"\n>>> 智慧核心运行中，当前 EP：{model.provider}")
            last_print_time = time.time()
            
            tracking_delay_start = time.perf_counter()
            while self.running:
                cycle_start = time.perf_counter()
                grab_start = time.perf_counter()
                img = camera.grab_frame()
                if img is None:
                    continue
                grab_end = time.perf_counter()
                grab_latency = (grab_end - grab_start) * 1000

                infer_start = time.perf_counter()
                result, image = model.predict(img)
                infer_end = time.perf_counter()
                infer_latency = (infer_end - infer_start) * 1000

                if (
                    result is not None \
                    and (self.mapper.get_trigger_values()[1] > 128 \
                    or time.perf_counter() - tracking_delay_start < 0.2)
                ):
                    xy_result = result - ident_center
                    distances = np.abs(xy_result[:, 0]) + np.abs(xy_result[:, 1])
                    min_idx = distances.argmin()
                    strength = 1
                    if self.mapper.get_trigger_values()[0] < 128:
                        strength *= hipfire_scale
                    euclidean_distance = np.sqrt(np.sum(xy_result[min_idx]**2))
                    cos_angle = xy_result[min_idx][0] / euclidean_distance
                    sin_angle = xy_result[min_idx][1] / euclidean_distance
                    if euclidean_distance < strong_center:
                        map_euclidean_distance = map_range(euclidean_distance, strong_size, curve_inner) * strength
                    elif euclidean_distance < weak_center:
                        map_euclidean_distance = map_range(euclidean_distance, weak_size, curve_outer) * strength
                    else:
                        map_euclidean_distance = 0
                    rx_offset = map_euclidean_distance * cos_angle
                    ry_offset = map_euclidean_distance * sin_angle
                    self.mapper.add_rx_ry_offset(rx_offset, ry_offset)
                    if self.mapper.get_trigger_values()[1] > 128:
                        tracking_delay_start = time.perf_counter()
                else:
                    self.mapper.rx_offset = 0
                    self.mapper.ry_offset = 0

                cycle_end = time.perf_counter()
                cycle_latency = (cycle_end - cycle_start) * 1000

                now = time.time()
                if now - last_print_time > 1:
                    latency_str = (
                        f"[Latency] full cycle: {cycle_latency:.3f} ms\n"
                        f"[Latency] screen grab: {grab_latency:.3f} ms\n"
                        f"[Latency] inference: {infer_latency:.3f} ms"
                    )
                    self.root.after(0, self.update_latency_label, latency_str)
                    last_print_time = now

            self.mapper.rx_override = None
            self.mapper.ry_override = None
            sys.stdout.write("\n>>> 智慧核心已关闭。")
        except Exception as e:
            sys.stdout.write("\n>>> 智慧核心运行时出错。")
            handle_exception(e)
            self.running = False
            self._handle_logic_failure()
        finally:
            camera.stop()
        
    def set_exclusive(self, state: bool, verbose: bool = True):
        if state:
            msg = ["\n>>> 正在启动手柄独占，请勿退出...", "\n>>> 手柄独占已启动。", "\n>>> 启动手柄独占时出错: "]
        else:
            msg = ["\n>>> 正在停止手柄独占，请勿退出...", "\n>>> 手柄独占已停止。", "\n>>> 停止手柄独占时出错: "]
        button_states = {
            "init": self.init_button.cget("state"),
            "cfg": self.cfg_button.cget("state"),
            "mapper": self.mapper_button.cget("state"),
            "button": self.button.cget("state"),
        }
        try:
            if verbose:
                sys.stdout.write(msg[0])
            self.init_button.config(state='disabled')
            self.mapper_button.config(state='disabled')
            self.cfg_button.config(state='disabled')
            self.button.config(state='disabled')
            with open("user_config.json", "r") as f:
                config = json.load(f)
            instance_id = config["controller"]["Instance_ID"]
            self.hidhide.set_this_to_visible(instance_id, state)
            self.replugger.replug(instance_id)
            if verbose:
                sys.stdout.write(msg[1])
            response = True
        except Exception as e:
            if verbose:
                sys.stdout.write(f"{msg[2]}{e}")
                if str(e) == "CM_Locate_DevNodeW failed: 0x000D":
                    sys.stdout.write("\n>>> 请检查手柄是否插入。若您更换了手柄，请重新初始化。")
                elif str(e) == "CM_Disable_DevNode failed: 0x0017":
                    sys.stdout.write("\n>>> 检测到多个相同的手柄实例。")
                else:
                    handle_exception(e)
            response = False
        finally:
            self.init_button.config(state=button_states["init"])
            self.mapper_button.config(state=button_states["mapper"])
            self.cfg_button.config(state=button_states["cfg"])
            self.button.config(state=button_states["button"])
            return response

    def on_close(self):
        if messagebox.askyesno("退出", "确定退出吗？"):
            self.running = False
            self.set_exclusive(state=False, verbose=True)
            if self.mapper:
                self.mapper.stop()
            self.set_exclusive(state=False, verbose=False)
            self.hidhide.close()
            # messagebox.showinfo("调试", "on_close")
            self.root.destroy()


if __name__ == "__main__":
    root = tk.Tk()
    app = App(root)
    root.mainloop()
