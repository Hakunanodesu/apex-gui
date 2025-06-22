import tkinter as tk
from tkinter import scrolledtext
import json
import sys

from utils.tools import enum_hid_devices
from utils.delay_stdout import DelayedStdoutRedirector


class InitApp:
    def __init__(self, root):
        self.root = root
        self.root.title("初始化配置")
        self.force_quit = True

        # 布局配置
        self.root.rowconfigure(0, weight=1)
        self.root.rowconfigure(2, weight=0)
        self.root.columnconfigure(0, weight=1)

        # 文本输出区
        self.text = scrolledtext.ScrolledText(root, height=20, width=80, state='disabled')
        self.text.grid(row=0, column=0, sticky="nsew")
        sys.stdout = DelayedStdoutRedirector(self.text, interval_ms=50)

        # 输入框
        vcmd = (self.root.register(self._validate_number), '%P')
        self.entry = tk.Entry(root, state="normal", validate='key', validatecommand=vcmd)
        self.entry.bind("<Return>", self._on_entry)
        self.entry.grid(row=1, column=0, sticky="ew", padx=5, pady=5, ipady=8)
        self.entry.focus()

        # 初始配置
        try:
            with open("user_config.json", "r") as f:
                self.config = json.load(f)
        except Exception:
            self.config = {
                "controller": {"Name": "", "Vendor_ID": "", "Product_ID": "", "Instance_ID": ""},
                "detect_settings": {
                    "range": {"outer": 320, "middle": 320, "inner": 80},
                    "curve": {"outer": [0.2, 0.2], "inner": [0.02, 1.0]},
                    "hipfire_scale": 0.7
                }
            }

        # 开始手柄检测
        sys.stdout.write(">>> 正在初始化手柄配置...")
        self._poll_controller()
        self.root.protocol("WM_DELETE_WINDOW", self._on_closing)

    def _on_closing(self):
        self.root.destroy()

    def _validate_number(self, proposed_text):
        return proposed_text == "" or proposed_text.isdigit()

    def _poll_controller(self):
        devices = enum_hid_devices()
        # 过滤出 Sony(0x054C) 和 Microsoft(0x045E) 设备
        filtered = [(name, vid, pid, instance_id) for (name, vid, pid, instance_id) in devices if vid in ("0x54c", "0x45e")]
        self.filtered_devices = filtered
        if not filtered:
            sys.stdout.write(">>> 未检测到 DualSense 或 Xbox 无线控制器，请插入手柄后回车重试。")
        else:
            sys.stdout.write(f">>> 检测到 {len(filtered)} 台手柄设备：")
            for i, (name, vid, pid, instance_id) in enumerate(filtered):
                if "{" in instance_id and "}" in instance_id:
                    name = name + "（蓝牙）"
                sys.stdout.write(f"[{i}] {name}")
            sys.stdout.write(">>> 请输入要使用的手柄编号（留空则刷新）：")

    def _on_entry(self, event):
        s = self.entry.get().strip()
        self.entry.delete(0, tk.END)

        # 手柄选择阶段
        if hasattr(self, 'filtered_devices'):
            if s == "":
                self._poll_controller()
            elif s.isdigit() and 0 <= int(s) < len(self.filtered_devices):
                name, vid, pid, instance_id = self.filtered_devices[int(s)]
                self.config["controller"]["Name"] = name
                self.config["controller"]["Vendor_ID"] = vid
                self.config["controller"]["Product_ID"] = pid
                self.config["controller"]["Instance_ID"] = instance_id
                sys.stdout.write(">>> 手柄配置完成，窗口将自动关闭。")
                with open("user_config.json", "w") as f:
                    json.dump(self.config, f, indent=4)
                self.force_quit = False
                self.root.after(500, self.root.destroy)
            else:
                sys.stdout.write(">>> 输入无效，请重新输入手柄编号：")


if __name__ == "__main__":
    root = tk.Tk()
    InitApp(root)
    root.mainloop()
