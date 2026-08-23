using System.Globalization;
using Avalonia;
using Avalonia.Fonts.Inter;
using OctoHD.Core.Updates;

namespace OctoHD.App;

internal static class Program
{
    [STAThread]
    public static void Main(string[] args)
    {
        if (SelfUpdateBootstrapper.TryHandleStartup(args))
        {
            return;
        }

        var englishCulture = CultureInfo.GetCultureInfo("en-US");
        CultureInfo.DefaultThreadCurrentCulture = englishCulture;
        CultureInfo.DefaultThreadCurrentUICulture = englishCulture;
        BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
    }

    public static AppBuilder BuildAvaloniaApp() =>
        AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .UseWaylandWithFallback()
            .WithInterFont();
}
