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
}
