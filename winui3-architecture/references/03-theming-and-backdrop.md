# 03 — Theming and Backdrop

WinUI 3 gives you three things that are easy to confuse:

1. **Theme** — Light / Dark / System. Changes text, surfaces, and control colours.
2. **Backdrop** — Mica / Acrylic / None. Changes what the window shows *behind* your content.
3. **Decoration** — your own vector layer drawn on top of the backdrop.

Keep them separate. A backdrop does not replace a theme; a theme does not imply a backdrop.

## Backdrop capability matrix

| Kind | Windows 11 22000+ | Windows 10 1809–21H2 |
|---|---|---|
| Mica | Yes | **No** — `MicaController.IsSupported()` returns false |
| Acrylic (DesktopAcrylicController) | Yes | Usually yes; check `IsSupported()` anyway |
| None | Yes | Yes |

Resolve user intent against the machine, never the other way around:

```csharp
private static BackdropKind Resolve(BackdropKind kind)
{
    if (kind == BackdropKind.Mica && !MicaController.IsSupported())
        kind = BackdropKind.Acrylic;
    if (kind == BackdropKind.Acrylic && !DesktopAcrylicController.IsSupported())
        kind = BackdropKind.None;
    return kind;
}
```

Expose `EffectiveKind` next to the stored preference. The UI shows the preference; the
code uses `EffectiveKind` for anything visual.

## Why a custom SystemBackdrop

`Window.SystemBackdrop = new MicaBackdrop()` works, but it does not follow the app theme
when the user switches Light/Dark at runtime, and it cannot switch kinds without
recreating the window. Subclass `SystemBackdrop`:

```csharp
public sealed class MicaSystemBackdrop : SystemBackdrop
{
    private MicaController? _mica;
    private DesktopAcrylicController? _acrylic;
    private SystemBackdropConfiguration? _configuration;
    private SystemBackdropTheme _theme = SystemBackdropTheme.Default;
    private BackdropKind _kind = BackdropKind.Mica;
    private ICompositionSupportsSystemBackdrop? _target;

    public BackdropKind EffectiveKind { get; private set; } = BackdropKind.Mica;

    public void SetKind(BackdropKind kind)
    {
        if (_kind == kind) return;          // short-circuit: theme changes call this repeatedly
        _kind = kind;
        if (_target != null) ApplyControllers(_target);
        else EffectiveKind = Resolve(kind);
    }

    public void SetTheme(SystemBackdropTheme theme)
    {
        _theme = theme;
        if (_configuration != null) _configuration.Theme = theme;
    }

    protected override void OnTargetConnected(ICompositionSupportsSystemBackdrop target, XamlRoot root)
    {
        base.OnTargetConnected(target, root);
        _target = target;
        _configuration = new SystemBackdropConfiguration { IsInputActive = true, Theme = _theme };
        ApplyControllers(target);
    }

    protected override void OnTargetDisconnected(ICompositionSupportsSystemBackdrop target)
    {
        base.OnTargetDisconnected(target);
        ReleaseControllers(target);
        _target = null;
        _configuration = null;
    }

    private void ApplyControllers(ICompositionSupportsSystemBackdrop target)
    {
        ReleaseControllers(target);
        if (_configuration == null) return;

        EffectiveKind = Resolve(_kind);
        if (EffectiveKind == BackdropKind.Mica)
        {
            _mica = new MicaController();
            _mica.SetSystemBackdropConfiguration(_configuration);
            _mica.AddSystemBackdropTarget(target);
        }
        else if (EffectiveKind == BackdropKind.Acrylic)
        {
            _acrylic = new DesktopAcrylicController();
            _acrylic.SetSystemBackdropConfiguration(_configuration);
            _acrylic.AddSystemBackdropTarget(target);
        }
    }

    private void ReleaseControllers(ICompositionSupportsSystemBackdrop target)
    {
        _mica?.RemoveSystemBackdropTarget(target);
        _mica?.Dispose();
        _mica = null;
        _acrylic?.RemoveSystemBackdropTarget(target);
        _acrylic?.Dispose();
        _acrylic = null;
    }
}
```

The `if (_kind == kind) return;` guard is not an optimisation. Theme switching calls
`SetKind` repeatedly; without the guard the controller is rebuilt each time and the
window flashes.

## The opaque-background trap

A backdrop is composited **behind** your content. Any opaque brush between the backdrop
and the desktop hides it completely — the window looks like a plain grey rectangle and
you conclude "Mica does not work".

