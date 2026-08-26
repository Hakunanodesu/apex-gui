using ImGuiNET;
using System.Numerics;
using Keys = OpenTK.Windowing.GraphicsLibraryFramework.Keys;

public sealed partial class MainWindow
{
    private bool DrawConfigBoundCombo(
        string comboId,
        IReadOnlyList<string> options,
        ref int selectedIndex,
        float width,
        bool disabled)
    {
        if (selectedIndex < 0 || selectedIndex >= options.Count)
        {
            selectedIndex = 0;
        }

        ImGui.SetNextItemWidth(width);
        ImGui.BeginDisabled(disabled);
        var changed = false;
        if (ImGui.BeginCombo(comboId, options[selectedIndex]))
        {
            for (var i = 0; i < options.Count; i++)
            {
                var isSelected = i == selectedIndex;
                if (ImGui.Selectable(options[i], isSelected))
                {
                    selectedIndex = i;
                    changed = true;
                }

                if (isSelected)
                {
                    ImGui.SetItemDefaultFocus();
                }
            }

            ImGui.EndCombo();
        }

        ImGui.EndDisabled();
        return changed;
    }

    private enum TouchpadKeyCaptureTarget
    {
        None = 0,
        Left = 1,
        Right = 2,
        Voice = 3
    }

    private readonly record struct HomeLayoutMetrics(
        float FirstColumnWidth,
        float ReserveWidth,
        float AddButtonWidth,
        float DeleteButtonWidth);

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
                                   "选择游戏",
                                   "选择模型",
                                   "吸附参数设定",
                                   "吸附曲线预览",
                                   "按键设定",
                                   "宏",
                                   "开启吸附方式",
                                   "连点策略",
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
        var vigemReady = _mappingRuntimeState.IsViGemBusReady;
        if (!ImGui.BeginTable("##HomeTopTable", 2, ImGuiTableFlags.SizingStretchProp))
        {
            return;
        }

        ImGui.TableSetupColumn("Label", ImGuiTableColumnFlags.WidthFixed, metrics.FirstColumnWidth);
        ImGui.TableSetupColumn("Content", ImGuiTableColumnFlags.WidthStretch);

        DrawDependencyStatusRow(vigemReady, metrics, topPanelStyle);
        DrawConfigSelectionRow(metrics, topPanelStyle);
        DrawSmartCoreRow(metrics);
        DrawDmlAdapterRow(metrics);

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
        var disableConfigSelection = _mappingRuntimeState.IsEnabled;
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

    private void DrawSmartCoreRow(HomeLayoutMetrics metrics)
    {
        ImGui.TableNextRow();
        ImGui.TableNextRow();
        ImGui.TableSetColumnIndex(0);
        ImGui.AlignTextToFramePadding();
        ImGui.TextUnformatted("智慧核心");
        ImGui.TableSetColumnIndex(1);
        ImGui.BeginDisabled(!_mappingRuntimeState.IsDependenciesReady);
        var requestedSmartCoreEnabled = _mappingRuntimeState.RequestedEnabled;
        if (ImGui.Checkbox("##SmartCoreEnabledCheckbox", ref requestedSmartCoreEnabled))
        {
            _mappingRuntimeState.RequestedEnabled = requestedSmartCoreEnabled;
            _viGEmMappingWorker?.SetRequestedEnabled(requestedSmartCoreEnabled);
            if (!requestedSmartCoreEnabled)
            {
                CloseSmartCorePreviewWindow();
                RefreshHomeInputDevices();
            }
            RefreshSmartCoreState();
            PushAimAssistConfig();
            SyncSmartCoreVisionPipeline();
        }
        ImGui.EndDisabled();
        ImGui.SameLine();
        var smartCorePreviewWindowOpen = IsSmartCorePreviewWindowOpen();
        ImGui.BeginDisabled(!_mappingRuntimeState.RequestedEnabled || smartCorePreviewWindowOpen);
        if (ImGui.Button("预览##SmartCorePreviewButton"))
        {
            OpenSmartCorePreviewWindow();
        }
        ImGui.EndDisabled();
        ImGui.SameLine();
        ImGui.BeginDisabled(_mappingRuntimeState.IsEnabled);
        var useWgcCapture = _useWgcCapture;
        if (ImGui.Checkbox("WGC##SmartCoreWgcCapture", ref useWgcCapture))
        {
            _useWgcCapture = useWgcCapture;
            SaveWindowState();
            SyncSmartCoreVisionPipeline();
        }

        if (ImGui.IsItemHovered())
        {
            ImGui.SetTooltip("开启后使用 WGC 截图，关闭则使用 DXGI。");
        }

        ImGui.EndDisabled();
    }

