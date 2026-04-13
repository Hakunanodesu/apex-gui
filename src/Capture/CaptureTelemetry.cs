internal readonly struct CaptureTelemetry
{
    public readonly long PollCount;
    public readonly long SuccessCount;
    public readonly double TotalCaptureMs;
    public readonly double MaxCaptureMs;

    public CaptureTelemetry(long pollCount, long successCount, double totalCaptureMs, double maxCaptureMs)
    {
        PollCount = pollCount;
        SuccessCount = successCount;
        TotalCaptureMs = totalCaptureMs;
        MaxCaptureMs = maxCaptureMs;
    }
}
