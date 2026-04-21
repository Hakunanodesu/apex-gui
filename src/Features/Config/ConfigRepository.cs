using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;

internal sealed class ConfigRepository
{
    private readonly string _configsDirectoryPath;
    private readonly string _configCurrentFilePath;

    private static readonly JsonSerializerOptions IndentedJsonOptions = new()
    {
        WriteIndented = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    public ConfigRepository(string configsDirectoryPath)
    {
        _configsDirectoryPath = configsDirectoryPath;
        _configCurrentFilePath = Path.Combine(configsDirectoryPath, ".current");
    }

    public string ConfigsDirectoryPath => _configsDirectoryPath;

    public string GetConfigPath(string configBaseNameWithoutExtension)
    {
        return Path.Combine(_configsDirectoryPath, configBaseNameWithoutExtension + ".json");
    }

    public List<string> EnumerateConfigBaseNames()
    {
        var names = new List<string>();
        if (!Directory.Exists(_configsDirectoryPath))
        {
            return names;
        }

        foreach (var jsonPath in Directory.EnumerateFiles(_configsDirectoryPath, "*.json", SearchOption.TopDirectoryOnly))
        {
            var fileName = Path.GetFileNameWithoutExtension(jsonPath);
            if (!string.IsNullOrWhiteSpace(fileName))
            {
                names.Add(fileName);
            }
        }

        names.Sort(StringComparer.OrdinalIgnoreCase);
        return names;
    }

    public int ResolveSelectedIndex(IReadOnlyList<string> configFiles, int currentIndex, string? forceSelectBaseName, string? oldSelection, string? persistedName)
    {
        if (configFiles.Count == 0)
        {
            return 0;
        }

        if (!string.IsNullOrWhiteSpace(forceSelectBaseName))
        {
            var forceIndex = FindIndex(configFiles, forceSelectBaseName);
            if (forceIndex >= 0)
            {
                return forceIndex;
            }
        }

        if (!string.IsNullOrWhiteSpace(oldSelection))
        {
            var oldIndex = FindIndex(configFiles, oldSelection);
            if (oldIndex >= 0)
            {
                return oldIndex;
            }
        }

        if (!string.IsNullOrWhiteSpace(persistedName))
        {
            var persistedIndex = FindIndex(configFiles, persistedName);
            if (persistedIndex >= 0)
            {
                return persistedIndex;
            }
        }

        return Math.Clamp(currentIndex, 0, configFiles.Count - 1);
    }

    public string? TryReadCurrentConfigFileName()
    {
        try
        {
            if (!File.Exists(_configCurrentFilePath))
            {
                return null;
            }

            var line = File.ReadAllText(_configCurrentFilePath).Trim();
            return string.IsNullOrWhiteSpace(line) ? null : line;
        }
        catch
        {
            return null;
        }
    }

    public void WriteCurrentConfigFileName(string configBaseNameWithoutExtension)
    {
        try
        {
            Directory.CreateDirectory(_configsDirectoryPath);
            File.WriteAllText(_configCurrentFilePath, configBaseNameWithoutExtension + Environment.NewLine);
        }
        catch
        {
            // Keep UI responsive if the file is locked or the path is not writable.
        }
    }

    public void ClearCurrentConfigPointerFile()
    {
        try
        {
            if (File.Exists(_configCurrentFilePath))
            {
                File.Delete(_configCurrentFilePath);
            }
        }
        catch
        {
            // Ignore IO failures.
        }
    }

    public bool TryLoadJsonObject(string path, out JsonObject root)
    {
        root = new JsonObject();
        if (!File.Exists(path))
        {
            return false;
        }

        var raw = File.ReadAllText(path);
        if (string.IsNullOrWhiteSpace(raw))
        {
            return false;
        }

        if (JsonNode.Parse(raw) is not JsonObject parsed)
        {
            return false;
        }

        root = parsed;
        return true;
    }

    public JsonObject LoadJsonObjectOrEmpty(string path)
    {
        return TryLoadJsonObject(path, out var root) ? root : new JsonObject();
    }

    public void SaveJsonObject(string path, JsonObject root)
    {
        File.WriteAllText(path, root.ToJsonString(IndentedJsonOptions) + Environment.NewLine);
    }

    public string? TryReadString(string path, string key)
    {
        try
        {
            if (!TryLoadJsonObject(path, out var root))
            {
                return null;
            }

            var value = root[key]?.GetValue<string>()?.Trim();
            return string.IsNullOrWhiteSpace(value) ? null : value;
        }
        catch
        {
            return null;
        }
    }

    public int? TryReadInt(string path, string key)
    {
        try
        {
            if (!TryLoadJsonObject(path, out var root))
            {
                return null;
            }

            return root[key]?.GetValue<int>();
        }
        catch
        {
            return null;
        }
    }

    public float? TryReadFloat(string path, string key)
    {
        try
        {
            if (!TryLoadJsonObject(path, out var root))
            {
                return null;
            }

            return root[key]?.GetValue<float>();
        }
        catch
        {
            return null;
        }
    }

    public void TryWriteString(string path, string key, string value)
    {
        try
        {
            var root = LoadJsonObjectOrEmpty(path);
            root[key] = value;
            SaveJsonObject(path, root);
        }
        catch
        {
            // Keep selection changes responsive if file IO fails.
        }
    }

    public void TryWriteInt(string path, string key, int value)
    {
        try
        {
            var root = LoadJsonObjectOrEmpty(path);
            root[key] = value;
            SaveJsonObject(path, root);
        }
        catch
        {
            // Keep selection changes responsive if file IO fails.
        }
    }

    public void TryWriteFloat(string path, string key, float value)
    {
        try
        {
            var root = LoadJsonObjectOrEmpty(path);
            root[key] = value;
            SaveJsonObject(path, root);
        }
        catch
        {
            // Keep selection changes responsive if file IO fails.
        }
    }

    public void TryRemoveKey(string path, string key)
    {
        try
        {
            var root = LoadJsonObjectOrEmpty(path);
            root.Remove(key);
            SaveJsonObject(path, root);
        }
        catch
        {
            // Keep UI responsive if file IO fails.
        }
    }

    public bool TryNormalizeBaseName(string raw, out string baseName, out string error)
    {
        baseName = string.Empty;
        error = string.Empty;
        var normalized = raw.Trim();
        if (normalized.EndsWith(".json", StringComparison.OrdinalIgnoreCase))
        {
            normalized = normalized[..^5];
        }

        normalized = normalized.Trim();
        if (normalized.Length == 0)
        {
            error = "名称不能为空";
            return false;
        }

        if (normalized.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
        {
            error = "名称包含非法字符";
            return false;
        }

        if (normalized is "." or "..")
        {
            error = "名称无效";
            return false;
        }

        baseName = normalized;
        return true;
    }

    public bool TryCreateEmptyConfigFile(
        string rawName,
        string specialWeaponLogicConfigKey,
        string aimSnapWeaponListConfigKey,
        string rapidFireWeaponListConfigKey,
        string releaseFireWeaponListConfigKey,
        out string normalizedBaseName,
        out string error)
    {
        normalizedBaseName = string.Empty;
        error = string.Empty;
        if (!TryNormalizeBaseName(rawName, out var baseName, out var normalizeError))
        {
            error = normalizeError;
            return false;
        }

        try
        {
            Directory.CreateDirectory(_configsDirectoryPath);
            var path = GetConfigPath(baseName);
            if (File.Exists(path))
            {
                error = "已存在同名配置文件";
                return false;
            }

            var root = new JsonObject();
            var specialWeaponLogicRoot = EnsureObject(root, specialWeaponLogicConfigKey);
            specialWeaponLogicRoot[aimSnapWeaponListConfigKey] = new JsonArray();
            specialWeaponLogicRoot[rapidFireWeaponListConfigKey] = new JsonArray();
            specialWeaponLogicRoot[releaseFireWeaponListConfigKey] = new JsonArray();
            SaveJsonObject(path, root);
            normalizedBaseName = baseName;
            return true;
        }
        catch (Exception ex)
        {
            error = ex.Message;
            return false;
        }
    }

    public void TryDeleteConfigFile(string baseName)
    {
        if (string.IsNullOrWhiteSpace(baseName))
        {
            return;
        }

        try
        {
            var path = GetConfigPath(baseName);
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

    private static int FindIndex(IReadOnlyList<string> files, string target)
    {
        for (var i = 0; i < files.Count; i++)
        {
            if (string.Equals(files[i], target, StringComparison.OrdinalIgnoreCase))
            {
                return i;
            }
        }

        return -1;
    }

    private static JsonObject EnsureObject(JsonObject root, string key)
    {
        if (root[key] is JsonObject existing)
        {
            return existing;
        }

        var created = new JsonObject();
        root[key] = created;
        return created;
    }
}
