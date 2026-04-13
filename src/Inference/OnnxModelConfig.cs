internal readonly struct OnnxModelConfig
{
    public readonly string DisplayName;
    public readonly string JsonPath;
    public readonly string OnnxPath;
    public readonly int InputWidth;
    public readonly int InputHeight;
    public readonly float ConfThreshold;
    public readonly float IouThreshold;
    public readonly string ClassesRaw;
    public readonly HashSet<int> AllowedClasses;

    public OnnxModelConfig(
        string displayName,
        string jsonPath,
        string onnxPath,
        int inputWidth,
        int inputHeight,
        float confThreshold,
        float iouThreshold,
        string classesRaw,
        HashSet<int> allowedClasses)
    {
        DisplayName = displayName;
        JsonPath = jsonPath;
        OnnxPath = onnxPath;
        InputWidth = inputWidth;
        InputHeight = inputHeight;
        ConfThreshold = confThreshold;
        IouThreshold = iouThreshold;
        ClassesRaw = classesRaw;
        AllowedClasses = allowedClasses;
    }
}
