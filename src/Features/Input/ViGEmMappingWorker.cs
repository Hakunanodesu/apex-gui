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

internal sealed partial class ViGEmMappingWorker : IAimAssistDetectionSink, IWeaponRecognitionSink, IDisposable
{
    private sealed class MacroExecutionState
    {
        public bool TriggerWasPressed;
        public DateTime? PressDelayStartedAt;
        public DateTime? HoldStartedAt;
        public bool ActionPressed;
        public DateTime? ActionReleaseAt;

        public void Reset()
        {
            TriggerWasPressed = false;
            PressDelayStartedAt = null;
            HoldStartedAt = null;
            ActionPressed = false;
            ActionReleaseAt = null;
        }
    }

    private const double TargetLoopIntervalMs = 1000.0 / 500.0;
    private static readonly TimeSpan SdlInputFailureGrace = TimeSpan.FromSeconds(1);
    private const int ReleaseToFirePulseMs = 100;
    private const int MinRapidFireHz = 1;
    private const int MaxRapidFireHz = 30;
    private const int DetectionStaleAfterMs = 1000;
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
        var releasePulseDuration = TimeSpan.FromMilliseconds(ReleaseToFirePulseMs);
        var rapidFireLoop = new FixedRateLoop(DateTime.UtcNow);
        var releasePrevPressed = false;
        var snapRampTriggerPrev = false;
        var snapRampStartedAt = DateTime.UtcNow;
        var macroState = new MacroExecutionState();
        var lastMacro = (MacroRuntimeState?)null;
        DateTime? releasePulseUntil = null;
        DateTime? sdlInputFailureSinceUtc = null;
        ControllerOutputState? lastSubmittedState = null;
        var injectedKeyboardVirtualKeys = new HashSet<ushort>();
        var desiredKeyboardVirtualKeys = new HashSet<ushort>();
        while (_running)
        {
            FixedRateWaiter.WaitForNextTick(loopTimer, ref nextLoopAtMs, TargetLoopIntervalMs);
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
                desiredKeyboardVirtualKeys.Clear();
                UpdateKeyboardInjections(desiredKeyboardVirtualKeys, injectedKeyboardVirtualKeys);
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

                desiredKeyboardVirtualKeys.Clear();
                UpdateKeyboardInjections(desiredKeyboardVirtualKeys, injectedKeyboardVirtualKeys);

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
            var weaponPolicy = aimAssistConfigState.WeaponPolicy;
            var isAimSnapOverrideWeapon = ContainsWeaponName(weaponPolicy.AimSnapWeapons, recognizedWeaponName);
            var isRapidFireWeapon = ContainsWeaponName(weaponPolicy.RapidFireWeapons, recognizedWeaponName);
            var isReleaseFireWeapon = ContainsWeaponName(weaponPolicy.ReleaseFireWeapons, recognizedWeaponName);
            var shouldApplyRapidFire = weaponPolicy.RapidFireStrategy switch
            {
                RapidFireStrategy.Off => false,
                RapidFireStrategy.Always => true,
                RapidFireStrategy.WeaponBased => isRapidFireWeapon,
                _ => throw new InvalidOperationException($"Unhandled rapid-fire strategy: {weaponPolicy.RapidFireStrategy}")
            };
            var bindings = aimAssistConfigState.Bindings;
            var fireBindingIndex = bindings.FireBindingIndex;
            var voiceBindingIndex = bindings.VoiceBindingIndex;
            var touchpadLeftBindingIndex = bindings.TouchpadLeftBindingIndex;
            var touchpadRightBindingIndex = bindings.TouchpadRightBindingIndex;
            var firePressed = GamepadBindingCatalog.IsPressed(fireBindingIndex, input);

            var aimPressedForRamp = GamepadBindingCatalog.IsPressed(bindings.AimBindingIndex, input);
            var aimAssist = aimAssistConfigState.AimAssist;
            var snapRampTrigger = SnapActivationPolicy.IsActive(
                isAimSnapOverrideWeapon,
                aimAssist.SnapMode,
                firePressed,
                aimPressedForRamp);

            var nowForRamp = DateTime.UtcNow;
            if (snapRampTrigger && !snapRampTriggerPrev)
            {
                snapRampStartedAt = nowForRamp;
            }

            snapRampTriggerPrev = snapRampTrigger;
            var snapRampTime = aimAssist.SnapStrengthRampTime;
            float fireStrengthRampMultiplier;
            if (!snapRampTrigger || snapRampTime <= 0f)
            {
                fireStrengthRampMultiplier = 1f;
            }
            else
            {
                var rampElapsedSeconds = (float)(nowForRamp - snapRampStartedAt).TotalSeconds;
                fireStrengthRampMultiplier = Math.Clamp(rampElapsedSeconds / snapRampTime, 0f, 1f);
            }

            var mapped = new MappedGamepadState(input);

            short ResolveFireBindingAnalogValue()
            {
                return GamepadBindingCatalog.IsTriggerBinding(fireBindingIndex)
                    ? GamepadBindingCatalog.GetTriggerValue(fireBindingIndex, input)
                    : short.MaxValue;
            }

            void ApplyMacro()
            {
                var macro = aimAssistConfigState.Macro;
                if (!macro.HasValue)
                {
                    if (lastMacro.HasValue)
                    {
                        macroState.Reset();
                        lastMacro = null;
                    }
                    return;
                }

                if (lastMacro != macro)
                {
                    macroState.Reset();
                    lastMacro = macro;
                }

                ApplySingleMacro(macro.Value, macroState, DateTime.UtcNow);
            }

            void ApplySingleMacro(in MacroRuntimeState macro, MacroExecutionState state, DateTime now)
            {
                var triggerPressed = GamepadBindingCatalog.IsPressed(macro.TriggerBindingIndex, input);
                var delayMs = Math.Clamp(macro.DelayMs, MacroConfigCatalog.MinDelayMs, MacroConfigCatalog.MaxDelayMs);
                var actionDurationMs = Math.Clamp(
                    macro.ActionDurationMs,
                    MacroConfigCatalog.MinActionDurationMs,
                    MacroConfigCatalog.MaxActionDurationMs);

                switch (macro.TriggerMode)
                {
                    case MacroTriggerMode.Press:
                        if (triggerPressed && !state.TriggerWasPressed)
                        {
                            state.PressDelayStartedAt = now;
                        }

                        if (!triggerPressed && state.PressDelayStartedAt.HasValue && !state.ActionPressed)
                        {
                            state.PressDelayStartedAt = null;
                        }

                        if (state.PressDelayStartedAt.HasValue && !state.ActionPressed)
                        {
                            var elapsedMs = (now - state.PressDelayStartedAt.Value).TotalMilliseconds;
                            if (elapsedMs >= delayMs)
                            {
                                state.ActionPressed = true;
                                state.ActionReleaseAt = actionDurationMs > 0
                                    ? now.AddMilliseconds(actionDurationMs)
                                    : now;
                            }
                        }

                        if (state.ActionPressed)
                        {
                            GamepadBindingCatalog.ApplyBinding(ref mapped, macro.ActionBindingIndex, true);
                            if (state.ActionReleaseAt.HasValue && now >= state.ActionReleaseAt.Value)
                            {
                                state.ActionPressed = false;
                                state.PressDelayStartedAt = null;
                                state.ActionReleaseAt = null;
                            }
                        }
                        break;

                    case MacroTriggerMode.Hold:
                        if (triggerPressed)
                        {
                            state.HoldStartedAt ??= now;
                            var holdElapsedMs = (now - state.HoldStartedAt.Value).TotalMilliseconds;
                            if (holdElapsedMs >= delayMs && !state.ActionPressed)
                            {
                                state.ActionPressed = true;
                                state.ActionReleaseAt = actionDurationMs > 0
                                    ? now.AddMilliseconds(actionDurationMs)
                                    : null;
                            }
                        }
                        else
                        {
                            state.HoldStartedAt = null;
                            state.ActionPressed = false;
                            state.ActionReleaseAt = null;
                        }

                        if (state.ActionPressed)
                        {
                            var shouldRelease = !triggerPressed;
                            if (actionDurationMs > 0 && state.ActionReleaseAt.HasValue && now >= state.ActionReleaseAt.Value)
                            {
                                shouldRelease = true;
                            }

                            if (shouldRelease)
                            {
                                state.ActionPressed = false;
                                state.HoldStartedAt = null;
                                state.ActionReleaseAt = null;
                            }
                            else
                            {
                                GamepadBindingCatalog.ApplyBinding(ref mapped, macro.ActionBindingIndex, true);
                            }
                        }
                        break;

                    default:
                        throw new InvalidOperationException($"Unhandled macro trigger mode: {macro.TriggerMode}");
                }

                state.TriggerWasPressed = triggerPressed;
            }

            if (isReleaseFireWeapon)
            {
                if (firePressed)
                {
                    GamepadBindingCatalog.ApplyBinding(ref mapped, fireBindingIndex, false);
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
                        GamepadBindingCatalog.ApplyBinding(ref mapped, fireBindingIndex, true, short.MaxValue);
                    }
                    else
                    {
                        GamepadBindingCatalog.ApplyBinding(ref mapped, fireBindingIndex, false);
                        releasePulseUntil = null;
                    }
                }
            }
            else
            {
                releasePrevPressed = false;
                releasePulseUntil = null;
            }

