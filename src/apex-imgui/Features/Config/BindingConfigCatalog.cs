internal readonly record struct BindingConfigState(
    int AimBindingIndex,
    int FireBindingIndex,
    int VoiceBindingIndex,
    string VoiceCustomKey,
    int TouchpadLeftBindingIndex,
    int TouchpadRightBindingIndex,
    string TouchpadLeftCustomKey,
    string TouchpadRightCustomKey)
{
    public static BindingConfigState Default => new(
        GamepadBindingCatalog.DefaultAimIndex,
        GamepadBindingCatalog.DefaultFireIndex,
        GamepadBindingCatalog.DefaultTouchpadLeftIndex,
        BindingConfigCatalog.DefaultVoiceCustomKey,
        GamepadBindingCatalog.DefaultTouchpadLeftIndex,
        GamepadBindingCatalog.DefaultTouchpadRightIndex,
        GamepadBindingCatalog.DefaultCustomKeyboardKeyName,
        GamepadBindingCatalog.DefaultCustomKeyboardKeyName);
}

internal static class BindingConfigCatalog
{
    public const string AimBindingKey = "aimBinding";
    public const string FireBindingKey = "fireBinding";
    public const string VoiceBindingKey = "voiceBinding";
    public const string VoiceCustomKeyKey = "voiceCustomKey";
    public const string TouchpadLeftBindingKey = "touchpadLeftBinding";
    public const string TouchpadRightBindingKey = "touchpadRightBinding";
    public const string TouchpadLeftCustomKeyKey = "touchpadLeftCustomKey";
    public const string TouchpadRightCustomKeyKey = "touchpadRightCustomKey";

    public const string DefaultVoiceCustomKey = "V";

    public static BindingConfigState Read(ConfigRepository repository, string configPath)
    {
        return new BindingConfigState(
            ResolveOptionIndex(repository.TryReadString(configPath, AimBindingKey), GamepadBindingCatalog.DefaultAimIndex),
            ResolveOptionIndex(repository.TryReadString(configPath, FireBindingKey), GamepadBindingCatalog.DefaultFireIndex),
            ResolveOptionIndex(repository.TryReadString(configPath, VoiceBindingKey), GamepadBindingCatalog.DefaultTouchpadLeftIndex),
            NormalizeCustomKeyboardKey(repository.TryReadString(configPath, VoiceCustomKeyKey), DefaultVoiceCustomKey),
            ResolveTouchpadOptionIndex(repository.TryReadString(configPath, TouchpadLeftBindingKey), GamepadBindingCatalog.DefaultTouchpadLeftIndex),
            ResolveTouchpadOptionIndex(repository.TryReadString(configPath, TouchpadRightBindingKey), GamepadBindingCatalog.DefaultTouchpadRightIndex),
            NormalizeCustomKeyboardKey(repository.TryReadString(configPath, TouchpadLeftCustomKeyKey), GamepadBindingCatalog.DefaultCustomKeyboardKeyName),
            NormalizeCustomKeyboardKey(repository.TryReadString(configPath, TouchpadRightCustomKeyKey), GamepadBindingCatalog.DefaultCustomKeyboardKeyName));
    }

    public static void Write(ConfigRepository repository, string configPath, BindingConfigState s)
    {
        if (s.AimBindingIndex >= 0 && s.AimBindingIndex < GamepadBindingCatalog.Options.Length)
        {
            repository.TryWriteString(configPath, AimBindingKey, GamepadBindingCatalog.Options[s.AimBindingIndex]);
        }

        if (s.FireBindingIndex >= 0 && s.FireBindingIndex < GamepadBindingCatalog.Options.Length)
        {
            repository.TryWriteString(configPath, FireBindingKey, GamepadBindingCatalog.Options[s.FireBindingIndex]);
        }

        if (s.VoiceBindingIndex >= 0 && s.VoiceBindingIndex < GamepadBindingCatalog.Options.Length)
        {
            repository.TryWriteString(configPath, VoiceBindingKey, GamepadBindingCatalog.Options[s.VoiceBindingIndex]);
        }

        repository.TryWriteString(configPath, VoiceCustomKeyKey, s.VoiceCustomKey);

        if (s.TouchpadLeftBindingIndex >= 0 && s.TouchpadLeftBindingIndex < GamepadBindingCatalog.TouchpadOptions.Length)
        {
            repository.TryWriteString(configPath, TouchpadLeftBindingKey, GamepadBindingCatalog.TouchpadOptions[s.TouchpadLeftBindingIndex]);
        }

        if (s.TouchpadRightBindingIndex >= 0 && s.TouchpadRightBindingIndex < GamepadBindingCatalog.TouchpadOptions.Length)
        {
            repository.TryWriteString(configPath, TouchpadRightBindingKey, GamepadBindingCatalog.TouchpadOptions[s.TouchpadRightBindingIndex]);
        }

        repository.TryWriteString(configPath, TouchpadLeftCustomKeyKey, s.TouchpadLeftCustomKey);
        repository.TryWriteString(configPath, TouchpadRightCustomKeyKey, s.TouchpadRightCustomKey);
    }

    public static string NormalizeCustomKeyboardKey(string? key, string fallback)
    {
        if (GamepadBindingCatalog.TryResolveCustomKeyboardVirtualKey(key, out _, out var normalized))
        {
            return normalized;
        }

        return fallback;
    }

    private static int ResolveOptionIndex(string? value, int fallback)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return fallback;
        }

        var index = Array.IndexOf(GamepadBindingCatalog.Options, value);
        return index >= 0 ? index : fallback;
    }

    private static int ResolveTouchpadOptionIndex(string? value, int fallback)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return fallback;
        }

        var index = Array.IndexOf(GamepadBindingCatalog.TouchpadOptions, value);
        return index >= 0 ? index : fallback;
    }
}
