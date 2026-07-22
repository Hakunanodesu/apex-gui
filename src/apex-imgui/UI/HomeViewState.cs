internal sealed class HomeViewState
{
    public int SnapModeIndex { get; set; } = -1;
    public int RapidFireStrategyIndex { get; set; } = -1;
    public int RapidFireHz { get; set; } = 30;
    public int GameIndex { get; set; }
    public int AimBindingIndex { get; set; } = GamepadBindingCatalog.DefaultAimIndex;
    public int FireBindingIndex { get; set; } = GamepadBindingCatalog.DefaultFireIndex;
    public int VoiceBindingIndex { get; set; } = GamepadBindingCatalog.DefaultTouchpadLeftIndex;
    public string VoiceCustomKey { get; set; } = "V";
    public int TouchpadLeftBindingIndex { get; set; } = GamepadBindingCatalog.DefaultTouchpadLeftIndex;
    public int TouchpadRightBindingIndex { get; set; } = GamepadBindingCatalog.DefaultTouchpadRightIndex;
    public string TouchpadLeftCustomKey { get; set; } = GamepadBindingCatalog.DefaultCustomKeyboardKeyName;
    public string TouchpadRightCustomKey { get; set; } = GamepadBindingCatalog.DefaultCustomKeyboardKeyName;
    public int SnapOuterRange { get; set; } = 1;
    public float SnapOuterStrength { get; set; }
    public int SnapInnerRange { get; set; } = 1;
    public float SnapInnerStrength { get; set; }
    public float SnapStartStrength { get; set; }
    public float SnapVerticalStrengthFactor { get; set; }
    public float SnapHipfireStrengthFactor { get; set; }
    public float SnapHeight { get; set; }
    public float SnapStrengthRampTime { get; set; }
    public int SnapInnerInterpolationTypeIndex { get; set; }
    public string AddNameBuffer { get; set; } = string.Empty;
    public string AddError { get; set; } = string.Empty;
    public bool IsAddModalOpen { get; set; }
    public bool IsDeleteModalOpen { get; set; }
    public bool IsAddModalOpenRequested { get; set; }
    public bool IsDeleteModalOpenRequested { get; set; }
    public string? PendingDeleteConfigBaseName { get; set; }
    public MacroEntryState Macro { get; set; } = MacroConfigCatalog.CreateDefault();

    public void ApplySnapConfig(SnapSettingsState snapConfig)
    {
        SnapOuterRange = snapConfig.OuterRange;
        SnapInnerRange = snapConfig.InnerRange;
        SnapOuterStrength = snapConfig.OuterStrength;
        SnapInnerStrength = snapConfig.InnerStrength;
        SnapStartStrength = snapConfig.StartStrength;
        SnapVerticalStrengthFactor = snapConfig.VerticalStrengthFactor;
        SnapHipfireStrengthFactor = snapConfig.HipfireStrengthFactor;
        SnapHeight = snapConfig.Height;
        SnapStrengthRampTime = snapConfig.StrengthRampTime;
        SnapInnerInterpolationTypeIndex = snapConfig.InnerInterpolationTypeIndex;
    }

    public void ApplyBindings(BindingConfigState bindings)
    {
        AimBindingIndex = bindings.AimBindingIndex;
        FireBindingIndex = bindings.FireBindingIndex;
        VoiceBindingIndex = bindings.VoiceBindingIndex;
        VoiceCustomKey = bindings.VoiceCustomKey;
        TouchpadLeftBindingIndex = bindings.TouchpadLeftBindingIndex;
        TouchpadRightBindingIndex = bindings.TouchpadRightBindingIndex;
        TouchpadLeftCustomKey = bindings.TouchpadLeftCustomKey;
        TouchpadRightCustomKey = bindings.TouchpadRightCustomKey;
    }

    public BindingConfigState ToBindings() => new(
        AimBindingIndex,
        FireBindingIndex,
        VoiceBindingIndex,
        VoiceCustomKey,
        TouchpadLeftBindingIndex,
        TouchpadRightBindingIndex,
        TouchpadLeftCustomKey,
        TouchpadRightCustomKey);

    public SnapSettingsState ToSnapSettings() => new(
        SnapOuterRange,
        SnapInnerRange,
        SnapOuterStrength,
        SnapInnerStrength,
        SnapStartStrength,
        SnapVerticalStrengthFactor,
        SnapHipfireStrengthFactor,
        SnapHeight,
        SnapStrengthRampTime,
        SnapInnerInterpolationTypeIndex);

    public void ResetSnapSettings(
        int snapModeIndex,
        int rapidFireStrategyIndex,
        int rapidFireHz,
        int aimBindingIndex,
        int fireBindingIndex,
        int voiceBindingIndex,
        string voiceCustomKey,
        int touchpadLeftBindingIndex,
        int touchpadRightBindingIndex,
        string touchpadLeftCustomKey,
        string touchpadRightCustomKey)
    {
        SnapModeIndex = snapModeIndex;
        RapidFireStrategyIndex = rapidFireStrategyIndex;
        RapidFireHz = rapidFireHz;
        AimBindingIndex = aimBindingIndex;
        FireBindingIndex = fireBindingIndex;
        VoiceBindingIndex = voiceBindingIndex;
        VoiceCustomKey = voiceCustomKey;
        TouchpadLeftBindingIndex = touchpadLeftBindingIndex;
        TouchpadRightBindingIndex = touchpadRightBindingIndex;
        TouchpadLeftCustomKey = touchpadLeftCustomKey;
        TouchpadRightCustomKey = touchpadRightCustomKey;
        ApplySnapConfig(SnapSettingsState.Default);
    }

    public void OpenAddModal()
    {
        AddNameBuffer = string.Empty;
        AddError = string.Empty;
        IsAddModalOpen = true;
        IsAddModalOpenRequested = true;
    }

    public void CloseAddModal()
    {
        AddError = string.Empty;
        IsAddModalOpen = false;
    }

    public void OpenDeleteModal(string baseName)
    {
        PendingDeleteConfigBaseName = baseName;
        IsDeleteModalOpen = true;
        IsDeleteModalOpenRequested = true;
    }

    public void CloseDeleteModal()
    {
        PendingDeleteConfigBaseName = null;
        IsDeleteModalOpen = false;
    }
}
