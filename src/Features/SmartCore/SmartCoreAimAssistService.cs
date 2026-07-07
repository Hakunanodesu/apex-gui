internal sealed class SmartCoreAimAssistService
{
    private readonly SmartCoreActivationEvaluator _activationEvaluator = new();
    private readonly SmartCoreTargetSelector _targetSelector = new();
    private readonly SmartCoreStickMapper _stickMapper = new();

    public SmartCoreAimAssistResult Evaluate(in SmartCoreAimAssistContext context)
    {
        if (!_activationEvaluator.IsActive(context))
        {
            return SmartCoreAimAssistResult.Inactive;
        }

        if (!_targetSelector.TrySelectTarget(context, out var box))
        {
            return SmartCoreAimAssistResult.Inactive;
        }

        if (!_stickMapper.TryMap(context, box, out var rightX, out var rightY))
        {
            return SmartCoreAimAssistResult.Inactive;
        }

        return new SmartCoreAimAssistResult(true, rightX, rightY);
    }
}

internal readonly record struct AimAssistParams(
    bool IsEnabled,
    SnapMode SnapMode,
    int SnapOuterRange,
    int SnapInnerRange,
    float SnapOuterStrength,
    float SnapInnerStrength,
    float SnapStartStrength,
    float SnapVerticalStrengthFactor,
    float SnapHipfireStrengthFactor,
    float SnapHeight,
    float SnapStrengthRampTime,
    int SnapInnerInterpolationTypeIndex);

internal readonly record struct GamepadBindings(
    int AimBindingIndex,
    int FireBindingIndex,
    int VoiceBindingIndex,
    string VoiceCustomKey,
    int TouchpadLeftBindingIndex,
    int TouchpadRightBindingIndex,
    string TouchpadLeftCustomKey,
    string TouchpadRightCustomKey);

internal readonly record struct WeaponPolicy(
    RapidFireStrategy RapidFireStrategy,
    int RapidFireHz,
    string[] AimSnapWeapons,
    string[] RapidFireWeapons,
    string[] ReleaseFireWeapons);

internal readonly record struct SmartCoreAimAssistConfigState(
    AimAssistParams AimAssist,
    GamepadBindings Bindings,
    WeaponPolicy WeaponPolicy,
    MacroRuntimeState? Macro)
{
    public static SmartCoreAimAssistConfigState Disabled => new(
        new AimAssistParams(
            IsEnabled: false,
            SnapMode: SnapMode.Fire,
            SnapOuterRange: 1,
            SnapInnerRange: 1,
            SnapOuterStrength: 0f,
            SnapInnerStrength: 0f,
            SnapStartStrength: 0f,
            SnapVerticalStrengthFactor: 0f,
            SnapHipfireStrengthFactor: 0f,
            SnapHeight: 0f,
            SnapStrengthRampTime: 0f,
            SnapInnerInterpolationTypeIndex: 0),
        new GamepadBindings(
            AimBindingIndex: GamepadBindingCatalog.DefaultAimIndex,
            FireBindingIndex: GamepadBindingCatalog.DefaultFireIndex,
            VoiceBindingIndex: GamepadBindingCatalog.DefaultTouchpadLeftIndex,
            VoiceCustomKey: "V",
            TouchpadLeftBindingIndex: GamepadBindingCatalog.DefaultTouchpadLeftIndex,
            TouchpadRightBindingIndex: GamepadBindingCatalog.DefaultTouchpadRightIndex,
            TouchpadLeftCustomKey: GamepadBindingCatalog.DefaultCustomKeyboardKeyName,
            TouchpadRightCustomKey: GamepadBindingCatalog.DefaultCustomKeyboardKeyName),
        new WeaponPolicy(
            RapidFireStrategy: RapidFireStrategy.WeaponBased,
            RapidFireHz: 25,
            AimSnapWeapons: Array.Empty<string>(),
            RapidFireWeapons: Array.Empty<string>(),
            ReleaseFireWeapons: Array.Empty<string>()),
        null);
}

