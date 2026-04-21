using System.Text.Json;

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

internal static class OnnxModelConfigLoader
{
    public static List<OnnxModelConfig> LoadFromDirectory(string directory)
    {
        var result = new List<OnnxModelConfig>();
        if (!Directory.Exists(directory))
        {
            return result;
        }

        foreach (var jsonPath in Directory.GetFiles(directory, "*.json", SearchOption.TopDirectoryOnly))
        {
            if (TryLoadSingle(jsonPath, out var model))
            {
                result.Add(model);
            }
        }

        return result;
    }

    private static bool TryLoadSingle(string jsonPath, out OnnxModelConfig model)
    {
        model = default;
        try
        {
            var onnxPath = Path.ChangeExtension(jsonPath, ".onnx");
            if (!File.Exists(onnxPath))
            {
                return false;
            }

            using var doc = JsonDocument.Parse(File.ReadAllText(jsonPath));
            var root = doc.RootElement;
            if (!root.TryGetProperty("size", out var sizeEl))
            {
                return false;
            }

            var size = sizeEl.GetInt32();
            if (size <= 0)
            {
                return false;
            }

            var conf = root.TryGetProperty("conf_thres", out var confEl) ? confEl.GetSingle() : 0.25f;
            var iou = root.TryGetProperty("iou_thres", out var iouEl) ? iouEl.GetSingle() : 0.45f;
            var classesRaw = root.TryGetProperty("classes", out var classesEl) ? classesEl.ToString() : string.Empty;
            var allowed = ParseClasses(classesRaw);
            model = new OnnxModelConfig(
                Path.GetFileNameWithoutExtension(jsonPath),
                jsonPath,
                onnxPath,
                size,
                size,
                conf,
                iou,
                classesRaw,
                allowed);
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static HashSet<int> ParseClasses(string raw)
    {
        var set = new HashSet<int>();
        if (string.IsNullOrWhiteSpace(raw))
        {
            return set;
        }

        var parts = raw.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        foreach (var part in parts)
        {
            if (int.TryParse(part, out var value))
            {
                set.Add(value);
            }
        }

        return set;
    }
}
