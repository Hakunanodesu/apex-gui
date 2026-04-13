internal sealed class RealtimePerfStats
{
    private double _windowSeconds;

    private long _capturePollCount;
    private long _captureSuccessCount;
    private double _captureMsSum;
    private readonly List<double> _captureMsSamples = new(2000);
    private CaptureTelemetry _lastTelemetry;
    private bool _hasLastTelemetry;

    public void Reset()
    {
        _windowSeconds = 0;
        _capturePollCount = 0;
        _captureSuccessCount = 0;
        _captureMsSum = 0;
        _captureMsSamples.Clear();
        _lastTelemetry = default;
        _hasLastTelemetry = false;
    }

    public void PushSample(float deltaTimeSeconds, CaptureTelemetry telemetry, List<double> captureSamples)
    {
        var clampedDelta = Math.Max(deltaTimeSeconds, 1f / 1000f);
        _windowSeconds += clampedDelta;
        for (var i = 0; i < captureSamples.Count; i++)
        {
            _captureMsSamples.Add(captureSamples[i]);
        }

        if (!_hasLastTelemetry)
        {
            _lastTelemetry = telemetry;
            _hasLastTelemetry = true;
            return;
        }

        var pollDelta = telemetry.PollCount - _lastTelemetry.PollCount;
        var successDelta = telemetry.SuccessCount - _lastTelemetry.SuccessCount;
        var captureMsDelta = telemetry.TotalCaptureMs - _lastTelemetry.TotalCaptureMs;
        if (pollDelta > 0)
        {
            _capturePollCount += pollDelta;
            _captureSuccessCount += Math.Max(0, successDelta);
            _captureMsSum += Math.Max(0.0, captureMsDelta);
        }

        _lastTelemetry = telemetry;
    }

    public bool TryBuildSnapshot(out PerfSnapshot snapshot)
    {
        if (_windowSeconds < 1.0)
        {
            snapshot = default;
            return false;
        }

        var capturePollHz = _capturePollCount / _windowSeconds;
        var capturedFps = _captureSuccessCount / _windowSeconds;
        var avgCaptureMs = _captureSuccessCount > 0 ? _captureMsSum / _captureSuccessCount : 0.0;
        var p95CaptureMs = Percentile(_captureMsSamples, 0.95);
        var p99CaptureMs = Percentile(_captureMsSamples, 0.99);
        var successRate = _capturePollCount > 0
            ? (double)_captureSuccessCount / _capturePollCount * 100.0
            : 0.0;

        snapshot = new PerfSnapshot(
            capturePollHz,
            capturedFps,
            avgCaptureMs,
            p95CaptureMs,
            p99CaptureMs,
            successRate);

        Reset();
        return true;
    }

    private static double Percentile(List<double> values, double percentile)
    {
        if (values.Count == 0)
        {
            return 0.0;
        }

        values.Sort();
        var rank = percentile * (values.Count - 1);
        var low = (int)Math.Floor(rank);
        var high = (int)Math.Ceiling(rank);
        if (low == high)
        {
            return values[low];
        }

        var weight = rank - low;
        return values[low] * (1.0 - weight) + values[high] * weight;
    }
}
