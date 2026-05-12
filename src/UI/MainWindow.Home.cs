using ImGuiNET;
using System.Numerics;
using Keys = OpenTK.Windowing.GraphicsLibraryFramework.Keys;

public sealed partial class MainWindow
{
    private enum TouchpadKeyCaptureTarget
    {
        None = 0,
        Left = 1,
        Right = 2,
        Voice = 3
    }

    private const float SnapFloatStep = 0.01f;
    private const string SnapFloatFormat = "%.2f";
#if DEBUG
    private static readonly TimeSpan DebugTouchpadTestCountdown = TimeSpan.FromSeconds(3);
    private static readonly TimeSpan DebugTouchpadBurstDuration = TimeSpan.FromSeconds(3);

    private sealed class DebugTouchpadTestState
    {
        public DateTime CountdownEndsAtUtc;
        public DateTime BurstEndsAtUtc;
        public bool IsActive;
        public bool BurstStarted;
        public string CustomKey = string.Empty;
    }

    private readonly DebugTouchpadTestState _leftDebugTouchpadTestState = new();
    private readonly DebugTouchpadTestState _rightDebugTouchpadTestState = new();
#endif

    private readonly record struct HomeLayoutMetrics(
        float FirstColumnWidth,
        float ReserveWidth,
        float AddButtonWidth,
        float DeleteButtonWidth);

    private readonly record struct SnapSettingsLayout(
        float LabelWidth,
        float LastLabelWidth,
        float RangeInputWidth,
        float StrengthInputWidth,
        float ExtraInputWidth);

    private void DrawHomeTab()
    {
        RefreshSmartCoreState();

        var topPanelStyle = ImGui.GetStyle();
        var addButtonWidth = ImGui.CalcTextSize("添加").X + topPanelStyle.FramePadding.X * 2f;
        var deleteButtonWidth = ImGui.CalcTextSize("删除").X + topPanelStyle.FramePadding.X * 2f;
        var firstColumnWidth = MeasureMaxTextWidth(
                                   "依赖状态",
                                   "配置选择",
                                   "智慧核心",
                                   "选择模型",
                                   "吸附参数设定",
                                   "吸附曲线预览",
                                   "按键绑定",
                                   "开启吸附方式",
                                   "特殊武器逻辑")
                               + topPanelStyle.CellPadding.X * 2f;
        var reserveWidth = addButtonWidth + deleteButtonWidth + topPanelStyle.ItemSpacing.X * 2f;
        var metrics = new HomeLayoutMetrics(firstColumnWidth, reserveWidth, addButtonWidth, deleteButtonWidth);

        DrawHomeTopTable(metrics, topPanelStyle);
        DrawConfigFileModals();

        ImGui.Spacing();
        ImGui.Separator();
        ImGui.Separator();
        ImGui.Spacing();

        DrawHomeMainTable(metrics, topPanelStyle);
    }

    private void DrawHomeTopTable(HomeLayoutMetrics metrics, ImGuiStylePtr topPanelStyle)
    {
        var vigemReady = _smartCoreMappingState.IsViGemBusReady;
        if (!ImGui.BeginTable("##HomeTopTable", 2, ImGuiTableFlags.SizingStretchProp))
        {
            return;
        }

        ImGui.TableSetupColumn("Label", ImGuiTableColumnFlags.WidthFixed, metrics.FirstColumnWidth);
        ImGui.TableSetupColumn("Content", ImGuiTableColumnFlags.WidthStretch);

        DrawDependencyStatusRow(vigemReady, metrics, topPanelStyle);
        DrawConfigSelectionRow(metrics, topPanelStyle);
        DrawSmartCoreRow();

        ImGui.EndTable();
    }

    private void DrawDependencyStatusRow(bool vigemReady, HomeLayoutMetrics metrics, ImGuiStylePtr topPanelStyle)
    {
        ImGui.TableNextRow();
        ImGui.TableSetColumnIndex(0);
        ImGui.AlignTextToFramePadding();
        ImGui.TextUnformatted("依赖状态");
        ImGui.TableSetColumnIndex(1);

        var vigemActionLabel = vigemReady ? "重新安装" : "安装";
        var gamepads = GetConnectedGamepadOptions();
        var hasGamepads = gamepads.Length > 0;
        ImGui.SetCursorPosY(ImGui.GetCursorPosY() - topPanelStyle.CellPadding.Y);

        if (!ImGui.BeginTable("##DependencyStatusSubTable", 3, ImGuiTableFlags.SizingStretchProp))
        {
            return;
        }

        ImGui.TableSetupColumn("##DepName", ImGuiTableColumnFlags.WidthFixed, ImGui.CalcTextSize("ViGemBus 驱动").X);
        var depStateColumnWidth = MeasureMaxTextWidth("已就绪", "未就绪") + topPanelStyle.CellPadding.X * 2f;
        ImGui.TableSetupColumn("##DepState", ImGuiTableColumnFlags.WidthFixed, depStateColumnWidth);
        ImGui.TableSetupColumn("##DepAction", ImGuiTableColumnFlags.WidthStretch);

        ImGui.TableNextRow();
        ImGui.TableSetColumnIndex(0);
        ImGui.AlignTextToFramePadding();
        ImGui.TextUnformatted("ViGemBus 驱动");
        ImGui.TableSetColumnIndex(1);
        ImGui.AlignTextToFramePadding();
        ImGui.TextUnformatted(vigemReady ? "已就绪" : "未就绪");
        ImGui.TableSetColumnIndex(2);
        if (ImGui.Button(vigemActionLabel))
        {
            OpenViGemBusInstaller();
        }

        ImGui.TableNextRow();
        ImGui.TableNextRow();
        ImGui.TableSetColumnIndex(0);
        ImGui.AlignTextToFramePadding();
        ImGui.TextUnformatted("输入设备");
        ImGui.TableSetColumnIndex(1);
        ImGui.AlignTextToFramePadding();
        ImGui.TextUnformatted(hasGamepads ? "已就绪" : "未就绪");
        ImGui.TableSetColumnIndex(2);
        var gamepadIndexBeforeUi = _homeSelectedGamepadIndex;
        var inputRefreshButtonWidth = ImGui.CalcTextSize("刷新").X + topPanelStyle.FramePadding.X * 2f;
        ImGui.SetNextItemWidth(ImGui.GetContentRegionAvail().X - metrics.ReserveWidth);
        ImGui.Combo("##InputDeviceCombo", ref _homeSelectedGamepadIndex, gamepads, gamepads.Length);
        if (_homeSelectedGamepadIndex != gamepadIndexBeforeUi)
        {
            ApplySelectedGamepadSelection();
        }
        ImGui.SameLine();
        if (ImGui.Button("刷新##HomeInputDeviceRefresh", new Vector2(inputRefreshButtonWidth, 0f)))
        {
            RefreshHomeInputDevices();
        }

        ImGui.EndTable();
    }

