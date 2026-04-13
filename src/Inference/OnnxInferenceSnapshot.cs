internal readonly struct OnnxInferenceSnapshot
{
    public readonly string Status;
    public readonly double InferenceFps;
    public readonly double AvgInferenceMs;
    public readonly double P95InferenceMs;
    public readonly double P99InferenceMs;
    public readonly int DetectionCount;
    public readonly string OutputSummary;

    public OnnxInferenceSnapshot(
        string status,
        double inferenceFps,
        double avgInferenceMs,
        double p95InferenceMs,
        double p99InferenceMs,
        int detectionCount,
        string outputSummary)
    {
        Status = status;
        InferenceFps = inferenceFps;
        AvgInferenceMs = avgInferenceMs;
        P95InferenceMs = p95InferenceMs;
        P99InferenceMs = p99InferenceMs;
        DetectionCount = detectionCount;
        OutputSummary = outputSummary;
    }
}