    private void DrawDmlAdapterRow(HomeLayoutMetrics metrics)
    {
        ImGui.TableNextRow();
        ImGui.TableNextRow();
        ImGui.TableSetColumnIndex(1);
        ImGui.BeginDisabled(_mappingRuntimeState.IsEnabled);
        DrawDmlAdapterCombo(metrics.ReserveWidth);
        ImGui.EndDisabled();
    }

    private void DrawDmlAdapterCombo(float reserveWidth)
    {
        var adapters = DmlAdapterInfo.Adapters;
        if (adapters.Count == 0)
        {
            ImGui.AlignTextToFramePadding();
            ImGui.BeginDisabled();
            ImGui.TextUnformatted("未知");
            ImGui.EndDisabled();
            return;
        }

        var selectedIndex = DmlAdapterInfo.SelectedIndex;
        if (selectedIndex < 0 || selectedIndex >= adapters.Count)
        {
            selectedIndex = 0;
        }

        ImGui.SetNextItemWidth(MathF.Max(0f, ImGui.GetContentRegionAvail().X - reserveWidth));
        var changed = false;
        var previewLabel = FormatDmlAdapterLabel(adapters[selectedIndex]);
        if (ImGui.BeginCombo("##DmlAdapterCombo", previewLabel))
        {
            for (var i = 0; i < adapters.Count; i++)
            {
                var isSelected = i == selectedIndex;
                if (ImGui.Selectable(FormatDmlAdapterLabel(adapters[i]), isSelected))
                {
                    selectedIndex = i;
                    changed = true;
                }

                if (isSelected)
                {
                    ImGui.SetItemDefaultFocus();
                }
            }

            ImGui.EndCombo();
        }

        if (changed && DmlAdapterInfo.TrySelectIndex(selectedIndex))
        {
            SaveWindowState();
            SyncSmartCoreVisionPipeline();
        }
    }

    private static string FormatDmlAdapterLabel(DmlAdapterEntry adapter) =>
        $"{adapter.Description} [{adapter.DeviceId}]";

    private void DrawHomeMainTable(HomeLayoutMetrics metrics, ImGuiStylePtr topPanelStyle)
    {
        if (!ImGui.BeginTable("##HomeMainTable", 2, ImGuiTableFlags.SizingStretchProp))
        {
            return;
        }

        ImGui.TableSetupColumn("Label", ImGuiTableColumnFlags.WidthFixed, metrics.FirstColumnWidth);
        ImGui.TableSetupColumn("Content", ImGuiTableColumnFlags.WidthStretch);

        DrawGameSelectionRow(metrics);
        DrawModelSelectionRow(metrics);

        DrawSnapSettingsSection(metrics, topPanelStyle);
        DrawSnapCurveSection(topPanelStyle);
        DrawKeyBindingSection(metrics.ReserveWidth, topPanelStyle);
        DrawMacroSection(topPanelStyle);
        DrawSnapModeSection(metrics.ReserveWidth, topPanelStyle);
        DrawRapidFireStrategySection(metrics.ReserveWidth, topPanelStyle);
        DrawSpecialWeaponLogicSection();

        ImGui.EndTable();
    }

