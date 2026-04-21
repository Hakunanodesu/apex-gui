using System.Diagnostics;
using System.Threading;
using Vortice.Direct3D;
using Vortice.Direct3D11;
using Vortice.DXGI;
using static Vortice.Direct3D11.D3D11;
using static Vortice.DXGI.DXGI;

internal sealed class DesktopCaptureWorker : IDisposable
{
    private const double TargetCaptureIntervalMs = 1000.0 / 60.0;
    private readonly object _sync = new();
    private readonly Thread _thread;
    private bool _running = true;
    private byte[] _latestFrame = Array.Empty<byte>();
    private int _latestWidth;
    private int _latestHeight;
    private int _latestFrameId;
    private int _captureFrameId;
    private byte[] _latestWeaponRoi = Array.Empty<byte>();
    private int _latestWeaponRoiWidth;
    private int _latestWeaponRoiHeight;
    private int _latestWeaponRoiFrameId;
    private bool _previewFrameCacheEnabled;
    private string? _lastError;
    private OnnxWorker? _frameConsumer;

    private readonly Queue<double> _pendingCaptureMs = new();
    private int _requestedCaptureWidth = 320;
    private int _requestedCaptureHeight = 320;

    public DesktopCaptureWorker()
    {
        _thread = new Thread(CaptureThreadMain)
        {
            IsBackground = true,
            Name = "DXGI-Capture-Worker"
        };
        _thread.Start();
    }

    public bool TryCopyLatestFrame(ref byte[] uploadBuffer, ref int lastFrameId, out int width, out int height, out string? error)
    {
        lock (_sync)
        {
            error = _lastError;
            width = _latestWidth;
            height = _latestHeight;

            if (_latestFrameId == 0 || _latestFrameId == lastFrameId)
            {
                return false;
            }

            if (uploadBuffer.Length != _latestFrame.Length)
            {
                uploadBuffer = new byte[_latestFrame.Length];
            }

            System.Buffer.BlockCopy(_latestFrame, 0, uploadBuffer, 0, _latestFrame.Length);
            lastFrameId = _latestFrameId;
            return true;
        }
    }

    public void DrainCaptureSamples(List<double> destination)
    {
        lock (_sync)
        {
            while (_pendingCaptureMs.Count > 0)
            {
                destination.Add(_pendingCaptureMs.Dequeue());
            }
        }
    }

    public void SetCaptureRegion(int width, int height)
    {
        lock (_sync)
        {
            _requestedCaptureWidth = Math.Max(1, width);
            _requestedCaptureHeight = Math.Max(1, height);
        }
    }

    public bool TryCopyLatestWeaponRoi(ref byte[] uploadBuffer, ref int lastFrameId, out int width, out int height, out string? error)
    {
        lock (_sync)
        {
            error = _lastError;
            width = _latestWeaponRoiWidth;
            height = _latestWeaponRoiHeight;
            if (_latestWeaponRoiFrameId == 0 || _latestWeaponRoiFrameId == lastFrameId)
            {
                return false;
            }

            if (uploadBuffer.Length != _latestWeaponRoi.Length)
            {
                uploadBuffer = new byte[_latestWeaponRoi.Length];
            }

            Buffer.BlockCopy(_latestWeaponRoi, 0, uploadBuffer, 0, _latestWeaponRoi.Length);
            lastFrameId = _latestWeaponRoiFrameId;
            return true;
        }
    }

    public void SetFrameConsumer(OnnxWorker? frameConsumer)
    {
        lock (_sync)
        {
            _frameConsumer = frameConsumer;
        }
    }

    public void SetPreviewFrameCacheEnabled(bool enabled)
    {
        lock (_sync)
        {
            _previewFrameCacheEnabled = enabled;
            if (!enabled)
            {
                _latestFrame = Array.Empty<byte>();
                _latestWidth = 0;
                _latestHeight = 0;
                _latestFrameId = 0;
                _latestWeaponRoi = Array.Empty<byte>();
                _latestWeaponRoiWidth = 0;
                _latestWeaponRoiHeight = 0;
                _latestWeaponRoiFrameId = 0;
            }
        }
    }

