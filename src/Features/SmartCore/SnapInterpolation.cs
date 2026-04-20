internal static class SnapInterpolation
{
    public const int LinearIndex = 0;
    public const int QuadraticEaseInIndex = 1;
    public const int QuadraticEaseOutIndex = 2;
    public const int QuadraticEaseInOutIndex = 3;

    public static float EvaluateNormalized(float t, int typeIndex)
    {
        t = Math.Clamp(t, 0f, 1f);
        return typeIndex switch
        {
            QuadraticEaseInIndex => t * t,
            QuadraticEaseOutIndex => 1f - (1f - t) * (1f - t),
            QuadraticEaseInOutIndex => t < 0.5f
                ? 2f * t * t
                : 1f - (MathF.Pow(-2f * t + 2f, 2f) * 0.5f),
            _ => t
        };
    }
}
