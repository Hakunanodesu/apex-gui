using System.Numerics;
using System.Diagnostics;
using System.Linq;
using ImGuiNET;
using OpenTK.Graphics.OpenGL4;
using OpenTK.Windowing.Common;
using OpenTK.Windowing.Desktop;
using OpenTK.Windowing.GraphicsLibraryFramework;
using Keys = OpenTK.Windowing.GraphicsLibraryFramework.Keys;
using SDL3;

public sealed partial class MainWindow : GameWindow
{
    // Partial layout:
    // - MainWindow.cs: app bootstrap, lifecycle, shared state, top-level orchestration
    // - MainWindow.Home.cs: Home tab UI composition and controls
    // - MainWindow.Home.ConfigModals.cs: config create/delete modal flows
    // - MainWindow.Home.PreviewWindows.cs: preview window threading and rendering
    // - MainWindow.Vision.cs: SmartCore vision pipeline lifecycle
    private const string ViGemBusInstallPath = @"C:\Program Files\Nefarius Software Solutions";
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
    private const float DefaultSnapStrengthRampTime = 0f;

    private ImGuiController? _controller;
    private float _dpiScale = 1.0f;

    private readonly List<OnnxModelConfig> _onnxModels = new();
    private int _onnxTopSelectedModelIndex = -1;
    private static readonly string[] HomeSnapModeOptions = { "开火吸附", "瞄准 + 开火吸附" };
    private static readonly string[] HomeRapidFireStrategyOptions = { "关闭连点", "始终连点", "根据当前武器连点" };
    private const int AimAndFireSnapModeIndex = 1;
    private const int WeaponBasedRapidFireStrategyIndex = 2;
    private const int DefaultRapidFireHz = 30;
    private const int MinRapidFireHz = 1;
    private const int MaxRapidFireHz = 30;
    private static readonly string[] SnapInnerInterpolationTypeOptions =
    {
        "Linear",
        "Quadratic Ease-In",
        "Quadratic Ease-Out",
        "Quadratic Ease-In-Out"
    };
    private static readonly string[] TouchpadBindingOptions =
        GamepadBindingCatalog.Options.Concat(new[] { GamepadBindingCatalog.KeyboardCustomBindingName }).ToArray();
    private static readonly string[] HomeGameOptions = WeaponTemplateCatalog.GameOptions;
    private const string GameConfigKey = "game";
    private const string SpecialWeaponLogicConfigKey = "specialWeaponLogic";
    private const string AimSnapWeaponListConfigKey = "aimSnapWeapons";
    private const string RapidFireWeaponListConfigKey = "rapidFireWeapons";
    private const string ReleaseFireWeaponListConfigKey = "releaseFireWeapons";
    private const string AimBindingConfigKey = "aimBinding";
    private const string FireBindingConfigKey = "fireBinding";
    private const string VoiceBindingConfigKey = "voiceBinding";
    private const string VoiceCustomKeyConfigKey = "voiceCustomKey";
    private const string TouchpadLeftBindingConfigKey = "touchpadLeftBinding";
    private const string TouchpadRightBindingConfigKey = "touchpadRightBinding";
    private const string TouchpadLeftCustomKeyConfigKey = "touchpadLeftCustomKey";
    private const string TouchpadRightCustomKeyConfigKey = "touchpadRightCustomKey";
    private const string DefaultVoiceCustomKeyName = "V";
    private readonly HomeViewState _homeViewState = new();
    private string[] _specialWeaponNames;
    private bool[] _specialWeaponAimSnapEnabled;
    private bool[] _specialWeaponRapidFireEnabled;
    private bool[] _specialWeaponReleaseFireEnabled;
    private readonly List<string> _configFiles = new();
    private int _selectedConfigFileIndex;
    private int _homeSelectedGamepadIndex;
    private uint? _homeSelectedGamepadInstanceId;
    private OpenTK.Mathematics.Vector2i _lastNormalClientSize;
    private SdlGamepadWorker? _sdlGamepadWorker;
    private ViGEmMappingWorker? _viGEmMappingWorker;
    private readonly ConfigRepository _configRepository = new(ConfigsDirectoryPath);
    private readonly ConfigStore _configStore;
    private readonly GamepadService _gamepadService = new();
    private readonly SmartCoreMappingState _smartCoreMappingState = new();
    private static readonly WindowStateService WindowStateService = new();
    private (uint InstanceId, string Name)[] _cachedConnectedGamepads = Array.Empty<(uint InstanceId, string Name)>();
    private string[] _cachedGamepadOptions = Array.Empty<string>();
    private readonly HashSet<Keys> _touchpadCapturePreviousDownKeys = new();
    private TouchpadKeyCaptureTarget _activeTouchpadKeyCaptureTarget;
    private static uint? _startupSelectedGamepadInstanceId;
    internal static string WindowStateFilePath => Path.Combine(Environment.CurrentDirectory, WindowStateFileName);

