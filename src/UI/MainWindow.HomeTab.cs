using ImGuiNET;
using System.Numerics;

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
        var inputRefreshButtonWidth = metrics.BaseTextWidth * 2f + topPanelStyle.FramePadding.X * 2f;
        ImGui.SetNextItemWidth(ImGui.GetContentRegionAvail().X - metrics.ReserveWidth);
        ImGui.Combo("##InputDeviceCombo", ref _homeSelectedGamepadIndex, gamepads, gamepads.Length);
        ImGui.SameLine();
        if (ImGui.Button("刷新##HomeInputDeviceRefresh", new Vector2(inputRefreshButtonWidth, 0f)))
        {
            RefreshHomeInputDevices();
        }

        ImGui.EndTable();
    }

    private void DrawConfigSelectionRow(HomeLayoutMetrics metrics, ImGuiStylePtr topPanelStyle)
    {
        ImGui.TableNextRow();
        ImGui.TableNextRow();
        ImGui.TableSetColumnIndex(0);
        ImGui.SetCursorPosY(ImGui.GetCursorPosY() - topPanelStyle.CellPadding.Y);
        ImGui.AlignTextToFramePadding();
        ImGui.TextUnformatted("配置选择");
        ImGui.TableSetColumnIndex(1);
        ImGui.SetCursorPosY(ImGui.GetCursorPosY() - topPanelStyle.CellPadding.Y);
        var configComboWidth = ImGui.GetContentRegionAvail().X - metrics.ReserveWidth;
        DrawConfigFileCombo("##TopConfigCombo", configComboWidth);
        ImGui.SameLine();
        if (ImGui.Button("添加", new Vector2(metrics.AddButtonWidth, 0f)))
        {
            _addConfigNameBuffer = string.Empty;
            _configAddModalError = string.Empty;
            _configAddModalOpen = true;
            _configAddModalOpenRequested = true;
        }

        ImGui.SameLine();
        if (_configFiles.Count > 0)
        {
            if (ImGui.Button("删除", new Vector2(metrics.DeleteButtonWidth, 0f)))
            {
                _pendingDeleteConfigBaseName = _configFiles[Math.Clamp(_selectedConfigFileIndex, 0, _configFiles.Count - 1)];
                _configDeleteModalOpen = true;
                _configDeleteModalOpenRequested = true;
            }
        }
        else
        {
            ImGui.BeginDisabled();
            ImGui.Button("删除", new Vector2(metrics.DeleteButtonWidth, 0f));
            ImGui.EndDisabled();
        }
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
        ImGui.Checkbox("##SmartCoreEnabledCheckbox", ref requestedSmartCoreEnabled);
        _smartCoreMappingState.RequestedEnabled = requestedSmartCoreEnabled;
        ImGui.EndDisabled();
        ImGui.SameLine();
        ImGui.BeginDisabled(!_smartCoreMappingState.RequestedEnabled);
        if (ImGui.Button("预览##SmartCorePreviewButton"))
        {
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
        DrawHomeModelCombo("##HomeModelCombo", modelComboWidth);
        ImGui.SameLine();
        if (ImGui.Button("刷新", new Vector2(refreshButtonWidth, 0f)))
        {
            RefreshOnnxModels();
        }

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
        _snapOuterRange = Math.Clamp(_snapOuterRange, selectedModelSize, snapOuterRangeMax);
        _snapInnerRange = Math.Clamp(_snapInnerRange, 1, _snapOuterRange);
        _snapOuterStrength = Math.Clamp(_snapOuterStrength, 0f, 1f);
        _snapInnerStrength = Math.Clamp(_snapInnerStrength, 0f, 1f);
        var snapRangeInputWidth = ImGui.CalcTextSize("0000").X + topPanelStyle.FramePadding.X * 2f;
        var snapStrengthInputWidth = snapRangeInputWidth + ImGui.GetFrameHeight() * 2f + topPanelStyle.ItemInnerSpacing.X * 2f;
        var snapExtraInputWidth = snapStrengthInputWidth;
        _snapStartStrength = Math.Clamp(_snapStartStrength, 0f, 1f);
        _snapVerticalStrengthFactor = Math.Clamp(_snapVerticalStrengthFactor, 0f, 1f);
        _snapHipfireStrengthFactor = Math.Clamp(_snapHipfireStrengthFactor, 0f, 1f);
        _snapHeight = Math.Clamp(_snapHeight, 0f, 1f);
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
            if (ImGui.InputInt("##SnapInnerRange", ref _snapInnerRange, 0, 0))
            {
                _snapInnerRange = Math.Clamp(_snapInnerRange, 1, _snapOuterRange);
                TryWriteIntToCurrentConfig("snapInnerRange", _snapInnerRange);
            }

            ImGui.TableSetColumnIndex(2);
            ImGui.AlignTextToFramePadding();
            ImGui.TextUnformatted("外圈范围");
            ImGui.TableSetColumnIndex(3);
            ImGui.SetNextItemWidth(snapRangeInputWidth);
            if (ImGui.InputInt("##SnapOuterRange", ref _snapOuterRange, 0, 0))
            {
                _snapOuterRange = Math.Clamp(_snapOuterRange, selectedModelSize, snapOuterRangeMax);
                _snapInnerRange = Math.Clamp(_snapInnerRange, 1, _snapOuterRange);
                TryWriteIntToCurrentConfig("snapOuterRange", _snapOuterRange);
                TryWriteIntToCurrentConfig("snapInnerRange", _snapInnerRange);
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
            if (ImGui.InputFloat("##SnapInnerStrength", ref _snapInnerStrength, 0.01f, 0.01f, "%.2f"))
            {
                _snapInnerStrength = Math.Clamp(_snapInnerStrength, 0f, 1f);
                TryWriteFloatToCurrentConfig("snapInnerStrength", _snapInnerStrength);
            }

            ImGui.TableSetColumnIndex(2);
            ImGui.AlignTextToFramePadding();
            ImGui.TextUnformatted("外圈强度");
            ImGui.TableSetColumnIndex(3);
            ImGui.SetNextItemWidth(snapStrengthInputWidth);
            if (ImGui.InputFloat("##SnapOuterStrength", ref _snapOuterStrength, 0.01f, 0.01f, "%.2f"))
            {
                _snapOuterStrength = Math.Clamp(_snapOuterStrength, 0f, 1f);
                TryWriteFloatToCurrentConfig("snapOuterStrength", _snapOuterStrength);
            }

            ImGui.TableSetColumnIndex(4);
            ImGui.AlignTextToFramePadding();
            ImGui.TextUnformatted("起始强度");
            ImGui.TableSetColumnIndex(5);
            ImGui.SetNextItemWidth(snapExtraInputWidth);
            if (ImGui.InputFloat("##SnapStartStrength", ref _snapStartStrength, 0.01f, 0.01f, "%.2f"))
            {
                _snapStartStrength = Math.Clamp(_snapStartStrength, 0f, 1f);
                TryWriteFloatToCurrentConfig("snapStartStrength", _snapStartStrength);
            }

            // Row 3: extras
            ImGui.TableNextRow();
            ImGui.TableNextRow();
            ImGui.TableSetColumnIndex(0);
            ImGui.AlignTextToFramePadding();
            ImGui.TextUnformatted("腰射强度系数");
            ImGui.TableSetColumnIndex(1);
            ImGui.SetNextItemWidth(snapExtraInputWidth);
            if (ImGui.InputFloat("##SnapHipfireStrengthFactor", ref _snapHipfireStrengthFactor, 0.01f, 0.01f, "%.2f"))
            {
                _snapHipfireStrengthFactor = Math.Clamp(_snapHipfireStrengthFactor, 0f, 1f);
                TryWriteFloatToCurrentConfig("snapHipfireStrengthFactor", _snapHipfireStrengthFactor);
            }

            ImGui.TableSetColumnIndex(2);
            ImGui.AlignTextToFramePadding();
            ImGui.TextUnformatted("垂直强度系数");
            ImGui.TableSetColumnIndex(3);
            ImGui.SetNextItemWidth(snapExtraInputWidth);
            if (ImGui.InputFloat("##SnapVerticalStrengthFactor", ref _snapVerticalStrengthFactor, 0.01f, 0.01f, "%.2f"))
            {
                _snapVerticalStrengthFactor = Math.Clamp(_snapVerticalStrengthFactor, 0f, 1f);
                TryWriteFloatToCurrentConfig("snapVerticalStrengthFactor", _snapVerticalStrengthFactor);
            }

            ImGui.TableSetColumnIndex(4);
            ImGui.AlignTextToFramePadding();
            ImGui.TextUnformatted("吸附高度");
            ImGui.TableSetColumnIndex(5);
            ImGui.SetNextItemWidth(snapExtraInputWidth);
            if (ImGui.InputFloat("##SnapHeight", ref _snapHeight, 0.01f, 0.01f, "%.2f"))
            {
                _snapHeight = Math.Clamp(_snapHeight, 0f, 1f);
                TryWriteFloatToCurrentConfig("snapHeight", _snapHeight);
            }

            // Row 4: interpolation
            ImGui.TableNextRow();
            ImGui.TableNextRow();
            _snapInnerInterpolationTypeIndex = _snapInnerInterpolationTypeIndex >= 0 && _snapInnerInterpolationTypeIndex < SnapInnerInterpolationTypeOptions.Length
                ? _snapInnerInterpolationTypeIndex
                : 0;
            ImGui.TableSetColumnIndex(0);
            ImGui.AlignTextToFramePadding();
            ImGui.TextUnformatted("内圈插值类型");
            ImGui.TableSetColumnIndex(1);
            ImGui.SetNextItemWidth(snapStrengthInputWidth);
            var selectedSnapInnerInterpolationLabel = SnapInnerInterpolationTypeOptions[_snapInnerInterpolationTypeIndex];
            if (ImGui.BeginCombo("##SnapInnerInterpolationTypeCombo", selectedSnapInnerInterpolationLabel))
            {
                for (var i = 0; i < SnapInnerInterpolationTypeOptions.Length; i++)
                {
                    var isSelected = i == _snapInnerInterpolationTypeIndex;
                    if (ImGui.Selectable(SnapInnerInterpolationTypeOptions[i], isSelected))
                    {
                        _snapInnerInterpolationTypeIndex = i;
                        TryWriteStringToCurrentConfig("snapInnerInterpolationType", SnapInnerInterpolationTypeOptions[i]);
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
        _homeSnapModeIndex = _homeSnapModeIndex >= 0 && _homeSnapModeIndex < HomeSnapModeOptions.Length ? _homeSnapModeIndex : 0;
        var selectedSnapModeLabel = HomeSnapModeOptions[_homeSnapModeIndex];
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
                    TryWriteStringToCurrentConfig("snap", HomeSnapModeOptions[i]);
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
}