    private void DrawGameSelectionRow(HomeLayoutMetrics metrics)
    {
        ImGui.TableNextRow();
        ImGui.TableSetColumnIndex(0);
        ImGui.AlignTextToFramePadding();
        ImGui.TextUnformatted("选择游戏");
        ImGui.TableSetColumnIndex(1);
        var gameComboWidth = ImGui.GetContentRegionAvail().X - metrics.ReserveWidth;
        var gameIndex = _homeViewState.GameIndex;
        var gameChanged = DrawConfigBoundCombo(
            "##HomeGameCombo",
            HomeGameOptions,
            ref gameIndex,
            gameComboWidth,
            _configFiles.Count == 0);
        _homeViewState.GameIndex = gameIndex;
        if (gameChanged)
        {
            TryWriteStringToCurrentConfig(WeaponTemplateCatalog.GameConfigKey, HomeGameOptions[gameIndex]);
            RefreshSpecialWeaponNamesForCurrentGame();
            ApplySpecialWeaponLogicFromCurrentConfig();
            PushAimAssistConfig();
        }
    }

    private void DrawModelSelectionRow(HomeLayoutMetrics metrics)
    {
        ImGui.TableNextRow();
        ImGui.TableNextRow();
        ImGui.TableSetColumnIndex(0);
        ImGui.AlignTextToFramePadding();
        ImGui.TextUnformatted("选择模型");
        ImGui.TableSetColumnIndex(1);
        var style = ImGui.GetStyle();
        var refreshButtonWidth = ImGui.CalcTextSize("刷新").X + style.FramePadding.X * 2f;
        var modelComboWidth = ImGui.GetContentRegionAvail().X - metrics.ReserveWidth;
        ImGui.BeginDisabled(_mappingRuntimeState.IsEnabled);
        DrawHomeModelCombo("##HomeModelCombo", modelComboWidth);
        ImGui.SameLine();
        if (ImGui.Button("刷新", new Vector2(refreshButtonWidth, 0f)))
        {
            RefreshOnnxModels();
        }
        ImGui.EndDisabled();
    }

    private void DrawKeyBindingSection(float reserveWidth, ImGuiStylePtr topPanelStyle)
    {
        TryCaptureTouchpadCustomKey();

        ImGui.TableNextRow();
        ImGui.TableNextRow();

        ImGui.TableSetColumnIndex(0);
        ImGui.AlignTextToFramePadding();
        ImGui.TextUnformatted("按键设定");

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
        _homeViewState.TouchpadLeftBindingIndex = _homeViewState.TouchpadLeftBindingIndex >= 0 && _homeViewState.TouchpadLeftBindingIndex < GamepadBindingCatalog.TouchpadOptions.Length
            ? _homeViewState.TouchpadLeftBindingIndex
            : GamepadBindingCatalog.DefaultTouchpadLeftIndex;
        _homeViewState.TouchpadRightBindingIndex = _homeViewState.TouchpadRightBindingIndex >= 0 && _homeViewState.TouchpadRightBindingIndex < GamepadBindingCatalog.TouchpadOptions.Length
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
            normalizedVoiceCustomKey = BindingConfigCatalog.DefaultVoiceCustomKey;
        }

        _homeViewState.TouchpadLeftCustomKey = normalizedLeftCustomKey;
        _homeViewState.TouchpadRightCustomKey = normalizedRightCustomKey;
        _homeViewState.VoiceCustomKey = normalizedVoiceCustomKey;
        var disableBindingSelection = _configFiles.Count == 0 || GamepadBindingCatalog.Options.Length == 0;
        var disableTouchpadBindingSelection = _configFiles.Count == 0 || GamepadBindingCatalog.TouchpadOptions.Length == 0;
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
            var aimIndex = _homeViewState.AimBindingIndex;
            var aimChanged = DrawConfigBoundCombo(
                "##HomeAimBindingCombo",
                GamepadBindingCatalog.Options,
                ref aimIndex,
                bindingContentWidth,
                disableBindingSelection);
            _homeViewState.AimBindingIndex = aimIndex;
            if (aimChanged)
            {
                TryWriteStringToCurrentConfig(BindingConfigCatalog.AimBindingKey, GamepadBindingCatalog.Options[aimIndex]);
                PushAimAssistConfig();
            }

