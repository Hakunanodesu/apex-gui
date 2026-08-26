using System.Threading;
using Vortice.Direct3D;
using Vortice.Direct3D11;
using Vortice.DXGI;
using Windows.Graphics;
using Windows.Graphics.Capture;
using Windows.Graphics.DirectX;
using Windows.Graphics.DirectX.Direct3D11;
using static Vortice.Direct3D11.D3D11;
using static Vortice.DXGI.DXGI;

internal sealed class WgcDesktopCapturer : IScreenCapturer
{
    private const int MaxDrainFrames = 8;
    private const int FramePoolBufferCount = 2;

    private readonly ID3D11Device _device;
    private readonly ID3D11DeviceContext _context;
    private readonly IDirect3DDevice _winrtDevice;
    private readonly GraphicsCaptureItem _item;
    private readonly AutoResetEvent _frameAvailable = new(false);
    private Direct3D11CaptureFramePool _framePool;
    private GraphicsCaptureSession _session;
    private ID3D11Texture2D _stagingTexture;
    private SizeInt32 _poolSize;
    private byte[] _frameBuffer = Array.Empty<byte>();
    private byte[] _weaponRoiBuffer = Array.Empty<byte>();
    private readonly object _regionLock = new();
    private int _outputWidth;
    private int _outputHeight;
    private int _captureWidth;
    private int _captureHeight;
    private int _captureLeft;
    private int _captureTop;
    private volatile bool _closed;
    private bool _disposed;

    public WgcDesktopCapturer()
    {
        if (!GraphicsCaptureSession.IsSupported())
        {
            throw new InvalidOperationException("WGC is not supported on this system.");
        }

        var monitor = IntPtr.Zero;
        var desktopWidth = 0;
        var desktopHeight = 0;
        CreateDXGIFactory1(out IDXGIFactory1? factory).CheckError();
        if (factory is null)
        {
            throw new InvalidOperationException("Failed to create DXGI factory.");
        }

        using (factory)
        {
            factory.EnumAdapters1(0, out var adapter).CheckError();
            if (adapter is null)
            {
                throw new InvalidOperationException("No DXGI adapter found.");
            }

            using (adapter)
            {
                adapter.EnumOutputs(0, out var output).CheckError();
                if (output is null)
                {
                    throw new InvalidOperationException("No DXGI output found.");
                }

                using (output)
                {
                    var outputDesc = output.Description;
                    monitor = outputDesc.Monitor;
                    desktopWidth = outputDesc.DesktopCoordinates.Right - outputDesc.DesktopCoordinates.Left;
                    desktopHeight = outputDesc.DesktopCoordinates.Bottom - outputDesc.DesktopCoordinates.Top;
                }
            }
        }

        if (monitor == IntPtr.Zero)
        {
            throw new InvalidOperationException("Failed to resolve primary monitor.");
        }

        _device = D3D11CreateDevice(DriverType.Hardware, DeviceCreationFlags.BgraSupport);
        _context = _device.ImmediateContext;
        RuntimePerformance.TrySetGpuThreadPriority(_device, 7);
        _winrtDevice = WgcWinRtInterop.CreateDirect3DDevice(_device);
        _item = WgcWinRtInterop.CreateItemForMonitor(monitor);
        _item.Closed += OnCaptureClosed;

        _outputWidth = _item.Size.Width;
        _outputHeight = _item.Size.Height;
        if (_outputWidth <= 0 || _outputHeight <= 0)
        {
            _outputWidth = desktopWidth;
            _outputHeight = desktopHeight;
        }

        if (_outputWidth <= 0 || _outputHeight <= 0)
        {
            throw new InvalidOperationException("WGC output size is invalid.");
        }

        _poolSize = new SizeInt32 { Width = _outputWidth, Height = _outputHeight };
        _framePool = Direct3D11CaptureFramePool.CreateFreeThreaded(
            _winrtDevice,
            DirectXPixelFormat.B8G8R8A8UIntNormalized,
            FramePoolBufferCount,
            _poolSize);
        _framePool.FrameArrived += OnFrameArrived;
        _stagingTexture = CreateStagingTexture(_outputWidth, _outputHeight);
        SetCaptureRegion(320, 320);

        _session = _framePool.CreateCaptureSession(_item);
        WgcWinRtInterop.TryDisableCursorCapture(_session);
        WgcWinRtInterop.TryDisableBorder(_session);
        _session.StartCapture();
    }

