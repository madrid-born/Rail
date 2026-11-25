namespace Rail.Maui;

public partial class SplashPage : ContentPage
{
    private const string StartupAnimation = "RailSplash";
    private const uint SplashDuration = 950;

    internal event EventHandler? Completed;

    public SplashPage()
    {
        InitializeComponent();
        Loaded += Play;
        Unloaded += (_, _) => this.AbortAnimation(StartupAnimation);
    }

    private void Play(object? sender, EventArgs args)
    {
        RailMark.TranslationY = 0;
        Divider.Opacity = 0;
        Divider.ScaleX = 0;
        Attribution.Opacity = 0;
        Attribution.TranslationY = 5;

        var animation = new Animation();
        animation.Add(150d / SplashDuration, 550d / SplashDuration,
            new Animation(offset => RailMark.TranslationY = offset, 0, -44, Easing.CubicOut));
        animation.Add(340d / SplashDuration, 600d / SplashDuration,
            new Animation(scale =>
            {
                Divider.ScaleX = scale;
                Divider.Opacity = 0.43;
            }, 0, 1, Easing.CubicOut));
        animation.Add(490d / SplashDuration, 800d / SplashDuration,
            new Animation(opacity =>
            {
                Attribution.Opacity = opacity;
                Attribution.TranslationY = 5 * (1 - opacity);
            }, 0, 1, Easing.CubicInOut));

        animation.Commit(this, StartupAnimation, length: SplashDuration,
            finished: (_, _) => Dispatcher.Dispatch(() =>
            {
                if (IsLoaded && !this.AnimationIsRunning(StartupAnimation))
                    Completed?.Invoke(this, EventArgs.Empty);
            }));
    }
}