    private void DrawConfigSelectionRow(HomeLayoutMetrics metrics, ImGuiStylePtr topPanelStyle)
    {
        var disableConfigSelection = _smartCoreMappingState.IsEnabled;
        ImGui.TableNextRow();
        ImGui.TableNextRow();
        ImGui.TableSetColumnIndex(0);
        ImGui.SetCursorPosY(ImGui.GetCursorPosY() - topPanelStyle.CellPadding.Y);
        ImGui.AlignTextToFramePadding();
        ImGui.TextUnformatted("配置选择");
        ImGui.TableSetColumnIndex(1);
        ImGui.SetCursorPosY(ImGui.GetCursorPosY() - topPanelStyle.CellPadding.Y);
        var configComboWidth = ImGui.GetContentRegionAvail().X - metrics.ReserveWidth;
        ImGui.BeginDisabled(disableConfigSelection);
        DrawConfigFileCombo("##TopConfigCombo", configComboWidth);
        ImGui.SameLine();
        if (ImGui.Button("添加", new Vector2(metrics.AddButtonWidth, 0f)))
        {
            _homeViewState.OpenAddModal();
        }

        ImGui.SameLine();
        if (_configFiles.Count > 0)
        {
            if (ImGui.Button("删除", new Vector2(metrics.DeleteButtonWidth, 0f)))
            {
                _homeViewState.OpenDeleteModal(_configFiles[Math.Clamp(_selectedConfigFileIndex, 0, _configFiles.Count - 1)]);
            }
        }
        else
        {
            ImGui.BeginDisabled();
            ImGui.Button("删除", new Vector2(metrics.DeleteButtonWidth, 0f));
            ImGui.EndDisabled();
        }
        ImGui.EndDisabled();
    }

    private void DrawSmartCoreRow()
    {
        ImGui.TableNextRow();
        ImGui.TableNextRow();
        ImGui.TableSetColumnIndex(0);
        ImGui.AlignTextToFramePadding();
        ImGui.TextUnformatted("智慧核心");
        ImGui.TableSetColumnIndex(1);
        ImGui.BeginDisabled(!_smartCoreMappingState.IsDependenciesReady);
        var requestedSmartCoreEnabled = _smartCoreMappingState.RequestedEnabled;
        if (ImGui.Checkbox("##SmartCoreEnabledCheckbox", ref requestedSmartCoreEnabled))
        {
            _smartCoreMappingState.RequestedEnabled = requestedSmartCoreEnabled;
            _viGEmMappingWorker?.SetRequestedEnabled(requestedSmartCoreEnabled);
            if (!requestedSmartCoreEnabled)
            {
                CloseSmartCorePreviewWindow();
            }
            RefreshSmartCoreState();
            PushAimAssistConfig();
            SyncSmartCoreVisionPipeline();
        }
        ImGui.EndDisabled();
        ImGui.SameLine();
        var smartCorePreviewWindowOpen = IsSmartCorePreviewWindowOpen();
        ImGui.BeginDisabled(!_smartCoreMappingState.RequestedEnabled || smartCorePreviewWindowOpen);
        if (ImGui.Button("预览##SmartCorePreviewButton"))
        {
            OpenSmartCorePreviewWindow();
        }
        ImGui.EndDisabled();
    }

    private void DrawHomeMainTable(HomeLayoutMetrics metrics, ImGuiStylePtr topPanelStyle)
    {
        var modelLineStyle = ImGui.GetStyle();
        var refreshButtonWidth = ImGui.CalcTextSize("刷新").X + modelLineStyle.FramePadding.X * 2f;
        if (!ImGui.BeginTable("##HomeMainTable", 2, ImGuiTableFlags.SizingStretchProp))
        {
            return;
        }

        ImGui.TableSetupColumn("Label", ImGuiTableColumnFlags.WidthFixed, metrics.FirstColumnWidth);
        ImGui.TableSetupColumn("Content", ImGuiTableColumnFlags.WidthStretch);

        ImGui.TableNextRow();
        ImGui.TableSetColumnIndex(0);
        ImGui.AlignTextToFramePadding();
        ImGui.TextUnformatted("选择模型");
        ImGui.TableSetColumnIndex(1);
        var modelComboWidth = ImGui.GetContentRegionAvail().X - metrics.ReserveWidth;
        ImGui.BeginDisabled(_smartCoreMappingState.IsEnabled);
        DrawHomeModelCombo("##HomeModelCombo", modelComboWidth);
        ImGui.SameLine();
        if (ImGui.Button("刷新", new Vector2(refreshButtonWidth, 0f)))
        {
            RefreshOnnxModels();
        }
        ImGui.EndDisabled();

        DrawSnapSettingsSection(metrics, topPanelStyle);
        DrawSnapCurveSection(topPanelStyle);
        DrawKeyBindingSection(metrics.ReserveWidth, topPanelStyle);
        DrawSnapModeSection(metrics.ReserveWidth, topPanelStyle);
        DrawSpecialWeaponLogicSection(topPanelStyle);

        ImGui.EndTable();
    }

    private void DrawSnapSettingsSection(HomeLayoutMetrics metrics, ImGuiStylePtr topPanelStyle)
    {
        ImGui.TableNextRow();
        ImGui.TableNextRow();
        ImGui.TableSetColumnIndex(0);
        ImGui.AlignTextToFramePadding();
        ImGui.TextUnformatted("吸附参数设定");
        ImGui.TableSetColumnIndex(1);

        var selectedModelSize = _onnxTopSelectedModelIndex >= 0 && _onnxTopSelectedModelIndex < _onnxModels.Count
            ? Math.Max(1, _onnxModels[_onnxTopSelectedModelIndex].InputHeight)
            : 1;
        var displayHeightLimit = GetDisplayHeightOrWindowHeight();
        var snapOuterRangeMax = Math.Max(selectedModelSize, displayHeightLimit);
        NormalizeSnapSettings(selectedModelSize, snapOuterRangeMax);
        var layout = BuildSnapSettingsLayout(topPanelStyle);

        ImGui.SetCursorPosY(ImGui.GetCursorPosY() - topPanelStyle.CellPadding.Y);
        if (ImGui.BeginTable("##SnapSettingsGrid", 6, ImGuiTableFlags.SizingFixedFit))
        {
            SetupSnapSettingsGridColumns(layout);
            DrawSnapRangeRow(layout, selectedModelSize, snapOuterRangeMax);
            DrawSnapStrengthRow(layout);
            DrawSnapExtraRow(layout);

            ImGui.EndTable();
        }

        DrawSnapInterpolationTypeRow(metrics.ReserveWidth, layout.LabelWidth);
    }