- `Window` — no background.
- `NavigationView` — override its pane brushes to `Transparent` (see below).
- Page root `Grid` — `Background="{x:Null}"`.
- `Frame` — no background.

When the user selects **None**, the window has nothing behind it, so you must paint an
opaque colour yourself:

```csharp
RootGrid.Background = isBackdropActive
    ? null
    : new SolidColorBrush(ThemeHelper.CurrentTheme == ApplicationTheme.Dark
        ? Color.FromArgb(255, 32, 32, 32)
        : Color.FromArgb(255, 243, 243, 243));
```

Forgetting this in the "None" case produces a transparent hole showing the desktop icons.

## Theme dictionaries

Put semantic brushes in a merged dictionary **after** `XamlControlsResources`. A
`ThemeDictionaries` block written inline inside `Application.Resources` cannot override
keys that `XamlControlsResources` already defines.

```xml
<ResourceDictionary.MergedDictionaries>
    <XamlControlsResources xmlns="using:Microsoft.UI.Xaml.Controls" />
    <ResourceDictionary>
        <ResourceDictionary.ThemeDictionaries>
            <ResourceDictionary x:Key="Light">
                <SolidColorBrush x:Key="RiskCriticalBrush" Color="#C42B1C" />
                <SolidColorBrush x:Key="NavigationViewDefaultPaneBackground" Color="Transparent" />
                <SolidColorBrush x:Key="NavigationViewExpandedPaneBackground" Color="Transparent" />
            </ResourceDictionary>
            <ResourceDictionary x:Key="Dark">
                <SolidColorBrush x:Key="RiskCriticalBrush" Color="#FF99A4" />
                <SolidColorBrush x:Key="NavigationViewDefaultPaneBackground" Color="Transparent" />
                <SolidColorBrush x:Key="NavigationViewExpandedPaneBackground" Color="Transparent" />
            </ResourceDictionary>
        </ResourceDictionary.ThemeDictionaries>
    </ResourceDictionary>
</ResourceDictionary.MergedDictionaries>
```

Dark-mode colours are not the light ones darkened. Accent and status colours need
higher lightness in dark mode to keep contrast (`#C42B1C` → `#FF99A4`).

## Reading a theme brush from code

`Application.Current.Resources[key]` does **not** return theme-dictionary entries. Look
them up through `ThemeDictionaries` with the current theme name:

```csharp
public static SolidColorBrush Brush(string key)
{
    if (Application.Current.Resources.TryGetValue(key, out var v) && v is SolidColorBrush b)
        return b;

    string theme = CurrentTheme == ApplicationTheme.Dark ? "Dark" : "Light";
    if (Application.Current.Resources.ThemeDictionaries.TryGetValue(theme, out var dictObj)
        && dictObj is ResourceDictionary dict
        && dict.TryGetValue(key, out var tv) && tv is SolidColorBrush tb)
        return tb;

    return new SolidColorBrush(Colors.Gray);
}
```

Track `CurrentTheme` yourself and update it in the same place you apply the theme.

## Applying a theme at runtime

```csharp
// On the root element of the window
RootGrid.RequestedTheme = index switch
{
    1 => ElementTheme.Light,
    2 => ElementTheme.Dark,
    _ => ElementTheme.Default
};

ThemeHelper.CurrentTheme =
    RootGrid.ActualTheme == ElementTheme.Dark ? ApplicationTheme.Dark : ApplicationTheme.Light;

Backdrop.SetTheme(RootGrid.ActualTheme == ElementTheme.Dark
    ? SystemBackdropTheme.Dark
    : SystemBackdropTheme.Light);
```

Read `ActualTheme` **after** assigning `RequestedTheme`. With `Default`, `RequestedTheme`
does not tell you what the system chose.

## Decoration layer

A decorative vector layer (waves, grid, mountains) goes at the bottom of `RootGrid`,
before the NavigationView, with `IsHitTestVisible="False"` and stroke-only shapes. Fill
nothing — a filled shape hides the Mica.

To soften strokes without Win2D, draw the same path nine times with small offsets and
`Opacity="0.16"` each. It approximates a box blur with zero dependencies.

**Never share one `Geometry` instance across multiple `Path.Data` assignments.** Geometry
objects carry an owner; the second assignment throws `ArgumentException 0x80070057`
inside `set_Data`, where a surrounding try/catch will not see it. Parse a fresh geometry
per path, or build each `Path` through `XamlReader.Load`.
