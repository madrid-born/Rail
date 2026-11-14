namespace Rail.Maui;

// Only these narrowly scoped methods are exposed to the bundled HybridWebView.
public sealed class AuthenticationBridge
{
    private const string SessionKey = "rail.auth.session.v1";

    public Task<string?> ReadSession() => SecureStorage.Default.GetAsync(SessionKey);

    public async Task WriteSession(string? session)
    {
        if (session is null) SecureStorage.Default.Remove(SessionKey);
        else await SecureStorage.Default.SetAsync(SessionKey, session);
    }

    public Task OpenAuthenticationBrowser(string address)
    {
        if (!Uri.TryCreate(address, UriKind.Absolute, out var uri) ||
            uri.Scheme != Uri.UriSchemeHttps ||
            !uri.AbsolutePath.Equals("/api/Authentication/google", StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("Invalid authentication address.", nameof(address));
        return MainThread.InvokeOnMainThreadAsync(async () =>
            await Browser.Default.OpenAsync(uri, BrowserLaunchMode.External));
    }
}
