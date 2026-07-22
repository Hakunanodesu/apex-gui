using Vortice.DXGI;
using static Vortice.DXGI.DXGI;

internal readonly record struct DmlAdapterEntry(int DeviceId, string Description);

internal static class DmlAdapterInfo
{
    private static readonly List<DmlAdapterEntry> AdaptersList = new();
    private static int _selectedIndex;

    public static IReadOnlyList<DmlAdapterEntry> Adapters => AdaptersList;

    public static int SelectedIndex => _selectedIndex;

    public static int SelectedDeviceId =>
        AdaptersList.Count == 0 ? 0 : AdaptersList[_selectedIndex].DeviceId;

    public static string SelectedDescription =>
        AdaptersList.Count == 0 ? "未知" : AdaptersList[_selectedIndex].Description;

    public static void Initialize()
    {
        AdaptersList.Clear();
        _selectedIndex = 0;

        try
        {
            CreateDXGIFactory1(out IDXGIFactory1? factory).CheckError();
            if (factory is null)
            {
                return;
            }

            using (factory)
            {
                for (var deviceId = 0; ; deviceId++)
                {
                    var result = factory.EnumAdapters1((uint)deviceId, out var adapter);
                    if (result.Failure || adapter is null)
                    {
                        break;
                    }

                    using (adapter)
                    {
                        var desc = adapter.Description1;
                        if ((desc.Flags & AdapterFlags.Software) != 0)
                        {
                            continue;
                        }

                        var description = desc.Description;
                        if (string.IsNullOrWhiteSpace(description))
                        {
                            continue;
                        }

                        AdaptersList.Add(new DmlAdapterEntry(deviceId, description.Trim()));
                    }
                }
            }
        }
        catch
        {
            AdaptersList.Clear();
        }

        _selectedIndex = 0;
    }

    public static bool TrySelectByDescription(string? description)
    {
        if (AdaptersList.Count == 0)
        {
            _selectedIndex = 0;
            return false;
        }

        if (!string.IsNullOrWhiteSpace(description))
        {
            for (var i = 0; i < AdaptersList.Count; i++)
            {
                if (string.Equals(AdaptersList[i].Description, description.Trim(), StringComparison.OrdinalIgnoreCase))
                {
                    _selectedIndex = i;
                    return true;
                }
            }
        }

        _selectedIndex = 0;
        return false;
    }

    public static bool TrySelectIndex(int index)
    {
        if (index < 0 || index >= AdaptersList.Count)
        {
            return false;
        }

        _selectedIndex = index;
        return true;
    }
}