            if (shouldApplyRapidFire && firePressed && !isReleaseFireWeapon)
            {
                rapidFireLoop.Tick(DateTime.UtcNow, ResolveRapidFireHalfPeriod(aimAssistConfigState.WeaponPolicy.RapidFireHz));
                if (rapidFireLoop.IsHigh)
                {
                    GamepadBindingCatalog.ApplyBinding(ref mapped, fireBindingIndex, true, ResolveFireBindingAnalogValue());
                }
                else
                {
                    GamepadBindingCatalog.ApplyBinding(ref mapped, fireBindingIndex, false);
                }
            }
            else
            {
                rapidFireLoop.Reset(DateTime.UtcNow);
            }

            desiredKeyboardVirtualKeys.Clear();
            var hasTouchpadPoint = input.TouchpadPressed && input.TouchpadFingerCount > 0;
            var touchpadLeftPressed = false;
            var touchpadRightPressed = false;
            if (hasTouchpadPoint)
            {
                var isLeftTouchRegion = input.TouchpadX < 0.5f;
                touchpadLeftPressed = isLeftTouchRegion;
                touchpadRightPressed = !isLeftTouchRegion;
                if (touchpadLeftPressed && GamepadBindingCatalog.IsKeyboardCustomBinding(touchpadLeftBindingIndex))
                {
                    AddResolvedCustomKeyboardVirtualKey(desiredKeyboardVirtualKeys, aimAssistConfigState.Bindings.TouchpadLeftCustomKey);
                }

                if (touchpadRightPressed && GamepadBindingCatalog.IsKeyboardCustomBinding(touchpadRightBindingIndex))
                {
                    AddResolvedCustomKeyboardVirtualKey(desiredKeyboardVirtualKeys, aimAssistConfigState.Bindings.TouchpadRightCustomKey);
                }

                if (!GamepadBindingCatalog.IsKeyboardCustomBinding(touchpadLeftBindingIndex) && touchpadLeftPressed)
                {
                    GamepadBindingCatalog.ApplyBinding(ref mapped, touchpadLeftBindingIndex, true);
                }

                if (!GamepadBindingCatalog.IsKeyboardCustomBinding(touchpadRightBindingIndex) && touchpadRightPressed)
                {
                    GamepadBindingCatalog.ApplyBinding(ref mapped, touchpadRightBindingIndex, true);
                }
            }

