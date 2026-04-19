using System.Diagnostics;
using System.Threading;
using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;

internal readonly struct OnnxInferenceSnapshot
{
    public readonly string Status;
    public readonly double InferenceFps;
    public readonly double AvgInferenceMs;
    public readonly double P95InferenceMs;
    public readonly double P99InferenceMs;
    public readonly int DetectionCount;
    public readonly string OutputSummary;
    public readonly OnnxDebugProbe Probe;

    public OnnxInferenceSnapshot(
        string status,
        double inferenceFps,
        double avgInferenceMs,
        double p95InferenceMs,
        double p99InferenceMs,
        int detectionCount,
        string outputSummary)
        : this(status, inferenceFps, avgInferenceMs, p95InferenceMs, p99InferenceMs, detectionCount, outputSummary, default)
    {
    }

    public OnnxInferenceSnapshot(
        string status,
        double inferenceFps,
        double avgInferenceMs,
        double p95InferenceMs,
        double p99InferenceMs,
        int detectionCount,
        string outputSummary,
        OnnxDebugProbe probe)
    {
        Status = status;
        InferenceFps = inferenceFps;
        AvgInferenceMs = avgInferenceMs;
        P95InferenceMs = p95InferenceMs;
        P99InferenceMs = p99InferenceMs;
        DetectionCount = detectionCount;
        OutputSummary = outputSummary;
        Probe = probe;
    }
}

internal readonly struct OnnxDebugProbe
{
    public readonly bool HasValue;
    public readonly int InputWidth;
    public readonly int InputHeight;
    public readonly float Raw0;
    public readonly float Raw1;
    public readonly float Raw2;
    public readonly float Raw3;
    public readonly float Objectness;
    public readonly float ClassScore;
    public readonly float Score;

    public OnnxDebugProbe(
        bool hasValue,
        int inputWidth,
        int inputHeight,
        float raw0,
        float raw1,
        float raw2,
        float raw3,
        float objectness,
        float classScore,
        float score)
    {
        HasValue = hasValue;
        InputWidth = inputWidth;
        InputHeight = inputHeight;
        Raw0 = raw0;
        Raw1 = raw1;
        Raw2 = raw2;
        Raw3 = raw3;
        Objectness = objectness;
        ClassScore = classScore;
        Score = score;
    }
}

internal readonly struct OnnxDebugBox
{
    public readonly int InputWidth;
    public readonly int InputHeight;
    public readonly float X;
    public readonly float Y;
    public readonly float W;
    public readonly float H;
    public readonly float Score;

    public OnnxDebugBox(int inputWidth, int inputHeight, float x, float y, float w, float h, float score)
    {
        InputWidth = inputWidth;
        InputHeight = inputHeight;
        X = x;
        Y = y;
        W = w;
        H = h;
        Score = score;
    }
}

internal sealed class OnnxDmlWorker : IDisposable
{
    private readonly object _sync = new();
    private readonly Thread _thread;
    private readonly AutoResetEvent _frameArrived = new(false);
    private readonly OnnxModelConfig _model;

    private bool _running = true;
    private byte[] _latestFrame = Array.Empty<byte>();
    private int _latestFrameWidth;
    private int _latestFrameHeight;
    private int _latestFrameId;
    private int _lastProcessedFrameId;
    private OnnxDebugProbe _latestProbe;
    private OnnxDebugBox[] _latestBoxes = Array.Empty<OnnxDebugBox>();

    private readonly List<double> _windowSamples = new(256);
    private DateTime _windowStartUtc = DateTime.UtcNow;
    private long _windowInferenceCount;

    private OnnxInferenceSnapshot _snapshot = new(
        "未启动",
        0.0,
        0.0,
        0.0,
        0.0,
        0,
        "无");

    public OnnxDmlWorker(OnnxModelConfig model)
    {
        _model = model;
        _thread = new Thread(WorkerMain)
        {
            IsBackground = true,
            Name = "ONNX-DML-Worker"
        };
        _thread.Start();
    }

