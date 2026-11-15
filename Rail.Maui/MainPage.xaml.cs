namespace Rail.Maui;

public partial class MainPage : ContentPage
{
    private WebView? developmentWebView;
    private object? configuredWebView;
    private long exitDeadline;
    private bool handlingBack;
    private IDispatcherTimer? exitHintTimer;

    public MainPage()
    {
        InitializeComponent();
        RailWebView.SetInvokeJavaScriptTarget(new AuthenticationBridge());
        RailWebView.HandlerChanged += (_, _) => ConfigureWebView(RailWebView);

#if DEBUG
        // The optional development profile connects directly to Vite for Vue HMR.
        // Normal runs and all Release builds keep the bundled HybridWebView.
        var developmentUrl = Environment.GetEnvironmentVariable("RAIL_WEB_DEV_SERVER");
        if (!string.IsNullOrWhiteSpace(developmentUrl))
        {
            if (!Uri.TryCreate(developmentUrl, UriKind.Absolute, out var uri) ||
                (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
            {
                throw new InvalidOperationException("RAIL_WEB_DEV_SERVER must be an absolute HTTP or HTTPS URL.");
            }

            developmentWebView = new WebView { Source = uri.AbsoluteUri };
            developmentWebView.HandlerChanged += (_, _) => ConfigureWebView(developmentWebView);
            WebContent.Content = developmentWebView;
        }
#endif
    }

    private void ConfigureWebView(View view)
    {
        var platform = view.Handler?.PlatformView;
        if (platform is null || ReferenceEquals(platform, configuredWebView)) return;
        configuredWebView = platform;
#if WINDOWS
        if (platform is Microsoft.UI.Xaml.Controls.WebView2 native)
        {
            void Apply()
            {
                if (native.CoreWebView2 is not { } core) return;
                core.Settings.AreDefaultContextMenusEnabled = false;
                core.Settings.IsStatusBarEnabled = false;
                core.Settings.AreBrowserAcceleratorKeysEnabled = false;
                core.Settings.AreDevToolsEnabled = false;
            }
            native.CoreWebView2Initialized += (_, _) => Apply();
            Apply();
        }
#elif ANDROID
        if (platform is Android.Webkit.WebView native) native.LongClickable = false;
#elif IOS || MACCATALYST
        if (platform is WebKit.WKWebView native)
        {
            native.AllowsLinkPreview = false;
            native.ScrollView.Bounces = false;
        }
#endif
    }

    public bool ConfirmExit()
    {
        var now = Environment.TickCount64;
        if (exitDeadline > now) return true;
        exitDeadline = now + 2500;
        ExitHint.IsVisible = true;
        exitHintTimer ??= Dispatcher.CreateTimer();
        exitHintTimer.Stop();
        exitHintTimer.Interval = TimeSpan.FromMilliseconds(2500);
        exitHintTimer.IsRepeating = false;
        exitHintTimer.Tick -= HideExitHint;
        exitHintTimer.Tick += HideExitHint;
        exitHintTimer.Start();
        return false;
    }

    private void HideExitHint(object? sender, EventArgs args) => ExitHint.IsVisible = false;

    public async Task HandleBackAsync()
    {
        if (handlingBack) return;
        handlingBack = true;
        try
        {
            const string script = "typeof window.RailNative === 'undefined' || window.RailNative.back()";
            string? response;
            try
            {
                response = developmentWebView is { } web
                    ? await web.EvaluateJavaScriptAsync(script)
                    : await RailWebView.EvaluateJavaScriptAsync(script);
            }
            catch (Exception) { response = "true"; } // Allow guarded exit even while the WebView is unavailable.
            if (string.Equals(response?.Trim('"'), "true", StringComparison.OrdinalIgnoreCase) && ConfirmExit())
                Application.Current?.Quit();
        }
        finally { handlingBack = false; }
    }
}
