internal sealed class DependencyService
{
    private const string ViGemBusInstallPath = @"C:\Program Files\Nefarius Software Solutions";

    public bool IsViGemBusReady()
    {
        return Directory.Exists(ViGemBusInstallPath);
    }
}
