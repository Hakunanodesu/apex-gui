using System.Text.Json.Nodes;

internal sealed class SpecialWeaponLogicService
{
    public JsonObject EnsureRoot(JsonObject root, string key)
    {
        if (root[key] is JsonObject obj)
        {
            return obj;
        }

        obj = new JsonObject();
        root[key] = obj;
        return obj;
    }

    public JsonArray BuildEnabledWeaponListNode(IReadOnlyList<bool> enabledFlags, IReadOnlyList<string> weaponNames)
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

    public bool TryApplyEnabledWeaponListFromNode(JsonNode? node, bool[] target, IReadOnlyList<string> weaponNames)
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

    public void UpdateSingleWeaponAndSave(
        ConfigService configService,
        string configPath,
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
        if (weaponIndex < 0 || weaponIndex >= weaponNames.Count)
        {
            return;
        }

        try
        {
            var root = configService.LoadJsonObjectOrEmpty(configPath);
            var specialWeaponLogicRoot = EnsureRoot(root, rootKey);
            aimSnapFlags[weaponIndex] = aimSnapEnabled;
            rapidFireFlags[weaponIndex] = rapidFireEnabled;
            releaseFireFlags[weaponIndex] = releaseFireEnabled;
            specialWeaponLogicRoot[aimSnapListKey] = BuildEnabledWeaponListNode(aimSnapFlags, weaponNames);
            specialWeaponLogicRoot[rapidFireListKey] = BuildEnabledWeaponListNode(rapidFireFlags, weaponNames);
            specialWeaponLogicRoot[releaseFireListKey] = BuildEnabledWeaponListNode(releaseFireFlags, weaponNames);
            configService.SaveJsonObject(configPath, root);
        }
        catch
        {
            // Keep UI responsive if file IO fails.
        }
    }

    public void LoadFromConfig(
        ConfigService configService,
        string configPath,
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

        try
        {
            var root = configService.LoadJsonObjectOrEmpty(configPath);
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
                configService.SaveJsonObject(configPath, root);
            }
        }
        catch
        {
            // Ignore malformed data and keep defaults in UI.
            Array.Clear(aimSnapFlags);
            Array.Clear(rapidFireFlags);
            Array.Clear(releaseFireFlags);
        }
    }
}
