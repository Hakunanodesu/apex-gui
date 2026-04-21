using System.Diagnostics;
using System.Threading;

internal readonly record struct WeaponRecognitionResultState(
    string WeaponName,
    float Similarity)
{
    public static WeaponRecognitionResultState Empty =>
        new(WeaponTemplateCatalog.EmptyHandName, 0f);
}

internal sealed class WeaponRecognitionWorker : IDisposable
{
    private const double TargetLoopIntervalMs = 500.0;
    private readonly object _sync = new();
    private readonly DesktopCaptureWorker _captureService;
    private readonly IReadOnlyList<WeaponTemplateEntry> _templates;
    private readonly Thread _thread;
    private bool _running = true;
    private ViGEmMappingWorker? _consumer;
    private WeaponRecognitionResultState _latestResult = WeaponRecognitionResultState.Empty;
    private byte[] _latestSobel = Array.Empty<byte>();
    private int _latestSobelWidth;
    private int _latestSobelHeight;
    private int _latestSobelFrameId;

    public WeaponRecognitionWorker(DesktopCaptureWorker captureService)
    {
        _captureService = captureService;
        _templates = WeaponTemplateCatalog.LoadEmbeddedTemplates();
        _thread = new Thread(WorkerMain)
        {
            IsBackground = true,
            Name = "Weapon-Recognition-Worker"
        };
        _thread.Start();
    }

    public WeaponRecognitionResultState GetLatestResult()
    {
        lock (_sync)
        {
            return _latestResult;
        }
    }

    public bool TryCopyLatestSobel(ref byte[] outputBuffer, ref int lastFrameId, out int width, out int height)
    {
        lock (_sync)
        {
            width = _latestSobelWidth;
            height = _latestSobelHeight;
            if (_latestSobelFrameId == 0 || _latestSobelFrameId == lastFrameId || _latestSobel.Length == 0)
            {
                return false;
            }

            if (outputBuffer.Length != _latestSobel.Length)
            {
                outputBuffer = new byte[_latestSobel.Length];
            }

            Buffer.BlockCopy(_latestSobel, 0, outputBuffer, 0, _latestSobel.Length);
            lastFrameId = _latestSobelFrameId;
            return true;
        }
    }

    public void SetConsumer(ViGEmMappingWorker? consumer)
    {
        lock (_sync)
        {
            _consumer = consumer;
        }
    }

    private void WorkerMain()
    {
        var loopTimer = Stopwatch.StartNew();
        var nextLoopAtMs = 0.0;
        var lastFrameId = 0;
        byte[] roiBuffer = Array.Empty<byte>();
        while (_running)
        {
            WaitForNextTick(loopTimer, ref nextLoopAtMs, TargetLoopIntervalMs);
            if (!_running)
            {
                break;
            }

            if (!_captureService.TryCopyLatestWeaponRoi(ref roiBuffer, ref lastFrameId, out var width, out var height, out _))
            {
                continue;
            }

            if (width <= 0 || height <= 0 || roiBuffer.Length < width * height * 3)
            {
                Publish(WeaponRecognitionResultState.Empty);
                continue;
            }

            if (_templates.Count == 0)
            {
                Publish(WeaponRecognitionResultState.Empty);
                continue;
            }

            var sw = Stopwatch.StartNew();
            var gray = RgbToGray(roiBuffer, width, height);
            var resized = ResizeGrayToTemplate(gray, width, height, WeaponTemplateCatalog.TemplateWidth, WeaponTemplateCatalog.TemplateHeight);
            var sobel = SobelMagnitude(resized, WeaponTemplateCatalog.TemplateWidth, WeaponTemplateCatalog.TemplateHeight);
            lock (_sync)
            {
                if (_latestSobel.Length != sobel.Length)
                {
                    _latestSobel = new byte[sobel.Length];
                }

                Buffer.BlockCopy(sobel, 0, _latestSobel, 0, sobel.Length);
                _latestSobelWidth = WeaponTemplateCatalog.TemplateWidth;
                _latestSobelHeight = WeaponTemplateCatalog.TemplateHeight;
                _latestSobelFrameId++;
            }

            var bestName = string.Empty;
            var bestSimilarity = 0f;
            foreach (var template in _templates)
            {
                var sim = ComputeSsim(sobel, template.GrayPixels, WeaponTemplateCatalog.TemplateWidth, WeaponTemplateCatalog.TemplateHeight);
                if (sim <= bestSimilarity)
                {
                    continue;
                }

                bestSimilarity = sim;
                bestName = template.Name;
            }

            sw.Stop();
            var weaponName = bestSimilarity < WeaponTemplateCatalog.EmptyHandSsimThreshold || string.IsNullOrWhiteSpace(bestName)
                ? WeaponTemplateCatalog.EmptyHandName
                : bestName;
            Publish(new WeaponRecognitionResultState(weaponName, bestSimilarity));
        }
    }