    internal static bool TryLoadWindowState(out WindowStateSnapshot snapshot)
    {
        var loaded = WindowStateService.TryLoad(WindowStateFilePath, out snapshot);
        _startupSelectedGamepadInstanceId = loaded ? snapshot.SelectedGamepadInstanceId : null;
        return loaded;
    }

    public MainWindow(GameWindowSettings gameWindowSettings, NativeWindowSettings nativeWindowSettings)
        : base(gameWindowSettings, nativeWindowSettings)
    {
        _specialWeaponNames = Array.Empty<string>();
        _specialWeaponAimSnapEnabled = Array.Empty<bool>();
        _specialWeaponRapidFireEnabled = Array.Empty<bool>();
        _specialWeaponReleaseFireEnabled = Array.Empty<bool>();
        _configStore = new ConfigStore(_configRepository);
        RefreshSpecialWeaponNamesForCurrentGame();
    }

    protected override void OnLoad()
    {
        base.OnLoad();
        SDL.InitSubSystem(SDL.InitFlags.Gamepad);
        _homeSelectedGamepadInstanceId = _startupSelectedGamepadInstanceId;
        _sdlGamepadWorker = new SdlGamepadWorker();
        _viGEmMappingWorker = new ViGEmMappingWorker();
        _viGEmMappingWorker.SetSdlGamepadWorker(_sdlGamepadWorker);
        _controller = new ImGuiController(ClientSize.X, ClientSize.Y);
        VSync = VSyncMode.Off;
        RefreshDpiScale();
        RefreshOnnxModels();
        RefreshConfigFiles();
        RefreshHomeInputDevices();
        ApplySelectedGamepadSelection();
        PushAimAssistConfig();
        RefreshSmartCoreState();
        SyncSmartCoreVisionPipeline();
        _lastNormalClientSize = ClientSize;

        InitializeVirtualGamepadConnection();
    }

