internal static class GamepadBindingCatalog
{
    private const short TriggerPressedThreshold = short.MaxValue / 4;

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

    public const string KeyboardEqualsBindingName = "键盘 =";

    public static int KeyboardEqualsPseudoBindingIndex => Options.Length;

    public static int DefaultAimIndex => ResolveIndex("左扳机", 0);

    public static int DefaultFireIndex => ResolveIndex("右扳机", 1);

    public static int DefaultTouchpadLeftIndex => ResolveIndex("Back", 14);

    public static int DefaultTouchpadRightIndex => ResolveIndex("Start", 15);

    public static bool IsKeyboardEqualsBinding(int bindingIndex) => bindingIndex == KeyboardEqualsPseudoBindingIndex;

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

    private static int NormalizeIndex(int index)
    {
        if (index < 0 || index >= Options.Length)
        {
            return 0;
        }

        return index;
    }
}
