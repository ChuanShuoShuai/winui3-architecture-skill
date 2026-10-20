# 10 — Common Controls: Dialogs, Lists, Inputs, Feedback

Pick the right surface for each message, bind lists without killing virtualization,
and avoid the event-timing traps that make initialization flicker.

## Choosing a message surface

WinUI has four "something to tell the user" surfaces. They are not interchangeable:

| Surface | Blocks input? | Dismissal | Use for |
|---|---|---|---|
| `ContentDialog` | **Yes** (modal) | Buttons | Decisions: confirm delete, pick one of two paths |
| `InfoBar` | No | Close button, optional | Status the user should see but can ignore: update available, long-running job done |
| `TeachingTip` | No | Light dismiss or button | One-time guidance anchored to a control: "click here first" |
| `ToolTip` | No | Mouse leave | Micro-explanation on hover |

Decision rule: modal for **decisions**, InfoBar for **status**, TeachingTip for
**guidance**, ToolTip for **hover hints**. Using `ContentDialog` for status is the most
common misuse — it steals focus and queues behind other dialogs.

`InfoBar` severity: `Informational` / `Success` / `Warning` / `Error`. Set `IsOpen` from
code; keep `IsClosable="True"` unless the condition is blocking.

```xml
<InfoBar x:Name="UpdateBar"
         Title="A new version is available"
         Message="Restart to apply."
         Severity="Informational"
         IsOpen="False" />
```

`TeachingTip` needs an anchor — `Target="{x:Bind SomeButton}"` — or it floats centred.
For "show once", persist a `Settings` flag and set `IsOpen` only when it is unset.

## Lists

### ListView with items

```xml
<ListView x:Name="ProcessList"
          ItemsSource="{x:Bind ViewModel.Items}"
          SelectionMode="Single"
          IsItemClickEnabled="False">
    <ListView.ItemTemplate>
        <DataTemplate x:DataType="local:ProcessRow">
            <Grid ColumnSpacing="8" Height="36">
                <Grid.ColumnDefinitions>
                    <ColumnDefinition Width="*" />
                    <ColumnDefinition Width="80" />
                </Grid.ColumnDefinitions>
                <TextBlock Text="{x:Bind Name}" VerticalAlignment="Center"
                           TextTrimming="CharacterEllipsis" />
                <TextBlock Grid.Column="1" Text="{x:Bind MemoryText}"
                           HorizontalAlignment="Right" VerticalAlignment="Center" />
            </Grid>
        </DataTemplate>
    </ListView.ItemTemplate>
</ListView>
```

Two hard rules:

1. **Never wrap the item template in a fixed-height `ScrollViewer`.** `ListView`
   virtualizes through its own `ScrollViewer`; a nested one defeats it and the control
   builds a container for every row. At 5 000 rows that is a multi-second freeze.
2. **Do not replace the items panel with `StackPanel`.** The default
   `ItemsStackPanel` performs virtualization; a plain `StackPanel` does not. If you must
   customise, keep an `ItemsStackPanel`-derived panel:
   `<ListView.ItemsPanel><ItemsPanelTemplate><ItemsStackPanel /></ItemsPanelTemplate></ListView.ItemsPanel>`.

### Data updates: reconcile, do not replace

For a polling list (processes, feeds, prices), update row objects in place and raise
`PropertyChanged` per changed property. Reassigning `ItemsSource` raises a reset that
rebuilds every visible container, re-queries icons, and resets scroll position — visible
as a flicker every tick.

Keep one `ObservableCollection<T>` (or a reconcile helper) for the lifetime of the page.

### Selection and item click

- `SelectionMode="Single"` + read `SelectedItem` when an action runs.
- `IsItemClickEnabled="True"` + `ItemClick` event for "tap row = open detail"; in this
  mode selection is optional.
- `SelectionMode="Extended"` gives ctrl/shift multi-select; read `SelectedItems`.

### When ListView is the wrong control

- Pure read-only grid of many columns: `DataGrid` (community `CommunityToolkit.Datagrid`)
  or manual `Grid` headers + `ListView`.
- Heterogeneous layouts / extreme scale: `ItemsRepeater` — no selection or
  virtualization plumbing for free, full layout control.

## Selection inputs

### ComboBox

```xml
<ComboBox x:Name="IntervalBox" Header="Refresh interval" Width="200"
          SelectionChanged="IntervalBox_SelectionChanged">
    <ComboBoxItem Content="1 second" />
    <ComboBoxItem Content="2 seconds" />
    <ComboBoxItem Content="3 seconds" />
</ComboBox>
```

