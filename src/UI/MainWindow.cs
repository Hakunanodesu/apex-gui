using System.Numerics;
using System.Diagnostics;
using System.Threading;
using ImGuiNET;
using OpenTK.Graphics.OpenGL4;
using OpenTK.Windowing.Common;
using OpenTK.Windowing.Desktop;
using OpenTK.Windowing.GraphicsLibraryFramework;
using Keys = OpenTK.Windowing.GraphicsLibraryFramework.Keys;
using SDL3;
using Vortice.Direct3D;
using Vortice.Direct3D11;
using Vortice.DXGI;

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
    private static readonly string[] HomeSnapModeOptions = { "Fire Snap", "Aim Snap" };
    private static readonly string[] SnapInnerInterpolationTypeOptions = { "Linear", "Quadratic" };
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
    private readonly HomeViewState _homeViewState = new();
    private readonly bool[] _specialWeaponAimSnapEnabled = new bool[SpecialWeaponNames.Length];
    private readonly bool[] _specialWeaponRapidFireEnabled = new bool[SpecialWeaponNames.Length];
    private readonly bool[] _specialWeaponReleaseFireEnabled = new bool[SpecialWeaponNames.Length];
    private readonly List<string> _configFiles = new();
    private int _selectedConfigFileIndex;
    private int _homeSelectedGamepadIndex;
    private int _debugSelectedGamepadIndex;
    private OpenTK.Mathematics.Vector2i _lastNormalClientSize;
    private SdlGamepadWorker? _sdlGamepadWorker;
    private ViGEmMappingWorker? _viGEmMappingWorker;
    private readonly ConfigRepository _configRepository = new(ConfigsDirectoryPath);
    private readonly ConfigSession _configSession;
    private readonly InputDevicesFacade _inputDevicesFacade = new();
    private readonly SmartCoreFacade _smartCoreFacade = new();
    private readonly SmartCoreAimAssistService _smartCoreAimAssistService = new();
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
        _configSession = new ConfigSession(_configRepository);
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
            ImGui.Combo(id, ref _onnxTopSelectedModelIndex, "閺冪姴褰查悽銊δ侀崹濯?");
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
        var refreshResult = _configSession.RefreshConfigFiles(_configFiles, _selectedConfigFileIndex, forceSelectBaseName);
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
        _configSession.TryWriteString(_configFiles, _selectedConfigFileIndex, "model", modelName);
    }

    private void TryApplyModelSelectionFromCurrentConfig()
    {
        var selectionResult = _configSession.ApplyCurrentConfigSelection(
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
            HomeSnapModeOptions,
            SnapInnerInterpolationTypeOptions);
        if (!selectionResult.HasConfig)
        {
            _homeViewState.SnapModeIndex = -1;
            _onnxTopSelectedModelIndex = -1;
            return;
        }

        _homeViewState.SnapModeIndex = selectionResult.SnapModeIndex;
        ApplySpecialWeaponLogicFromCurrentConfig();
        _onnxTopSelectedModelIndex = selectionResult.ModelIndex;
        _homeViewState.ApplySnapConfig(selectionResult.SnapConfig);
    }

    private void ResetConfigUiStateToDefaults()
    {
        _smartCoreMappingState.RequestedEnabled = false;
        _onnxTopSelectedModelIndex = -1;
        _homeViewState.ResetSnapSettings(
            0,
            DefaultSnapOuterRange,
            DefaultSnapInnerRange,
            DefaultSnapOuterStrength,
            DefaultSnapInnerStrength,
            DefaultSnapStartStrength,
            DefaultSnapVerticalStrengthFactor,
            DefaultSnapHipfireStrengthFactor,
            DefaultSnapHeight,
            0);
        Array.Clear(_specialWeaponAimSnapEnabled);
        Array.Clear(_specialWeaponRapidFireEnabled);
        Array.Clear(_specialWeaponReleaseFireEnabled);
    }

    private void TryWriteStringToCurrentConfig(string key, string value) =>
        _configSession.TryWriteString(_configFiles, _selectedConfigFileIndex, key, value);

    private void TryWriteIntToCurrentConfig(string key, int value) =>
        _configSession.TryWriteInt(_configFiles, _selectedConfigFileIndex, key, value);

    private void TryWriteFloatToCurrentConfig(string key, float value) =>
        _configSession.TryWriteFloat(_configFiles, _selectedConfigFileIndex, key, value);

    private void TryWriteSpecialWeaponLogicValueToCurrentConfig(int weaponIndex, bool aimSnapEnabled, bool rapidFireEnabled, bool releaseFireEnabled)
    {
        _configSession.TryWriteSpecialWeaponLogic(
            _configFiles,
            _selectedConfigFileIndex,
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
        _configSession.LoadSpecialWeaponLogic(
            _configFiles,
            _selectedConfigFileIndex,
            SpecialWeaponLogicConfigKey,
            AimSnapWeaponListConfigKey,
            RapidFireWeaponListConfigKey,
            ReleaseFireWeaponListConfigKey,
            SpecialWeaponNames,
            _specialWeaponAimSnapEnabled,
            _specialWeaponRapidFireEnabled,
            _specialWeaponReleaseFireEnabled);
    }

    private string? TryReadStringFromCurrentConfig(string key) =>
        _configSession.TryReadString(_configFiles, _selectedConfigFileIndex, key);

    private void ClearSelectedModelNameFromCurrentConfig() =>
        _configSession.TryRemoveKey(_configFiles, _selectedConfigFileIndex, "model");

    private bool TryResolveCurrentConfigPath(out string configPath) =>
        _configSession.TryResolvePath(_configFiles, _selectedConfigFileIndex, out configPath);

/*
    private void DrawConfigFileModals()
    {
        if (_configAddModalOpenRequested)
        {
            ImGui.OpenPopup("鐠囩柉绶崗銉︽煀闁板秶鐤嗛崥宥囆?);
            _configAddModalOpenRequested = false;
        }

        if (ImGui.BeginPopupModal("鐠囩柉绶崗銉︽煀闁板秶鐤嗛崥宥囆?, ref _configAddModalOpen, ImGuiWindowFlags.AlwaysAutoResize))
        {
            ImGui.InputText("##AddConfigNameInput", ref _addConfigNameBuffer, 256);
            if (!string.IsNullOrEmpty(_configAddModalError))
            {
                ImGui.AlignTextToFramePadding();
                ImGui.TextUnformatted(_configAddModalError);
            }

            if (ImGui.Button("閸掓稑缂?))
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
            if (ImGui.Button("閸欐牗绉?))
            {
                _configAddModalError = string.Empty;
                _configAddModalOpen = false;
                ImGui.CloseCurrentPopup();
            }

            ImGui.EndPopup();
        }

        if (_configDeleteModalOpenRequested)
        {
            ImGui.OpenPopup("閸掔娀娅庨柊宥囩枂绾喛顓?);
            _configDeleteModalOpenRequested = false;
        }

        if (ImGui.BeginPopupModal("閸掔娀娅庨柊宥囩枂绾喛顓?, ref _configDeleteModalOpen, ImGuiWindowFlags.AlwaysAutoResize))
        {
            var name = _pendingDeleteConfigBaseName ?? string.Empty;
            ImGui.AlignTextToFramePadding();
            ImGui.TextUnformatted($"绾喖鐣鹃崚鐘绘珟闁板秶鐤嗛弬鍥︽ {name} 閸氭绱靛銈嗘惙娴ｆ粈绗夐崣顖涙寵闁库偓閵?);
            if (ImGui.Button("绾喖鐣?))
            {
                TryDeleteSelectedConfigFile(name);
                _pendingDeleteConfigBaseName = null;
                _configDeleteModalOpen = false;
                ImGui.CloseCurrentPopup();
            }

            ImGui.SameLine();
            if (ImGui.Button("閸欐牗绉?))
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
            _configService.ClearCurrentConfigPointerFile();
        }
    }

*/
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
        StopOnnxInference("Disposed");
        StopDxgiCapture("Disposed");
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

