internal sealed class SnapConfigState
{
    public int OuterRange { get; init; }

    public int InnerRange { get; init; }

    public float OuterStrength { get; init; }

    public float InnerStrength { get; init; }

    public float StartStrength { get; init; }

    public float VerticalStrengthFactor { get; init; }

    public float HipfireStrengthFactor { get; init; }

    public float Height { get; init; }

    public int InnerInterpolationTypeIndex { get; init; }
}