Trap: **setting `SelectedIndex` in code raises `SelectionChanged`.** When mirroring
stored settings into the control on page entry, set a `_loading` flag first and return
early from the handler while it is set — otherwise your "user changed interval" logic
runs once at startup and writes settings the user never touched.

If you keep `ObservableCollection<ComboBoxItem>` in code, prefer binding a list of plain
objects with `DisplayMemberPath="Label"` and `SelectedValuePath="Value"` — casting
`SelectedItem as ComboBoxItem` for the content is fragile.

### CheckBox, ToggleSwitch, RadioButtons

```xml
<CheckBox x:Name="AutoStartCheck" Content="Start with Windows" />

<ToggleSwitch x:Name="MonitorSwitch" Header="Real-time monitor"
              OnContent="On" OffContent="Paused" />

<RadioButtons Header="Theme">
    <RadioButton Content="Follow system" />
    <RadioButton Content="Light" />
    <RadioButton Content="Dark" />
</RadioButtons>
```

- `CheckBox.IsThreeState="True"` adds an indeterminate value; read `IsChecked as bool?`
  and handle `null`.
- `ToggleSwitch.Toggled` has the same init-time firing trap as `ComboBox.SelectionChanged`.
- `RadioButtons` (the WinUI wrapper, not bare `RadioButton`s) gives you one
  `SelectionChanged` for the group and keyboard navigation for free.

## Text inputs

```xml
<TextBox Header="API endpoint"
         PlaceholderText="https://..."
         TextChanging="Endpoint_TextChanging" />

<PasswordBox Header="API key" PasswordRevealMode="Peek" />
```

- `TextChanged` fires after the change is committed; `TextChanging` fires **during**
  composition (useful for live validation that must not wait).
- Neither fires for IME composition until it commits — Chinese/Japanese input shows the
  candidate string without events. If you must track it, handle `TextChanging` and accept
  intermediate values.
- `PasswordBox.Password` is a plain string property, not a dependency property — it does
  not support `{x:Bind}` one-way to a view model. Copy it in code.
- `NumberBox` for numeric input: set `Min`/`Max`, read `Value` (double), and handle
  `ValidationMode="InvalidInputOverwritten"` to reject garbage.

## Progress and busy states

```xml
<ProgressBar x:Name="ScanProgress" Maximum="100" Value="0" />

<ProgressRing x:Name="BusyRing" IsActive="{x:Bind ViewModel.Busy, Mode=OneWay}" />
```

- `ProgressRing.IsActive="False"` hides it entirely — bind `IsActive`, not `Visibility`.
- `ProgressBar.IsIndeterminate="True"` for unknown duration.
- For a scan of known item count: `Maximum = itemCount`, `Value = doneCount`, and update
  from the worker thread through `DispatcherQueue.TryEnqueue` — touching XAML from a
  worker thread throws a COM exception that looks unrelated to threading.

## Flyouts and menus

```xml
<Button Content="Options">
    <Button.Flyout>
        <MenuFlyout>
            <MenuFlyoutItem Text="Export CSV" Click="ExportCsv_Click" />
            <MenuFlyoutItem Text="Export HTML" Click="ExportHtml_Click" />
            <MenuFlyoutSeparator />
            <MenuFlyoutItem Text="Clear history" Click="Clear_Click" />
        </MenuFlyout>
    </Button.Flyout>
</Button>
```

- A `Flyout` holds arbitrary content (a small form, a colour picker);
  `MenuFlyout` is menu items only.
- `LightDismissOverlayMode="On"` on the flyout's host dims the background.
- Flyouts auto-close on outside click. For a click inside that must **keep** the flyout
  open (e.g. typing in a filter box), call `FlyoutBase.GetAttachedFlyout(sender)` and
  manage `IsOpen` yourself, or use a `Popup`.

## Toast from a page

See `07-tray-and-toast.md` for the full lifecycle. From a button:

```csharp
new ToastContentBuilder()
    .AddText("Scan finished")
    .AddText($"{count} processes checked")
    .AddArgument("action", "open")
    .Show();
```

## Checkboxes of the state you forgot

A short pre-ship list for control-heavy pages:

- [ ] Every `SelectionChanged` / `Toggled` handler is guarded against init-time firing.
- [ ] Long lists still scroll smoothly with 5 000 items (no nested `ScrollViewer`).
- [ ] IME (Chinese) input into the search box updates results without committing.
- [ ] `ProgressRing` disappears when `IsActive` goes false, not just when the page unloads.
- [ ] Every `ContentDialog` was tested twice in a row (the double-open exception).
- [ ] Flyouts close on outside click; flyouts containing text input stay open while typing.