    public void SetCaptureRegion(int width, int height)
    {
        var clampedWidth = Math.Clamp(width, 1, _outputWidth);
        var clampedHeight = Math.Clamp(height, 1, _outputHeight);

        lock (_regionLock)
        {
            _captureWidth = clampedWidth;
            _captureHeight = clampedHeight;
            _captureLeft = (_outputWidth - _captureWidth) / 2;
            _captureTop = (_outputHeight - _captureHeight) / 2;
            var requiredBytes = _captureWidth * _captureHeight * 4;
            if (_frameBuffer.Length != requiredBytes)
            {
                _frameBuffer = new byte[requiredBytes];
            }
        }
    }

    public bool TryWaitForFrame(int timeoutMs)
    {
        if (_disposed || _closed)
        {
            return false;
        }

        return _frameAvailable.WaitOne(Math.Max(0, timeoutMs));
    }

    public bool TryCaptureFrame(
        int timeoutMs,
        bool captureWeaponRoi,
        out byte[] frameData,
        out int width,
        out int height,
        out byte[] weaponRoiData,
        out int weaponRoiWidth,
        out int weaponRoiHeight,
        out string? error)
    {
        _ = timeoutMs;
        frameData = Array.Empty<byte>();
        width = 0;
        height = 0;
        weaponRoiData = Array.Empty<byte>();
        weaponRoiWidth = 0;
        weaponRoiHeight = 0;
        error = null;

        if (_disposed)
        {
            error = "Capture session disposed";
            return false;
        }

        if (_closed)
        {
            error = "WGC capture session closed";
            return false;
        }

        try
        {
            EnsurePoolMatchesItemSize();
            var frame = TakeLatestFrame();
            if (frame is null)
            {
                return false;
            }

            try
            {
                using var texture = WgcWinRtInterop.GetTexture2D(frame.Surface);
                var textureDesc = texture.Description;
                var textureWidth = (int)textureDesc.Width;
                var textureHeight = (int)textureDesc.Height;
                if (textureWidth <= 0 || textureHeight <= 0)
                {
                    return false;
                }

                if (textureWidth != _outputWidth || textureHeight != _outputHeight)
                {
                    _outputWidth = textureWidth;
                    _outputHeight = textureHeight;
                    RecreateStaging(_outputWidth, _outputHeight);
                    lock (_regionLock)
                    {
                        SetCaptureRegionUnlocked(_captureWidth, _captureHeight);
                    }
                }

                _context.CopyResource(_stagingTexture, texture);
            }
            finally
            {
                frame.Dispose();
            }

            _context.Map(_stagingTexture, 0, MapMode.Read, Vortice.Direct3D11.MapFlags.None, out var mapped).CheckError();
            try
            {
                int captureWidth;
                int captureHeight;
                int captureLeft;
                int captureTop;
                byte[] frameBuffer;
                lock (_regionLock)
                {
                    captureWidth = _captureWidth;
                    captureHeight = _captureHeight;
                    captureLeft = _captureLeft;
                    captureTop = _captureTop;
                    frameBuffer = _frameBuffer;
                }

                DesktopCaptureFrameCopy.CopyMappedFrame(
                    mapped.DataPointer,
                    (int)mapped.RowPitch,
                    _outputWidth,
                    _outputHeight,
                    captureLeft,
                    captureTop,
                    captureWidth,
                    captureHeight,
                    frameBuffer,
                    captureWeaponRoi,
                    ref _weaponRoiBuffer,
                    out weaponRoiData,
                    out weaponRoiWidth,
                    out weaponRoiHeight);

                frameData = frameBuffer;
                width = captureWidth;
                height = captureHeight;
            }
            finally
            {
                _context.Unmap(_stagingTexture, 0);
            }

            return true;
        }
        catch (Exception ex)
        {
            error = ex.Message;
            return false;
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _closed = true;
        _item.Closed -= OnCaptureClosed;
        _framePool.FrameArrived -= OnFrameArrived;
        _frameAvailable.Set();
        _session.Dispose();
        _framePool.Dispose();
        _stagingTexture.Dispose();
        _winrtDevice.Dispose();
        _context.Dispose();
        _device.Dispose();
        _disposed = true;
        _frameAvailable.Dispose();
    }

    private void OnFrameArrived(Direct3D11CaptureFramePool sender, object args)
    {
        if (!_disposed && !_closed)
        {
            _frameAvailable.Set();
        }
    }

    private void OnCaptureClosed(GraphicsCaptureItem sender, object args)
    {
        _closed = true;
    }

    private Direct3D11CaptureFrame? TakeLatestFrame()
    {
        Direct3D11CaptureFrame? latest = null;
        for (var i = 0; i < MaxDrainFrames; i++)
        {
            var frame = _framePool.TryGetNextFrame();
            if (frame is null)
            {
                break;
            }

            latest?.Dispose();
            latest = frame;
        }

        return latest;
    }

    private void EnsurePoolMatchesItemSize()
    {
        var size = _item.Size;
        if (size.Width <= 0 || size.Height <= 0)
        {
            return;
        }

        if (size.Width == _poolSize.Width && size.Height == _poolSize.Height)
        {
            return;
        }

        DrainAndDiscardFrames();
        _framePool.Recreate(
            _winrtDevice,
            DirectXPixelFormat.B8G8R8A8UIntNormalized,
            FramePoolBufferCount,
            size);
        _poolSize = size;
        _outputWidth = size.Width;
        _outputHeight = size.Height;
        RecreateStaging(_outputWidth, _outputHeight);
        SetCaptureRegion(_captureWidth, _captureHeight);
    }

    private void DrainAndDiscardFrames()
    {
        for (var i = 0; i < MaxDrainFrames; i++)
        {
            var frame = _framePool.TryGetNextFrame();
            if (frame is null)
            {
                break;
            }

            frame.Dispose();
        }
    }

    private void RecreateStaging(int width, int height)
    {
        _stagingTexture.Dispose();
        _stagingTexture = CreateStagingTexture(width, height);
    }

    private ID3D11Texture2D CreateStagingTexture(int width, int height)
    {
        var textureDesc = new Texture2DDescription
        {
            Width = (uint)width,
            Height = (uint)height,
            MipLevels = 1,
            ArraySize = 1,
            Format = Format.B8G8R8A8_UNorm,
            SampleDescription = new SampleDescription(1, 0),
            Usage = ResourceUsage.Staging,
            BindFlags = BindFlags.None,
            CPUAccessFlags = CpuAccessFlags.Read,
            MiscFlags = ResourceOptionFlags.None
        };
        return _device.CreateTexture2D(textureDesc);
    }

    private void SetCaptureRegionUnlocked(int width, int height)
    {
        var clampedWidth = Math.Clamp(width, 1, _outputWidth);
        var clampedHeight = Math.Clamp(height, 1, _outputHeight);
        _captureWidth = clampedWidth;
        _captureHeight = clampedHeight;
        _captureLeft = (_outputWidth - _captureWidth) / 2;
        _captureTop = (_outputHeight - _captureHeight) / 2;
        var requiredBytes = _captureWidth * _captureHeight * 4;
        if (_frameBuffer.Length != requiredBytes)
        {
            _frameBuffer = new byte[requiredBytes];
        }
    }
}
