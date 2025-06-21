import tkinter as tk
import json

from utils.tools import get_scaling_factor, median_of_three


class CFGApp:
    def __init__(self, root):
        self.root = root
        self.root.title("修改识别配置")
        self.force_quit = True
        
        self.scale = get_scaling_factor()
        self.root.geometry(f"{int(400 * self.scale)}x{int(550 * self.scale)}")

        # 注册验证函数
        vcmd = (self.root.register(self._validate_integer), '%P')
        vcmd_float = (self.root.register(self._validate_float), '%P')

        with open('user_config.json', 'r') as f:
            self.config = json.load(f)

        # 获取屏幕尺寸与长宽比
        self.screen_width = root.winfo_screenwidth()
        self.screen_height = root.winfo_screenheight()
        self.aspect = self.screen_height / self.screen_width

        # Grid 布局：仅中部 Canvas 可伸缩
        root.grid_rowconfigure(0, weight=0)
        root.grid_rowconfigure(1, weight=0)
        root.grid_rowconfigure(2, weight=1)
        root.grid_rowconfigure(3, weight=0)
        root.grid_rowconfigure(4, weight=0)
        root.grid_columnconfigure(0, weight=1)

        # 顶部灰色范围 Canvas，仅横向填充
        self.range_canvas = tk.Canvas(
            root, bg='lightgray', highlightthickness=0
        )
        self.range_canvas.grid(row=0, column=0, sticky='we', padx=20, pady=(20, 10))
        self.range_canvas.bind('<Configure>', lambda e: self.draw_ranges())

        # 范围输入框
        self.range_frame = tk.Frame(root)
        self.range_frame.grid(row=1, column=0)
        self.vars = {}
        defaults_range = {
            'ident':  self.config["detect_settings"]["range"]["outer"], 
            'weak': self.config["detect_settings"]["range"]["middle"], 
            'strong': self.config["detect_settings"]["range"]["inner"]
        }
        range_labels = ["识别（px", "弱吸附（px", "强吸附（px"]
        for label_text, key in zip(range_labels, defaults_range.keys()):
            tk.Label(self.range_frame, text=label_text).pack(side='left', padx=5)
            var = tk.StringVar(value=str(defaults_range[key]))
            entry = tk.Entry(self.range_frame, textvariable=var, width=5, validate='key', validatecommand=vcmd)
            entry.pack(side='left', padx=5)
            entry.bind('<Return>', lambda e: self.update_ranges())
            self.vars[key] = var

        # 中部坐标系 Canvas，可横纵填充
        self.coord_canvas = tk.Canvas(
            root, bg='white', highlightthickness=1, highlightbackground='black'
        )
        self.coord_canvas.grid(row=2, column=0, sticky='nsew', padx=20, pady=(10, 10))
        self.coord_canvas.bind('<Configure>', lambda e: self.draw_coord())

        # 底部坐标点输入
        self.coord_frame = tk.Frame(root)
        self.coord_frame.grid(row=3, column=0, pady=(0, 20))
        self.y_vars = {}
        defaults_coord = {
            'inner_close': self.config["detect_settings"]["curve"]["inner"][0],
            'inner_far': self.config["detect_settings"]["curve"]["inner"][1],
            'outer_close': self.config["detect_settings"]["curve"]["outer"][0],
            'outer_far': self.config["detect_settings"]["curve"]["outer"][1]
        }
        
        # 创建2x2网格布局
        for i in range(2):
            self.coord_frame.grid_columnconfigure(i, weight=1)
            self.coord_frame.grid_rowconfigure(i, weight=1)
            
        # 内圈输入框
        tk.Label(self.coord_frame, text="内圈 近（%").grid(row=0, column=0, padx=10, pady=5)
        var_inner_close = tk.StringVar(value=str(defaults_coord['inner_close']))
        entry_inner_close = tk.Entry(self.coord_frame, textvariable=var_inner_close, width=6, validate='key', validatecommand=vcmd_float)
        entry_inner_close.grid(row=0, column=1, padx=5, pady=5)
        entry_inner_close.bind('<Return>', lambda e: self.draw_coord())
        self.y_vars['inner_close'] = var_inner_close
        
        tk.Label(self.coord_frame, text="内圈 远（%").grid(row=1, column=0, padx=10, pady=5)
        var_inner_far = tk.StringVar(value=str(defaults_coord['inner_far']))
        entry_inner_far = tk.Entry(self.coord_frame, textvariable=var_inner_far, width=6, validate='key', validatecommand=vcmd_float)
        entry_inner_far.grid(row=1, column=1, padx=5, pady=5)
        entry_inner_far.bind('<Return>', lambda e: self.draw_coord())
        self.y_vars['inner_far'] = var_inner_far
        
        # 外圈输入框
        tk.Label(self.coord_frame, text="外圈 近（%").grid(row=0, column=2, padx=10, pady=5)
        var_outer_close = tk.StringVar(value=str(defaults_coord['outer_close']))
        entry_outer_close = tk.Entry(self.coord_frame, textvariable=var_outer_close, width=6, validate='key', validatecommand=vcmd_float)
        entry_outer_close.grid(row=0, column=3, padx=5, pady=5)
        entry_outer_close.bind('<Return>', lambda e: self.draw_coord())
        entry_outer_close.bind('<Return>', self._on_outer_enter)
        self.y_vars['outer_close'] = var_outer_close
        
        tk.Label(self.coord_frame, text="外圈 远（%").grid(row=1, column=2, padx=10, pady=5)
        var_outer_far = tk.StringVar(value=str(defaults_coord['outer_far']))
        entry_outer_far = tk.Entry(self.coord_frame, textvariable=var_outer_far, width=6, validate='key', validatecommand=vcmd_float)
        entry_outer_far.grid(row=1, column=3, padx=5, pady=5)
        entry_outer_far.bind('<Return>', lambda e: self.draw_coord())
        entry_outer_far.bind('<Return>', self._on_outer_enter)
        self.y_vars['outer_far'] = var_outer_far
        
        # 底部坐标点输入之后，添加"完成"按钮
        self.bottom_frame = tk.Frame(root)
        self.bottom_frame.grid(row=4, column=0, pady=(0, 20))
        
        # 添加腰射系数输入框
        tk.Label(self.bottom_frame, text="腰射系数（%").pack(side='left', padx=5)
        self.hipfire_var = tk.StringVar(value=str(self.config["detect_settings"]["hipfire_scale"]))  # 默认值0.5
        self.hipfire_entry = tk.Entry(self.bottom_frame, textvariable=self.hipfire_var, width=4, validate='key', validatecommand=vcmd_float)
        self.hipfire_entry.pack(side='left', padx=10)

        # 修复 repeater 复选框 - 保存变量为实例变量
        self.repeater_var = tk.IntVar(value=self.config["detect_settings"]["repeater"])
        repeater = tk.Checkbutton(
            self.bottom_frame,
            text="连点",
            variable=self.repeater_var,
            onvalue=1,     # 选中时变量的值
            offvalue=0     # 取消选中时变量的值
        )
        repeater.pack(side='left', padx=10)
        
        # 完成按钮
        self.done_button = tk.Button(self.bottom_frame, text="完成", command=self.on_done, width=10)
        self.done_button.pack(side='left', padx=10)

        # 初始化并绘制
        self.valid_ranges = {}
        self.update_ranges()

    def _on_outer_enter(self, event=None):
        try:
            val = float(self.y_vars['outer_close'].get())
            inner = float(self.y_vars['inner_close'].get())
        except ValueError:
            # 非法输入就重置为内圈值
            self.y_vars['outer_close'].set(self.y_vars['inner_close'].get())
        else:
            # 范围 [inner, 1]
            if val < inner:
                val = inner
            elif val > 1.0:
                val = 1.0
            self.y_vars['outer_close'].set(str(val))
        finally:
            self.draw_coord()

    def _validate_integer(self, P):
        if P == "":  # 允许空字符串
            return True
        try:
            value = int(P)
            return value >= 0
        except ValueError:
            return False

    def _validate_float(self, P):
        if P == "":  # 允许空字符串
            return True
        try:
            value = float(P)
            return 0 <= value <= 1  # 限制在0-1范围内
        except ValueError:
            return False

    def update_ranges(self):
        try:
            ident = int(self.vars['ident'].get())
            weak = int(self.vars['weak'].get())
            strong = int(self.vars['strong'].get())
        except ValueError:
            return
        weak = min(weak, ident)
        strong = min(strong, weak)
        self.vars['ident'].set(str(ident))
        self.vars['weak'].set(str(weak))
        self.vars['strong'].set(str(strong))
        self.valid_ranges = {'ident': ident, 'weak': weak, 'strong': strong}
        self.draw_ranges()
        self.draw_coord()

    def draw_ranges(self):
        c = self.range_canvas
        c.delete('all')
        w = c.winfo_width()
        if w <= 0:
            return
        # 保持与屏幕长宽比
        h = int(w * self.aspect)
        c.config(height=h)
        # 缩放比例
        scale = w / self.screen_width
        for key, color in [('ident', 'blue'), ('weak', 'green'), ('strong', 'red')]:
            val = self.valid_ranges.get(key, 0)
            half = val * scale / 2
            cx, cy = w/2, h/2
            c.create_rectangle(cx-half, cy-half, cx+half, cy+half,
                               outline=color, width=2)

    def draw_coord(self):
        c = self.coord_canvas
        c.delete('all')
        w = c.winfo_width()
        h = c.winfo_height()
        if w <= 0 or h <= 0 or not self.valid_ranges:
            return
        ml, mt, mr, mb = 40 * self.scale, 20 * self.scale, 30 * self.scale, 30 * self.scale
        x0, y0 = ml, mt
        x1, y1 = w-mr, h-mb
        c.create_line(x0, y1, x1, y1, width=2)
        c.create_line(x0, y1, x0, y0, width=2)
        c.create_line(x1, y1, x1, y0, width=2)
        c.create_line(x1, y0, x0, y0, width=2)
        weak = self.valid_ranges['weak']
        strong = self.valid_ranges['strong']
        total_w = x1 - x0
        xs = [x0, x0+strong/weak*total_w, x1]
        for xpos, label in zip(xs, ['0', str(strong), str(weak)]):
            c.create_line(xpos, y1, xpos, y1-5)
            c.create_text(xpos, y1+10, text=label, anchor='n')
        for yv, label in [(0, '1.0'), (1, '0.0')]:
            ypos = y0 + yv*(y1-y0)
            c.create_line(x0, ypos, x0+5, ypos)
            c.create_text(x0-10, ypos, text=label, anchor='e')
        pts = []
        for key in ['inner_close', 'inner_far', 'outer_close', 'outer_far']:
            try: 
                yv = float(self.y_vars[key].get())
            except Exception: 
                yv = 0.0
            yv = median_of_three(yv, 1.0, 0.0)
            ypos = y1 - (yv)*(y1-y0)
            x = xs[0] if key == 'inner_close' else (xs[1] if key in ['inner_far', 'outer_close'] else xs[2])
            pts.append((x, ypos))
        (x0_, y0_), (x1_, y1_), (x2_, y2_), (x3_, y3_) = pts
        c.create_line(x0_, y0_, x1_, y1_, fill='red', width=2)
        c.create_line(x2_, y2_, x3_, y3_, fill='green', width=2)
        c.create_line(x1_, y1, x1_, y0, width=2, dash=(4, 2, 1, 2))

    def on_done(self):
        # 更新范围值
        self.update_ranges()
        # 更新坐标值
        self.draw_coord()
        # 将 StringVar 对象转换为浮点数
        y_values = {key: float(var.get()) for key, var in self.y_vars.items()}
        self.config["detect_settings"]["curve"]["inner"] = [y_values['inner_close'], y_values['inner_far']]
        self.config["detect_settings"]["curve"]["outer"] = [y_values['outer_close'], y_values['outer_far']]
        self.config["detect_settings"]["range"]["outer"] = self.valid_ranges['ident']
        self.config["detect_settings"]["range"]["middle"] = self.valid_ranges['weak']
        self.config["detect_settings"]["range"]["inner"] = self.valid_ranges['strong']
        self.config["detect_settings"]["hipfire_scale"] = float(self.hipfire_var.get())
        self.config["detect_settings"]["repeater"] = self.repeater_var.get()
        with open('user_config.json', 'w') as f:
            json.dump(self.config, f, indent=4)
        self.force_quit = False
        self.root.destroy()


if __name__ == '__main__':
    root = tk.Tk() 
    CFGApp(root)
    root.mainloop()
