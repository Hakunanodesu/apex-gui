using Keys = OpenTK.Windowing.GraphicsLibraryFramework.Keys;

internal static class GamepadBindingCatalog
{
    private const short TriggerPressedThreshold = short.MaxValue / 4;
    private const ushort VkBackspace = 0x08;
    private const ushort VkTab = 0x09;
    private const ushort VkEnter = 0x0D;
    private const ushort VkPause = 0x13;
    private const ushort VkCapsLock = 0x14;
    private const ushort VkEscape = 0x1B;
    private const ushort VkSpace = 0x20;
    private const ushort VkPageUp = 0x21;
    private const ushort VkPageDown = 0x22;
    private const ushort VkEnd = 0x23;
    private const ushort VkHome = 0x24;
    private const ushort VkLeft = 0x25;
    private const ushort VkUp = 0x26;
    private const ushort VkRight = 0x27;
    private const ushort VkDown = 0x28;
    private const ushort VkInsert = 0x2D;
    private const ushort VkDelete = 0x2E;
    private const ushort Vk0 = 0x30;
    private const ushort VkA = 0x41;
    private const ushort VkF1 = 0x70;
    private const ushort VkNumLock = 0x90;
    private const ushort VkScrollLock = 0x91;
    private const ushort VkLeftShift = 0xA0;
    private const ushort VkRightShift = 0xA1;
    private const ushort VkLeftControl = 0xA2;
    private const ushort VkRightControl = 0xA3;
    private const ushort VkLeftAlt = 0xA4;
    private const ushort VkRightAlt = 0xA5;
    private const ushort VkSemicolon = 0xBA;
    private const ushort VkPlus = 0xBB;
    private const ushort VkComma = 0xBC;
    private const ushort VkMinus = 0xBD;
    private const ushort VkPeriod = 0xBE;
    private const ushort VkSlash = 0xBF;
    private const ushort VkTilde = 0xC0;
    private const ushort VkLeftBracket = 0xDB;
    private const ushort VkBackslash = 0xDC;
    private const ushort VkRightBracket = 0xDD;
    private const ushort VkQuote = 0xDE;
    private const ushort VkNumpad0 = 0x60;
    private const ushort VkNumpadMultiply = 0x6A;
    private const ushort VkNumpadAdd = 0x6B;
    private const ushort VkNumpadSubtract = 0x6D;
    private const ushort VkNumpadDecimal = 0x6E;
    private const ushort VkNumpadDivide = 0x6F;
    private const ushort VkPrintScreen = 0x2C;
    private const ushort VkLeftWindows = 0x5B;
    private const ushort VkRightWindows = 0x5C;
    private const ushort VkApps = 0x5D;

    public static readonly string[] Options =
    {
        "左扳机",
        "右扳机",
        "左肩键",
        "右肩键",
        "A",
        "B",
        "X",
        "Y",
        "左摇杆按下",
        "右摇杆按下",
        "十字键上",
        "十字键下",
        "十字键左",
        "十字键右",
        "Back",
        "Start"
    };

    public static readonly string[] TouchpadOptions =
        Options.Concat(new[] { KeyboardCustomBindingName }).ToArray();

    public const string KeyboardCustomBindingName = "自定义键盘按键";
    public const string DefaultCustomKeyboardKeyName = "=";

    private static readonly List<Keys> CapturableCustomKeyboardKeysValue = new();
    private static readonly Dictionary<Keys, string> OpenTkKeyToDisplayName = new();
    private static readonly Dictionary<string, ushort> DisplayNameToVirtualKey = new(StringComparer.OrdinalIgnoreCase);
    private static readonly Dictionary<string, string> DisplayNameNormalization = new(StringComparer.OrdinalIgnoreCase);
    private static readonly KeyBindingDefinition[] KeyBindingDefinitions = BuildKeyBindingDefinitions();

    public static int KeyboardCustomPseudoBindingIndex => Options.Length;
    public static IReadOnlyList<Keys> CapturableCustomKeyboardKeys => CapturableCustomKeyboardKeysValue;

