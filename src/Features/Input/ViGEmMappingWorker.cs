using System.Diagnostics;
using System.Threading;
using Nefarius.ViGEm.Client;
using Nefarius.ViGEm.Client.Targets;
using Nefarius.ViGEm.Client.Targets.Xbox360;

internal readonly record struct ViGEmMappingSnapshot(
    bool IsConnected,
    bool RequestedEnabled,
    bool IsMappingActive,
    uint? SelectedInstanceId,
    string? LastError);

internal readonly record struct ControllerOutputState(
    short LeftX,
    short LeftY,
    short RightX,
    short RightY,
    byte LeftTrigger,
    byte RightTrigger,
    bool A,
    bool B,
    bool X,
    bool Y,
    bool Back,
    bool Start,
    bool Guide,
    bool LeftShoulder,
    bool RightShoulder,
    bool LeftThumb,
    bool RightThumb,
    bool DpadUp,
    bool DpadDown,
    bool DpadLeft,
    bool DpadRight);

internal sealed class ViGEmMappingWorker : IDisposable
{
    private const double TargetLoopIntervalMs = 1000.0 / 500.0;
    private static readonly TimeSpan SdlInputFailureGrace = TimeSpan.FromSeconds(1);
    private const int TriggerTimingUnitMs = 20;
    private const int ReleaseToFirePulseMs = 100;
    private readonly object _sync = new();
    private readonly Thread _thread;
    private readonly SmartCoreAimAssistService _smartCoreAimAssistService = new();
    private bool _running = true;
    private SdlGamepadWorker? _sdlGamepadWorker;
    private ViGEmClient? _client;
    private IXbox360Controller? _controller;
    private bool _isConnected;
    private bool _requestedEnabled;
    private bool _isMappingActive;
    private bool _hasSelectedGamepad;
    private uint _selectedGamepadInstanceId;
    private string _status = "未初始化";
    private string? _lastError;
    private SmartCoreAimAssistConfigState _aimAssistConfigState = SmartCoreAimAssistConfigState.Disabled;
    private SmartCoreDetectionState _aimAssistDetectionState = SmartCoreDetectionState.Empty;
    private WeaponRecognitionResultState _weaponRecognitionState = WeaponRecognitionResultState.Empty;

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

    public ViGEmMappingSnapshot GetSnapshot()
    {
        lock (_sync)
        {
            return new ViGEmMappingSnapshot(
                _isConnected,
                _requestedEnabled,
                _isMappingActive,
                _hasSelectedGamepad ? _selectedGamepadInstanceId : null,
                _lastError);
        }
    }

    public void SetRequestedEnabled(bool requestedEnabled)
    {
        lock (_sync)
        {
            _requestedEnabled = requestedEnabled;
            if (!requestedEnabled)
            {
                _isMappingActive = false;
            }
        }
    }

    public void SetSelectedGamepad(uint? instanceId)
    {
        SdlGamepadWorker? sdlGamepadWorker;
        lock (_sync)
        {
            _hasSelectedGamepad = instanceId.HasValue;
            _selectedGamepadInstanceId = instanceId ?? 0;
            if (!_hasSelectedGamepad)
            {
                _isMappingActive = false;
            }

            sdlGamepadWorker = _sdlGamepadWorker;
        }

        sdlGamepadWorker?.SetSelectedGamepad(instanceId);
    }

    public void SetAimAssistConfig(in SmartCoreAimAssistConfigState state)
    {
        lock (_sync)
        {
            if (AreEquivalentConfig(_aimAssistConfigState, state))
            {
                return;
            }

            _aimAssistConfigState = state;
        }
    }

    public void SetAimAssistDetections(in SmartCoreDetectionState state)
    {
        lock (_sync)
        {
            _aimAssistDetectionState = state;
        }
    }

    public void SetWeaponRecognition(in WeaponRecognitionResultState state)
    {
        lock (_sync)
        {
            _weaponRecognitionState = state;
        }
    }