    private void NormalizeSnapSettings(int selectedModelSize, int snapOuterRangeMax)
    {
        _homeViewState.SnapOuterRange = Math.Clamp(_homeViewState.SnapOuterRange, selectedModelSize, snapOuterRangeMax);
        _homeViewState.SnapInnerRange = Math.Clamp(_homeViewState.SnapInnerRange, 1, _homeViewState.SnapOuterRange);
        _homeViewState.SnapOuterStrength = Math.Clamp(_homeViewState.SnapOuterStrength, 0f, 1f);
        _homeViewState.SnapInnerStrength = Math.Clamp(_homeViewState.SnapInnerStrength, 0f, 1f);
        _homeViewState.SnapStartStrength = Math.Clamp(_homeViewState.SnapStartStrength, 0f, 1f);
        _homeViewState.SnapVerticalStrengthFactor = Math.Clamp(_homeViewState.SnapVerticalStrengthFactor, 0f, 1f);
        _homeViewState.SnapHipfireStrengthFactor = Math.Clamp(_homeViewState.SnapHipfireStrengthFactor, 0f, 1f);
        _homeViewState.SnapHeight = Math.Clamp(_homeViewState.SnapHeight, 0f, 1f);
    }

    private static SnapSettingsLayout BuildSnapSettingsLayout(ImGuiStylePtr topPanelStyle)
    {
        var rangeInputWidth = ImGui.CalcTextSize("0000").X + topPanelStyle.FramePadding.X * 2f;
        var strengthInputWidth = rangeInputWidth + ImGui.GetFrameHeight() * 2f + topPanelStyle.ItemInnerSpacing.X * 2f;
        var labelWidth = MeasureMaxTextWidth(
                             "内圈范围",
                             "外圈范围",
                             "内圈强度",
                             "外圈强度",
                             "腰射强度系数",
                             "垂直强度系数")
                         + topPanelStyle.CellPadding.X * 2f;
        var lastLabelWidth = MeasureMaxTextWidth("起始强度", "吸附高度") + topPanelStyle.CellPadding.X * 2f;
        return new SnapSettingsLayout(
            labelWidth,
            lastLabelWidth,
            rangeInputWidth,
            strengthInputWidth,
            strengthInputWidth);
    }

    private static float MeasureMaxTextWidth(params string[] texts)
    {
        var maxWidth = 0f;
        for (var i = 0; i < texts.Length; i++)
        {
            maxWidth = MathF.Max(maxWidth, ImGui.CalcTextSize(texts[i]).X);
        }

        return maxWidth;
    }

    private static void SetupSnapSettingsGridColumns(in SnapSettingsLayout layout)
    {
        ImGui.TableSetupColumn("##SnapLabelCol0", ImGuiTableColumnFlags.WidthFixed, layout.LabelWidth);
        ImGui.TableSetupColumn("##SnapInputCol0", ImGuiTableColumnFlags.WidthFixed, layout.StrengthInputWidth);
        ImGui.TableSetupColumn("##SnapLabelCol1", ImGuiTableColumnFlags.WidthFixed, layout.LabelWidth);
        ImGui.TableSetupColumn("##SnapInputCol1", ImGuiTableColumnFlags.WidthFixed, layout.StrengthInputWidth);
        ImGui.TableSetupColumn("##SnapLabelCol2", ImGuiTableColumnFlags.WidthFixed, layout.LastLabelWidth);
        ImGui.TableSetupColumn("##SnapInputCol2", ImGuiTableColumnFlags.WidthFixed, layout.StrengthInputWidth);
    }

    private void DrawSnapRangeRow(in SnapSettingsLayout layout, int selectedModelSize, int snapOuterRangeMax)
    {
        ImGui.TableNextRow();
        ImGui.TableSetColumnIndex(0);
        ImGui.AlignTextToFramePadding();
        ImGui.TextUnformatted("内圈范围");
        ImGui.TableSetColumnIndex(1);
        ImGui.SetNextItemWidth(layout.RangeInputWidth);
        var snapInnerRange = _homeViewState.SnapInnerRange;
        if (ImGui.InputInt("##SnapInnerRange", ref snapInnerRange, 0, 0))
        {
            _homeViewState.SnapInnerRange = Math.Clamp(snapInnerRange, 1, _homeViewState.SnapOuterRange);
            TryWriteIntToCurrentConfig("snapInnerRange", _homeViewState.SnapInnerRange);
            PushAimAssistConfig();
        }

        ImGui.TableSetColumnIndex(2);
        ImGui.AlignTextToFramePadding();
        ImGui.TextUnformatted("外圈范围");
        ImGui.TableSetColumnIndex(3);
        ImGui.SetNextItemWidth(layout.RangeInputWidth);
        var snapOuterRange = _homeViewState.SnapOuterRange;
        if (ImGui.InputInt("##SnapOuterRange", ref snapOuterRange, 0, 0))
        {
            _homeViewState.SnapOuterRange = Math.Clamp(snapOuterRange, selectedModelSize, snapOuterRangeMax);
            _homeViewState.SnapInnerRange = Math.Clamp(_homeViewState.SnapInnerRange, 1, _homeViewState.SnapOuterRange);
            TryWriteIntToCurrentConfig("snapOuterRange", _homeViewState.SnapOuterRange);
            TryWriteIntToCurrentConfig("snapInnerRange", _homeViewState.SnapInnerRange);
            PushAimAssistConfig();
            SyncSmartCoreVisionPipeline();
        }

        ImGui.TableSetColumnIndex(5);
        var snapRangePreviewWindowOpen = IsSnapRangePreviewWindowOpen();
        ImGui.BeginDisabled(snapRangePreviewWindowOpen);
        if (ImGui.Button("范围预览##SnapRangePreviewWindowButton", new Vector2(layout.ExtraInputWidth, 0f)))
        {
            OpenSnapRangePreviewWindow();
        }
        ImGui.EndDisabled();
    }

    private void DrawSnapStrengthRow(in SnapSettingsLayout layout)
    {
        ImGui.TableNextRow();
        ImGui.TableNextRow();

        ImGui.TableSetColumnIndex(0);
        ImGui.AlignTextToFramePadding();
        ImGui.TextUnformatted("内圈强度");
        ImGui.TableSetColumnIndex(1);
        ImGui.SetNextItemWidth(layout.StrengthInputWidth);
        DrawClampedConfigFloatInput(
            "##SnapInnerStrength",
            "snapInnerStrength",
            _homeViewState.SnapInnerStrength,
            value => _homeViewState.SnapInnerStrength = value,
            0f,
            1f);

        ImGui.TableSetColumnIndex(2);
        ImGui.AlignTextToFramePadding();
        ImGui.TextUnformatted("外圈强度");
        ImGui.TableSetColumnIndex(3);
        ImGui.SetNextItemWidth(layout.StrengthInputWidth);
        DrawClampedConfigFloatInput(
            "##SnapOuterStrength",
            "snapOuterStrength",
            _homeViewState.SnapOuterStrength,
            value => _homeViewState.SnapOuterStrength = value,
            0f,
            1f);

        ImGui.TableSetColumnIndex(4);
        ImGui.AlignTextToFramePadding();
        ImGui.TextUnformatted("起始强度");
        ImGui.TableSetColumnIndex(5);
        ImGui.SetNextItemWidth(layout.ExtraInputWidth);
        DrawClampedConfigFloatInput(
            "##SnapStartStrength",
            "snapStartStrength",
            _homeViewState.SnapStartStrength,
            value => _homeViewState.SnapStartStrength = value,
            0f,
            1f);
    }

