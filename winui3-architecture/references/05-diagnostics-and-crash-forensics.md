# 05 — Diagnostics and Crash Forensics

A WinUI 3 app that dies before its first frame leaves almost no trace: no dialog, usually
no Windows Error Reporting entry, and no managed dump. Everything below exists to make
that failure observable.

## Evidence order

Check in this order. Each step is faster than the next.

| Order | Artifact | What it tells you |
|---|---|---|
| 1 | `%LocalAppData%\<App>\crash.log` | The managed exception, with the full inner chain |
| 2 | `%LocalAppData%\<App>\startup.log` | The last completed startup step — everything after it is suspect |
| 3 | `%LocalAppData%\CrashDumps\*.dmp` | Whether the process actually crashed or was just recycled |
| 4 | Event Viewer → Windows Logs → Application | Native faults that never reached managed code |
| 5 | `dotnet-dump analyze <dmp> -c "pe"` | Last resort; frequently returns nothing for XAML parse failures |

Start with `crash.log`. It is written synchronously in the failing frame and names the
exact property or resource that failed.

## Parsing a XamlParseException

```
[0] Microsoft.UI.Xaml.Markup.XamlParseException: Cannot find a Resource with the Name/Key FooBrush
HResult: 0x802B000A
   at Microsoft.UI.Xaml.Application.LoadComponent(...)
[1] System.Runtime.InteropServices.COMException: ...
```

Read the **innermost** message and the HResult:

| HResult | Meaning |
|---|---|
| `0x802B000A` | Resource key not found — a `ThemeResource`/`StaticResource` that does not exist |
| `0x80004005` | Generic COM failure, often a type that failed to load |
| `0x80070057` | Invalid argument — commonly a shared `Geometry` assigned to a second `Path` |

A page-level `XamlParseException` usually means the key exists in `App.xaml` but not in the
theme dictionary for the theme currently applied. Test in both Light and Dark.

## startup.log

One line per step, timestamped. Read the tail:

```
2026-10-19 12:17:20.798  App ctor begin
2026-10-19 12:17:20.811  App ctor done
2026-10-19 12:17:20.902  OnLaunched begin
2026-10-19 12:17:21.140  MainWindow ctor begin
2026-10-19 12:17:21.367  MainWindow ctor done
2026-10-19 12:17:21.402  MainWindow activated
```

If the last line is `OnLaunched begin`, the fault is inside `new MainWindow()` — most
likely XAML parsing. If it is `MainWindow ctor begin`, the fault is in your own field
initialisers or a service constructor invoked during construction.

Never do I/O, network calls, or `await` in a window or page constructor. Defer to
`Loaded` or `OnNavigatedTo`.

## Distinguishing "crashed" from "was killed"

When an app is launched as a child process of an automation shell, the shell may terminate
it when its session ends. The process vanishes; the app never crashed. Decide with:

- `%LocalAppData%\CrashDumps\` — a `.dmp` for the process means a real crash.
- No dump **and** another GUI process launched the same way also vanished → environment
  reclamation, not a bug.
- Event Viewer → Application → Windows Error Reporting entry confirms a real fault.

Do not spend an afternoon debugging a crash that never happened.

## Silent exit after a rebuild

Symptom: you build, run, nothing appears, no crash log.

Cause: the single-instance mutex. An older instance (often minimised to tray) absorbed the
launch and called `Environment.Exit(0)`.

Check:

```bash
tasklist //FI "IMAGENAME eq MyApp.exe"          # is anything running?
tasklist //V //FI "IMAGENAME eq MyApp.exe"      # is there a window / tray?
```

Then kill every instance and launch again. Treat "process exits in under a second with a
healthy startup.log" as the signature of this trap.

## Diagnosing UI freezes

A frozen window reports nothing. Add a watchdog that logs when the UI thread stops
pumping:

```csharp
// On the UI thread, every 500 ms
DispatcherQueue.TryEnqueue(() =>
{
    long now = Environment.TickCount64;
    if (now - _lastWatchdogTick > 3000) LogStartup($"UI thread stalled {now - _lastWatchdogTick} ms");
    _lastWatchdogTick = now;
});
```

Pair it with a sampling dump of the process while frozen (`dotnet-dump collect -p <pid>`)
and inspect the UI thread's managed stack.

## Log file hygiene

- Cap the file: if `crash.log` exceeds ~1 MB, rotate it to `crash.old.log` before append.
- Never log secrets. Settings may contain API keys; never dump `AppSettings` wholesale.
- Write with `File.AppendAllText` and swallow IO errors — logging must not become a new
  crash source.
- Keep logs out of the install directory. `Program Files` is read-only for standard users.

## Checklist

- [ ] A healthy run appends an unchanged `crash.log`.
- [ ] Deliberately throw in a page constructor → the log names that page and the inner exception.
- [ ] Deliberately reference a nonexistent `ThemeResource` → log shows `0x802B000A`.
- [ ] Launch while a stale instance runs → you can identify the mutex trap in under a minute.
