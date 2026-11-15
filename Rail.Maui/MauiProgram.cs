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
                fonts.AddFont("DUBAI-LIGHT.TTF", "DubaiLight");
                fonts.AddFont("DUBAI-REGULAR.TTF", "DubaiRegular");
                fonts.AddFont("DUBAI-MEDIUM.TTF", "DubaiMedium");
                fonts.AddFont("DUBAI-BOLD.TTF", "DubaiBold");
            });

#if WINDOWS
        builder.ConfigureLifecycleEvents(events => events.AddWindows(windows => windows.OnWindowCreated(window =>
        {
            var iconPath = Path.Combine(AppContext.BaseDirectory, "appicon.ico");
            if (File.Exists(iconPath))
                window.AppWindow.SetIcon(iconPath);
            var allowClose = false;
            window.AppWindow.Closing += (_, args) =>
            {
                if (allowClose) return;
                var page = Application.Current?.Windows.Select(item => item.Page)
                    .OfType<AppShell>().Select(shell => shell.CurrentPage).OfType<MainPage>().FirstOrDefault();
                if (page is null) return;
                args.Cancel = true;
                if (page.ConfirmExit()) { allowClose = true; window.DispatcherQueue.TryEnqueue(window.Close); }
            };
        })));
#endif

#if DEBUG
        builder.Logging.AddDebug();
#endif

        return builder.Build();
    }
}