            ImGui.TableNextRow();
            ImGui.TableNextRow();
            ImGui.TableSetColumnIndex(0);
            ImGui.AlignTextToFramePadding();
            ImGui.TextUnformatted("开火");

            ImGui.TableSetColumnIndex(1);
            bindingContentWidth = MathF.Max(minBindingContentWidth, ImGui.GetContentRegionAvail().X - reserveWidth);
            var fireIndex = _homeViewState.FireBindingIndex;
            var fireChanged = DrawConfigBoundCombo(
                "##HomeFireBindingCombo",
                GamepadBindingCatalog.Options,
                ref fireIndex,
                bindingContentWidth,
                disableBindingSelection);
            _homeViewState.FireBindingIndex = fireIndex;
            if (fireChanged)
            {
                TryWriteStringToCurrentConfig(BindingConfigCatalog.FireBindingKey, GamepadBindingCatalog.Options[fireIndex]);
                PushAimAssistConfig();
            }

            ImGui.TableNextRow();
            ImGui.TableNextRow();
            ImGui.TableSetColumnIndex(0);
            ImGui.AlignTextToFramePadding();
            ImGui.TextUnformatted("触摸板（左）");

            ImGui.TableSetColumnIndex(1);
            bindingContentWidth = MathF.Max(minBindingContentWidth, ImGui.GetContentRegionAvail().X - reserveWidth);
            var touchpadCaptureButtonWidth = ImGui.CalcTextSize("PrintScreen").X + topPanelStyle.FramePadding.X * 2f;
            var touchpadComboWidth = MathF.Max(70f, bindingContentWidth - topPanelStyle.ItemSpacing.X - touchpadCaptureButtonWidth);
            var touchpadLeftIndex = _homeViewState.TouchpadLeftBindingIndex;
            var touchpadLeftChanged = DrawConfigBoundCombo(
                "##HomeTouchpadLeftBindingCombo",
                GamepadBindingCatalog.TouchpadOptions,
                ref touchpadLeftIndex,
                touchpadComboWidth,
                disableTouchpadBindingSelection);
            _homeViewState.TouchpadLeftBindingIndex = touchpadLeftIndex;
            if (touchpadLeftChanged)
            {
                TryWriteStringToCurrentConfig(BindingConfigCatalog.TouchpadLeftBindingKey, GamepadBindingCatalog.TouchpadOptions[touchpadLeftIndex]);
                if (!GamepadBindingCatalog.IsKeyboardCustomBinding(touchpadLeftIndex))
                {
                    CancelTouchpadKeyCapture();
                }
                PushAimAssistConfig();
            }
            ImGui.SameLine(0f, topPanelStyle.ItemSpacing.X);
            ImGui.BeginDisabled(!leftCustomSelected || disableTouchpadBindingSelection);
            var leftButtonLabel = BuildCustomKeyCaptureButtonLabel(TouchpadKeyCaptureTarget.Left, _homeViewState.TouchpadLeftCustomKey);
            if (ImGui.Button($"{leftButtonLabel}###HomeTouchpadLeftCustomKeyCaptureButton", new Vector2(touchpadCaptureButtonWidth, 0f)))
            {
                ArmTouchpadKeyCapture(TouchpadKeyCaptureTarget.Left);
            }
            ImGui.EndDisabled();

            ImGui.TableNextRow();
            ImGui.TableNextRow();
            ImGui.TableSetColumnIndex(0);
            ImGui.AlignTextToFramePadding();
            ImGui.TextUnformatted("触摸板（右）");

