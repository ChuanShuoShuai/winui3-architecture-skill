# 08 — Custom Controls and UI Conventions

WinUI 3's built-in controls cover 80 % of an app. These conventions keep the remaining
20 % consistent and avoid the runtime traps in XAML.

## Converters

Two converters carry most apps:

```csharp
public sealed class NullToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language)
        => value is null ? Visibility.Visible : Visibility.Collapsed;

    public object ConvertBack(object value, Type targetType, object parameter, string language)
        => throw new NotSupportedException();
}

public sealed class BoolToVisibilityConverter : IValueConverter
{
    public bool Invert { get; set; }

    public object Convert(object value, Type targetType, object parameter, string language)
    {
        bool b = value is bool v && v;
        if (Invert) b = !b;
        return b ? Visibility.Visible : Visibility.Collapsed;
    }

    public object ConvertBack(object value, Type targetType, object parameter, string language)
        => throw new NotSupportedException();
}
```

Register once in `App.xaml`:

```xml
<conv:NullToVisibilityConverter x:Key="NullToVisibleConverter" />
<conv:BoolToVisibilityConverter x:Key="BoolToVisibleConverter" />
```

### Icon placeholder pattern

Program icons are bitmaps with transparent pixels. Drawing a glyph underneath and relying
on transparency shows the glyph through the icon's empty areas. Make them mutually
exclusive instead:

```xml
<Grid Width="22" Height="22">
    <FontIcon Glyph="&#xE71D;" FontSize="14"
              Foreground="{ThemeResource TextFillColorTertiaryBrush}"
              Visibility="{Binding Icon, Converter={StaticResource NullToVisibleConverter}}" />
    <Image Source="{Binding Icon}" Width="22" Height="22" Stretch="Uniform" />
</Grid>
```

The converter shows the glyph only when `Icon` is `null`; the `Image` renders nothing when
its source is null.

## Card templates

Use `DataTemplate` resources for repeated rows. Keep every text element's alignment and
`TextTrimming="CharacterEllipsis"` explicit — cards that grow to fit long names destroy
the visual rhythm.

```xml
<DataTemplate x:Key="ItemCardTemplate">
    <Border Background="{ThemeResource CardBackgroundFillColorDefaultBrush}"
            CornerRadius="8" Padding="12,10" Margin="0,0,0,8">
        <Grid>
            <Grid.ColumnDefinitions>
                <ColumnDefinition Width="4" />
                <ColumnDefinition Width="*" />
            </Grid.ColumnDefinitions>
            <Border Width="4" CornerRadius="2"
                    Background="{ThemeResource AccentFillColorDefaultBrush}" />
            <StackPanel Grid.Column="1" Margin="10,0,0,0" Spacing="2">
                <TextBlock Text="{Binding Title}" FontWeight="SemiBold"
                           TextTrimming="CharacterEllipsis" />
                <TextBlock Text="{Binding Subtitle}"
                           Style="{StaticResource CaptionTextBlockStyle}"
                           Foreground="{ThemeResource TextFillColorSecondaryBrush}" />
            </StackPanel>
        </Grid>
    </Border>
</DataTemplate>
```

## Icon + text buttons

Standardise every action button on `FontIcon + TextBlock` so icons line up across the app.

**XAML:**

```xml
<Button>
    <StackPanel Orientation="Horizontal" Spacing="6">
        <FontIcon Glyph="&#xE8E5;" FontSize="14" />
        <TextBlock Text="Refresh" />
    </StackPanel>
</Button>
```

**Code:**

```csharp
public static TextBlock SetButtonIconText(Button button, string glyph, string text)
{
    var panel = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 };
    panel.Children.Add(new FontIcon { Glyph = glyph, FontSize = 14 });
    var label = new TextBlock { Text = text };
    panel.Children.Add(label);
    button.Content = panel;
    return label;
}
```

When the label text changes at runtime, mutate the returned `TextBlock.Text`. Assigning
`button.Content = "new text"` silently discards the icon.

Table header sort buttons are the exception: no icon, just a `TextBlock` with a sort
chevron appended.

## Building UI in code

Prefer XAML. Reach for code when the structure depends on data (unknown column count,
dynamic form).

```csharp
var panel = new StackPanel { Spacing = 8 };
foreach (var field in schema.Fields)
{
    panel.Children.Add(new TextBlock { Text = field.Label, FontWeight = FontWeights.SemiBold });
    panel.Children.Add(new TextBox { Text = field.Value ?? string.Empty });
}
Host.Children.Add(panel);
```

Rules:

- Set `Spacing` / `Margin` on the container, not each child.
- Use `ThemeResource` brushes via `ThemeHelper.Brush(...)`, never hard-coded colours.
- Call `Microsoft.UI.Xaml.Markup.XamlReader.Load(string)` when you need a real `Geometry`
  or a template; there is no public `Geometry.Parse`.

## ContentDialog

Dialogs are the most common source of self-inflicted bugs.

**Nesting is forbidden.** `ShowAsync` throws if called while another `ContentDialog` is
open. For a drill-down, record the intent, hide the current dialog, and open the next one
after the hide completes:

```csharp
private async Task ShowDetail(Item item)
{
    while (true)
    {
        var dialog = BuildDetailDialog(item);
        var result = await dialog.ShowAsync();

        if (_pendingDrillDown is { } child) { _pendingDrillDown = null; item = child; continue; }
        if (_pendingBack) { _pendingBack = false; item = Stack.Pop(); continue; }
        return;
    }
}
```

**Content must be a `Grid` with `Auto/*/Auto` rows**, not a `StackPanel`. A `ContentDialog`
clips overflowing content instead of scrolling it — the last items in a tall `StackPanel`
are simply invisible with no scrollbar.

**Set spacing with margins**, not `RowSpacing`: `RowSpacing` also applies around collapsed
rows and produces gaps that look like a rendering bug.

**Guard against double-clicks.** `ShowAsync` throws `"Only one ContentDialog can be open"`
if a button is clicked twice before the first dialog renders. Use a `_showing` flag set
synchronously before `await`.

## Resource keys: verify before use

Referencing a `ThemeResource` key that does not exist:

- Compiles with **no warning**.
- Throws `XamlParseException` at runtime, typically killing the whole page.

Before adding an unfamiliar key, confirm it exists elsewhere in the project:

```bash
grep -c "SomeControlBrush" -r .
```

A count above 1 means the key is already proven to resolve.

## Checklist

- [ ] No literal colours in XAML; every colour is `{ThemeResource ...}` or a semantic brush.
- [ ] Icon + text buttons use the shared helper.
- [ ] Dialog content is a `Grid`; no `RowSpacing`.
- [ ] Every new `ThemeResource` key was grep-verified.
- [ ] Both themes rendered once with the new UI before shipping.
