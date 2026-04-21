public sealed partial class MainWindow
{
    private readonly record struct VisionPipelineConfig(string ModelPath, int CaptureWidth, int CaptureHeight);

    private DesktopCaptureWorker? _dxgiWorker;
    private OnnxWorker? _onnxWorker;
    private WeaponRecognitionWorker? _weaponRecWorker;
    private VisionPipelineConfig? _currentVisionConfig;

    private void SyncSmartCoreVisionPipeline()
    {
        var targetConfig = BuildTargetVisionConfig();
        if (_currentVisionConfig == targetConfig)
        {
            return;
        }

        ApplyVisionPipelineState(targetConfig);
    }

    private VisionPipelineConfig? BuildTargetVisionConfig()
    {
        if (!_smartCoreMappingState.IsEnabled)
        {
            return null;
        }

        if (!TryGetHomeSelectedModel(out var model))
        {
            return null;
        }

        var captureSize = Math.Max(1, _homeViewState.SnapOuterRange);
        return new VisionPipelineConfig(model.OnnxPath, captureSize, captureSize);
    }

    private void ApplyVisionPipelineState(VisionPipelineConfig? targetConfig)
    {
        if (!targetConfig.HasValue)
        {
            StopVisionPipeline();
            return;
        }

        if (!TryGetHomeSelectedModel(out var model))
        {
            StopVisionPipeline();
            return;
        }

        StartVisionPipeline(model, targetConfig.Value);
    }

    private bool TryGetHomeSelectedModel(out OnnxModelConfig model)
    {
        if (_onnxTopSelectedModelIndex >= 0 && _onnxTopSelectedModelIndex < _onnxModels.Count)
        {
            model = _onnxModels[_onnxTopSelectedModelIndex];
            return true;
        }

        model = default;
        return false;
    }

    private void StartVisionPipeline(OnnxModelConfig model, VisionPipelineConfig config)
    {
        try
        {
            StopVisionPipeline();
            _dxgiWorker = new DesktopCaptureWorker();
            _dxgiWorker.SetCaptureRegion(config.CaptureWidth, config.CaptureHeight);
            _dxgiWorker.SetPreviewFrameCacheEnabled(IsSmartCorePreviewWindowOpen());
            _onnxWorker = new OnnxWorker(model);
            _onnxWorker.SetDetectionConsumer(_viGEmMappingWorker);
            _weaponRecWorker = new WeaponRecognitionWorker(_dxgiWorker);
            _weaponRecWorker.SetConsumer(_viGEmMappingWorker);
            _dxgiWorker.SetFrameConsumer(_onnxWorker);
            _currentVisionConfig = config;
        }
        catch
        {
            StopVisionPipeline();
        }
    }

    private void StopVisionPipeline()
    {
        _dxgiWorker?.SetPreviewFrameCacheEnabled(false);
        _dxgiWorker?.SetFrameConsumer(null);
        _onnxWorker?.Dispose();
        _onnxWorker = null;
        _weaponRecWorker?.Dispose();
        _weaponRecWorker = null;
        _dxgiWorker?.Dispose();
        _dxgiWorker = null;
        _currentVisionConfig = null;
        _viGEmMappingWorker?.SetAimAssistDetections(SmartCoreDetectionState.Empty);
        _viGEmMappingWorker?.SetWeaponRecognition(WeaponRecognitionResultState.Empty);
    }
}