    private void Publish(in WeaponRecognitionResultState result)
    {
        ViGEmMappingWorker? consumer;
        lock (_sync)
        {
            _latestResult = result;
            consumer = _consumer;
        }

        consumer?.SetWeaponRecognition(result);
    }

    private static byte[] RgbToGray(byte[] rgb, int width, int height)
    {
        var gray = new byte[width * height];
        for (var i = 0; i < gray.Length; i++)
        {
            var rgbIndex = i * 3;
            var r = rgb[rgbIndex + 0];
            var g = rgb[rgbIndex + 1];
            var b = rgb[rgbIndex + 2];
            gray[i] = (byte)Math.Clamp((int)MathF.Round(0.299f * r + 0.587f * g + 0.114f * b), 0, 255);
        }

        return gray;
    }

    private static byte[] ResizeGrayToTemplate(byte[] src, int srcW, int srcH, int dstW, int dstH)
    {
        if (srcW == dstW && srcH == dstH)
        {
            return src.ToArray();
        }

        return srcW > dstW || srcH > dstH
            ? ResizeGrayBox(src, srcW, srcH, dstW, dstH)
            : ResizeGrayBilinear(src, srcW, srcH, dstW, dstH);
    }

    private static byte[] ResizeGrayBox(byte[] src, int srcW, int srcH, int dstW, int dstH)
    {
        var dst = new byte[dstW * dstH];
        for (var y = 0; y < dstH; y++)
        {
            var y0 = y * srcH / dstH;
            var y1 = Math.Max(y0 + 1, (y + 1) * srcH / dstH);
            y1 = Math.Min(y1, srcH);
            for (var x = 0; x < dstW; x++)
            {
                var x0 = x * srcW / dstW;
                var x1 = Math.Max(x0 + 1, (x + 1) * srcW / dstW);
                x1 = Math.Min(x1, srcW);

                long sum = 0;
                var count = 0;
                for (var yy = y0; yy < y1; yy++)
                {
                    var rowBase = yy * srcW;
                    for (var xx = x0; xx < x1; xx++)
                    {
                        sum += src[rowBase + xx];
                        count++;
                    }
                }

                dst[y * dstW + x] = (byte)(count > 0 ? sum / count : 0);
            }
        }

        return dst;
    }

    private static byte[] ResizeGrayBilinear(byte[] src, int srcW, int srcH, int dstW, int dstH)
    {
        var dst = new byte[dstW * dstH];
        for (var y = 0; y < dstH; y++)
        {
            var gy = (y + 0.5f) * srcH / dstH - 0.5f;
            var y0 = Math.Clamp((int)MathF.Floor(gy), 0, srcH - 1);
            var y1 = Math.Clamp(y0 + 1, 0, srcH - 1);
            var wy = gy - y0;
            for (var x = 0; x < dstW; x++)
            {
                var gx = (x + 0.5f) * srcW / dstW - 0.5f;
                var x0 = Math.Clamp((int)MathF.Floor(gx), 0, srcW - 1);
                var x1 = Math.Clamp(x0 + 1, 0, srcW - 1);
                var wx = gx - x0;

                var p00 = src[y0 * srcW + x0];
                var p01 = src[y0 * srcW + x1];
                var p10 = src[y1 * srcW + x0];
                var p11 = src[y1 * srcW + x1];
                var top = p00 + (p01 - p00) * wx;
                var bottom = p10 + (p11 - p10) * wx;
                var value = top + (bottom - top) * wy;
                dst[y * dstW + x] = (byte)Math.Clamp((int)MathF.Round(value), 0, 255);
            }
        }

        return dst;
    }

