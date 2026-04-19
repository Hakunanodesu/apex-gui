internal sealed class SmartCoreActivationEvaluator
{
    private const int FireSnapModeIndex = 0;
    private const int AimSnapModeIndex = 1;
    private const short TriggerPressedThreshold = short.MaxValue / 4;

    public bool IsActive(in SmartCoreAimAssistContext context)
    {
        if (!context.IsEnabled || !context.IsMappingActive || context.Boxes is null || context.Boxes.Length == 0)
        {
            return false;
        }

        return context.SnapModeIndex switch
        {
            FireSnapModeIndex => context.Input.RightTrigger >= TriggerPressedThreshold,
            AimSnapModeIndex => context.Input.LeftTrigger >= TriggerPressedThreshold,
            _ => false
        };
    }
}