    public void SubmitFrame(byte[] frameData, int width, int height, int frameId)
    {
        lock (_sync)
        {
            if (!_running)
            {
                return;
            }

            if (_latestFrame.Length != frameData.Length)
            {
                _latestFrame = new byte[frameData.Length];
            }

            System.Buffer.BlockCopy(frameData, 0, _latestFrame, 0, frameData.Length);
            _latestFrameWidth = width;
            _latestFrameHeight = height;
            _latestFrameId = frameId;
        }

        _frameArrived.Set();
    }

    public OnnxInferenceSnapshot GetSnapshot()
    {
        lock (_sync)
        {
            return _snapshot;
        }
    }

    public OnnxDebugProbe GetDebugProbe()
    {
        lock (_sync)
        {
            return _latestProbe;
        }
    }

    public OnnxDebugBox[] GetDebugBoxes()
    {
        lock (_sync)
        {
            var copy = new OnnxDebugBox[_latestBoxes.Length];
            Array.Copy(_latestBoxes, copy, _latestBoxes.Length);
            return copy;
        }
    }

    private void WorkerMain()
    {
        try
        {
            using var options = new SessionOptions();
            options.AppendExecutionProvider_DML();
            using var session = new InferenceSession(_model.OnnxPath, options);

            var input = session.InputMetadata.First();
            var inputName = input.Key;
            var inputDims = ResolveInputShape(input.Value.Dimensions, _model.InputHeight, _model.InputWidth);
            var layout = DetectLayout(inputDims);

            SetStatus("推理中");
            while (_running)
            {
                _frameArrived.WaitOne(50);
                if (!_running)
                {
                    break;
                }

                byte[] frame;
                int frameWidth;
                int frameHeight;
                int frameId;
                lock (_sync)
                {
                    if (_latestFrameId == 0 || _latestFrameId == _lastProcessedFrameId)
                    {
                        continue;
                    }

                    frame = new byte[_latestFrame.Length];
                    System.Buffer.BlockCopy(_latestFrame, 0, frame, 0, _latestFrame.Length);
                    frameWidth = _latestFrameWidth;
                    frameHeight = _latestFrameHeight;
                    frameId = _latestFrameId;
                    _lastProcessedFrameId = frameId;
                }

                var sw = Stopwatch.StartNew();
                var inputData = Preprocess(frame, frameWidth, frameHeight, _model.InputWidth, _model.InputHeight, layout);
                var inputTensor = new DenseTensor<float>(inputData, inputDims);
                using var outputs = session.Run(new[] { NamedOnnxValue.CreateFromTensor(inputName, inputTensor) });
                sw.Stop();

                var outputSummary = BuildOutputSummary(outputs);
                var detectionCount = CountDetections(
                    outputs,
                    _model.InputWidth,
                    _model.InputHeight,
                    _model.ConfThreshold,
                    _model.IouThreshold,
                    _model.AllowedClasses,
                    out var probe,
                    out var boxes);
                lock (_sync)
                {
                    _latestProbe = probe;
                    _latestBoxes = boxes;
                }

                PushInferenceSample(sw.Elapsed.TotalMilliseconds, detectionCount, outputSummary);
            }
        }
        catch (Exception ex)
        {
            SetStatus($"推理失败: {ex.GetType().Name}: {ex.Message}");
            _running = false;
        }
    }

    private void SetStatus(string status)
    {
        lock (_sync)
        {
            _snapshot = new OnnxInferenceSnapshot(
                status,
                _snapshot.InferenceFps,
                _snapshot.AvgInferenceMs,
                _snapshot.P95InferenceMs,
                _snapshot.P99InferenceMs,
                _snapshot.DetectionCount,
                _snapshot.OutputSummary);
        }
    }

    private void PushInferenceSample(double ms, int detectionCount, string outputSummary)
    {
        lock (_sync)
        {
            _windowSamples.Add(ms);
            _windowInferenceCount++;
            var elapsed = DateTime.UtcNow - _windowStartUtc;
            if (elapsed.TotalSeconds >= 1.0)
            {
                var fps = _windowInferenceCount / elapsed.TotalSeconds;
                var avg = _windowSamples.Count > 0 ? _windowSamples.Average() : 0.0;
                var p95 = Percentile(_windowSamples, 0.95);
                var p99 = Percentile(_windowSamples, 0.99);
                _snapshot = new OnnxInferenceSnapshot("推理中", fps, avg, p95, p99, detectionCount, outputSummary);
                _windowSamples.Clear();
                _windowInferenceCount = 0;
                _windowStartUtc = DateTime.UtcNow;
            }
            else
            {
                _snapshot = new OnnxInferenceSnapshot(
                    _snapshot.Status,
                    _snapshot.InferenceFps,
                    _snapshot.AvgInferenceMs,
                    _snapshot.P95InferenceMs,
                    _snapshot.P99InferenceMs,
                    detectionCount,
                    outputSummary);
            }
        }
    }

