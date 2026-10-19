# 01 — Startup and Single Instance

The WinUI 3 startup chain is short and unforgiving: if anything throws before the first
frame, the process disappears with no dialog, no event log entry, and no dump. Build the
forensics in first, then the app.

## Startup chain

```
Main() [auto-generated] → App ctor → App.InitializeComponent() → OnLaunched → new MainWindow() → Activate()
```

`App.xaml` generation writes `Main()` automatically when `<EnableMsixTooling>` and
`<UseWinUI>` are set and no `DISABLE_XAML_GENERATED_MAIN` symbol is defined. Do not hand-write
`Main()` unless you need to host the message loop yourself.

## Instrument every step

Log a line at each hop. The cost is a file append per launch; the payoff is knowing
whether a silent exit happened in XAML parsing or in window construction.

```csharp
public App()
{
    LogStartup("App ctor begin");
    try { InitializeComponent(); }
    catch (Exception ex) { LogCrash("App.InitializeComponent", ex); throw; }
    LogStartup("App ctor done");

    UnhandledException += (s, e) => { LogCrash("UnhandledException", e.Exception); e.Handled = true; };
}

protected override void OnLaunched(LaunchActivatedEventArgs args)
{
    LogStartup("OnLaunched begin");
    // ... mutex, window ...
    LogStartup("MainWindow activated");
}
```

`UnhandledException` must be subscribed **in the constructor**. Subscribing in
`OnLaunched` misses every failure raised while parsing `App.xaml` — the most common
startup crash class.

## Crash log format

Write the full inner-exception chain, not just `ex.Message`. `XamlParseException`
wraps the real cause one or two levels down, and the HResult distinguishes
"resource not found" from "type not found".

```csharp
var sb = new StringBuilder();
Exception? cur = ex;
int depth = 0;
while (cur != null)
{
    sb.AppendLine($"[{depth}] {cur.GetType().FullName}: {cur.Message}");
    sb.AppendLine("HResult: 0x" + cur.HResult.ToString("X8"));
    sb.AppendLine(cur.StackTrace);
    cur = cur.InnerException;
    depth++;
}
```

Location: `%LocalAppData%\<AppName>\crash.log`. Never write logs next to the executable —
the install directory is usually read-only under `Program Files`.

## Single instance

Three behaviours are needed, and only one of them is the mutex:

| Situation | Expected behaviour |
|---|---|
| Second launch, no arguments | Exit silently; running instance comes to front |
| Second launch carrying an activation argument (toast click) | Forward the argument over a named pipe, then exit |
| First launch was itself argument-activated | Process the stored argument after the window exists |

```csharp
_mutex = new Mutex(true, @"Local\MyApp_SingleInstance", out bool createdNew);
if (!createdNew)
{
    if (ShouldForwardActivation()) ForwardActivationAndExit();
    else Environment.Exit(0);
    return;
}
```

Use the `Local\` prefix. A `Global\` mutex leaks across sessions, so a second user on
the same machine is locked out of their own app.

### Forwarding over a named pipe

```csharp
private const string PipeName = "MyApp_ToastActivation";

// Server (first instance), started before the window exists
var server = new NamedPipeServerStream(PipeName, PipeDirection.In, 1,
                                       PipeTransmissionMode.Byte, PipeOptions.Asynchronous);
_ = Task.Run(async () =>
{
    while (true)
    {
        try
        {
            await server.WaitForConnectionAsync();
            using var reader = new StreamReader(server, leaveOpen: true);
            string? arg = await reader.ReadToEndAsync();
            if (!string.IsNullOrEmpty(arg))
                _pending ??= arg;               // handled after the window exists
            server.Disconnect();
        }
        catch { break; }
    }
});
```

Retry the client connect a few times: the first instance may still be booting when the
second one tries to write.

```csharp
for (int i = 0; i < 5; i++)
{
    try
    {
        using var client = new NamedPipeClientStream(".", PipeName, PipeDirection.Out);
        client.Connect(1000);
        using var writer = new StreamWriter(client);
        writer.Write(argument);
        writer.Flush();
        break;
    }
    catch { Thread.Sleep(200); }
}
```

## Verification trap

The mutex makes manual testing lie. After rebuilding, an old instance still running in
the tray will absorb the new launch and exit it silently — you will spend an hour
debugging a build you never actually ran.

**Before testing a new build: kill every instance, including the tray icon.**

## Checklist

- [ ] `UnhandledException` wired in the constructor.
- [ ] `startup.log` reaches "MainWindow activated" on a healthy run.
- [ ] `crash.log` unchanged after a healthy run.
- [ ] Second launch exits within ~1 second, first window foregrounds.
- [ ] No file written next to the executable.
