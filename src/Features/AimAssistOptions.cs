internal enum SnapMode
{
    Fire = 0,
    AimAndFire = 1
}

internal enum RapidFireStrategy
{
    Off = 0,
    Always = 1,
    WeaponBased = 2
}

internal static class AimAssistOptionCatalog
{
    public const string RapidFireStrategyKey = "rapidFireStrategy";
    public const string RapidFireHzKey = "rapidFireHz";

    public static readonly string[] SnapModeOptions = { "开火吸附", "瞄准 + 开火吸附" };
    public static readonly string[] RapidFireStrategyOptions = { "关闭连点", "始终连点", "根据当前武器连点" };
    public static readonly string[] SnapInnerInterpolationTypeOptions =
    {
        "Linear",
        "Quadratic Ease-In",
        "Quadratic Ease-Out",
        "Quadratic Ease-In-Out"
    };

    public static string Label(SnapMode mode) => SnapModeOptions[(int)mode];

    public static string Label(RapidFireStrategy strategy) => RapidFireStrategyOptions[(int)strategy];

    public static SnapMode ResolveSnapMode(string? label)
    {
        return (SnapMode)ResolveIndex(label, SnapModeOptions, (int)SnapMode.Fire);
    }

    public static RapidFireStrategy ResolveRapidFireStrategy(string? label)
    {
        return (RapidFireStrategy)ResolveIndex(label, RapidFireStrategyOptions, (int)RapidFireStrategy.WeaponBased);
    }

    public static int ResolveInterpolationType(string? label)
    {
        return ResolveIndex(label, SnapInnerInterpolationTypeOptions, 0);
    }

    public static SnapMode ResolveSnapMode(int index)
    {
        return index >= 0 && index < SnapModeOptions.Length ? (SnapMode)index : SnapMode.Fire;
    }

    public static RapidFireStrategy ResolveRapidFireStrategy(int index)
    {
        return index >= 0 && index < RapidFireStrategyOptions.Length ? (RapidFireStrategy)index : RapidFireStrategy.WeaponBased;
    }

    private static int ResolveIndex(string? label, string[] options, int fallback)
    {
        if (string.IsNullOrWhiteSpace(label))
        {
            return fallback;
        }

        var index = Array.IndexOf(options, label);
        return index >= 0 ? index : fallback;
    }
}
