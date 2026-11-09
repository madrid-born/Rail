using Microsoft.Extensions.DependencyInjection;

namespace Rail.Maui;

public partial class App : Application
{
    public App()
    {
        InitializeComponent();
    }

    protected override Window CreateWindow(IActivationState? activationState)
    {
        // An unpackaged Windows launch needs an in-app splash.
        var splash = new ContentPage
        {
            BackgroundColor = (Color)Resources["Primary"],
            Content = new Image
            {
                Source = "splash.png",
                WidthRequest = 240,
                HeightRequest = 240,
                HorizontalOptions = LayoutOptions.Center,
                VerticalOptions = LayoutOptions.Center
            }
        };

        var window = new Window(splash);
        var timer = Dispatcher.CreateTimer();
        timer.Interval = TimeSpan.FromMilliseconds(650);
        timer.IsRepeating = false;
        timer.Tick += (_, _) => window.Page = new AppShell();
        splash.Loaded += ShowSplash;
        window.Destroying += (_, _) => timer.Stop();
        return window;

        void ShowSplash(object? sender, EventArgs args)
        {
            splash.Loaded -= ShowSplash;
            timer.Start();
        }
    }
}
