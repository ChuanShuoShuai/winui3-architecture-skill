# 06 — Packaging and Deployment

Unpackaged WinUI 3 (the realistic choice outside the Store) has two decisions that people
mix up:

- **.NET runtime** — framework-dependent or self-contained.
- **Windows App SDK runtime** — always a separate switch (`WindowsAppSDKSelfContained`).

You can have either independently. The common ship shape is: **.NET framework-dependent,
WindowsAppSDK self-contained**, plus a prerequisite check in the installer.

## Project properties that matter

```xml
<WindowsPackageType>None</WindowsPackageType>
<TargetFramework>net10.0-windows10.0.19041.0</TargetFramework>
<TargetPlatformMinVersion>10.0.17763.0</TargetPlatformMinVersion>
<Platforms>x64</Platforms>
<RuntimeIdentifier>win-x64</RuntimeIdentifier>
<UseWinUI>true</UseWinUI>
<WindowsAppSDKSelfContained>true</WindowsAppSDKSelfContained>
<SatelliteResourceLanguages>zh-Hans;zh-Hant;en-US</SatelliteResourceLanguages>
```

`SatelliteResourceLanguages` is the single highest-leverage line: WinUI ships dozens of
localisation folders, and trimming to the ones you actually support cuts tens of megabytes.

## Two-stage release

`dotnet build` alone does not produce a runnable self-contained folder. Publish explicitly:

```bash
dotnet build MyApp.sln -c Release -p:Platform=x64
dotnet publish MyApp/MyApp.csproj -c Release -p:Platform=x64
```

Then package the publish output with Inno Setup.

## Payload bloat: the two causes

### 1. Stale subdirectories in the output folder

Installers using `recursesubdirs` swallow everything under the output path. A forgotten
`publish/` or `runtimes/` folder from an earlier experiment silently doubles the installer.

Before packaging, list the output directory and expect a **known file count and a known
set of language subdirectories**. Anything else is residue — delete it and rebuild.

```bash
ls "MyApp/bin/x64/Release/net10.0-windows10.0.19041.0/win-x64" | wc -l
ls "MyApp/bin/x64/Release/net10.0-windows10.0.19041.0/win-x64" | grep -v '\.'
```

### 2. The WebView2 user-data symlink

If the app uses `WebView2`, at runtime it creates a directory junction next to the exe:

```
MyApp.exe.WebView2 -> %LocalAppData%\MyApp\WebView2Data
```

`recursesubdirs` follows it and packs the entire runtime cache — hundreds of files, tens
of megabytes, including user session data. Exclude it explicitly in the installer script:

```ini
[Files]
Source: "path\to\publish\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs; \
  Excludes: "*.WebView2\*,*.pdb,*.xml"
```

Verify by counting `Compressing:` lines in the Inno Setup build log. That number is the
truth; a directory listing is not (symlinked trees are undercounted by plain `find`).

## Inno Setup minimum

```ini
#define MyAppName "MyApp"
#define MyAppVersion "1.0.0"
#define MyAppExeName "MyApp.exe"

[Setup]
AppId={{GUID-HERE}}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
DefaultDirName={autopf}\{#MyAppName}
OutputBaseFilename={#MyAppName}_setup_{#MyAppVersion}
Compression=lzma2/max
SolidCompression=yes
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
PrivilegesRequired=admin
WizardStyle=modern
SetupIconFile=app.ico
UninstallDisplayIcon={app}\{#MyAppExeName}

[Files]
Source: "publish\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs; \
  Excludes: "*.WebView2\*"

[Icons]
Name: "{autoprograms}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"
Name: "{autodesktop}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; Tasks: desktopicon

[Tasks]
Name: "desktopicon"; Description: "Create a desktop shortcut"; Flags: unchecked

[Run]
Filename: "{app}\{#MyAppExeName}"; Description: "Launch {#MyAppName}"; Flags: nowait postinstall skipifsilent
```

Build from the command line:

```bash
"F:/Inno Setup 7/ISCC.exe" MyApp.iss
```

## Version numbers live in four places

Changing `<Version>` in the csproj is not enough. A release touches:

1. `.csproj` → `<Version>`
2. About / settings page text
3. Any `LocalVersion` constant used for update checks
4. `.iss` → `#define MyAppVersion` (drives both the filename and the version resource)

Miss number 4 and the installer filename disagrees with the binary's file version, which
breaks update detection.

## Ship-time prerequisite check

With `WindowsAppSDKSelfContained=true` the WinAppSDK runtime travels with the app. The
**.NET desktop runtime does not**, unless you also set `<SelfContained>true</SelfContained>`
(which pushes the payload past 150 MB).

Pick one:

- **Lean**: framework-dependent .NET + a `[Code]` section in the installer that checks
  `HKLM\SOFTWARE\Microsoft\NET Framework Setup` or runs `dotnet --list-runtimes`, and
  links to the download page when missing.
- **Bulky**: `<SelfContained>true</SelfContained>`, ~150 MB installer, no prerequisite.

Document the choice in the README. "It does not start on my machine" is almost always a
missing .NET desktop runtime.

## Release checklist

- [ ] `dotnet build -c Release -p:Platform=x64` → 0 errors.
- [ ] `dotnet publish` output contains no `coreclr.dll` (unless self-contained).
- [ ] Output folder file count matches the previous release (± your changes).
- [ ] No `*.WebView2` junction inside the publish folder.
- [ ] Installer size within ~5 MB of the previous release; investigate any larger jump.
- [ ] Clean VM without .NET → installer runs → app starts (or the check explains why not).
- [ ] Version string visible in the About page matches the installer filename.
- [ ] Uninstall removes `%LocalAppData%\<App>` only if the user opted in — otherwise leave settings.
