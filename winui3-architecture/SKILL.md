---
name: winui3-architecture
description: Build production-grade WinUI 3 desktop applications on a proven architecture. Use when scaffolding a new WinUI 3 / Windows App SDK app, refactoring an existing one toward a standard shell, or diagnosing WinUI 3 runtime crashes. Covers single-instance startup with crash forensics, NavigationView shell navigation, Mica/Acrylic backdrop switching with graceful degradation, settings persistence with schema migration, self-contained packaging with Inno Setup, tray and toast integration, and custom control conventions. Targets unpackaged (WindowsPackageType=None) x64 apps on Windows 10 1809 through Windows 11.
agent_created: true
---

# WinUI 3 Application Architecture

Ship a WinUI 3 desktop app that starts reliably, degrades gracefully on Windows 10, and survives the non-obvious runtime failures that produce no compile-time error.

## When to use this skill

- Scaffolding a new WinUI 3 desktop project that must run unpackaged (no MSIX).
- Adding a shell to an existing app: navigation, theming, settings, tray.
- Diagnosing "the app exits silently", "XamlParseException on startup", "Mica does not show".
- Preparing a release build with a self-contained payload and an installer.

Do not use this skill for UWP, WPF, or MAUI apps. The XAML dialect and lifecycle differ.

## Architecture at a glance

```
MyApp/
├─ App.xaml / App.xaml.cs        # composition root: mutex, crash log, window creation
├─ MainWindow.xaml(.cs)          # shell only: NavigationView + page host
├─ MainWindow.<Feature>.cs       # partial-class split for large features
├─ MicaSystemBackdrop.cs         # SystemBackdrop subclass, Mica/Acrylic/None
├─ AppSettings.cs                # static settings, JSON on disk, migration marker
├─ ThemeHelper.cs                # theme-safe resource lookup from code
├─ Models/                       # INotifyPropertyChanged view models
├─ Converters/                   # IValueConverter implementations
├─ Services/                     # OS interop, dialogs, rendering
└─ Core/                         # pure logic, no UI dependency (unit-testable)
```

Two rules carry most of the weight:

1. **`Core/` never references `Microsoft.UI.*`.** Pure logic stays headless-testable.
2. **`MainWindow.xaml.cs` holds the shell, not the features.** Split features into
   `MainWindow.<Feature>.cs` partial files once a file passes ~800 lines.

## Scaffolding sequence

Follow this order; each step depends on the previous one compiling.

### 1. Project file

Start from `assets/template/Project.csproj.template`. The non-obvious properties:

```xml
<WindowsPackageType>None</WindowsPackageType>          <!-- unpackaged: no MSIX, no Store -->
<TargetFramework>net10.0-windows10.0.19041.0</TargetFramework>
<TargetPlatformMinVersion>10.0.17763.0</TargetPlatformMinVersion>  <!-- Win10 1809 -->
<Platforms>x64</Platforms>
<RuntimeIdentifier>win-x64</RuntimeIdentifier>
<WindowsAppSDKSelfContained>true</WindowsAppSDKSelfContained>
<SatelliteResourceLanguages>zh-Hans;zh-Hant;en-US</SatelliteResourceLanguages>
```

`SatelliteResourceLanguages` alone removes tens of megabytes of unused localization
folders. `WindowsAppSDK` stays self-contained even when .NET is framework-dependent —
do not confuse the two switches.

### 2. App: mutex, crash log, window creation

Copy `assets/template/App.xaml.cs`. It establishes, in this order:

1. `UnhandledException` hook wired **in the constructor**, before any XAML loads.
2. A named mutex (`Local\<Name>_SingleInstance`) at the top of `OnLaunched`.
3. A named pipe to forward activation arguments from a second instance.
4. `MainWindow` creation wrapped in try/catch that writes `crash.log`.

Log the startup chain step by step (`App ctor → OnLaunched → window created → activated`).
When a WinUI app dies before the first frame, this log is the only evidence left.

### 3. Shell: NavigationView

Copy `assets/template/MainWindow.xaml`. The shell is a two-row `Grid`:

- Row 0: custom title bar / header strip (`Auto`).
- Row 1: `NavigationView` with `MenuItems` (primary pages) and `FooterMenuItems`
  (settings and secondary destinations).

Keep `IsSettingsVisible="False"` and put Settings in `FooterMenuItems` so its position
is under your control. Use `Tag` to identify destinations and drive navigation from a
single `SelectionChanged` handler.

### 4. Theme and backdrop

Copy `assets/template/MicaSystemBackdrop.cs` and `assets/template/ThemeHelper.cs`.
The backdrop resolves user intent against machine capability:

```csharp
Mica    → Acrylic  when !MicaController.IsSupported()
Acrylic → None     when !DesktopAcrylicController.IsSupported()
```

