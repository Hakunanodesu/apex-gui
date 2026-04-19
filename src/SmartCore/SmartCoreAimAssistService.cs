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
