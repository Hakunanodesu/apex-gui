using System.Reflection;
using System.Numerics;
using System.Diagnostics;
using System.Threading;
using System.Runtime.InteropServices;
using ImGuiNET;
using OpenTK.Graphics.OpenGL4;
using OpenTK.Windowing.Common;
using OpenTK.Windowing.Common.Input;
using OpenTK.Windowing.Desktop;
using OpenTK.Windowing.GraphicsLibraryFramework;
using StbImageSharp;
using Vortice.Direct3D;
using Vortice.Direct3D11;
using Vortice.DXGI;
using Windows.Graphics.Capture;
using Windows.Graphics.DirectX;
using Windows.Graphics.DirectX.Direct3D11;
using WinRT;
using static Vortice.Direct3D11.D3D11;
using static Vortice.DXGI.DXGI;

var nativeWindowSettings = new NativeWindowSettings
{
    Title = "apex-imgui",
    ClientSize = new OpenTK.Mathematics.Vector2i(1280, 720),
    APIVersion = new Version(3, 3),
    Flags = ContextFlags.ForwardCompatible,
    Icon = ResourceAssets.LoadWindowIcon()
};

using var window = new DemoWindow(GameWindowSettings.Default, nativeWindowSettings);
window.Run();

public sealed class DemoWindow : GameWindow
{
    private ImGuiController? _controller;
    private float _dpiScale = 1.0f;
    private WgcCaptureWorker? _wgcWorker;
    private int _wgcPreviewTexture;
    private int _wgcPreviewWidth;
    private int _wgcPreviewHeight;
    private int _wgcLastPreviewFrameId;
    private byte[] _wgcUploadBuffer = Array.Empty<byte>();
    private string _wgcStatus = "未启动";
    private readonly RealtimePerfStats _wgcPerfStats = new();
    private PerfSnapshot _wgcPerfSnapshot;
    private readonly List<double> _wgcSampleBuffer = new(256);
    private double _wgcPreviewRefreshAccumulatorMs;

    private DesktopCaptureWorker? _dxgiWorker;
    private int _dxgiPreviewTexture;
    private int _dxgiPreviewWidth;
    private int _dxgiPreviewHeight;
    private int _dxgiLastPreviewFrameId;
    private byte[] _dxgiUploadBuffer = Array.Empty<byte>();
    private string _dxgiStatus = "未启动";
    private readonly RealtimePerfStats _dxgiPerfStats = new();
    private PerfSnapshot _dxgiPerfSnapshot;
    private readonly List<double> _dxgiSampleBuffer = new(256);
    private double _dxgiPreviewRefreshAccumulatorMs;

    public DemoWindow(GameWindowSettings gameWindowSettings, NativeWindowSettings nativeWindowSettings)
        : base(gameWindowSettings, nativeWindowSettings)
    {
    }

    protected override void OnLoad()
    {
        base.OnLoad();
        _controller = new ImGuiController(ClientSize.X, ClientSize.Y);
        VSync = VSyncMode.Off;
        RefreshDpiScale();
        GL.ClearColor(0.10f, 0.11f, 0.13f, 1.0f);
    }

    protected override void OnRenderFrame(FrameEventArgs args)
    {
        base.OnRenderFrame(args);
        if (_controller is null)
        {
            return;
        }

        RefreshDpiScale();
        UpdateWgcPreview((float)args.Time);
        var wgcTelemetry = _wgcWorker?.GetTelemetrySnapshot() ?? default;
        _wgcSampleBuffer.Clear();
        _wgcWorker?.DrainCaptureSamples(_wgcSampleBuffer);
        _wgcPerfStats.PushSample((float)args.Time, wgcTelemetry, _wgcSampleBuffer);
        if (_wgcPerfStats.TryBuildSnapshot(out var wgcSnapshot))
        {
            _wgcPerfSnapshot = wgcSnapshot;
        }

        UpdateDxgiPreview((float)args.Time);
        var dxgiTelemetry = _dxgiWorker?.GetTelemetrySnapshot() ?? default;
        _dxgiSampleBuffer.Clear();
        _dxgiWorker?.DrainCaptureSamples(_dxgiSampleBuffer);
        _dxgiPerfStats.PushSample((float)args.Time, dxgiTelemetry, _dxgiSampleBuffer);
        if (_dxgiPerfStats.TryBuildSnapshot(out var dxgiSnapshot))
        {
            _dxgiPerfSnapshot = dxgiSnapshot;
        }

        _controller.Update(this, (float)args.Time, _dpiScale);
        DrawUi();
        _controller.Render();

        SwapBuffers();
    }

    private void DrawUi()
    {
        var io = ImGui.GetIO();
        var viewport = ImGui.GetMainViewport();

        ImGui.SetNextWindowPos(viewport.Pos);
        ImGui.SetNextWindowSize(viewport.Size);

        var windowFlags =
            ImGuiWindowFlags.NoTitleBar |
            ImGuiWindowFlags.NoResize |
            ImGuiWindowFlags.NoMove |
            ImGuiWindowFlags.NoCollapse;

        ImGui.Begin("MainOverlay", windowFlags);

        if (ImGui.BeginTabBar("RootTabs"))
        {
            if (ImGui.BeginTabItem("主页"))
            {
                DrawWgcTab();
                ImGui.EndTabItem();
            }

            if (ImGui.BeginTabItem("调试"))
            {
                DrawDxgiTab();
                ImGui.EndTabItem();
            }

            ImGui.EndTabBar();
        }

        ImGui.End();
    }

    private void DrawWgcTab()
    {
        DrawCapturePanel(
            "WGC 屏幕捕获",
            _wgcStatus,
            _wgcPerfSnapshot,
            _wgcWorker is not null,
            OnStartWgcClicked,
            OnStopWgcClicked,
            _wgcPreviewTexture,
            _wgcPreviewWidth,
            _wgcPreviewHeight);
    }

    private void DrawDxgiTab()
    {
        DrawCapturePanel(
            "DXGI 屏幕捕获",
            _dxgiStatus,
            _dxgiPerfSnapshot,
            _dxgiWorker is not null,
            OnStartDxgiClicked,
            OnStopDxgiClicked,
            _dxgiPreviewTexture,
            _dxgiPreviewWidth,
            _dxgiPreviewHeight);
    }

