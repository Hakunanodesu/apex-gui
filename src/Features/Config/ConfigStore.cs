using System.Text.Json.Nodes;

internal sealed class SnapConfigState
{
    public int OuterRange { get; init; }
    public int InnerRange { get; init; }
    public float OuterStrength { get; init; }
    public float InnerStrength { get; init; }
    public float StartStrength { get; init; }
    public float VerticalStrengthFactor { get; init; }
    public float HipfireStrengthFactor { get; init; }
    public float Height { get; init; }
    public int InnerInterpolationTypeIndex { get; init; }
}

internal readonly record struct ConfigRefreshResult(IReadOnlyList<string> ConfigFiles, int SelectedIndex);

internal readonly record struct ConfigSelectionResult(
    bool HasConfig,
    int SnapModeIndex,
    int ModelIndex,
    int AimBindingIndex,
    int FireBindingIndex,
    int TouchpadLeftBindingIndex,
    int TouchpadRightBindingIndex,
    SnapConfigState SnapConfig)
{
    public static ConfigSelectionResult Empty => new(
        false,
        -1,
        -1,
        GamepadBindingCatalog.DefaultAimIndex,
        GamepadBindingCatalog.DefaultFireIndex,
        GamepadBindingCatalog.DefaultTouchpadLeftIndex,
        GamepadBindingCatalog.DefaultTouchpadRightIndex,
        new SnapConfigState());
}

internal sealed class ConfigStore
{
    private readonly ConfigRepository _repository;

    public ConfigStore(ConfigRepository repository)
    {
        _repository = repository;
    }

    public ConfigRefreshResult RefreshConfigFiles(IReadOnlyList<string> currentConfigFiles, int currentSelectedIndex, string? forceSelectBaseName)
    {
        var oldSelection = currentConfigFiles.Count > 0 && currentSelectedIndex >= 0 && currentSelectedIndex < currentConfigFiles.Count
            ? currentConfigFiles[currentSelectedIndex]
            : null;

        var configFiles = _repository.EnumerateConfigBaseNames();
        if (configFiles.Count == 0)
        {
            _repository.ClearCurrentConfigPointerFile();
            return new ConfigRefreshResult(configFiles, 0);
        }

        var persistedName = _repository.TryReadCurrentConfigFileName();
        var selectedIndex = _repository.ResolveSelectedIndex(
            configFiles,
            currentSelectedIndex,
            forceSelectBaseName,
            oldSelection,
            persistedName);

        if (!string.IsNullOrWhiteSpace(forceSelectBaseName))
        {
            _repository.WriteCurrentConfigFileName(configFiles[selectedIndex]);
        }

        return new ConfigRefreshResult(configFiles, selectedIndex);
    }

    public ConfigSelectionResult ApplyCurrentConfigSelection(
        IReadOnlyList<string> configFiles,
        int selectedConfigFileIndex,
        IReadOnlyList<OnnxModelConfig> onnxModels,
        int displayHeightLimit,
        int defaultOuterRange,
        int defaultInnerRange,
        float defaultOuterStrength,
        float defaultInnerStrength,
        float defaultStartStrength,
        float defaultVerticalStrengthFactor,
        float defaultHipfireStrengthFactor,
        float defaultHeight,
        IReadOnlyList<string> snapModeOptions,
        IReadOnlyList<string> interpolationOptions,
        IReadOnlyList<string> bindingOptions,
        IReadOnlyList<string> touchpadBindingOptions,
        int defaultAimBindingIndex,
        int defaultFireBindingIndex,
        int defaultTouchpadLeftBindingIndex,
        int defaultTouchpadRightBindingIndex)
    {
        if (!TryResolvePath(configFiles, selectedConfigFileIndex, out var configPath))
        {
            return ConfigSelectionResult.Empty;
        }

        var snapModeIndex = ResolveOptionIndex(
            _repository.TryReadString(configPath, "snap"),
            snapModeOptions,
            0);
        var aimBindingIndex = ResolveOptionIndex(
            _repository.TryReadString(configPath, "aimBinding"),
            bindingOptions,
            defaultAimBindingIndex);
        var fireBindingIndex = ResolveOptionIndex(
            _repository.TryReadString(configPath, "fireBinding"),
            bindingOptions,
            defaultFireBindingIndex);
        var touchpadLeftBindingIndex = ResolveOptionIndex(
            _repository.TryReadString(configPath, "touchpadLeftBinding"),
            touchpadBindingOptions,
            defaultTouchpadLeftBindingIndex);
        var touchpadRightBindingIndex = ResolveOptionIndex(
            _repository.TryReadString(configPath, "touchpadRightBinding"),
            touchpadBindingOptions,
            defaultTouchpadRightBindingIndex);
        var modelIndex = onnxModels.Count == 0
            ? -1
            : ResolveModelIndex(_repository.TryReadString(configPath, "model"), onnxModels);
        var selectedModelSize = modelIndex >= 0 && modelIndex < onnxModels.Count
            ? Math.Max(1, onnxModels[modelIndex].InputHeight)
            : defaultOuterRange;
        var snapConfig = ReadSnapConfig(
            configPath,
            selectedModelSize,
            displayHeightLimit,
            defaultOuterRange,
            defaultInnerRange,
            defaultOuterStrength,
            defaultInnerStrength,
            defaultStartStrength,
            defaultVerticalStrengthFactor,
            defaultHipfireStrengthFactor,
            defaultHeight,
            interpolationOptions);

        return new ConfigSelectionResult(
            true,
            snapModeIndex,
            modelIndex,
            aimBindingIndex,
            fireBindingIndex,
            touchpadLeftBindingIndex,
            touchpadRightBindingIndex,
            snapConfig);
    }