    protected override void OnRenderFrame(FrameEventArgs args)
    {
        base.OnRenderFrame(args);
        if (_controller is null)
        {
            return;
        }

        RefreshDpiScale();

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

        var innerRangeForPreview = Math.Max(1, _homeViewState.SnapInnerRange);
        var startStrengthForPreview = Math.Clamp(_homeViewState.SnapStartStrength, 0f, 1f);
        var innerStrengthForPreview = Math.Clamp(_homeViewState.SnapInnerStrength, 0f, 1f);
        var outerStrengthForPreview = Math.Clamp(_homeViewState.SnapOuterStrength, 0f, 1f);

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

        var interpolationTypeIndexForPreview = _homeViewState.SnapInnerInterpolationTypeIndex >= 0 && _homeViewState.SnapInnerInterpolationTypeIndex < SnapInnerInterpolationTypeOptions.Length
            ? _homeViewState.SnapInnerInterpolationTypeIndex
            : 0;
        const int interpolationSegments = 24;
        for (var i = 0; i < interpolationSegments; i++)
        {
            var t0 = i / (float)interpolationSegments;
            var t1 = (i + 1) / (float)interpolationSegments;
            var curveT0 = SnapInterpolation.EvaluateNormalized(t0, interpolationTypeIndexForPreview);
            var curveT1 = SnapInterpolation.EvaluateNormalized(t1, interpolationTypeIndexForPreview);
            var x0 = innerRangeForPreview * t0;
            var x1 = innerRangeForPreview * t1;
            var y0 = startStrengthForPreview + (innerStrengthForPreview - startStrengthForPreview) * curveT0;
            var y1 = startStrengthForPreview + (innerStrengthForPreview - startStrengthForPreview) * curveT1;
            drawList.AddLine(MapToPlot(x0, y0), MapToPlot(x1, y1), lineColor, 2.0f);
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
            ImGui.Combo(id, ref _onnxTopSelectedModelIndex, "未找到可用模型");
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
            SyncSmartCoreVisionPipeline();
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
        TryApplyModelSelectionFromCurrentConfig();
    }

    private void RefreshConfigFiles(string? forceSelectBaseName = null)
    {
        var refreshResult = _configStore.RefreshConfigFiles(_configFiles, _selectedConfigFileIndex, forceSelectBaseName);
        _configFiles.Clear();
        _configFiles.AddRange(refreshResult.ConfigFiles);
        if (_configFiles.Count == 0)
        {
            _selectedConfigFileIndex = refreshResult.SelectedIndex;
            return;
        }

        _selectedConfigFileIndex = refreshResult.SelectedIndex;
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

    private void WriteCurrentConfigFileName(string configBaseNameWithoutExtension) =>
        _configRepository.WriteCurrentConfigFileName(configBaseNameWithoutExtension);

    private void TryWriteSelectedModelNameToCurrentConfig(string modelName)
    {
        _configStore.TryWriteString(_configFiles, _selectedConfigFileIndex, "model", modelName);
    }

    private void TryApplyModelSelectionFromCurrentConfig()
    {
        var selectionResult = _configStore.ApplyCurrentConfigSelection(
            _configFiles,
            _selectedConfigFileIndex,
            _onnxModels,
            GetDisplayHeightOrWindowHeight(),
            DefaultSnapOuterRange,
            DefaultSnapInnerRange,
            DefaultSnapOuterStrength,
            DefaultSnapInnerStrength,
            DefaultSnapStartStrength,
            DefaultSnapVerticalStrengthFactor,
            DefaultSnapHipfireStrengthFactor,
            DefaultSnapHeight,
            DefaultSnapStrengthRampTime,
            HomeSnapModeOptions,
            HomeRapidFireStrategyOptions,
            HomeGameOptions,
            SnapInnerInterpolationTypeOptions,
            GamepadBindingCatalog.Options,
            TouchpadBindingOptions,
            GamepadBindingCatalog.DefaultAimIndex,
            GamepadBindingCatalog.DefaultFireIndex,
            GamepadBindingCatalog.DefaultTouchpadLeftIndex,
            DefaultVoiceCustomKeyName,
            GamepadBindingCatalog.DefaultTouchpadLeftIndex,
            GamepadBindingCatalog.DefaultTouchpadRightIndex,
            DefaultRapidFireHz);
        if (!selectionResult.HasConfig)
        {
            _homeViewState.SnapModeIndex = -1;
            _homeViewState.RapidFireStrategyIndex = -1;
            _homeViewState.RapidFireHz = DefaultRapidFireHz;
            _homeViewState.GameIndex = 0;
            RefreshSpecialWeaponNamesForCurrentGame();
            _homeViewState.AimBindingIndex = GamepadBindingCatalog.DefaultAimIndex;
            _homeViewState.FireBindingIndex = GamepadBindingCatalog.DefaultFireIndex;
            _homeViewState.VoiceBindingIndex = GamepadBindingCatalog.DefaultTouchpadLeftIndex;
            _homeViewState.VoiceCustomKey = DefaultVoiceCustomKeyName;
            _homeViewState.TouchpadLeftBindingIndex = GamepadBindingCatalog.DefaultTouchpadLeftIndex;
            _homeViewState.TouchpadRightBindingIndex = GamepadBindingCatalog.DefaultTouchpadRightIndex;
            _homeViewState.TouchpadLeftCustomKey = GamepadBindingCatalog.DefaultCustomKeyboardKeyName;
            _homeViewState.TouchpadRightCustomKey = GamepadBindingCatalog.DefaultCustomKeyboardKeyName;
            _onnxTopSelectedModelIndex = -1;
            PushAimAssistConfig();
            SyncSmartCoreVisionPipeline();
            return;
        }

        _homeViewState.SnapModeIndex = selectionResult.SnapModeIndex;
        _homeViewState.RapidFireStrategyIndex = selectionResult.RapidFireStrategyIndex;
        _homeViewState.RapidFireHz = selectionResult.RapidFireHz;
        _homeViewState.GameIndex = selectionResult.GameIndex;
        _homeViewState.AimBindingIndex = selectionResult.AimBindingIndex;
        _homeViewState.FireBindingIndex = selectionResult.FireBindingIndex;
        _homeViewState.VoiceBindingIndex = selectionResult.VoiceBindingIndex;
        _homeViewState.VoiceCustomKey = selectionResult.VoiceCustomKey;
        _homeViewState.TouchpadLeftBindingIndex = selectionResult.TouchpadLeftBindingIndex;
        _homeViewState.TouchpadRightBindingIndex = selectionResult.TouchpadRightBindingIndex;
        _homeViewState.TouchpadLeftCustomKey = selectionResult.TouchpadLeftCustomKey;
        _homeViewState.TouchpadRightCustomKey = selectionResult.TouchpadRightCustomKey;
        RefreshSpecialWeaponNamesForCurrentGame();
        ApplySpecialWeaponLogicFromCurrentConfig();
        _onnxTopSelectedModelIndex = selectionResult.ModelIndex;
        _homeViewState.ApplySnapConfig(selectionResult.SnapConfig);
        PushAimAssistConfig();
        SyncSmartCoreVisionPipeline();
    }

    private string GetSelectedGameName()
    {
        _homeViewState.GameIndex = _homeViewState.GameIndex >= 0 && _homeViewState.GameIndex < HomeGameOptions.Length
            ? _homeViewState.GameIndex
            : 0;
        return HomeGameOptions[_homeViewState.GameIndex];
    }

    private void RefreshSpecialWeaponNamesForCurrentGame()
    {
        _specialWeaponNames = WeaponTemplateCatalog.GetWeaponNamesForGame(GetSelectedGameName());
        _specialWeaponAimSnapEnabled = new bool[_specialWeaponNames.Length];
        _specialWeaponRapidFireEnabled = new bool[_specialWeaponNames.Length];
        _specialWeaponReleaseFireEnabled = new bool[_specialWeaponNames.Length];
    }

    private void ResetConfigUiStateToDefaults()
    {
        _smartCoreMappingState.RequestedEnabled = false;
        _viGEmMappingWorker?.SetRequestedEnabled(false);
        CloseSmartCorePreviewWindow();
        _onnxTopSelectedModelIndex = -1;
        _homeViewState.ResetSnapSettings(
            0,
            WeaponBasedRapidFireStrategyIndex,
            DefaultRapidFireHz,
            GamepadBindingCatalog.DefaultAimIndex,
            GamepadBindingCatalog.DefaultFireIndex,
            GamepadBindingCatalog.DefaultTouchpadLeftIndex,
            DefaultVoiceCustomKeyName,
            GamepadBindingCatalog.DefaultTouchpadLeftIndex,
            GamepadBindingCatalog.DefaultTouchpadRightIndex,
            GamepadBindingCatalog.DefaultCustomKeyboardKeyName,
            GamepadBindingCatalog.DefaultCustomKeyboardKeyName,
            DefaultSnapOuterRange,
            DefaultSnapInnerRange,
            DefaultSnapOuterStrength,
            DefaultSnapInnerStrength,
            DefaultSnapStartStrength,
            DefaultSnapVerticalStrengthFactor,
            DefaultSnapHipfireStrengthFactor,
            DefaultSnapHeight,
            DefaultSnapStrengthRampTime,
            0);
        _homeViewState.GameIndex = 0;
        RefreshSpecialWeaponNamesForCurrentGame();
        Array.Clear(_specialWeaponAimSnapEnabled);
        Array.Clear(_specialWeaponRapidFireEnabled);
        Array.Clear(_specialWeaponReleaseFireEnabled);
        PushAimAssistConfig();
        RefreshSmartCoreState();
        SyncSmartCoreVisionPipeline();
    }

    private void TryWriteStringToCurrentConfig(string key, string value) =>
        _configStore.TryWriteString(_configFiles, _selectedConfigFileIndex, key, value);

    private void TryWriteIntToCurrentConfig(string key, int value) =>
        _configStore.TryWriteInt(_configFiles, _selectedConfigFileIndex, key, value);

    private void TryWriteFloatToCurrentConfig(string key, float value) =>
        _configStore.TryWriteFloat(_configFiles, _selectedConfigFileIndex, key, value);

    private void TryWriteBoolToCurrentConfig(string key, bool value) =>
        _configStore.TryWriteBool(_configFiles, _selectedConfigFileIndex, key, value);

    private void PersistNewConfigDefaultsToFile()
    {
        if (_configFiles.Count == 0)
        {
            return;
        }

        _homeViewState.GameIndex = _homeViewState.GameIndex >= 0 && _homeViewState.GameIndex < HomeGameOptions.Length
            ? _homeViewState.GameIndex
            : 0;
        TryWriteStringToCurrentConfig(GameConfigKey, HomeGameOptions[_homeViewState.GameIndex]);

        if (_homeViewState.SnapModeIndex >= 0 && _homeViewState.SnapModeIndex < HomeSnapModeOptions.Length)
        {
            TryWriteStringToCurrentConfig("snap", HomeSnapModeOptions[_homeViewState.SnapModeIndex]);
        }

        if (_homeViewState.RapidFireStrategyIndex >= 0 && _homeViewState.RapidFireStrategyIndex < HomeRapidFireStrategyOptions.Length)
        {
            TryWriteStringToCurrentConfig("rapidFireStrategy", HomeRapidFireStrategyOptions[_homeViewState.RapidFireStrategyIndex]);
        }

        TryWriteIntToCurrentConfig("rapidFireHz", _homeViewState.RapidFireHz);

        if (_homeViewState.AimBindingIndex >= 0 && _homeViewState.AimBindingIndex < GamepadBindingCatalog.Options.Length)
        {
            TryWriteStringToCurrentConfig(AimBindingConfigKey, GamepadBindingCatalog.Options[_homeViewState.AimBindingIndex]);
        }

        if (_homeViewState.FireBindingIndex >= 0 && _homeViewState.FireBindingIndex < GamepadBindingCatalog.Options.Length)
        {
            TryWriteStringToCurrentConfig(FireBindingConfigKey, GamepadBindingCatalog.Options[_homeViewState.FireBindingIndex]);
        }

        if (_homeViewState.VoiceBindingIndex >= 0 && _homeViewState.VoiceBindingIndex < GamepadBindingCatalog.Options.Length)
        {
            TryWriteStringToCurrentConfig(VoiceBindingConfigKey, GamepadBindingCatalog.Options[_homeViewState.VoiceBindingIndex]);
        }

        TryWriteStringToCurrentConfig(VoiceCustomKeyConfigKey, _homeViewState.VoiceCustomKey);

        if (_homeViewState.TouchpadLeftBindingIndex >= 0 && _homeViewState.TouchpadLeftBindingIndex < TouchpadBindingOptions.Length)
        {
            TryWriteStringToCurrentConfig(TouchpadLeftBindingConfigKey, TouchpadBindingOptions[_homeViewState.TouchpadLeftBindingIndex]);
        }

        if (_homeViewState.TouchpadRightBindingIndex >= 0 && _homeViewState.TouchpadRightBindingIndex < TouchpadBindingOptions.Length)
        {
            TryWriteStringToCurrentConfig(TouchpadRightBindingConfigKey, TouchpadBindingOptions[_homeViewState.TouchpadRightBindingIndex]);
        }

        TryWriteStringToCurrentConfig(TouchpadLeftCustomKeyConfigKey, _homeViewState.TouchpadLeftCustomKey);
        TryWriteStringToCurrentConfig(TouchpadRightCustomKeyConfigKey, _homeViewState.TouchpadRightCustomKey);

        if (_onnxTopSelectedModelIndex >= 0 && _onnxTopSelectedModelIndex < _onnxModels.Count)
        {
            TryWriteSelectedModelNameToCurrentConfig(_onnxModels[_onnxTopSelectedModelIndex].DisplayName);
        }
        else
        {
            ClearSelectedModelNameFromCurrentConfig();
        }

        TryWriteIntToCurrentConfig("snapOuterRange", _homeViewState.SnapOuterRange);
        TryWriteIntToCurrentConfig("snapInnerRange", _homeViewState.SnapInnerRange);
        TryWriteFloatToCurrentConfig("snapOuterStrength", _homeViewState.SnapOuterStrength);
        TryWriteFloatToCurrentConfig("snapInnerStrength", _homeViewState.SnapInnerStrength);
        TryWriteFloatToCurrentConfig("snapStartStrength", _homeViewState.SnapStartStrength);
        TryWriteFloatToCurrentConfig("snapVerticalStrengthFactor", _homeViewState.SnapVerticalStrengthFactor);
        TryWriteFloatToCurrentConfig("snapHipfireStrengthFactor", _homeViewState.SnapHipfireStrengthFactor);
        TryWriteFloatToCurrentConfig("snapHeight", _homeViewState.SnapHeight);
        TryWriteFloatToCurrentConfig("snapStrengthRampTime", _homeViewState.SnapStrengthRampTime);

        if (_homeViewState.SnapInnerInterpolationTypeIndex >= 0
            && _homeViewState.SnapInnerInterpolationTypeIndex < SnapInnerInterpolationTypeOptions.Length)
        {
            TryWriteStringToCurrentConfig(
                "snapInnerInterpolationType",
                SnapInnerInterpolationTypeOptions[_homeViewState.SnapInnerInterpolationTypeIndex]);
        }
    }

    private void TryWriteSpecialWeaponLogicValueToCurrentConfig(int weaponIndex, bool aimSnapEnabled, bool rapidFireEnabled, bool releaseFireEnabled)
    {
        _configStore.TryWriteSpecialWeaponLogic(
            _configFiles,
            _selectedConfigFileIndex,
            SpecialWeaponLogicConfigKey,
            GetSelectedGameName(),
            AimSnapWeaponListConfigKey,
            RapidFireWeaponListConfigKey,
            ReleaseFireWeaponListConfigKey,
            _specialWeaponNames,
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
        _configStore.LoadSpecialWeaponLogic(
            _configFiles,
            _selectedConfigFileIndex,
            SpecialWeaponLogicConfigKey,
            GetSelectedGameName(),
            AimSnapWeaponListConfigKey,
            RapidFireWeaponListConfigKey,
            ReleaseFireWeaponListConfigKey,
            _specialWeaponNames,
            _specialWeaponAimSnapEnabled,
            _specialWeaponRapidFireEnabled,
            _specialWeaponReleaseFireEnabled);
    }

    private string? TryReadStringFromCurrentConfig(string key) =>
        _configStore.TryReadString(_configFiles, _selectedConfigFileIndex, key);

    private void ClearSelectedModelNameFromCurrentConfig() =>
        _configStore.TryRemoveKey(_configFiles, _selectedConfigFileIndex, "model");

    private bool TryResolveCurrentConfigPath(out string configPath) =>
        _configStore.TryResolvePath(_configFiles, _selectedConfigFileIndex, out configPath);

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
        CloseSmartCorePreviewWindow();

        _viGEmMappingWorker?.Dispose();
        _viGEmMappingWorker = null;
        _sdlGamepadWorker?.Dispose();
        _sdlGamepadWorker = null;
        StopVisionPipeline();

        _controller?.Dispose();
        SaveWindowState();
        SDL.QuitSubSystem(SDL.InitFlags.Gamepad);
        base.OnUnload();
    }
    private void InitializeVirtualGamepadConnection()
    {
        try
        {
            _viGEmMappingWorker?.ConnectVirtualGamepad();
            RefreshSmartCoreState();
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
                IsMaximized = WindowState == WindowState.Maximized,
                SelectedGamepadInstanceId = _homeSelectedGamepadInstanceId
            };
            WindowStateService.Save(WindowStateFilePath, snapshot);
        }
        catch
        {
            // Ignore persistence failures to avoid blocking shutdown.
        }
    }

    private void ApplySelectedGamepadSelection()
    {
        uint? selectedInstanceId = null;
        if (_homeSelectedGamepadIndex >= 0 && _homeSelectedGamepadIndex < _cachedConnectedGamepads.Length)
        {
            selectedInstanceId = _cachedConnectedGamepads[_homeSelectedGamepadIndex].InstanceId;
        }

        _homeSelectedGamepadInstanceId = selectedInstanceId;
        _viGEmMappingWorker?.SetSelectedGamepad(selectedInstanceId);
        RefreshSmartCoreState();
    }

    private void ResolveSelectedGamepadIndexFromInstanceId()
    {
        if (_cachedConnectedGamepads.Length == 0)
        {
            _homeSelectedGamepadIndex = -1;
            return;
        }

        if (_homeSelectedGamepadInstanceId.HasValue)
        {
            for (var i = 0; i < _cachedConnectedGamepads.Length; i++)
            {
                if (_cachedConnectedGamepads[i].InstanceId == _homeSelectedGamepadInstanceId.Value)
                {
                    _homeSelectedGamepadIndex = i;
                    return;
                }
            }
        }

        _homeSelectedGamepadIndex = 0;
        _homeSelectedGamepadInstanceId = _cachedConnectedGamepads[0].InstanceId;
    }

    private void PushAimAssistConfig()
    {
        if (_viGEmMappingWorker is null)
        {
            return;
        }

        var config = new SmartCoreAimAssistConfigState(
            _smartCoreMappingState.IsEnabled,
            _smartCoreMappingState.IsMappingActive,
            _homeViewState.SnapModeIndex,
            _homeViewState.RapidFireStrategyIndex,
            _homeViewState.RapidFireHz,
            _homeViewState.SnapOuterRange,
            _homeViewState.SnapInnerRange,
            _homeViewState.SnapOuterStrength,
            _homeViewState.SnapInnerStrength,
            _homeViewState.SnapStartStrength,
            _homeViewState.SnapVerticalStrengthFactor,
            _homeViewState.SnapHipfireStrengthFactor,
            _homeViewState.SnapHeight,
            _homeViewState.SnapStrengthRampTime,
            _homeViewState.SnapInnerInterpolationTypeIndex,
            _homeViewState.AimBindingIndex,
            _homeViewState.FireBindingIndex,
            _homeViewState.VoiceBindingIndex,
            _homeViewState.VoiceCustomKey,
            _homeViewState.TouchpadLeftBindingIndex,
            _homeViewState.TouchpadRightBindingIndex,
            _homeViewState.TouchpadLeftCustomKey,
            _homeViewState.TouchpadRightCustomKey,
            BuildEnabledWeaponNameList(_specialWeaponAimSnapEnabled),
            BuildEnabledWeaponNameList(_specialWeaponRapidFireEnabled),
            BuildEnabledWeaponNameList(_specialWeaponReleaseFireEnabled));
        _viGEmMappingWorker.SetAimAssistConfig(config);
        SyncWeaponRecognitionEnabled();
    }

    private string[] BuildEnabledWeaponNameList(IReadOnlyList<bool> enabledFlags)
    {
        if (_specialWeaponNames.Length == 0 || enabledFlags.Count == 0)
        {
            return Array.Empty<string>();
        }

        var enabled = new List<string>(_specialWeaponNames.Length);
        for (var i = 0; i < _specialWeaponNames.Length; i++)
        {
            if (i < enabledFlags.Count && enabledFlags[i])
            {
                enabled.Add(_specialWeaponNames[i]);
            }
        }

        return enabled.ToArray();
    }

    private void RefreshSmartCoreState()
    {
        _smartCoreMappingState.IsViGemBusReady = Directory.Exists(ViGemBusInstallPath);
        _smartCoreMappingState.HasInputDevice = _cachedConnectedGamepads.Length > 0;
        _smartCoreMappingState.IsEnabled = _smartCoreMappingState.RequestedEnabled && _smartCoreMappingState.IsDependenciesReady;

        var snapshot = _viGEmMappingWorker?.GetSnapshot();
        _smartCoreMappingState.IsMappingActive = snapshot?.IsMappingActive ?? false;
        _smartCoreMappingState.LastError = snapshot?.LastError ?? string.Empty;
    }
}