            if (GamepadBindingCatalog.IsPressed(voiceBindingIndex, input))
            {
                AddResolvedCustomKeyboardVirtualKey(desiredKeyboardVirtualKeys, aimAssistConfigState.Bindings.VoiceCustomKey);
            }

            ApplyMacro();

            UpdateKeyboardInjections(desiredKeyboardVirtualKeys, injectedKeyboardVirtualKeys);

            var aimAssistResult = _smartCoreAimAssistService.Evaluate(new SmartCoreAimAssistContext(
                aimAssist.IsEnabled,
                aimAssist.SnapMode,
                aimAssist.SnapOuterRange,
                aimAssist.SnapInnerRange,
                aimAssist.SnapOuterStrength,
                aimAssist.SnapInnerStrength,
                aimAssist.SnapStartStrength,
                aimAssist.SnapVerticalStrengthFactor,
                aimAssist.SnapHipfireStrengthFactor,
                aimAssist.SnapHeight,
                aimAssist.SnapInnerInterpolationTypeIndex,
                bindings.AimBindingIndex,
                bindings.FireBindingIndex,
                isAimSnapOverrideWeapon,
                fireStrengthRampMultiplier,
                input,
                GetFreshDetectionBoxes(in aimAssistDetectionState)));

            var outputState = new ControllerOutputState(
                input.LeftX,
                InvertStickY(input.LeftY),
                CombineStickAxis(input.RightX, aimAssistResult.IsActive ? aimAssistResult.RightX : (short)0),
                InvertStickY(CombineStickAxis(input.RightY, aimAssistResult.IsActive ? aimAssistResult.RightY : (short)0)),
                ToXboxTrigger(mapped.LeftTrigger),
                ToXboxTrigger(mapped.RightTrigger),
                mapped.A,
                mapped.B,
                mapped.X,
                mapped.Y,
                mapped.Back,
                mapped.Start,
                mapped.Guide,
                mapped.LeftShoulder,
                mapped.RightShoulder,
                mapped.LeftThumb,
                mapped.RightThumb,
                mapped.DpadUp,
                mapped.DpadDown,
                mapped.DpadLeft,
                mapped.DpadRight);

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

