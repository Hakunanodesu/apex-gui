internal sealed class HomeViewState
{
    public int SnapModeIndex { get; set; } = -1;
    public int AimBindingIndex { get; set; } = GamepadBindingCatalog.DefaultAimIndex;
    public int FireBindingIndex { get; set; } = GamepadBindingCatalog.DefaultFireIndex;
    public int SnapOuterRange { get; set; } = 1;
    public float SnapOuterStrength { get; set; }
    public int SnapInnerRange { get; set; } = 1;
    public float SnapInnerStrength { get; set; }
    public float SnapStartStrength { get; set; }
    public float SnapVerticalStrengthFactor { get; set; }
    public float SnapHipfireStrengthFactor { get; set; }
    public float SnapHeight { get; set; }
    public int SnapInnerInterpolationTypeIndex { get; set; }
    public string AddNameBuffer { get; set; } = string.Empty;
    public string AddError { get; set; } = string.Empty;
    public bool IsAddModalOpen { get; set; }
    public bool IsDeleteModalOpen { get; set; }
    public bool IsAddModalOpenRequested { get; set; }
    public bool IsDeleteModalOpenRequested { get; set; }
    public string? PendingDeleteConfigBaseName { get; set; }

    public void ApplySnapConfig(SnapConfigState snapConfig)
    {
        SnapOuterRange = snapConfig.OuterRange;
        SnapInnerRange = snapConfig.InnerRange;
        SnapOuterStrength = snapConfig.OuterStrength;
        SnapInnerStrength = snapConfig.InnerStrength;
        SnapStartStrength = snapConfig.StartStrength;
        SnapVerticalStrengthFactor = snapConfig.VerticalStrengthFactor;
        SnapHipfireStrengthFactor = snapConfig.HipfireStrengthFactor;
        SnapHeight = snapConfig.Height;
        SnapInnerInterpolationTypeIndex = snapConfig.InnerInterpolationTypeIndex;
    }

    public void ResetSnapSettings(
        int snapModeIndex,
        int aimBindingIndex,
        int fireBindingIndex,
        int outerRange,
        int innerRange,
        float outerStrength,
        float innerStrength,
        float startStrength,
        float verticalStrengthFactor,
        float hipfireStrengthFactor,
        float height,
        int interpolationTypeIndex)
    {
        SnapModeIndex = snapModeIndex;
        AimBindingIndex = aimBindingIndex;
        FireBindingIndex = fireBindingIndex;
        SnapOuterRange = outerRange;
        SnapInnerRange = innerRange;
        SnapOuterStrength = outerStrength;
        SnapInnerStrength = innerStrength;
        SnapStartStrength = startStrength;
        SnapVerticalStrengthFactor = verticalStrengthFactor;
        SnapHipfireStrengthFactor = hipfireStrengthFactor;
        SnapHeight = height;
        SnapInnerInterpolationTypeIndex = interpolationTypeIndex;
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
