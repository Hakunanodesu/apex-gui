internal sealed class ConfigAppService
{
    public List<string> EnumerateConfigBaseNames(string configsDirectoryPath)
    {
        var names = new List<string>();
        if (!Directory.Exists(configsDirectoryPath))
        {
            return names;
        }

        foreach (var jsonPath in Directory.EnumerateFiles(configsDirectoryPath, "*.json", SearchOption.TopDirectoryOnly))
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
}
