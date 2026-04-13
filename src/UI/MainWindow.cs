using System.Diagnostics;
using System.Numerics;
using ImGuiNET;
using OpenTK.Graphics.OpenGL4;
using OpenTK.Windowing.Common;
using OpenTK.Windowing.Desktop;
using OpenTK.Windowing.GraphicsLibraryFramework;

public sealed class MainWindow : GameWindow
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
    private bool _wisdomCoreEnabled;
    private OnnxDmlWorker? _onnxWorker;
    private string _onnxStatus = "未启动";
    private OnnxInferenceSnapshot _onnxSnapshot;
    private int _onnxLastFrameId;
    private byte[] _onnxUploadBuffer = Array.Empty<byte>();

    public MainWindow(GameWindowSettings gameWindowSettings, NativeWindowSettings nativeWindowSettings)
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

    private void DrawUi()
    {
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

    private void DrawHomeTab()
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

        ImGui.TextUnformatted("选择配置");
        ImGui.SameLine();
        var topPanelStyle = ImGui.GetStyle();
        var addButtonWidth = ImGui.CalcTextSize("添加").X + topPanelStyle.FramePadding.X * 2f;
        var deleteButtonWidth = ImGui.CalcTextSize("删除").X + topPanelStyle.FramePadding.X * 2f;
        var configComboWidth = Math.Max(
            120f,
            ImGui.GetContentRegionAvail().X - addButtonWidth - deleteButtonWidth - topPanelStyle.ItemSpacing.X * 2f);
        DrawConfigFileCombo("##TopConfigCombo", configComboWidth);
        ImGui.SameLine();
        ImGui.Button("添加##ConfigAdd");
        ImGui.SameLine();
        ImGui.Button("删除##ConfigDelete");

        ImGui.Separator();

        var wisdomCoreLabel = _wisdomCoreEnabled ? "关闭智慧核心" : "启动智慧核心";
        var style = ImGui.GetStyle();
        var wisdomCoreButtonWidth = ImGui.CalcTextSize(wisdomCoreLabel).X + style.FramePadding.X * 2f;
        var wisdomCoreOffsetX = Math.Max(0f, (ImGui.GetContentRegionAvail().X - wisdomCoreButtonWidth) * 0.5f);
        if (wisdomCoreOffsetX > 0f)
        {
            ImGui.SetCursorPosX(ImGui.GetCursorPosX() + wisdomCoreOffsetX);
        }

        if (_wisdomCoreEnabled)
        {
            ImGui.PushStyleColor(ImGuiCol.Button, new Vector4(0.18f, 0.78f, 0.29f, 1f));
            ImGui.PushStyleColor(ImGuiCol.ButtonHovered, new Vector4(0.24f, 0.84f, 0.35f, 1f));
            ImGui.PushStyleColor(ImGuiCol.ButtonActive, new Vector4(0.12f, 0.66f, 0.23f, 1f));
            if (ImGui.Button(wisdomCoreLabel))
            {
                _wisdomCoreEnabled = false;
            }

            ImGui.PopStyleColor(3);
        }
        else
        {
            if (ImGui.Button(wisdomCoreLabel))
            {
                _wisdomCoreEnabled = true;
            }
        }

        ImGui.Separator();
        ImGui.Spacing();
        ImGui.Text("选择模型");
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

    private void DrawConfigFileCombo(string id, float width = -1f)
    {
        if (_configFiles.Count == 0)
        {
            ImGui.BeginDisabled();
            ImGui.SetNextItemWidth(width);
            ImGui.Combo(id, ref _selectedConfigFileIndex, "无可用配置\0");
            ImGui.EndDisabled();
            return;
        }

        _selectedConfigFileIndex = Math.Clamp(_selectedConfigFileIndex, 0, _configFiles.Count - 1);
        var selected = _configFiles[_selectedConfigFileIndex];

        ImGui.SetNextItemWidth(width);
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

    private void RefreshDpiScale()
    {
        if (_controller is null)
        {
            return;
        }

        var nextDpiScale = 1.0f;
        if (base.TryGetCurrentMonitorScale(out var scaleX, out var scaleY))
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

    private void OnStartDxgiClicked()
    {
        StartDxgiCapture();
    }

    private void OnStopDxgiClicked()
    {
        StopDxgiCapture("已停止");
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
}
