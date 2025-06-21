import time
import threading
from datetime import datetime
from pathlib import Path
from utils.logger import get_logger


class LogCleaner:
    """日志清理器"""
    
    def __init__(self, log_dir="logs", keep_days=7, max_size_mb=100):
        self.log_dir = Path(log_dir)
        self.keep_days = keep_days
        self.max_size_mb = max_size_mb
        self.logger = get_logger("LogCleaner")
        self.cleanup_thread = None
        self.running = False
    
    def start_auto_cleanup(self, interval_hours=24):
        """启动自动清理"""
        if self.cleanup_thread and self.cleanup_thread.is_alive():
            self.logger.warning("自动清理已在运行")
            return
        
        self.running = True
        self.cleanup_thread = threading.Thread(
            target=self._auto_cleanup_loop, 
            args=(interval_hours,), 
            daemon=True
        )
        self.cleanup_thread.start()
        self.logger.info(f"启动自动日志清理，间隔: {interval_hours}小时")
    
    def stop_auto_cleanup(self):
        """停止自动清理"""
        self.running = False
        if self.cleanup_thread:
            self.cleanup_thread.join(timeout=5)
        self.logger.info("停止自动日志清理")
    
    def _auto_cleanup_loop(self, interval_hours):
        """自动清理循环"""
        while self.running:
            try:
                self.cleanup_old_logs()
                self.cleanup_by_size()
                
                # 等待指定时间
                for _ in range(interval_hours * 3600):  # 转换为秒
                    if not self.running:
                        break
                    time.sleep(1)
                    
            except Exception as e:
                self.logger.error(f"自动清理过程中出错: {e}")
                time.sleep(60)  # 出错后等待1分钟再重试
    
    def cleanup_old_logs(self, keep_days=None):
        """清理旧日志文件"""
        if keep_days is None:
            keep_days = self.keep_days
        
        if not self.log_dir.exists():
            return
        
        cutoff_time = datetime.now().timestamp() - (keep_days * 24 * 60 * 60)
        deleted_count = 0
        deleted_size = 0
        
        try:
            for log_file in self.log_dir.glob("*.log"):
                if log_file.stat().st_mtime < cutoff_time:
                    file_size = log_file.stat().st_size
                    log_file.unlink()
                    deleted_count += 1
                    deleted_size += file_size
                    self.logger.debug(f"删除旧日志文件: {log_file.name}")
            
            if deleted_count > 0:
                self.logger.info(f"清理了 {deleted_count} 个旧日志文件，释放空间: {deleted_size/1024/1024:.2f}MB")
                
        except Exception as e:
            self.logger.error(f"清理旧日志文件时出错: {e}")
    
    def cleanup_by_size(self, max_size_mb=None):
        """按大小清理日志文件"""
        if max_size_mb is None:
            max_size_mb = self.max_size_mb
        
        if not self.log_dir.exists():
            return
        
        try:
            # 获取所有日志文件及其大小
            log_files = []
            total_size = 0
            
            for log_file in self.log_dir.glob("*.log"):
                file_size = log_file.stat().st_size
                log_files.append((log_file, file_size, log_file.stat().st_mtime))
                total_size += file_size
            
            # 如果总大小超过限制，删除最旧的文件
            if total_size > max_size_mb * 1024 * 1024:
                # 按修改时间排序（最旧的在前）
                log_files.sort(key=lambda x: x[2])
                
                deleted_count = 0
                deleted_size = 0
                
                for log_file, file_size, _ in log_files:
                    if total_size - deleted_size <= max_size_mb * 1024 * 1024:
                        break
                    
                    log_file.unlink()
                    deleted_count += 1
                    deleted_size += file_size
                    self.logger.debug(f"删除大文件: {log_file.name} ({file_size/1024/1024:.2f}MB)")
                
                if deleted_count > 0:
                    self.logger.info(f"按大小清理了 {deleted_count} 个日志文件，释放空间: {deleted_size/1024/1024:.2f}MB")
                    
        except Exception as e:
            self.logger.error(f"按大小清理日志文件时出错: {e}")
    
    def get_log_stats(self):
        """获取日志统计信息"""
        if not self.log_dir.exists():
            return {
                "total_files": 0,
                "total_size_mb": 0,
                "oldest_file": None,
                "newest_file": None
            }
        
        try:
            log_files = []
            total_size = 0
            
            for log_file in self.log_dir.glob("*.log"):
                file_size = log_file.stat().st_size
                modify_time = log_file.stat().st_mtime
                log_files.append((log_file.name, file_size, modify_time))
                total_size += file_size
            
            if not log_files:
                return {
                    "total_files": 0,
                    "total_size_mb": 0,
                    "oldest_file": None,
                    "newest_file": None
                }
            
            # 按修改时间排序
            log_files.sort(key=lambda x: x[2])
            
            return {
                "total_files": len(log_files),
                "total_size_mb": total_size / 1024 / 1024,
                "oldest_file": {
                    "name": log_files[0][0],
                    "time": datetime.fromtimestamp(log_files[0][2]).strftime("%Y-%m-%d %H:%M:%S")
                },
                "newest_file": {
                    "name": log_files[-1][0],
                    "time": datetime.fromtimestamp(log_files[-1][2]).strftime("%Y-%m-%d %H:%M:%S")
                }
            }
            
        except Exception as e:
            self.logger.error(f"获取日志统计信息时出错: {e}")
            return None
    
    def manual_cleanup(self):
        """手动清理"""
        self.logger.info("开始手动清理日志文件")
        self.cleanup_old_logs()
        self.cleanup_by_size()
        
        # 显示清理后的统计信息
        stats = self.get_log_stats()
        if stats:
            self.logger.info(f"清理完成 - 文件数: {stats['total_files']}, 总大小: {stats['total_size_mb']:.2f}MB")


# 全局日志清理器实例
_log_cleaner_instance = None


def get_log_cleaner():
    """获取全局日志清理器实例"""
    global _log_cleaner_instance
    if _log_cleaner_instance is None:
        _log_cleaner_instance = LogCleaner()
    return _log_cleaner_instance


def start_auto_cleanup(interval_hours=24):
    """启动自动清理"""
    cleaner = get_log_cleaner()
    cleaner.start_auto_cleanup(interval_hours)


def stop_auto_cleanup():
    """停止自动清理"""
    cleaner = get_log_cleaner()
    cleaner.stop_auto_cleanup()


def manual_cleanup():
    """手动清理"""
    cleaner = get_log_cleaner()
    cleaner.manual_cleanup()


def get_log_stats():
    """获取日志统计信息"""
    cleaner = get_log_cleaner()
    return cleaner.get_log_stats()


if __name__ == "__main__":
    # 测试日志清理器
    cleaner = LogCleaner()
    stats = cleaner.get_log_stats()
    print("日志统计信息:", stats)
    
    if stats and stats["total_files"] > 0:
        print("开始清理...")
        cleaner.manual_cleanup()
        
        stats_after = cleaner.get_log_stats()
        print("清理后统计信息:", stats_after) 