using System.Numerics;
using ImGuiNET;
using OpenTK.Graphics.OpenGL4;

public sealed partial class MainWindow
{
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

    private int _onnxDebugSelectedModelIndex;
    private OnnxDmlWorker? _onnxWorker;
    private string _onnxStatus = "未启动";
    private OnnxInferenceSnapshot _onnxSnapshot;
    private int _onnxLastFrameId;
    private byte[] _onnxUploadBuffer = Array.Empty<byte>();

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

        ImGui.Separator();
        DrawDebugGamepadCombo();

        ImGui.Separator();
        DrawViGEmVirtualGamepadPanel();
    }

    private void DrawDebugGamepadCombo()
    {
        var gamepads = GetConnectedGamepadOptions();
        var hasGamepads = gamepads.Length > 0;
        _debugSelectedGamepadIndex = hasGamepads
            ? (_debugSelectedGamepadIndex >= 0 && _debugSelectedGamepadIndex < gamepads.Length ? _debugSelectedGamepadIndex : 0)
            : -1;

        ImGui.Text("输入设备(SDL3)");
        ImGui.SameLine();
        ImGui.TextDisabled(hasGamepads ? "已就绪" : "未检测到手柄");

        var style = ImGui.GetStyle();
        var refreshButtonWidth = ImGui.CalcTextSize("刷新").X + style.FramePadding.X * 2f;
        ImGui.SetNextItemWidth(ImGui.GetContentRegionAvail().X - refreshButtonWidth - style.ItemSpacing.X);
        ImGui.Combo("##DebugInputDeviceCombo", ref _debugSelectedGamepadIndex, gamepads, gamepads.Length);
        ImGui.SameLine();
        if (ImGui.Button("刷新##DebugInputDeviceRefresh"))
        {
            RefreshDebugInputDevices();
        }
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
        DrawDebugModelCombo("##DebugModelCombo");

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

    private void DrawViGEmVirtualGamepadPanel()
    {
        ImGui.Text("虚拟手柄(ViGEm)");
        ImGui.Separator();

        if (_viGEmMappingWorker is null)
        {
            ImGui.Text("状态: 未创建");
            if (!string.IsNullOrWhiteSpace(_smartCoreMappingState.LastError))
            {
                ImGui.Text($"错误: {_smartCoreMappingState.LastError}");
            }
            return;
        }

        ImGui.Text($"状态: {_viGEmMappingWorker.Status}");
        ImGui.SameLine();
        ImGui.TextDisabled(_viGEmMappingWorker.IsConnected ? "已连接" : "未连接");

        if (ImGui.Button("重新连接"))
        {
            _smartCoreMappingState.LastError = string.Empty;
            _viGEmMappingWorker.ConnectVirtualGamepad();
        }

        ImGui.SameLine();
        if (ImGui.Button("断开并释放"))
        {
            _smartCoreMappingState.LastError = string.Empty;
            _viGEmMappingWorker.DisconnectVirtualGamepad();
        }

        if (!string.IsNullOrWhiteSpace(_smartCoreMappingState.LastError))
        {
            ImGui.Text($"错误: {_smartCoreMappingState.LastError}");
        }
    }

    // Debug 页面模型选择必须保持独立：
    // - 不读取 Configs/*.json 中的 model 字段
    // - 不写入当前配置文件
    // - 不受“无配置文件时禁用主页模型选择”的约束
    // 该方法仅维护调试页自己的 _onnxDebugSelectedModelIndex。
    private void DrawDebugModelCombo(string id)
    {
        if (_onnxModels.Count == 0)
        {
            ImGui.BeginDisabled();
            ImGui.SetNextItemWidth(-1);
            ImGui.Combo(id, ref _onnxDebugSelectedModelIndex, "无可用模型\0");
            ImGui.EndDisabled();
            return;
        }

        _onnxDebugSelectedModelIndex = Math.Clamp(_onnxDebugSelectedModelIndex, 0, _onnxModels.Count - 1);
        var selected = _onnxModels[_onnxDebugSelectedModelIndex];

        ImGui.SetNextItemWidth(-1);
        if (ImGui.BeginCombo(id, selected.DisplayName))
        {
            for (var i = 0; i < _onnxModels.Count; i++)
            {
                var isSelected = i == _onnxDebugSelectedModelIndex;
                if (ImGui.Selectable(_onnxModels[i].DisplayName, isSelected))
                {
                    _onnxDebugSelectedModelIndex = i;
                }

                if (isSelected)
                {
                    ImGui.SetItemDefaultFocus();
                }
            }

            ImGui.EndCombo();
        }
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
