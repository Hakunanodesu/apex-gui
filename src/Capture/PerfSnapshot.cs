internal readonly struct PerfSnapshot
{
    public readonly double CapturePollHz;
    public readonly double CapturedFps;
    public readonly double AvgCaptureMs;
    public readonly double P95CaptureMs;
    public readonly double P99CaptureMs;
    public readonly double CaptureSuccessRate;

    public PerfSnapshot(
        double capturePollHz,
        double capturedFps,
        double avgCaptureMs,
        double p95CaptureMs,
        double p99CaptureMs,
        double captureSuccessRate)
    {
        CapturePollHz = capturePollHz;
        CapturedFps = capturedFps;
        AvgCaptureMs = avgCaptureMs;
        P95CaptureMs = p95CaptureMs;
        P99CaptureMs = p99CaptureMs;
        CaptureSuccessRate = captureSuccessRate;
    }
}
