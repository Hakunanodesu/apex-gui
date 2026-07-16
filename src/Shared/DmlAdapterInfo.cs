using Vortice.DXGI;
using static Vortice.DXGI.DXGI;

internal static class DmlAdapterInfo
{
    public const int DeviceId = 0;

    private static string _adapterDescription = "未知";

    public static string AdapterDescription => _adapterDescription;

    public static void Initialize()
    {
        try
        {
            CreateDXGIFactory1(out IDXGIFactory1? factory).CheckError();
            if (factory is null)
            {
                _adapterDescription = "未知";
                return;
            }

            using (factory)
            {
                factory.EnumAdapters1(DeviceId, out var adapter).CheckError();
                if (adapter is null)
                {
                    _adapterDescription = "未知";
                    return;
                }

                using (adapter)
                {
                    var description = adapter.Description1.Description;
                    _adapterDescription = string.IsNullOrWhiteSpace(description) ? "未知" : description.Trim();
                }
            }
        }
        catch
        {
            _adapterDescription = "未知";
        }
    }
}
