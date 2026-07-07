using ImGuiNET;

public sealed partial class MainWindow
{
    private void DrawMacroSection(ImGuiStylePtr topPanelStyle)
    {
        ImGui.TableNextRow();
        ImGui.TableNextRow();

        ImGui.TableSetColumnIndex(0);
        ImGui.SetCursorPosY(ImGui.GetCursorPosY() - topPanelStyle.CellPadding.Y);
        ImGui.AlignTextToFramePadding();
        ImGui.TextUnformatted("宏");

        ImGui.TableSetColumnIndex(1);
        ImGui.SetCursorPosY(ImGui.GetCursorPosY() - topPanelStyle.CellPadding.Y);
        if (GetSelectedGameName() != "Apex Legends")
        {
            ImGui.AlignTextToFramePadding();
            ImGui.TextDisabled("无");
            return;
        }

        var modeComboWidth = ImGui.CalcTextSize("按住").X + topPanelStyle.FramePadding.X * 2f + ImGui.GetFrameHeight();
        var bindingComboWidth = MathF.Max(90f, ImGui.CalcTextSize("左摇杆按下").X + topPanelStyle.FramePadding.X * 2f + ImGui.GetFrameHeight());
        var macroInputWidth = ImGui.CalcTextSize("2000").X + topPanelStyle.FramePadding.X * 2f;

        var macro = _homeViewState.Macro;

        ImGui.BeginDisabled(_configFiles.Count == 0);
        var macroEnabled = macro.Enabled;
        if (ImGui.Checkbox("##HomeMacroEnabled", ref macroEnabled))
        {
            macro.Enabled = macroEnabled;
            OnMacroSettingsChanged();
        }
        ImGui.EndDisabled();

        ImGui.SameLine(0f, topPanelStyle.ItemSpacing.X);
        ImGui.BeginDisabled(_configFiles.Count == 0 || !macro.Enabled);

        ImGui.SetNextItemWidth(modeComboWidth);
        var selectedModeLabel = MacroConfigCatalog.TriggerModeOptions[(int)macro.TriggerMode];
        if (ImGui.BeginCombo("##HomeMacroTriggerModeCombo", selectedModeLabel))
        {
            for (var i = 0; i < MacroConfigCatalog.TriggerModeOptions.Length; i++)
            {
                var isSelected = i == (int)macro.TriggerMode;
                if (ImGui.Selectable(MacroConfigCatalog.TriggerModeOptions[i], isSelected))
                {
                    macro.TriggerMode = (MacroTriggerMode)i;
                    OnMacroSettingsChanged();
                }

                if (isSelected)
                {
                    ImGui.SetItemDefaultFocus();
                }
            }

            ImGui.EndCombo();
        }

        ImGui.SameLine(0f, topPanelStyle.ItemSpacing.X);
        var macroTriggerBindingIndex = macro.TriggerBindingIndex;
        var macroTriggerBindingChanged = DrawConfigBoundCombo(
            "##HomeMacroTriggerBindingCombo",
            GamepadBindingCatalog.Options,
            ref macroTriggerBindingIndex,
            bindingComboWidth,
            false);
        macro.TriggerBindingIndex = macroTriggerBindingIndex;
        if (macroTriggerBindingChanged)
        {
            OnMacroSettingsChanged();
        }

        ImGui.SameLine(0f, topPanelStyle.ItemSpacing.X);
        ImGui.SetNextItemWidth(macroInputWidth);
        var macroDelayMs = macro.DelayMs;
        if (ImGui.InputInt("##HomeMacroDelayMs", ref macroDelayMs, 0, 0))
        {
            macro.DelayMs = Math.Clamp(macroDelayMs, MacroConfigCatalog.MinDelayMs, MacroConfigCatalog.MaxDelayMs);
            OnMacroSettingsChanged();
        }

        ImGui.SameLine(0f, topPanelStyle.ItemSpacing.X);
        ImGui.AlignTextToFramePadding();
        ImGui.TextUnformatted("ms 后按下");

        ImGui.SameLine(0f, topPanelStyle.ItemSpacing.X);
        var macroActionBindingIndex = macro.ActionBindingIndex;
        var macroActionBindingChanged = DrawConfigBoundCombo(
            "##HomeMacroActionBindingCombo",
            GamepadBindingCatalog.Options,
            ref macroActionBindingIndex,
            bindingComboWidth,
            false);
        macro.ActionBindingIndex = macroActionBindingIndex;
        if (macroActionBindingChanged)
        {
            OnMacroSettingsChanged();
        }

        ImGui.SameLine(0f, topPanelStyle.ItemSpacing.X);
        ImGui.SetNextItemWidth(macroInputWidth);
        var macroActionDurationMs = macro.ActionDurationMs;
        if (ImGui.InputInt("##HomeMacroActionDurationMs", ref macroActionDurationMs, 0, 0))
        {
            macro.ActionDurationMs = Math.Clamp(
                macroActionDurationMs,
                MacroConfigCatalog.MinActionDurationMs,
                MacroConfigCatalog.MaxActionDurationMs);
            OnMacroSettingsChanged();
        }

        ImGui.SameLine(0f, topPanelStyle.ItemSpacing.X);
        ImGui.AlignTextToFramePadding();
        ImGui.TextUnformatted("ms");
        ImGui.EndDisabled();
    }
}
