internal sealed class ConfigSelectionService
{
    public int ResolveOptionIndex(string? value, IReadOnlyList<string> options, int fallback = 0)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return fallback;
        }

        for (var i = 0; i < options.Count; i++)
        {
            if (string.Equals(options[i], value, StringComparison.OrdinalIgnoreCase))
            {
                return i;
            }
        }

        return fallback;
    }

    public int ResolveModelIndex(string? modelName, IReadOnlyList<OnnxModelConfig> models)
    {
        if (string.IsNullOrWhiteSpace(modelName))
        {
            return -1;
        }

        for (var i = 0; i < models.Count; i++)
        {
            if (string.Equals(models[i].DisplayName, modelName, StringComparison.OrdinalIgnoreCase))
            {
                return i;
            }
        }

        return -1;
    }

    public SnapConfigState ReadSnapConfig(
        ConfigService configService,
        string configPath,
        int selectedModelSize,
        int displayHeightLimit,
        int defaultOuterRange,
        int defaultInnerRange,
        float defaultOuterStrength,
        float defaultInnerStrength,
        float defaultStartStrength,
        float defaultVerticalStrengthFactor,
        float defaultHipfireStrengthFactor,
        float defaultHeight,
        IReadOnlyList<string> interpolationOptions)
    {
        var snapOuterRangeMax = Math.Max(selectedModelSize, displayHeightLimit);
        var outerRange = Math.Clamp(configService.TryReadInt(configPath, "snapOuterRange") ?? defaultOuterRange, selectedModelSize, snapOuterRangeMax);
        var innerRange = Math.Clamp(configService.TryReadInt(configPath, "snapInnerRange") ?? defaultInnerRange, 1, outerRange);
        var outerStrength = Math.Clamp(configService.TryReadFloat(configPath, "snapOuterStrength") ?? defaultOuterStrength, 0f, 1f);
        var innerStrength = Math.Clamp(configService.TryReadFloat(configPath, "snapInnerStrength") ?? defaultInnerStrength, 0f, 1f);
        var startStrength = Math.Clamp(configService.TryReadFloat(configPath, "snapStartStrength") ?? defaultStartStrength, 0f, 1f);
        var verticalStrengthFactor = Math.Clamp(configService.TryReadFloat(configPath, "snapVerticalStrengthFactor") ?? defaultVerticalStrengthFactor, 0f, 1f);
        var hipfireStrengthFactor = Math.Clamp(configService.TryReadFloat(configPath, "snapHipfireStrengthFactor") ?? defaultHipfireStrengthFactor, 0f, 1f);
        var height = Math.Clamp(configService.TryReadFloat(configPath, "snapHeight") ?? defaultHeight, 0f, 1f);
        var interpolationTypeIndex = ResolveOptionIndex(
            configService.TryReadString(configPath, "snapInnerInterpolationType"),
            interpolationOptions,
            0);

        return new SnapConfigState
        {
            OuterRange = outerRange,
            InnerRange = innerRange,
            OuterStrength = outerStrength,
            InnerStrength = innerStrength,
            StartStrength = startStrength,
            VerticalStrengthFactor = verticalStrengthFactor,
            HipfireStrengthFactor = hipfireStrengthFactor,
            Height = height,
            InnerInterpolationTypeIndex = interpolationTypeIndex
        };
    }
}
