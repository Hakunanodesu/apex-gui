using System.Reflection;
using System.Numerics;
using System.Diagnostics;
using System.Threading;
using System.Text.Json;
using ImGuiNET;
using OpenTK.Graphics.OpenGL4;
using OpenTK.Windowing.Common;
using OpenTK.Windowing.Common.Input;
using OpenTK.Windowing.Desktop;
using OpenTK.Windowing.GraphicsLibraryFramework;
using Keys = OpenTK.Windowing.GraphicsLibraryFramework.Keys;
using SDL3;
using StbImageSharp;
using Vortice.Direct3D;
using Vortice.Direct3D11;
using Vortice.DXGI;
using static Vortice.Direct3D11.D3D11;
using static Vortice.DXGI.DXGI;

public sealed partial class MainWindow : GameWindow
{
    private const string ViGemBusInstallerUrl = "https://github.com/nefarius/ViGEmBus/releases/download/v1.22.0/ViGEmBus_1.22.0_x64_x86_arm64.exe";
    private const string WindowStateFileName = "window_state.ini";
    private const int DefaultSnapOuterRange = 1;
    private const int DefaultSnapInnerRange = 1;
    private const float DefaultSnapOuterStrength = 0f;
    private const float DefaultSnapInnerStrength = 0f;
    private const float DefaultSnapStartStrength = 0f;
    private const float DefaultSnapVerticalStrengthFactor = 0f;
    private const float DefaultSnapHipfireStrengthFactor = 0f;
    private const float DefaultSnapHeight = 0f;

    private ImGuiController? _controller;
    private float _dpiScale = 1.0f;

    private readonly List<OnnxModelConfig> _onnxModels = new();
    private int _onnxTopSelectedModelIndex = -1;
    private static readonly string[] HomeSnapModeOptions = { "开火吸附", "瞄准吸附" };
    private static readonly string[] SnapInnerInterpolationTypeOptions = { "线性插值", "二次插值" };
    private static readonly string[] SpecialWeaponNames =
    {
        "30-30",
        "g7",
        "kraber",
        "longbow",
        "mastiff",
        "p2020_single",
        "peacekeeper",
        "sentinel",
        "triple_take",
        "wingman"
    };
    private const string SpecialWeaponLogicConfigKey = "specialWeaponLogic";
    private const string AimSnapWeaponListConfigKey = "aimSnapWeapons";
    private const string RapidFireWeaponListConfigKey = "rapidFireWeapons";
    private const string ReleaseFireWeaponListConfigKey = "releaseFireWeapons";
    private int _homeSnapModeIndex = -1;
    private readonly bool[] _specialWeaponAimSnapEnabled = new bool[SpecialWeaponNames.Length];
    private readonly bool[] _specialWeaponRapidFireEnabled = new bool[SpecialWeaponNames.Length];
    private readonly bool[] _specialWeaponReleaseFireEnabled = new bool[SpecialWeaponNames.Length];
    private readonly List<string> _configFiles = new();
    private int _selectedConfigFileIndex;
    private string _addConfigNameBuffer = string.Empty;
    private string _configAddModalError = string.Empty;
    private bool _configAddModalOpen;
    private bool _configDeleteModalOpen;
    private bool _configAddModalOpenRequested;
    private bool _configDeleteModalOpenRequested;
    private string? _pendingDeleteConfigBaseName;
    private int _snapOuterRange = DefaultSnapOuterRange;
    private float _snapOuterStrength = DefaultSnapOuterStrength;
    private int _snapInnerRange = DefaultSnapInnerRange;
    private float _snapInnerStrength = DefaultSnapInnerStrength;
    private float _snapStartStrength = DefaultSnapStartStrength;
    private float _snapVerticalStrengthFactor = DefaultSnapVerticalStrengthFactor;
    private float _snapHipfireStrengthFactor = DefaultSnapHipfireStrengthFactor;
    private float _snapHeight = DefaultSnapHeight;
    private int _snapInnerInterpolationTypeIndex;
    private int _homeSelectedGamepadIndex;
    private int _debugSelectedGamepadIndex;
    private OpenTK.Mathematics.Vector2i _lastNormalClientSize;
    private SdlGamepadWorker? _sdlGamepadWorker;
    private ViGEmMappingWorker? _viGEmMappingWorker;
    private readonly ConfigService _configService = new(ConfigsDirectoryPath);
    private readonly ConfigAppService _configAppService = new();
    private readonly ConfigContextService _configContextService = new();
    private readonly ConfigSelectionService _configSelectionService = new();
    private readonly ConfigFileLifecycleService _configFileLifecycleService = new();
    private readonly DependencyService _dependencyService = new();
    private readonly InputDeviceService _inputDeviceService = new();
    private readonly SpecialWeaponLogicService _specialWeaponLogicService = new();
    private readonly SmartCoreMappingService _smartCoreMappingService = new();
    private readonly SmartCoreMappingState _smartCoreMappingState = new();
    private static readonly WindowStateService WindowStateService = new();
    private (uint InstanceId, string Name)[] _cachedConnectedGamepads = Array.Empty<(uint InstanceId, string Name)>();
    private string[] _cachedGamepadOptions = Array.Empty<string>();
    internal static string WindowStateFilePath => Path.Combine(Environment.CurrentDirectory, WindowStateFileName);