    private static int[] ResolveInputShape(IReadOnlyList<int> dims, int height, int width)
    {
        var resolved = dims.Select(d => d <= 0 ? 1 : d).ToArray();
        if (resolved.Length != 4)
        {
            return new[] { 1, 3, height, width };
        }

        if (resolved[1] == 3 || resolved[1] == 1)
        {
            resolved[0] = 1;
            resolved[2] = height;
            resolved[3] = width;
            return resolved;
        }

        resolved[0] = 1;
        resolved[1] = height;
        resolved[2] = width;
        resolved[3] = 3;
        return resolved;
    }

    private static string DetectLayout(int[] dims)
    {
        if (dims.Length == 4 && dims[1] is 1 or 3)
        {
            return "NCHW";
        }

        return "NHWC";
    }

    private static float[] Preprocess(byte[] bgra, int srcW, int srcH, int dstW, int dstH, string layout)
    {
        var data = new float[dstW * dstH * 3];
        for (var y = 0; y < dstH; y++)
        {
            var sy = y * srcH / dstH;
            for (var x = 0; x < dstW; x++)
            {
                var sx = x * srcW / dstW;
                var srcIndex = (sy * srcW + sx) * 4;
                var b = bgra[srcIndex + 0] / 255f;
                var g = bgra[srcIndex + 1] / 255f;
                var r = bgra[srcIndex + 2] / 255f;

                if (layout == "NCHW")
                {
                    var pixel = y * dstW + x;
                    data[pixel] = r;
                    data[dstW * dstH + pixel] = g;
                    data[2 * dstW * dstH + pixel] = b;
                }
                else
                {
                    var pixel = (y * dstW + x) * 3;
                    data[pixel + 0] = r;
                    data[pixel + 1] = g;
                    data[pixel + 2] = b;
                }
            }
        }

        return data;
    }

    private static string BuildOutputSummary(IDisposableReadOnlyCollection<DisposableNamedOnnxValue> outputs)
    {
        var parts = new List<string>();
        foreach (var output in outputs.Take(3))
        {
            try
            {
                var tensor = output.AsTensor<float>();
                var shape = string.Join("x", tensor.Dimensions.ToArray().Select(d => d.ToString()));
                parts.Add($"{output.Name}:{shape}");
            }
            catch
            {
                parts.Add($"{output.Name}:non-float");
            }
        }

        return parts.Count == 0 ? "无输出" : string.Join(", ", parts);
    }

    private static int CountDetections(
        IDisposableReadOnlyCollection<DisposableNamedOnnxValue> outputs,
        int inputWidth,
        int inputHeight,
        float confThres,
        float iouThres,
        HashSet<int> allowedClasses,
        out OnnxDebugProbe probe,
        out OnnxDebugBox[] boxes)
    {
        probe = default;
        boxes = Array.Empty<OnnxDebugBox>();
        foreach (var output in outputs)
        {
            Tensor<float>? tensor;
            try
            {
                tensor = output.AsTensor<float>();
            }
            catch
            {
                continue;
            }

            if (tensor.Rank != 3)
            {
                continue;
            }

            var dims = tensor.Dimensions.ToArray();
            var values = tensor.ToArray();
            if (TryParseDetections(values, dims, inputWidth, inputHeight, confThres, iouThres, allowedClasses, out var count, out probe, out boxes))
            {
                return count;
            }
        }

        return 0;
    }

