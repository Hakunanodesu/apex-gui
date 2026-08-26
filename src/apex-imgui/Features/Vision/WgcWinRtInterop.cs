using System.Runtime.InteropServices;
using Vortice.Direct3D11;
using Vortice.DXGI;
using Windows.Graphics.Capture;
using Windows.Graphics.DirectX.Direct3D11;
using WinRT;

internal static class WgcWinRtInterop
{
    private static readonly Guid GraphicsCaptureItemIid = new("79C3F95B-31F7-4EC2-A464-632EF5D30760");
    private static readonly Guid Id3d11Texture2DIid = new("6F15AAF2-D208-4E89-9AB4-489535D34F9C");
    private static readonly Guid GraphicsCaptureSession2Iid = new("2C39AE40-7D2E-5044-804E-8B6799D4CF9E");
    private static readonly Guid GraphicsCaptureSession3Iid = new("F2CDD966-22AE-5EA1-9596-3A289344C3BE");

    public static IDirect3DDevice CreateDirect3DDevice(ID3D11Device device)
    {
        using var dxgiDevice = device.QueryInterface<IDXGIDevice>();
        var hr = CreateDirect3D11DeviceFromDXGIDevice(dxgiDevice.NativePointer, out var inspectable);
        Marshal.ThrowExceptionForHR(hr);
        try
        {
            return MarshalInspectable<IDirect3DDevice>.FromAbi(inspectable);
        }
        finally
        {
            Marshal.Release(inspectable);
        }
    }

    public static GraphicsCaptureItem CreateItemForMonitor(IntPtr monitor)
    {
        var interop = ActivationFactory.Get("Windows.Graphics.Capture.GraphicsCaptureItem")
            .AsInterface<IGraphicsCaptureItemInterop>();
        var iid = GraphicsCaptureItemIid;
        var hr = interop.CreateForMonitor(monitor, ref iid, out var itemPtr);
        Marshal.ThrowExceptionForHR(hr);
        try
        {
            return MarshalInspectable<GraphicsCaptureItem>.FromAbi(itemPtr);
        }
        finally
        {
            Marshal.Release(itemPtr);
        }
    }

    public static ID3D11Texture2D GetTexture2D(IDirect3DSurface surface)
    {
        var access = surface.As<IDirect3DDxgiInterfaceAccess>();
        var iid = Id3d11Texture2DIid;
        var hr = access.GetInterface(ref iid, out var texturePtr);
        Marshal.ThrowExceptionForHR(hr);
        return new ID3D11Texture2D(texturePtr);
    }

    public static void TryDisableCursorCapture(GraphicsCaptureSession session)
    {
        TrySetInspectableBoolean(session, GraphicsCaptureSession2Iid, value: false);
    }

    public static void TryDisableBorder(GraphicsCaptureSession session)
    {
        TrySetInspectableBoolean(session, GraphicsCaptureSession3Iid, value: false);
    }

    private static void TrySetInspectableBoolean(object winrtObject, Guid interfaceIid, bool value)
    {
        IntPtr unknown;
        try
        {
            unknown = Marshal.GetIUnknownForObject(winrtObject);
        }
        catch
        {
            if (winrtObject is not IWinRTObject obj)
            {
                return;
            }

            unknown = obj.NativeObject.ThisPtr;
            Marshal.AddRef(unknown);
        }

        try
        {
            var iid = interfaceIid;
            var hr = Marshal.QueryInterface(unknown, in iid, out var iface);
            if (hr != 0 || iface == IntPtr.Zero)
            {
                return;
            }

            try
            {
                var vtable = Marshal.ReadIntPtr(iface);
                var putPtr = Marshal.ReadIntPtr(vtable, 7 * IntPtr.Size);
                var put = Marshal.GetDelegateForFunctionPointer<PutBoolean>(putPtr);
                _ = put(iface, value ? (byte)1 : (byte)0);
            }
            finally
            {
                Marshal.Release(iface);
            }
        }
        finally
        {
            Marshal.Release(unknown);
        }
    }

    [DllImport("d3d11.dll", ExactSpelling = true)]
    private static extern int CreateDirect3D11DeviceFromDXGIDevice(IntPtr dxgiDevice, out IntPtr graphicsDevice);

    [ComImport]
    [Guid("3628E81B-3CAC-4C60-B7F4-23CE0E0C3356")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IGraphicsCaptureItemInterop
    {
        [PreserveSig]
        int CreateForWindow(IntPtr window, ref Guid iid, out IntPtr result);

        [PreserveSig]
        int CreateForMonitor(IntPtr monitor, ref Guid iid, out IntPtr result);
    }

    [ComImport]
    [Guid("A9B3D012-3DF2-4EE3-B8D1-8695F457D3C1")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IDirect3DDxgiInterfaceAccess
    {
        [PreserveSig]
        int GetInterface(ref Guid iid, out IntPtr p);
    }

    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    private delegate int PutBoolean(IntPtr thisPtr, byte value);
}
