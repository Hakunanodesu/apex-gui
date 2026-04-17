using System.Threading;
using SDL3;

internal readonly struct SdlGamepadInputSnapshot
{
    public readonly short LeftX;
    public readonly short LeftY;
    public readonly short RightX;
    public readonly short RightY;
    public readonly short LeftTrigger;
    public readonly short RightTrigger;
    public readonly bool A;
    public readonly bool B;
    public readonly bool X;
    public readonly bool Y;
    public readonly bool Back;
    public readonly bool Start;
    public readonly bool Guide;
    public readonly bool LeftShoulder;
    public readonly bool RightShoulder;
    public readonly bool LeftThumb;
    public readonly bool RightThumb;
    public readonly bool DpadUp;
    public readonly bool DpadDown;
    public readonly bool DpadLeft;
    public readonly bool DpadRight;

    public SdlGamepadInputSnapshot(
        short leftX,
        short leftY,
        short rightX,
        short rightY,
        short leftTrigger,
        short rightTrigger,
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
        bool dpadRight)
    {
        LeftX = leftX;
        LeftY = leftY;
        RightX = rightX;
        RightY = rightY;
        LeftTrigger = leftTrigger;
        RightTrigger = rightTrigger;
        A = a;
        B = b;
        X = x;
        Y = y;
        Back = back;
        Start = start;
        Guide = guide;
        LeftShoulder = leftShoulder;
        RightShoulder = rightShoulder;
        LeftThumb = leftThumb;
        RightThumb = rightThumb;
        DpadUp = dpadUp;
        DpadDown = dpadDown;
        DpadLeft = dpadLeft;
        DpadRight = dpadRight;
    }
}

internal sealed class SdlGamepadWorker : IDisposable
{
    private readonly object _sync = new();
    private readonly Thread _thread;
    private bool _running = true;
    private bool _refreshRequested = true;
    private int _refreshRequestVersion;
    private int _refreshCompletedVersion;
    private uint _selectedInstanceId;
    private bool _hasSelectedInstance;
    private (uint InstanceId, string Name)[] _connectedGamepads = Array.Empty<(uint InstanceId, string Name)>();
    private SdlGamepadInputSnapshot _latestInput;
    private bool _hasLatestInput;
    private string? _lastError;

    public SdlGamepadWorker()
    {
        _thread = new Thread(WorkerMain)
        {
            IsBackground = true,
            Name = "SDL-Gamepad-Worker"
        };
        _thread.Start();
    }

    public void RefreshDevices()
    {
        lock (_sync)
        {
            _refreshRequested = true;
            _refreshRequestVersion++;
        }
    }

    public void SetSelectedGamepad(uint? instanceId)
    {
        lock (_sync)
        {
            _hasSelectedInstance = instanceId.HasValue;
            _selectedInstanceId = instanceId ?? 0;
        }
    }

    public (uint InstanceId, string Name)[] GetConnectedGamepads(bool forceRefresh = false)
    {
        if (forceRefresh)
        {
            int targetVersion;
            lock (_sync)
            {
                _refreshRequested = true;
                _refreshRequestVersion++;
                targetVersion = _refreshRequestVersion;
            }

            var waitUntil = Environment.TickCount64 + 250;
            while (_running)
            {
                bool done;
                lock (_sync)
                {
                    done = _refreshCompletedVersion >= targetVersion;
                }

                if (done || Environment.TickCount64 >= waitUntil)
                {
                    break;
                }

                Thread.Sleep(1);
            }
        }

        lock (_sync)
        {
            var copy = new (uint InstanceId, string Name)[_connectedGamepads.Length];
            Array.Copy(_connectedGamepads, copy, _connectedGamepads.Length);
            return copy;
        }
    }

    public bool TryGetLatestInput(out SdlGamepadInputSnapshot snapshot, out string? error)
    {
        lock (_sync)
        {
            snapshot = _latestInput;
            error = _lastError;
            return _hasLatestInput;
        }
    }