        if (injectedKeyboardVirtualKeys.Count > 0)
        {
            desiredKeyboardVirtualKeys.Clear();
            UpdateKeyboardInjections(desiredKeyboardVirtualKeys, injectedKeyboardVirtualKeys);
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

    private static TimeSpan ResolveRapidFireHalfPeriod(int rapidFireHz)
    {
        var hz = Math.Clamp(rapidFireHz, MinRapidFireHz, MaxRapidFireHz);
        return TimeSpan.FromMilliseconds(500.0 / hz);
    }

    private static OnnxDebugBox[] GetFreshDetectionBoxes(in SmartCoreDetectionState state)
    {
        if (state.Boxes.Length == 0)
        {
            return Array.Empty<OnnxDebugBox>();
        }

        var ageMs = (DateTime.UtcNow - state.ReceivedAtUtc).TotalMilliseconds;
        return ageMs <= DetectionStaleAfterMs ? state.Boxes : Array.Empty<OnnxDebugBox>();
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
        return a.AimAssist == b.AimAssist &&
               a.Bindings == b.Bindings &&
               a.WeaponPolicy.RapidFireStrategy == b.WeaponPolicy.RapidFireStrategy &&
               a.WeaponPolicy.RapidFireHz == b.WeaponPolicy.RapidFireHz &&
               AreSameList(a.WeaponPolicy.AimSnapWeapons, b.WeaponPolicy.AimSnapWeapons) &&
               AreSameList(a.WeaponPolicy.RapidFireWeapons, b.WeaponPolicy.RapidFireWeapons) &&
               AreSameList(a.WeaponPolicy.ReleaseFireWeapons, b.WeaponPolicy.ReleaseFireWeapons) &&
               a.Macro == b.Macro;
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

}

