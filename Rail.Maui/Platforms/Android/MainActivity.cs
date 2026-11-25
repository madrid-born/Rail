using Android.App;
using Android.Content.PM;
using Android.OS;
using Android.Views;
using AndroidX.Core.View;

namespace Rail.Maui;

[Activity(Theme = "@style/Maui.SplashTheme", MainLauncher = true, LaunchMode = LaunchMode.SingleTop,
    ConfigurationChanges = ConfigChanges.ScreenSize | ConfigChanges.Orientation | ConfigChanges.UiMode |
                           ConfigChanges.ScreenLayout | ConfigChanges.SmallestScreenSize | ConfigChanges.Density)]
public class MainActivity : MauiAppCompatActivity
{
    protected override void OnCreate(Bundle? savedInstanceState)
    {
        base.OnCreate(savedInstanceState);

        // MAUI already lays out edge to edge. Older Android versions also need
        // permission to draw into the screen area around a display cutout.
        if (OperatingSystem.IsAndroidVersionAtLeast(28) &&
            !OperatingSystem.IsAndroidVersionAtLeast(35) &&
            Window is { Attributes: { } attributes } window)
        {
            attributes.LayoutInDisplayCutoutMode = LayoutInDisplayCutoutMode.ShortEdges;
            window.Attributes = attributes;
        }
    }

    protected override void OnResume()
    {
        base.OnResume();
        HideNavigationBar();
    }

    public override void OnWindowFocusChanged(bool hasFocus)
    {
        base.OnWindowFocusChanged(hasFocus);

        if (hasFocus)
            HideNavigationBar();
    }

    private void HideNavigationBar()
    {
        if (Window is not { } window ||
            WindowCompat.GetInsetsController(window, window.DecorView) is not { } controller)
            return;

        // A swipe reveals the navigation bar over the app without changing its layout.
        controller.SystemBarsBehavior = WindowInsetsControllerCompat.BehaviorShowTransientBarsBySwipe;
        controller.Hide(WindowInsetsCompat.Type.NavigationBars());
    }
}