    private void WorkerMain()
    {
        IntPtr openedGamepad = IntPtr.Zero;
        uint openedInstanceId = 0;
        var refreshTick = 0;

        try
        {
            while (_running)
            {
                SDL.PumpEvents();
                SDL.UpdateJoysticks();
                SDL.UpdateGamepads();

                bool needRefresh;
                bool hasSelected;
                uint selectedInstanceId;
                lock (_sync)
                {
                    needRefresh = _refreshRequested || refreshTick++ >= 300;
                    hasSelected = _hasSelectedInstance;
                    selectedInstanceId = _selectedInstanceId;
                    if (needRefresh)
                    {
                        _refreshRequested = false;
                    }
                }

                if (needRefresh)
                {
                    refreshTick = 0;
                    lock (_sync)
                    {
                        _connectedGamepads = EnumerateConnectedGamepads();
                        _refreshCompletedVersion = _refreshRequestVersion;
                    }
                }

                if (!hasSelected)
                {
                    if (openedGamepad != IntPtr.Zero)
                    {
                        SDL.CloseGamepad(openedGamepad);
                        openedGamepad = IntPtr.Zero;
                        openedInstanceId = 0;
                    }

                    lock (_sync)
                    {
                        _hasLatestInput = false;
                    }
                    Thread.Sleep(2);
                    continue;
                }

                if (openedGamepad == IntPtr.Zero || openedInstanceId != selectedInstanceId)
                {
                    if (openedGamepad != IntPtr.Zero)
                    {
                        SDL.CloseGamepad(openedGamepad);
                        openedGamepad = IntPtr.Zero;
                        openedInstanceId = 0;
                    }

                    openedGamepad = SDL.OpenGamepad(selectedInstanceId);
                    openedInstanceId = selectedInstanceId;
                    if (openedGamepad == IntPtr.Zero)
                    {
                        lock (_sync)
                        {
                            _hasLatestInput = false;
                            _lastError = "SDL 打开手柄失败";
                        }
                        Thread.Sleep(10);
                        continue;
                    }
                }

                var snapshot = new SdlGamepadInputSnapshot(
                    SDL.GetGamepadAxis(openedGamepad, SDL.GamepadAxis.LeftX),
                    SDL.GetGamepadAxis(openedGamepad, SDL.GamepadAxis.LeftY),
                    SDL.GetGamepadAxis(openedGamepad, SDL.GamepadAxis.RightX),
                    SDL.GetGamepadAxis(openedGamepad, SDL.GamepadAxis.RightY),
                    SDL.GetGamepadAxis(openedGamepad, SDL.GamepadAxis.LeftTrigger),
                    SDL.GetGamepadAxis(openedGamepad, SDL.GamepadAxis.RightTrigger),
                    SDL.GetGamepadButton(openedGamepad, SDL.GamepadButton.South),
                    SDL.GetGamepadButton(openedGamepad, SDL.GamepadButton.East),
                    SDL.GetGamepadButton(openedGamepad, SDL.GamepadButton.West),
                    SDL.GetGamepadButton(openedGamepad, SDL.GamepadButton.North),
                    GetGamepadButtonByNames(openedGamepad, "Back", "Select", "View", "Minus"),
                    GetGamepadButtonByNames(openedGamepad, "Start", "Menu", "Options", "Plus"),
                    GetGamepadButtonByNames(openedGamepad, "Guide", "Home"),
                    GetGamepadButtonByNames(openedGamepad, "LeftShoulder", "LeftBumper"),
                    GetGamepadButtonByNames(openedGamepad, "RightShoulder", "RightBumper"),
                    GetGamepadButtonByNames(openedGamepad, "LeftStick", "LeftThumb"),
                    GetGamepadButtonByNames(openedGamepad, "RightStick", "RightThumb"),
                    GetGamepadButtonByNames(openedGamepad, "DpadUp", "DPadUp", "Up"),
                    GetGamepadButtonByNames(openedGamepad, "DpadDown", "DPadDown", "Down"),
                    GetGamepadButtonByNames(openedGamepad, "DpadLeft", "DPadLeft", "Left"),
                    GetGamepadButtonByNames(openedGamepad, "DpadRight", "DPadRight", "Right"));

                lock (_sync)
                {
                    _latestInput = snapshot;
                    _hasLatestInput = true;
                    _lastError = null;
                }

                Thread.Sleep(1);
            }
        }
        catch (Exception ex)
        {
            lock (_sync)
            {
                _hasLatestInput = false;
                _lastError = $"{ex.GetType().Name}: {ex.Message}";
            }
        }
        finally
        {
            if (openedGamepad != IntPtr.Zero)
            {
                SDL.CloseGamepad(openedGamepad);
            }
        }
    }

    private static (uint InstanceId, string Name)[] EnumerateConnectedGamepads()
    {
        if ((SDL.WasInit(SDL.InitFlags.Gamepad) & SDL.InitFlags.Gamepad) == 0)
        {
            if (!SDL.InitSubSystem(SDL.InitFlags.Gamepad))
            {
                return Array.Empty<(uint InstanceId, string Name)>();
            }
        }

        var gamepads = SDL.GetGamepads(out var count);
        if (gamepads is null || count <= 0)
        {
            return Array.Empty<(uint InstanceId, string Name)>();
        }

        var options = new List<(uint InstanceId, string Name)>(count);
        for (var i = 0; i < count; i++)
        {
            var instanceId = gamepads[i];
            var name = SDL.GetGamepadNameForID(instanceId);
            if (!string.IsNullOrWhiteSpace(name))
            {
                options.Add((instanceId, name));
            }
        }

        return options.ToArray();
    }

    private static bool GetGamepadButtonByNames(IntPtr gamepad, params string[] enumNames)
    {
        for (var i = 0; i < enumNames.Length; i++)
        {
            if (Enum.TryParse<SDL.GamepadButton>(enumNames[i], true, out var button))
            {
                return SDL.GetGamepadButton(gamepad, button);
            }
        }

        return false;
    }

    public void Dispose()
    {
        _running = false;
        if (_thread.IsAlive)
        {
            _thread.Join(500);
        }
    }
}