    private void DrawCapturePanel(
        string title,
        string status,
        PerfSnapshot perfSnapshot,
        bool isRunning,
        Action onStart,
        Action onStop,
        int previewTexture,
        int previewWidth,
        int previewHeight)
    {
        ImGui.Text(title);
        ImGui.Separator();
        ImGui.Text($"状态: {status}");
        ImGui.Text("捕获性能统计(1秒刷新)");
        ImGui.Text($"捕获轮询频率: {perfSnapshot.CapturePollHz:0.0} Hz");
        ImGui.Text($"捕获帧率: {perfSnapshot.CapturedFps:0.0} fps");
        ImGui.Text($"捕获耗时均值: {perfSnapshot.AvgCaptureMs:0.00} ms");
        ImGui.Text($"捕获耗时 P95: {perfSnapshot.P95CaptureMs:0.00} ms");
        ImGui.Text($"捕获耗时 P99: {perfSnapshot.P99CaptureMs:0.00} ms");
        ImGui.Text($"捕获成功率: {perfSnapshot.CaptureSuccessRate:0.0}%");

        if (!isRunning)
        {
            if (ImGui.Button("启动捕获"))
            {
                onStart();
            }
        }
        else
        {
            if (ImGui.Button("停止捕获"))
            {
                onStop();
            }
        }

        ImGui.SameLine();
        ImGui.Text($"窗口大小: {ClientSize.X} x {ClientSize.Y}");

        ImGui.Separator();
        ImGui.Text("预览:");

        if (previewTexture != 0 && previewWidth > 0 && previewHeight > 0)
        {
            var available = ImGui.GetContentRegionAvail();
            var scaleX = available.X / previewWidth;
            var scaleY = available.Y / previewHeight;
            var scale = MathF.Min(scaleX, scaleY);
            scale = MathF.Min(scale, 1.0f);
            scale = MathF.Max(scale, 0.1f);

            var previewSize = new Vector2(previewWidth * scale, previewHeight * scale);
            ImGui.Image((IntPtr)previewTexture, previewSize, new Vector2(0, 0), new Vector2(1, 1));
        }
        else
        {
            ImGui.Text("暂无画面");
        }
    }

    private void OnStartWgcClicked()
    {
        StartWgcCapture();
    }

    private void OnStopWgcClicked()
    {
        StopWgcCapture("已停止");
    }

    private void OnStartDxgiClicked()
    {
        StartDxgiCapture();
    }

    private void OnStopDxgiClicked()
    {
        StopDxgiCapture("已停止");
    }

    private void StartWgcCapture()
    {
        try
        {
            StopWgcCapture("重启捕获");
            _wgcWorker = new WgcCaptureWorker();
            _wgcStatus = "捕获中";
            _wgcPerfStats.Reset();
            _wgcPerfSnapshot = default;
            _wgcLastPreviewFrameId = 0;
            _wgcPreviewRefreshAccumulatorMs = 0.0;
        }
        catch (Exception ex)
        {
            _wgcStatus = $"启动失败: {ex.Message}";
            _wgcWorker = null;
        }
    }

    private void StopWgcCapture(string status)
    {
        _wgcWorker?.Dispose();
        _wgcWorker = null;
        _wgcStatus = status;
        _wgcPreviewRefreshAccumulatorMs = 0.0;
        _wgcLastPreviewFrameId = 0;
        _wgcUploadBuffer = Array.Empty<byte>();
        _wgcPreviewWidth = 0;
        _wgcPreviewHeight = 0;
        if (_wgcPreviewTexture != 0)
        {
            GL.DeleteTexture(_wgcPreviewTexture);
            _wgcPreviewTexture = 0;
        }
    }

    private void UpdateWgcPreview(float frameDeltaSeconds)
    {
        if (_wgcWorker is null)
        {
            return;
        }

        _wgcPreviewRefreshAccumulatorMs += Math.Max(frameDeltaSeconds, 0f) * 1000.0;
        if (_wgcPreviewRefreshAccumulatorMs < 20.0)
        {
            return;
        }
        _wgcPreviewRefreshAccumulatorMs = 0.0;

        if (_wgcWorker.TryCopyLatestFrame(ref _wgcUploadBuffer, ref _wgcLastPreviewFrameId, out var width, out var height, out var error))
        {
            EnsureWgcPreviewTexture(width, height);
            GL.BindTexture(TextureTarget.Texture2D, _wgcPreviewTexture);
            GL.TexSubImage2D(
                TextureTarget.Texture2D,
                0,
                0,
                0,
                width,
                height,
                OpenTK.Graphics.OpenGL4.PixelFormat.Bgra,
                PixelType.UnsignedByte,
                _wgcUploadBuffer);
        }
        else if (!string.IsNullOrWhiteSpace(error))
        {
            StopWgcCapture($"捕获错误: {error}");
        }
    }

