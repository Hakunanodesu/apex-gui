using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;

internal sealed class ConfigService
{
    private readonly string _configsDirectoryPath;
    private readonly string _configCurrentFilePath;

    private static readonly JsonSerializerOptions IndentedJsonOptions = new()
    {
        WriteIndented = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    public ConfigService(string configsDirectoryPath)
    {
        _configsDirectoryPath = configsDirectoryPath;
        _configCurrentFilePath = Path.Combine(configsDirectoryPath, ".current");
    }

    public string ConfigsDirectoryPath => _configsDirectoryPath;

    public string GetConfigPath(string configBaseNameWithoutExtension)
    {
        return Path.Combine(_configsDirectoryPath, configBaseNameWithoutExtension + ".json");
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
}