    private static bool TryParseDetections(
        float[] values,
        int[] dims,
        int inputWidth,
        int inputHeight,
        float confThres,
        float iouThres,
        HashSet<int> allowedClasses,
        out int count,
        out OnnxDebugProbe probe,
        out OnnxDebugBox[] boxes)
    {
        count = 0;
        probe = default;
        boxes = Array.Empty<OnnxDebugBox>();
        if (dims.Length != 3)
        {
            return false;
        }

        int rows;
        int cols;
        bool transposed;
        if (dims[2] >= 6)
        {
            rows = dims[1];
            cols = dims[2];
            transposed = false;
        }
        else if (dims[1] >= 6)
        {
            rows = dims[2];
            cols = dims[1];
            transposed = true;
        }
        else
        {
            return false;
        }

        var candidateBoxes = new List<(float x, float y, float w, float h, float score, float obj, float classScore)>();
        for (var i = 0; i < rows; i++)
        {
            int baseIndex;
            if (!transposed)
            {
                baseIndex = i * cols;
            }
            else
            {
                baseIndex = i;
            }

            float Read(int c) => !transposed ? values[baseIndex + c] : values[c * rows + baseIndex];

            var obj = Read(4);
            if (obj <= 0f)
            {
                continue;
            }

            var bestClassScore = 0f;
            var bestClass = -1;
            for (var c = 5; c < cols; c++)
            {
                var cls = Read(c);
                if (cls > bestClassScore)
                {
                    bestClassScore = cls;
                    bestClass = c - 5;
                }
            }

            if (bestClass >= 0 && allowedClasses.Count > 0 && !allowedClasses.Contains(bestClass))
            {
                continue;
            }

            var score = obj * bestClassScore;
            if (score < confThres)
            {
                continue;
            }

            candidateBoxes.Add((Read(0), Read(1), Math.Abs(Read(2)), Math.Abs(Read(3)), score, obj, bestClassScore));
        }

        if (candidateBoxes.Count == 0)
        {
            return true;
        }

        candidateBoxes.Sort((a, b) => b.score.CompareTo(a.score));
        var kept = new List<(float x, float y, float w, float h, float score, float obj, float classScore)>();
        foreach (var box in candidateBoxes)
        {
            var suppressed = false;
            foreach (var keptBox in kept)
            {
                if (ComputeIoU(
                        (box.x, box.y, box.w, box.h, box.score),
                        (keptBox.x, keptBox.y, keptBox.w, keptBox.h, keptBox.score)) > iouThres)
                {
                    suppressed = true;
                    break;
                }
            }

            if (!suppressed)
            {
                kept.Add(box);
            }
        }

        count = kept.Count;
        var primary = kept[0];
        probe = new OnnxDebugProbe(
            true,
            inputWidth,
            inputHeight,
            primary.x,
            primary.y,
            primary.w,
            primary.h,
            primary.obj,
            primary.classScore,
            primary.score);
        boxes = kept.Select(box => new OnnxDebugBox(inputWidth, inputHeight, box.x, box.y, box.w, box.h, box.score)).ToArray();
        return true;
    }

    private static float ComputeIoU(
        (float x, float y, float w, float h, float score) a,
        (float x, float y, float w, float h, float score) b)
    {
        var ax1 = a.x - a.w * 0.5f;
        var ay1 = a.y - a.h * 0.5f;
        var ax2 = a.x + a.w * 0.5f;
        var ay2 = a.y + a.h * 0.5f;

        var bx1 = b.x - b.w * 0.5f;
        var by1 = b.y - b.h * 0.5f;
        var bx2 = b.x + b.w * 0.5f;
        var by2 = b.y + b.h * 0.5f;

        var interX1 = Math.Max(ax1, bx1);
        var interY1 = Math.Max(ay1, by1);
        var interX2 = Math.Min(ax2, bx2);
        var interY2 = Math.Min(ay2, by2);
        var interW = Math.Max(0f, interX2 - interX1);
        var interH = Math.Max(0f, interY2 - interY1);
        var interArea = interW * interH;
        var union = a.w * a.h + b.w * b.h - interArea;
        if (union <= 0f)
        {
            return 0f;
        }

        return interArea / union;
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

    public void Dispose()
    {
        _running = false;
        _frameArrived.Set();
        if (_thread.IsAlive)
        {
            _thread.Join(500);
        }
        _frameArrived.Dispose();
    }
}
