namespace Rail.Maui;

public partial class App : Application
{
    public App()
    {
        InitializeComponent();
    }

    protected override Window CreateWindow(IActivationState? activationState)
    {
        var splash = new SplashPage();
        var window = new Window(splash) { Title = "Rail" };
        splash.Completed += (_, _) =>
        {
            if (window.Page == splash)
                window.Page = new AppShell();
        };
        return window;
    }
}
