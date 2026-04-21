using ImGuiNET;
using System.Numerics;

public sealed partial class MainWindow
{
    private const float SnapFloatStep = 0.01f;
    private const string SnapFloatFormat = "%.2f";

    private readonly record struct HomeLayoutMetrics(
        float BaseTextWidth,
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
        var baseTextWidth = ImGui.CalcTextSize("一").X;
        var addButtonWidth = baseTextWidth * 2f + topPanelStyle.FramePadding.X * 2f;
        var deleteButtonWidth = baseTextWidth * 2f + topPanelStyle.FramePadding.X * 2f;
        var reserveWidth = addButtonWidth + deleteButtonWidth + topPanelStyle.ItemSpacing.X * 2f;
        var metrics = new HomeLayoutMetrics(baseTextWidth, baseTextWidth * 6.5f, reserveWidth, addButtonWidth, deleteButtonWidth);

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
        ImGui.TableSetupColumn("##DepState", ImGuiTableColumnFlags.WidthFixed, metrics.BaseTextWidth * 3f);
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
        var inputRefreshButtonWidth = metrics.BaseTextWidth * 2f + topPanelStyle.FramePadding.X * 2f;
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
        var refreshButtonWidth = metrics.BaseTextWidth * 2f + modelLineStyle.FramePadding.X * 2f;
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
        DrawKeyBindingSection(metrics.ReserveWidth);
        DrawSnapModeSection(metrics.ReserveWidth);
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
        var layout = BuildSnapSettingsLayout(metrics, topPanelStyle);

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

    private static SnapSettingsLayout BuildSnapSettingsLayout(HomeLayoutMetrics metrics, ImGuiStylePtr topPanelStyle)
    {
        var rangeInputWidth = ImGui.CalcTextSize("0000").X + topPanelStyle.FramePadding.X * 2f;
        var strengthInputWidth = rangeInputWidth + ImGui.GetFrameHeight() * 2f + topPanelStyle.ItemInnerSpacing.X * 2f;
        return new SnapSettingsLayout(
            metrics.BaseTextWidth * 6f,
            metrics.BaseTextWidth * 4f,
            rangeInputWidth,
            strengthInputWidth,
            strengthInputWidth);
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
        ImGui.SetCursorPosY(ImGui.GetCursorPosY() - topPanelStyle.CellPadding.Y);
        ImGui.AlignTextToFramePadding();
        ImGui.TextUnformatted("吸附曲线预览");
        ImGui.TableSetColumnIndex(1);
        ImGui.SetCursorPosY(ImGui.GetCursorPosY() - topPanelStyle.CellPadding.Y);
        DrawSnapCurvePreview();
    }

    private void DrawKeyBindingSection(float reserveWidth)
    {
        ImGui.TableNextRow();
        ImGui.TableNextRow();

        ImGui.TableSetColumnIndex(0);
        ImGui.AlignTextToFramePadding();
        ImGui.TextUnformatted("按键绑定");

        ImGui.TableSetColumnIndex(1);
        var style = ImGui.GetStyle();
        var availableWidth = MathF.Max(0f, ImGui.GetContentRegionAvail().X - reserveWidth);
        var labelWidth = MathF.Max(ImGui.CalcTextSize("瞄准").X, ImGui.CalcTextSize("开火").X);
        var comboWidth = MathF.Max(90f, availableWidth - labelWidth - style.ItemSpacing.X);
        _homeViewState.AimBindingIndex = _homeViewState.AimBindingIndex >= 0 && _homeViewState.AimBindingIndex < GamepadBindingCatalog.Options.Length
            ? _homeViewState.AimBindingIndex
            : GamepadBindingCatalog.DefaultAimIndex;
        _homeViewState.FireBindingIndex = _homeViewState.FireBindingIndex >= 0 && _homeViewState.FireBindingIndex < GamepadBindingCatalog.Options.Length
            ? _homeViewState.FireBindingIndex
            : GamepadBindingCatalog.DefaultFireIndex;
        var disableBindingSelection = _configFiles.Count == 0 || GamepadBindingCatalog.Options.Length == 0;

        ImGui.AlignTextToFramePadding();
        ImGui.TextUnformatted("瞄准");
        ImGui.SameLine();
        ImGui.SetNextItemWidth(comboWidth);
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

        ImGui.SetCursorPosY(ImGui.GetCursorPosY() + style.ItemSpacing.Y);
        ImGui.AlignTextToFramePadding();
        ImGui.TextUnformatted("开火");
        ImGui.SameLine();
        ImGui.SetNextItemWidth(comboWidth);
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
    }

    private void DrawSnapModeSection(float reserveWidth)
    {
        ImGui.TableNextRow();
        ImGui.TableNextRow();

        ImGui.TableSetColumnIndex(0);
        ImGui.AlignTextToFramePadding();
        ImGui.TextUnformatted("开启吸附方式");
        ImGui.TableSetColumnIndex(1);
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