    private void EnsureWgcPreviewTexture(int width, int height)
    {
        if (_wgcPreviewTexture != 0 && width == _wgcPreviewWidth && height == _wgcPreviewHeight)
        {
            return;
        }

        if (_wgcPreviewTexture != 0)
        {
            GL.DeleteTexture(_wgcPreviewTexture);
            _wgcPreviewTexture = 0;
        }

        _wgcPreviewTexture = GL.GenTexture();
        GL.BindTexture(TextureTarget.Texture2D, _wgcPreviewTexture);
        GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMinFilter, (int)TextureMinFilter.Linear);
        GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMagFilter, (int)TextureMagFilter.Linear);
        GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapS, (int)TextureWrapMode.ClampToEdge);
        GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapT, (int)TextureWrapMode.ClampToEdge);
        GL.TexImage2D(
            TextureTarget.Texture2D,
            0,
            PixelInternalFormat.Rgba,
            width,
            height,
            0,
            OpenTK.Graphics.OpenGL4.PixelFormat.Bgra,
            PixelType.UnsignedByte,
            IntPtr.Zero);

        _wgcPreviewWidth = width;
        _wgcPreviewHeight = height;
    }

    private void StartDxgiCapture()
    {
        try
        {
            StopDxgiCapture("重启捕获");
            _dxgiWorker = new DesktopCaptureWorker();
            _dxgiStatus = "捕获中";
            _dxgiPerfStats.Reset();
            _dxgiPerfSnapshot = default;
            _dxgiLastPreviewFrameId = 0;
            _dxgiPreviewRefreshAccumulatorMs = 0.0;
        }
        catch (Exception ex)
        {
            _dxgiStatus = $"启动失败: {ex.Message}";
            _dxgiWorker = null;
        }
    }

    private void StopDxgiCapture(string status)
    {
        _dxgiWorker?.Dispose();
        _dxgiWorker = null;
        _dxgiStatus = status;
        _dxgiPreviewRefreshAccumulatorMs = 0.0;
        _dxgiLastPreviewFrameId = 0;
        _dxgiUploadBuffer = Array.Empty<byte>();
        _dxgiPreviewWidth = 0;
        _dxgiPreviewHeight = 0;
        if (_dxgiPreviewTexture != 0)
        {
            GL.DeleteTexture(_dxgiPreviewTexture);
            _dxgiPreviewTexture = 0;
        }
    }

    private void UpdateDxgiPreview(float frameDeltaSeconds)
    {
        if (_dxgiWorker is null)
        {
            return;
        }

        _dxgiPreviewRefreshAccumulatorMs += Math.Max(frameDeltaSeconds, 0f) * 1000.0;
        if (_dxgiPreviewRefreshAccumulatorMs < 20.0)
        {
            return;
        }
        _dxgiPreviewRefreshAccumulatorMs = 0.0;

        if (_dxgiWorker.TryCopyLatestFrame(ref _dxgiUploadBuffer, ref _dxgiLastPreviewFrameId, out var width, out var height, out var error))
        {
            EnsureDxgiPreviewTexture(width, height);
            GL.BindTexture(TextureTarget.Texture2D, _dxgiPreviewTexture);
            GL.TexSubImage2D(
                TextureTarget.Texture2D,
                0,
                0,
                0,
                width,
                height,
                OpenTK.Graphics.OpenGL4.PixelFormat.Bgra,
                PixelType.UnsignedByte,
                _dxgiUploadBuffer);
        }
        else if (!string.IsNullOrWhiteSpace(error))
        {
            StopDxgiCapture($"捕获错误: {error}");
        }
    }

    private void EnsureDxgiPreviewTexture(int width, int height)
    {
        if (_dxgiPreviewTexture != 0 && width == _dxgiPreviewWidth && height == _dxgiPreviewHeight)
        {
            return;
        }

        if (_dxgiPreviewTexture != 0)
        {
            GL.DeleteTexture(_dxgiPreviewTexture);
            _dxgiPreviewTexture = 0;
        }

        _dxgiPreviewTexture = GL.GenTexture();
        GL.BindTexture(TextureTarget.Texture2D, _dxgiPreviewTexture);
        GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMinFilter, (int)TextureMinFilter.Linear);
        GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMagFilter, (int)TextureMagFilter.Linear);
        GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapS, (int)TextureWrapMode.ClampToEdge);
        GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapT, (int)TextureWrapMode.ClampToEdge);
        GL.TexImage2D(
            TextureTarget.Texture2D,
            0,
            PixelInternalFormat.Rgba,
            width,
            height,
            0,
            OpenTK.Graphics.OpenGL4.PixelFormat.Bgra,
            PixelType.UnsignedByte,
            IntPtr.Zero);

        _dxgiPreviewWidth = width;
        _dxgiPreviewHeight = height;
    }

    protected override void OnResize(ResizeEventArgs e)
    {
        base.OnResize(e);
        GL.Viewport(0, 0, ClientSize.X, ClientSize.Y);
        _controller?.WindowResized(ClientSize.X, ClientSize.Y);
        RefreshDpiScale();
    }

    protected override void OnTextInput(TextInputEventArgs e)
    {
        base.OnTextInput(e);
        _controller?.PressChar((char)e.Unicode);
    }

    protected override void OnMouseWheel(MouseWheelEventArgs e)
    {
        base.OnMouseWheel(e);
        _controller?.AddMouseScroll(e.OffsetX, e.OffsetY);
    }

    protected override void OnUnload()
    {
        StopWgcCapture("已释放");
        StopDxgiCapture("已释放");
        if (_wgcPreviewTexture != 0)
        {
            GL.DeleteTexture(_wgcPreviewTexture);
            _wgcPreviewTexture = 0;
        }
        if (_dxgiPreviewTexture != 0)
        {
            GL.DeleteTexture(_dxgiPreviewTexture);
            _dxgiPreviewTexture = 0;
        }

        _controller?.Dispose();
        base.OnUnload();
    }

    private void RefreshDpiScale()
    {
        if (_controller is null)
        {
            return;
        }

        var nextDpiScale = 1.0f;
        if (TryGetCurrentMonitorScale(out var scaleX, out var scaleY))
        {
            nextDpiScale = (scaleX + scaleY) * 0.5f;
        }

        nextDpiScale = Math.Clamp(nextDpiScale, 0.5f, 4.0f);
        if (MathF.Abs(nextDpiScale - _dpiScale) < 0.01f)
        {
            return;
        }

        _dpiScale = nextDpiScale;
        _controller.SetDpiScale(_dpiScale);
    }
}

internal readonly struct PerfSnapshot
{
    public readonly double CapturePollHz;
    public readonly double CapturedFps;
    public readonly double AvgCaptureMs;
    public readonly double P95CaptureMs;
    public readonly double P99CaptureMs;
    public readonly double CaptureSuccessRate;

    public PerfSnapshot(
        double capturePollHz,
        double capturedFps,
        double avgCaptureMs,
        double p95CaptureMs,
        double p99CaptureMs,
        double captureSuccessRate)
    {
        CapturePollHz = capturePollHz;
        CapturedFps = capturedFps;
        AvgCaptureMs = avgCaptureMs;
        P95CaptureMs = p95CaptureMs;
        P99CaptureMs = p99CaptureMs;
        CaptureSuccessRate = captureSuccessRate;
    }
}

internal sealed class RealtimePerfStats
{
    private double _windowSeconds;

    private long _capturePollCount;
    private long _captureSuccessCount;
    private double _captureMsSum;
    private readonly List<double> _captureMsSamples = new(2000);
    private CaptureTelemetry _lastTelemetry;
    private bool _hasLastTelemetry;

    public void Reset()
    {
        _windowSeconds = 0;
        _capturePollCount = 0;
        _captureSuccessCount = 0;
        _captureMsSum = 0;
        _captureMsSamples.Clear();
        _lastTelemetry = default;
        _hasLastTelemetry = false;
    }

    public void PushSample(float deltaTimeSeconds, CaptureTelemetry telemetry, List<double> captureSamples)
    {
        var clampedDelta = Math.Max(deltaTimeSeconds, 1f / 1000f);
        _windowSeconds += clampedDelta;
        for (var i = 0; i < captureSamples.Count; i++)
        {
            _captureMsSamples.Add(captureSamples[i]);
        }

        if (!_hasLastTelemetry)
        {
            _lastTelemetry = telemetry;
            _hasLastTelemetry = true;
            return;
        }

        var pollDelta = telemetry.PollCount - _lastTelemetry.PollCount;
        var successDelta = telemetry.SuccessCount - _lastTelemetry.SuccessCount;
        var captureMsDelta = telemetry.TotalCaptureMs - _lastTelemetry.TotalCaptureMs;
        if (pollDelta > 0)
        {
            _capturePollCount += pollDelta;
            _captureSuccessCount += Math.Max(0, successDelta);
            _captureMsSum += Math.Max(0.0, captureMsDelta);
        }

        _lastTelemetry = telemetry;
    }