            ImGui.TableSetColumnIndex(1);
            bindingContentWidth = MathF.Max(minBindingContentWidth, ImGui.GetContentRegionAvail().X - reserveWidth);
            touchpadComboWidth = MathF.Max(70f, bindingContentWidth - topPanelStyle.ItemSpacing.X - touchpadCaptureButtonWidth);
            var touchpadRightIndex = _homeViewState.TouchpadRightBindingIndex;
            var touchpadRightChanged = DrawConfigBoundCombo(
                "##HomeTouchpadRightBindingCombo",
                GamepadBindingCatalog.TouchpadOptions,
                ref touchpadRightIndex,
                touchpadComboWidth,
                disableTouchpadBindingSelection);
            _homeViewState.TouchpadRightBindingIndex = touchpadRightIndex;
            if (touchpadRightChanged)
            {
                TryWriteStringToCurrentConfig(BindingConfigCatalog.TouchpadRightBindingKey, GamepadBindingCatalog.TouchpadOptions[touchpadRightIndex]);
                if (!GamepadBindingCatalog.IsKeyboardCustomBinding(touchpadRightIndex))
                {
                    CancelTouchpadKeyCapture();
                }
                PushAimAssistConfig();
            }
            ImGui.SameLine(0f, topPanelStyle.ItemSpacing.X);
            ImGui.BeginDisabled(!rightCustomSelected || disableTouchpadBindingSelection);
            var rightButtonLabel = BuildCustomKeyCaptureButtonLabel(TouchpadKeyCaptureTarget.Right, _homeViewState.TouchpadRightCustomKey);
            if (ImGui.Button($"{rightButtonLabel}###HomeTouchpadRightCustomKeyCaptureButton", new Vector2(touchpadCaptureButtonWidth, 0f)))
            {
                ArmTouchpadKeyCapture(TouchpadKeyCaptureTarget.Right);
            }
            ImGui.EndDisabled();

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
            var voiceIndex = _homeViewState.VoiceBindingIndex;
            var voiceChanged = DrawConfigBoundCombo(
                "##HomeVoiceBindingCombo",
                GamepadBindingCatalog.Options,
                ref voiceIndex,
                voiceComboWidth,
                disableBindingSelection);
            _homeViewState.VoiceBindingIndex = voiceIndex;
            if (voiceChanged)
            {
                TryWriteStringToCurrentConfig(BindingConfigCatalog.VoiceBindingKey, GamepadBindingCatalog.Options[voiceIndex]);
                PushAimAssistConfig();
            }
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
            TryWriteStringToCurrentConfig(BindingConfigCatalog.TouchpadLeftCustomKeyKey, capturedDisplayName);
        }
        else if (_activeTouchpadKeyCaptureTarget == TouchpadKeyCaptureTarget.Right)
        {
            _homeViewState.TouchpadRightCustomKey = capturedDisplayName;
            TryWriteStringToCurrentConfig(BindingConfigCatalog.TouchpadRightCustomKeyKey, capturedDisplayName);
        }
        else
        {
            _homeViewState.VoiceCustomKey = capturedDisplayName;
            TryWriteStringToCurrentConfig(BindingConfigCatalog.VoiceCustomKeyKey, capturedDisplayName);
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
        var snapComboWidth = ImGui.GetContentRegionAvail().X - reserveWidth;
        var snapModeIndex = _homeViewState.SnapModeIndex;
        var snapModeChanged = DrawConfigBoundCombo(
            "##HomeSnapModeCombo",
            AimAssistOptionCatalog.SnapModeOptions,
            ref snapModeIndex,
            snapComboWidth,
            _configFiles.Count == 0);
        _homeViewState.SnapModeIndex = snapModeIndex;
        if (snapModeChanged)
        {
            TryWriteStringToCurrentConfig(SnapConfigCatalog.SnapModeKey, AimAssistOptionCatalog.SnapModeOptions[snapModeIndex]);
            PushAimAssistConfig();
        }
    }

    private void DrawRapidFireStrategySection(float reserveWidth, ImGuiStylePtr topPanelStyle)
    {
        ImGui.TableNextRow();
        ImGui.TableNextRow();

        ImGui.TableSetColumnIndex(0);
        ImGui.AlignTextToFramePadding();
        ImGui.TextUnformatted("连点策略");
        ImGui.TableSetColumnIndex(1);
        _homeViewState.RapidFireHz = Math.Clamp(_homeViewState.RapidFireHz, MinRapidFireHz, MaxRapidFireHz);
        var style = ImGui.GetStyle();
        var minRapidFireStrategyComboWidth = 0f;
        for (var i = 0; i < AimAssistOptionCatalog.RapidFireStrategyOptions.Length; i++)
        {
            minRapidFireStrategyComboWidth = MathF.Max(minRapidFireStrategyComboWidth, ImGui.CalcTextSize(AimAssistOptionCatalog.RapidFireStrategyOptions[i]).X);
        }

        minRapidFireStrategyComboWidth += style.FramePadding.X * 2f + ImGui.GetFrameHeight();
        var rapidFireHzLabel = "连点频率";
        const string rapidFireHzTooltip = "按住开火键时的连点速度，表示每秒完整触发几次开火。\n范围：1–30，仅在连点策略非「关闭连点」时生效。";
        var rapidFireHzInputWidth = ImGui.CalcTextSize("000").X + style.FramePadding.X * 2f + ImGui.GetFrameHeight() * 2f + style.ItemInnerSpacing.X * 2f;
        var rapidFireHzLabelWidth = ImGui.CalcTextSize(rapidFireHzLabel).X;
        var rapidFireRowSpacing = style.ItemSpacing.X * 2f;
        var rapidFireRowContentWidth = ImGui.GetContentRegionAvail().X - reserveWidth;
        var rapidFireStrategyComboWidth = MathF.Max(
            minRapidFireStrategyComboWidth,
            rapidFireRowContentWidth - rapidFireHzInputWidth - rapidFireHzLabelWidth - rapidFireRowSpacing);
        var rapidFireStrategyIndex = _homeViewState.RapidFireStrategyIndex;
        var rapidFireStrategyChanged = DrawConfigBoundCombo(
            "##HomeRapidFireStrategyCombo",
            AimAssistOptionCatalog.RapidFireStrategyOptions,
            ref rapidFireStrategyIndex,
            rapidFireStrategyComboWidth,
            _configFiles.Count == 0);
        _homeViewState.RapidFireStrategyIndex = rapidFireStrategyIndex;
        if (rapidFireStrategyChanged)
        {
            TryWriteStringToCurrentConfig(SpecialWeaponLogicCatalog.RapidFireStrategyKey, AimAssistOptionCatalog.RapidFireStrategyOptions[rapidFireStrategyIndex]);
            PushAimAssistConfig();
        }

        ImGui.SameLine(0f, style.ItemSpacing.X);
        ImGui.AlignTextToFramePadding();
        ImGui.TextUnformatted(rapidFireHzLabel);
        if (ImGui.IsItemHovered())
        {
            ImGui.SetTooltip(rapidFireHzTooltip);
        }

        ImGui.SameLine(0f, style.ItemSpacing.X);
        ImGui.SetNextItemWidth(rapidFireHzInputWidth);
        ImGui.BeginDisabled(_homeViewState.RapidFireStrategyIndex == (int)RapidFireStrategy.Off);
        var rapidFireHz = _homeViewState.RapidFireHz;
        if (ImGui.InputInt("##HomeRapidFireHz", ref rapidFireHz, 1, 5))
        {
            _homeViewState.RapidFireHz = Math.Clamp(rapidFireHz, MinRapidFireHz, MaxRapidFireHz);
            TryWriteIntToCurrentConfig(SpecialWeaponLogicCatalog.RapidFireHzKey, _homeViewState.RapidFireHz);
            PushAimAssistConfig();
        }

        if (ImGui.IsItemHovered())
        {
            ImGui.SetTooltip(rapidFireHzTooltip);
        }

        ImGui.EndDisabled();
    }

    private void DrawSpecialWeaponLogicSection()
    {
        ImGui.TableNextRow();
        ImGui.TableNextRow();

        ImGui.TableSetColumnIndex(0);
        ImGui.AlignTextToFramePadding();
        ImGui.TextUnformatted("特殊武器逻辑");
        ImGui.TableSetColumnIndex(1);
        ImGui.BeginDisabled(_configFiles.Count == 0);
        var disableAimSnapColumn = _homeViewState.SnapModeIndex == (int)SnapMode.AimAndFire;
        var disableRapidFireColumn = _homeViewState.RapidFireStrategyIndex != (int)RapidFireStrategy.WeaponBased;
        var (weaponNameColumnWidth, aimSnapColumnWidth, rapidFireColumnWidth, releaseFireColumnWidth) = MeasureSpecialWeaponColumnWidths();
        if (ImGui.BeginTable(
                "##SpecialWeaponLogicTable",
                4,
                ImGuiTableFlags.BordersInnerV | ImGuiTableFlags.BordersOuter | ImGuiTableFlags.RowBg | ImGuiTableFlags.SizingFixedFit | ImGuiTableFlags.NoHostExtendX))
        {
            ImGui.TableSetupColumn("武器名", ImGuiTableColumnFlags.WidthFixed, weaponNameColumnWidth);
            ImGui.TableSetupColumn("瞄准 + 开火吸附", ImGuiTableColumnFlags.WidthFixed, aimSnapColumnWidth);
            ImGui.TableSetupColumn("开火连点", ImGuiTableColumnFlags.WidthFixed, rapidFireColumnWidth);
            ImGui.TableSetupColumn("松手开火", ImGuiTableColumnFlags.WidthFixed, releaseFireColumnWidth);
            ImGui.TableHeadersRow();

            for (var i = 0; i < _specialWeaponNames.Length; i++)
            {
                ImGui.TableNextRow();

                ImGui.TableSetColumnIndex(0);
                ImGui.AlignTextToFramePadding();
                ImGui.TextUnformatted(_specialWeaponNames[i]);

                ImGui.BeginDisabled(disableAimSnapColumn);
                DrawSpecialWeaponToggleCell(i, 1, $"##SpecialWeaponAimSnap_{i}", ref _specialWeaponAimSnapEnabled[i]);
                ImGui.EndDisabled();
                ImGui.BeginDisabled(disableRapidFireColumn);
                DrawSpecialWeaponToggleCell(
                    i,
                    2,
                    $"##SpecialWeaponRapidFire_{i}",
                    ref _specialWeaponRapidFireEnabled[i],
                    ref _specialWeaponReleaseFireEnabled[i]);
                ImGui.EndDisabled();
                DrawSpecialWeaponToggleCell(
                    i,
                    3,
                    $"##SpecialWeaponReleaseFire_{i}",
                    ref _specialWeaponReleaseFireEnabled[i],
                    ref _specialWeaponRapidFireEnabled[i]);
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

        var aimSnapColumnWidth = ImGui.CalcTextSize("瞄准 + 开火吸附").X;
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
        var unusedExclusiveFlag = false;
        DrawSpecialWeaponToggleCell(weaponIndex, columnIndex, controlId, ref flag, ref unusedExclusiveFlag);
    }

    private void DrawSpecialWeaponToggleCell(int weaponIndex, int columnIndex, string controlId, ref bool flag, ref bool exclusiveFlag)
    {
        ImGui.TableSetColumnIndex(columnIndex);
        if (!ImGui.Checkbox(controlId, ref flag))
        {
            return;
        }

        if (flag)
        {
            exclusiveFlag = false;
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
        _cachedConnectedGamepads = _sdlGamepadWorker?.GetConnectedGamepads(forceRefresh)
                                   ?? Array.Empty<(uint InstanceId, string Name)>();
        if (_cachedConnectedGamepads.Length == 0)
        {
            _cachedGamepadOptions = Array.Empty<string>();
            return;
        }

        _cachedGamepadOptions = new string[_cachedConnectedGamepads.Length];
        for (var i = 0; i < _cachedConnectedGamepads.Length; i++)
        {
            _cachedGamepadOptions[i] = _cachedConnectedGamepads[i].Name;
        }
    }


}