    private void DrawSnapExtraRow(in SnapSettingsLayout layout)
    {
        ImGui.TableNextRow();
        ImGui.TableNextRow();

        ImGui.TableSetColumnIndex(0);
        ImGui.AlignTextToFramePadding();
        ImGui.TextUnformatted("腰射强度系数");
        ImGui.TableSetColumnIndex(1);
        ImGui.SetNextItemWidth(layout.ExtraInputWidth);
        DrawClampedConfigFloatInput(
            "##SnapHipfireStrengthFactor",
            "snapHipfireStrengthFactor",
            _homeViewState.SnapHipfireStrengthFactor,
            value => _homeViewState.SnapHipfireStrengthFactor = value,
            0f,
            1f);

        ImGui.TableSetColumnIndex(2);
        ImGui.AlignTextToFramePadding();
        ImGui.TextUnformatted("垂直强度系数");
        ImGui.TableSetColumnIndex(3);
        ImGui.SetNextItemWidth(layout.ExtraInputWidth);
        DrawClampedConfigFloatInput(
            "##SnapVerticalStrengthFactor",
            "snapVerticalStrengthFactor",
            _homeViewState.SnapVerticalStrengthFactor,
            value => _homeViewState.SnapVerticalStrengthFactor = value,
            0f,
            1f);

        ImGui.TableSetColumnIndex(4);
        ImGui.AlignTextToFramePadding();
        ImGui.TextUnformatted("吸附高度");
        ImGui.TableSetColumnIndex(5);
        ImGui.SetNextItemWidth(layout.ExtraInputWidth);
        DrawClampedConfigFloatInput(
            "##SnapHeight",
            "snapHeight",
            _homeViewState.SnapHeight,
            value => _homeViewState.SnapHeight = value,
            0f,
            1f);
    }

    private void DrawSnapInterpolationTypeRow(float reserveWidth, float labelWidth)
    {
        if (!ImGui.BeginTable("##SnapInterpolationRow", 2, ImGuiTableFlags.SizingStretchProp))
        {
            return;
        }

        ImGui.TableSetupColumn("##SnapInterpolationLabel", ImGuiTableColumnFlags.WidthFixed, labelWidth);
        ImGui.TableSetupColumn("##SnapInterpolationInput", ImGuiTableColumnFlags.WidthStretch);
        ImGui.TableNextRow();
        _homeViewState.SnapInnerInterpolationTypeIndex =
            _homeViewState.SnapInnerInterpolationTypeIndex >= 0 && _homeViewState.SnapInnerInterpolationTypeIndex < SnapInnerInterpolationTypeOptions.Length
                ? _homeViewState.SnapInnerInterpolationTypeIndex
                : 0;

        ImGui.TableSetColumnIndex(0);
        ImGui.AlignTextToFramePadding();
        ImGui.TextUnformatted("内圈插值类型");

        ImGui.TableSetColumnIndex(1);
        var interpolationComboWidth = MathF.Max(90f, ImGui.GetContentRegionAvail().X - reserveWidth);
        ImGui.SetNextItemWidth(interpolationComboWidth);
        var selectedSnapInnerInterpolationLabel = SnapInnerInterpolationTypeOptions[_homeViewState.SnapInnerInterpolationTypeIndex];
        if (ImGui.BeginCombo("##SnapInnerInterpolationTypeCombo", selectedSnapInnerInterpolationLabel))
        {
            for (var i = 0; i < SnapInnerInterpolationTypeOptions.Length; i++)
            {
                var isSelected = i == _homeViewState.SnapInnerInterpolationTypeIndex;
                if (ImGui.Selectable(SnapInnerInterpolationTypeOptions[i], isSelected))
                {
                    _homeViewState.SnapInnerInterpolationTypeIndex = i;
                    TryWriteStringToCurrentConfig("snapInnerInterpolationType", SnapInnerInterpolationTypeOptions[i]);
                    PushAimAssistConfig();
                }

                if (isSelected)
                {
                    ImGui.SetItemDefaultFocus();
                }
            }

            ImGui.EndCombo();
        }

        ImGui.EndTable();
    }

    private void DrawClampedConfigFloatInput(
        string controlId,
        string configKey,
        float currentValue,
        Action<float> setValue,
        float min,
        float max)
    {
        var editedValue = currentValue;
        if (!ImGui.InputFloat(controlId, ref editedValue, SnapFloatStep, SnapFloatStep, SnapFloatFormat))
        {
            return;
        }

        var clampedValue = Math.Clamp(editedValue, min, max);
        setValue(clampedValue);
        TryWriteFloatToCurrentConfig(configKey, clampedValue);
        PushAimAssistConfig();
    }

    private void DrawSnapCurveSection(ImGuiStylePtr topPanelStyle)
    {
        ImGui.TableNextRow();
        ImGui.TableNextRow();
        ImGui.TableSetColumnIndex(0);
        // ImGui.SetCursorPosY(ImGui.GetCursorPosY() - topPanelStyle.CellPadding.Y);
        ImGui.AlignTextToFramePadding();
        ImGui.TextUnformatted("吸附曲线预览");
        ImGui.TableSetColumnIndex(1);
        // ImGui.SetCursorPosY(ImGui.GetCursorPosY() - topPanelStyle.CellPadding.Y);
        DrawSnapCurvePreview();
    }

