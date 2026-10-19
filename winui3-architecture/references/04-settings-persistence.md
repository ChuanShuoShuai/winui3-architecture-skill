# 04 — Settings Persistence

Store configuration in `%LocalAppData%\<AppName>\settings.json`. Not next to the
executable (install directories are read-only), not in the registry (uninstall leaves
garbage), not in `AppData\Roaming` (this is machine-local UI state).

## Shape

A static class with plain properties. No DI container, no `IOptionsMonitor` — a desktop
app with one settings file does not need the ceremony.

```csharp
public static class AppSettings
{
    private static readonly string Dir = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "MyApp");
    private static readonly string FilePath = Path.Combine(Dir, "settings.json");

    public static string SettingsFilePath => FilePath;   // for import/export UI

    public static int ThemeIndex { get; set; }                 // 0 system, 1 light, 2 dark
    public static int BackdropMode { get; set; } = 2;          // 0 none, 1 acrylic, 2 mica
    public static int ScanIntervalSec { get; set; } = 3;
    public static List<string> PinnedApps { get; set; } = new();
}
```

## Loading: per-key, never whole-object deserialise

`JsonSerializer.Deserialize<AppSettings>()` fails the whole file when one property has an
unexpected type, and it silently drops keys that no longer exist in the class. Read key
by key with `TryGetProperty` so a partial or older file still yields everything it does
contain:

```csharp
public static void Load()
{
    try
    {
        if (!File.Exists(FilePath)) return;
        using var doc = JsonDocument.Parse(File.ReadAllText(FilePath));
        var root = doc.RootElement;

        if (root.TryGetProperty("ThemeIndex", out var t)) ThemeIndex = t.GetInt32();
        if (root.TryGetProperty("BackdropMode", out var b)) BackdropMode = b.GetInt32();
        if (root.TryGetProperty("ScanIntervalSec", out var s)) ScanIntervalSec = s.GetInt32();

        if (root.TryGetProperty("PinnedApps", out var p) && p.ValueKind == JsonValueKind.Array)
        {
            PinnedApps = p.EnumerateArray()
                .Where(x => x.ValueKind == JsonValueKind.String)
                .Select(x => x.GetString()!)
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .ToList();
        }
    }
    catch { /* corrupt file: keep defaults */ }
}
```

The empty `catch` is deliberate: a corrupt settings file must never block startup. The
next `Save()` overwrites it with valid content.

## Saving

Serialize an anonymous object so the on-disk key set is explicit and reviewable:

```csharp
public static void Save()
{
    try
    {
        Directory.CreateDirectory(Dir);
        File.WriteAllText(FilePath, JsonSerializer.Serialize(new
        {
            ThemeIndex,
            BackdropMode,
            ScanIntervalSec,
            PinnedApps
        }));
    }
    catch { /* read-only disk, locked file: ignore */ }
}
```

Write on change *and* on exit. Writing on every slider tick is fine — the file is a few
hundred bytes.

## The default-change migration trap

This one is subtle and hits every long-lived app.

Suppose refresh interval defaults to 2 seconds. A user installs, never touches the
setting, and `settings.json` records `ScanIntervalSec: 2`. Later you change the default
to 3 seconds. On upgrade, `Load()` reads `2` — which is indistinguishable from a user who
deliberately chose 2. **The new default never reaches existing users.**

Fix with a version marker:

```csharp
private const int IntervalDefaultsVersion = 2;   // bump whenever the default changes
public static int IntervalDefaultsApplied { get; set; } = IntervalDefaultsVersion;

// in Load(), after reading values
bool migrated = false;
if (IntervalDefaultsApplied < IntervalDefaultsVersion)
{
    ScanIntervalSec = 3;                          // new default
    IntervalDefaultsApplied = IntervalDefaultsVersion;
    migrated = true;
}
if (ScanIntervalSec is not (1 or 2 or 3))         // defensive clamp
{
    ScanIntervalSec = 3;
    migrated = true;
}

if (migrated) Save();
```

### Ordering rule

`if (migrated) Save();` must be the **last** statement in `Load()`, after every property
has been read. Calling `Save()` mid-load serialises the not-yet-read properties at their
defaults and silently wipes the user's configuration.

## Validation and clamping

Never trust the file. Clamp every numeric that drives UI or timing:

```csharp
if (ScanIntervalSec is not (1 or 2 or 3)) ScanIntervalSec = 3;
if (BackdropMode is < 0 or > 2) BackdropMode = 2;
```

A `ScrollViewer` with `ZoomFactor = 0` from a hand-edited file freezes the UI in a way
that looks like a hang, not a settings bug.

## Export / import

Expose `SettingsFilePath` and let the user copy the JSON. For a proper save dialog, use
the Win32 common dialog rather than `FileSavePicker` — the latter throws in unpackaged
apps on some Windows 10 builds.

```csharp
// Services/ClassicFileDialog.cs — GetSaveFileNameW via P/Invoke
var path = ClassicFileDialog.PickSaveFile(ownerHandle,
    filter: "JSON (*.json)|*.json|All files (*.*)|*.*",
    defaultExt: "json");
```

Write text files with `new UTF8Encoding(true)` (with BOM). Without the BOM, Excel and
several Chinese-locale editors open exported CSV/JSON as mojibake.

## Checklist

- [ ] Delete the file → app starts with defaults, no crash.
- [ ] Truncate the file to `{` → app starts with defaults, no crash.
- [ ] Change a value → restart → value persists.
- [ ] Bump the default + version marker → old config migrates once, then respects user choice.
- [ ] Settings file lives under `%LocalAppData%`, not beside the exe.