    public bool TryResolvePath(IReadOnlyList<string> configFiles, int selectedConfigFileIndex, out string configPath)
    {
        configPath = string.Empty;
        if (configFiles.Count == 0)
        {
            return false;
        }

        var configIndex = Math.Clamp(selectedConfigFileIndex, 0, configFiles.Count - 1);
        configPath = _repository.GetConfigPath(configFiles[configIndex]);
        return true;
    }

    public string? TryReadString(IReadOnlyList<string> configFiles, int selectedConfigFileIndex, string key)
    {
        return TryResolvePath(configFiles, selectedConfigFileIndex, out var configPath)
            ? _repository.TryReadString(configPath, key)
            : null;
    }

    public void TryWriteString(IReadOnlyList<string> configFiles, int selectedConfigFileIndex, string key, string value)
    {
        if (TryResolvePath(configFiles, selectedConfigFileIndex, out var configPath))
        {
            _repository.TryWriteString(configPath, key, value);
        }
    }

    public void TryWriteInt(IReadOnlyList<string> configFiles, int selectedConfigFileIndex, string key, int value)
    {
        if (TryResolvePath(configFiles, selectedConfigFileIndex, out var configPath))
        {
            _repository.TryWriteInt(configPath, key, value);
        }
    }

    public void TryWriteFloat(IReadOnlyList<string> configFiles, int selectedConfigFileIndex, string key, float value)
    {
        if (TryResolvePath(configFiles, selectedConfigFileIndex, out var configPath))
        {
            _repository.TryWriteFloat(configPath, key, value);
        }
    }

    public void TryRemoveKey(IReadOnlyList<string> configFiles, int selectedConfigFileIndex, string key)
    {
        if (TryResolvePath(configFiles, selectedConfigFileIndex, out var configPath))
        {
            _repository.TryRemoveKey(configPath, key);
        }
    }

