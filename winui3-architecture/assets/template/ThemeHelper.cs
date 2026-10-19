using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;

namespace MyApp;

/// <summary>
/// Theme-aware resource lookup.
///
/// Application.Current.Resources[key] does NOT return entries declared inside a
/// ThemeDictionaries block. Those must be looked up through ThemeDictionaries with
/// the current theme name, which is what this helper does.
/// </summary>
public static class ThemeHelper
{
    /// <summary>Currently applied theme. Update it wherever you apply the theme.</summary>
    public static ApplicationTheme CurrentTheme { get; set; } = ApplicationTheme.Light;

    public static SolidColorBrush Brush(string key)
    {
        if (Application.Current.Resources.TryGetValue(key, out object? value)
            && value is SolidColorBrush direct)
        {
            return direct;
        }

        string theme = CurrentTheme == ApplicationTheme.Dark ? "Dark" : "Light";
        if (Application.Current.Resources.ThemeDictionaries.TryGetValue(theme, out object? dictObj)
            && dictObj is ResourceDictionary dict
            && dict.TryGetValue(key, out object? themed)
            && themed is SolidColorBrush themedBrush)
        {
            return themedBrush;
        }

        return new SolidColorBrush(Colors.Gray);
    }

    /// <summary>Apply a theme index (0 = system, 1 = light, 2 = dark) to a root element.</summary>
    public static void ApplyTheme(FrameworkElement root, int themeIndex, MicaSystemBackdrop? backdrop = null)
    {
        root.RequestedTheme = themeIndex switch
        {
            1 => ElementTheme.Light,
            2 => ElementTheme.Dark,
            _ => ElementTheme.Default
        };

        // Read ActualTheme AFTER assigning RequestedTheme: with Default,
        // RequestedTheme does not tell you what the system selected.
        bool dark = root.ActualTheme == ElementTheme.Dark;
        CurrentTheme = dark ? ApplicationTheme.Dark : ApplicationTheme.Light;

        backdrop?.SetTheme(dark ? Microsoft.UI.Composition.SystemBackdrops.SystemBackdropTheme.Dark
                                : Microsoft.UI.Composition.SystemBackdrops.SystemBackdropTheme.Light);
    }
}