    static GamepadBindingCatalog()
    {
        foreach (var definition in KeyBindingDefinitions)
        {
            DisplayNameToVirtualKey[definition.DisplayName] = definition.VirtualKey;
            DisplayNameNormalization[definition.DisplayName] = definition.DisplayName;
            foreach (var alias in definition.Aliases)
            {
                DisplayNameNormalization[alias] = definition.DisplayName;
            }

            foreach (var candidateName in definition.OpenTkKeyCandidates)
            {
                if (!Enum.TryParse(candidateName, ignoreCase: false, out Keys key) ||
                    OpenTkKeyToDisplayName.ContainsKey(key))
                {
                    continue;
                }

                OpenTkKeyToDisplayName[key] = definition.DisplayName;
                CapturableCustomKeyboardKeysValue.Add(key);
            }
        }

    }

    public static int DefaultAimIndex => ResolveIndex("左扳机", 0);

    public static int DefaultFireIndex => ResolveIndex("右扳机", 1);

    public static int DefaultTouchpadLeftIndex => ResolveIndex("Back", 14);

    public static int DefaultTouchpadRightIndex => ResolveIndex("Start", 15);

    public static bool IsKeyboardCustomBinding(int bindingIndex) => bindingIndex == KeyboardCustomPseudoBindingIndex;

    public static bool TryGetCustomKeyboardDisplayName(Keys key, out string displayName)
    {
        return OpenTkKeyToDisplayName.TryGetValue(key, out displayName!);
    }

    public static bool TryResolveCustomKeyboardVirtualKey(string? displayName, out ushort virtualKey, out string normalizedDisplayName)
    {
        if (string.IsNullOrWhiteSpace(displayName))
        {
            displayName = DefaultCustomKeyboardKeyName;
        }

        if (!DisplayNameNormalization.TryGetValue(displayName.Trim(), out normalizedDisplayName!))
        {
            virtualKey = 0;
            normalizedDisplayName = string.Empty;
            return false;
        }

        if (!DisplayNameToVirtualKey.TryGetValue(normalizedDisplayName, out virtualKey))
        {
            virtualKey = 0;
            normalizedDisplayName = string.Empty;
            return false;
        }

        return true;
    }

    public static int ResolveIndex(string? bindingName, int fallbackIndex)
    {
        if (string.IsNullOrWhiteSpace(bindingName))
        {
            return NormalizeIndex(fallbackIndex);
        }

        var index = Array.IndexOf(Options, bindingName);
        return index >= 0 ? index : NormalizeIndex(fallbackIndex);
    }

    public static bool IsPressed(int bindingIndex, in SdlGamepadInputSnapshot input)
    {
        return NormalizeIndex(bindingIndex) switch
        {
            0 => input.LeftTrigger >= TriggerPressedThreshold,
            1 => input.RightTrigger >= TriggerPressedThreshold,
            2 => input.LeftShoulder,
            3 => input.RightShoulder,
            4 => input.A,
            5 => input.B,
            6 => input.X,
            7 => input.Y,
            8 => input.LeftThumb,
            9 => input.RightThumb,
            10 => input.DpadUp,
            11 => input.DpadDown,
            12 => input.DpadLeft,
            13 => input.DpadRight,
            14 => input.Back,
            15 => input.Start,
            _ => false
        };
    }

    public static bool IsTriggerBinding(int bindingIndex)
    {
        var normalized = NormalizeIndex(bindingIndex);
        return normalized == 0 || normalized == 1;
    }

    public static short GetTriggerValue(int bindingIndex, in SdlGamepadInputSnapshot input)
    {
        return NormalizeIndex(bindingIndex) switch
        {
            0 => input.LeftTrigger,
            1 => input.RightTrigger,
            _ => (short)0
        };
    }

