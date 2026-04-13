using System.Diagnostics;
using Vortice.Direct3D11;

internal static class RuntimePerformance
{
    private static string _dxgiGpuPriorityStatus = "未初始化";

    public static string DxgiGpuPriorityStatus => _dxgiGpuPriorityStatus;

    public static void ConfigureProcessPriority()
    {
        try
        {
            Process.GetCurrentProcess().PriorityClass = ProcessPriorityClass.High;
        }
        catch
        {
            // Ignore when the OS policy/user permissions do not allow elevating priority.
        }
    }

    public static string GetProcessPriorityText()
    {
        try
        {
            return Process.GetCurrentProcess().PriorityClass.ToString();
        }
        catch (Exception ex)
        {
            return $"读取失败: {ex.GetType().Name}";
        }
    }

    public static void TrySetGpuThreadPriority(ID3D11Device device, int priority)
    {
        try
        {
            using var dxgiDevice = device.QueryInterface<Vortice.DXGI.IDXGIDevice>();
            dxgiDevice.SetGPUThreadPriority(priority).CheckError();
            dxgiDevice.GetGPUThreadPriority(out var actual).CheckError();
            _dxgiGpuPriorityStatus = $"已设置 ({actual})";
        }
        catch (Exception ex)
        {
            _dxgiGpuPriorityStatus = $"未生效: {ex.GetType().Name}";
        }
    }
}
