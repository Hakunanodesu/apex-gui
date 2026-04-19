internal readonly struct SmartCoreAimAssistContext
{
    public readonly bool IsEnabled;
    public readonly bool IsMappingActive;
    public readonly int SnapModeIndex;
    public readonly int SnapOuterRange;
    public readonly int SnapInnerRange;
    public readonly float SnapOuterStrength;
    public readonly float SnapInnerStrength;
    public readonly float SnapStartStrength;
    public readonly float SnapVerticalStrengthFactor;
    public readonly float SnapHipfireStrengthFactor;
    public readonly float SnapHeight;
    public readonly int SnapInnerInterpolationTypeIndex;
    public readonly SdlGamepadInputSnapshot Input;
    public readonly OnnxDebugBox[] Boxes;

    public SmartCoreAimAssistContext(
        bool isEnabled,
        bool isMappingActive,
        int snapModeIndex,
        int snapOuterRange,
        int snapInnerRange,
        float snapOuterStrength,
        float snapInnerStrength,
        float snapStartStrength,
        float snapVerticalStrengthFactor,
        float snapHipfireStrengthFactor,
        float snapHeight,
        int snapInnerInterpolationTypeIndex,
        SdlGamepadInputSnapshot input,
        OnnxDebugBox[] boxes)
    {
        IsEnabled = isEnabled;
        IsMappingActive = isMappingActive;
        SnapModeIndex = snapModeIndex;
        SnapOuterRange = snapOuterRange;
        SnapInnerRange = snapInnerRange;
        SnapOuterStrength = snapOuterStrength;
        SnapInnerStrength = snapInnerStrength;
        SnapStartStrength = snapStartStrength;
        SnapVerticalStrengthFactor = snapVerticalStrengthFactor;
        SnapHipfireStrengthFactor = snapHipfireStrengthFactor;
        SnapHeight = snapHeight;
        SnapInnerInterpolationTypeIndex = snapInnerInterpolationTypeIndex;
        Input = input;
        Boxes = boxes;
    }
}
