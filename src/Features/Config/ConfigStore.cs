using System.Text.Json.Nodes;

internal readonly record struct ConfigRefreshResult(IReadOnlyList<string> ConfigFiles, int SelectedIndex);

internal readonly record struct ConfigSelectionResult(
    bool HasConfig,
    int SnapModeIndex,
    int RapidFireStrategyIndex,
    int RapidFireHz,
    int GameIndex,
    int ModelIndex,
    BindingConfigState Bindings,
    SnapSettingsState Snap)
{
    public static ConfigSelectionResult Empty => new(
        false,
        -1,
        -1,
        25,
        0,
        -1,
        BindingConfigState.Default,
        SnapSettingsState.Default);
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
        IReadOnlyList<string> snapModeOptions,
        IReadOnlyList<string> rapidFireStrategyOptions,
        IReadOnlyList<string> gameOptions,
        int defaultRapidFireHz)
    {
        if (!TryResolvePath(configFiles, selectedConfigFileIndex, out var configPath))
        {
            return ConfigSelectionResult.Empty;
        }

        var snapModeIndex = ResolveOptionIndex(
            _repository.TryReadString(configPath, SnapConfigCatalog.SnapModeKey),
            snapModeOptions,
            0);
        var rapidFireStrategyIndex = ResolveOptionIndex(
            _repository.TryReadString(configPath, AimAssistOptionCatalog.RapidFireStrategyKey),
            rapidFireStrategyOptions,
            2);
        var rapidFireHz = Math.Clamp(_repository.TryReadInt(configPath, AimAssistOptionCatalog.RapidFireHzKey) ?? defaultRapidFireHz, 1, 30);
        var gameIndex = ResolveOptionIndex(
            _repository.TryReadString(configPath, WeaponTemplateCatalog.GameConfigKey),
            gameOptions,
            0);
        var bindings = BindingConfigCatalog.Read(_repository, configPath);
        var modelIndex = onnxModels.Count == 0
            ? -1
            : ResolveModelIndex(_repository.TryReadString(configPath, OnnxModelConfigLoader.ModelConfigKey), onnxModels);
        var selectedModelSize = modelIndex >= 0 && modelIndex < onnxModels.Count
            ? Math.Max(1, onnxModels[modelIndex].InputHeight)
            : SnapSettingsState.Default.OuterRange;
        var snapConfig = SnapConfigCatalog.Read(_repository, configPath, selectedModelSize, displayHeightLimit);

        return new ConfigSelectionResult(
            true,
            snapModeIndex,
            rapidFireStrategyIndex,
            rapidFireHz,
            gameIndex,
            modelIndex,
            bindings,
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

    public void TryWriteBool(IReadOnlyList<string> configFiles, int selectedConfigFileIndex, string key, bool value)
    {
        if (TryResolvePath(configFiles, selectedConfigFileIndex, out var configPath))
        {
            _repository.TryWriteBool(configPath, key, value);
        }
    }

    public void TryRemoveKey(IReadOnlyList<string> configFiles, int selectedConfigFileIndex, string key)
    {
        if (TryResolvePath(configFiles, selectedConfigFileIndex, out var configPath))
        {
            _repository.TryRemoveKey(configPath, key);
        }
    }

    public void TryWriteSnap(IReadOnlyList<string> configFiles, int selectedConfigFileIndex, SnapSettingsState snap)
    {
        if (TryResolvePath(configFiles, selectedConfigFileIndex, out var configPath))
        {
            SnapConfigCatalog.Write(_repository, configPath, snap);
        }
    }

    public void TryWriteBindings(IReadOnlyList<string> configFiles, int selectedConfigFileIndex, BindingConfigState bindings)
    {
        if (TryResolvePath(configFiles, selectedConfigFileIndex, out var configPath))
        {
            BindingConfigCatalog.Write(_repository, configPath, bindings);
        }
    }

    public void LoadSpecialWeaponLogic(
        IReadOnlyList<string> configFiles,
        int selectedConfigFileIndex,
        string gameKey,
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
            var specialWeaponLogicRoot = EnsureRoot(root, SpecialWeaponLogicCatalog.RootKey);
            var gameLogicRoot = EnsureRoot(specialWeaponLogicRoot, gameKey);
            var hasAnyChanges = false;
            var hasAimSnapList = TryApplyEnabledWeaponListFromNode(gameLogicRoot[SpecialWeaponLogicCatalog.AimSnapWeaponListKey], aimSnapFlags, weaponNames);
            var hasRapidFireList = TryApplyEnabledWeaponListFromNode(gameLogicRoot[SpecialWeaponLogicCatalog.RapidFireWeaponListKey], rapidFireFlags, weaponNames);
            var hasReleaseFireList = TryApplyEnabledWeaponListFromNode(gameLogicRoot[SpecialWeaponLogicCatalog.ReleaseFireWeaponListKey], releaseFireFlags, weaponNames);

            if (gameLogicRoot[SpecialWeaponLogicCatalog.AimSnapWeaponListKey] is not JsonArray)
            {
                gameLogicRoot[SpecialWeaponLogicCatalog.AimSnapWeaponListKey] = BuildEnabledWeaponListNode(aimSnapFlags, weaponNames);
                hasAnyChanges = true;
            }

            if (gameLogicRoot[SpecialWeaponLogicCatalog.RapidFireWeaponListKey] is not JsonArray)
            {
                gameLogicRoot[SpecialWeaponLogicCatalog.RapidFireWeaponListKey] = BuildEnabledWeaponListNode(rapidFireFlags, weaponNames);
                hasAnyChanges = true;
            }

            if (gameLogicRoot[SpecialWeaponLogicCatalog.ReleaseFireWeaponListKey] is not JsonArray)
            {
                gameLogicRoot[SpecialWeaponLogicCatalog.ReleaseFireWeaponListKey] = BuildEnabledWeaponListNode(releaseFireFlags, weaponNames);
                hasAnyChanges = true;
            }

            if (hasAimSnapList && gameLogicRoot[SpecialWeaponLogicCatalog.AimSnapWeaponListKey] is JsonArray)
            {
                gameLogicRoot[SpecialWeaponLogicCatalog.AimSnapWeaponListKey] = BuildEnabledWeaponListNode(aimSnapFlags, weaponNames);
            }

            if (hasRapidFireList && gameLogicRoot[SpecialWeaponLogicCatalog.RapidFireWeaponListKey] is JsonArray)
            {
                gameLogicRoot[SpecialWeaponLogicCatalog.RapidFireWeaponListKey] = BuildEnabledWeaponListNode(rapidFireFlags, weaponNames);
            }

            if (hasReleaseFireList && gameLogicRoot[SpecialWeaponLogicCatalog.ReleaseFireWeaponListKey] is JsonArray)
            {
                gameLogicRoot[SpecialWeaponLogicCatalog.ReleaseFireWeaponListKey] = BuildEnabledWeaponListNode(releaseFireFlags, weaponNames);
            }

            for (var i = 0; i < weaponNames.Count; i++)
            {
                if (!rapidFireFlags[i] || !releaseFireFlags[i])
                {
                    continue;
                }

                rapidFireFlags[i] = false;
                hasAnyChanges = true;
            }

            if (hasAnyChanges)
            {
                gameLogicRoot[SpecialWeaponLogicCatalog.RapidFireWeaponListKey] = BuildEnabledWeaponListNode(rapidFireFlags, weaponNames);
                gameLogicRoot[SpecialWeaponLogicCatalog.ReleaseFireWeaponListKey] = BuildEnabledWeaponListNode(releaseFireFlags, weaponNames);
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

    public MacroEntryState LoadMacro(
        IReadOnlyList<string> configFiles,
        int selectedConfigFileIndex)
    {
        if (!TryResolvePath(configFiles, selectedConfigFileIndex, out var configPath))
        {
            return MacroConfigCatalog.CreateDefault();
        }

        try
        {
            var root = _repository.LoadJsonObjectOrEmpty(configPath);
            if (root[MacroConfigCatalog.ConfigKey] is JsonObject macroNode &&
                MacroConfigCatalog.TryReadEntry(macroNode, out var entry))
            {
                return entry;
            }
        }
        catch
        {
            // Fall through to default on read failure.
        }

        return MacroConfigCatalog.CreateDefault();
    }

    public void TryWriteMacro(
        IReadOnlyList<string> configFiles,
        int selectedConfigFileIndex,
        MacroEntryState macro)
    {
        if (!TryResolvePath(configFiles, selectedConfigFileIndex, out var configPath))
        {
            return;
        }

        try
        {
            var root = _repository.LoadJsonObjectOrEmpty(configPath);
            root[MacroConfigCatalog.ConfigKey] = MacroConfigCatalog.ToJsonObject(macro);
            _repository.SaveJsonObject(configPath, root);
        }
        catch
        {
            // Keep UI responsive if file IO fails.
        }
    }

    public void TryWriteSpecialWeaponLogic(
        IReadOnlyList<string> configFiles,
        int selectedConfigFileIndex,
        string gameKey,
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
            var specialWeaponLogicRoot = EnsureRoot(root, SpecialWeaponLogicCatalog.RootKey);
            var gameLogicRoot = EnsureRoot(specialWeaponLogicRoot, gameKey);
            aimSnapFlags[weaponIndex] = aimSnapEnabled;
            rapidFireFlags[weaponIndex] = rapidFireEnabled;
            releaseFireFlags[weaponIndex] = releaseFireEnabled;
            gameLogicRoot[SpecialWeaponLogicCatalog.AimSnapWeaponListKey] = BuildEnabledWeaponListNode(aimSnapFlags, weaponNames);
            gameLogicRoot[SpecialWeaponLogicCatalog.RapidFireWeaponListKey] = BuildEnabledWeaponListNode(rapidFireFlags, weaponNames);
            gameLogicRoot[SpecialWeaponLogicCatalog.ReleaseFireWeaponListKey] = BuildEnabledWeaponListNode(releaseFireFlags, weaponNames);
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