Store the user's *intent*, display the machine's *effective* value. On Windows 10,
grey out the Mica option and fall back — never let the selection silently do nothing.

### 5. Settings

Copy `assets/template/AppSettings.cs`. Use a static class with plain properties plus
`Load()` / `Save()`. Read with `JsonDocument` and `TryGetProperty` per key so a
truncated or older file never throws. Two details that bite later:

- **Defaults that change over time need a version marker.** When a default flips
  (e.g. refresh interval 2s → 3s), old configs hold the *old default*, not a user
  choice. Store `XxxDefaultsApplied` and migrate once.
- **Persist the migration after all properties are read.** Writing inside `Load()`
  before the rest of the file is parsed overwrites unread keys with defaults.

### 6. Pages and view models

Pages live in `Pages/` and are hosted in a `Frame` inside the NavigationView content.
View models implement `INotifyPropertyChanged` and expose *derived text* properties
(`CpuText`, `MemoryText`, `StatusText`) that raise `PropertyChanged` when their source
fields change. Bind to those, not to `StringFormat` converters — WinUI has no
`StringFormat`.

For collections that update on a timer, reconcile in place instead of replacing the
collection. Replacing raises `Reset`, which tears down and rebuilds every visible
container.

## Non-negotiable rules

These produce no compiler error and no exception at the point of the mistake. Each one
has cost a production release.

| Rule | Failure mode |
|---|---|
| `x:Name` must not collide with a method in the same class | `CS0102`, followed by a cascade of bogus "Unknown type" XAML errors — fix the collision first, ignore the cascade |
| Only reference `ThemeResource` keys that already exist in the project | Compiles fine, throws `XamlParseException` at runtime and kills the page |
| Never assign one `Geometry` instance to two `Path.Data` properties | `ArgumentException 0x80070057` thrown inside `set_Data`, not caught by surrounding try/catch |
| `ContentDialog` cannot nest `ShowAsync` | Chain dialogs by hiding the current one and reopening after `await` returns |
| `Microsoft.UI.Xaml.Window` has no `Visibility` | Track "hidden to tray" with your own flag |
| `PaneDisplayMode="Auto"` in LeftMinimal floats the pane over the page | Hamburger lands on page titles — add a 48 px top inset via `DisplayModeChanged`, or pin `Left` |
| The pane toggle takes keyboard focus on launch | A heavy focus rectangle is painted over the hamburger |
| Do not set an opaque background on `Window` / `NavigationView` / page root when using a backdrop | The backdrop is drawn but covered; the window looks flat |
| Never call `PerformanceCounter` per instance in a polling loop | Megabyte-per-tick allocations; use PDH array queries |

## Verification checklist

Before declaring the app done:

- [ ] `dotnet build -c Release -p:Platform=x64` → 0 errors, 0 warnings from your code.
- [ ] Delete `%LocalAppData%\<App>\settings.json`, launch, confirm defaults apply.
- [ ] Launch twice → second instance exits, first instance comes to front.
- [ ] Switch theme Light → Dark → System with each backdrop mode; no visual flash.
- [ ] Resize below 1000 px width → pane collapses to compact; above 1200 → expands.
- [ ] Publish self-contained, install to a clean VM, launch with no .NET installed.
- [ ] Force an exception in a page constructor → `crash.log` contains the inner-exception chain.

## Reference map

Load only what the task needs.

| File | Read when |
|---|---|
| `references/01-startup-and-single-instance.md` | App lifecycle, mutex, activation forwarding, crash log format |
| `references/02-shell-and-navigation.md` | NavigationView tuning, page hosting, title bar, adaptive pane |
| `references/03-theming-and-backdrop.md` | Mica/Acrylic, theme dictionaries, code-side brush lookup |
| `references/04-settings-persistence.md` | Config schema, migration markers, import/export |
| `references/05-diagnostics-and-crash-forensics.md` | Startup log, crash log, dump triage, silent-exit diagnosis |
| `references/06-packaging-and-deployment.md` | Self-contained publish, Inno Setup, payload bloat, runtime prerequisites |
| `references/07-tray-and-toast.md` | Tray icon, notification activation, single-instance argument forwarding |
| `references/08-custom-controls.md` | Converter conventions, card templates, code-built UI, icon+text buttons |
| `references/09-window-targeting.md` | Hit-testing top-level windows, screen capture, overlay picker |
| `references/10-common-controls.md` | Choosing InfoBar vs ContentDialog vs TeachingTip, ListView virtualization, ComboBox/ToggleSwitch init-time event traps |

## Templates

`assets/template/` contains a minimal compiling skeleton. Copy the whole folder into a
new project, then rename the namespace. Each file carries the comments that matter —
read them before editing, they explain why the code is shaped that way.
