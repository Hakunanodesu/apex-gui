using System.Reflection;
using System.Numerics;
using System.Diagnostics;
using System.Threading;
using System.Text.Json;
using ImGuiNET;
using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;
using OpenTK.Graphics.OpenGL4;
using OpenTK.Windowing.Common;
using OpenTK.Windowing.Common.Input;
using OpenTK.Windowing.Desktop;
using OpenTK.Windowing.GraphicsLibraryFramework;
using StbImageSharp;
using Vortice.Direct3D;
using Vortice.Direct3D11;
using Vortice.DXGI;
using static Vortice.Direct3D11.D3D11;
using static Vortice.DXGI.DXGI;

RuntimePerformance.ConfigureProcessPriority();

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
    private const string ViGemBusInstallerUrl = "https://github.com/nefarius/ViGEmBus/releases/download/v1.22.0/ViGEmBus_1.22.0_x64_x86_arm64.exe";

    private ImGuiController? _controller;
    private float _dpiScale = 1.0f;

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
    private bool _dxgiPreviewEnabled = true;

    private readonly List<OnnxModelConfig> _onnxModels = new();
    private int _onnxTopSelectedModelIndex;
    private int _onnxDebugSelectedModelIndex;
    private readonly List<string> _configFiles = new();
    private int _selectedConfigFileIndex;
    private OnnxDmlWorker? _onnxWorker;
    private string _onnxStatus = "未启动";
    private OnnxInferenceSnapshot _onnxSnapshot;
    private int _onnxLastFrameId;
    private byte[] _onnxUploadBuffer = Array.Empty<byte>();

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
        RefreshOnnxModels();
        RefreshConfigFiles();
    }

    protected override void OnRenderFrame(FrameEventArgs args)
    {
        base.OnRenderFrame(args);
        if (_controller is null)
        {
            return;
        }

        RefreshDpiScale();
        if (_dxgiPreviewEnabled)
        {
            UpdateDxgiPreview((float)args.Time);
        }
        var dxgiTelemetry = _dxgiWorker?.GetTelemetrySnapshot() ?? default;
        _dxgiSampleBuffer.Clear();
        _dxgiWorker?.DrainCaptureSamples(_dxgiSampleBuffer);
        _dxgiPerfStats.PushSample((float)args.Time, dxgiTelemetry, _dxgiSampleBuffer);
        if (_dxgiPerfStats.TryBuildSnapshot(out var dxgiSnapshot))
        {
            _dxgiPerfSnapshot = dxgiSnapshot;
        }

        PumpOnnxFromDxgi();
        if (_onnxWorker is not null)
        {
            _onnxSnapshot = _onnxWorker.GetSnapshot();
            _onnxStatus = _onnxSnapshot.Status;
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
        DrawTopPanel();

        if (ImGui.BeginTabBar("RootTabs"))
        {
            if (ImGui.BeginTabItem("主页"))
            {
                DrawHomeTab();
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

    private void DrawTopPanel()
    {
        var vigemReady = Directory.Exists(@"C:\Program Files\Nefarius Software Solutions");
        ImGui.TextUnformatted("ViGemBus");
        ImGui.SameLine();
        if (vigemReady)
        {
            ImGui.TextColored(new Vector4(0.18f, 0.78f, 0.29f, 1f), "已就绪");
        }
        else
        {
            ImGui.TextColored(new Vector4(0.86f, 0.24f, 0.24f, 1f), "未就绪");
        }
        ImGui.SameLine();
        var vigemActionLabel = vigemReady ? "重新安装" : "安装";
        if (ImGui.Button(vigemActionLabel))
        {
            OpenViGemBusInstaller();
        }

        ImGui.Separator();

        ImGui.TextUnformatted("配置");
        ImGui.SameLine();
        DrawConfigFileCombo("##TopConfigCombo");
    }

    private static void OpenViGemBusInstaller()
    {
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = ViGemBusInstallerUrl,
                UseShellExecute = true
            };
            Process.Start(psi);
        }
        catch
        {
            // Ignore launcher failures to keep UI responsive.
        }
    }

    private void DrawHomeTab()
    {
        ImGui.Text("模型");
        ImGui.SameLine();
        DrawOnnxModelCombo("##HomeModelCombo", ref _onnxTopSelectedModelIndex);
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
            ref _dxgiPreviewEnabled,
            _dxgiPreviewTexture,
            _dxgiPreviewWidth,
            _dxgiPreviewHeight);

        ImGui.Separator();
        ImGui.Text("运行优先级");
        ImGui.Text($"进程优先级: {RuntimePerformance.GetProcessPriorityText()}");
        ImGui.Text($"DXGI GPU 优先级: {RuntimePerformance.DxgiGpuPriorityStatus}");

        ImGui.Separator();
        DrawOnnxDmlEpTab();
    }

    private void DrawOnnxDmlEpTab()
    {
        ImGui.Text("ONNX Runtime (DirectML EP) 推理调试");
        ImGui.Separator();
        ImGui.Text($"状态: {_onnxStatus}");

        if (ImGui.Button("刷新模型"))
        {
            RefreshOnnxModels();
        }

        if (_onnxModels.Count == 0)
        {
            ImGui.Text("Models 目录中未找到可用的 json+onnx 模型配置。");
            return;
        }

        ImGui.Text("模型选择");
        ImGui.SameLine();
        DrawOnnxModelCombo("##DebugModelCombo", ref _onnxDebugSelectedModelIndex);

        var selected = _onnxModels[Math.Clamp(_onnxDebugSelectedModelIndex, 0, _onnxModels.Count - 1)];
        ImGui.Text($"输入尺寸: {selected.InputWidth}x{selected.InputHeight}");
        ImGui.Text($"conf_thres: {selected.ConfThreshold:0.###}");
        ImGui.Text($"iou_thres: {selected.IouThreshold:0.###}");
        ImGui.Text($"classes: {selected.ClassesRaw}");

        if (_onnxWorker is null)
        {
            if (ImGui.Button("启动推理"))
            {
                StartOnnxInference(selected);
            }
        }
        else
        {
            if (ImGui.Button("停止推理"))
            {
                StopOnnxInference("已停止");
            }
        }

        ImGui.Separator();
        ImGui.Text($"推理帧率: {_onnxSnapshot.InferenceFps:0.0} fps");
        ImGui.Text($"推理耗时均值: {_onnxSnapshot.AvgInferenceMs:0.00} ms");
        ImGui.Text($"推理耗时 P95: {_onnxSnapshot.P95InferenceMs:0.00} ms");
        ImGui.Text($"推理耗时 P99: {_onnxSnapshot.P99InferenceMs:0.00} ms");
        ImGui.Text($"检测框数量: {_onnxSnapshot.DetectionCount}");
        ImGui.Text($"输出摘要: {_onnxSnapshot.OutputSummary}");
    }

    private void DrawOnnxModelCombo(string id, ref int selectedIndex)
    {
        if (_onnxModels.Count == 0)
        {
            ImGui.BeginDisabled();
            ImGui.SetNextItemWidth(-1);
            ImGui.Combo(id, ref selectedIndex, "无可用模型\0");
            ImGui.EndDisabled();
            return;
        }

        selectedIndex = Math.Clamp(selectedIndex, 0, _onnxModels.Count - 1);
        var selected = _onnxModels[selectedIndex];

        ImGui.SetNextItemWidth(-1);
        if (ImGui.BeginCombo(id, selected.DisplayName))
        {
            for (var i = 0; i < _onnxModels.Count; i++)
            {
                var isSelected = i == selectedIndex;
                if (ImGui.Selectable(_onnxModels[i].DisplayName, isSelected))
                {
                    selectedIndex = i;
                }

                if (isSelected)
                {
                    ImGui.SetItemDefaultFocus();
                }
            }

            ImGui.EndCombo();
        }
    }

    private void DrawConfigFileCombo(string id)
    {
        if (_configFiles.Count == 0)
        {
            ImGui.BeginDisabled();
            ImGui.SetNextItemWidth(-1);
            ImGui.Combo(id, ref _selectedConfigFileIndex, "无可用配置\0");
            ImGui.EndDisabled();
            return;
        }

        _selectedConfigFileIndex = Math.Clamp(_selectedConfigFileIndex, 0, _configFiles.Count - 1);
        var selected = _configFiles[_selectedConfigFileIndex];

        ImGui.SetNextItemWidth(-1);
        if (ImGui.BeginCombo(id, selected))
        {
            for (var i = 0; i < _configFiles.Count; i++)
            {
                var isSelected = i == _selectedConfigFileIndex;
                if (ImGui.Selectable(_configFiles[i], isSelected))
                {
                    _selectedConfigFileIndex = i;
                }

                if (isSelected)
                {
                    ImGui.SetItemDefaultFocus();
                }
            }

            ImGui.EndCombo();
        }
    }

    private void RefreshOnnxModels()
    {
        _onnxModels.Clear();
        var modelsDir = Path.Combine(AppContext.BaseDirectory, "Models");
        _onnxModels.AddRange(OnnxModelConfigLoader.LoadFromDirectory(modelsDir));

        _onnxModels.Sort((a, b) => string.Compare(a.DisplayName, b.DisplayName, StringComparison.OrdinalIgnoreCase));
        _onnxTopSelectedModelIndex = Math.Clamp(_onnxTopSelectedModelIndex, 0, Math.Max(0, _onnxModels.Count - 1));
        _onnxDebugSelectedModelIndex = Math.Clamp(_onnxDebugSelectedModelIndex, 0, Math.Max(0, _onnxModels.Count - 1));
    }

    private void RefreshConfigFiles()
    {
        var oldSelection = _configFiles.Count > 0 && _selectedConfigFileIndex >= 0 && _selectedConfigFileIndex < _configFiles.Count
            ? _configFiles[_selectedConfigFileIndex]
            : null;

        _configFiles.Clear();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var searchRoots = new[]
        {
            Path.Combine(AppContext.BaseDirectory, "Configs"),
            Path.Combine(Environment.CurrentDirectory, "Configs")
        };

        foreach (var root in searchRoots)
        {
            if (!Directory.Exists(root))
            {
                continue;
            }

            foreach (var jsonPath in Directory.EnumerateFiles(root, "*.json", SearchOption.TopDirectoryOnly))
            {
                var fileName = Path.GetFileNameWithoutExtension(jsonPath);
                if (!string.IsNullOrWhiteSpace(fileName) && seen.Add(fileName))
                {
                    _configFiles.Add(fileName);
                }
            }
        }

        _configFiles.Sort(StringComparer.OrdinalIgnoreCase);
        if (_configFiles.Count == 0)
        {
            _selectedConfigFileIndex = 0;
            return;
        }

        if (!string.IsNullOrWhiteSpace(oldSelection))
        {
            var oldIndex = _configFiles.FindIndex(name => string.Equals(name, oldSelection, StringComparison.OrdinalIgnoreCase));
            if (oldIndex >= 0)
            {
                _selectedConfigFileIndex = oldIndex;
                return;
            }
        }

        _selectedConfigFileIndex = Math.Clamp(_selectedConfigFileIndex, 0, _configFiles.Count - 1);
    }

    private void StartOnnxInference(OnnxModelConfig model)
    {
        try
        {
            StopOnnxInference("重启推理");
            if (_dxgiWorker is null)
            {
                StartDxgiCapture();
            }

            _dxgiWorker?.SetCaptureRegion(model.InputWidth, model.InputHeight);
            _onnxLastFrameId = 0;
            _onnxUploadBuffer = Array.Empty<byte>();
            _onnxWorker = new OnnxDmlWorker(model);
            _onnxStatus = "推理中";
        }
        catch (Exception ex)
        {
            _onnxStatus = $"启动失败: {ex.GetType().Name}: {ex.Message}";
            _onnxWorker = null;
        }
    }

    private void StopOnnxInference(string status)
    {
        _onnxWorker?.Dispose();
        _onnxWorker = null;
        _onnxStatus = status;
        _onnxSnapshot = default;
    }

    private void PumpOnnxFromDxgi()
    {
        if (_onnxWorker is null || _dxgiWorker is null)
        {
            return;
        }

        if (_dxgiWorker.TryCopyLatestFrame(ref _onnxUploadBuffer, ref _onnxLastFrameId, out var width, out var height, out var error))
        {
            _onnxWorker.SubmitFrame(_onnxUploadBuffer, width, height, _onnxLastFrameId);
        }
        else if (!string.IsNullOrWhiteSpace(error))
        {
            StopOnnxInference($"推理输入错误: {error}");
        }
    }

    private void DrawCapturePanel(
        string title,
        string status,
        PerfSnapshot perfSnapshot,
        bool isRunning,
        Action onStart,
        Action onStop,
        ref bool previewEnabled,
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
        ImGui.SameLine();
        ImGui.Checkbox($"显示预览##{title}", ref previewEnabled);

        ImGui.Separator();
        ImGui.Text("预览:");

        if (!previewEnabled)
        {
            ImGui.Text("预览已关闭");
            return;
        }

        if (previewTexture != 0 && previewWidth > 0 && previewHeight > 0)
        {
            var previewSize = new Vector2(previewWidth, previewHeight);
            ImGui.Image((IntPtr)previewTexture, previewSize, new Vector2(0, 0), new Vector2(1, 1));
        }
        else
        {
            ImGui.Text("暂无画面");
        }
    }

    private void OnStartDxgiClicked()
    {
        StartDxgiCapture();
    }

    private void OnStopDxgiClicked()
    {
        StopDxgiCapture("已停止");
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
        StopOnnxInference("已释放");
        StopDxgiCapture("已释放");
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
        var avgCaptureMs = _captureSuccessCount > 0 ? _captureMsSum / _captureSuccessCount : 0.0;
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
            using var dxgiDevice = device.QueryInterface<IDXGIDevice>();
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

    public void SetCaptureRegion(int width, int height)
    {
        lock (_sync)
        {
            _requestedCaptureWidth = Math.Max(1, width);
            _requestedCaptureHeight = Math.Max(1, height);
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
                int requestedWidth;
                int requestedHeight;
                lock (_sync)
                {
                    requestedWidth = _requestedCaptureWidth;
                    requestedHeight = _requestedCaptureHeight;
                }

                duplicator.SetCaptureRegion(requestedWidth, requestedHeight);
                timer.Restart();
                var ok = duplicator.TryCaptureFrame(1, out var frameData, out var width, out var height, out var error);
                timer.Stop();
                var shouldBackoff = false;

                lock (_sync)
                {
                    _pollCount++;
                    var elapsedMs = timer.Elapsed.TotalMilliseconds;

                    if (ok)
                    {
                        _successCount++;
                        _captureMsSum += elapsedMs;
                        _captureMsMax = Math.Max(_captureMsMax, elapsedMs);
                        _pendingCaptureMs.Enqueue(elapsedMs);
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
                    else
                    {
                        shouldBackoff = true;
                    }
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

internal readonly struct OnnxModelConfig
{
    public readonly string DisplayName;
    public readonly string JsonPath;
    public readonly string OnnxPath;
    public readonly int InputWidth;
    public readonly int InputHeight;
    public readonly float ConfThreshold;
    public readonly float IouThreshold;
    public readonly string ClassesRaw;
    public readonly HashSet<int> AllowedClasses;

    public OnnxModelConfig(
        string displayName,
        string jsonPath,
        string onnxPath,
        int inputWidth,
        int inputHeight,
        float confThreshold,
        float iouThreshold,
        string classesRaw,
        HashSet<int> allowedClasses)
    {
        DisplayName = displayName;
        JsonPath = jsonPath;
        OnnxPath = onnxPath;
        InputWidth = inputWidth;
        InputHeight = inputHeight;
        ConfThreshold = confThreshold;
        IouThreshold = iouThreshold;
        ClassesRaw = classesRaw;
        AllowedClasses = allowedClasses;
    }
}

internal static class OnnxModelConfigLoader
{
    public static List<OnnxModelConfig> LoadFromDirectory(string directory)
    {
        var result = new List<OnnxModelConfig>();
        if (!Directory.Exists(directory))
        {
            return result;
        }

        foreach (var jsonPath in Directory.GetFiles(directory, "*.json", SearchOption.TopDirectoryOnly))
        {
            if (TryLoadSingle(jsonPath, out var model))
            {
                result.Add(model);
            }
        }

        return result;
    }

    private static bool TryLoadSingle(string jsonPath, out OnnxModelConfig model)
    {
        model = default;
        try
        {
            var onnxPath = Path.ChangeExtension(jsonPath, ".onnx");
            if (!File.Exists(onnxPath))
            {
                return false;
            }

            using var doc = JsonDocument.Parse(File.ReadAllText(jsonPath));
            var root = doc.RootElement;
            if (!root.TryGetProperty("size", out var sizeEl))
            {
                return false;
            }

            var size = sizeEl.GetInt32();
            if (size <= 0)
            {
                return false;
            }

            var conf = root.TryGetProperty("conf_thres", out var confEl) ? confEl.GetSingle() : 0.25f;
            var iou = root.TryGetProperty("iou_thres", out var iouEl) ? iouEl.GetSingle() : 0.45f;
            var classesRaw = root.TryGetProperty("classes", out var classesEl) ? classesEl.ToString() : string.Empty;
            var allowed = ParseClasses(classesRaw);
            model = new OnnxModelConfig(
                Path.GetFileNameWithoutExtension(jsonPath),
                jsonPath,
                onnxPath,
                size,
                size,
                conf,
                iou,
                classesRaw,
                allowed);
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static HashSet<int> ParseClasses(string raw)
    {
        var set = new HashSet<int>();
        if (string.IsNullOrWhiteSpace(raw))
        {
            return set;
        }

        var parts = raw.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        foreach (var part in parts)
        {
            if (int.TryParse(part, out var value))
            {
                set.Add(value);
            }
        }

        return set;
    }
}

internal readonly struct OnnxInferenceSnapshot
{
    public readonly string Status;
    public readonly double InferenceFps;
    public readonly double AvgInferenceMs;
    public readonly double P95InferenceMs;
    public readonly double P99InferenceMs;
    public readonly int DetectionCount;
    public readonly string OutputSummary;

    public OnnxInferenceSnapshot(
        string status,
        double inferenceFps,
        double avgInferenceMs,
        double p95InferenceMs,
        double p99InferenceMs,
        int detectionCount,
        string outputSummary)
    {
        Status = status;
        InferenceFps = inferenceFps;
        AvgInferenceMs = avgInferenceMs;
        P95InferenceMs = p95InferenceMs;
        P99InferenceMs = p99InferenceMs;
        DetectionCount = detectionCount;
        OutputSummary = outputSummary;
    }
}

internal sealed class OnnxDmlWorker : IDisposable
{
    private readonly object _sync = new();
    private readonly Thread _thread;
    private readonly AutoResetEvent _frameArrived = new(false);
    private readonly OnnxModelConfig _model;

    private bool _running = true;
    private byte[] _latestFrame = Array.Empty<byte>();
    private int _latestFrameWidth;
    private int _latestFrameHeight;
    private int _latestFrameId;
    private int _lastProcessedFrameId;

    private readonly List<double> _windowSamples = new(256);
    private DateTime _windowStartUtc = DateTime.UtcNow;
    private long _windowInferenceCount;

    private OnnxInferenceSnapshot _snapshot = new(
        "未启动",
        0.0,
        0.0,
        0.0,
        0.0,
        0,
        "无");

    public OnnxDmlWorker(OnnxModelConfig model)
    {
        _model = model;
        _thread = new Thread(WorkerMain)
        {
            IsBackground = true,
            Name = "ONNX-DML-Worker"
        };
        _thread.Start();
    }

    public void SubmitFrame(byte[] frameData, int width, int height, int frameId)
    {
        lock (_sync)
        {
            if (!_running)
            {
                return;
            }

            if (_latestFrame.Length != frameData.Length)
            {
                _latestFrame = new byte[frameData.Length];
            }

            System.Buffer.BlockCopy(frameData, 0, _latestFrame, 0, frameData.Length);
            _latestFrameWidth = width;
            _latestFrameHeight = height;
            _latestFrameId = frameId;
        }

        _frameArrived.Set();
    }

    public OnnxInferenceSnapshot GetSnapshot()
    {
        lock (_sync)
        {
            return _snapshot;
        }
    }

    private void WorkerMain()
    {
        try
        {
            using var options = new SessionOptions();
            options.AppendExecutionProvider_DML();
            using var session = new InferenceSession(_model.OnnxPath, options);

            var input = session.InputMetadata.First();
            var inputName = input.Key;
            var inputDims = ResolveInputShape(input.Value.Dimensions, _model.InputHeight, _model.InputWidth);
            var layout = DetectLayout(inputDims);

            SetStatus("推理中");
            while (_running)
            {
                _frameArrived.WaitOne(50);
                if (!_running)
                {
                    break;
                }

                byte[] frame;
                int frameWidth;
                int frameHeight;
                int frameId;
                lock (_sync)
                {
                    if (_latestFrameId == 0 || _latestFrameId == _lastProcessedFrameId)
                    {
                        continue;
                    }

                    frame = new byte[_latestFrame.Length];
                    System.Buffer.BlockCopy(_latestFrame, 0, frame, 0, _latestFrame.Length);
                    frameWidth = _latestFrameWidth;
                    frameHeight = _latestFrameHeight;
                    frameId = _latestFrameId;
                    _lastProcessedFrameId = frameId;
                }

                var sw = Stopwatch.StartNew();
                var inputData = Preprocess(frame, frameWidth, frameHeight, _model.InputWidth, _model.InputHeight, layout);
                var inputTensor = new DenseTensor<float>(inputData, inputDims);
                using var outputs = session.Run(new[] { NamedOnnxValue.CreateFromTensor(inputName, inputTensor) });
                sw.Stop();

                var outputSummary = BuildOutputSummary(outputs);
                var detectionCount = CountDetections(outputs, _model.ConfThreshold, _model.IouThreshold, _model.AllowedClasses);
                PushInferenceSample(sw.Elapsed.TotalMilliseconds, detectionCount, outputSummary);
            }
        }
        catch (Exception ex)
        {
            SetStatus($"推理失败: {ex.GetType().Name}: {ex.Message}");
            _running = false;
        }
    }

    private void SetStatus(string status)
    {
        lock (_sync)
        {
            _snapshot = new OnnxInferenceSnapshot(
                status,
                _snapshot.InferenceFps,
                _snapshot.AvgInferenceMs,
                _snapshot.P95InferenceMs,
                _snapshot.P99InferenceMs,
                _snapshot.DetectionCount,
                _snapshot.OutputSummary);
        }
    }

    private void PushInferenceSample(double ms, int detectionCount, string outputSummary)
    {
        lock (_sync)
        {
            _windowSamples.Add(ms);
            _windowInferenceCount++;
            var elapsed = DateTime.UtcNow - _windowStartUtc;
            if (elapsed.TotalSeconds >= 1.0)
            {
                var fps = _windowInferenceCount / elapsed.TotalSeconds;
                var avg = _windowSamples.Count > 0 ? _windowSamples.Average() : 0.0;
                var p95 = Percentile(_windowSamples, 0.95);
                var p99 = Percentile(_windowSamples, 0.99);
                _snapshot = new OnnxInferenceSnapshot("推理中", fps, avg, p95, p99, detectionCount, outputSummary);
                _windowSamples.Clear();
                _windowInferenceCount = 0;
                _windowStartUtc = DateTime.UtcNow;
            }
            else
            {
                _snapshot = new OnnxInferenceSnapshot(
                    _snapshot.Status,
                    _snapshot.InferenceFps,
                    _snapshot.AvgInferenceMs,
                    _snapshot.P95InferenceMs,
                    _snapshot.P99InferenceMs,
                    detectionCount,
                    outputSummary);
            }
        }
    }

    private static int[] ResolveInputShape(IReadOnlyList<int> dims, int height, int width)
    {
        var resolved = dims.Select(d => d <= 0 ? 1 : d).ToArray();
        if (resolved.Length != 4)
        {
            return new[] { 1, 3, height, width };
        }

        if (resolved[1] == 3 || resolved[1] == 1)
        {
            resolved[0] = 1;
            resolved[2] = height;
            resolved[3] = width;
            return resolved;
        }

        resolved[0] = 1;
        resolved[1] = height;
        resolved[2] = width;
        resolved[3] = 3;
        return resolved;
    }

    private static string DetectLayout(int[] dims)
    {
        if (dims.Length == 4 && dims[1] is 1 or 3)
        {
            return "NCHW";
        }

        return "NHWC";
    }

    private static float[] Preprocess(byte[] bgra, int srcW, int srcH, int dstW, int dstH, string layout)
    {
        var data = new float[dstW * dstH * 3];
        for (var y = 0; y < dstH; y++)
        {
            var sy = y * srcH / dstH;
            for (var x = 0; x < dstW; x++)
            {
                var sx = x * srcW / dstW;
                var srcIndex = (sy * srcW + sx) * 4;
                var b = bgra[srcIndex + 0] / 255f;
                var g = bgra[srcIndex + 1] / 255f;
                var r = bgra[srcIndex + 2] / 255f;

                if (layout == "NCHW")
                {
                    var pixel = y * dstW + x;
                    data[pixel] = r;
                    data[dstW * dstH + pixel] = g;
                    data[2 * dstW * dstH + pixel] = b;
                }
                else
                {
                    var pixel = (y * dstW + x) * 3;
                    data[pixel + 0] = r;
                    data[pixel + 1] = g;
                    data[pixel + 2] = b;
                }
            }
        }

        return data;
    }

    private static string BuildOutputSummary(IDisposableReadOnlyCollection<DisposableNamedOnnxValue> outputs)
    {
        var parts = new List<string>();
        foreach (var output in outputs.Take(3))
        {
            try
            {
                var tensor = output.AsTensor<float>();
                var shape = string.Join("x", tensor.Dimensions.ToArray().Select(d => d.ToString()));
                parts.Add($"{output.Name}:{shape}");
            }
            catch
            {
                parts.Add($"{output.Name}:non-float");
            }
        }

        return parts.Count == 0 ? "无输出" : string.Join(", ", parts);
    }

    private static int CountDetections(
        IDisposableReadOnlyCollection<DisposableNamedOnnxValue> outputs,
        float confThres,
        float iouThres,
        HashSet<int> allowedClasses)
    {
        foreach (var output in outputs)
        {
            Tensor<float>? tensor;
            try
            {
                tensor = output.AsTensor<float>();
            }
            catch
            {
                continue;
            }

            if (tensor.Rank != 3)
            {
                continue;
            }

            var dims = tensor.Dimensions.ToArray();
            var values = tensor.ToArray();
            if (TryParseDetections(values, dims, confThres, iouThres, allowedClasses, out var count))
            {
                return count;
            }
        }

        return 0;
    }

    private static bool TryParseDetections(
        float[] values,
        int[] dims,
        float confThres,
        float iouThres,
        HashSet<int> allowedClasses,
        out int count)
    {
        count = 0;
        if (dims.Length != 3)
        {
            return false;
        }

        int rows;
        int cols;
        bool transposed;
        if (dims[2] >= 6)
        {
            rows = dims[1];
            cols = dims[2];
            transposed = false;
        }
        else if (dims[1] >= 6)
        {
            rows = dims[2];
            cols = dims[1];
            transposed = true;
        }
        else
        {
            return false;
        }

        var boxes = new List<(float x, float y, float w, float h, float score)>();
        for (var i = 0; i < rows; i++)
        {
            int baseIndex;
            if (!transposed)
            {
                baseIndex = i * cols;
            }
            else
            {
                baseIndex = i;
            }

            float Read(int c) => !transposed ? values[baseIndex + c] : values[c * rows + baseIndex];

            var obj = Read(4);
            if (obj <= 0f)
            {
                continue;
            }

            var bestClassScore = 0f;
            var bestClass = -1;
            for (var c = 5; c < cols; c++)
            {
                var cls = Read(c);
                if (cls > bestClassScore)
                {
                    bestClassScore = cls;
                    bestClass = c - 5;
                }
            }

            if (bestClass >= 0 && allowedClasses.Count > 0 && !allowedClasses.Contains(bestClass))
            {
                continue;
            }

            var score = obj * bestClassScore;
            if (score < confThres)
            {
                continue;
            }

            boxes.Add((Read(0), Read(1), Math.Abs(Read(2)), Math.Abs(Read(3)), score));
        }

        if (boxes.Count == 0)
        {
            return true;
        }

        boxes.Sort((a, b) => b.score.CompareTo(a.score));
        var kept = new List<(float x, float y, float w, float h, float score)>();
        foreach (var box in boxes)
        {
            var suppressed = false;
            foreach (var keptBox in kept)
            {
                if (ComputeIoU(box, keptBox) > iouThres)
                {
                    suppressed = true;
                    break;
                }
            }

            if (!suppressed)
            {
                kept.Add(box);
            }
        }

        count = kept.Count;
        return true;
    }

    private static float ComputeIoU(
        (float x, float y, float w, float h, float score) a,
        (float x, float y, float w, float h, float score) b)
    {
        var ax1 = a.x - a.w * 0.5f;
        var ay1 = a.y - a.h * 0.5f;
        var ax2 = a.x + a.w * 0.5f;
        var ay2 = a.y + a.h * 0.5f;

        var bx1 = b.x - b.w * 0.5f;
        var by1 = b.y - b.h * 0.5f;
        var bx2 = b.x + b.w * 0.5f;
        var by2 = b.y + b.h * 0.5f;

        var interX1 = Math.Max(ax1, bx1);
        var interY1 = Math.Max(ay1, by1);
        var interX2 = Math.Min(ax2, bx2);
        var interY2 = Math.Min(ay2, by2);
        var interW = Math.Max(0f, interX2 - interX1);
        var interH = Math.Max(0f, interY2 - interY1);
        var interArea = interW * interH;
        var union = a.w * a.h + b.w * b.h - interArea;
        if (union <= 0f)
        {
            return 0f;
        }

        return interArea / union;
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

    public void Dispose()
    {
        _running = false;
        _frameArrived.Set();
        if (_thread.IsAlive)
        {
            _thread.Join(500);
        }
        _frameArrived.Dispose();
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
