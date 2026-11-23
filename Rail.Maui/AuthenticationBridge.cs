using Microsoft.Maui.Devices.Sensors;

namespace Rail.Maui;

// Only these narrowly scoped methods are exposed to the bundled HybridWebView.
public sealed class AuthenticationBridge
{
    private static readonly Services.PackagedMapArchive MapArchive = new(
        () => FileSystem.Current.OpenAppPackageFileAsync("rail-pages/maps/tehran-region.pmtiles"),
        () => FileSystem.Current.OpenAppPackageFileAsync("rail-pages/maps/archive.sha256"),
        Path.Combine(FileSystem.Current.AppDataDirectory, "offline-maps"));

    // This fixed archive is the only file exposed to JavaScript. Never accept a path.
    public Task<string> ReadMapBytes(long offset, int length) => MapArchive.ReadBase64Async(offset, length);

    public Task<string?> ReadCurrentLocation() => MainThread.InvokeOnMainThreadAsync<string?>(async () =>
    {
        try
        {
            if (!Geolocation.Default.IsEnabled) return "disabled";
            var request = new GeolocationRequest(GeolocationAccuracy.Medium, TimeSpan.FromSeconds(10));
            var location = await Geolocation.Default.GetLocationAsync(request);
            return location is null ? null : FormattableString.Invariant($"{location.Longitude},{location.Latitude}");
        }
        catch (FeatureNotEnabledException) { return "disabled"; }
        catch (PermissionException) { return "permission-denied"; }
    });

    public Task<bool> OpenLocationSettings() =>
#if ANDROID
        MainThread.InvokeOnMainThreadAsync(() =>
        {
            if (Platform.CurrentActivity is not { } activity) return false;
            using var intent = new Android.Content.Intent(Android.Provider.Settings.ActionLocationSourceSettings);
            activity.StartActivity(intent);
            return true;
        });
#elif WINDOWS
        MainThread.InvokeOnMainThreadAsync(async () =>
            await global::Windows.System.Launcher.LaunchUriAsync(new Uri("ms-settings:privacy-location")));
#else
        Task.FromResult(false);
#endif

    private const string SessionKey = "rail.auth.session.v1";
    // Stable OS preferences survive normal in-place app updates, independently of WebView data.
    private const string MapPreferencesKey = "rail.map.preferences";

    public string? ReadMapPreferences() => Preferences.Default.Get<string?>(MapPreferencesKey, null);

    public void WriteMapPreferences(string preferences) => Preferences.Default.Set(MapPreferencesKey, preferences);

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
