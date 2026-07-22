using System.Reflection;
using OpenTK.Mathematics;
using OpenTK.Windowing.Common;
using OpenTK.Windowing.Desktop;

internal static class AppBootstrap
{
    public static MainWindow CreateMainWindow()
    {
        var windowSize = new Vector2i(480, 480);
        var restoreWindowState = WindowState.Normal;
        if (MainWindow.TryLoadWindowState(out var savedWindowState))
        {
            windowSize = new Vector2i(
                Math.Max(400, savedWindowState.Width),
                Math.Max(300, savedWindowState.Height));
            restoreWindowState = savedWindowState.IsMaximized ? WindowState.Maximized : WindowState.Normal;
        }

        var nativeWindowSettings = new NativeWindowSettings
        {
            Title = BuildWindowTitle(),
            ClientSize = windowSize,
            APIVersion = new Version(3, 3),
            Flags = ContextFlags.ForwardCompatible,
            Icon = ResourceAssets.LoadWindowIcon()
        };

        var window = new MainWindow(GameWindowSettings.Default, nativeWindowSettings)
        {
            WindowState = restoreWindowState
        };

        return window;
    }

    private static string BuildWindowTitle()
    {
        const string appName = "apex-imgui";
        var assembly = Assembly.GetExecutingAssembly();
        var informationalVersion = assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?
            .InformationalVersion;
        var normalizedVersion = string.IsNullOrWhiteSpace(informationalVersion)
            ? assembly.GetName().Version?.ToString(3)
            : informationalVersion.Split('+')[0];
        return string.IsNullOrWhiteSpace(normalizedVersion)
            ? appName
            : $"{appName} v{normalizedVersion}";
    }
}
