using System.Reflection;
using System.Numerics;
using System.Diagnostics;
using System.Threading;
using System.Text.Json;
using System.Text.Json.Nodes;
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
using static Vortice.Direct3D11.D3D11;
using static Vortice.DXGI.DXGI;

public sealed partial class MainWindow : GameWindow
{
    private const string ViGemBusInstallerUrl = "https://github.com/nefarius/ViGEmBus/releases/download/v1.22.0/ViGEmBus_1.22.0_x64_x86_arm64.exe";
    private const int DefaultSnapOuterRange = 1;
    private const int DefaultSnapInnerRange = 1;
    private const float DefaultSnapOuterStrength = 0f;
    private const float DefaultSnapInnerStrength = 0f;
    private const float DefaultSnapStartStrength = 0f;
    private const float DefaultSnapVerticalStrengthFactor = 1f;
    private const float DefaultSnapHipfireStrengthFactor = 1f;
    private const float DefaultSnapHeight = 0f;

    private ImGuiController? _controller;
    private float _dpiScale = 1.0f;

    private readonly List<OnnxModelConfig> _onnxModels = new();
    private int _onnxTopSelectedModelIndex = -1;
    private static readonly string[] HomeSnapModeOptions = { "开火吸附", "瞄准吸附" };
    private int _homeSnapModeIndex = -1;
    private readonly List<string> _configFiles = new();
    private int _selectedConfigFileIndex;
    private string _addConfigNameBuffer = string.Empty;
    private string _configAddModalError = string.Empty;
    private bool _configAddModalOpen;
    private bool _configDeleteModalOpen;
    private bool _configAddModalOpenRequested;
    private bool _configDeleteModalOpenRequested;
    private string? _pendingDeleteConfigBaseName;
    private bool _smartCoreEnabled;
    private int _snapOuterRange = DefaultSnapOuterRange;
    private float _snapOuterStrength = DefaultSnapOuterStrength;
    private int _snapInnerRange = DefaultSnapInnerRange;
    private float _snapInnerStrength = DefaultSnapInnerStrength;
    private float _snapStartStrength = DefaultSnapStartStrength;
    private float _snapVerticalStrengthFactor = DefaultSnapVerticalStrengthFactor;
    private float _snapHipfireStrengthFactor = DefaultSnapHipfireStrengthFactor;
    private float _snapHeight = DefaultSnapHeight;
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
                DrawHomeTab();
                ImGui.EndTabItem();
            }

#if DEBUG
            if (ImGui.BeginTabItem("调试"))
            {
                DrawDxgiTab();
                ImGui.EndTabItem();
            }