    public bool TryBuildSnapshot(out PerfSnapshot snapshot)
    {
        if (_windowSeconds < 1.0)
        {
            snapshot = default;
            return false;
        }

        var capturePollHz = _capturePollCount / _windowSeconds;
        var capturedFps = _captureSuccessCount / _windowSeconds;
        var avgCaptureMs = _capturePollCount > 0 ? _captureMsSum / _capturePollCount : 0.0;
        var p95CaptureMs = Percentile(_captureMsSamples, 0.95);
        var p99CaptureMs = Percentile(_captureMsSamples, 0.99);
        var successRate = _capturePollCount > 0
            ? (double)_captureSuccessCount / _capturePollCount * 100.0
            : 0.0;

        snapshot = new PerfSnapshot(
            capturePollHz,
            capturedFps,
            avgCaptureMs,
            p95CaptureMs,
            p99CaptureMs,
            successRate);

        Reset();
        return true;
    }

    private static double Percentile(List<double> values, double percentile)
    {
        if (values.Count == 0)
        {
            return 0.0;
        }

        values.Sort();
        var rank = percentile * (values.Count - 1);
        var low = (int)Math.Floor(rank);
        var high = (int)Math.Ceiling(rank);
        if (low == high)
        {
            return values[low];
        }

        var weight = rank - low;
        return values[low] * (1.0 - weight) + values[high] * weight;
    }
}

internal readonly struct CaptureTelemetry
{
    public readonly long PollCount;
    public readonly long SuccessCount;
    public readonly double TotalCaptureMs;
    public readonly double MaxCaptureMs;

    public CaptureTelemetry(long pollCount, long successCount, double totalCaptureMs, double maxCaptureMs)
    {
        PollCount = pollCount;
        SuccessCount = successCount;
        TotalCaptureMs = totalCaptureMs;
        MaxCaptureMs = maxCaptureMs;
    }
}

internal sealed class DesktopCaptureWorker : IDisposable
{
    private readonly object _sync = new();
    private readonly Thread _thread;
    private bool _running = true;
    private byte[] _latestFrame = Array.Empty<byte>();
    private int _latestWidth;
    private int _latestHeight;
    private int _latestFrameId;
    private string? _lastError;

    private long _pollCount;
    private long _successCount;
    private double _captureMsSum;
    private double _captureMsMax;
    private readonly Queue<double> _pendingCaptureMs = new();

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

