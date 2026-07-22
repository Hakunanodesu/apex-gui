using ImGuiNET;
using System.Numerics;

public sealed partial class MainWindow
{
    private const float SnapFloatStep = 0.01f;
    private const string SnapFloatFormat = "%.2f";

    private readonly record struct SnapSettingsLayout(
        float LabelWidth,
        float LastLabelWidth,
        float RangeInputWidth,
        float StrengthInputWidth,
        float ExtraInputWidth);

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
            DrawSnapStrengthRampRow(layout);

            ImGui.EndTable();
        }

        DrawSnapInterpolationTypeRow(metrics.ReserveWidth, layout.LabelWidth);
    }

    private void NormalizeSnapSettings(int selectedModelSize, int snapOuterRangeMax)
    {
        _homeViewState.ApplySnapConfig(
            SnapConfigCatalog.Normalized(_homeViewState.ToSnapSettings(), selectedModelSize, snapOuterRangeMax));
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
        if (ImGui.IsItemHovered())
        {
            ImGui.SetTooltip("内圈吸附半径，目标进入该范围后按内圈强度进行吸附。\n单位：像素，需小于等于外圈范围。");
        }

        ImGui.TableSetColumnIndex(1);
        ImGui.SetNextItemWidth(layout.RangeInputWidth);
        var snapInnerRange = _homeViewState.SnapInnerRange;
        if (ImGui.InputInt("##SnapInnerRange", ref snapInnerRange, 0, 0))
        {
            _homeViewState.SnapInnerRange = Math.Clamp(snapInnerRange, 1, _homeViewState.SnapOuterRange);
            TryWriteIntToCurrentConfig(SnapConfigCatalog.InnerRangeKey, _homeViewState.SnapInnerRange);
            PushAimAssistConfig();
        }

        ImGui.TableSetColumnIndex(2);
        ImGui.AlignTextToFramePadding();
        ImGui.TextUnformatted("外圈范围");
        if (ImGui.IsItemHovered())
        {
            ImGui.SetTooltip("外圈吸附半径，目标进入该范围即开始吸附。\n单位：像素，上限取决于模型尺寸与显示高度。");
        }

        ImGui.TableSetColumnIndex(3);
        ImGui.SetNextItemWidth(layout.RangeInputWidth);
        var snapOuterRange = _homeViewState.SnapOuterRange;
        if (ImGui.InputInt("##SnapOuterRange", ref snapOuterRange, 0, 0))
        {
            _homeViewState.SnapOuterRange = Math.Clamp(snapOuterRange, selectedModelSize, snapOuterRangeMax);
            _homeViewState.SnapInnerRange = Math.Clamp(_homeViewState.SnapInnerRange, 1, _homeViewState.SnapOuterRange);
            TryWriteIntToCurrentConfig(SnapConfigCatalog.OuterRangeKey, _homeViewState.SnapOuterRange);
            TryWriteIntToCurrentConfig(SnapConfigCatalog.InnerRangeKey, _homeViewState.SnapInnerRange);
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
            SnapConfigCatalog.InnerStrengthKey,
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
            SnapConfigCatalog.OuterStrengthKey,
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
            SnapConfigCatalog.StartStrengthKey,
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
            SnapConfigCatalog.HipfireStrengthFactorKey,
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
            SnapConfigCatalog.VerticalStrengthFactorKey,
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
            SnapConfigCatalog.HeightKey,
            _homeViewState.SnapHeight,
            value => _homeViewState.SnapHeight = value,
            0f,
            1f);
    }

    private void DrawSnapStrengthRampRow(in SnapSettingsLayout layout)
    {
        ImGui.TableNextRow();
        ImGui.TableNextRow();

        ImGui.TableSetColumnIndex(0);
        ImGui.AlignTextToFramePadding();
        ImGui.TextUnformatted("强度爬升时间");
        if (ImGui.IsItemHovered())
        {
            ImGui.SetTooltip("吸附强度从起始强度爬升到目标强度所需的时间（秒）。\n0 表示瞬间到达目标强度，数值越大爬升越平缓。");
        }

        ImGui.TableSetColumnIndex(1);
        ImGui.SetNextItemWidth(layout.StrengthInputWidth);
        DrawClampedConfigFloatInput(
            "##SnapStrengthRampTime",
            SnapConfigCatalog.StrengthRampTimeKey,
            _homeViewState.SnapStrengthRampTime,
            value => _homeViewState.SnapStrengthRampTime = value,
            0f,
            1f,
            0.1f,
            "%.1f");
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
            _homeViewState.SnapInnerInterpolationTypeIndex >= 0 && _homeViewState.SnapInnerInterpolationTypeIndex < AimAssistOptionCatalog.SnapInnerInterpolationTypeOptions.Length
                ? _homeViewState.SnapInnerInterpolationTypeIndex
                : 0;

        ImGui.TableSetColumnIndex(0);
        ImGui.AlignTextToFramePadding();
        ImGui.TextUnformatted("内圈插值类型");

        ImGui.TableSetColumnIndex(1);
        var interpolationComboWidth = MathF.Max(90f, ImGui.GetContentRegionAvail().X - reserveWidth);
        var interpolationIndex = _homeViewState.SnapInnerInterpolationTypeIndex;
        var interpolationChanged = DrawConfigBoundCombo(
            "##SnapInnerInterpolationTypeCombo",
            AimAssistOptionCatalog.SnapInnerInterpolationTypeOptions,
            ref interpolationIndex,
            interpolationComboWidth,
            false);
        _homeViewState.SnapInnerInterpolationTypeIndex = interpolationIndex;
        if (interpolationChanged)
        {
            TryWriteStringToCurrentConfig(SnapConfigCatalog.InnerInterpolationTypeKey, AimAssistOptionCatalog.SnapInnerInterpolationTypeOptions[interpolationIndex]);
            PushAimAssistConfig();
        }

        ImGui.EndTable();
    }

    private void DrawClampedConfigFloatInput(
        string controlId,
        string configKey,
        float currentValue,
        Action<float> setValue,
        float min,
        float max,
        float step = SnapFloatStep,
        string format = SnapFloatFormat)
    {
        var editedValue = currentValue;
        if (!ImGui.InputFloat(controlId, ref editedValue, step, step, format))
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
}