    public static void ApplyBinding(
        ref MappedGamepadState state,
        int bindingIndex,
        bool pressed,
        short analogValueWhenPressed = short.MaxValue)
    {
        switch (NormalizeIndex(bindingIndex))
        {
            case 0:
                state.LeftTrigger = pressed ? analogValueWhenPressed : (short)0;
                break;
            case 1:
                state.RightTrigger = pressed ? analogValueWhenPressed : (short)0;
                break;
            case 2:
                state.LeftShoulder = pressed;
                break;
            case 3:
                state.RightShoulder = pressed;
                break;
            case 4:
                state.A = pressed;
                break;
            case 5:
                state.B = pressed;
                break;
            case 6:
                state.X = pressed;
                break;
            case 7:
                state.Y = pressed;
                break;
            case 8:
                state.LeftThumb = pressed;
                break;
            case 9:
                state.RightThumb = pressed;
                break;
            case 10:
                state.DpadUp = pressed;
                break;
            case 11:
                state.DpadDown = pressed;
                break;
            case 12:
                state.DpadLeft = pressed;
                break;
            case 13:
                state.DpadRight = pressed;
                break;
            case 14:
                state.Back = pressed;
                break;
            case 15:
                state.Start = pressed;
                break;
        }
    }

    private static int NormalizeIndex(int index)
    {
        if (index < 0 || index >= Options.Length)
        {
            return 0;
        }

        return index;
    }

    private static KeyBindingDefinition[] BuildKeyBindingDefinitions()
    {
        var definitions = new List<KeyBindingDefinition>
        {
            new("=", VkPlus, new[] { "Equal", "KPEqual" }, "="),
            new("-", VkMinus, new[] { "Minus" }, "-"),
            new("[", VkLeftBracket, new[] { "LeftBracket" }, "["),
            new("]", VkRightBracket, new[] { "RightBracket" }, "]"),
            new("\\", VkBackslash, new[] { "Backslash" }, "\\"),
            new(";", VkSemicolon, new[] { "Semicolon" }, ";"),
            new("'", VkQuote, new[] { "Apostrophe", "Quote" }, "'"),
            new(",", VkComma, new[] { "Comma" }, ","),
            new(".", VkPeriod, new[] { "Period" }, "."),
            new("/", VkSlash, new[] { "Slash" }, "/"),
            new("`", VkTilde, new[] { "GraveAccent", "Grave" }, "`"),
            new("空格", VkSpace, new[] { "Space" }, "Space"),
            new("Tab", VkTab, new[] { "Tab" }),
            new("Enter", VkEnter, new[] { "Enter", "KeyPadEnter", "KPEnter" }),
            new("Esc", VkEscape, new[] { "Escape" }, "ESC"),
            new("Backspace", VkBackspace, new[] { "Backspace" }),
            new("Delete", VkDelete, new[] { "Delete" }),
            new("Insert", VkInsert, new[] { "Insert" }),
            new("Home", VkHome, new[] { "Home" }),
            new("End", VkEnd, new[] { "End" }),
            new("PageUp", VkPageUp, new[] { "PageUp" }),
            new("PageDown", VkPageDown, new[] { "PageDown" }),
            new("Up", VkUp, new[] { "Up" }),
            new("Down", VkDown, new[] { "Down" }),
            new("Left", VkLeft, new[] { "Left" }),
            new("Right", VkRight, new[] { "Right" }),
            new("CapsLock", VkCapsLock, new[] { "CapsLock" }),
            new("NumLock", VkNumLock, new[] { "NumLock" }),
            new("ScrollLock", VkScrollLock, new[] { "ScrollLock" }),
            new("Pause", VkPause, new[] { "Pause" }),
            new("PrintScreen", VkPrintScreen, new[] { "PrintScreen" }),
            new("左Shift", VkLeftShift, new[] { "LeftShift" }, "LShift"),
            new("右Shift", VkRightShift, new[] { "RightShift" }, "RShift"),
            new("左Ctrl", VkLeftControl, new[] { "LeftControl" }, "LCtrl"),
            new("右Ctrl", VkRightControl, new[] { "RightControl" }, "RCtrl"),
            new("左Alt", VkLeftAlt, new[] { "LeftAlt" }, "LAlt"),
            new("右Alt", VkRightAlt, new[] { "RightAlt" }, "RAlt", "AltGr"),
            new("左Win", VkLeftWindows, new[] { "LeftSuper", "LeftWin" }, "LWin"),
            new("右Win", VkRightWindows, new[] { "RightSuper", "RightWin" }, "RWin"),
            new("Menu", VkApps, new[] { "Menu" })
        };

        for (var i = 0; i <= 9; i++)
        {
            var digit = i.ToString();
            definitions.Add(new KeyBindingDefinition(
                digit,
                (ushort)(Vk0 + i),
                new[] { $"D{i}", $"Number{i}" },
                digit));
        }

        for (var i = 0; i < 26; i++)
        {
            var letter = ((char)('A' + i)).ToString();
            definitions.Add(new KeyBindingDefinition(
                letter,
                (ushort)(VkA + i),
                new[] { letter },
                letter.ToLowerInvariant()));
        }

        for (var i = 1; i <= 24; i++)
        {
            var keyName = $"F{i}";
            definitions.Add(new KeyBindingDefinition(
                keyName,
                (ushort)(VkF1 + i - 1),
                new[] { keyName }));
        }

        for (var i = 0; i <= 9; i++)
        {
            var keyName = $"Num{i}";
            definitions.Add(new KeyBindingDefinition(
                keyName,
                (ushort)(VkNumpad0 + i),
                new[] { $"KeyPad{i}", $"KP{i}" },
                $"Numpad{i}"));
        }

        definitions.Add(new KeyBindingDefinition("Num+", VkNumpadAdd, new[] { "KeyPadAdd", "KPAdd" }, "Numpad+"));
        definitions.Add(new KeyBindingDefinition("Num-", VkNumpadSubtract, new[] { "KeyPadSubtract", "KPSubtract" }, "Numpad-"));
        definitions.Add(new KeyBindingDefinition("Num*", VkNumpadMultiply, new[] { "KeyPadMultiply", "KPMultiply" }, "Numpad*"));
        definitions.Add(new KeyBindingDefinition("Num/", VkNumpadDivide, new[] { "KeyPadDivide", "KPDivide" }, "Numpad/"));
        definitions.Add(new KeyBindingDefinition("Num.", VkNumpadDecimal, new[] { "KeyPadDecimal", "KPDecimal" }, "Numpad."));
        return definitions.ToArray();
    }