    private static byte[] SobelMagnitude(byte[] gray, int width, int height)
    {
        if (width <= 0 || height <= 0)
        {
            return Array.Empty<byte>();
        }

        var magnitude = new float[width * height];
        var maxValue = 0f;
        for (var y = 1; y < height - 1; y++)
        {
            for (var x = 1; x < width - 1; x++)
            {
                var p00 = gray[(y - 1) * width + (x - 1)];
                var p01 = gray[(y - 1) * width + x];
                var p02 = gray[(y - 1) * width + (x + 1)];
                var p10 = gray[y * width + (x - 1)];
                var p12 = gray[y * width + (x + 1)];
                var p20 = gray[(y + 1) * width + (x - 1)];
                var p21 = gray[(y + 1) * width + x];
                var p22 = gray[(y + 1) * width + (x + 1)];

                var gx = -p00 + p02 - 2f * p10 + 2f * p12 - p20 + p22;
                var gy = p00 + 2f * p01 + p02 - p20 - 2f * p21 - p22;
                var value = MathF.Sqrt(gx * gx + gy * gy);
                magnitude[y * width + x] = value;
                if (value > maxValue)
                {
                    maxValue = value;
                }
            }
        }

        if (maxValue <= float.Epsilon)
        {
            return new byte[width * height];
        }

        var output = new byte[width * height];
        var scale = 255f / maxValue;
        for (var i = 0; i < output.Length; i++)
        {
            output[i] = (byte)Math.Clamp((int)MathF.Round(magnitude[i] * scale), 0, 255);
        }

        return output;
    }

    private static float ComputeSsim(byte[] a, byte[] b, int width, int height)
    {
        if (a.Length != b.Length || a.Length != width * height || width <= 0 || height <= 0)
        {
            return 0f;
        }

        var n = (float)(width * height);
        var sumA = 0f;
        var sumB = 0f;
        var sumA2 = 0f;
        var sumB2 = 0f;
        var sumAB = 0f;
        for (var i = 0; i < a.Length; i++)
        {
            var va = a[i];
            var vb = b[i];
            sumA += va;
            sumB += vb;
            sumA2 += va * va;
            sumB2 += vb * vb;
            sumAB += va * vb;
        }

        var meanA = sumA / n;
        var meanB = sumB / n;
        var varA = sumA2 / n - meanA * meanA;
        var varB = sumB2 / n - meanB * meanB;
        var cov = sumAB / n - meanA * meanB;

        const float k1 = 0.01f;
        const float k2 = 0.03f;
        const float l = 255f;
        var c1 = (k1 * l) * (k1 * l);
        var c2 = (k2 * l) * (k2 * l);
        var numerator = (2f * meanA * meanB + c1) * (2f * cov + c2);
        var denominator = (meanA * meanA + meanB * meanB + c1) * (varA + varB + c2);
        if (MathF.Abs(denominator) <= float.Epsilon)
        {
            return 0f;
        }

        return Math.Clamp(numerator / denominator, 0f, 1f);
    }

    public void Dispose()
    {
        _running = false;
        if (_thread.IsAlive)
        {
            _thread.Join(800);
        }
    }

    private static void WaitForNextTick(Stopwatch loopTimer, ref double nextLoopAtMs, double intervalMs)
    {
        if (nextLoopAtMs <= 0.0)
        {
            nextLoopAtMs = loopTimer.Elapsed.TotalMilliseconds;
        }

        nextLoopAtMs += intervalMs;
        while (true)
        {
            var remainingMs = nextLoopAtMs - loopTimer.Elapsed.TotalMilliseconds;
            if (remainingMs <= 0.0)
            {
                break;
            }

            if (remainingMs >= 1.5)
            {
                Thread.Sleep(1);
                continue;
            }

            Thread.SpinWait(64);
        }
    }
}

