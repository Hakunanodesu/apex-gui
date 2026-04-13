using System.Diagnostics;
using System.Threading;

internal sealed class DesktopCaptureWorker : IDisposable
{
    private readonly object _sync = new();
    private readonly Thread _thread;
    private bool _running = true;
    private byte[] _latestFrame = Array.Empty<byte>();
    private int _latestWidth;
    private int _latestHeight;
    private int _latestFrameId;
    private string? _lastError;

    private long _pollCount;
    private long _successCount;
    private double _captureMsSum;
    private double _captureMsMax;
    private readonly Queue<double> _pendingCaptureMs = new();
    private int _requestedCaptureWidth = 320;
    private int _requestedCaptureHeight = 320;

    public DesktopCaptureWorker()
    {
        _thread = new Thread(CaptureThreadMain)
        {
            IsBackground = true,
            Name = "DXGI-Capture-Worker"
        };
        _thread.Start();
    }

    public bool TryCopyLatestFrame(ref byte[] uploadBuffer, ref int lastFrameId, out int width, out int height, out string? error)
    {
        lock (_sync)
        {
            error = _lastError;
            width = _latestWidth;
            height = _latestHeight;

            if (_latestFrameId == 0 || _latestFrameId == lastFrameId)
            {
                return false;
            }

            if (uploadBuffer.Length != _latestFrame.Length)
            {
                uploadBuffer = new byte[_latestFrame.Length];
            }

            System.Buffer.BlockCopy(_latestFrame, 0, uploadBuffer, 0, _latestFrame.Length);
            lastFrameId = _latestFrameId;
            return true;
        }
    }

    public CaptureTelemetry GetTelemetrySnapshot()
    {
        lock (_sync)
        {
            return new CaptureTelemetry(_pollCount, _successCount, _captureMsSum, _captureMsMax);
        }
    }

    public void DrainCaptureSamples(List<double> destination)
    {
        lock (_sync)
        {
            while (_pendingCaptureMs.Count > 0)
            {
                destination.Add(_pendingCaptureMs.Dequeue());
            }
        }
    }

    public void SetCaptureRegion(int width, int height)
    {
        lock (_sync)
        {
            _requestedCaptureWidth = Math.Max(1, width);
            _requestedCaptureHeight = Math.Max(1, height);
        }
    }

    private void CaptureThreadMain()
    {
        try
        {
            using var duplicator = new DxgiDesktopDuplicator();
            var timer = Stopwatch.StartNew();

            while (_running)
            {
                int requestedWidth;
                int requestedHeight;
                lock (_sync)
                {
                    requestedWidth = _requestedCaptureWidth;
                    requestedHeight = _requestedCaptureHeight;
                }

                duplicator.SetCaptureRegion(requestedWidth, requestedHeight);
                timer.Restart();
                var ok = duplicator.TryCaptureFrame(1, out var frameData, out var width, out var height, out var error);
                timer.Stop();
                var shouldBackoff = false;

                lock (_sync)
                {
                    _pollCount++;
                    var elapsedMs = timer.Elapsed.TotalMilliseconds;

                    if (ok)
                    {
                        _successCount++;
                        _captureMsSum += elapsedMs;
                        _captureMsMax = Math.Max(_captureMsMax, elapsedMs);
                        _pendingCaptureMs.Enqueue(elapsedMs);
                        if (_latestFrame.Length != frameData.Length)
                        {
                            _latestFrame = new byte[frameData.Length];
                        }

                        System.Buffer.BlockCopy(frameData, 0, _latestFrame, 0, frameData.Length);
                        _latestWidth = width;
                        _latestHeight = height;
                        _latestFrameId++;
                    }
                    else if (!string.IsNullOrWhiteSpace(error))
                    {
                        _lastError = error;
                        _running = false;
                    }
                    else
                    {
                        shouldBackoff = true;
                    }
                }

                if (shouldBackoff)
                {
                    Thread.Sleep(1);
                }
            }
        }
        catch (Exception ex)
        {
            lock (_sync)
            {
                _lastError = $"{ex.GetType().Name}: {ex.Message}";
                _running = false;
            }
        }
    }

    public void Dispose()
    {
        _running = false;
        if (_thread.IsAlive)
        {
            _thread.Join(300);
        }
    }
}
