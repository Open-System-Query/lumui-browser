using Avalonia;
using Avalonia.Win32;

namespace Lumui.Browser;

public static class Program
{
    [STAThread]
    public static void Main(String[] args) =>
        Run(args);

    public static Int32 Run(String[] args) =>
        BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);

    public static AppBuilder BuildAvaloniaApp()
    {
        return AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .With(new Win32PlatformOptions
            {
                RenderingMode =
                [
                    Win32RenderingMode.AngleEgl,
                    Win32RenderingMode.Software
                ]
            })
            .With(new SkiaOptions
            {
                MaxGpuResourceSizeBytes = 256L * 1024L * 1024L
            })
            .WithInterFont();
    }
}
