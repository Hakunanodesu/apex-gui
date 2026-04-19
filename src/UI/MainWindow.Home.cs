using ImGuiNET;
using System.Numerics;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using System.Threading;

public sealed partial class MainWindow
{
    private readonly record struct HomeLayoutMetrics(
        float BaseTextWidth,
        float FirstColumnWidth,
        float ReserveWidth,
        float AddButtonWidth,
        float DeleteButtonWidth);

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
        _homeSelectedGamepadIndex = hasGamepads
            ? (_homeSelectedGamepadIndex >= 0 && _homeSelectedGamepadIndex < gamepads.Length ? _homeSelectedGamepadIndex : 0)
            : -1;
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
        _homeViewState.SnapOuterRange = Math.Clamp(_homeViewState.SnapOuterRange, selectedModelSize, snapOuterRangeMax);
        _homeViewState.SnapInnerRange = Math.Clamp(_homeViewState.SnapInnerRange, 1, _homeViewState.SnapOuterRange);
        _homeViewState.SnapOuterStrength = Math.Clamp(_homeViewState.SnapOuterStrength, 0f, 1f);
        _homeViewState.SnapInnerStrength = Math.Clamp(_homeViewState.SnapInnerStrength, 0f, 1f);
        var snapRangeInputWidth = ImGui.CalcTextSize("0000").X + topPanelStyle.FramePadding.X * 2f;
        var snapStrengthInputWidth = snapRangeInputWidth + ImGui.GetFrameHeight() * 2f + topPanelStyle.ItemInnerSpacing.X * 2f;
        var snapExtraInputWidth = snapStrengthInputWidth;
        _homeViewState.SnapStartStrength = Math.Clamp(_homeViewState.SnapStartStrength, 0f, 1f);
        _homeViewState.SnapVerticalStrengthFactor = Math.Clamp(_homeViewState.SnapVerticalStrengthFactor, 0f, 1f);
        _homeViewState.SnapHipfireStrengthFactor = Math.Clamp(_homeViewState.SnapHipfireStrengthFactor, 0f, 1f);
        _homeViewState.SnapHeight = Math.Clamp(_homeViewState.SnapHeight, 0f, 1f);
        var snapLabelWidth = metrics.BaseTextWidth * 6f;
        var snapLastLabelWidth = metrics.BaseTextWidth * 4f;
        ImGui.SetCursorPosY(ImGui.GetCursorPosY() - topPanelStyle.CellPadding.Y);
        if (ImGui.BeginTable("##SnapSettingsGrid", 6, ImGuiTableFlags.SizingFixedFit))
        {
            ImGui.TableSetupColumn("##SnapLabelCol0", ImGuiTableColumnFlags.WidthFixed, snapLabelWidth);
            ImGui.TableSetupColumn("##SnapInputCol0", ImGuiTableColumnFlags.WidthFixed, snapStrengthInputWidth);
            ImGui.TableSetupColumn("##SnapLabelCol1", ImGuiTableColumnFlags.WidthFixed, snapLabelWidth);
            ImGui.TableSetupColumn("##SnapInputCol1", ImGuiTableColumnFlags.WidthFixed, snapStrengthInputWidth);
            ImGui.TableSetupColumn("##SnapLabelCol2", ImGuiTableColumnFlags.WidthFixed, snapLastLabelWidth);
            ImGui.TableSetupColumn("##SnapInputCol2", ImGuiTableColumnFlags.WidthFixed, snapStrengthInputWidth);

            // Row 1: ranges
            ImGui.TableNextRow();
            ImGui.TableSetColumnIndex(0);
            ImGui.AlignTextToFramePadding();
            ImGui.TextUnformatted("内圈范围");
            ImGui.TableSetColumnIndex(1);
            ImGui.SetNextItemWidth(snapRangeInputWidth);
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
            ImGui.SetNextItemWidth(snapRangeInputWidth);
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
            if (ImGui.Button("范围预览##SnapRangePreviewWindowButton", new Vector2(snapExtraInputWidth, 0f)))
            {
                OpenSnapRangePreviewWindow();
            }
            ImGui.EndDisabled();

            // Row 2: strengths
            ImGui.TableNextRow();
            ImGui.TableNextRow();
            ImGui.TableSetColumnIndex(0);
            ImGui.AlignTextToFramePadding();
            ImGui.TextUnformatted("内圈强度");
            ImGui.TableSetColumnIndex(1);
            ImGui.SetNextItemWidth(snapStrengthInputWidth);
            var snapInnerStrength = _homeViewState.SnapInnerStrength;
            if (ImGui.InputFloat("##SnapInnerStrength", ref snapInnerStrength, 0.01f, 0.01f, "%.2f"))
            {
                _homeViewState.SnapInnerStrength = Math.Clamp(snapInnerStrength, 0f, 1f);
                TryWriteFloatToCurrentConfig("snapInnerStrength", _homeViewState.SnapInnerStrength);
                PushAimAssistConfig();
            }

            ImGui.TableSetColumnIndex(2);
            ImGui.AlignTextToFramePadding();
            ImGui.TextUnformatted("外圈强度");
            ImGui.TableSetColumnIndex(3);
            ImGui.SetNextItemWidth(snapStrengthInputWidth);
            var snapOuterStrength = _homeViewState.SnapOuterStrength;
            if (ImGui.InputFloat("##SnapOuterStrength", ref snapOuterStrength, 0.01f, 0.01f, "%.2f"))
            {
                _homeViewState.SnapOuterStrength = Math.Clamp(snapOuterStrength, 0f, 1f);
                TryWriteFloatToCurrentConfig("snapOuterStrength", _homeViewState.SnapOuterStrength);
                PushAimAssistConfig();
            }

            ImGui.TableSetColumnIndex(4);
            ImGui.AlignTextToFramePadding();
            ImGui.TextUnformatted("起始强度");
            ImGui.TableSetColumnIndex(5);
            ImGui.SetNextItemWidth(snapExtraInputWidth);
            var snapStartStrength = _homeViewState.SnapStartStrength;
            if (ImGui.InputFloat("##SnapStartStrength", ref snapStartStrength, 0.01f, 0.01f, "%.2f"))
            {
                _homeViewState.SnapStartStrength = Math.Clamp(snapStartStrength, 0f, 1f);
                TryWriteFloatToCurrentConfig("snapStartStrength", _homeViewState.SnapStartStrength);
                PushAimAssistConfig();
            }

            // Row 3: extras
            ImGui.TableNextRow();
            ImGui.TableNextRow();
            ImGui.TableSetColumnIndex(0);
            ImGui.AlignTextToFramePadding();
            ImGui.TextUnformatted("腰射强度系数");
            ImGui.TableSetColumnIndex(1);
            ImGui.SetNextItemWidth(snapExtraInputWidth);
            var snapHipfireStrengthFactor = _homeViewState.SnapHipfireStrengthFactor;
            if (ImGui.InputFloat("##SnapHipfireStrengthFactor", ref snapHipfireStrengthFactor, 0.01f, 0.01f, "%.2f"))
            {
                _homeViewState.SnapHipfireStrengthFactor = Math.Clamp(snapHipfireStrengthFactor, 0f, 1f);
                TryWriteFloatToCurrentConfig("snapHipfireStrengthFactor", _homeViewState.SnapHipfireStrengthFactor);
                PushAimAssistConfig();
            }

            ImGui.TableSetColumnIndex(2);
            ImGui.AlignTextToFramePadding();
            ImGui.TextUnformatted("垂直强度系数");
            ImGui.TableSetColumnIndex(3);
            ImGui.SetNextItemWidth(snapExtraInputWidth);
            var snapVerticalStrengthFactor = _homeViewState.SnapVerticalStrengthFactor;
            if (ImGui.InputFloat("##SnapVerticalStrengthFactor", ref snapVerticalStrengthFactor, 0.01f, 0.01f, "%.2f"))
            {
                _homeViewState.SnapVerticalStrengthFactor = Math.Clamp(snapVerticalStrengthFactor, 0f, 1f);
                TryWriteFloatToCurrentConfig("snapVerticalStrengthFactor", _homeViewState.SnapVerticalStrengthFactor);
                PushAimAssistConfig();
            }

            ImGui.TableSetColumnIndex(4);
            ImGui.AlignTextToFramePadding();
            ImGui.TextUnformatted("吸附高度");
            ImGui.TableSetColumnIndex(5);
            ImGui.SetNextItemWidth(snapExtraInputWidth);
            var snapHeight = _homeViewState.SnapHeight;
            if (ImGui.InputFloat("##SnapHeight", ref snapHeight, 0.01f, 0.01f, "%.2f"))
            {
                _homeViewState.SnapHeight = Math.Clamp(snapHeight, 0f, 1f);
                TryWriteFloatToCurrentConfig("snapHeight", _homeViewState.SnapHeight);
                PushAimAssistConfig();
            }

            // Row 4: interpolation
            ImGui.TableNextRow();
            ImGui.TableNextRow();
            _homeViewState.SnapInnerInterpolationTypeIndex = _homeViewState.SnapInnerInterpolationTypeIndex >= 0 && _homeViewState.SnapInnerInterpolationTypeIndex < SnapInnerInterpolationTypeOptions.Length
                ? _homeViewState.SnapInnerInterpolationTypeIndex
                : 0;
            ImGui.TableSetColumnIndex(0);
            ImGui.AlignTextToFramePadding();
            ImGui.TextUnformatted("内圈插值类型");
            ImGui.TableSetColumnIndex(1);
            ImGui.SetNextItemWidth(snapStrengthInputWidth);
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
        var weaponNameColumnWidth = ImGui.CalcTextSize("武器名").X;
        for (var i = 0; i < SpecialWeaponNames.Length; i++)
        {
            weaponNameColumnWidth = MathF.Max(weaponNameColumnWidth, ImGui.CalcTextSize(SpecialWeaponNames[i]).X);
        }

        var aimSnapColumnWidth = ImGui.CalcTextSize("瞄准吸附").X;
        var rapidFireColumnWidth = ImGui.CalcTextSize("开火连点").X;
        var releaseFireColumnWidth = ImGui.CalcTextSize("松手开火").X;
        var specialWeaponStyle = ImGui.GetStyle();
        weaponNameColumnWidth += specialWeaponStyle.CellPadding.X * 2f;
        aimSnapColumnWidth += specialWeaponStyle.CellPadding.X * 2f;
        rapidFireColumnWidth += specialWeaponStyle.CellPadding.X * 2f;
        releaseFireColumnWidth += specialWeaponStyle.CellPadding.X * 2f;
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

            for (var i = 0; i < SpecialWeaponNames.Length; i++)
            {
                ImGui.TableNextRow();

                ImGui.TableSetColumnIndex(0);
                ImGui.AlignTextToFramePadding();
                ImGui.TextUnformatted(SpecialWeaponNames[i]);

                ImGui.TableSetColumnIndex(1);
                if (ImGui.Checkbox($"##SpecialWeaponAimSnap_{i}", ref _specialWeaponAimSnapEnabled[i]))
                {
                    TryWriteSpecialWeaponLogicValueToCurrentConfig(i, _specialWeaponAimSnapEnabled[i], _specialWeaponRapidFireEnabled[i], _specialWeaponReleaseFireEnabled[i]);
                }

                ImGui.TableSetColumnIndex(2);
                if (ImGui.Checkbox($"##SpecialWeaponRapidFire_{i}", ref _specialWeaponRapidFireEnabled[i]))
                {
                    TryWriteSpecialWeaponLogicValueToCurrentConfig(i, _specialWeaponAimSnapEnabled[i], _specialWeaponRapidFireEnabled[i], _specialWeaponReleaseFireEnabled[i]);
                }

                ImGui.TableSetColumnIndex(3);
                if (ImGui.Checkbox($"##SpecialWeaponReleaseFire_{i}", ref _specialWeaponReleaseFireEnabled[i]))
                {
                    TryWriteSpecialWeaponLogicValueToCurrentConfig(i, _specialWeaponAimSnapEnabled[i], _specialWeaponRapidFireEnabled[i], _specialWeaponReleaseFireEnabled[i]);
                }
            }

            ImGui.EndTable();
        }
        ImGui.EndDisabled();
    }

    private void RefreshHomeInputDevices()
    {
        RefreshInputDevicesCore(ref _homeSelectedGamepadIndex, forceRefresh: true);
        ApplySelectedGamepadSelection();
    }

    private void RefreshInputDevicesCore(ref int selectedIndex, bool forceRefresh)
    {
        UpdateConnectedGamepadCache(forceRefresh);
        selectedIndex = _gamepadService.NormalizeSelectedIndex(selectedIndex, _cachedGamepadOptions.Length);
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

    private void DrawConfigFileModals()
    {
        var isAddModalOpen = _homeViewState.IsAddModalOpen;
        var addNameBuffer = _homeViewState.AddNameBuffer;
        if (_homeViewState.IsAddModalOpenRequested)
        {
            ImGui.OpenPopup("璇疯緭鍏ユ柊閰嶇疆鍚嶇О");
            _homeViewState.IsAddModalOpenRequested = false;
        }

        if (ImGui.BeginPopupModal("璇疯緭鍏ユ柊閰嶇疆鍚嶇О", ref isAddModalOpen, ImGuiWindowFlags.AlwaysAutoResize))
        {
            ImGui.InputText("##AddConfigNameInput", ref addNameBuffer, 256);
            _homeViewState.AddNameBuffer = addNameBuffer;
            if (!string.IsNullOrEmpty(_homeViewState.AddError))
            {
                ImGui.AlignTextToFramePadding();
                ImGui.TextUnformatted(_homeViewState.AddError);
            }

            if (ImGui.Button("鍒涘缓"))
            {
                if (TryCreateEmptyConfigFile(_homeViewState.AddNameBuffer, out var err))
                {
                    _homeViewState.CloseAddModal();
                    ImGui.CloseCurrentPopup();
                }
                else
                {
                    _homeViewState.AddError = err;
                }
            }

            ImGui.SameLine();
            if (ImGui.Button("鍙栨秷"))
            {
                _homeViewState.CloseAddModal();
                ImGui.CloseCurrentPopup();
            }

            ImGui.EndPopup();
        }
        _homeViewState.IsAddModalOpen = isAddModalOpen;

        var isDeleteModalOpen = _homeViewState.IsDeleteModalOpen;
        if (_homeViewState.IsDeleteModalOpenRequested)
        {
            ImGui.OpenPopup("鍒犻櫎閰嶇疆纭");
            _homeViewState.IsDeleteModalOpenRequested = false;
        }

        if (ImGui.BeginPopupModal("鍒犻櫎閰嶇疆纭", ref isDeleteModalOpen, ImGuiWindowFlags.AlwaysAutoResize))
        {
            var name = _homeViewState.PendingDeleteConfigBaseName ?? string.Empty;
            ImGui.AlignTextToFramePadding();
            ImGui.TextUnformatted($"纭畾鍒犻櫎閰嶇疆鏂囦欢 {name} 鍚楋紵姝ゆ搷浣滀笉鍙挙閿€銆?");
            if (ImGui.Button("纭畾"))
            {
                TryDeleteSelectedConfigFile(name);
                _homeViewState.CloseDeleteModal();
                ImGui.CloseCurrentPopup();
            }

            ImGui.SameLine();
            if (ImGui.Button("鍙栨秷"))
            {
                _homeViewState.CloseDeleteModal();
                ImGui.CloseCurrentPopup();
            }

            ImGui.EndPopup();
        }
        _homeViewState.IsDeleteModalOpen = isDeleteModalOpen;
    }

    private bool TryCreateEmptyConfigFile(string rawName, out string error)
    {
        if (_configRepository.TryCreateEmptyConfigFile(
                rawName,
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
        _configRepository.TryDeleteConfigFile(baseName);
        RefreshConfigFiles();
        if (_configFiles.Count > 0)
        {
            WriteCurrentConfigFileName(_configFiles[_selectedConfigFileIndex]);
        }
        else
        {
            _configRepository.ClearCurrentConfigPointerFile();
        }
    }

    private const int SmartCorePreviewIntervalMs = 1000 / 60;
    private readonly object _smartCorePreviewWindowLock = new();
    private System.Windows.Forms.Form? _smartCorePreviewWindow;
    private bool _smartCorePreviewShuttingDown;
    private readonly object _snapRangePreviewWindowLock = new();
    private System.Windows.Forms.Form? _snapRangePreviewWindow;
    private bool _snapRangePreviewWindowVisible;
    private bool _snapRangePreviewShuttingDown;

    private void UpdateSmartCorePreviewCaptureDemand(bool enabled)
    {
        _dxgiWorker?.SetPreviewFrameCacheEnabled(enabled);
    }

    private void OpenSmartCorePreviewWindow()
    {
        lock (_smartCorePreviewWindowLock)
        {
            if (_smartCorePreviewWindow is not null && !_smartCorePreviewWindow.IsDisposed)
            {
                _smartCorePreviewWindow.BeginInvoke(new Action(() =>
                {
                    _smartCorePreviewWindow.Show();
                    _smartCorePreviewWindow.Activate();
                    _smartCorePreviewWindow.BringToFront();
                }));
                UpdateSmartCorePreviewCaptureDemand(true);
                return;
            }

            var previewWindowThread = new Thread(() =>
            {
                var initialSize = Math.Max(1, _homeViewState.SnapOuterRange);
                using var form = new SmartCorePreviewForm
                {
                    Text = string.Empty,
                    StartPosition = System.Windows.Forms.FormStartPosition.CenterScreen,
                    ClientSize = new System.Drawing.Size(initialSize, initialSize),
                    TopMost = true,
                    FormBorderStyle = System.Windows.Forms.FormBorderStyle.FixedSingle,
                    MaximizeBox = false,
                    MinimizeBox = false
                };
                form.SizeGripStyle = System.Windows.Forms.SizeGripStyle.Hide;
                form.BackColor = System.Drawing.Color.FromArgb(18, 20, 24);
                form.ShowInTaskbar = false;
                form.Shown += (_, _) =>
                {
                    form.MinimumSize = form.Size;
                    form.MaximumSize = form.Size;
                    form.TopMost = true;
                    UpdateSmartCorePreviewCaptureDemand(true);
                };

                form.VisibleChanged += (_, _) =>
                {
                    UpdateSmartCorePreviewCaptureDemand(form.Visible);
                };

                var frameBuffer = Array.Empty<byte>();
                var lastFrameId = 0;
                var frameWidth = 0;
                var frameHeight = 0;
                string? frameError = null;
                System.Drawing.Bitmap? cachedBitmap = null;

                var refreshTimer = new System.Windows.Forms.Timer { Interval = SmartCorePreviewIntervalMs };
                refreshTimer.Tick += (_, _) =>
                {
                    if (!form.Visible)
                    {
                        return;
                    }

                    var targetSize = Math.Max(1, _homeViewState.SnapOuterRange);
                    var expectedClientSize = new System.Drawing.Size(targetSize, targetSize);
                    if (form.ClientSize != expectedClientSize)
                    {
                        form.ClientSize = expectedClientSize;
                        form.MinimumSize = form.Size;
                        form.MaximumSize = form.Size;
                    }

                    var worker = _dxgiWorker;
                    var hasNewFrame = false;
                    if (worker is not null)
                    {
                        hasNewFrame = worker.TryCopyLatestFrame(ref frameBuffer, ref lastFrameId, out frameWidth, out frameHeight, out frameError);
                    }
                    else
                    {
                        frameWidth = 0;
                        frameHeight = 0;
                    }

                    if (hasNewFrame || worker is null)
                    {
                        form.Invalidate();
                    }
                };

                form.Paint += (_, e) =>
                {
                    e.Graphics.Clear(System.Drawing.Color.FromArgb(18, 20, 24));
                    e.Graphics.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
                    e.Graphics.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.NearestNeighbor;
                    e.Graphics.PixelOffsetMode = System.Drawing.Drawing2D.PixelOffsetMode.Half;

                    if (frameWidth <= 0 || frameHeight <= 0 || frameBuffer.Length != frameWidth * frameHeight * 4)
                    {
                        var statusText = string.IsNullOrWhiteSpace(frameError) ? "绛夊緟鎹曡幏鐢婚潰..." : $"鎹曡幏閿欒: {frameError}";
                        using var statusBrush = new System.Drawing.SolidBrush(System.Drawing.Color.Gainsboro);
                        e.Graphics.DrawString(statusText, form.Font, statusBrush, new System.Drawing.PointF(12f, 12f));
                        return;
                    }

                    var clientRect = new System.Drawing.Rectangle(0, 0, form.ClientSize.Width, form.ClientSize.Height);
                    var scale = Math.Min(clientRect.Width / (float)frameWidth, clientRect.Height / (float)frameHeight);
                    scale = Math.Max(scale, 1f);
                    var drawWidth = Math.Max(1, (int)MathF.Round(frameWidth * scale));
                    var drawHeight = Math.Max(1, (int)MathF.Round(frameHeight * scale));
                    var drawRect = new System.Drawing.Rectangle(
                        clientRect.X + (clientRect.Width - drawWidth) / 2,
                        clientRect.Y + (clientRect.Height - drawHeight) / 2,
                        drawWidth,
                        drawHeight);

                    if (cachedBitmap is null || cachedBitmap.Width != frameWidth || cachedBitmap.Height != frameHeight)
                    {
                        cachedBitmap?.Dispose();
                        cachedBitmap = new System.Drawing.Bitmap(frameWidth, frameHeight, PixelFormat.Format32bppArgb);
                    }

                    var bitmapData = cachedBitmap.LockBits(
                        new System.Drawing.Rectangle(0, 0, frameWidth, frameHeight),
                        ImageLockMode.WriteOnly,
                        PixelFormat.Format32bppArgb);
                    try
                    {
                        Marshal.Copy(frameBuffer, 0, bitmapData.Scan0, frameBuffer.Length);
                    }
                    finally
                    {
                        cachedBitmap.UnlockBits(bitmapData);
                    }

                    e.Graphics.DrawImage(cachedBitmap, drawRect);

                    var boxes = _onnxWorker?.GetDebugBoxes() ?? Array.Empty<OnnxDebugBox>();
                    if (boxes.Length > 0)
                    {
                        using var boxPen = new System.Drawing.Pen(System.Drawing.Color.Red, 2f);
                        for (var i = 0; i < boxes.Length; i++)
                        {
                            var box = boxes[i];
                            var x1 = box.X - box.W * 0.5f;
                            var y1 = box.Y - box.H * 0.5f;
                            var x2 = box.X + box.W * 0.5f;
                            var y2 = box.Y + box.H * 0.5f;

                            var minX = Math.Clamp(MathF.Min(x1, x2), 0f, frameWidth);
                            var minY = Math.Clamp(MathF.Min(y1, y2), 0f, frameHeight);
                            var maxX = Math.Clamp(MathF.Max(x1, x2), 0f, frameWidth);
                            var maxY = Math.Clamp(MathF.Max(y1, y2), 0f, frameHeight);

                            var overlayRect = new System.Drawing.RectangleF(
                                drawRect.Left + minX / frameWidth * drawRect.Width,
                                drawRect.Top + minY / frameHeight * drawRect.Height,
                                (maxX - minX) / frameWidth * drawRect.Width,
                                (maxY - minY) / frameHeight * drawRect.Height);

                            if (overlayRect.Width > 1f && overlayRect.Height > 1f)
                            {
                                e.Graphics.DrawRectangle(boxPen, overlayRect.X, overlayRect.Y, overlayRect.Width, overlayRect.Height);
                            }
                        }
                    }
                };

                form.FormClosing += (_, e) =>
                {
                    if (!_smartCorePreviewShuttingDown && e.CloseReason == System.Windows.Forms.CloseReason.UserClosing)
                    {
                        e.Cancel = true;
                        form.Hide();
                    }
                };

                form.FormClosed += (_, _) =>
                {
                    UpdateSmartCorePreviewCaptureDemand(false);
                    refreshTimer.Stop();
                    refreshTimer.Dispose();
                    cachedBitmap?.Dispose();
                    lock (_smartCorePreviewWindowLock)
                    {
                        _smartCorePreviewWindow = null;
                    }
                };

                lock (_smartCorePreviewWindowLock)
                {
                    _smartCorePreviewWindow = form;
                }

                refreshTimer.Start();
                form.Show();
                System.Windows.Forms.Application.Run(form);
            })
            {
                IsBackground = true,
                Name = "SmartCorePreviewWindowThread"
            };
            previewWindowThread.SetApartmentState(ApartmentState.STA);
            previewWindowThread.Start();
        }
    }

    private void CloseSmartCorePreviewWindow()
    {
        lock (_smartCorePreviewWindowLock)
        {
            if (_smartCorePreviewWindow is not null && !_smartCorePreviewWindow.IsDisposed)
            {
                _smartCorePreviewShuttingDown = true;
                UpdateSmartCorePreviewCaptureDemand(false);
                _smartCorePreviewWindow.BeginInvoke(new Action(() => _smartCorePreviewWindow.Close()));
            }
        }
    }

    private bool IsSmartCorePreviewWindowOpen()
    {
        lock (_smartCorePreviewWindowLock)
        {
            return _smartCorePreviewWindow is not null && !_smartCorePreviewWindow.IsDisposed && _smartCorePreviewWindow.Visible;
        }
    }

    private void OpenSnapRangePreviewWindow()
    {
        lock (_snapRangePreviewWindowLock)
        {
            if (_snapRangePreviewWindow is not null && !_snapRangePreviewWindow.IsDisposed)
            {
                _snapRangePreviewWindow.BeginInvoke(new Action(() =>
                {
                    _snapRangePreviewWindowVisible = true;
                    _snapRangePreviewWindow.Show();
                    _snapRangePreviewWindow.Activate();
                    _snapRangePreviewWindow.BringToFront();
                }));
                return;
            }

            var previewWindowThread = new Thread(() =>
            {
                using var form = new System.Windows.Forms.Form
                {
                    Text = string.Empty,
                    StartPosition = System.Windows.Forms.FormStartPosition.CenterScreen,
                    ClientSize = new System.Drawing.Size(Math.Max(1, _homeViewState.SnapOuterRange), Math.Max(1, _homeViewState.SnapOuterRange)),
                    TopMost = true,
                    FormBorderStyle = System.Windows.Forms.FormBorderStyle.FixedDialog,
                    MaximizeBox = false
                };
                form.BackColor = System.Drawing.Color.FromArgb(20, 22, 26);
                form.ShowInTaskbar = false;

                var refreshTimer = new System.Windows.Forms.Timer { Interval = 50 };
                refreshTimer.Tick += (_, _) =>
                {
                    var outer = Math.Max(1, _homeViewState.SnapOuterRange);
                    var expectedSize = new System.Drawing.Size(outer, outer);
                    if (form.ClientSize != expectedSize)
                    {
                        form.ClientSize = expectedSize;
                    }

                    form.Invalidate();
                };

                form.Paint += (_, e) =>
                {
                    e.Graphics.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;

                    var outer = Math.Max(1, _homeViewState.SnapOuterRange);
                    var inner = Math.Clamp(_homeViewState.SnapInnerRange, 0, outer);
                    var drawDiameter = Math.Max(2, Math.Min(form.ClientSize.Width, form.ClientSize.Height) - 2);
                    var centerX = form.ClientSize.Width / 2f;
                    var centerY = form.ClientSize.Height / 2f;
                    var outerRadius = drawDiameter / 2f;
                    var innerRadius = outerRadius * (inner / (float)outer);

                    using var outerPen = new System.Drawing.Pen(System.Drawing.Color.FromArgb(0, 180, 255), 2f);
                    using var innerPen = new System.Drawing.Pen(System.Drawing.Color.FromArgb(255, 140, 60), 2f);

                    var outerRect = new System.Drawing.RectangleF(
                        centerX - outerRadius,
                        centerY - outerRadius,
                        outerRadius * 2f,
                        outerRadius * 2f);
                    var innerRect = new System.Drawing.RectangleF(
                        centerX - innerRadius,
                        centerY - innerRadius,
                        innerRadius * 2f,
                        innerRadius * 2f);

                    e.Graphics.DrawEllipse(outerPen, outerRect);
                    e.Graphics.DrawEllipse(innerPen, innerRect);
                };

                form.FormClosing += (_, e) =>
                {
                    if (!_snapRangePreviewShuttingDown && e.CloseReason == System.Windows.Forms.CloseReason.UserClosing)
                    {
                        e.Cancel = true;
                        _snapRangePreviewWindowVisible = false;
                        form.Hide();
                    }
                };

                form.FormClosed += (_, _) =>
                {
                    refreshTimer.Stop();
                    refreshTimer.Dispose();
                    lock (_snapRangePreviewWindowLock)
                    {
                        _snapRangePreviewWindowVisible = false;
                        _snapRangePreviewWindow = null;
                    }
                };

                lock (_snapRangePreviewWindowLock)
                {
                    _snapRangePreviewWindow = form;
                }

                refreshTimer.Start();
                _snapRangePreviewWindowVisible = true;
                form.Show();
                System.Windows.Forms.Application.Run(form);
            })
            {
                IsBackground = true,
                Name = "SnapRangePreviewWindowThread"
            };
            previewWindowThread.SetApartmentState(ApartmentState.STA);
            previewWindowThread.Start();
        }
    }

    private bool IsSnapRangePreviewWindowOpen()
    {
        lock (_snapRangePreviewWindowLock)
        {
            return _snapRangePreviewWindowVisible && _snapRangePreviewWindow is not null && !_snapRangePreviewWindow.IsDisposed;
        }
    }

    private void CloseSnapRangePreviewWindow()
    {
        lock (_snapRangePreviewWindowLock)
        {
            if (_snapRangePreviewWindow is not null && !_snapRangePreviewWindow.IsDisposed)
            {
                _snapRangePreviewShuttingDown = true;
                _snapRangePreviewWindow.BeginInvoke(new Action(() => _snapRangePreviewWindow.Close()));
            }
        }
    }

}

internal sealed class SmartCorePreviewForm : System.Windows.Forms.Form
{
    public SmartCorePreviewForm()
    {
        SetStyle(
            System.Windows.Forms.ControlStyles.UserPaint |
            System.Windows.Forms.ControlStyles.AllPaintingInWmPaint |
            System.Windows.Forms.ControlStyles.OptimizedDoubleBuffer,
            true);
        UpdateStyles();
    }

    protected override void OnPaintBackground(System.Windows.Forms.PaintEventArgs e)
    {
        // Skip the default background erase to reduce visible flicker between frames.
    }
}

internal sealed class HomeViewState
{
    public int SnapModeIndex { get; set; } = -1;
    public int SnapOuterRange { get; set; } = 1;
    public float SnapOuterStrength { get; set; }
    public int SnapInnerRange { get; set; } = 1;
    public float SnapInnerStrength { get; set; }
    public float SnapStartStrength { get; set; }
    public float SnapVerticalStrengthFactor { get; set; }
    public float SnapHipfireStrengthFactor { get; set; }
    public float SnapHeight { get; set; }
    public int SnapInnerInterpolationTypeIndex { get; set; }
    public string AddNameBuffer { get; set; } = string.Empty;
    public string AddError { get; set; } = string.Empty;
    public bool IsAddModalOpen { get; set; }
    public bool IsDeleteModalOpen { get; set; }
    public bool IsAddModalOpenRequested { get; set; }
    public bool IsDeleteModalOpenRequested { get; set; }
    public string? PendingDeleteConfigBaseName { get; set; }

    public void ApplySnapConfig(SnapConfigState snapConfig)
    {
        SnapOuterRange = snapConfig.OuterRange;
        SnapInnerRange = snapConfig.InnerRange;
        SnapOuterStrength = snapConfig.OuterStrength;
        SnapInnerStrength = snapConfig.InnerStrength;
        SnapStartStrength = snapConfig.StartStrength;
        SnapVerticalStrengthFactor = snapConfig.VerticalStrengthFactor;
        SnapHipfireStrengthFactor = snapConfig.HipfireStrengthFactor;
        SnapHeight = snapConfig.Height;
        SnapInnerInterpolationTypeIndex = snapConfig.InnerInterpolationTypeIndex;
    }

    public void ResetSnapSettings(
        int snapModeIndex,
        int outerRange,
        int innerRange,
        float outerStrength,
        float innerStrength,
        float startStrength,
        float verticalStrengthFactor,
        float hipfireStrengthFactor,
        float height,
        int interpolationTypeIndex)
    {
        SnapModeIndex = snapModeIndex;
        SnapOuterRange = outerRange;
        SnapInnerRange = innerRange;
        SnapOuterStrength = outerStrength;
        SnapInnerStrength = innerStrength;
        SnapStartStrength = startStrength;
        SnapVerticalStrengthFactor = verticalStrengthFactor;
        SnapHipfireStrengthFactor = hipfireStrengthFactor;
        SnapHeight = height;
        SnapInnerInterpolationTypeIndex = interpolationTypeIndex;
    }

    public void OpenAddModal()
    {
        AddNameBuffer = string.Empty;
        AddError = string.Empty;
        IsAddModalOpen = true;
        IsAddModalOpenRequested = true;
    }

    public void CloseAddModal()
    {
        AddError = string.Empty;
        IsAddModalOpen = false;
    }

    public void OpenDeleteModal(string baseName)
    {
        PendingDeleteConfigBaseName = baseName;
        IsDeleteModalOpen = true;
        IsDeleteModalOpenRequested = true;
    }

    public void CloseDeleteModal()
    {
        PendingDeleteConfigBaseName = null;
        IsDeleteModalOpen = false;
    }
}
