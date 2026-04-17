internal sealed class ConfigContextService
{
    public bool TryResolveCurrentConfigPath(IReadOnlyList<string> configFiles, int selectedConfigFileIndex, ConfigService configService, out string configPath)
    {
        configPath = string.Empty;
        if (configFiles.Count == 0)
        {
            return false;
        }

        var configIndex = Math.Clamp(selectedConfigFileIndex, 0, configFiles.Count - 1);
        configPath = configService.GetConfigPath(configFiles[configIndex]);
        return true;
    }
}