    public void LoadSpecialWeaponLogic(
        IReadOnlyList<string> configFiles,
        int selectedConfigFileIndex,
        string rootKey,
        string aimSnapListKey,
        string rapidFireListKey,
        string releaseFireListKey,
        IReadOnlyList<string> weaponNames,
        bool[] aimSnapFlags,
        bool[] rapidFireFlags,
        bool[] releaseFireFlags)
    {
        Array.Clear(aimSnapFlags);
        Array.Clear(rapidFireFlags);
        Array.Clear(releaseFireFlags);

        if (!TryResolvePath(configFiles, selectedConfigFileIndex, out var configPath))
        {
            return;
        }

        try
        {
            var root = _repository.LoadJsonObjectOrEmpty(configPath);
            var specialWeaponLogicRoot = EnsureRoot(root, rootKey);
            var hasAnyChanges = false;
            var hasAimSnapList = TryApplyEnabledWeaponListFromNode(specialWeaponLogicRoot[aimSnapListKey], aimSnapFlags, weaponNames);
            var hasRapidFireList = TryApplyEnabledWeaponListFromNode(specialWeaponLogicRoot[rapidFireListKey], rapidFireFlags, weaponNames);
            var hasReleaseFireList = TryApplyEnabledWeaponListFromNode(specialWeaponLogicRoot[releaseFireListKey], releaseFireFlags, weaponNames);

            if (specialWeaponLogicRoot[aimSnapListKey] is not JsonArray)
            {
                specialWeaponLogicRoot[aimSnapListKey] = BuildEnabledWeaponListNode(aimSnapFlags, weaponNames);
                hasAnyChanges = true;
            }

            if (specialWeaponLogicRoot[rapidFireListKey] is not JsonArray)
            {
                specialWeaponLogicRoot[rapidFireListKey] = BuildEnabledWeaponListNode(rapidFireFlags, weaponNames);
                hasAnyChanges = true;
            }

            if (specialWeaponLogicRoot[releaseFireListKey] is not JsonArray)
            {
                specialWeaponLogicRoot[releaseFireListKey] = BuildEnabledWeaponListNode(releaseFireFlags, weaponNames);
                hasAnyChanges = true;
            }

            if (hasAimSnapList && specialWeaponLogicRoot[aimSnapListKey] is JsonArray)
            {
                specialWeaponLogicRoot[aimSnapListKey] = BuildEnabledWeaponListNode(aimSnapFlags, weaponNames);
            }

            if (hasRapidFireList && specialWeaponLogicRoot[rapidFireListKey] is JsonArray)
            {
                specialWeaponLogicRoot[rapidFireListKey] = BuildEnabledWeaponListNode(rapidFireFlags, weaponNames);
            }

            if (hasReleaseFireList && specialWeaponLogicRoot[releaseFireListKey] is JsonArray)
            {
                specialWeaponLogicRoot[releaseFireListKey] = BuildEnabledWeaponListNode(releaseFireFlags, weaponNames);
            }

            if (hasAnyChanges)
            {
                _repository.SaveJsonObject(configPath, root);
            }
        }
        catch
        {
            Array.Clear(aimSnapFlags);
            Array.Clear(rapidFireFlags);
            Array.Clear(releaseFireFlags);
        }
    }

    public void TryWriteSpecialWeaponLogic(
        IReadOnlyList<string> configFiles,
        int selectedConfigFileIndex,
        string rootKey,
        string aimSnapListKey,
        string rapidFireListKey,
        string releaseFireListKey,
        IReadOnlyList<string> weaponNames,
        int weaponIndex,
        bool aimSnapEnabled,
        bool rapidFireEnabled,
        bool releaseFireEnabled,
        bool[] aimSnapFlags,
        bool[] rapidFireFlags,
        bool[] releaseFireFlags)
    {
        if (!TryResolvePath(configFiles, selectedConfigFileIndex, out var configPath))
        {
            return;
        }

        if (weaponIndex < 0 || weaponIndex >= weaponNames.Count)
        {
            return;
        }

        try
        {
            var root = _repository.LoadJsonObjectOrEmpty(configPath);
            var specialWeaponLogicRoot = EnsureRoot(root, rootKey);
            aimSnapFlags[weaponIndex] = aimSnapEnabled;
            rapidFireFlags[weaponIndex] = rapidFireEnabled;
            releaseFireFlags[weaponIndex] = releaseFireEnabled;
            specialWeaponLogicRoot[aimSnapListKey] = BuildEnabledWeaponListNode(aimSnapFlags, weaponNames);
            specialWeaponLogicRoot[rapidFireListKey] = BuildEnabledWeaponListNode(rapidFireFlags, weaponNames);
            specialWeaponLogicRoot[releaseFireListKey] = BuildEnabledWeaponListNode(releaseFireFlags, weaponNames);
            _repository.SaveJsonObject(configPath, root);
        }
        catch
        {
            // Keep UI responsive if file IO fails.
        }
    }

