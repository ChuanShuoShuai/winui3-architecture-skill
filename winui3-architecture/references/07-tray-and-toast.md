# 07 — Tray Icon and Toast Notifications

Both features break the normal window lifecycle, and both interact with the single-instance
mutex. Implement them **after** startup and settings are stable.

## Tray icon

Use `H.NotifyIcon.WinUI`. It is the maintained package that works with WinUI 3 and
provides a `TaskbarIcon` you can declare in XAML.

```xml
<Window ...
        xmlns:tb="using:H.NotifyIcon">
    <Grid>
        <tb:TaskbarIcon x:Name="TrayIcon"
                        IconSource="Assets/app.ico"
                        ToolTipText="MyApp"
                        NoLeftClickDelay="True"
                        LeftClickCommand="{x:Bind ShowWindowCommand}"
                        ContextMenuMode="SecondWindow">
            <tb:TaskbarIcon.ContextFlyout>
                <MenuFlyout>
                    <MenuFlyoutItem Text="Open" Click="TrayOpen_Click" />
                    <MenuFlyoutItem Text="Exit" Click="TrayExit_Click" />
                </MenuFlyout>
            </tb:TaskbarIcon.ContextFlyout>
        </tb:TaskbarIcon>
    </Grid>
</Window>
```

### Hiding to tray

`Microsoft.UI.Xaml.Window` has **no** `Visibility` property. Hiding means removing the
window from the taskbar and moving it off-screen; track the state yourself.

```csharp
private bool _hiddenToTray;

public void HideToTray()
{
    _hiddenToTray = true;
    var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(this);
    ShowWindow(hwnd, SW_HIDE);
}

public void RestoreFromTray()
{
    _hiddenToTray = false;
    var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(this);
    ShowWindow(hwnd, SW_SHOW);
    SetForegroundWindow(hwnd);
    Activate();
}
```

Everything that branches on "is the window visible" must read `_hiddenToTray`, not
`AppWindow.IsVisible` (which stays true for an off-screen window on some builds).

### Close-to-tray

Handle `AppWindow.Closing` and cancel it unless the exit is intentional:

```csharp
AppWindow.Closing += (s, e) =>
{
    if (!_reallyExiting && AppSettings.CloseToTray)
    {
        e.Cancel = true;
        HideToTray();
    }
};
```

Set `_reallyExiting = true` from the tray menu's Exit item before calling `Close()`.
Without the flag, the tray Exit item hides the window again and the process never ends.

## Toast notifications

Package: `Microsoft.Toolkit.Uwp.Notifications` (7.x). It registers the COM activator
automatically on first use in an unpackaged app.

### Show

```csharp
new ToastContentBuilder()
    .AddText("High-risk process detected")
    .AddText($"{name} (PID {pid})")
    .AddArgument("action", "open")
    .AddArgument("pid", pid.ToString())
    .AddButton(new ToastButton()
        .SetContent("Terminate")
        .AddArgument("action", "kill")
        .AddArgument("pid", pid.ToString()))
    .Show();
```

Keep the argument payload short and flat. It is serialised into the activation string and
survives across process boundaries.

### Activate

```csharp
ToastNotificationManagerCompat.OnActivated += e =>
{
    var args = e.Argument;                     // "action=kill&pid=1234"
    if (MainWindow != null)
        MainWindow.DispatcherQueue.TryEnqueue(() => HandleActivation(args));
    else
        _pendingActivation = args;             // window not created yet
};
```

The callback arrives on a **COM thread**, never the UI thread. Always marshal through
`DispatcherQueue`. Touching XAML from the callback thread throws a COM exception that
looks unrelated to notifications.

### The cold-start case

Clicking a toast while the app is not running launches a new process with the activation
argument. That process hits the single-instance mutex and must forward the argument to the
running instance — or, if it *is* the first instance, store it and process it after the
window exists.

Sequence:

```
toast clicked (app not running)
  → new process starts
  → mutex: createdNew == true  (it is the only instance)
  → ToastNotificationManagerCompat.WasCurrentProcessToastActivated() == true
  → OnActivated fires, _pendingActivation is null → store
  → window created
  → drain _pendingActivation
```

If another instance *is* running, `createdNew == false` and the new process forwards the
argument over the named pipe described in `01-startup-and-single-instance.md`.

**Test all four combinations**: app running / not running × toast clicked / toast button
clicked.

## Registration prerequisites

Unpackaged toast notifications need a registered AppUserModelID. `Microsoft.Toolkit.Uwp.Notifications`
handles this, but the AUMID must be stable across versions — derive it from a fixed GUID,
not from the install path.

```csharp
// Once, at startup
ToastNotificationManagerCompat.CreateToastNotifier();
```

If notifications never appear:

1. Windows Settings → Notifications → is the app listed and enabled?
2. Focus Assist / Do Not Disturb silently swallows everything.
3. The AUMID changed between builds → old registration is orphaned. Clear
   `HKCU\Software\Classes\AppUserModelId\<your-aumid>` and re-register.

## Checklist

- [ ] Tray icon appears with the correct icon at 100 % and 150 % DPI.
- [ ] Close button hides to tray; tray Exit actually terminates the process.
- [ ] Toast with a button, app running → button action executes on the UI thread.
- [ ] Toast with a button, app closed → app starts, window opens, action executes once.
- [ ] No duplicate execution when a second instance forwards the activation.
