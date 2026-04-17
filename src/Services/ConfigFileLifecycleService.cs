using System.Text.Json.Nodes;

internal sealed class ConfigFileLifecycleService
{
    public bool TryNormalizeBaseName(string raw, out string baseName, out string error)
    {
        baseName = string.Empty;
        error = string.Empty;
        var n = raw.Trim();
        if (n.EndsWith(".json", StringComparison.OrdinalIgnoreCase))
        {
            n = n[..^5];
        }

        n = n.Trim();
        if (n.Length == 0)
        {
            error = "名称不能为空";
            return false;
        }

        if (n.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
        {
            error = "名称包含非法字符";
            return false;
        }

        if (n is "." or "..")
        {
            error = "名称无效";
            return false;
        }

        baseName = n;
        return true;
    }

    public bool TryCreateEmptyConfigFile(
        string rawName,
        ConfigService configService,
        SpecialWeaponLogicService specialWeaponLogicService,
        string specialWeaponLogicConfigKey,
        string aimSnapWeaponListConfigKey,
        string rapidFireWeaponListConfigKey,
        string releaseFireWeaponListConfigKey,
        out string normalizedBaseName,
        out string error)
    {
        normalizedBaseName = string.Empty;
        error = string.Empty;
        if (!TryNormalizeBaseName(rawName, out var baseName, out var normErr))
        {
            error = normErr;
            return false;
        }

        try
        {
            Directory.CreateDirectory(configService.ConfigsDirectoryPath);
            var path = configService.GetConfigPath(baseName);
            if (File.Exists(path))
            {
                error = "已存在同名配置文件";
                return false;
            }

            var root = new JsonObject();
            var specialWeaponLogicRoot = specialWeaponLogicService.EnsureRoot(root, specialWeaponLogicConfigKey);
            specialWeaponLogicRoot[aimSnapWeaponListConfigKey] = new JsonArray();
            specialWeaponLogicRoot[rapidFireWeaponListConfigKey] = new JsonArray();
            specialWeaponLogicRoot[releaseFireWeaponListConfigKey] = new JsonArray();
            configService.SaveJsonObject(path, root);
            normalizedBaseName = baseName;
            return true;
        }
        catch (Exception ex)
        {
            error = ex.Message;
            return false;
        }
    }

    public void TryDeleteConfigFile(string baseName, ConfigService configService)
    {
        if (string.IsNullOrWhiteSpace(baseName))
        {
            return;
        }

        try
        {
            var path = configService.GetConfigPath(baseName);
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch
        {
            // Ignore delete failures; list refresh will reflect disk state on next scan if needed.
        }
    }
}
