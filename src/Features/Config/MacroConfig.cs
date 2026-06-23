using System.Text.Json.Nodes;

internal sealed class MacroEntryState
{
    public int TriggerModeIndex { get; set; }
    public int TriggerBindingIndex { get; set; } = GamepadBindingCatalog.DefaultTouchpadLeftIndex;
    public int DelayMs { get; set; }
    public int ActionBindingIndex { get; set; } = GamepadBindingCatalog.ResolveIndex("A", 4);
    public int ActionDurationMs { get; set; }
}

internal readonly struct MacroRuntimeState
{
    public MacroRuntimeState(
        int triggerModeIndex,
        int triggerBindingIndex,
        int delayMs,
        int actionBindingIndex,
        int actionDurationMs)
    {
        TriggerModeIndex = triggerModeIndex;
        TriggerBindingIndex = triggerBindingIndex;
        DelayMs = delayMs;
        ActionBindingIndex = actionBindingIndex;
        ActionDurationMs = actionDurationMs;
    }

    public int TriggerModeIndex { get; }
    public int TriggerBindingIndex { get; }
    public int DelayMs { get; }
    public int ActionBindingIndex { get; }
    public int ActionDurationMs { get; }
}

internal static class MacroConfigCatalog
{
    public const string ConfigKey = "macros";
    public const int MinDelayMs = 0;
    public const int MaxDelayMs = 2000;
    public const int MinActionDurationMs = 0;
    public const int MaxActionDurationMs = 1000;

    private const string TriggerModeConfigKey = "triggerMode";
    private const string TriggerBindingConfigKey = "triggerBinding";
    private const string DelayMsConfigKey = "delayMs";
    private const string ActionBindingConfigKey = "actionBinding";
    private const string ActionDurationMsConfigKey = "actionDurationMs";

    public static readonly string[] TriggerModeOptions = { "按下", "按住" };

    public static MacroEntryState CreateDefault() => new();

    public static void Normalize(MacroEntryState entry)
    {
        entry.TriggerModeIndex = entry.TriggerModeIndex >= 0 && entry.TriggerModeIndex < TriggerModeOptions.Length
            ? entry.TriggerModeIndex
            : 0;
        entry.TriggerBindingIndex = entry.TriggerBindingIndex >= 0 && entry.TriggerBindingIndex < GamepadBindingCatalog.Options.Length
            ? entry.TriggerBindingIndex
            : GamepadBindingCatalog.DefaultTouchpadLeftIndex;
        entry.ActionBindingIndex = entry.ActionBindingIndex >= 0 && entry.ActionBindingIndex < GamepadBindingCatalog.Options.Length
            ? entry.ActionBindingIndex
            : GamepadBindingCatalog.ResolveIndex("A", 4);
        entry.DelayMs = Math.Clamp(entry.DelayMs, MinDelayMs, MaxDelayMs);
        entry.ActionDurationMs = Math.Clamp(entry.ActionDurationMs, MinActionDurationMs, MaxActionDurationMs);
    }

    public static MacroRuntimeState ToRuntimeState(MacroEntryState entry)
    {
        Normalize(entry);
        return new MacroRuntimeState(
            entry.TriggerModeIndex,
            entry.TriggerBindingIndex,
            entry.DelayMs,
            entry.ActionBindingIndex,
            entry.ActionDurationMs);
    }

    public static MacroRuntimeState[] ToRuntimeStates(IReadOnlyList<MacroEntryState> entries)
    {
        if (entries.Count == 0)
        {
            return Array.Empty<MacroRuntimeState>();
        }

        var runtimeStates = new List<MacroRuntimeState>(entries.Count);
        for (var i = 0; i < entries.Count; i++)
        {
            var runtimeState = ToRuntimeState(entries[i]);
            if (runtimeState.TriggerBindingIndex == runtimeState.ActionBindingIndex)
            {
                continue;
            }

            runtimeStates.Add(runtimeState);
        }

        return runtimeStates.ToArray();
    }

    public static bool TryReadEntry(JsonNode? node, out MacroEntryState entry)
    {
        entry = CreateDefault();
        if (node is not JsonObject obj)
        {
            return false;
        }

        entry.TriggerModeIndex = ResolveOptionIndex(
            TryReadString(obj, TriggerModeConfigKey),
            TriggerModeOptions,
            0);
        entry.TriggerBindingIndex = GamepadBindingCatalog.ResolveIndex(
            TryReadString(obj, TriggerBindingConfigKey),
            GamepadBindingCatalog.DefaultTouchpadLeftIndex);
        entry.ActionBindingIndex = GamepadBindingCatalog.ResolveIndex(
            TryReadString(obj, ActionBindingConfigKey),
            GamepadBindingCatalog.ResolveIndex("A", 4));
        entry.DelayMs = TryReadInt(obj, DelayMsConfigKey) ?? 0;
        entry.ActionDurationMs = TryReadInt(obj, ActionDurationMsConfigKey) ?? 0;
        Normalize(entry);
        return true;
    }

    public static JsonObject ToJsonObject(MacroEntryState entry)
    {
        Normalize(entry);
        return new JsonObject
        {
            [TriggerModeConfigKey] = TriggerModeOptions[entry.TriggerModeIndex],
            [TriggerBindingConfigKey] = GamepadBindingCatalog.Options[entry.TriggerBindingIndex],
            [DelayMsConfigKey] = entry.DelayMs,
            [ActionBindingConfigKey] = GamepadBindingCatalog.Options[entry.ActionBindingIndex],
            [ActionDurationMsConfigKey] = entry.ActionDurationMs
        };
    }

    public static JsonArray ToJsonArray(IReadOnlyList<MacroEntryState> entries)
    {
        var array = new JsonArray();
        for (var i = 0; i < entries.Count; i++)
        {
            array.Add(ToJsonObject(entries[i]));
        }

        return array;
    }

    private static int ResolveOptionIndex(string? value, IReadOnlyList<string> options, int fallback)
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
}