    private void CaptureThreadMain()
    {
        try
        {
            using var duplicator = new DxgiDesktopDuplicator();
            var timer = Stopwatch.StartNew();
            var frameBudgetTimer = Stopwatch.StartNew();

            while (_running)
            {
                var remainingMs = TargetCaptureIntervalMs - frameBudgetTimer.Elapsed.TotalMilliseconds;
                if (remainingMs > 1.0)
                {
                    Thread.Sleep((int)remainingMs);
                    continue;
                }

                frameBudgetTimer.Restart();

                int requestedWidth;
                int requestedHeight;
                lock (_sync)
                {
                    requestedWidth = _requestedCaptureWidth;
                    requestedHeight = _requestedCaptureHeight;
                }

                duplicator.SetCaptureRegion(requestedWidth, requestedHeight);
                timer.Restart();
                var ok = duplicator.TryCaptureFrame(
                    1,
                    out var frameData,
                    out var width,
                    out var height,
                    out var weaponRoiData,
                    out var weaponRoiWidth,
                    out var weaponRoiHeight,
                    out var error);
                timer.Stop();
                var shouldBackoff = false;
                OnnxWorker? frameConsumer = null;
                var frameId = 0;

                lock (_sync)
                {
                    var elapsedMs = timer.Elapsed.TotalMilliseconds;

                    if (ok)
                    {
                        _pendingCaptureMs.Enqueue(elapsedMs);
                        _captureFrameId++;
                        frameId = _captureFrameId;
                        frameConsumer = _frameConsumer;
                        if (_previewFrameCacheEnabled)
                        {
                            if (_latestFrame.Length != frameData.Length)
                            {
                                _latestFrame = new byte[frameData.Length];
                            }

                            System.Buffer.BlockCopy(frameData, 0, _latestFrame, 0, frameData.Length);
                            _latestWidth = width;
                            _latestHeight = height;
                            _latestFrameId++;
                        }

                        if (weaponRoiWidth > 0 && weaponRoiHeight > 0 && weaponRoiData.Length == weaponRoiWidth * weaponRoiHeight * 3)
                        {
                            if (_latestWeaponRoi.Length != weaponRoiData.Length)
                            {
                                _latestWeaponRoi = new byte[weaponRoiData.Length];
                            }

                            Buffer.BlockCopy(weaponRoiData, 0, _latestWeaponRoi, 0, weaponRoiData.Length);
                            _latestWeaponRoiWidth = weaponRoiWidth;
                            _latestWeaponRoiHeight = weaponRoiHeight;
                            _latestWeaponRoiFrameId++;
                        }
                    }
                    else if (!string.IsNullOrWhiteSpace(error))
                    {
                        _lastError = error;
                        _running = false;
                    }
                    else
                    {
                        shouldBackoff = true;
                    }
                }

                if (ok && frameConsumer is not null)
                {
                    frameConsumer.SubmitFrame(frameData, width, height, frameId);
                }

                if (shouldBackoff)
                {
                    Thread.Sleep(1);
                }
            }
        }
        catch (Exception ex)
        {
            lock (_sync)
            {
                _lastError = $"{ex.GetType().Name}: {ex.Message}";
                _running = false;
            }
        }
    }

    public void Dispose()
    {
        _running = false;
        if (_thread.IsAlive)
        {
            _thread.Join(300);
        }
    }
}

internal sealed class DxgiDesktopDuplicator : IDisposable
{
    private const float WeaponRoiBaseWidth = 1920f;
    private const float WeaponRoiOffsetX = 384f;
    private const float WeaponRoiOffsetY = 122f;
    private const float WeaponRoiBaseWidthPixels = WeaponTemplateCatalog.TemplateWidth;
    private const float WeaponRoiBaseHeightPixels = WeaponTemplateCatalog.TemplateHeight;

    private readonly ID3D11Device _device;
    private readonly ID3D11DeviceContext _context;
    private readonly IDXGIOutputDuplication _duplication;
    private readonly ID3D11Texture2D _stagingTexture;
    private byte[] _frameBuffer = Array.Empty<byte>();
    private byte[] _weaponRoiBuffer = Array.Empty<byte>();
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
                        // CopyResource requires source/destination textures to have matching dimensions.
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

    public unsafe bool TryCaptureFrame(
        int timeoutMs,
        out byte[] frameData,
        out int width,
        out int height,
        out byte[] weaponRoiData,
        out int weaponRoiWidth,
        out int weaponRoiHeight,
        out string? error)
    {
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

                var (roiLeft, roiTop, roiWidth, roiHeight) = CalcWeaponRoi(_outputWidth, _outputHeight);
                if (roiWidth > 0 && roiHeight > 0)
                {
                    var requiredBytes = roiWidth * roiHeight * 3;
                    if (_weaponRoiBuffer.Length != requiredBytes)
                    {
                        _weaponRoiBuffer = new byte[requiredBytes];
                    }

                    var srcPtr = (byte*)mapped.DataPointer;
                    for (var y = 0; y < roiHeight; y++)
                    {
                        var sourceY = roiTop + y;
                        var sourceOffset = sourceY * mapped.RowPitch + roiLeft * 4;
                        var destination = y * roiWidth * 3;
                        for (var x = 0; x < roiWidth; x++)
                        {
                            var pixelOffset = sourceOffset + x * 4;
                            _weaponRoiBuffer[destination + x * 3 + 0] = srcPtr[pixelOffset + 2]; // R
                            _weaponRoiBuffer[destination + x * 3 + 1] = srcPtr[pixelOffset + 1]; // G
                            _weaponRoiBuffer[destination + x * 3 + 2] = srcPtr[pixelOffset + 0]; // B
                        }
                    }

                    weaponRoiData = _weaponRoiBuffer;
                    weaponRoiWidth = roiWidth;
                    weaponRoiHeight = roiHeight;
                }
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

    private static (int Left, int Top, int Width, int Height) CalcWeaponRoi(int frameWidth, int frameHeight)
    {
        var scale = frameWidth / WeaponRoiBaseWidth;
        var left = frameWidth - (int)MathF.Round(WeaponRoiOffsetX * scale);
        var top = frameHeight - (int)MathF.Round(WeaponRoiOffsetY * scale);
        var width = (int)MathF.Round(WeaponRoiBaseWidthPixels * scale);
        var height = (int)MathF.Round(WeaponRoiBaseHeightPixels * scale);

        left = Math.Clamp(left, 0, frameWidth);
        top = Math.Clamp(top, 0, frameHeight);
        width = Math.Clamp(width, 0, frameWidth - left);
        height = Math.Clamp(height, 0, frameHeight - top);
        return (left, top, width, height);
    }
}

