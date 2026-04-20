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

internal readonly struct SmartCoreAimAssistConfigState
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
    public readonly int AimBindingIndex;
    public readonly int FireBindingIndex;
    public readonly string[] AimSnapWeapons;
    public readonly string[] RapidFireWeapons;
    public readonly string[] ReleaseFireWeapons;

    public SmartCoreAimAssistConfigState(
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
        int aimBindingIndex,
        int fireBindingIndex,
        string[] aimSnapWeapons,
        string[] rapidFireWeapons,
        string[] releaseFireWeapons)
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
        AimBindingIndex = aimBindingIndex;
        FireBindingIndex = fireBindingIndex;
        AimSnapWeapons = aimSnapWeapons;
        RapidFireWeapons = rapidFireWeapons;
        ReleaseFireWeapons = releaseFireWeapons;
    }

    public static SmartCoreAimAssistConfigState Disabled => new(
        false,
        false,
        0,
        1,
        1,
        0f,
        0f,
        0f,
        0f,
        0f,
        0f,
        0,
        GamepadBindingCatalog.DefaultAimIndex,
        GamepadBindingCatalog.DefaultFireIndex,
        Array.Empty<string>(),
        Array.Empty<string>(),
        Array.Empty<string>());
}

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
    public readonly int AimBindingIndex;
    public readonly int FireBindingIndex;
    public readonly bool IsAimSnapOverrideWeapon;
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
        int aimBindingIndex,
        int fireBindingIndex,
        bool isAimSnapOverrideWeapon,
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
        AimBindingIndex = aimBindingIndex;
        FireBindingIndex = fireBindingIndex;
        IsAimSnapOverrideWeapon = isAimSnapOverrideWeapon;
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

    public SmartCoreDetectionState(OnnxDebugBox[] boxes)
    {
        Boxes = boxes;
    }

    public static SmartCoreDetectionState Empty => new(Array.Empty<OnnxDebugBox>());
}

internal sealed class SmartCoreActivationEvaluator
{
    private const int FireSnapModeIndex = 0;
    private const int AimAndFireSnapModeIndex = 1;

    public bool IsActive(in SmartCoreAimAssistContext context)
    {
        if (!context.IsEnabled || context.Boxes is null || context.Boxes.Length == 0)
        {
            return false;
        }

        if (context.IsAimSnapOverrideWeapon)
        {
            return GamepadBindingCatalog.IsPressed(context.FireBindingIndex, context.Input) ||
                   GamepadBindingCatalog.IsPressed(context.AimBindingIndex, context.Input);
        }

        return context.SnapModeIndex switch
        {
            FireSnapModeIndex => GamepadBindingCatalog.IsPressed(context.FireBindingIndex, context.Input),
            AimAndFireSnapModeIndex =>
                GamepadBindingCatalog.IsPressed(context.AimBindingIndex, context.Input) ||
                GamepadBindingCatalog.IsPressed(context.FireBindingIndex, context.Input),
            _ => false
        };
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

        float strength;
        if (distance <= innerRadiusModel)
        {
            var t = innerRadiusModel <= 0.001f ? 1f : Math.Clamp(distance / innerRadiusModel, 0f, 1f);
            var curveT = context.SnapInnerInterpolationTypeIndex == 1 ? t * t : t;
            strength = Lerp(startStrength, innerStrength, curveT);
        }
        else
        {
            strength = outerStrength;
        }

        if (!context.Input.LeftThumb)
        {
            strength *= Math.Clamp(context.SnapHipfireStrengthFactor, 0f, 1f);
        }

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