    public CaptureTelemetry GetTelemetrySnapshot()
    {
        lock (_sync)
        {
            return new CaptureTelemetry(_pollCount, _successCount, _captureMsSum, _captureMsMax);
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

    private void CaptureThreadMain()
    {
        try
        {
            using var duplicator = new DxgiDesktopDuplicator();
            var timer = Stopwatch.StartNew();

            while (_running)
            {
                timer.Restart();
                var ok = duplicator.TryCaptureFrame(1, out var frameData, out var width, out var height, out var error);
                timer.Stop();

                lock (_sync)
                {
                    _pollCount++;
                    var elapsedMs = timer.Elapsed.TotalMilliseconds;
                    _captureMsSum += elapsedMs;
                    _captureMsMax = Math.Max(_captureMsMax, elapsedMs);
                    _pendingCaptureMs.Enqueue(elapsedMs);

                    if (ok)
                    {
                        _successCount++;
                        if (_latestFrame.Length != frameData.Length)
                        {
                            _latestFrame = new byte[frameData.Length];
                        }

                        System.Buffer.BlockCopy(frameData, 0, _latestFrame, 0, frameData.Length);
                        _latestWidth = width;
                        _latestHeight = height;
                        _latestFrameId++;
                    }
                    else if (!string.IsNullOrWhiteSpace(error))
                    {
                        _lastError = error;
                        _running = false;
                    }
                }
            }
        }
        catch (Exception ex)
        {
            lock (_sync)
            {
                _lastError = ex.Message;
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

internal sealed class WgcCaptureWorker : IDisposable
{
    private readonly object _sync = new();
    private readonly Thread _thread;
    private bool _running = true;
    private byte[] _latestFrame = Array.Empty<byte>();
    private int _latestWidth;
    private int _latestHeight;
    private int _latestFrameId;
    private string? _lastError;

    private long _pollCount;
    private long _successCount;
    private double _captureMsSum;
    private double _captureMsMax;
    private readonly Queue<double> _pendingCaptureMs = new();

    public WgcCaptureWorker()
    {
        _thread = new Thread(CaptureThreadMain)
        {
            IsBackground = true,
            Name = "WGC-Capture-Worker"
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

    public CaptureTelemetry GetTelemetrySnapshot()
    {
        lock (_sync)
        {
            return new CaptureTelemetry(_pollCount, _successCount, _captureMsSum, _captureMsMax);
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

    private void CaptureThreadMain()
    {
        try
        {
            using var capturer = new WgcPrimaryMonitorCapturer();
            var timer = Stopwatch.StartNew();

            while (_running)
            {
                timer.Restart();
                var ok = capturer.TryCaptureFrame(out var frameData, out var width, out var height, out var error);
                timer.Stop();

                lock (_sync)
                {
                    _pollCount++;
                    var elapsedMs = timer.Elapsed.TotalMilliseconds;
                    _captureMsSum += elapsedMs;
                    _captureMsMax = Math.Max(_captureMsMax, elapsedMs);
                    _pendingCaptureMs.Enqueue(elapsedMs);

                    if (ok)
                    {
                        _successCount++;
                        if (_latestFrame.Length != frameData.Length)
                        {
                            _latestFrame = new byte[frameData.Length];
                        }

                        System.Buffer.BlockCopy(frameData, 0, _latestFrame, 0, frameData.Length);
                        _latestWidth = width;
                        _latestHeight = height;
                        _latestFrameId++;
                    }
                    else if (!string.IsNullOrWhiteSpace(error))
                    {
                        _lastError = error;
                        _running = false;
                    }
                }
            }
        }
        catch (Exception ex)
        {
            lock (_sync)
            {
                _lastError = ex.Message;
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

internal sealed class WgcPrimaryMonitorCapturer : IDisposable
{
    private readonly ID3D11Device _device;
    private readonly ID3D11DeviceContext _context;
    private readonly ID3D11Texture2D _stagingTexture;
    private readonly byte[] _frameBuffer;
    private readonly GraphicsCaptureSession _session;
    private readonly Direct3D11CaptureFramePool _framePool;
    private readonly Queue<Direct3D11CaptureFrame> _frames = new();
    private readonly object _framesLock = new();
    private readonly int _width;
    private readonly int _height;
    private bool _disposed;

    public WgcPrimaryMonitorCapturer()
    {
        if (!GraphicsCaptureSession.IsSupported())
        {
            throw new InvalidOperationException("Windows Graphics Capture is not supported on this system.");
        }

        _device = D3D11CreateDevice(DriverType.Hardware, DeviceCreationFlags.BgraSupport);
        _context = _device.ImmediateContext;

        var primaryMonitor = Win32Interop.MonitorFromPoint(new Win32Point(0, 0), 1);
        if (primaryMonitor == IntPtr.Zero)
        {
            throw new InvalidOperationException("Failed to locate primary monitor.");
        }

        var item = Win32Interop.CreateItemForMonitor(primaryMonitor);
        _width = item.Size.Width;
        _height = item.Size.Height;
        if (_width <= 0 || _height <= 0)
        {
            throw new InvalidOperationException("WGC target size is invalid.");
        }

        var dxgiDevice = _device.QueryInterface<IDXGIDevice>();
        using (dxgiDevice)
        {
            var d3dDevice = Win32Interop.CreateDirect3DDevice(dxgiDevice.NativePointer);
            _framePool = Direct3D11CaptureFramePool.CreateFreeThreaded(
                d3dDevice,
                DirectXPixelFormat.B8G8R8A8UIntNormalized,
                2,
                new Windows.Graphics.SizeInt32(_width, _height));
        }

        _session = _framePool.CreateCaptureSession(item);
        _framePool.FrameArrived += OnFrameArrived;
        _session.StartCapture();

        var textureDesc = new Texture2DDescription
        {
            Width = (uint)_width,
            Height = (uint)_height,
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
        _frameBuffer = new byte[_width * _height * 4];
    }

    private void OnFrameArrived(Direct3D11CaptureFramePool sender, object args)
    {
        var frame = sender.TryGetNextFrame();
        if (frame is null)
        {
            return;
        }

        lock (_framesLock)
        {
            _frames.Enqueue(frame);
            while (_frames.Count > 2)
            {
                _frames.Dequeue().Dispose();
            }
        }
    }

    public bool TryCaptureFrame(out byte[] frameData, out int width, out int height, out string? error)
    {
        frameData = Array.Empty<byte>();
        width = 0;
        height = 0;
        error = null;

        if (_disposed)
        {
            error = "WGC session disposed";
            return false;
        }

        Direct3D11CaptureFrame? frame = null;
        lock (_framesLock)
        {
            if (_frames.Count > 0)
            {
                frame = _frames.Dequeue();
            }
        }

        if (frame is null)
        {
            return false;
        }

        using (frame)
        {
            var texture = Win32Interop.GetTexture2D(frame.Surface);
            using (texture)
            {
                _context.CopyResource(_stagingTexture, texture);
                _context.Map(_stagingTexture, 0, MapMode.Read, Vortice.Direct3D11.MapFlags.None, out var mapped).CheckError();
                try
                {
                    var rowBytes = _width * 4;
                    for (var y = 0; y < _height; y++)
                    {
                        var source = new IntPtr(mapped.DataPointer + y * mapped.RowPitch);
                        var destination = y * rowBytes;
                        System.Runtime.InteropServices.Marshal.Copy(source, _frameBuffer, destination, rowBytes);
                    }
                }
                finally
                {
                    _context.Unmap(_stagingTexture, 0);
                }
            }
        }

        frameData = _frameBuffer;
        width = _width;
        height = _height;
        return true;
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _framePool.FrameArrived -= OnFrameArrived;
        _session.Dispose();
        _framePool.Dispose();
        lock (_framesLock)
        {
            while (_frames.Count > 0)
            {
                _frames.Dequeue().Dispose();
            }
        }

        _stagingTexture.Dispose();
        _context.Dispose();
        _device.Dispose();
        _disposed = true;
    }
}

internal static class Win32Interop
{
    private static readonly Guid GraphicsCaptureItemGuid = new("79C3F95B-31F7-4EC2-A464-632EF5D30760");
    private static readonly Guid ID3D11Texture2DGuid = new("6f15aaf2-d208-4e89-9ab4-489535d34f9c");

    public static IntPtr MonitorFromPoint(Win32Point point, uint flags)
    {
        return MonitorFromPointNative(point, flags);
    }

    public static GraphicsCaptureItem CreateItemForMonitor(IntPtr hMonitor)
    {
        using var factory = ActivationFactory.Get("Windows.Graphics.Capture.GraphicsCaptureItem");
        using var interopRef = factory.As<IGraphicsCaptureItemInterop>(typeof(IGraphicsCaptureItemInterop).GUID);
        var interop = (IGraphicsCaptureItemInterop)Marshal.GetObjectForIUnknown(interopRef.ThisPtr);
        interop.CreateForMonitor(hMonitor, GraphicsCaptureItemGuid, out var resultPtr);
        return MarshalInspectable<GraphicsCaptureItem>.FromAbi(resultPtr);
    }

    public static IDirect3DDevice CreateDirect3DDevice(IntPtr dxgiDevicePtr)
    {
        var hr = CreateDirect3D11DeviceFromDXGIDevice(dxgiDevicePtr, out var devicePtr);
        if (hr < 0)
        {
            Marshal.ThrowExceptionForHR(hr);
        }

        return MarshalInspectable<IDirect3DDevice>.FromAbi(devicePtr);
    }

    public static ID3D11Texture2D GetTexture2D(IDirect3DSurface surface)
    {
        var access = surface.As<IDirect3DDxgiInterfaceAccess>();
        access.GetInterface(ID3D11Texture2DGuid, out var texturePtr);
        return new ID3D11Texture2D(texturePtr);
    }

    [DllImport("user32.dll")]
    private static extern IntPtr MonitorFromPointNative(Win32Point pt, uint dwFlags);

    [DllImport("d3d11.dll")]
    private static extern int CreateDirect3D11DeviceFromDXGIDevice(
        IntPtr dxgiDevice,
        out IntPtr graphicsDevice);
}

[StructLayout(LayoutKind.Sequential)]
internal struct Win32Point
{
    public int X;
    public int Y;

    public Win32Point(int x, int y)
    {
        X = x;
        Y = y;
    }
}

[ComImport]
[System.Runtime.InteropServices.Guid("3628e81b-3cac-4c60-b7f4-23ce0e0c3356")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IGraphicsCaptureItemInterop
{
    void CreateForWindow(IntPtr window, [In] in Guid iid, out IntPtr result);
    void CreateForMonitor(IntPtr monitor, [In] in Guid iid, out IntPtr result);
}

[ComImport]
[System.Runtime.InteropServices.Guid("A9B3D012-3DF2-4EE3-B8D1-8695F457D3C1")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IDirect3DDxgiInterfaceAccess
{
    void GetInterface([In] in Guid iid, out IntPtr result);
}

public sealed class ImGuiController : IDisposable
{
    private readonly IntPtr _context;
    private readonly int _vertexArray;
    private readonly int _vertexBuffer;
    private readonly int _indexBuffer;
    private readonly int _shader;
    private readonly int _vertexShader;
    private readonly int _fragmentShader;
    private readonly int _attribLocationTex;
    private readonly int _attribLocationProjMtx;

    private int _fontTexture;
    private int _windowWidth;
    private int _windowHeight;
    private Vector2 _scrollDelta;
    private float _dpiScale = 1.0f;
    private ImFontPtr _englishFont;
    private bool _hasEnglishFont;

    public ImGuiController(int width, int height)
    {
        _windowWidth = width;
        _windowHeight = height;

        _context = ImGui.CreateContext();
        ImGui.SetCurrentContext(_context);
        var io = ImGui.GetIO();
        io.ConfigFlags &= ~ImGuiConfigFlags.DockingEnable;
        ConfigureFonts(io);

        io.BackendFlags |= ImGuiBackendFlags.HasMouseCursors;
        io.BackendFlags |= ImGuiBackendFlags.HasSetMousePos;

        _vertexShader = GL.CreateShader(ShaderType.VertexShader);
        GL.ShaderSource(_vertexShader, VertexSource);
        GL.CompileShader(_vertexShader);
        GL.GetShader(_vertexShader, ShaderParameter.CompileStatus, out var vertexOk);
        if (vertexOk == 0)
        {
            throw new InvalidOperationException(GL.GetShaderInfoLog(_vertexShader));
        }

        _fragmentShader = GL.CreateShader(ShaderType.FragmentShader);
        GL.ShaderSource(_fragmentShader, FragmentSource);
        GL.CompileShader(_fragmentShader);
        GL.GetShader(_fragmentShader, ShaderParameter.CompileStatus, out var fragmentOk);
        if (fragmentOk == 0)
        {
            throw new InvalidOperationException(GL.GetShaderInfoLog(_fragmentShader));
        }

        _shader = GL.CreateProgram();
        GL.AttachShader(_shader, _vertexShader);
        GL.AttachShader(_shader, _fragmentShader);
        GL.LinkProgram(_shader);
        GL.GetProgram(_shader, GetProgramParameterName.LinkStatus, out var linkOk);
        if (linkOk == 0)
        {
            throw new InvalidOperationException(GL.GetProgramInfoLog(_shader));
        }

        _attribLocationTex = GL.GetUniformLocation(_shader, "Texture");
        _attribLocationProjMtx = GL.GetUniformLocation(_shader, "ProjMtx");

        _vertexBuffer = GL.GenBuffer();
        _indexBuffer = GL.GenBuffer();
        _vertexArray = GL.GenVertexArray();

        GL.BindVertexArray(_vertexArray);
        GL.BindBuffer(BufferTarget.ArrayBuffer, _vertexBuffer);
        GL.BindBuffer(BufferTarget.ElementArrayBuffer, _indexBuffer);

        var stride = 20;
        GL.EnableVertexAttribArray(0);
        GL.VertexAttribPointer(0, 2, VertexAttribPointerType.Float, false, stride, 0);
        GL.EnableVertexAttribArray(1);
        GL.VertexAttribPointer(1, 2, VertexAttribPointerType.Float, false, stride, 8);
        GL.EnableVertexAttribArray(2);
        GL.VertexAttribPointer(2, 4, VertexAttribPointerType.UnsignedByte, true, stride, 16);

        CreateFontTexture();
        SetDpiScale(1.0f);
        SetPerFrameData(1f / 60f);
    }

    public ImFontPtr EnglishFont => _englishFont;
    public bool HasEnglishFont => _hasEnglishFont;

    public void WindowResized(int width, int height)
    {
        _windowWidth = width;
        _windowHeight = height;
    }

    public void PressChar(char keyChar)
    {
        ImGui.GetIO().AddInputCharacter(keyChar);
    }

    public void AddMouseScroll(float x, float y)
    {
        _scrollDelta += new Vector2(x, y);
    }

    public void Update(GameWindow window, float deltaTime, float dpiScale)
    {
        ImGui.SetCurrentContext(_context);
        SetDpiScale(dpiScale);
        SetPerFrameData(deltaTime);
        UpdateInput(window);
        ImGui.NewFrame();
    }

    public void SetDpiScale(float dpiScale)
    {
        var clamped = Math.Clamp(dpiScale, 0.5f, 4.0f);
        if (MathF.Abs(clamped - _dpiScale) < 0.01f)
        {
            return;
        }

        _dpiScale = clamped;
        var io = ImGui.GetIO();
        io.FontGlobalScale = _dpiScale;
    }

    private void ConfigureFonts(ImGuiIOPtr io)
    {
        io.Fonts.Clear();

        var zhFontPath = ResourceAssets.ExtractToTemp("AlibabaPuHuiTi-3-55-Regular.otf");
        var enFontPath = ResourceAssets.ExtractToTemp("JetBrainsMono-Regular.ttf");

        io.Fonts.AddFontFromFileTTF(zhFontPath, 18.0f, null, io.Fonts.GetGlyphRangesChineseFull());
        _englishFont = io.Fonts.AddFontFromFileTTF(enFontPath, 17.0f, null, io.Fonts.GetGlyphRangesDefault());
        _hasEnglishFont = true;
    }

    public void Render()
    {
        ImGui.Render();
        RenderDrawData(ImGui.GetDrawData());
    }

    private void SetPerFrameData(float deltaTime)
    {
        var io = ImGui.GetIO();
        io.DisplaySize = new Vector2(_windowWidth, _windowHeight);
        io.DisplayFramebufferScale = Vector2.One;
        io.DeltaTime = deltaTime > 0f ? deltaTime : 1f / 60f;
    }

    private void UpdateInput(GameWindow window)
    {
        var io = ImGui.GetIO();

        var mouse = window.MouseState;
        io.AddMousePosEvent(mouse.X, mouse.Y);
        io.AddMouseButtonEvent(0, mouse.IsButtonDown(MouseButton.Left));
        io.AddMouseButtonEvent(1, mouse.IsButtonDown(MouseButton.Right));
        io.AddMouseButtonEvent(2, mouse.IsButtonDown(MouseButton.Middle));
        io.AddMouseWheelEvent(_scrollDelta.X, _scrollDelta.Y);
        _scrollDelta = Vector2.Zero;

        var keyboard = window.KeyboardState;
        io.AddKeyEvent(ImGuiKey.Tab, keyboard.IsKeyDown(Keys.Tab));
        io.AddKeyEvent(ImGuiKey.LeftArrow, keyboard.IsKeyDown(Keys.Left));
        io.AddKeyEvent(ImGuiKey.RightArrow, keyboard.IsKeyDown(Keys.Right));
        io.AddKeyEvent(ImGuiKey.UpArrow, keyboard.IsKeyDown(Keys.Up));
        io.AddKeyEvent(ImGuiKey.DownArrow, keyboard.IsKeyDown(Keys.Down));
        io.AddKeyEvent(ImGuiKey.PageUp, keyboard.IsKeyDown(Keys.PageUp));
        io.AddKeyEvent(ImGuiKey.PageDown, keyboard.IsKeyDown(Keys.PageDown));
        io.AddKeyEvent(ImGuiKey.Home, keyboard.IsKeyDown(Keys.Home));
        io.AddKeyEvent(ImGuiKey.End, keyboard.IsKeyDown(Keys.End));
        io.AddKeyEvent(ImGuiKey.Insert, keyboard.IsKeyDown(Keys.Insert));
        io.AddKeyEvent(ImGuiKey.Delete, keyboard.IsKeyDown(Keys.Delete));
        io.AddKeyEvent(ImGuiKey.Backspace, keyboard.IsKeyDown(Keys.Backspace));
        io.AddKeyEvent(ImGuiKey.Space, keyboard.IsKeyDown(Keys.Space));
        io.AddKeyEvent(ImGuiKey.Enter, keyboard.IsKeyDown(Keys.Enter));
        io.AddKeyEvent(ImGuiKey.Escape, keyboard.IsKeyDown(Keys.Escape));
        io.AddKeyEvent(ImGuiKey.A, keyboard.IsKeyDown(Keys.A));
        io.AddKeyEvent(ImGuiKey.C, keyboard.IsKeyDown(Keys.C));
        io.AddKeyEvent(ImGuiKey.V, keyboard.IsKeyDown(Keys.V));
        io.AddKeyEvent(ImGuiKey.X, keyboard.IsKeyDown(Keys.X));
        io.AddKeyEvent(ImGuiKey.Y, keyboard.IsKeyDown(Keys.Y));
        io.AddKeyEvent(ImGuiKey.Z, keyboard.IsKeyDown(Keys.Z));

        var ctrl = keyboard.IsKeyDown(Keys.LeftControl) || keyboard.IsKeyDown(Keys.RightControl);
        var shift = keyboard.IsKeyDown(Keys.LeftShift) || keyboard.IsKeyDown(Keys.RightShift);
        var alt = keyboard.IsKeyDown(Keys.LeftAlt) || keyboard.IsKeyDown(Keys.RightAlt);
        var super = keyboard.IsKeyDown(Keys.LeftSuper) || keyboard.IsKeyDown(Keys.RightSuper);
        io.AddKeyEvent(ImGuiKey.ModCtrl, ctrl);
        io.AddKeyEvent(ImGuiKey.ModShift, shift);
        io.AddKeyEvent(ImGuiKey.ModAlt, alt);
        io.AddKeyEvent(ImGuiKey.ModSuper, super);
    }

    private unsafe void RenderDrawData(ImDrawDataPtr drawData)
    {
        if (drawData.CmdListsCount == 0)
        {
            return;
        }

        var fbWidth = (int)(drawData.DisplaySize.X * drawData.FramebufferScale.X);
        var fbHeight = (int)(drawData.DisplaySize.Y * drawData.FramebufferScale.Y);
        if (fbWidth <= 0 || fbHeight <= 0)
        {
            return;
        }

        drawData.ScaleClipRects(drawData.FramebufferScale);

        GL.Viewport(0, 0, fbWidth, fbHeight);
        GL.Enable(EnableCap.Blend);
        GL.BlendEquation(BlendEquationMode.FuncAdd);
        GL.BlendFunc(BlendingFactor.SrcAlpha, BlendingFactor.OneMinusSrcAlpha);
        GL.Disable(EnableCap.CullFace);
        GL.Disable(EnableCap.DepthTest);
        GL.Enable(EnableCap.ScissorTest);

        GL.UseProgram(_shader);
        GL.Uniform1(_attribLocationTex, 0);

        var l = drawData.DisplayPos.X;
        var r = drawData.DisplayPos.X + drawData.DisplaySize.X;
        var t = drawData.DisplayPos.Y;
        var b = drawData.DisplayPos.Y + drawData.DisplaySize.Y;
        var orthoProjection = new float[]
        {
            2.0f / (r - l), 0.0f, 0.0f, 0.0f,
            0.0f, 2.0f / (t - b), 0.0f, 0.0f,
            0.0f, 0.0f, -1.0f, 0.0f,
            (r + l) / (l - r), (t + b) / (b - t), 0.0f, 1.0f
        };
        GL.UniformMatrix4(_attribLocationProjMtx, 1, false, orthoProjection);

        GL.BindVertexArray(_vertexArray);
        GL.ActiveTexture(TextureUnit.Texture0);

        for (var n = 0; n < drawData.CmdListsCount; n++)
        {
            var cmdList = drawData.CmdLists[n];

            GL.BindBuffer(BufferTarget.ArrayBuffer, _vertexBuffer);
            GL.BufferData(
                BufferTarget.ArrayBuffer,
                cmdList.VtxBuffer.Size * sizeof(ImDrawVert),
                cmdList.VtxBuffer.Data,
                BufferUsageHint.StreamDraw);

            GL.BindBuffer(BufferTarget.ElementArrayBuffer, _indexBuffer);
            GL.BufferData(
                BufferTarget.ElementArrayBuffer,
                cmdList.IdxBuffer.Size * sizeof(ushort),
                cmdList.IdxBuffer.Data,
                BufferUsageHint.StreamDraw);

            var idxOffset = 0;
            for (var cmdIndex = 0; cmdIndex < cmdList.CmdBuffer.Size; cmdIndex++)
            {
                var pcmd = cmdList.CmdBuffer[cmdIndex];
                if (pcmd.UserCallback != IntPtr.Zero)
                {
                    throw new NotSupportedException("ImGui user callbacks are not supported in this minimal sample.");
                }

                var clip = pcmd.ClipRect;
                GL.Scissor(
                    (int)clip.X,
                    (int)(fbHeight - clip.W),
                    (int)(clip.Z - clip.X),
                    (int)(clip.W - clip.Y));

                GL.BindTexture(TextureTarget.Texture2D, (int)pcmd.TextureId);
                GL.DrawElementsBaseVertex(
                    PrimitiveType.Triangles,
                    (int)pcmd.ElemCount,
                    DrawElementsType.UnsignedShort,
                    (IntPtr)(idxOffset * sizeof(ushort)),
                    (int)pcmd.VtxOffset);

                idxOffset += (int)pcmd.ElemCount;
            }
        }

        GL.Disable(EnableCap.ScissorTest);
        GL.BindVertexArray(0);
        GL.UseProgram(0);
    }

    private unsafe void CreateFontTexture()
    {
        var io = ImGui.GetIO();
        io.Fonts.GetTexDataAsRGBA32(out IntPtr pixels, out var width, out var height, out _);

        _fontTexture = GL.GenTexture();
        GL.BindTexture(TextureTarget.Texture2D, _fontTexture);
        GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMinFilter, (int)TextureMinFilter.Linear);
        GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMagFilter, (int)TextureMagFilter.Linear);
        GL.TexImage2D(
            TextureTarget.Texture2D,
            0,
            PixelInternalFormat.Rgba,
            width,
            height,
            0,
            PixelFormat.Rgba,
            PixelType.UnsignedByte,
            pixels);

        io.Fonts.SetTexID((IntPtr)_fontTexture);
        io.Fonts.ClearTexData();
    }

    public void Dispose()
    {
        ImGui.SetCurrentContext(_context);
        ImGui.DestroyContext(_context);

        if (_fontTexture != 0)
        {
            GL.DeleteTexture(_fontTexture);
        }

        GL.DeleteBuffer(_vertexBuffer);
        GL.DeleteBuffer(_indexBuffer);
        GL.DeleteVertexArray(_vertexArray);
        GL.DeleteProgram(_shader);
        GL.DeleteShader(_vertexShader);
        GL.DeleteShader(_fragmentShader);
    }

    private const string VertexSource = """
        #version 330 core
        uniform mat4 ProjMtx;
        layout (location = 0) in vec2 Position;
        layout (location = 1) in vec2 UV;
        layout (location = 2) in vec4 Color;
        out vec2 Frag_UV;
        out vec4 Frag_Color;
        void main()
        {
            Frag_UV = UV;
            Frag_Color = Color;
            gl_Position = ProjMtx * vec4(Position.xy, 0.0, 1.0);
        }
        """;

    private const string FragmentSource = """
        #version 330 core
        uniform sampler2D Texture;
        in vec2 Frag_UV;
        in vec4 Frag_Color;
        out vec4 Out_Color;
        void main()
        {
            Out_Color = Frag_Color * texture(Texture, Frag_UV.st);
        }
        """;
}

internal sealed class DxgiDesktopDuplicator : IDisposable
{
    private readonly ID3D11Device _device;
    private readonly ID3D11DeviceContext _context;
    private readonly IDXGIOutputDuplication _duplication;
    private readonly ID3D11Texture2D _stagingTexture;
    private readonly byte[] _frameBuffer;
    private readonly int _width;
    private readonly int _height;
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

                    var outputDesc = output.Description;
                    _width = outputDesc.DesktopCoordinates.Right - outputDesc.DesktopCoordinates.Left;
                    _height = outputDesc.DesktopCoordinates.Bottom - outputDesc.DesktopCoordinates.Top;
                    if (_width <= 0 || _height <= 0)
                    {
                        throw new InvalidOperationException("DXGI output size is invalid.");
                    }

                    _duplication = output1.DuplicateOutput(_device);

                    var textureDesc = new Texture2DDescription
                    {
                        Width = (uint)_width,
                        Height = (uint)_height,
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
                    _frameBuffer = new byte[_width * _height * 4];
                }
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
                var rowBytes = _width * 4;
                for (var y = 0; y < _height; y++)
                {
                    var source = new IntPtr(mapped.DataPointer + y * mapped.RowPitch);
                    var destination = y * rowBytes;
                    System.Runtime.InteropServices.Marshal.Copy(source, _frameBuffer, destination, rowBytes);
                }
            }
            finally
            {
                _context.Unmap(_stagingTexture, 0);
            }

            frameData = _frameBuffer;
            width = _width;
            height = _height;
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

internal static class ResourceAssets
{
    private static readonly Assembly Assembly = typeof(ResourceAssets).Assembly;
    private static readonly string ExtractRoot = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "apex-imgui",
        "embedded-assets");

    public static string ExtractToTemp(string fileName)
    {
        var bytes = GetBytes(fileName);
        Directory.CreateDirectory(ExtractRoot);

        var targetPath = Path.Combine(ExtractRoot, fileName);
        if (File.Exists(targetPath))
        {
            var existing = File.ReadAllBytes(targetPath);
            if (existing.AsSpan().SequenceEqual(bytes))
            {
                return targetPath;
            }
        }

        File.WriteAllBytes(targetPath, bytes);
        return targetPath;
    }

    public static byte[] GetBytes(string fileName)
    {
        var resourceName = Assembly
            .GetManifestResourceNames()
            .FirstOrDefault(name => name.EndsWith(fileName, StringComparison.OrdinalIgnoreCase));

        if (resourceName is null)
        {
            throw new FileNotFoundException($"Embedded resource not found: {fileName}");
        }

        using var stream = Assembly.GetManifestResourceStream(resourceName)
            ?? throw new FileNotFoundException($"Embedded resource stream missing: {resourceName}");
        using var memory = new MemoryStream();
        stream.CopyTo(memory);
        return memory.ToArray();
    }

    public static WindowIcon LoadWindowIcon()
    {
        var iconBytes = GetBytes("3mz_ds_ver.png");
        var decoded = ImageResult.FromMemory(iconBytes, ColorComponents.RedGreenBlueAlpha);
        var iconImage = new OpenTK.Windowing.Common.Input.Image(decoded.Width, decoded.Height, decoded.Data);
        return new WindowIcon(iconImage);
    }
}