internal readonly struct SmartCoreAimAssistContext
{
    public readonly bool IsEnabled;
    public readonly SnapMode SnapMode;
    public readonly int SnapOuterRange;
    public readonly int SnapInnerRange;
    public readonly float SnapOuterStrength;
    public readonly float SnapInnerStrength;
    public readonly float SnapStartStrength;
    public readonly float SnapVerticalStrengthFactor;
    public readonly float SnapHipfireStrengthFactor;
    public readonly float SnapHeight;
    public readonly int SnapInnerInterpolationTypeIndex;
    public readonly int AimBindingIndex;
    public readonly int FireBindingIndex;
    public readonly bool IsAimSnapOverrideWeapon;
    public readonly float FireStrengthRampMultiplier;
    public readonly SdlGamepadInputSnapshot Input;
    public readonly OnnxDebugBox[] Boxes;

    public SmartCoreAimAssistContext(
        bool isEnabled,
        SnapMode snapMode,
        int snapOuterRange,
        int snapInnerRange,
        float snapOuterStrength,
        float snapInnerStrength,
        float snapStartStrength,
        float snapVerticalStrengthFactor,
        float snapHipfireStrengthFactor,
        float snapHeight,
        int snapInnerInterpolationTypeIndex,
        int aimBindingIndex,
        int fireBindingIndex,
        bool isAimSnapOverrideWeapon,
        float fireStrengthRampMultiplier,
        SdlGamepadInputSnapshot input,
        OnnxDebugBox[] boxes)
    {
        IsEnabled = isEnabled;
        SnapMode = snapMode;
        SnapOuterRange = snapOuterRange;
        SnapInnerRange = snapInnerRange;
        SnapOuterStrength = snapOuterStrength;
        SnapInnerStrength = snapInnerStrength;
        SnapStartStrength = snapStartStrength;
        SnapVerticalStrengthFactor = snapVerticalStrengthFactor;
        SnapHipfireStrengthFactor = snapHipfireStrengthFactor;
        SnapHeight = snapHeight;
        SnapInnerInterpolationTypeIndex = snapInnerInterpolationTypeIndex;
        AimBindingIndex = aimBindingIndex;
        FireBindingIndex = fireBindingIndex;
        IsAimSnapOverrideWeapon = isAimSnapOverrideWeapon;
        FireStrengthRampMultiplier = fireStrengthRampMultiplier;
        Input = input;
        Boxes = boxes;
    }
}

internal readonly struct SmartCoreAimAssistResult
{
    public readonly bool IsActive;
    public readonly short RightX;
    public readonly short RightY;

    public SmartCoreAimAssistResult(bool isActive, short rightX, short rightY)
    {
        IsActive = isActive;
        RightX = rightX;
        RightY = rightY;
    }

    public static SmartCoreAimAssistResult Inactive => new(false, 0, 0);
}

internal readonly struct SmartCoreDetectionState
{
    public readonly OnnxDebugBox[] Boxes;
    public readonly DateTime ReceivedAtUtc;

    public SmartCoreDetectionState(OnnxDebugBox[] boxes, DateTime receivedAtUtc)
    {
        Boxes = boxes;
        ReceivedAtUtc = receivedAtUtc;
    }

    public static SmartCoreDetectionState Empty => new(Array.Empty<OnnxDebugBox>(), DateTime.MinValue);
}

internal interface IAimAssistDetectionSink
{
    void SetAimAssistDetections(in SmartCoreDetectionState state);
}

internal sealed class SmartCoreActivationEvaluator
{
    public bool IsActive(in SmartCoreAimAssistContext context)
    {
        if (!context.IsEnabled || context.Boxes is null || context.Boxes.Length == 0)
        {
            return false;
        }

        var firePressed = GamepadBindingCatalog.IsPressed(context.FireBindingIndex, context.Input);
        var aimPressed = GamepadBindingCatalog.IsPressed(context.AimBindingIndex, context.Input);
        return SnapActivationPolicy.IsActive(context.IsAimSnapOverrideWeapon, context.SnapMode, firePressed, aimPressed);
    }
}

