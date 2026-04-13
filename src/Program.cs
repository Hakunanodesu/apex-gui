using OpenTK.Windowing.Common;
using OpenTK.Windowing.Desktop;

RuntimePerformance.ConfigureProcessPriority();

var nativeWindowSettings = new NativeWindowSettings
{
    Title = "apex-imgui",
    ClientSize = new OpenTK.Mathematics.Vector2i(1280, 720),
    APIVersion = new Version(3, 3),
    Flags = ContextFlags.ForwardCompatible,
    Icon = ResourceAssets.LoadWindowIcon()
};

using var window = new MainWindow(GameWindowSettings.Default, nativeWindowSettings);
window.Run();
