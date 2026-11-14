namespace Rail.Maui;

public partial class MainPage : ContentPage
{
    public MainPage()
    {
        InitializeComponent();
        RailWebView.SetInvokeJavaScriptTarget(new AuthenticationBridge());

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

            Content = new WebView { Source = uri.AbsoluteUri };
        }
#endif
    }
}
