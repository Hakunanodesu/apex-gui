using OpenTK.Mathematics;
using OpenTK.Windowing.Common;
using OpenTK.Windowing.Desktop;

RuntimePerformance.ConfigureProcessPriority();

var windowSize = new Vector2i(480, 480);
var restoreWindowState = WindowState.Normal;
Vector2i? windowLocation = null;
if (MainWindow.TryLoadWindowState(out var savedWindowState))
{
    windowSize = new Vector2i(
        Math.Max(400, savedWindowState.Width),
        Math.Max(300, savedWindowState.Height));
    windowLocation = new Vector2i(savedWindowState.X, savedWindowState.Y);
    restoreWindowState = savedWindowState.IsMaximized ? WindowState.Maximized : WindowState.Normal;
}

var nativeWindowSettings = new NativeWindowSettings
{
    Title = "apex-imgui",
    ClientSize = windowSize,
    APIVersion = new Version(3, 3),
    Flags = ContextFlags.ForwardCompatible,
    Icon = ResourceAssets.LoadWindowIcon()
};

using var window = new MainWindow(GameWindowSettings.Default, nativeWindowSettings);
if (windowLocation is not null)
{
    window.Location = windowLocation.Value;
}

window.WindowState = restoreWindowState;
window.Run();
