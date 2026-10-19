using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace MyApp;

/// <summary>
/// Settings persisted to %LocalAppData%\MyApp\settings.json.
///
/// Loading is per-key on purpose: a partially written or older file must never
/// throw, and unknown keys must be ignored rather than failing the whole parse.
/// </summary>
public static class AppSettings
{
    private static readonly string Dir = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "MyApp");

    private static readonly string FilePath = Path.Combine(Dir, "settings.json");

    /// <summary>Full path to settings.json, for import / export UI.</summary>
    public static string SettingsFilePath => FilePath;

    // ---- persisted values -------------------------------------------------

    /// <summary>0 = follow system, 1 = light, 2 = dark.</summary>
    public static int ThemeIndex { get; set; }

    /// <summary>0 = none, 1 = acrylic, 2 = mica.</summary>
    public static int BackdropMode { get; set; } = 2;

    public static int WindowWidth { get; set; } = 1200;
    public static int WindowHeight { get; set; } = 800;

    /// <summary>Navigation Tag of the last visited page, restored on launch.</summary>
    public static string LastPage { get; set; } = "home";

    public static List<string> RecentItems { get; set; } = new();

    // ---- default-change migration ----------------------------------------

    /// <summary>
    /// Bump whenever a default value changes. Old settings files contain the
    /// previous default, which is indistinguishable from a deliberate choice;
    /// this marker lets Load() migrate exactly once.
    /// </summary>
    private const int DefaultsVersion = 1;

    public static int DefaultsApplied { get; set; } = DefaultsVersion;

    // ---- load / save ------------------------------------------------------

    public static void Load()
    {
        try
        {
            if (!File.Exists(FilePath)) return;

            using var doc = JsonDocument.Parse(File.ReadAllText(FilePath));
            var root = doc.RootElement;

            if (root.TryGetProperty("ThemeIndex", out var t)) ThemeIndex = t.GetInt32();
            if (root.TryGetProperty("BackdropMode", out var b)) BackdropMode = b.GetInt32();
            if (root.TryGetProperty("WindowWidth", out var w)) WindowWidth = w.GetInt32();
            if (root.TryGetProperty("WindowHeight", out var h)) WindowHeight = h.GetInt32();
            if (root.TryGetProperty("LastPage", out var lp)) LastPage = lp.GetString() ?? "home";
            if (root.TryGetProperty("DefaultsApplied", out var da)) DefaultsApplied = da.GetInt32();

            if (root.TryGetProperty("RecentItems", out var ri) && ri.ValueKind == JsonValueKind.Array)
            {
                RecentItems = ri.EnumerateArray()
                    .Where(x => x.ValueKind == JsonValueKind.String)
                    .Select(x => x.GetString()!)
                    .Where(x => !string.IsNullOrWhiteSpace(x))
                    .ToList();
            }

            bool migrated = false;

            if (DefaultsApplied < DefaultsVersion)
            {
                // Apply the new default here, once.
                DefaultsApplied = DefaultsVersion;
                migrated = true;
            }

            // Defensive clamps: never trust the file.
            if (BackdropMode is < 0 or > 2) { BackdropMode = 2; migrated = true; }
            if (WindowWidth < 640) { WindowWidth = 1200; migrated = true; }
            if (WindowHeight < 480) { WindowHeight = 800; migrated = true; }

            // MUST be the last statement: saving mid-load would serialise the
            // properties that have not been read yet at their default values.
            if (migrated) Save();
        }
        catch { /* corrupt file: keep defaults, Save() will overwrite it */ }
    }

    public static void Save()
    {
        try
        {
            Directory.CreateDirectory(Dir);
            File.WriteAllText(FilePath, JsonSerializer.Serialize(new
            {
                ThemeIndex,
                BackdropMode,
                WindowWidth,
                WindowHeight,
                LastPage,
                RecentItems,
                DefaultsApplied
            }));
        }
        catch { /* read-only or locked: ignore */ }
    }
}
