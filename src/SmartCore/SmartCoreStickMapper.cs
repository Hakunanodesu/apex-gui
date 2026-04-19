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