internal sealed class SmartCoreStickMapper
{
    public bool TryMap(in SmartCoreAimAssistContext context, in OnnxDebugBox box, out short rightX, out short rightY)
    {
        rightX = 0;
        rightY = 0;

        var snapHeight = Math.Clamp(context.SnapHeight, 0f, 1f);
        var targetY = box.Y + box.H * (0.5f - snapHeight);
        var centerX = box.InputWidth * 0.5f;
        var centerY = box.InputHeight * 0.5f;
        var dx = box.X - centerX;
        var dy = targetY - centerY;
        var distance = MathF.Sqrt(dx * dx + dy * dy);
        if (distance <= 0.001f)
        {
            return false;
        }

        var outerRange = Math.Max(1, context.SnapOuterRange);
        var innerRange = Math.Clamp(context.SnapInnerRange, 1, outerRange);
        var outerRadiusModel = MathF.Min(box.InputWidth, box.InputHeight) * 0.5f;
        var innerRadiusModel = outerRadiusModel * (innerRange / (float)outerRange);
        if (distance > outerRadiusModel)
        {
            return false;
        }

        var startStrength = Math.Clamp(context.SnapStartStrength, 0f, 1f);
        var innerStrength = Math.Clamp(context.SnapInnerStrength, 0f, 1f);
        var outerStrength = Math.Clamp(context.SnapOuterStrength, 0f, 1f);
        var verticalFactor = Math.Clamp(context.SnapVerticalStrengthFactor, 0f, 1f);

        var isAiming = GamepadBindingCatalog.IsPressed(context.AimBindingIndex, context.Input);
        float strength;
        if (isAiming)
        {
            if (distance <= innerRadiusModel)
            {
                var t = innerRadiusModel <= 0.001f ? 1f : Math.Clamp(distance / innerRadiusModel, 0f, 1f);
                var curveT = SnapInterpolation.EvaluateNormalized(t, context.SnapInnerInterpolationTypeIndex);
                strength = Lerp(startStrength, innerStrength, curveT);
            }
            else
            {
                strength = outerStrength;
            }
        }
        else
        {
            if (distance <= innerRadiusModel)
            {
                var t = innerRadiusModel <= 0.001f ? 1f : Math.Clamp(distance / innerRadiusModel, 0f, 1f);
                var curveT = SnapInterpolation.EvaluateNormalized(t, context.SnapInnerInterpolationTypeIndex);
                strength = Lerp(startStrength, innerStrength, curveT);
            }
            else
            {
                // Hipfire keeps inner-ring strength once outside inner range.
                strength = innerStrength;
            }

            strength *= Math.Clamp(context.SnapHipfireStrengthFactor, 0f, 1f);
        }

        strength *= Math.Clamp(context.FireStrengthRampMultiplier, 0f, 1f);

        if (strength <= 0f)
        {
            return false;
        }

        var invDistance = 1f / distance;
        var normalizedX = dx * invDistance;
        var normalizedY = dy * invDistance;
        var outputX = normalizedX * strength;
        var outputY = normalizedY * strength * verticalFactor;
        rightX = ToStickAxis(outputX);
        rightY = ToStickAxis(outputY);
        return rightX != 0 || rightY != 0;
    }

    private static float Lerp(float a, float b, float t)
    {
        return a + (b - a) * t;
    }

    private static short ToStickAxis(float normalized)
    {
        var scaled = normalized * short.MaxValue;
        return (short)Math.Clamp((int)MathF.Round(scaled), short.MinValue, short.MaxValue);
    }
}

internal sealed class SmartCoreTargetSelector
{
    public bool TrySelectTarget(in SmartCoreAimAssistContext context, out OnnxDebugBox box)
    {
        box = default;
        if (context.Boxes is null || context.Boxes.Length == 0)
        {
            return false;
        }

        var inputWidth = context.Boxes[0].InputWidth;
        var inputHeight = context.Boxes[0].InputHeight;
        if (inputWidth <= 0 || inputHeight <= 0)
        {
            return false;
        }

        var centerX = inputWidth * 0.5f;
        var centerY = inputHeight * 0.5f;
        var bestIndex = -1;
        var bestDistanceSquared = float.MaxValue;
        for (var i = 0; i < context.Boxes.Length; i++)
        {
            var candidate = context.Boxes[i];
            var dx = candidate.X - centerX;
            var dy = candidate.Y - centerY;
            var distanceSquared = dx * dx + dy * dy;
            if (distanceSquared >= bestDistanceSquared)
            {
                continue;
            }

            bestDistanceSquared = distanceSquared;
            bestIndex = i;
        }

        if (bestIndex < 0)
        {
            return false;
        }

        box = context.Boxes[bestIndex];
        return true;
    }
}