#endif

            ImGui.EndTabBar();
        }

        ImGui.End();
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
        var vigemReady = Directory.Exists(@"C:\Program Files\Nefarius Software Solutions");
        var topPanelStyle = ImGui.GetStyle();
        var addButtonWidth = ImGui.CalcTextSize("添加").X + topPanelStyle.FramePadding.X * 2f;
        var deleteButtonWidth = ImGui.CalcTextSize("删除").X + topPanelStyle.FramePadding.X * 2f;
        var reserveWidth = addButtonWidth + deleteButtonWidth + topPanelStyle.ItemSpacing.X * 2f;

        // 修改这里的文本即可统一控制所有第一列宽度。
        var firstColumnWidth = ImGui.CalcTextSize("一二三四五六 ").X;

        if (ImGui.BeginTable("##HomeTopTable", 2, ImGuiTableFlags.SizingStretchProp))
        {
            ImGui.TableSetupColumn("Label", ImGuiTableColumnFlags.WidthFixed, firstColumnWidth);
            ImGui.TableSetupColumn("Content", ImGuiTableColumnFlags.WidthStretch);

            ImGui.TableNextRow();
            ImGui.TableSetColumnIndex(0);
            ImGui.TextUnformatted("依赖状态");
            ImGui.TableSetColumnIndex(1);
            ImGui.TextUnformatted("ViGemBus");
            ImGui.SameLine();
            ImGui.TextUnformatted(vigemReady ? "已就绪" : "未就绪");
            ImGui.SameLine();
            var vigemActionLabel = vigemReady ? "重新安装" : "安装";
            if (ImGui.Button(vigemActionLabel))
            {
                OpenViGemBusInstaller();
            }

            ImGui.TableNextRow();
            ImGui.TableSetColumnIndex(0);
            ImGui.TextUnformatted("配置选择");
            ImGui.TableSetColumnIndex(1);
            var configComboWidth = ImGui.GetContentRegionAvail().X - reserveWidth;
            DrawConfigFileCombo("##TopConfigCombo", configComboWidth);
            ImGui.SameLine();
            if (ImGui.Button("添加", new Vector2(addButtonWidth, 0f)))
            {
                _addConfigNameBuffer = string.Empty;
                _configAddModalError = string.Empty;
                _configAddModalOpen = true;
                _configAddModalOpenRequested = true;
            }
            ImGui.SameLine();
            if (_configFiles.Count > 0)
            {
                if (ImGui.Button("删除", new Vector2(deleteButtonWidth, 0f)))
                {
                    _pendingDeleteConfigBaseName = _configFiles[Math.Clamp(_selectedConfigFileIndex, 0, _configFiles.Count - 1)];
                    _configDeleteModalOpen = true;
                    _configDeleteModalOpenRequested = true;
                }
            }
            else
            {
                ImGui.BeginDisabled();
                ImGui.Button("删除", new Vector2(deleteButtonWidth, 0f));
                ImGui.EndDisabled();
            }

            ImGui.TableNextRow();
            ImGui.TableSetColumnIndex(0);
            ImGui.TextUnformatted("智慧核心");
            ImGui.TableSetColumnIndex(1);
            ImGui.Checkbox("##SmartCoreEnabled", ref _smartCoreEnabled);

            ImGui.EndTable();
        }

        DrawConfigFileModals();

        ImGui.Separator();
        ImGui.Separator();

        var modelLineStyle = ImGui.GetStyle();
        var refreshButtonWidth = ImGui.CalcTextSize("刷新").X + modelLineStyle.FramePadding.X * 2f;
        if (ImGui.BeginTable("##HomeMainTable", 2, ImGuiTableFlags.SizingStretchProp))
        {
            ImGui.TableSetupColumn("Label", ImGuiTableColumnFlags.WidthFixed, firstColumnWidth);
            ImGui.TableSetupColumn("Content", ImGuiTableColumnFlags.WidthStretch);

            ImGui.TableNextRow();
            ImGui.TableSetColumnIndex(0);
            ImGui.TextUnformatted("选择模型");
            ImGui.TableSetColumnIndex(1);
            var modelComboWidth = ImGui.GetContentRegionAvail().X - reserveWidth;
            DrawHomeModelCombo("##HomeModelCombo", modelComboWidth);
            ImGui.SameLine();
            if (ImGui.Button("刷新", new Vector2(refreshButtonWidth, 0f)))
            {
                RefreshOnnxModels();
            }

            ImGui.TableNextRow();
            ImGui.TableSetColumnIndex(0);
            ImGui.TextUnformatted("开启吸附方式");
            ImGui.TableSetColumnIndex(1);
            var selectedSnapModeLabel = _homeSnapModeIndex >= 0 && _homeSnapModeIndex < HomeSnapModeOptions.Length
                ? HomeSnapModeOptions[_homeSnapModeIndex]
                : string.Empty;
            var snapComboWidth = ImGui.GetContentRegionAvail().X - reserveWidth;
            ImGui.SetNextItemWidth(snapComboWidth);
            ImGui.BeginDisabled(_configFiles.Count == 0);
            if (ImGui.BeginCombo("##HomeSnapModeCombo", selectedSnapModeLabel))
            {
                for (var i = 0; i < HomeSnapModeOptions.Length; i++)
                {
                    var isSelected = i == _homeSnapModeIndex;
                    if (ImGui.Selectable(HomeSnapModeOptions[i], isSelected))
                    {
                        _homeSnapModeIndex = i;
                        TryWriteStringValueToCurrentConfig("snap", HomeSnapModeOptions[i]);
                    }

                    if (isSelected)
                    {
                        ImGui.SetItemDefaultFocus();
                    }
                }

                ImGui.EndCombo();
            }
            ImGui.EndDisabled();

            ImGui.TableNextRow();
            ImGui.TableSetColumnIndex(0);
            ImGui.TextUnformatted("吸附参数设定");
            ImGui.TableSetColumnIndex(1);
            var selectedModelSize = _onnxTopSelectedModelIndex >= 0 && _onnxTopSelectedModelIndex < _onnxModels.Count
                ? Math.Max(1, _onnxModels[_onnxTopSelectedModelIndex].InputHeight)
                : 1;
            var displayHeightLimit = GetDisplayHeightOrWindowHeight();
            var snapOuterRangeMax = Math.Max(selectedModelSize, displayHeightLimit);
            _snapOuterRange = Math.Clamp(_snapOuterRange, selectedModelSize, snapOuterRangeMax);
            _snapInnerRange = Math.Clamp(_snapInnerRange, 1, _snapOuterRange);
            _snapOuterStrength = Math.Clamp(_snapOuterStrength, 0f, 1f);
            _snapInnerStrength = Math.Clamp(_snapInnerStrength, 0f, 1f);
            var snapRangeInputWidth = ImGui.CalcTextSize("0000").X + topPanelStyle.FramePadding.X * 2f;
            var snapStrengthInputWidth = snapRangeInputWidth + ImGui.GetFrameHeight() * 2f + topPanelStyle.ItemInnerSpacing.X * 2f;
            ImGui.TextUnformatted("外圈范围");
            ImGui.SameLine();
            ImGui.SetNextItemWidth(snapRangeInputWidth);
            if (ImGui.InputInt("##SnapOuterRange", ref _snapOuterRange, 0, 0))
            {
                _snapOuterRange = Math.Clamp(_snapOuterRange, selectedModelSize, snapOuterRangeMax);
                _snapInnerRange = Math.Clamp(_snapInnerRange, 1, _snapOuterRange);
                TryWriteIntValueToCurrentConfig("snapOuterRange", _snapOuterRange);
                TryWriteIntValueToCurrentConfig("snapInnerRange", _snapInnerRange);
            }
            ImGui.SameLine();
            ImGui.TextUnformatted("外圈强度");
            ImGui.SameLine();
            ImGui.SetNextItemWidth(snapStrengthInputWidth);
            if (ImGui.InputFloat("##SnapOuterStrength", ref _snapOuterStrength, 0.01f, 0.01f, "%.2f"))
            {
                _snapOuterStrength = Math.Clamp(_snapOuterStrength, 0f, 1f);
                TryWriteFloatValueToCurrentConfig("snapOuterStrength", _snapOuterStrength);
            }
            ImGui.TextUnformatted("内圈范围");
            ImGui.SameLine();
            ImGui.SetNextItemWidth(snapRangeInputWidth);
            if (ImGui.InputInt("##SnapInnerRange", ref _snapInnerRange, 0, 0))
            {
                _snapInnerRange = Math.Clamp(_snapInnerRange, 1, _snapOuterRange);
                TryWriteIntValueToCurrentConfig("snapInnerRange", _snapInnerRange);
            }
            ImGui.SameLine();
            ImGui.TextUnformatted("内圈强度");
            ImGui.SameLine();
            ImGui.SetNextItemWidth(snapStrengthInputWidth);
            if (ImGui.InputFloat("##SnapInnerStrength", ref _snapInnerStrength, 0.01f, 0.01f, "%.2f"))
            {
                _snapInnerStrength = Math.Clamp(_snapInnerStrength, 0f, 1f);
                TryWriteFloatValueToCurrentConfig("snapInnerStrength", _snapInnerStrength);
            }

            var snapExtraInputWidth = snapStrengthInputWidth;
            _snapStartStrength = Math.Clamp(_snapStartStrength, 0f, 1f);
            _snapVerticalStrengthFactor = Math.Clamp(_snapVerticalStrengthFactor, 0f, 1f);
            _snapHipfireStrengthFactor = Math.Clamp(_snapHipfireStrengthFactor, 0f, 1f);
            _snapHeight = Math.Clamp(_snapHeight, 0f, 1f);
            ImGui.TextUnformatted("起始强度");
            ImGui.SameLine();
            ImGui.SetNextItemWidth(snapExtraInputWidth);
            if (ImGui.InputFloat("##SnapStartStrength", ref _snapStartStrength, 0.01f, 0.01f, "%.2f"))
            {
                _snapStartStrength = Math.Clamp(_snapStartStrength, 0f, 1f);
                TryWriteFloatValueToCurrentConfig("snapStartStrength", _snapStartStrength);
            }
            ImGui.SameLine();
            ImGui.TextUnformatted("垂直强度系数");
            ImGui.SameLine();
            ImGui.SetNextItemWidth(snapExtraInputWidth);
            if (ImGui.InputFloat("##SnapVerticalStrengthFactor", ref _snapVerticalStrengthFactor, 0.01f, 0.01f, "%.2f"))
            {
                _snapVerticalStrengthFactor = Math.Clamp(_snapVerticalStrengthFactor, 0f, 1f);
                TryWriteFloatValueToCurrentConfig("snapVerticalStrengthFactor", _snapVerticalStrengthFactor);
            }
            ImGui.SameLine();
            ImGui.TextUnformatted("腰射强度系数");
            ImGui.SameLine();
            ImGui.SetNextItemWidth(snapExtraInputWidth);
            if (ImGui.InputFloat("##SnapHipfireStrengthFactor", ref _snapHipfireStrengthFactor, 0.01f, 0.01f, "%.2f"))
            {
                _snapHipfireStrengthFactor = Math.Clamp(_snapHipfireStrengthFactor, 0f, 1f);
                TryWriteFloatValueToCurrentConfig("snapHipfireStrengthFactor", _snapHipfireStrengthFactor);
            }
            
            ImGui.TextUnformatted("吸附高度");
            ImGui.SameLine();
            ImGui.SetNextItemWidth(snapExtraInputWidth);
            if (ImGui.InputFloat("##SnapHeight", ref _snapHeight, 0.01f, 0.01f, "%.2f"))
            {
                _snapHeight = Math.Clamp(_snapHeight, 0f, 1f);
                TryWriteFloatValueToCurrentConfig("snapHeight", _snapHeight);
            }

            ImGui.TableNextRow();
            ImGui.TableSetColumnIndex(0);
            ImGui.TextUnformatted("吸附曲线预览");
            ImGui.TableSetColumnIndex(1);
            DrawSnapCurvePreview();

            ImGui.EndTable();
        }
    }

    private void DrawSnapCurvePreview()
    {
        var previewHeight = ImGui.GetFrameHeightWithSpacing() * 4f;
        var previewWidth = MathF.Min(previewHeight * 2f, ImGui.GetContentRegionAvail().X);
        var canvasPos = ImGui.GetCursorScreenPos();
        var canvasSize = new Vector2(previewWidth, previewHeight);
        ImGui.InvisibleButton("##SnapCurvePreviewCanvas", canvasSize);

        var drawList = ImGui.GetWindowDrawList();
        var canvasMin = canvasPos;
        var canvasMax = canvasPos + canvasSize;
        const float axisPadding = 12f;
        var plotMin = new Vector2(canvasMin.X + axisPadding, canvasMin.Y + axisPadding);
        var plotMax = new Vector2(canvasMax.X - axisPadding, canvasMax.Y - axisPadding);

        var axisColor = ImGui.GetColorU32(ImGuiCol.Text);
        var lineColor = ImGui.GetColorU32(new Vector4(0.20f, 0.70f, 1.00f, 1.00f));
        var pointColor = ImGui.GetColorU32(new Vector4(1.00f, 0.45f, 0.20f, 1.00f));
        var borderColor = ImGui.GetColorU32(ImGuiCol.Border);
        var bgColor = ImGui.GetColorU32(new Vector4(0.08f, 0.09f, 0.11f, 1.00f));

        drawList.AddRectFilled(canvasMin, canvasMax, bgColor, 4f);
        drawList.AddRect(canvasMin, canvasMax, borderColor, 4f);
        drawList.AddLine(new Vector2(plotMin.X, plotMax.Y), new Vector2(plotMax.X, plotMax.Y), axisColor, 1.5f);
        drawList.AddLine(new Vector2(plotMin.X, plotMin.Y), new Vector2(plotMin.X, plotMax.Y), axisColor, 1.5f);

        var innerRangeForPreview = Math.Max(1, _snapInnerRange);
        var startStrengthForPreview = Math.Clamp(_snapStartStrength, 0f, 1f);
        var innerStrengthForPreview = Math.Clamp(_snapInnerStrength, 0f, 1f);
        var outerStrengthForPreview = Math.Clamp(_snapOuterStrength, 0f, 1f);

        Vector2 MapToPlot(float x, float y)
        {
            var nx = Math.Clamp(x / innerRangeForPreview, 0f, 1f);
            var ny = Math.Clamp(y, 0f, 1f);
            return new Vector2(
                plotMin.X + nx * (plotMax.X - plotMin.X),
                plotMax.Y - ny * (plotMax.Y - plotMin.Y));
        }

        var lineStart = MapToPlot(0f, startStrengthForPreview);
        var lineEnd = MapToPlot(innerRangeForPreview, innerStrengthForPreview);
        var highlightPoint = MapToPlot(innerRangeForPreview, outerStrengthForPreview);

        drawList.AddLine(lineStart, lineEnd, lineColor, 2.0f);
        drawList.AddCircleFilled(highlightPoint, 4.0f, pointColor);
        drawList.AddText(new Vector2(plotMin.X + 4f, plotMin.Y + 2f), axisColor, "1");
        drawList.AddText(new Vector2(plotMin.X + 4f, plotMax.Y - ImGui.GetTextLineHeight() - 2f), axisColor, "0");
        drawList.AddText(new Vector2(plotMax.X - 24f, plotMax.Y - ImGui.GetTextLineHeight() - 2f), axisColor, innerRangeForPreview.ToString());
    }

    private void DrawHomeModelCombo(string id, float width = -1f)
    {
        var comboWidth = width > 0f ? width : -1f;
        if (_onnxModels.Count == 0)
        {
            ImGui.BeginDisabled();
            ImGui.SetNextItemWidth(comboWidth);
            ImGui.Combo(id, ref _onnxTopSelectedModelIndex, "无可用模型\0");
            ImGui.EndDisabled();
            return;
        }

        if (_configFiles.Count == 0)
        {
            var selectedWhenDisabled = _onnxTopSelectedModelIndex >= 0 && _onnxTopSelectedModelIndex < _onnxModels.Count
                ? _onnxModels[_onnxTopSelectedModelIndex].DisplayName
                : string.Empty;
            var disabledIndex = 0;
            ImGui.BeginDisabled();
            ImGui.SetNextItemWidth(comboWidth);
            ImGui.Combo(id, ref disabledIndex, $"{selectedWhenDisabled}\0");
            ImGui.EndDisabled();
            return;
        }

        _onnxTopSelectedModelIndex = _onnxTopSelectedModelIndex >= 0 && _onnxTopSelectedModelIndex < _onnxModels.Count
            ? _onnxTopSelectedModelIndex
            : -1;
        var indexBeforeUi = _onnxTopSelectedModelIndex;
        var selectedLabel = _onnxTopSelectedModelIndex >= 0
            ? _onnxModels[_onnxTopSelectedModelIndex].DisplayName
            : string.Empty;

        ImGui.SetNextItemWidth(comboWidth);
        if (ImGui.BeginCombo(id, selectedLabel))
        {
            for (var i = 0; i < _onnxModels.Count; i++)
            {
                var isSelected = i == _onnxTopSelectedModelIndex;
                if (ImGui.Selectable(_onnxModels[i].DisplayName, isSelected))
                {
                    _onnxTopSelectedModelIndex = i;
                }

                if (isSelected)
                {
                    ImGui.SetItemDefaultFocus();
                }
            }

            ImGui.EndCombo();
        }

        if (_onnxTopSelectedModelIndex != indexBeforeUi)
        {
            TryWriteSelectedModelNameToCurrentConfig(_onnxModels[_onnxTopSelectedModelIndex].DisplayName);
        }
    }

    private void DrawConfigFileCombo(string id, float width = -1f)
    {
        var comboWidth = width > 0f ? width : -1f;
        if (_configFiles.Count == 0)
        {
            var disabledIndex = 0;
            ImGui.BeginDisabled();
            ImGui.SetNextItemWidth(comboWidth);
            ImGui.Combo(id, ref disabledIndex, "\0");
            ImGui.EndDisabled();
            return;
        }

        var indexBeforeUi = Math.Clamp(_selectedConfigFileIndex, 0, _configFiles.Count - 1);
        _selectedConfigFileIndex = indexBeforeUi;
        var selected = _configFiles[_selectedConfigFileIndex];

        ImGui.SetNextItemWidth(comboWidth);
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

        if (_selectedConfigFileIndex != indexBeforeUi)
        {
            WriteCurrentConfigFileName(_configFiles[_selectedConfigFileIndex]);
            TryApplyModelSelectionFromCurrentConfig();
        }
    }

    private void RefreshOnnxModels()
    {
        _onnxModels.Clear();
        var modelsDir = Path.Combine(ContentRootDirectory, "Models");
        _onnxModels.AddRange(OnnxModelConfigLoader.LoadFromDirectory(modelsDir));

        _onnxModels.Sort((a, b) => string.Compare(a.DisplayName, b.DisplayName, StringComparison.OrdinalIgnoreCase));
        if (_onnxModels.Count == 0)
        {
            _onnxTopSelectedModelIndex = -1;
        }
        else if (_onnxTopSelectedModelIndex >= _onnxModels.Count)
        {
            _onnxTopSelectedModelIndex = -1;
        }
        _onnxDebugSelectedModelIndex = Math.Clamp(_onnxDebugSelectedModelIndex, 0, Math.Max(0, _onnxModels.Count - 1));
        TryApplyModelSelectionFromCurrentConfig();
    }

    private void RefreshConfigFiles(string? forceSelectBaseName = null)
    {
        var oldSelection = _configFiles.Count > 0 && _selectedConfigFileIndex >= 0 && _selectedConfigFileIndex < _configFiles.Count
            ? _configFiles[_selectedConfigFileIndex]
            : null;

        _configFiles.Clear();
        var configsDir = Path.Combine(ContentRootDirectory, "Configs");
        if (Directory.Exists(configsDir))
        {
            foreach (var jsonPath in Directory.EnumerateFiles(configsDir, "*.json", SearchOption.TopDirectoryOnly))
            {
                var fileName = Path.GetFileNameWithoutExtension(jsonPath);
                if (!string.IsNullOrWhiteSpace(fileName))
                {
                    _configFiles.Add(fileName);
                }
            }
        }

        _configFiles.Sort(StringComparer.OrdinalIgnoreCase);
        if (_configFiles.Count == 0)
        {
            _selectedConfigFileIndex = 0;
            ClearCurrentConfigPointerFile();
            return;
        }

        if (!string.IsNullOrWhiteSpace(forceSelectBaseName))
        {
            var forceIndex = _configFiles.FindIndex(name => string.Equals(name, forceSelectBaseName, StringComparison.OrdinalIgnoreCase));
            if (forceIndex >= 0)
            {
                _selectedConfigFileIndex = forceIndex;
                WriteCurrentConfigFileName(_configFiles[forceIndex]);
                TryApplyModelSelectionFromCurrentConfig();
                return;
            }
        }

        if (!string.IsNullOrWhiteSpace(oldSelection))
        {
            var oldIndex = _configFiles.FindIndex(name => string.Equals(name, oldSelection, StringComparison.OrdinalIgnoreCase));
            if (oldIndex >= 0)
            {
                _selectedConfigFileIndex = oldIndex;
                TryApplyModelSelectionFromCurrentConfig();
                return;
            }
        }

        var persistedName = TryReadCurrentConfigFileName();
        if (!string.IsNullOrWhiteSpace(persistedName))
        {
            var persistedIndex = _configFiles.FindIndex(name => string.Equals(name, persistedName, StringComparison.OrdinalIgnoreCase));
            if (persistedIndex >= 0)
            {
                _selectedConfigFileIndex = persistedIndex;
                TryApplyModelSelectionFromCurrentConfig();
                return;
            }
        }

        _selectedConfigFileIndex = Math.Clamp(_selectedConfigFileIndex, 0, _configFiles.Count - 1);
        TryApplyModelSelectionFromCurrentConfig();
    }

    private static string ContentRootDirectory
    {
        get
        {
#if DEBUG
            return Environment.CurrentDirectory;
#else
            return AppContext.BaseDirectory;
#endif
        }
    }

    private static string ConfigsDirectoryPath => Path.Combine(ContentRootDirectory, "Configs");

    private static string ConfigCurrentFilePath => Path.Combine(ConfigsDirectoryPath, ".current");

    private static readonly JsonSerializerOptions IndentedJsonOptions = new() { WriteIndented = true };

    private static string? TryReadCurrentConfigFileName()
    {
        try
        {
            if (!File.Exists(ConfigCurrentFilePath))
            {
                return null;
            }

            var line = File.ReadAllText(ConfigCurrentFilePath).Trim();
            return string.IsNullOrWhiteSpace(line) ? null : line;
        }
        catch
        {
            return null;
        }
    }

    private static void WriteCurrentConfigFileName(string configBaseNameWithoutExtension)
    {
        try
        {
            Directory.CreateDirectory(ConfigsDirectoryPath);
            File.WriteAllText(ConfigCurrentFilePath, configBaseNameWithoutExtension + Environment.NewLine);
        }
        catch
        {
            // Keep UI responsive if the file is locked or the path is not writable.
        }
    }

    private static void ClearCurrentConfigPointerFile()
    {
        try
        {
            if (File.Exists(ConfigCurrentFilePath))
            {
                File.Delete(ConfigCurrentFilePath);
            }
        }
        catch
        {
            // Ignore IO failures.
        }
    }

    private void TryWriteSelectedModelNameToCurrentConfig(string modelName)
    {
        TryWriteStringValueToCurrentConfig("model", modelName);
    }

    private void TryApplyModelSelectionFromCurrentConfig()
    {
        if (_configFiles.Count == 0)
        {
            _homeSnapModeIndex = -1;
            _onnxTopSelectedModelIndex = -1;
            return;
        }

        _homeSnapModeIndex = TryReadSnapModeIndexFromCurrentConfig();
        if (_onnxModels.Count == 0)
        {
            _onnxTopSelectedModelIndex = -1;
            ApplySnapParametersFromCurrentConfig();
            return;
        }

        var modelName = TryReadSelectedModelNameFromCurrentConfig();
        if (string.IsNullOrWhiteSpace(modelName))
        {
            _onnxTopSelectedModelIndex = -1;
            return;
        }

        var modelIndex = _onnxModels.FindIndex(m => string.Equals(m.DisplayName, modelName, StringComparison.OrdinalIgnoreCase));
        if (modelIndex < 0)
        {
            _onnxTopSelectedModelIndex = -1;
            ApplySnapParametersFromCurrentConfig();
            return;
        }

        _onnxTopSelectedModelIndex = modelIndex;
        ApplySnapParametersFromCurrentConfig();
    }

    private int TryReadSnapModeIndexFromCurrentConfig()
    {
        var modeValue = TryReadStringValueFromCurrentConfig("snap");
        if (string.IsNullOrWhiteSpace(modeValue))
        {
            return -1;
        }

        for (var i = 0; i < HomeSnapModeOptions.Length; i++)
        {
            if (string.Equals(HomeSnapModeOptions[i], modeValue, StringComparison.OrdinalIgnoreCase))
            {
                return i;
            }
        }

        return -1;
    }

    private string? TryReadSelectedModelNameFromCurrentConfig()
    {
        return TryReadStringValueFromCurrentConfig("model");
    }

    private void ApplySnapParametersFromCurrentConfig()
    {
        var selectedModelSize = _onnxTopSelectedModelIndex >= 0 && _onnxTopSelectedModelIndex < _onnxModels.Count
            ? Math.Max(1, _onnxModels[_onnxTopSelectedModelIndex].InputHeight)
            : DefaultSnapOuterRange;
        var displayHeightLimit = GetDisplayHeightOrWindowHeight();
        var snapOuterRangeMax = Math.Max(selectedModelSize, displayHeightLimit);

        _snapOuterRange = Math.Clamp(TryReadIntValueFromCurrentConfig("snapOuterRange") ?? DefaultSnapOuterRange, selectedModelSize, snapOuterRangeMax);
        _snapInnerRange = Math.Clamp(TryReadIntValueFromCurrentConfig("snapInnerRange") ?? DefaultSnapInnerRange, 1, _snapOuterRange);
        _snapOuterStrength = Math.Clamp(TryReadFloatValueFromCurrentConfig("snapOuterStrength") ?? DefaultSnapOuterStrength, 0f, 1f);
        _snapInnerStrength = Math.Clamp(TryReadFloatValueFromCurrentConfig("snapInnerStrength") ?? DefaultSnapInnerStrength, 0f, 1f);
        _snapStartStrength = Math.Clamp(TryReadFloatValueFromCurrentConfig("snapStartStrength") ?? DefaultSnapStartStrength, 0f, 1f);
        _snapVerticalStrengthFactor = Math.Clamp(TryReadFloatValueFromCurrentConfig("snapVerticalStrengthFactor") ?? DefaultSnapVerticalStrengthFactor, 0f, 1f);
        _snapHipfireStrengthFactor = Math.Clamp(TryReadFloatValueFromCurrentConfig("snapHipfireStrengthFactor") ?? DefaultSnapHipfireStrengthFactor, 0f, 1f);
        _snapHeight = Math.Clamp(TryReadFloatValueFromCurrentConfig("snapHeight") ?? DefaultSnapHeight, 0f, 1f);
    }

    private void ResetConfigUiStateToDefaults()
    {
        _smartCoreEnabled = false;
        _homeSnapModeIndex = -1;
        _onnxTopSelectedModelIndex = -1;
        _snapOuterRange = DefaultSnapOuterRange;
        _snapInnerRange = DefaultSnapInnerRange;
        _snapOuterStrength = DefaultSnapOuterStrength;
        _snapInnerStrength = DefaultSnapInnerStrength;
        _snapStartStrength = DefaultSnapStartStrength;
        _snapVerticalStrengthFactor = DefaultSnapVerticalStrengthFactor;
        _snapHipfireStrengthFactor = DefaultSnapHipfireStrengthFactor;
        _snapHeight = DefaultSnapHeight;
    }

    private void TryWriteStringValueToCurrentConfig(string key, string value)
    {
        if (!TryGetCurrentConfigPath(out var configPath))
        {
            return;
        }

        try
        {
            var root = LoadJsonObjectOrEmpty(configPath);
            root[key] = value;
            SaveJsonObject(configPath, root);
        }
        catch
        {
            // Keep selection changes responsive if file IO fails.
        }
    }

    private void TryWriteIntValueToCurrentConfig(string key, int value)
    {
        if (!TryGetCurrentConfigPath(out var configPath))
        {
            return;
        }

        try
        {
            var root = LoadJsonObjectOrEmpty(configPath);
            root[key] = value;
            SaveJsonObject(configPath, root);
        }
        catch
        {
            // Keep selection changes responsive if file IO fails.
        }
    }

    private void TryWriteFloatValueToCurrentConfig(string key, float value)
    {
        if (!TryGetCurrentConfigPath(out var configPath))
        {
            return;
        }

        try
        {
            var root = LoadJsonObjectOrEmpty(configPath);
            root[key] = value;
            SaveJsonObject(configPath, root);
        }
        catch
        {
            // Keep selection changes responsive if file IO fails.
        }
    }

    private string? TryReadStringValueFromCurrentConfig(string key)
    {
        if (!TryGetCurrentConfigPath(out var configPath))
        {
            return null;
        }

        try
        {
            if (!TryLoadJsonObject(configPath, out var root))
            {
                return null;
            }

            var value = root[key]?.GetValue<string>()?.Trim();
            return string.IsNullOrWhiteSpace(value) ? null : value;
        }
        catch
        {
            return null;
        }
    }

    private int? TryReadIntValueFromCurrentConfig(string key)
    {
        if (!TryGetCurrentConfigPath(out var configPath))
        {
            return null;
        }

        try
        {
            if (!TryLoadJsonObject(configPath, out var root))
            {
                return null;
            }

            return root[key]?.GetValue<int>();
        }
        catch
        {
            return null;
        }
    }

    private float? TryReadFloatValueFromCurrentConfig(string key)
    {
        if (!TryGetCurrentConfigPath(out var configPath))
        {
            return null;
        }

        try
        {
            if (!TryLoadJsonObject(configPath, out var root))
            {
                return null;
            }

            return root[key]?.GetValue<float>();
        }
        catch
        {
            return null;
        }
    }

    private void ClearSelectedModelNameFromCurrentConfig()
    {
        RemoveStringKeyFromCurrentConfig("model");
    }

    private void RemoveStringKeyFromCurrentConfig(string key)
    {
        if (!TryGetCurrentConfigPath(out var configPath))
        {
            return;
        }

        try
        {
            var root = LoadJsonObjectOrEmpty(configPath);
            root.Remove(key);
            SaveJsonObject(configPath, root);
        }
        catch
        {
            // Keep UI responsive if file IO fails.
        }
    }

    private bool TryGetCurrentConfigPath(out string configPath)
    {
        configPath = string.Empty;
        if (_configFiles.Count == 0)
        {
            return false;
        }

        var configIndex = Math.Clamp(_selectedConfigFileIndex, 0, _configFiles.Count - 1);
        configPath = Path.Combine(ConfigsDirectoryPath, _configFiles[configIndex] + ".json");
        return true;
    }

    private static bool TryLoadJsonObject(string path, out JsonObject root)
    {
        root = new JsonObject();
        if (!File.Exists(path))
        {
            return false;
        }

        var raw = File.ReadAllText(path);
        if (string.IsNullOrWhiteSpace(raw))
        {
            return false;
        }

        if (JsonNode.Parse(raw) is not JsonObject parsed)
        {
            return false;
        }

        root = parsed;
        return true;
    }

    private static JsonObject LoadJsonObjectOrEmpty(string path)
    {
        return TryLoadJsonObject(path, out var root) ? root : new JsonObject();
    }

    private static void SaveJsonObject(string path, JsonObject root)
    {
        File.WriteAllText(path, root.ToJsonString(IndentedJsonOptions) + Environment.NewLine);
    }

    private void DrawConfigFileModals()
    {
        if (_configAddModalOpenRequested)
        {
            ImGui.OpenPopup("请输入新配置名称");
            _configAddModalOpenRequested = false;
        }

        if (ImGui.BeginPopupModal("请输入新配置名称", ref _configAddModalOpen, ImGuiWindowFlags.AlwaysAutoResize))
        {
            ImGui.InputText("##AddConfigNameInput", ref _addConfigNameBuffer, 256);
            if (!string.IsNullOrEmpty(_configAddModalError))
            {
                ImGui.TextUnformatted(_configAddModalError);
            }

            if (ImGui.Button("创建"))
            {
                if (TryCreateEmptyConfigFile(_addConfigNameBuffer, out var err))
                {
                    _configAddModalError = string.Empty;
                    _configAddModalOpen = false;
                    ImGui.CloseCurrentPopup();
                }
                else
                {
                    _configAddModalError = err;
                }
            }

            ImGui.SameLine();
            if (ImGui.Button("取消"))
            {
                _configAddModalError = string.Empty;
                _configAddModalOpen = false;
                ImGui.CloseCurrentPopup();
            }

            ImGui.EndPopup();
        }

        if (_configDeleteModalOpenRequested)
        {
            ImGui.OpenPopup("删除配置确认");
            _configDeleteModalOpenRequested = false;
        }

        if (ImGui.BeginPopupModal("删除配置确认", ref _configDeleteModalOpen, ImGuiWindowFlags.AlwaysAutoResize))
        {
            var name = _pendingDeleteConfigBaseName ?? string.Empty;
            ImGui.TextUnformatted($"确定删除配置文件 {name} 吗？此操作不可撤销。");
            if (ImGui.Button("确定"))
            {
                TryDeleteSelectedConfigFile(name);
                _pendingDeleteConfigBaseName = null;
                _configDeleteModalOpen = false;
                ImGui.CloseCurrentPopup();
            }

            ImGui.SameLine();
            if (ImGui.Button("取消"))
            {
                _pendingDeleteConfigBaseName = null;
                _configDeleteModalOpen = false;
                ImGui.CloseCurrentPopup();
            }

            ImGui.EndPopup();
        }
    }

    private static bool TryNormalizeConfigBaseName(string raw, out string baseName, out string error)
    {
        baseName = string.Empty;
        error = string.Empty;
        var n = raw.Trim();
        if (n.EndsWith(".json", StringComparison.OrdinalIgnoreCase))
        {
            n = n[..^5];
        }

        n = n.Trim();
        if (n.Length == 0)
        {
            error = "名称不能为空";
            return false;
        }

        if (n.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
        {
            error = "名称包含非法字符";
            return false;
        }

        if (n is "." or "..")
        {
            error = "名称无效";
            return false;
        }

        baseName = n;
        return true;
    }

    private bool TryCreateEmptyConfigFile(string rawName, out string error)
    {
        error = string.Empty;
        if (!TryNormalizeConfigBaseName(rawName, out var baseName, out var normErr))
        {
            error = normErr;
            return false;
        }

        try
        {
            Directory.CreateDirectory(ConfigsDirectoryPath);
            var path = Path.Combine(ConfigsDirectoryPath, baseName + ".json");
            if (File.Exists(path))
            {
                error = "已存在同名配置文件";
                return false;
            }

            File.WriteAllText(path, "{}" + Environment.NewLine);
            ResetConfigUiStateToDefaults();
            RefreshConfigFiles(baseName);
            return true;
        }
        catch (Exception ex)
        {
            error = ex.Message;
            return false;
        }
    }

    private void TryDeleteSelectedConfigFile(string baseName)
    {
        if (string.IsNullOrWhiteSpace(baseName))
        {
            return;
        }

        try
        {
            var path = Path.Combine(ConfigsDirectoryPath, baseName + ".json");
            if (File.Exists(path))
            {
                File.Delete(path);
            }

            RefreshConfigFiles();
            if (_configFiles.Count > 0)
            {
                WriteCurrentConfigFileName(_configFiles[_selectedConfigFileIndex]);
            }
            else
            {
                ClearCurrentConfigPointerFile();
            }
        }
        catch
        {
            // Ignore delete failures; list refresh will reflect disk state on next scan if needed.
        }
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

    private int GetDisplayHeightOrWindowHeight()
    {
        try
        {
            unsafe
            {
                var monitor = GLFW.GetPrimaryMonitor();
                if (monitor != null)
                {
                    var videoMode = GLFW.GetVideoMode(monitor);
                    if (videoMode != null && videoMode->Height > 0)
                    {
                        return videoMode->Height;
                    }
                }
            }
        }
        catch
        {
            // Fallback to window height when monitor query fails.
        }

        return Math.Max(1, ClientSize.Y);
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

public sealed class ImGuiController : IDisposable
{
    private const float BaseFontSize = 16.0f;
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

        var style = ImGui.GetStyle();
        style.FrameRounding = 6f;

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
        if (MathF.Abs(dpiScale - _dpiScale) < 0.01f)
        {
            return;
        }

        _dpiScale = dpiScale;
        var io = ImGui.GetIO();
        ConfigureFonts(io);
        CreateFontTexture();
    }

    private void ConfigureFonts(ImGuiIOPtr io)
    {
        io.Fonts.Clear();

        var zhFontPath = ResourceAssets.ExtractToTemp("AlibabaPuHuiTi-3-55-Regular.otf");
        var scaledFontSize = BaseFontSize * _dpiScale;

        _englishFont = io.Fonts.AddFontFromFileTTF(zhFontPath, scaledFontSize, null, io.Fonts.GetGlyphRangesChineseFull());
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

        if (_fontTexture != 0)
        {
            GL.DeleteTexture(_fontTexture);
            _fontTexture = 0;
        }

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
