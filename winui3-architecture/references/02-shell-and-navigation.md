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
                    CompactModeThresholdWidth="1000"
                    ExpandedModeThresholdWidth="1200"
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
| `CompactModeThresholdWidth` | `1000` | Below this the pane collapses to icons only |
| `ExpandedModeThresholdWidth` | `1200` | Above this it stays open; between the two it uses the last user choice |

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