    private void DrawKeyBindingSection(float reserveWidth, ImGuiStylePtr topPanelStyle)
    {
        TryCaptureTouchpadCustomKey();
#if DEBUG
        UpdateDebugTouchpadTestState(_leftDebugTouchpadTestState);
        UpdateDebugTouchpadTestState(_rightDebugTouchpadTestState);
#endif

        ImGui.TableNextRow();
        ImGui.TableNextRow();

        ImGui.TableSetColumnIndex(0);
        ImGui.AlignTextToFramePadding();
        ImGui.TextUnformatted("按键绑定");

        ImGui.TableSetColumnIndex(1);
        var availableWidth = MathF.Max(0f, ImGui.GetContentRegionAvail().X - reserveWidth);
        var labelWidth = ImGui.CalcTextSize("触摸板（左）").X;
        var minBindingContentWidth = 140f;
        _homeViewState.AimBindingIndex = _homeViewState.AimBindingIndex >= 0 && _homeViewState.AimBindingIndex < GamepadBindingCatalog.Options.Length
            ? _homeViewState.AimBindingIndex
            : GamepadBindingCatalog.DefaultAimIndex;
        _homeViewState.FireBindingIndex = _homeViewState.FireBindingIndex >= 0 && _homeViewState.FireBindingIndex < GamepadBindingCatalog.Options.Length
            ? _homeViewState.FireBindingIndex
            : GamepadBindingCatalog.DefaultFireIndex;
        _homeViewState.VoiceBindingIndex = _homeViewState.VoiceBindingIndex >= 0 && _homeViewState.VoiceBindingIndex < GamepadBindingCatalog.Options.Length
            ? _homeViewState.VoiceBindingIndex
            : GamepadBindingCatalog.DefaultTouchpadLeftIndex;
        _homeViewState.TouchpadLeftBindingIndex = _homeViewState.TouchpadLeftBindingIndex >= 0 && _homeViewState.TouchpadLeftBindingIndex < TouchpadBindingOptions.Length
            ? _homeViewState.TouchpadLeftBindingIndex
            : GamepadBindingCatalog.DefaultTouchpadLeftIndex;
        _homeViewState.TouchpadRightBindingIndex = _homeViewState.TouchpadRightBindingIndex >= 0 && _homeViewState.TouchpadRightBindingIndex < TouchpadBindingOptions.Length
            ? _homeViewState.TouchpadRightBindingIndex
            : GamepadBindingCatalog.DefaultTouchpadRightIndex;
        if (!GamepadBindingCatalog.TryResolveCustomKeyboardVirtualKey(_homeViewState.TouchpadLeftCustomKey, out _, out var normalizedLeftCustomKey))
        {
            normalizedLeftCustomKey = GamepadBindingCatalog.DefaultCustomKeyboardKeyName;
        }

        if (!GamepadBindingCatalog.TryResolveCustomKeyboardVirtualKey(_homeViewState.TouchpadRightCustomKey, out _, out var normalizedRightCustomKey))
        {
            normalizedRightCustomKey = GamepadBindingCatalog.DefaultCustomKeyboardKeyName;
        }
        if (!GamepadBindingCatalog.TryResolveCustomKeyboardVirtualKey(_homeViewState.VoiceCustomKey, out _, out var normalizedVoiceCustomKey))
        {
            normalizedVoiceCustomKey = DefaultVoiceCustomKeyName;
        }

        _homeViewState.TouchpadLeftCustomKey = normalizedLeftCustomKey;
        _homeViewState.TouchpadRightCustomKey = normalizedRightCustomKey;
        _homeViewState.VoiceCustomKey = normalizedVoiceCustomKey;
        var disableBindingSelection = _configFiles.Count == 0 || GamepadBindingCatalog.Options.Length == 0;
        var disableTouchpadBindingSelection = _configFiles.Count == 0 || TouchpadBindingOptions.Length == 0;
        var leftCustomSelected = GamepadBindingCatalog.IsKeyboardCustomBinding(_homeViewState.TouchpadLeftBindingIndex);
        var rightCustomSelected = GamepadBindingCatalog.IsKeyboardCustomBinding(_homeViewState.TouchpadRightBindingIndex);
        if ((_activeTouchpadKeyCaptureTarget == TouchpadKeyCaptureTarget.Left && !leftCustomSelected) ||
            (_activeTouchpadKeyCaptureTarget == TouchpadKeyCaptureTarget.Right && !rightCustomSelected))
        {
            CancelTouchpadKeyCapture();
        }

        ImGui.SetCursorPosY(ImGui.GetCursorPosY() - topPanelStyle.CellPadding.Y);
        if (ImGui.BeginTable(
                "##HomeKeyBindingInlineTable",
                2,
                ImGuiTableFlags.SizingStretchProp | ImGuiTableFlags.NoSavedSettings))
        {
            ImGui.TableSetupColumn("##HomeBindingLabelColumn", ImGuiTableColumnFlags.WidthFixed, labelWidth);
            ImGui.TableSetupColumn("##HomeBindingContentColumn", ImGuiTableColumnFlags.WidthStretch, 1f);

            ImGui.TableNextRow();

            ImGui.TableSetColumnIndex(0);
            ImGui.AlignTextToFramePadding();
            ImGui.TextUnformatted("瞄准");

            ImGui.TableSetColumnIndex(1);
            var bindingContentWidth = MathF.Max(minBindingContentWidth, ImGui.GetContentRegionAvail().X - reserveWidth);
            ImGui.SetNextItemWidth(bindingContentWidth);
            var selectedAimLabel = GamepadBindingCatalog.Options[_homeViewState.AimBindingIndex];
            ImGui.BeginDisabled(disableBindingSelection);
            if (ImGui.BeginCombo("##HomeAimBindingCombo", selectedAimLabel))
            {
                for (var i = 0; i < GamepadBindingCatalog.Options.Length; i++)
                {
                    var isSelected = i == _homeViewState.AimBindingIndex;
                    if (ImGui.Selectable(GamepadBindingCatalog.Options[i], isSelected))
                    {
                        _homeViewState.AimBindingIndex = i;
                        TryWriteStringToCurrentConfig(AimBindingConfigKey, GamepadBindingCatalog.Options[i]);
                        PushAimAssistConfig();
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
            ImGui.TableNextRow();
            ImGui.TableSetColumnIndex(0);
            ImGui.AlignTextToFramePadding();
            ImGui.TextUnformatted("开火");

            ImGui.TableSetColumnIndex(1);
            bindingContentWidth = MathF.Max(minBindingContentWidth, ImGui.GetContentRegionAvail().X - reserveWidth);
            ImGui.SetNextItemWidth(bindingContentWidth);
            var selectedFireLabel = GamepadBindingCatalog.Options[_homeViewState.FireBindingIndex];
            ImGui.BeginDisabled(disableBindingSelection);
            if (ImGui.BeginCombo("##HomeFireBindingCombo", selectedFireLabel))
            {
                for (var i = 0; i < GamepadBindingCatalog.Options.Length; i++)
                {
                    var isSelected = i == _homeViewState.FireBindingIndex;
                    if (ImGui.Selectable(GamepadBindingCatalog.Options[i], isSelected))
                    {
                        _homeViewState.FireBindingIndex = i;
                        TryWriteStringToCurrentConfig(FireBindingConfigKey, GamepadBindingCatalog.Options[i]);
                        PushAimAssistConfig();
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
            ImGui.TableNextRow();
            ImGui.TableSetColumnIndex(0);
            ImGui.AlignTextToFramePadding();
            ImGui.TextUnformatted("触摸板（左）");

            ImGui.TableSetColumnIndex(1);
            bindingContentWidth = MathF.Max(minBindingContentWidth, ImGui.GetContentRegionAvail().X - reserveWidth);
            var touchpadCaptureButtonWidth = ImGui.CalcTextSize("PrintScreen").X + topPanelStyle.FramePadding.X * 2f;
#if DEBUG
            var touchpadDebugTriggerButtonWidth = ImGui.CalcTextSize("连点中").X + topPanelStyle.FramePadding.X * 2f;
#endif
            var touchpadComboWidth = bindingContentWidth - topPanelStyle.ItemSpacing.X - touchpadCaptureButtonWidth;
            touchpadComboWidth = MathF.Max(70f, touchpadComboWidth);
            ImGui.SetNextItemWidth(touchpadComboWidth);
            var selectedTouchpadLeftLabel = TouchpadBindingOptions[_homeViewState.TouchpadLeftBindingIndex];
            ImGui.BeginDisabled(disableTouchpadBindingSelection);
            if (ImGui.BeginCombo("##HomeTouchpadLeftBindingCombo", selectedTouchpadLeftLabel))
            {
                for (var i = 0; i < TouchpadBindingOptions.Length; i++)
                {
                    var isSelected = i == _homeViewState.TouchpadLeftBindingIndex;
                    if (ImGui.Selectable(TouchpadBindingOptions[i], isSelected))
                    {
                        _homeViewState.TouchpadLeftBindingIndex = i;
                        TryWriteStringToCurrentConfig(TouchpadLeftBindingConfigKey, TouchpadBindingOptions[i]);
                        if (!GamepadBindingCatalog.IsKeyboardCustomBinding(i))
                        {
                            CancelTouchpadKeyCapture();
                        }
                        PushAimAssistConfig();
                    }

                    if (isSelected)
                    {
                        ImGui.SetItemDefaultFocus();
                    }
                }

                ImGui.EndCombo();
            }
            ImGui.EndDisabled();
            ImGui.SameLine(0f, topPanelStyle.ItemSpacing.X);
            ImGui.BeginDisabled(!leftCustomSelected || disableTouchpadBindingSelection);
            var leftButtonLabel = BuildCustomKeyCaptureButtonLabel(TouchpadKeyCaptureTarget.Left, _homeViewState.TouchpadLeftCustomKey);
            if (ImGui.Button($"{leftButtonLabel}###HomeTouchpadLeftCustomKeyCaptureButton", new Vector2(touchpadCaptureButtonWidth, 0f)))
            {
                ArmTouchpadKeyCapture(TouchpadKeyCaptureTarget.Left);
            }
            ImGui.EndDisabled();
#if DEBUG
            ImGui.SameLine(0f, topPanelStyle.ItemSpacing.X);
            var leftDebugTestButtonLabel = BuildDebugTouchpadTestButtonLabel(_leftDebugTouchpadTestState);
            ImGui.BeginDisabled(!leftCustomSelected || disableTouchpadBindingSelection || _leftDebugTouchpadTestState.IsActive);
            if (ImGui.Button($"{leftDebugTestButtonLabel}###HomeTouchpadLeftCustomKeyTestButton", new Vector2(touchpadDebugTriggerButtonWidth, 0f)))
            {
                StartDebugTouchpadTest(_leftDebugTouchpadTestState, _homeViewState.TouchpadLeftCustomKey);
            }
            ImGui.EndDisabled();
#endif

            ImGui.TableNextRow();
            ImGui.TableNextRow();
            ImGui.TableSetColumnIndex(0);
            ImGui.AlignTextToFramePadding();
            ImGui.TextUnformatted("触摸板（右）");

            ImGui.TableSetColumnIndex(1);
            bindingContentWidth = MathF.Max(minBindingContentWidth, ImGui.GetContentRegionAvail().X - reserveWidth);
            touchpadComboWidth = MathF.Max(70f, bindingContentWidth - topPanelStyle.ItemSpacing.X - touchpadCaptureButtonWidth);
            ImGui.SetNextItemWidth(touchpadComboWidth);
            var selectedTouchpadRightLabel = TouchpadBindingOptions[_homeViewState.TouchpadRightBindingIndex];
            ImGui.BeginDisabled(disableTouchpadBindingSelection);
            if (ImGui.BeginCombo("##HomeTouchpadRightBindingCombo", selectedTouchpadRightLabel))
            {
                for (var i = 0; i < TouchpadBindingOptions.Length; i++)
                {
                    var isSelected = i == _homeViewState.TouchpadRightBindingIndex;
                    if (ImGui.Selectable(TouchpadBindingOptions[i], isSelected))
                    {
                        _homeViewState.TouchpadRightBindingIndex = i;
                        TryWriteStringToCurrentConfig(TouchpadRightBindingConfigKey, TouchpadBindingOptions[i]);
                        if (!GamepadBindingCatalog.IsKeyboardCustomBinding(i))
                        {
                            CancelTouchpadKeyCapture();
                        }
                        PushAimAssistConfig();
                    }

                    if (isSelected)
                    {
                        ImGui.SetItemDefaultFocus();
                    }
                }

                ImGui.EndCombo();
            }
            ImGui.EndDisabled();
            ImGui.SameLine(0f, topPanelStyle.ItemSpacing.X);
            ImGui.BeginDisabled(!rightCustomSelected || disableTouchpadBindingSelection);
            var rightButtonLabel = BuildCustomKeyCaptureButtonLabel(TouchpadKeyCaptureTarget.Right, _homeViewState.TouchpadRightCustomKey);
            if (ImGui.Button($"{rightButtonLabel}###HomeTouchpadRightCustomKeyCaptureButton", new Vector2(touchpadCaptureButtonWidth, 0f)))
            {
                ArmTouchpadKeyCapture(TouchpadKeyCaptureTarget.Right);
            }
            ImGui.EndDisabled();
#if DEBUG
            ImGui.SameLine(0f, topPanelStyle.ItemSpacing.X);
            var rightDebugTestButtonLabel = BuildDebugTouchpadTestButtonLabel(_rightDebugTouchpadTestState);
            ImGui.BeginDisabled(!rightCustomSelected || disableTouchpadBindingSelection || _rightDebugTouchpadTestState.IsActive);
            if (ImGui.Button($"{rightDebugTestButtonLabel}###HomeTouchpadRightCustomKeyTestButton", new Vector2(touchpadDebugTriggerButtonWidth, 0f)))
            {
                StartDebugTouchpadTest(_rightDebugTouchpadTestState, _homeViewState.TouchpadRightCustomKey);
            }
            ImGui.EndDisabled();
#endif

            ImGui.TableNextRow();
            ImGui.TableNextRow();
            ImGui.TableSetColumnIndex(0);
            ImGui.AlignTextToFramePadding();
            ImGui.TextUnformatted("语音");

            ImGui.TableSetColumnIndex(1);
            bindingContentWidth = MathF.Max(minBindingContentWidth, ImGui.GetContentRegionAvail().X - reserveWidth);
            var voiceCaptureButtonWidth = ImGui.CalcTextSize("PrintScreen").X + topPanelStyle.FramePadding.X * 2f;
            var fromLabelWidth = ImGui.CalcTextSize("从").X;
            var toLabelWidth = ImGui.CalcTextSize("映射到").X;
            var voiceComboWidth = MathF.Max(
                90f,
                bindingContentWidth - voiceCaptureButtonWidth - fromLabelWidth - toLabelWidth - topPanelStyle.ItemSpacing.X * 3f);
            ImGui.AlignTextToFramePadding();
            ImGui.TextUnformatted("从");
            ImGui.SameLine(0f, topPanelStyle.ItemSpacing.X);
            ImGui.SetNextItemWidth(voiceComboWidth);
            var selectedVoiceLabel = GamepadBindingCatalog.Options[_homeViewState.VoiceBindingIndex];
            ImGui.BeginDisabled(disableBindingSelection);
            if (ImGui.BeginCombo("##HomeVoiceBindingCombo", selectedVoiceLabel))
            {
                for (var i = 0; i < GamepadBindingCatalog.Options.Length; i++)
                {
                    var isSelected = i == _homeViewState.VoiceBindingIndex;
                    if (ImGui.Selectable(GamepadBindingCatalog.Options[i], isSelected))
                    {
                        _homeViewState.VoiceBindingIndex = i;
                        TryWriteStringToCurrentConfig(VoiceBindingConfigKey, GamepadBindingCatalog.Options[i]);
                        PushAimAssistConfig();
                    }

                    if (isSelected)
                    {
                        ImGui.SetItemDefaultFocus();
                    }
                }

                ImGui.EndCombo();
            }
            ImGui.EndDisabled();
            ImGui.SameLine(0f, topPanelStyle.ItemSpacing.X);
            ImGui.AlignTextToFramePadding();
            ImGui.TextUnformatted("映射到");
            ImGui.SameLine(0f, topPanelStyle.ItemSpacing.X);
            ImGui.BeginDisabled(_configFiles.Count == 0);
            var voiceButtonLabel = BuildCustomKeyCaptureButtonLabel(TouchpadKeyCaptureTarget.Voice, _homeViewState.VoiceCustomKey);
            if (ImGui.Button($"{voiceButtonLabel}###HomeVoiceCustomKeyCaptureButton", new Vector2(voiceCaptureButtonWidth, 0f)))
            {
                ArmTouchpadKeyCapture(TouchpadKeyCaptureTarget.Voice);
            }
            ImGui.EndDisabled();

            ImGui.EndTable();
        }
    }

#if DEBUG
    private void StartDebugTouchpadTest(DebugTouchpadTestState state, string customKey)
    {
        state.CustomKey = customKey;
        state.IsActive = true;
        state.BurstStarted = false;
        state.CountdownEndsAtUtc = DateTime.UtcNow + DebugTouchpadTestCountdown;
        state.BurstEndsAtUtc = state.CountdownEndsAtUtc + DebugTouchpadBurstDuration;
        _smartCoreMappingState.LastError = string.Empty;
    }

    private void UpdateDebugTouchpadTestState(DebugTouchpadTestState state)
    {
        if (!state.IsActive)
        {
            return;
        }

        var now = DateTime.UtcNow;
        if (!state.BurstStarted)
        {
            if (now < state.CountdownEndsAtUtc)
            {
                return;
            }

            var worker = _viGEmMappingWorker;
            if (worker is null)
            {
                _smartCoreMappingState.LastError = "映射线程未初始化";
                state.IsActive = false;
                return;
            }

            if (!worker.TryStartCustomKeyboardBurst(state.CustomKey, DebugTouchpadBurstDuration, out var error))
            {
                if (!string.IsNullOrWhiteSpace(error))
                {
                    _smartCoreMappingState.LastError = error;
                }
                state.IsActive = false;
                return;
            }

            state.BurstStarted = true;
            return;
        }

        if (now >= state.BurstEndsAtUtc)
        {
            state.IsActive = false;
            state.BurstStarted = false;
        }
    }

    private static string BuildDebugTouchpadTestButtonLabel(DebugTouchpadTestState state)
    {
        if (!state.IsActive)
        {
            return "测试";
        }

        if (state.BurstStarted)
        {
            return "连点中";
        }

        var remaining = Math.Max(0.0, (state.CountdownEndsAtUtc - DateTime.UtcNow).TotalSeconds);
        return $"{Math.Ceiling(remaining)}s";
    }

#endif

    private void TryCaptureTouchpadCustomKey()
    {
        if (_activeTouchpadKeyCaptureTarget == TouchpadKeyCaptureTarget.None)
        {
            return;
        }

        Keys? captured = null;
        foreach (var key in GamepadBindingCatalog.CapturableCustomKeyboardKeys)
        {
            if (!KeyboardState.IsKeyDown(key) || _touchpadCapturePreviousDownKeys.Contains(key))
            {
                continue;
            }

            captured = key;
            break;
        }

        _touchpadCapturePreviousDownKeys.Clear();
        foreach (var key in GamepadBindingCatalog.CapturableCustomKeyboardKeys)
        {
            if (KeyboardState.IsKeyDown(key))
            {
                _touchpadCapturePreviousDownKeys.Add(key);
            }
        }

        if (!captured.HasValue ||
            !GamepadBindingCatalog.TryGetCustomKeyboardDisplayName(captured.Value, out var capturedDisplayName))
        {
            return;
        }

        if (_activeTouchpadKeyCaptureTarget == TouchpadKeyCaptureTarget.Left)
        {
            _homeViewState.TouchpadLeftCustomKey = capturedDisplayName;
            TryWriteStringToCurrentConfig(TouchpadLeftCustomKeyConfigKey, capturedDisplayName);
        }
        else if (_activeTouchpadKeyCaptureTarget == TouchpadKeyCaptureTarget.Right)
        {
            _homeViewState.TouchpadRightCustomKey = capturedDisplayName;
            TryWriteStringToCurrentConfig(TouchpadRightCustomKeyConfigKey, capturedDisplayName);
        }
        else
        {
            _homeViewState.VoiceCustomKey = capturedDisplayName;
            TryWriteStringToCurrentConfig(VoiceCustomKeyConfigKey, capturedDisplayName);
        }

        CancelTouchpadKeyCapture();
        PushAimAssistConfig();
    }

    private void ArmTouchpadKeyCapture(TouchpadKeyCaptureTarget target)
    {
        _activeTouchpadKeyCaptureTarget = target;
        _touchpadCapturePreviousDownKeys.Clear();
        foreach (var key in GamepadBindingCatalog.CapturableCustomKeyboardKeys)
        {
            if (KeyboardState.IsKeyDown(key))
            {
                _touchpadCapturePreviousDownKeys.Add(key);
            }
        }
    }

    private void CancelTouchpadKeyCapture()
    {
        _activeTouchpadKeyCaptureTarget = TouchpadKeyCaptureTarget.None;
        _touchpadCapturePreviousDownKeys.Clear();
    }

    private string BuildCustomKeyCaptureButtonLabel(TouchpadKeyCaptureTarget target, string customKey)
    {
        if (_activeTouchpadKeyCaptureTarget == target)
        {
            return "按下任意键";
        }

        return string.IsNullOrWhiteSpace(customKey) ? "点击设置" : customKey;
    }

    private void DrawSnapModeSection(float reserveWidth, ImGuiStylePtr topPanelStyle)
    {
        ImGui.TableNextRow();
        ImGui.TableNextRow();

        ImGui.TableSetColumnIndex(0);
        ImGui.SetCursorPosY(ImGui.GetCursorPosY() - topPanelStyle.CellPadding.Y);
        ImGui.AlignTextToFramePadding();
        ImGui.TextUnformatted("开启吸附方式");
        ImGui.TableSetColumnIndex(1);
        ImGui.SetCursorPosY(ImGui.GetCursorPosY() - topPanelStyle.CellPadding.Y);
        _homeViewState.SnapModeIndex = _homeViewState.SnapModeIndex >= 0 && _homeViewState.SnapModeIndex < HomeSnapModeOptions.Length ? _homeViewState.SnapModeIndex : 0;
        var selectedSnapModeLabel = HomeSnapModeOptions[_homeViewState.SnapModeIndex];
        var snapComboWidth = ImGui.GetContentRegionAvail().X - reserveWidth;
        ImGui.SetNextItemWidth(snapComboWidth);
        ImGui.BeginDisabled(_configFiles.Count == 0);
        if (ImGui.BeginCombo("##HomeSnapModeCombo", selectedSnapModeLabel))
        {
            for (var i = 0; i < HomeSnapModeOptions.Length; i++)
            {
                var isSelected = i == _homeViewState.SnapModeIndex;
                if (ImGui.Selectable(HomeSnapModeOptions[i], isSelected))
                {
                    _homeViewState.SnapModeIndex = i;
                    TryWriteStringToCurrentConfig("snap", HomeSnapModeOptions[i]);
                    PushAimAssistConfig();
                }

                if (isSelected)
                {
                    ImGui.SetItemDefaultFocus();
                }
            }

            ImGui.EndCombo();
        }
        ImGui.EndDisabled();
    }

    private void DrawSpecialWeaponLogicSection(ImGuiStylePtr topPanelStyle)
    {
        ImGui.TableNextRow();
        ImGui.TableNextRow();

        ImGui.TableSetColumnIndex(0);
        ImGui.AlignTextToFramePadding();
        ImGui.TextUnformatted("特殊武器逻辑");
        ImGui.TableSetColumnIndex(1);
        ImGui.BeginDisabled(_configFiles.Count == 0);
        var (weaponNameColumnWidth, aimSnapColumnWidth, rapidFireColumnWidth, releaseFireColumnWidth) = MeasureSpecialWeaponColumnWidths();

        ImGui.SetCursorPosY(ImGui.GetCursorPosY() + topPanelStyle.CellPadding.Y);
        if (ImGui.BeginTable(
                "##SpecialWeaponLogicTable",
                4,
                ImGuiTableFlags.BordersInnerV | ImGuiTableFlags.BordersOuter | ImGuiTableFlags.RowBg | ImGuiTableFlags.SizingFixedFit | ImGuiTableFlags.NoHostExtendX))
        {
            ImGui.TableSetupColumn("武器名", ImGuiTableColumnFlags.WidthFixed, weaponNameColumnWidth);
            ImGui.TableSetupColumn("瞄准吸附", ImGuiTableColumnFlags.WidthFixed, aimSnapColumnWidth);
            ImGui.TableSetupColumn("开火连点", ImGuiTableColumnFlags.WidthFixed, rapidFireColumnWidth);
            ImGui.TableSetupColumn("松手开火", ImGuiTableColumnFlags.WidthFixed, releaseFireColumnWidth);
            ImGui.TableHeadersRow();

            for (var i = 0; i < _specialWeaponNames.Length; i++)
            {
                ImGui.TableNextRow();

                ImGui.TableSetColumnIndex(0);
                ImGui.AlignTextToFramePadding();
                ImGui.TextUnformatted(_specialWeaponNames[i]);

                DrawSpecialWeaponToggleCell(i, 1, $"##SpecialWeaponAimSnap_{i}", ref _specialWeaponAimSnapEnabled[i]);
                DrawSpecialWeaponToggleCell(i, 2, $"##SpecialWeaponRapidFire_{i}", ref _specialWeaponRapidFireEnabled[i]);
                DrawSpecialWeaponToggleCell(i, 3, $"##SpecialWeaponReleaseFire_{i}", ref _specialWeaponReleaseFireEnabled[i]);
            }

            ImGui.EndTable();
        }
        ImGui.EndDisabled();
    }

    private (float WeaponNameColumnWidth, float AimSnapColumnWidth, float RapidFireColumnWidth, float ReleaseFireColumnWidth)
        MeasureSpecialWeaponColumnWidths()
    {
        var weaponNameColumnWidth = ImGui.CalcTextSize("武器名").X;
        for (var i = 0; i < _specialWeaponNames.Length; i++)
        {
            weaponNameColumnWidth = MathF.Max(weaponNameColumnWidth, ImGui.CalcTextSize(_specialWeaponNames[i]).X);
        }

        var aimSnapColumnWidth = ImGui.CalcTextSize("瞄准吸附").X;
        var rapidFireColumnWidth = ImGui.CalcTextSize("开火连点").X;
        var releaseFireColumnWidth = ImGui.CalcTextSize("松手开火").X;
        var style = ImGui.GetStyle();
        var cellPadding = style.CellPadding.X * 2f;
        return (
            weaponNameColumnWidth + cellPadding,
            aimSnapColumnWidth + cellPadding,
            rapidFireColumnWidth + cellPadding,
            releaseFireColumnWidth + cellPadding);
    }

    private void DrawSpecialWeaponToggleCell(int weaponIndex, int columnIndex, string controlId, ref bool flag)
    {
        ImGui.TableSetColumnIndex(columnIndex);
        if (!ImGui.Checkbox(controlId, ref flag))
        {
            return;
        }

        TryWriteSpecialWeaponLogicValueToCurrentConfig(
            weaponIndex,
            _specialWeaponAimSnapEnabled[weaponIndex],
            _specialWeaponRapidFireEnabled[weaponIndex],
            _specialWeaponReleaseFireEnabled[weaponIndex]);
        PushAimAssistConfig();
    }

    private void RefreshHomeInputDevices()
    {
        RefreshInputDevicesCore(ref _homeSelectedGamepadIndex, forceRefresh: true);
        ApplySelectedGamepadSelection();
    }

    private void RefreshInputDevicesCore(ref int selectedIndex, bool forceRefresh)
    {
        UpdateConnectedGamepadCache(forceRefresh);
        ResolveSelectedGamepadIndexFromInstanceId();
        selectedIndex = _homeSelectedGamepadIndex;
    }

    private string[] GetConnectedGamepadOptions()
    {
        if (_cachedGamepadOptions.Length == 0)
        {
            return Array.Empty<string>();
        }

        return _cachedGamepadOptions;
    }

    private void UpdateConnectedGamepadCache(bool forceRefresh = false)
    {
        _cachedConnectedGamepads = _gamepadService.GetConnectedGamepads(_sdlGamepadWorker, forceRefresh);
        _cachedGamepadOptions = _gamepadService.BuildGamepadOptions(_cachedConnectedGamepads);
    }


}

