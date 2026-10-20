# 02 — Shell and Navigation

One `Window`, one `NavigationView`, pages hosted in a `Frame`. Resist the urge to add a
second top-level window for "secondary" features — route everything through the shell so
theme, backdrop, and settings state stay in one place.

## Layout

```xml
<Grid x:Name="RootGrid">
    <Grid.RowDefinitions>
        <RowDefinition Height="Auto" />   <!-- title bar / header -->
        <RowDefinition Height="*" />      <!-- NavigationView -->
    </Grid.RowDefinitions>

    <Grid x:Name="TitleBarArea" Grid.Row="0" Height="32" />
    <!-- custom title bar content goes here -->

    <NavigationView x:Name="NavView" Grid.Row="1"
                    PaneDisplayMode="Auto"
                    IsSettingsVisible="False"
                    IsBackButtonVisible="Collapsed"
                    IsTitleBarAutoPaddingEnabled="False"
                    OpenPaneLength="200"
                    CompactModeThresholdWidth="560"
                    ExpandedModeThresholdWidth="1008"
                    DisplayModeChanged="NavView_DisplayModeChanged"
                    SelectionChanged="NavView_SelectionChanged">
        <NavigationView.MenuItems>
            <NavigationViewItem Icon="Home" Content="Monitor" Tag="monitor" />
            <NavigationViewItem Content="Dashboard" Tag="dashboard">
                <NavigationViewItem.Icon>
                    <FontIcon Glyph="&#xE8A0;" />
                </NavigationViewItem.Icon>
            </NavigationViewItem>
        </NavigationView.MenuItems>

        <NavigationView.FooterMenuItems>
            <NavigationViewItem Icon="Setting" Content="Settings" Tag="settings" />
        </NavigationView.FooterMenuItems>

        <Frame x:Name="ContentFrame" />
    </NavigationView>
</Grid>
```

## Why these values

| Property | Value | Reason |
|---|---|---|
| `IsSettingsVisible` | `False` | The built-in Settings item pins to the bottom in a position you cannot control; a `FooterMenuItems` item gives you the slot and the icon |
| `IsBackButtonVisible` | `Collapsed` | Desktop apps navigate by selection, not by history; the back chrome is noise |
| `IsTitleBarAutoPaddingEnabled` | `False` | Required when you draw a custom title bar — otherwise the pane starts 32 px down |
| `OpenPaneLength` | `200` | The default 320 px eats a third of a 1000 px window |
| `PaneDisplayMode` | `Auto` | Three standard modes across widths — see below |
| `CompactModeThresholdWidth` | `560` | Below this the pane floats (LeftMinimal) |
| `ExpandedModeThresholdWidth` | `1008` | Above this the pane stays open; between the two it is a 48 px icon rail |

## The three pane modes, and the LeftMinimal overlap trap

| Window width | Mode | Behaviour |
|---|---|---|
| ≥ `ExpandedModeThresholdWidth` | Left | Pane docked, content beside it |
| between the thresholds | LeftCompact | 48 px icon rail, content beside it |
| < `CompactModeThresholdWidth` | **LeftMinimal** | **Pane floats on top of the content** |

In LeftMinimal the content extends under the pane, and the hamburger button sits at the
content's top-left corner — without extra handling it overlaps the first heading of every
page, and expanding the pane draws a translucent panel across the content.

The standard fix is to give the content host a top inset **only while in Minimal**, driven
by `DisplayModeChanged`:

```csharp
private void NavView_DisplayModeChanged(NavigationView sender, NavigationViewDisplayModeChangedEventArgs args)
{
    UpdateContentInset(args.DisplayMode);
}

private void UpdateContentInset(NavigationViewDisplayMode mode)
{
    ContentFrame.Margin = mode == NavigationViewDisplayMode.Minimal
        ? new Thickness(0, 48, 0, 0)   // clear the floating hamburger
        : new Thickness(0);
}
```

Call `UpdateContentInset(NavView.DisplayMode)` once in the window constructor too — the
event does not fire for the initial mode. This is what `assets/template/` ships.

Alternatives, when you do not want the floating pane at all:

1. **`PaneDisplayMode="Left"`** — pane docked at every width; simplest, but it cannot
   shrink on narrow windows, which is its own UX problem below ~600 DIP.
2. `Auto` with `CompactModeThresholdWidth` below your minimum window width — LeftMinimal
   is then unreachable.

Whichever you choose, verify at the narrowest window size you support.

## The floating pane needs acrylic, not transparency

With a Mica backdrop you are tempted to keep the pane background `Transparent` so the
backdrop shows through while docked. That same key paints the pane in LeftMinimal when
it expands **over the page** — a transparent overlay lets the page show through sharply,
which reads as a rendering bug.

Theme-resource overrides are the wrong lever here: which key the floating pane uses has
shifted across WinUI versions, and a single static brush cannot be "transparent while
docked, acrylic while overlaying". The reliable switch is the `SplitView` inside
NavigationView's own template — reached by name and driven directly:

```csharp
private readonly SolidColorBrush _paneInlineBrush = new(Microsoft.UI.Colors.Transparent);
private SplitView? _paneSplitView;
private AcrylicBrush? _paneOverlayAcrylic;

private void UpdatePaneBackdrop()
{
    _paneSplitView ??= FindChildByName(NavView, "RootSplitView") as SplitView;
    if (_paneSplitView is not SplitView sv) return;

    bool overlay = sv.DisplayMode is SplitViewDisplayMode.Overlay or SplitViewDisplayMode.CompactOverlay;
    sv.PaneBackground = overlay && sv.IsPaneOpen ? PaneOverlayAcrylic() : _paneInlineBrush;
}

private AcrylicBrush PaneOverlayAcrylic()
{
    bool dark = RootGrid.ActualTheme == ElementTheme.Dark;
    var tint = dark
        ? Windows.UI.Color.FromArgb(0xFF, 0x20, 0x20, 0x20)
        : Windows.UI.Color.FromArgb(0xFF, 0xF9, 0xF9, 0xF9);

    _paneOverlayAcrylic ??= new AcrylicBrush { TintOpacity = 0.6 };
    _paneOverlayAcrylic.TintColor = tint;
    _paneOverlayAcrylic.FallbackColor = tint;
    return _paneOverlayAcrylic;
}

private static FrameworkElement? FindChildByName(DependencyObject root, string name)
{
    int n = VisualTreeHelper.GetChildrenCount(root);
    for (int i = 0; i < n; i++)
    {
        var child = VisualTreeHelper.GetChild(root, i);
        if (child is FrameworkElement fe && fe.Name == name) return fe;
        var found = FindChildByName(child, name);
        if (found != null) return found;
    }
    return null;
}
```

Every state change that can move the pane between docked and overlay must re-run it —
five hooks, because no single event covers all transitions:

```csharp
NavView.Loaded     += (_, _) => UpdatePaneBackdrop();   // template (and RootSplitView) exists
NavView.PaneOpening += (_, _) => UpdatePaneBackdrop();
NavView.PaneOpened += (_, _) => UpdatePaneBackdrop();
NavView.PaneClosed += (_, _) => UpdatePaneBackdrop();
SizeChanged        += (_, _) => UpdatePaneBackdrop();   // width crosses a threshold
```

Also call it once after applying a theme — the acrylic tint colour follows the theme, and
the event does not fire for the initial mode on its own.

`AcrylicBrush` with `BackgroundSource` default blurs **in-app content beneath the brush**,
which is exactly what an overlay pane needs. `TintOpacity = 0.6` keeps the page faintly
visible, matching the system Settings look. `FallbackColor` must equal `TintColor`, so a
fallback frame degrades to a solid pane instead of a flash of transparency.

## Navigation handler

Drive everything from one handler keyed on `Tag`:

```csharp
private void NavView_SelectionChanged(NavigationView sender, NavigationViewSelectionChangedEventArgs args)
{
    if (args.SelectedItem is not NavigationViewItem item) return;
    if (item.Tag is not string tag) return;

    Type? page = tag switch
    {
        "monitor"   => typeof(MonitorPage),
        "dashboard" => typeof(DashboardPage),
        "settings"  => typeof(SettingsPage),
        _ => null
    };

    if (page is null || ContentFrame.CurrentSourcePageType == page) return;

    AppSettings.LastPage = tag;          // restore on next launch
    ContentFrame.Navigate(page, null, new EntranceNavigationTransitionInfo());
}
```

Guard on `CurrentSourcePageType == page` before navigating. Re-navigating to the same page
rebuilds the whole visual tree and throws away scroll position.

## Restoring the last page

After the window is activated, select the saved item instead of navigating directly:

```csharp
DispatcherQueue.TryEnqueue(() =>
{
    var target = NavView.MenuItems.Concat(NavView.FooterMenuItems)
        .OfType<NavigationViewItem>()
        .FirstOrDefault(i => (i.Tag as string) == AppSettings.LastPage);

    NavView.SelectedItem = target ?? NavView.MenuItems[0];
});
```

Setting `SelectedItem` fires `SelectionChanged`, so the navigation happens through the
normal path and the pane highlight stays correct.

## Custom title bar

```csharp
ExtendsContentIntoTitleBar = true;
SetTitleBar(TitleBarArea);
```

Then in XAML, add left padding equal to the caption button width if you right-align the
title bar content — otherwise your buttons slide under the caption buttons at certain
window widths.

Requirements for a draggable area: the element must be a `UIElement` with a non-null
background (`Transparent` is fine) and must not be covered by an interactive control.

## Page conventions

- Every page is a `Page` with `Background="{x:Null}"` (or no background at all) when a
  backdrop is active.
- Page constructors must not perform I/O. Load in `OnNavigatedTo`, release in
  `OnNavigatedFrom`.
- Subscribe to view-model events in `OnNavigatedTo` and unsubscribe in
  `OnNavigatedFrom`. A page cached by `NavigationCacheMode.Required` will otherwise
  accumulate handlers across navigations.

## Large windows

`MainWindow.xaml.cs` at 6000 lines is a symptom, not a style. Split by feature:

```
MainWindow.xaml.cs            # constructor, navigation, window-level plumbing
MainWindow.Monitor.cs         # monitoring page wiring
MainWindow.Settings.cs        # settings page wiring
MainWindow.<Feature>.cs
```

Partial classes share fields and methods, so there is no interface cost — only
discoverability improves.
