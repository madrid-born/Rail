# Rail assets for .NET MAUI

Copy this folder’s `Resources` subfolders into your MAUI app project. Keep the filenames so the generated app icon matches the standard template references.

| File | Purpose |
| --- | --- |
| `Resources/AppIcon/appicon.svg` | Full-square Persian Teal background |
| `Resources/AppIcon/appiconfg.svg` | Transparent ivory Rail mark for the app icon and Android themed icon |
| `Resources/Splash/splash.svg` | Centered ivory mark for the native launch screen |
| `Resources/Images/rail_logo.svg` | Optional in-app wordmark for light backgrounds |
| `Resources/Images/rail_logo_ivory.svg` | Optional in-app wordmark for dark backgrounds |

## Project file

Replace the template’s existing `MauiIcon` and `MauiSplashScreen` items with this block. MAUI processes the first icon and the first splash entry. The Android-only scale keeps the mark inside the adaptive icon mask. The splash background is set separately so the SVG can stay transparent.

```xml
<ItemGroup>
    <MauiIcon Include="Resources\AppIcon\appicon.svg"
              ForegroundFile="Resources\AppIcon\appiconfg.svg"
              BaseSize="1024,1024"
              Color="#00636A" />
    <MauiIcon Update="Resources\AppIcon\appicon.svg"
              ForegroundScale="0.65"
              Condition="$([MSBuild]::GetTargetPlatformIdentifier('$(TargetFramework)')) == 'android'" />

    <MauiSplashScreen Include="Resources\Splash\splash.svg"
                      BaseSize="160,160"
                      Color="#00636A" />
</ItemGroup>
```

For Android, the standard manifest icon names are `@mipmap/appicon` and `@mipmap/appicon_round`. Use the template’s `@style/Maui.SplashTheme` for the launcher activity. For iOS and Mac Catalyst, the standard icon catalog is `Assets.xcassets/appicon.appiconset`. MAUI generates the iOS launch storyboard. [App icon configuration](https://learn.microsoft.com/en-us/dotnet/maui/user-interface/images/app-icons?view=net-maui-10.0) · [Splash configuration](https://learn.microsoft.com/en-us/dotnet/maui/user-interface/images/splashscreen?view=net-maui-10.0)

## Optional in-app images

If your project already includes `Resources\Images\*` as `MauiImage` items, place this after that glob. If there is no glob, use `Include` in place of `Update`.

```xml
<ItemGroup>
    <MauiImage Update="Resources\Images\rail_logo.svg;Resources\Images\rail_logo_ivory.svg"
               BaseSize="320,120" />
</ItemGroup>
```

Reference the compiled images as `rail_logo.png` and `rail_logo_ivory.png` in XAML or C#. [MAUI image resources](https://learn.microsoft.com/en-us/dotnet/maui/user-interface/images/images?view=net-maui-10.0)
