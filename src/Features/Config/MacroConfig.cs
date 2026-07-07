using System.Text.Json.Nodes;

internal enum MacroTriggerMode
{
    Press,
    Hold
}

internal sealed class MacroEntryState
{
    public bool Enabled { get; set; } = true;
    public MacroTriggerMode TriggerMode { get; set; }
    public int TriggerBindingIndex { get; set; } = GamepadBindingCatalog.DefaultTouchpadLeftIndex;
    public int DelayMs { get; set; }
    public int ActionBindingIndex { get; set; } = GamepadBindingCatalog.ResolveIndex("A", 4);
    public int ActionDurationMs { get; set; }
}

internal readonly record struct MacroRuntimeState(
    MacroTriggerMode TriggerMode,
    int TriggerBindingIndex,
    int DelayMs,
    int ActionBindingIndex,
    int ActionDurationMs);

internal static class MacroConfigCatalog
{
    public const string ConfigKey = "macro";
    public const int MinDelayMs = 0;
    public const int MaxDelayMs = 2000;
    public const int MinActionDurationMs = 0;
    public const int MaxActionDurationMs = 1000;

    private const string EnabledConfigKey = "enabled";
    private const string TriggerModeConfigKey = "triggerMode";
    private const string TriggerBindingConfigKey = "triggerBinding";
    private const string DelayMsConfigKey = "delayMs";
    private const string ActionBindingConfigKey = "actionBinding";
    private const string ActionDurationMsConfigKey = "actionDurationMs";

    public static readonly string[] TriggerModeOptions = { "按下", "按住" };

    public static MacroEntryState CreateDefault() => new();

    public static MacroEntryState Normalized(MacroEntryState entry)
    {
        var triggerMode = Enum.IsDefined(entry.TriggerMode) ? entry.TriggerMode : MacroTriggerMode.Press;
        return new MacroEntryState
        {
            Enabled = entry.Enabled,
            TriggerMode = triggerMode,
            TriggerBindingIndex = ClampIndex(entry.TriggerBindingIndex, GamepadBindingCatalog.DefaultTouchpadLeftIndex),
            ActionBindingIndex = ClampIndex(entry.ActionBindingIndex, GamepadBindingCatalog.ResolveIndex("A", 4)),
            DelayMs = Math.Clamp(entry.DelayMs, MinDelayMs, MaxDelayMs),
            ActionDurationMs = Math.Clamp(entry.ActionDurationMs, MinActionDurationMs, MaxActionDurationMs)
        };
    }

    public static MacroRuntimeState? ToRuntimeState(MacroEntryState entry)
    {
        var n = Normalized(entry);
        if (!n.Enabled || n.TriggerBindingIndex == n.ActionBindingIndex)
        {
            return null;
        }

        return new MacroRuntimeState(
            n.TriggerMode,
            n.TriggerBindingIndex,
            n.DelayMs,
            n.ActionBindingIndex,
            n.ActionDurationMs);
    }

    public static bool TryReadEntry(JsonNode? node, out MacroEntryState entry)
    {
        entry = CreateDefault();
        if (node is not JsonObject obj)
        {
            return false;
        }

        entry.Enabled = TryReadBool(obj, EnabledConfigKey) ?? true;
        entry.TriggerMode = ResolveTriggerMode(TryReadString(obj, TriggerModeConfigKey));
        entry.TriggerBindingIndex = GamepadBindingCatalog.ResolveIndex(
            TryReadString(obj, TriggerBindingConfigKey),
            GamepadBindingCatalog.DefaultTouchpadLeftIndex);
        entry.ActionBindingIndex = GamepadBindingCatalog.ResolveIndex(
            TryReadString(obj, ActionBindingConfigKey),
            GamepadBindingCatalog.ResolveIndex("A", 4));
        entry.DelayMs = TryReadInt(obj, DelayMsConfigKey) ?? 0;
        entry.ActionDurationMs = TryReadInt(obj, ActionDurationMsConfigKey) ?? 0;
        entry = Normalized(entry);
        return true;
    }

    public static JsonObject ToJsonObject(MacroEntryState entry)
    {
        var n = Normalized(entry);
        return new JsonObject
        {
            [EnabledConfigKey] = n.Enabled,
            [TriggerModeConfigKey] = TriggerModeOptions[(int)n.TriggerMode],
            [TriggerBindingConfigKey] = GamepadBindingCatalog.Options[n.TriggerBindingIndex],
            [DelayMsConfigKey] = n.DelayMs,
            [ActionBindingConfigKey] = GamepadBindingCatalog.Options[n.ActionBindingIndex],
            [ActionDurationMsConfigKey] = n.ActionDurationMs
        };
    }

    private static int ClampIndex(int value, int fallback)
    {
        return value >= 0 && value < GamepadBindingCatalog.Options.Length ? value : fallback;
    }

    private static MacroTriggerMode ResolveTriggerMode(string? value)
    {
        for (var i = 0; i < TriggerModeOptions.Length; i++)
        {
            if (string.Equals(TriggerModeOptions[i], value, StringComparison.OrdinalIgnoreCase))
            {
                return (MacroTriggerMode)i;
            }
        }

        return MacroTriggerMode.Press;
    }

    private static string? TryReadString(JsonObject obj, string key)
    {
        try
        {
            return obj[key]?.GetValue<string>()?.Trim();
        }
        catch
        {
            return null;
        }
    }

    private static int? TryReadInt(JsonObject obj, string key)
    {
        try
        {
            return obj[key]?.GetValue<int>();
        }
        catch
        {
            return null;
        }
    }

    private static bool? TryReadBool(JsonObject obj, string key)
    {
        try
        {
            return obj[key]?.GetValue<bool>();
        }
        catch
        {
            return null;
        }
    }
}
