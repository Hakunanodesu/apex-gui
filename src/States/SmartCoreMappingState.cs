internal sealed class SmartCoreMappingState
{
    public bool RequestedEnabled { get; set; }

    public bool IsViGemBusReady { get; set; }

    public bool HasInputDevice { get; set; }

    public bool IsDependenciesReady => IsViGemBusReady && HasInputDevice;

    public bool IsEnabled { get; set; }

    public bool IsMappingActive { get; set; }

    public int EffectiveSelectedIndex { get; set; } = -1;

    public uint? EffectiveSelectedInstanceId { get; set; }

    public string LastError { get; set; } = string.Empty;
}