    internal static bool TryLoadWindowState(out WindowStateSnapshot snapshot)
    {
        return WindowStateService.TryLoad(WindowStateFilePath, out snapshot);
    }

    public MainWindow(GameWindowSettings gameWindowSettings, NativeWindowSettings nativeWindowSettings)
        : base(gameWindowSettings, nativeWindowSettings)
    {
    }

    protected override void OnLoad()
    {
        base.OnLoad();
        SDL.InitSubSystem(SDL.InitFlags.Gamepad);
        _sdlGamepadWorker = new SdlGamepadWorker();
        _viGEmMappingWorker = new ViGEmMappingWorker();
        _viGEmMappingWorker.SetSdlGamepadWorker(_sdlGamepadWorker);
        _controller = new ImGuiController(ClientSize.X, ClientSize.Y);
        VSync = VSyncMode.Off;
        RefreshDpiScale();
        RefreshOnnxModels();
        RefreshConfigFiles();
        RefreshHomeInputDevices();
        RefreshDebugInputDevices();
        _lastNormalClientSize = ClientSize;

        InitDebugVirtualGamepad();
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

        MirrorSelectedGamepadToVirtualGamepad();

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

        var interpolationTypeIndexForPreview = _snapInnerInterpolationTypeIndex >= 0 && _snapInnerInterpolationTypeIndex < SnapInnerInterpolationTypeOptions.Length
            ? _snapInnerInterpolationTypeIndex
            : 0;
        if (interpolationTypeIndexForPreview == 0)
        {
            drawList.AddLine(lineStart, lineEnd, lineColor, 2.0f);
        }
        else
        {
            const int quadraticSegments = 24;
            for (var i = 0; i < quadraticSegments; i++)
            {
                var t0 = i / (float)quadraticSegments;
                var t1 = (i + 1) / (float)quadraticSegments;
                var x0 = innerRangeForPreview * t0;
                var x1 = innerRangeForPreview * t1;
                var y0 = startStrengthForPreview + (innerStrengthForPreview - startStrengthForPreview) * t0 * t0;
                var y1 = startStrengthForPreview + (innerStrengthForPreview - startStrengthForPreview) * t1 * t1;
                drawList.AddLine(MapToPlot(x0, y0), MapToPlot(x1, y1), lineColor, 2.0f);
            }
        }

        drawList.AddCircleFilled(highlightPoint, 4.0f, pointColor);
        drawList.AddText(new Vector2(plotMin.X + 4f, plotMin.Y + 2f), axisColor, "1.0");
        drawList.AddText(new Vector2(plotMin.X + 4f, plotMax.Y - ImGui.GetTextLineHeight() - 2f), axisColor, "0");
        var xAxisTickLabel = innerRangeForPreview.ToString();
        var xAxisTickLabelWidth = ImGui.CalcTextSize(xAxisTickLabel).X;
        drawList.AddText(new Vector2(plotMax.X - xAxisTickLabelWidth - 10f, plotMax.Y - ImGui.GetTextLineHeight() - 2f), axisColor, xAxisTickLabel);
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
        _configFiles.AddRange(_configAppService.EnumerateConfigBaseNames(_configService.ConfigsDirectoryPath));
        if (_configFiles.Count == 0)
        {
            _selectedConfigFileIndex = 0;
            ClearCurrentConfigPointerFile();
            return;
        }

        var persistedName = TryReadCurrentConfigFileName();
        _selectedConfigFileIndex = _configAppService.ResolveSelectedIndex(
            _configFiles,
            _selectedConfigFileIndex,
            forceSelectBaseName,
            oldSelection,
            persistedName);
        if (!string.IsNullOrWhiteSpace(forceSelectBaseName))
        {
            WriteCurrentConfigFileName(_configFiles[_selectedConfigFileIndex]);
        }

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

    private string? TryReadCurrentConfigFileName() => _configService.TryReadCurrentConfigFileName();

    private void WriteCurrentConfigFileName(string configBaseNameWithoutExtension) =>
        _configService.WriteCurrentConfigFileName(configBaseNameWithoutExtension);

    private void ClearCurrentConfigPointerFile() => _configService.ClearCurrentConfigPointerFile();

    private void TryWriteSelectedModelNameToCurrentConfig(string modelName)
    {
        TryWriteStringToCurrentConfig("model", modelName);
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
        ApplySpecialWeaponLogicFromCurrentConfig();
        if (_onnxModels.Count == 0)
        {
            _onnxTopSelectedModelIndex = -1;
            ApplySnapParametersFromCurrentConfig();
            return;
        }

        _onnxTopSelectedModelIndex = _configSelectionService.ResolveModelIndex(TryReadSelectedModelNameFromCurrentConfig(), _onnxModels);
        ApplySnapParametersFromCurrentConfig();
    }

    private int TryReadSnapModeIndexFromCurrentConfig()
    {
        return _configSelectionService.ResolveOptionIndex(
            TryReadStringFromCurrentConfig("snap"),
            HomeSnapModeOptions,
            0);
    }

    private string? TryReadSelectedModelNameFromCurrentConfig()
    {
        return TryReadStringFromCurrentConfig("model");
    }

    private void ApplySnapParametersFromCurrentConfig()
    {
        if (!TryResolveCurrentConfigPath(out var configPath))
        {
            return;
        }

        var selectedModelSize = _onnxTopSelectedModelIndex >= 0 && _onnxTopSelectedModelIndex < _onnxModels.Count
            ? Math.Max(1, _onnxModels[_onnxTopSelectedModelIndex].InputHeight)
            : DefaultSnapOuterRange;
        var snapConfigState = _configSelectionService.ReadSnapConfig(
            _configService,
            configPath,
            selectedModelSize,
            GetDisplayHeightOrWindowHeight(),
            DefaultSnapOuterRange,
            DefaultSnapInnerRange,
            DefaultSnapOuterStrength,
            DefaultSnapInnerStrength,
            DefaultSnapStartStrength,
            DefaultSnapVerticalStrengthFactor,
            DefaultSnapHipfireStrengthFactor,
            DefaultSnapHeight,
            SnapInnerInterpolationTypeOptions);

        _snapOuterRange = snapConfigState.OuterRange;
        _snapInnerRange = snapConfigState.InnerRange;
        _snapOuterStrength = snapConfigState.OuterStrength;
        _snapInnerStrength = snapConfigState.InnerStrength;
        _snapStartStrength = snapConfigState.StartStrength;
        _snapVerticalStrengthFactor = snapConfigState.VerticalStrengthFactor;
        _snapHipfireStrengthFactor = snapConfigState.HipfireStrengthFactor;
        _snapHeight = snapConfigState.Height;
        _snapInnerInterpolationTypeIndex = snapConfigState.InnerInterpolationTypeIndex;
    }

    private void ResetConfigUiStateToDefaults()
    {
        _smartCoreMappingState.RequestedEnabled = false;
        _homeSnapModeIndex = 0;
        _onnxTopSelectedModelIndex = -1;
        _snapOuterRange = DefaultSnapOuterRange;
        _snapInnerRange = DefaultSnapInnerRange;
        _snapOuterStrength = DefaultSnapOuterStrength;
        _snapInnerStrength = DefaultSnapInnerStrength;
        _snapStartStrength = DefaultSnapStartStrength;
        _snapVerticalStrengthFactor = DefaultSnapVerticalStrengthFactor;
        _snapHipfireStrengthFactor = DefaultSnapHipfireStrengthFactor;
        _snapHeight = DefaultSnapHeight;
        _snapInnerInterpolationTypeIndex = 0;
        Array.Clear(_specialWeaponAimSnapEnabled);
        Array.Clear(_specialWeaponRapidFireEnabled);
        Array.Clear(_specialWeaponReleaseFireEnabled);
    }

    private void TryWriteStringToCurrentConfig(string key, string value)
    {
        if (!TryResolveCurrentConfigPath(out var configPath))
        {
            return;
        }

        _configService.TryWriteString(configPath, key, value);
    }

    private void TryWriteIntToCurrentConfig(string key, int value)
    {
        if (!TryResolveCurrentConfigPath(out var configPath))
        {
            return;
        }

        _configService.TryWriteInt(configPath, key, value);
    }

    private void TryWriteFloatToCurrentConfig(string key, float value)
    {
        if (!TryResolveCurrentConfigPath(out var configPath))
        {
            return;
        }

        _configService.TryWriteFloat(configPath, key, value);
    }

    private void TryWriteSpecialWeaponLogicValueToCurrentConfig(int weaponIndex, bool aimSnapEnabled, bool rapidFireEnabled, bool releaseFireEnabled)
    {
        if (!TryResolveCurrentConfigPath(out var configPath))
        {
            return;
        }

        _specialWeaponLogicService.UpdateSingleWeaponAndSave(
            _configService,
            configPath,
            SpecialWeaponLogicConfigKey,
            AimSnapWeaponListConfigKey,
            RapidFireWeaponListConfigKey,
            ReleaseFireWeaponListConfigKey,
            SpecialWeaponNames,
            weaponIndex,
            aimSnapEnabled,
            rapidFireEnabled,
            releaseFireEnabled,
            _specialWeaponAimSnapEnabled,
            _specialWeaponRapidFireEnabled,
            _specialWeaponReleaseFireEnabled);
    }

    private void ApplySpecialWeaponLogicFromCurrentConfig()
    {
        if (!TryResolveCurrentConfigPath(out var configPath))
        {
            Array.Clear(_specialWeaponAimSnapEnabled);
            Array.Clear(_specialWeaponRapidFireEnabled);
            Array.Clear(_specialWeaponReleaseFireEnabled);
            return;
        }

        _specialWeaponLogicService.LoadFromConfig(
            _configService,
            configPath,
            SpecialWeaponLogicConfigKey,
            AimSnapWeaponListConfigKey,
            RapidFireWeaponListConfigKey,
            ReleaseFireWeaponListConfigKey,
            SpecialWeaponNames,
            _specialWeaponAimSnapEnabled,
            _specialWeaponRapidFireEnabled,
            _specialWeaponReleaseFireEnabled);
    }

    private string? TryReadStringFromCurrentConfig(string key)
    {
        if (!TryResolveCurrentConfigPath(out var configPath))
        {
            return null;
        }

        return _configService.TryReadString(configPath, key);
    }

    private void ClearSelectedModelNameFromCurrentConfig()
    {
        if (TryResolveCurrentConfigPath(out var configPath))
        {
            _configService.TryRemoveKey(configPath, "model");
        }
    }

    private bool TryResolveCurrentConfigPath(out string configPath) =>
        _configContextService.TryResolveCurrentConfigPath(_configFiles, _selectedConfigFileIndex, _configService, out configPath);

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
                ImGui.AlignTextToFramePadding();
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
            ImGui.AlignTextToFramePadding();
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

    private bool TryCreateEmptyConfigFile(string rawName, out string error)
    {
        if (_configFileLifecycleService.TryCreateEmptyConfigFile(
                rawName,
                _configService,
                _specialWeaponLogicService,
                SpecialWeaponLogicConfigKey,
                AimSnapWeaponListConfigKey,
                RapidFireWeaponListConfigKey,
                ReleaseFireWeaponListConfigKey,
                out var baseName,
                out error))
        {
            ResetConfigUiStateToDefaults();
            RefreshConfigFiles(baseName);
            return true;
        }

        return false;
    }

    private void TryDeleteSelectedConfigFile(string baseName)
    {
        _configFileLifecycleService.TryDeleteConfigFile(baseName, _configService);
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

    protected override void OnResize(ResizeEventArgs e)
    {
        base.OnResize(e);
        GL.Viewport(0, 0, ClientSize.X, ClientSize.Y);
        _controller?.WindowResized(ClientSize.X, ClientSize.Y);
        RefreshDpiScale();
        RememberNormalWindowBounds();
    }

    protected override void OnMove(WindowPositionEventArgs e)
    {
        base.OnMove(e);
        RememberNormalWindowBounds();
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
        CloseSnapRangePreviewWindow();

        _viGEmMappingWorker?.Dispose();
        _viGEmMappingWorker = null;
        _sdlGamepadWorker?.Dispose();
        _sdlGamepadWorker = null;
        StopOnnxInference("已释放");
        StopDxgiCapture("已释放");
        if (_dxgiPreviewTexture != 0)
        {
            GL.DeleteTexture(_dxgiPreviewTexture);
            _dxgiPreviewTexture = 0;
        }

        _controller?.Dispose();
        SaveWindowState();
        SDL.QuitSubSystem(SDL.InitFlags.Gamepad);
        base.OnUnload();
    }
    private void InitDebugVirtualGamepad()
    {
        try
        {
            _viGEmMappingWorker?.ConnectVirtualGamepad();
        }
        catch (Exception ex)
        {
            _smartCoreMappingState.LastError = $"{ex.GetType().Name}: {ex.Message}";
        }
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

    private void RememberNormalWindowBounds()
    {
        if (WindowState == WindowState.Maximized)
        {
            return;
        }

        if (ClientSize.X > 0 && ClientSize.Y > 0)
        {
            _lastNormalClientSize = ClientSize;
        }

    }

    private void SaveWindowState()
    {
        try
        {
            var useLastNormalBounds = WindowState == WindowState.Maximized;
            var size = useLastNormalBounds ? _lastNormalClientSize : ClientSize;
            if (size.X <= 0 || size.Y <= 0)
            {
                size = ClientSize;
            }

            var snapshot = new WindowStateSnapshot
            {
                Width = Math.Max(400, size.X),
                Height = Math.Max(300, size.Y),
                IsMaximized = WindowState == WindowState.Maximized
            };
            WindowStateService.Save(WindowStateFilePath, snapshot);
        }
        catch
        {
            // Ignore persistence failures to avoid blocking shutdown.
        }
    }
}

internal sealed class WindowStateSnapshot
{
    public int Width { get; init; }
    public int Height { get; init; }
    public bool IsMaximized { get; init; }
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
        io.AddMouseWheelEvent(0f, _scrollDelta.Y);
        _scrollDelta = Vector2.Zero;

        var keyboard = window.KeyboardState;
        // Keep only basic numeric-edit keys.
        io.AddKeyEvent(ImGuiKey.Delete, keyboard.IsKeyDown(Keys.Delete));
        io.AddKeyEvent(ImGuiKey.Backspace, keyboard.IsKeyDown(Keys.Backspace));
        io.AddKeyEvent(ImGuiKey.Enter, keyboard.IsKeyDown(Keys.Enter));
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
