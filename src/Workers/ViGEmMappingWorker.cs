using System.Threading;
using Nefarius.ViGEm.Client;
using Nefarius.ViGEm.Client.Targets;
using Nefarius.ViGEm.Client.Targets.Xbox360;

internal sealed class ViGEmMappingWorker : IDisposable
{
    private readonly object _sync = new();
    private readonly Thread _thread;
    private bool _running = true;
    private SdlGamepadWorker? _sdlGamepadWorker;
    private ViGEmClient? _client;
    private IXbox360Controller? _controller;
    private bool _isConnected;
    private string _status = "未初始化";
    private string? _lastError;

    public ViGEmMappingWorker()
    {
        _thread = new Thread(WorkerMain)
        {
            IsBackground = true,
            Name = "ViGEm-Mapping-Worker"
        };
        _thread.Start();
    }

    public void SetSdlGamepadWorker(SdlGamepadWorker? sdlGamepadWorker)
    {
        lock (_sync)
        {
            _sdlGamepadWorker = sdlGamepadWorker;
        }
    }

    public bool IsConnected
    {
        get
        {
            lock (_sync)
            {
                return _isConnected;
            }
        }
    }

    public string Status
    {
        get
        {
            lock (_sync)
            {
                return _status;
            }
        }
    }

    public void ConnectVirtualGamepad()
    {
        lock (_sync)
        {
            if (_isConnected)
            {
                _status = "已连接";
                return;
            }

            try
            {
                _client ??= new ViGEmClient();
                _controller ??= _client.CreateXbox360Controller();
                _controller.Connect();
                _isConnected = true;
                _status = "已连接";
                _lastError = null;
            }
            catch (Exception ex)
            {
                _isConnected = false;
                _status = $"连接失败: {ex.GetType().Name}: {ex.Message}";
                _lastError = _status;
                SafeDisposeController();
                SafeDisposeClient();
            }
        }
    }

    public void DisconnectVirtualGamepad()
    {
        lock (_sync)
        {
            if (!_isConnected && _controller is null)
            {
                _status = "已断开";
                return;
            }

            try
            {
                _controller?.Disconnect();
            }
            catch (Exception ex)
            {
                _status = $"断开失败: {ex.GetType().Name}: {ex.Message}";
                _lastError = _status;
            }
            finally
            {
                _isConnected = false;
                SafeDisposeController();
                SafeDisposeClient();
                _status = "已断开";
            }
        }
    }

    public string? GetLastError()
    {
        lock (_sync)
        {
            return _lastError;
        }
    }

    private void WorkerMain()
    {
        while (_running)
        {
            SdlGamepadWorker? sdlWorker;
            bool isConnected;
            lock (_sync)
            {
                sdlWorker = _sdlGamepadWorker;
                isConnected = _isConnected;
            }

            if (sdlWorker is null || !isConnected)
            {
                Thread.Sleep(2);
                continue;
            }

            if (!sdlWorker.TryGetLatestInput(out var input, out var sdlError))
            {
                if (!string.IsNullOrWhiteSpace(sdlError))
                {
                    lock (_sync)
                    {
                        _lastError = sdlError;
                    }
                }

                Thread.Sleep(1);
                continue;
            }

            if (!TrySubmitState(
                    input.LeftX,
                    InvertStickY(input.LeftY),
                    input.RightX,
                    InvertStickY(input.RightY),
                    ToXboxTrigger(input.LeftTrigger),
                    ToXboxTrigger(input.RightTrigger),
                    input.A,
                    input.B,
                    input.X,
                    input.Y,
                    input.Back,
                    input.Start,
                    input.Guide,
                    input.LeftShoulder,
                    input.RightShoulder,
                    input.LeftThumb,
                    input.RightThumb,
                    input.DpadUp,
                    input.DpadDown,
                    input.DpadLeft,
                    input.DpadRight,
                    out var mapError))
            {
                lock (_sync)
                {
                    _lastError = mapError;
                }
            }
            else
            {
                lock (_sync)
                {
                    _lastError = null;
                }
            }

            Thread.Sleep(1);
        }
    }

    private bool TrySubmitState(
        short leftX,
        short leftY,
        short rightX,
        short rightY,
        byte leftTrigger,
        byte rightTrigger,
        bool a,
        bool b,
        bool x,
        bool y,
        bool back,
        bool start,
        bool guide,
        bool leftShoulder,
        bool rightShoulder,
        bool leftThumb,
        bool rightThumb,
        bool dpadUp,
        bool dpadDown,
        bool dpadLeft,
        bool dpadRight,
        out string error)
    {
        lock (_sync)
        {
            error = string.Empty;
            if (!_isConnected || _controller is null)
            {
                error = "虚拟手柄未连接";
                return false;
            }

            try
            {
                _controller.SetAxisValue(Xbox360Axis.LeftThumbX, leftX);
                _controller.SetAxisValue(Xbox360Axis.LeftThumbY, leftY);
                _controller.SetAxisValue(Xbox360Axis.RightThumbX, rightX);
                _controller.SetAxisValue(Xbox360Axis.RightThumbY, rightY);
                _controller.SetSliderValue(Xbox360Slider.LeftTrigger, leftTrigger);
                _controller.SetSliderValue(Xbox360Slider.RightTrigger, rightTrigger);

                _controller.SetButtonState(Xbox360Button.A, a);
                _controller.SetButtonState(Xbox360Button.B, b);
                _controller.SetButtonState(Xbox360Button.X, x);
                _controller.SetButtonState(Xbox360Button.Y, y);
                _controller.SetButtonState(Xbox360Button.Back, back);
                _controller.SetButtonState(Xbox360Button.Start, start);
                _controller.SetButtonState(Xbox360Button.Guide, guide);
                _controller.SetButtonState(Xbox360Button.LeftShoulder, leftShoulder);
                _controller.SetButtonState(Xbox360Button.RightShoulder, rightShoulder);
                _controller.SetButtonState(Xbox360Button.LeftThumb, leftThumb);
                _controller.SetButtonState(Xbox360Button.RightThumb, rightThumb);
                _controller.SetButtonState(Xbox360Button.Up, dpadUp);
                _controller.SetButtonState(Xbox360Button.Down, dpadDown);
                _controller.SetButtonState(Xbox360Button.Left, dpadLeft);
                _controller.SetButtonState(Xbox360Button.Right, dpadRight);
                _controller.SubmitReport();
                return true;
            }
            catch (Exception ex)
            {
                error = $"{ex.GetType().Name}: {ex.Message}";
                return false;
            }
        }
    }

    private static byte ToXboxTrigger(short raw)
    {
        var clamped = Math.Clamp((int)raw, 0, short.MaxValue);
        return (byte)(clamped * byte.MaxValue / short.MaxValue);
    }

    private static short InvertStickY(short raw)
    {
        var inverted = -(int)raw;
        return (short)Math.Clamp(inverted, short.MinValue, short.MaxValue);
    }

    public void Dispose()
    {
        _running = false;
        if (_thread.IsAlive)
        {
            _thread.Join(500);
        }

        DisconnectVirtualGamepad();
    }

    private void SafeDisposeController()
    {
        try
        {
            // IXbox360Controller 未公开 Dispose；断开连接即可。
        }
        catch
        {
            // ignore
        }
        finally
        {
            _controller = null;
        }
    }

    private void SafeDisposeClient()
    {
        try
        {
            _client?.Dispose();
        }
        catch
        {
            // ignore
        }
        finally
        {
            _client = null;
        }
    }
}

