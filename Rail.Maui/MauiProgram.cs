using Microsoft.Extensions.Logging;

#if WINDOWS
using Microsoft.Maui.LifecycleEvents;
#endif

namespace Rail.Maui;

public static class MauiProgram
{
    public static MauiApp CreateMauiApp()
    {
        var builder = MauiApp.CreateBuilder();
        builder
            .UseMauiApp<App>()
            .ConfigureFonts(fonts =>
            {
                fonts.AddFont("OpenSans-Regular.ttf", "OpenSansRegular");
                fonts.AddFont("OpenSans-Semibold.ttf", "OpenSansSemibold");
            });

#if WINDOWS
        builder.ConfigureLifecycleEvents(events => events.AddWindows(windows => windows.OnWindowCreated(window =>
        {
            var iconPath = Path.Combine(AppContext.BaseDirectory, "appicon.ico");
            if (File.Exists(iconPath))
                window.AppWindow.SetIcon(iconPath);
        })));
#endif

#if DEBUG
        builder.Logging.AddDebug();
#endif

        return builder.Build();
    }
}
