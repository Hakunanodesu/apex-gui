using Vortice.Direct3D;
using Vortice.Direct3D11;
using Vortice.DXGI;
using static Vortice.Direct3D11.D3D11;
using static Vortice.DXGI.DXGI;

internal sealed class DxgiDesktopDuplicator : IDisposable
{
    private readonly ID3D11Device _device;
    private readonly ID3D11DeviceContext _context;
    private readonly IDXGIOutputDuplication _duplication;
    private readonly ID3D11Texture2D _stagingTexture;
    private byte[] _frameBuffer = Array.Empty<byte>();
    private readonly object _regionLock = new();
    private readonly int _outputWidth;
    private readonly int _outputHeight;
    private int _captureWidth;
    private int _captureHeight;
    private int _captureLeft;
    private int _captureTop;
    private bool _disposed;

    public DxgiDesktopDuplicator()
    {
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
                using (var output1 = output.QueryInterface<IDXGIOutput1>())
                {
                    _device = D3D11CreateDevice(DriverType.Hardware, DeviceCreationFlags.BgraSupport);
                    _context = _device.ImmediateContext;
                    RuntimePerformance.TrySetGpuThreadPriority(_device, 7);

                    var outputDesc = output.Description;
                    var outputWidth = outputDesc.DesktopCoordinates.Right - outputDesc.DesktopCoordinates.Left;
                    var outputHeight = outputDesc.DesktopCoordinates.Bottom - outputDesc.DesktopCoordinates.Top;
                    if (outputWidth <= 0 || outputHeight <= 0)
                    {
                        throw new InvalidOperationException("DXGI output size is invalid.");
                    }

                    _outputWidth = outputWidth;
                    _outputHeight = outputHeight;
                    SetCaptureRegion(320, 320);

                    _duplication = output1.DuplicateOutput(_device);

                    var textureDesc = new Texture2DDescription
                    {
                        Width = (uint)outputWidth,
                        Height = (uint)outputHeight,
                        MipLevels = 1,
                        ArraySize = 1,
                        Format = Format.B8G8R8A8_UNorm,
                        SampleDescription = new SampleDescription(1, 0),
                        Usage = ResourceUsage.Staging,
                        BindFlags = BindFlags.None,
                        CPUAccessFlags = CpuAccessFlags.Read,
                        MiscFlags = ResourceOptionFlags.None
                    };
                    _stagingTexture = _device.CreateTexture2D(textureDesc);
                }
            }
        }
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

    public bool TryCaptureFrame(int timeoutMs, out byte[] frameData, out int width, out int height, out string? error)
    {
        frameData = Array.Empty<byte>();
        width = 0;
        height = 0;
        error = null;

        if (_disposed)
        {
            error = "Capture session disposed";
            return false;
        }

        IDXGIResource? desktopResource = null;
        var acquired = false;
        try
        {
            var result = _duplication.AcquireNextFrame((uint)Math.Max(0, timeoutMs), out _, out desktopResource);
            if (result == Vortice.DXGI.ResultCode.WaitTimeout)
            {
                return false;
            }

            result.CheckError();
            acquired = true;

            using var texture = desktopResource.QueryInterface<ID3D11Texture2D>();
            _context.CopyResource(_stagingTexture, texture);

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

                var rowBytes = captureWidth * 4;
                for (var y = 0; y < captureHeight; y++)
                {
                    var sourceY = captureTop + y;
                    var sourceOffset = sourceY * mapped.RowPitch + captureLeft * 4;
                    var source = new IntPtr(mapped.DataPointer + sourceOffset);
                    var destination = y * rowBytes;
                    System.Runtime.InteropServices.Marshal.Copy(source, frameBuffer, destination, rowBytes);
                }

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
        finally
        {
            desktopResource?.Dispose();
            if (acquired)
            {
                _duplication.ReleaseFrame();
            }
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _duplication.Dispose();
        _stagingTexture.Dispose();
        _context.Dispose();
        _device.Dispose();
        _disposed = true;
    }
}
