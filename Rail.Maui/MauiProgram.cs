using Microsoft.Extensions.Logging;

using Microsoft.Maui.LifecycleEvents;

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
            })
            .ConfigureLifecycleEvents(events => events.AddWindows(windows => windows.OnWindowCreated(window =>
            {
                var iconPath = Path.Combine(AppContext.BaseDirectory, "appicon.ico");
                if (File.Exists(iconPath))
                    window.AppWindow.SetIcon(iconPath);
            })));

#if DEBUG
        builder.Logging.AddDebug();
#endif

        return builder.Build();
    }
}
