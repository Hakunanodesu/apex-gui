internal readonly record struct SnapSettingsState(
    int OuterRange,
    int InnerRange,
    float OuterStrength,
    float InnerStrength,
    float StartStrength,
    float VerticalStrengthFactor,
    float HipfireStrengthFactor,
    float Height,
    float StrengthRampTime,
    int InnerInterpolationTypeIndex)
{
    public static SnapSettingsState Default => new(1, 1, 0f, 0f, 0f, 0f, 0f, 0f, 0f, 0);
}

internal static class SnapConfigCatalog
{
    public const string SnapModeKey = "snap";
    public const string OuterRangeKey = "snapOuterRange";
    public const string InnerRangeKey = "snapInnerRange";
    public const string OuterStrengthKey = "snapOuterStrength";
    public const string InnerStrengthKey = "snapInnerStrength";
    public const string StartStrengthKey = "snapStartStrength";
    public const string VerticalStrengthFactorKey = "snapVerticalStrengthFactor";
    public const string HipfireStrengthFactorKey = "snapHipfireStrengthFactor";
    public const string HeightKey = "snapHeight";
    public const string StrengthRampTimeKey = "snapStrengthRampTime";
    public const string InnerInterpolationTypeKey = "snapInnerInterpolationType";

    public static SnapSettingsState Read(
        ConfigRepository repository, string configPath, int selectedModelSize, int displayHeightLimit)
    {
        var snapOuterRangeMax = Math.Max(selectedModelSize, displayHeightLimit);
        var outerRange = Math.Clamp(
            repository.TryReadInt(configPath, OuterRangeKey) ?? SnapSettingsState.Default.OuterRange,
            selectedModelSize,
            snapOuterRangeMax);
        var innerRange = Math.Clamp(
            repository.TryReadInt(configPath, InnerRangeKey) ?? SnapSettingsState.Default.InnerRange,
            1,
            outerRange);
        return new SnapSettingsState(
            outerRange,
            innerRange,
            Math.Clamp(repository.TryReadFloat(configPath, OuterStrengthKey) ?? 0f, 0f, 1f),
            Math.Clamp(repository.TryReadFloat(configPath, InnerStrengthKey) ?? 0f, 0f, 1f),
            Math.Clamp(repository.TryReadFloat(configPath, StartStrengthKey) ?? 0f, 0f, 1f),
            Math.Clamp(repository.TryReadFloat(configPath, VerticalStrengthFactorKey) ?? 0f, 0f, 1f),
            Math.Clamp(repository.TryReadFloat(configPath, HipfireStrengthFactorKey) ?? 0f, 0f, 1f),
            Math.Clamp(repository.TryReadFloat(configPath, HeightKey) ?? 0f, 0f, 1f),
            Math.Clamp(repository.TryReadFloat(configPath, StrengthRampTimeKey) ?? 0f, 0f, 1f),
            AimAssistOptionCatalog.ResolveInterpolationType(repository.TryReadString(configPath, InnerInterpolationTypeKey)));
    }

    public static SnapSettingsState Normalized(SnapSettingsState s, int selectedModelSize, int snapOuterRangeMax)
    {
        var outerRange = Math.Clamp(s.OuterRange, selectedModelSize, snapOuterRangeMax);
        return new SnapSettingsState(
            outerRange,
            Math.Clamp(s.InnerRange, 1, outerRange),
            Math.Clamp(s.OuterStrength, 0f, 1f),
            Math.Clamp(s.InnerStrength, 0f, 1f),
            Math.Clamp(s.StartStrength, 0f, 1f),
            Math.Clamp(s.VerticalStrengthFactor, 0f, 1f),
            Math.Clamp(s.HipfireStrengthFactor, 0f, 1f),
            Math.Clamp(s.Height, 0f, 1f),
            Math.Clamp(s.StrengthRampTime, 0f, 1f),
            Math.Clamp(s.InnerInterpolationTypeIndex, 0, AimAssistOptionCatalog.SnapInnerInterpolationTypeOptions.Length - 1));
    }

    public static void Write(ConfigRepository repository, string configPath, SnapSettingsState s)
    {
        repository.TryWriteInt(configPath, OuterRangeKey, s.OuterRange);
        repository.TryWriteInt(configPath, InnerRangeKey, s.InnerRange);
        repository.TryWriteFloat(configPath, OuterStrengthKey, s.OuterStrength);
        repository.TryWriteFloat(configPath, InnerStrengthKey, s.InnerStrength);
        repository.TryWriteFloat(configPath, StartStrengthKey, s.StartStrength);
        repository.TryWriteFloat(configPath, VerticalStrengthFactorKey, s.VerticalStrengthFactor);
        repository.TryWriteFloat(configPath, HipfireStrengthFactorKey, s.HipfireStrengthFactor);
        repository.TryWriteFloat(configPath, HeightKey, s.Height);
        repository.TryWriteFloat(configPath, StrengthRampTimeKey, s.StrengthRampTime);
        if (s.InnerInterpolationTypeIndex >= 0
            && s.InnerInterpolationTypeIndex < AimAssistOptionCatalog.SnapInnerInterpolationTypeOptions.Length)
        {
            repository.TryWriteString(
                configPath,
                InnerInterpolationTypeKey,
                AimAssistOptionCatalog.SnapInnerInterpolationTypeOptions[s.InnerInterpolationTypeIndex]);
        }
    }
}