    private void WorkerMain()
    {
        var loopTimer = Stopwatch.StartNew();
        var nextLoopAtMs = 0.0;
        var rapidFireHalfPeriod = TimeSpan.FromMilliseconds(TriggerTimingUnitMs);
        var releasePulseDuration = TimeSpan.FromMilliseconds(ReleaseToFirePulseMs);
        var rapidHigh = false;
        var rapidLastToggleAt = DateTime.UtcNow;
        var releasePrevPressed = false;
        DateTime? releasePulseUntil = null;
        DateTime? sdlInputFailureSinceUtc = null;
        ControllerOutputState? lastSubmittedState = null;
        while (_running)
        {
            WaitForNextTick(loopTimer, ref nextLoopAtMs, TargetLoopIntervalMs);
            SdlGamepadWorker? sdlWorker;
            bool isConnected;
            bool requestedEnabled;
            bool hasSelectedGamepad;
            SmartCoreAimAssistConfigState aimAssistConfigState;
            SmartCoreDetectionState aimAssistDetectionState;
            WeaponRecognitionResultState weaponRecognitionState;
            lock (_sync)
            {
                sdlWorker = _sdlGamepadWorker;
                isConnected = _isConnected;
                requestedEnabled = _requestedEnabled;
                hasSelectedGamepad = _hasSelectedGamepad;
                aimAssistConfigState = _aimAssistConfigState;
                aimAssistDetectionState = _aimAssistDetectionState;
                weaponRecognitionState = _weaponRecognitionState;
            }

            if (sdlWorker is null || !isConnected || !requestedEnabled || !hasSelectedGamepad)
            {
                sdlInputFailureSinceUtc = null;
                lastSubmittedState = null;
                lock (_sync)
                {
                    _isMappingActive = false;
                }
                continue;
            }

            if (!sdlWorker.TryGetLatestInput(out var input, out var sdlError))
            {
                var now = DateTime.UtcNow;
                sdlInputFailureSinceUtc ??= now;
                var inGraceWindow = (now - sdlInputFailureSinceUtc.Value) < SdlInputFailureGrace;
                if (inGraceWindow)
                {
                    continue;
                }

                lock (_sync)
                {
                    _isMappingActive = false;
                    if (!string.IsNullOrWhiteSpace(sdlError))
                    {
                        _lastError = sdlError;
                    }
                }

                lastSubmittedState = null;
                continue;
            }

            sdlInputFailureSinceUtc = null;

            var recognizedWeaponName = weaponRecognitionState.WeaponName;
            var isAimSnapOverrideWeapon = ContainsWeaponName(aimAssistConfigState.AimSnapWeapons, recognizedWeaponName);
            var isRapidFireWeapon = ContainsWeaponName(aimAssistConfigState.RapidFireWeapons, recognizedWeaponName);
            var isReleaseFireWeapon = ContainsWeaponName(aimAssistConfigState.ReleaseFireWeapons, recognizedWeaponName);
            var fireBindingIndex = aimAssistConfigState.FireBindingIndex;
            var firePressed = GamepadBindingCatalog.IsPressed(fireBindingIndex, input);

            short mappedLeftTrigger = input.LeftTrigger;
            short mappedRightTrigger = input.RightTrigger;
            var mappedA = input.A;
            var mappedB = input.B;
            var mappedX = input.X;
            var mappedY = input.Y;
            var mappedBack = input.Back;
            var mappedStart = input.Start;
            var mappedGuide = input.Guide;
            var mappedLeftShoulder = input.LeftShoulder;
            var mappedRightShoulder = input.RightShoulder;
            var mappedLeftThumb = input.LeftThumb;
            var mappedRightThumb = input.RightThumb;
            var mappedDpadUp = input.DpadUp;
            var mappedDpadDown = input.DpadDown;
            var mappedDpadLeft = input.DpadLeft;
            var mappedDpadRight = input.DpadRight;

            short ResolveFireBindingAnalogValue()
            {
                return GamepadBindingCatalog.IsTriggerBinding(fireBindingIndex)
                    ? GamepadBindingCatalog.GetTriggerValue(fireBindingIndex, input)
                    : short.MaxValue;
            }

            void SetFireBindingPressed(bool pressed, short analogValueWhenPressed = short.MaxValue)
            {
                switch (fireBindingIndex)
                {
                    case 0:
                        mappedLeftTrigger = pressed ? analogValueWhenPressed : (short)0;
                        break;
                    case 1:
                        mappedRightTrigger = pressed ? analogValueWhenPressed : (short)0;
                        break;
                    case 2:
                        mappedLeftShoulder = pressed;
                        break;
                    case 3:
                        mappedRightShoulder = pressed;
                        break;
                    case 4:
                        mappedA = pressed;
                        break;
                    case 5:
                        mappedB = pressed;
                        break;
                    case 6:
                        mappedX = pressed;
                        break;
                    case 7:
                        mappedY = pressed;
                        break;
                    case 8:
                        mappedLeftThumb = pressed;
                        break;
                    case 9:
                        mappedRightThumb = pressed;
                        break;
                    case 10:
                        mappedDpadUp = pressed;
                        break;
                    case 11:
                        mappedDpadDown = pressed;
                        break;
                    case 12:
                        mappedDpadLeft = pressed;
                        break;
                    case 13:
                        mappedDpadRight = pressed;
                        break;
                    case 14:
                        mappedBack = pressed;
                        break;
                    case 15:
                        mappedStart = pressed;
                        break;
                }
            }

            if (isReleaseFireWeapon)
            {
                if (firePressed)
                {
                    SetFireBindingPressed(false);
                    releasePrevPressed = true;
                    releasePulseUntil = null;
                }
                else
                {
                    if (releasePrevPressed)
                    {
                        releasePrevPressed = false;
                        releasePulseUntil = DateTime.UtcNow + releasePulseDuration;
                    }

                    if (releasePulseUntil.HasValue && DateTime.UtcNow < releasePulseUntil.Value)
                    {
                        SetFireBindingPressed(true, short.MaxValue);
                    }
                    else
                    {
                        SetFireBindingPressed(false);
                        releasePulseUntil = null;
                    }
                }
            }
            else
            {
                releasePrevPressed = false;
                releasePulseUntil = null;
            }

            if (isRapidFireWeapon && firePressed && !isReleaseFireWeapon)
            {
                var now = DateTime.UtcNow;
                var elapsed = now - rapidLastToggleAt;
                if (elapsed >= rapidFireHalfPeriod)
                {
                    var steps = Math.Max(1, (int)(elapsed.Ticks / rapidFireHalfPeriod.Ticks));
                    if ((steps & 1) == 1)
                    {
                        rapidHigh = !rapidHigh;
                    }

                    rapidLastToggleAt = rapidLastToggleAt.AddTicks(rapidFireHalfPeriod.Ticks * steps);
                }

                if (rapidHigh)
                {
                    SetFireBindingPressed(true, ResolveFireBindingAnalogValue());
                }
                else
                {
                    SetFireBindingPressed(false);
                }
            }
            else
            {
                rapidHigh = false;
                rapidLastToggleAt = DateTime.UtcNow;
            }

            var aimAssistResult = _smartCoreAimAssistService.Evaluate(new SmartCoreAimAssistContext(
                aimAssistConfigState.IsEnabled,
                aimAssistConfigState.IsMappingActive,
                aimAssistConfigState.SnapModeIndex,
                aimAssistConfigState.SnapOuterRange,
                aimAssistConfigState.SnapInnerRange,
                aimAssistConfigState.SnapOuterStrength,
                aimAssistConfigState.SnapInnerStrength,
                aimAssistConfigState.SnapStartStrength,
                aimAssistConfigState.SnapVerticalStrengthFactor,
                aimAssistConfigState.SnapHipfireStrengthFactor,
                aimAssistConfigState.SnapHeight,
                aimAssistConfigState.SnapInnerInterpolationTypeIndex,
                aimAssistConfigState.AimBindingIndex,
                aimAssistConfigState.FireBindingIndex,
                isAimSnapOverrideWeapon,
                input,
                aimAssistDetectionState.Boxes));

            var outputState = new ControllerOutputState(
                input.LeftX,
                InvertStickY(input.LeftY),
                CombineStickAxis(input.RightX, aimAssistResult.IsActive ? aimAssistResult.RightX : (short)0),
                InvertStickY(CombineStickAxis(input.RightY, aimAssistResult.IsActive ? aimAssistResult.RightY : (short)0)),
                ToXboxTrigger(mappedLeftTrigger),
                ToXboxTrigger(mappedRightTrigger),
                mappedA,
                mappedB,
                mappedX,
                mappedY,
                mappedBack,
                mappedStart,
                mappedGuide,
                mappedLeftShoulder,
                mappedRightShoulder,
                mappedLeftThumb,
                mappedRightThumb,
                mappedDpadUp,
                mappedDpadDown,
                mappedDpadLeft,
                mappedDpadRight);

            if (lastSubmittedState.HasValue && lastSubmittedState.Value.Equals(outputState))
            {
                lock (_sync)
                {
                    _isMappingActive = true;
                    _lastError = null;
                }
                continue;
            }

            if (!TrySubmitState(
                    outputState.LeftX,
                    outputState.LeftY,
                    outputState.RightX,
                    outputState.RightY,
                    outputState.LeftTrigger,
                    outputState.RightTrigger,
                    outputState.A,
                    outputState.B,
                    outputState.X,
                    outputState.Y,
                    outputState.Back,
                    outputState.Start,
                    outputState.Guide,
                    outputState.LeftShoulder,
                    outputState.RightShoulder,
                    outputState.LeftThumb,
                    outputState.RightThumb,
                    outputState.DpadUp,
                    outputState.DpadDown,
                    outputState.DpadLeft,
                    outputState.DpadRight,
                    out var mapError))
            {
                lock (_sync)
                {
                    _lastError = mapError;
                }
                lastSubmittedState = null;
            }
            else
            {
                lastSubmittedState = outputState;
                lock (_sync)
                {
                    _isMappingActive = true;
                    _lastError = null;
                }
            }

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

    private static short CombineStickAxis(short baseValue, short offset)
    {
        var combined = (int)baseValue + offset;
        return (short)Math.Clamp(combined, short.MinValue, short.MaxValue);
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

    private static bool ContainsWeaponName(IReadOnlyList<string>? weaponNames, string? weaponName)
    {
        if (string.IsNullOrWhiteSpace(weaponName) ||
            string.Equals(weaponName, WeaponTemplateCatalog.EmptyHandName, StringComparison.OrdinalIgnoreCase) ||
            weaponNames is null ||
            weaponNames.Count == 0)
        {
            return false;
        }

        for (var i = 0; i < weaponNames.Count; i++)
        {
            if (string.Equals(weaponNames[i], weaponName, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    private static bool AreEquivalentConfig(in SmartCoreAimAssistConfigState a, in SmartCoreAimAssistConfigState b)
    {
        return a.IsEnabled == b.IsEnabled &&
               a.IsMappingActive == b.IsMappingActive &&
               a.SnapModeIndex == b.SnapModeIndex &&
               a.SnapOuterRange == b.SnapOuterRange &&
               a.SnapInnerRange == b.SnapInnerRange &&
               a.SnapOuterStrength.Equals(b.SnapOuterStrength) &&
               a.SnapInnerStrength.Equals(b.SnapInnerStrength) &&
               a.SnapStartStrength.Equals(b.SnapStartStrength) &&
               a.SnapVerticalStrengthFactor.Equals(b.SnapVerticalStrengthFactor) &&
               a.SnapHipfireStrengthFactor.Equals(b.SnapHipfireStrengthFactor) &&
               a.SnapHeight.Equals(b.SnapHeight) &&
               a.SnapInnerInterpolationTypeIndex == b.SnapInnerInterpolationTypeIndex &&
               a.AimBindingIndex == b.AimBindingIndex &&
               a.FireBindingIndex == b.FireBindingIndex &&
               AreSameList(a.AimSnapWeapons, b.AimSnapWeapons) &&
               AreSameList(a.RapidFireWeapons, b.RapidFireWeapons) &&
               AreSameList(a.ReleaseFireWeapons, b.ReleaseFireWeapons);
    }

    private static bool AreSameList(IReadOnlyList<string>? a, IReadOnlyList<string>? b)
    {
        if (ReferenceEquals(a, b))
        {
            return true;
        }

        if (a is null || b is null || a.Count != b.Count)
        {
            return false;
        }

        for (var i = 0; i < a.Count; i++)
        {
            if (!string.Equals(a[i], b[i], StringComparison.Ordinal))
            {
                return false;
            }
        }

        return true;
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