    private readonly record struct KeyBindingDefinition(
        string DisplayName,
        ushort VirtualKey,
        string[] OpenTkKeyCandidates,
        params string[] Aliases);
}

internal struct MappedGamepadState
{
    public short LeftTrigger;
    public short RightTrigger;
    public bool LeftShoulder;
    public bool RightShoulder;
    public bool A;
    public bool B;
    public bool X;
    public bool Y;
    public bool LeftThumb;
    public bool RightThumb;
    public bool DpadUp;
    public bool DpadDown;
    public bool DpadLeft;
    public bool DpadRight;
    public bool Back;
    public bool Start;
    public bool Guide;

    public MappedGamepadState(in SdlGamepadInputSnapshot input)
    {
        LeftTrigger = input.LeftTrigger;
        RightTrigger = input.RightTrigger;
        LeftShoulder = input.LeftShoulder;
        RightShoulder = input.RightShoulder;
        A = input.A;
        B = input.B;
        X = input.X;
        Y = input.Y;
        LeftThumb = input.LeftThumb;
        RightThumb = input.RightThumb;
        DpadUp = input.DpadUp;
        DpadDown = input.DpadDown;
        DpadLeft = input.DpadLeft;
        DpadRight = input.DpadRight;
        Back = input.Back;
        Start = input.Start;
        Guide = input.Guide;
    }
}