    private int ResolveOptionIndex(string? value, IReadOnlyList<string> options, int fallback = 0)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return fallback;
        }

        for (var i = 0; i < options.Count; i++)
        {
            if (string.Equals(options[i], value, StringComparison.OrdinalIgnoreCase))
            {
                return i;
            }
        }

        return fallback;
    }

    private int ResolveModelIndex(string? modelName, IReadOnlyList<OnnxModelConfig> models)
    {
        if (string.IsNullOrWhiteSpace(modelName))
        {
            return -1;
        }

        for (var i = 0; i < models.Count; i++)
        {
            if (string.Equals(models[i].DisplayName, modelName, StringComparison.OrdinalIgnoreCase))
            {
                return i;
            }
        }

        return -1;
    }

    private SnapConfigState ReadSnapConfig(
        string configPath,
        int selectedModelSize,
        int displayHeightLimit,
        int defaultOuterRange,
        int defaultInnerRange,
        float defaultOuterStrength,
        float defaultInnerStrength,
        float defaultStartStrength,
        float defaultVerticalStrengthFactor,
        float defaultHipfireStrengthFactor,
        float defaultHeight,
        IReadOnlyList<string> interpolationOptions)
    {
        var snapOuterRangeMax = Math.Max(selectedModelSize, displayHeightLimit);
        var outerRange = Math.Clamp(_repository.TryReadInt(configPath, "snapOuterRange") ?? defaultOuterRange, selectedModelSize, snapOuterRangeMax);
        var innerRange = Math.Clamp(_repository.TryReadInt(configPath, "snapInnerRange") ?? defaultInnerRange, 1, outerRange);
        var outerStrength = Math.Clamp(_repository.TryReadFloat(configPath, "snapOuterStrength") ?? defaultOuterStrength, 0f, 1f);
        var innerStrength = Math.Clamp(_repository.TryReadFloat(configPath, "snapInnerStrength") ?? defaultInnerStrength, 0f, 1f);
        var startStrength = Math.Clamp(_repository.TryReadFloat(configPath, "snapStartStrength") ?? defaultStartStrength, 0f, 1f);
        var verticalStrengthFactor = Math.Clamp(_repository.TryReadFloat(configPath, "snapVerticalStrengthFactor") ?? defaultVerticalStrengthFactor, 0f, 1f);
        var hipfireStrengthFactor = Math.Clamp(_repository.TryReadFloat(configPath, "snapHipfireStrengthFactor") ?? defaultHipfireStrengthFactor, 0f, 1f);
        var height = Math.Clamp(_repository.TryReadFloat(configPath, "snapHeight") ?? defaultHeight, 0f, 1f);
        var interpolationTypeIndex = ResolveOptionIndex(
            _repository.TryReadString(configPath, "snapInnerInterpolationType"),
            interpolationOptions,
            0);

        return new SnapConfigState
        {
            OuterRange = outerRange,
            InnerRange = innerRange,
            OuterStrength = outerStrength,
            InnerStrength = innerStrength,
            StartStrength = startStrength,
            VerticalStrengthFactor = verticalStrengthFactor,
            HipfireStrengthFactor = hipfireStrengthFactor,
            Height = height,
            InnerInterpolationTypeIndex = interpolationTypeIndex
        };
    }

    private static JsonObject EnsureRoot(JsonObject root, string key)
    {
        if (root[key] is JsonObject obj)
        {
            return obj;
        }

        obj = new JsonObject();
        root[key] = obj;
        return obj;
    }

    private static JsonArray BuildEnabledWeaponListNode(IReadOnlyList<bool> enabledFlags, IReadOnlyList<string> weaponNames)
    {
        var listNode = new JsonArray();
        for (var i = 0; i < weaponNames.Count; i++)
        {
            if (i < enabledFlags.Count && enabledFlags[i])
            {
                listNode.Add(weaponNames[i]);
            }
        }

        return listNode;
    }

    private static bool TryApplyEnabledWeaponListFromNode(JsonNode? node, bool[] target, IReadOnlyList<string> weaponNames)
    {
        if (node is not JsonArray listNode)
        {
            return false;
        }

        Array.Clear(target);
        foreach (var item in listNode)
        {
            string? weaponName;
            try
            {
                weaponName = item?.GetValue<string>()?.Trim();
            }
            catch
            {
                continue;
            }

            if (string.IsNullOrWhiteSpace(weaponName))
            {
                continue;
            }

            var index = -1;
            for (var i = 0; i < weaponNames.Count; i++)
            {
                if (string.Equals(weaponNames[i], weaponName, StringComparison.OrdinalIgnoreCase))
                {
                    index = i;
                    break;
                }
            }

            if (index >= 0)
            {
                target[index] = true;
            }
        }

        return true;
    }

}
