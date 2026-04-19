public sealed partial class MainWindow
{
    private void UpdateSmartCoreAimAssist()
    {
        if (_viGEmMappingWorker is null)
        {
            return;
        }

        if (_sdlGamepadWorker is null || !_sdlGamepadWorker.TryGetLatestInput(out var input, out _))
        {
            _viGEmMappingWorker.SetAimAssistOverride(0, 0, enabled: false);
            return;
        }

        var boxes = _onnxWorker?.GetDebugBoxes() ?? Array.Empty<OnnxDebugBox>();
        var context = new SmartCoreAimAssistContext(
            _smartCoreMappingState.IsEnabled,
            _smartCoreMappingState.IsMappingActive,
            _homeViewState.SnapModeIndex,
            _homeViewState.SnapOuterRange,
            _homeViewState.SnapInnerRange,
            _homeViewState.SnapOuterStrength,
            _homeViewState.SnapInnerStrength,
            _homeViewState.SnapStartStrength,
            _homeViewState.SnapVerticalStrengthFactor,
            _homeViewState.SnapHipfireStrengthFactor,
            _homeViewState.SnapHeight,
            _homeViewState.SnapInnerInterpolationTypeIndex,
            input,
            boxes);

        var result = _smartCoreAimAssistService.Evaluate(context);
        _viGEmMappingWorker.SetAimAssistOverride(result.RightX, result.RightY, result.IsActive);
    }
}
