import tkinter as tk
from tkinter import ttk, filedialog, messagebox
from datetime import datetime
from pathlib import Path


class LogViewer:
    """日志查看器"""
    
    def __init__(self, root):
        self.root = root
        self.root.title("日志查看器")
        self.root.geometry("1000x700")
        
        self.log_dir = Path("logs")
        self.current_log_file = None
        
        self.setup_ui()
        self.load_log_files()
    
    def setup_ui(self):
        # 主框架
        main_frame = ttk.Frame(self.root)
        main_frame.pack(fill='both', expand=True, padx=10, pady=10)
        
        # 顶部控制区域
        control_frame = ttk.Frame(main_frame)
        control_frame.pack(fill='x', pady=(0, 10))
        
        # 日志文件选择
        ttk.Label(control_frame, text="日志文件:").pack(side='left')
        self.log_file_var = tk.StringVar()
        self.log_combo = ttk.Combobox(control_frame, textvariable=self.log_file_var, width=50)
        self.log_combo.pack(side='left', padx=(5, 10))
        self.log_combo.bind('<<ComboboxSelected>>', self.on_log_file_selected)
        
        # 刷新按钮
        ttk.Button(control_frame, text="刷新", command=self.load_log_files).pack(side='left', padx=(0, 10))
        
        # 过滤控制
        ttk.Label(control_frame, text="过滤:").pack(side='left')
        self.filter_var = tk.StringVar()
        self.filter_entry = ttk.Entry(control_frame, textvariable=self.filter_var, width=20)
        self.filter_entry.pack(side='left', padx=(5, 5))
        self.filter_entry.bind('<KeyRelease>', self.apply_filter)
        
        # 级别过滤
        self.level_var = tk.StringVar(value="ALL")
        level_combo = ttk.Combobox(control_frame, textvariable=self.level_var, 
                                  values=["ALL", "DEBUG", "INFO", "WARNING", "ERROR", "CRITICAL"], 
                                  width=10)
        level_combo.pack(side='left', padx=(0, 10))
        level_combo.bind('<<ComboboxSelected>>', self.apply_filter)
        
        # 清除过滤按钮
        ttk.Button(control_frame, text="清除过滤", command=self.clear_filter).pack(side='left')
        
        # 日志显示区域
        log_frame = ttk.Frame(main_frame)
        log_frame.pack(fill='both', expand=True)
        
        # 创建文本显示区域和滚动条
        self.text_widget = tk.Text(log_frame, wrap='word', font=('Consolas', 9))
        scrollbar_y = ttk.Scrollbar(log_frame, orient='vertical', command=self.text_widget.yview)
        scrollbar_x = ttk.Scrollbar(log_frame, orient='horizontal', command=self.text_widget.xview)
        self.text_widget.configure(yscrollcommand=scrollbar_y.set, xscrollcommand=scrollbar_x.set)
        
        # 布局
        self.text_widget.grid(row=0, column=0, sticky='nsew')
        scrollbar_y.grid(row=0, column=1, sticky='ns')
        scrollbar_x.grid(row=1, column=0, sticky='ew')
        log_frame.grid_rowconfigure(0, weight=1)
        log_frame.grid_columnconfigure(0, weight=1)
        
        # 底部状态栏
        self.status_var = tk.StringVar()
        status_bar = ttk.Label(main_frame, textvariable=self.status_var, relief='sunken')
        status_bar.pack(fill='x', pady=(10, 0))
        
        # 右键菜单
        self.context_menu = tk.Menu(self.root, tearoff=0)
        self.context_menu.add_command(label="复制", command=self.copy_selection)
        self.context_menu.add_command(label="全选", command=self.select_all)
        self.context_menu.add_separator()
        self.context_menu.add_command(label="导出选中内容", command=self.export_selection)
        
        self.text_widget.bind("<Button-3>", self.show_context_menu)
    
    def load_log_files(self):
        """加载日志文件列表"""
        if not self.log_dir.exists():
            self.log_combo['values'] = []
            return
        
        log_files = []
        for log_file in self.log_dir.glob("*.log"):
            # 显示文件名和创建时间
            stat = log_file.stat()
            create_time = datetime.fromtimestamp(stat.st_ctime).strftime("%Y-%m-%d %H:%M:%S")
            display_name = f"{log_file.name} ({create_time})"
            log_files.append((display_name, log_file))
        
        # 按时间倒序排列
        log_files.sort(key=lambda x: x[1].stat().st_ctime, reverse=True)
        
        self.log_combo['values'] = [x[0] for x in log_files]
        if log_files:
            self.log_combo.set(log_files[0][0])
            self.current_log_file = log_files[0][1]
            self.load_log_content()
    
    def on_log_file_selected(self, event):
        """日志文件选择事件"""
        selection = self.log_combo.get()
        if selection:
            # 从显示名称中提取文件名
            log_name = selection.split(" (")[0]
            self.current_log_file = self.log_dir / log_name
            self.load_log_content()
    
    def load_log_content(self):
        """加载日志内容"""
        if not self.current_log_file or not self.current_log_file.exists():
            return
        
        try:
            with open(self.current_log_file, 'r', encoding='utf-8') as f:
                content = f.read()
            
            self.text_widget.delete(1.0, tk.END)
            self.text_widget.insert(1.0, content)
            
            # 设置语法高亮
            self.highlight_log_levels()
            
            # 更新状态栏
            lines = len(content.splitlines())
            size = self.current_log_file.stat().st_size
            self.status_var.set(f"文件: {self.current_log_file.name} | 行数: {lines} | 大小: {size/1024:.1f}KB")
            
        except Exception as e:
            messagebox.showerror("错误", f"读取日志文件失败: {e}")
    
    def highlight_log_levels(self):
        """高亮显示不同级别的日志"""
        # 清除现有标签
        self.text_widget.tag_remove("DEBUG", "1.0", tk.END)
        self.text_widget.tag_remove("INFO", "1.0", tk.END)
        self.text_widget.tag_remove("WARNING", "1.0", tk.END)
        self.text_widget.tag_remove("ERROR", "1.0", tk.END)
        self.text_widget.tag_remove("CRITICAL", "1.0", tk.END)
        
        # 设置标签颜色
        self.text_widget.tag_config("DEBUG", foreground="gray")
        self.text_widget.tag_config("INFO", foreground="black")
        self.text_widget.tag_config("WARNING", foreground="orange")
        self.text_widget.tag_config("ERROR", foreground="red")
        self.text_widget.tag_config("CRITICAL", foreground="red", background="yellow")
        
        # 应用高亮
        content = self.text_widget.get(1.0, tk.END)
        lines = content.split('\n')
        
        for i, line in enumerate(lines, 1):
            if '[DEBUG]' in line:
                self.text_widget.tag_add("DEBUG", f"{i}.0", f"{i}.end")
            elif '[INFO]' in line:
                self.text_widget.tag_add("INFO", f"{i}.0", f"{i}.end")
            elif '[WARNING]' in line:
                self.text_widget.tag_add("WARNING", f"{i}.0", f"{i}.end")
            elif '[ERROR]' in line:
                self.text_widget.tag_add("ERROR", f"{i}.0", f"{i}.end")
            elif '[CRITICAL]' in line:
                self.text_widget.tag_add("CRITICAL", f"{i}.0", f"{i}.end")
    
    def apply_filter(self, event=None):
        """应用过滤条件"""
        filter_text = self.filter_var.get().lower()
        level_filter = self.level_var.get()
        
        # 重新加载内容
        if self.current_log_file and self.current_log_file.exists():
            with open(self.current_log_file, 'r', encoding='utf-8') as f:
                lines = f.readlines()
            
            filtered_lines = []
            for line in lines:
                # 级别过滤
                if level_filter != "ALL":
                    if f'[{level_filter}]' not in line:
                        continue
                
                # 文本过滤
                if filter_text and filter_text not in line.lower():
                    continue
                
                filtered_lines.append(line)
            
            self.text_widget.delete(1.0, tk.END)
            self.text_widget.insert(1.0, ''.join(filtered_lines))
            self.highlight_log_levels()
            
            # 更新状态栏
            self.status_var.set(f"显示 {len(filtered_lines)} 行 (过滤: {filter_text or '无'} | 级别: {level_filter})")
    
    def clear_filter(self):
        """清除过滤条件"""
        self.filter_var.set("")
        self.level_var.set("ALL")
        self.load_log_content()
    
    def show_context_menu(self, event):
        """显示右键菜单"""
        self.context_menu.post(event.x_root, event.y_root)
    
    def copy_selection(self):
        """复制选中内容"""
        try:
            selected_text = self.text_widget.get(tk.SEL_FIRST, tk.SEL_LAST)
            self.root.clipboard_clear()
            self.root.clipboard_append(selected_text)
        except tk.TclError:
            pass  # 没有选中内容
    
    def select_all(self):
        """全选"""
        self.text_widget.tag_add(tk.SEL, "1.0", tk.END)
        self.text_widget.mark_set(tk.INSERT, "1.0")
        self.text_widget.see(tk.INSERT)
    
    def export_selection(self):
        """导出选中内容"""
        try:
            selected_text = self.text_widget.get(tk.SEL_FIRST, tk.SEL_LAST)
        except tk.TclError:
            messagebox.showwarning("警告", "请先选择要导出的内容")
            return
        
        file_path = filedialog.asksaveasfilename(
            defaultextension=".txt",
            filetypes=[("文本文件", "*.txt"), ("所有文件", "*.*")]
        )
        
        if file_path:
            try:
                with open(file_path, 'w', encoding='utf-8') as f:
                    f.write(selected_text)
                messagebox.showinfo("成功", f"内容已导出到: {file_path}")
            except Exception as e:
                messagebox.showerror("错误", f"导出失败: {e}")


def open_log_viewer():
    """打开日志查看器"""
    root = tk.Toplevel()
    app = LogViewer(root)
    root.grab_set()  # 模态窗口
    return app


if __name__ == "__main__":
    root = tk.Tk()
    app = LogViewer(root)
    root.mainloop() 