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
