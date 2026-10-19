# 01 — 启动与单实例

WinUI 3 的启动链路很短，也很不宽容：首帧之前任何一处抛异常，进程会直接消失——
没有对话框、事件日志里通常也没有记录、连托管转储都不一定有。所以先把取证建好，再写功能。

## 启动链路

```
Main() [自动生成] → App 构造 → App.InitializeComponent() → OnLaunched → new MainWindow() → Activate()
```

当 `<UseWinUI>` 与 `<EnableMsixTooling>` 都打开、且没有定义 `DISABLE_XAML_GENERATED_MAIN`
符号时，编译器会自动生成 `Main()`。除非你要自己接管消息循环，否则不要手写。

## 给每一步埋日志

每个环节写一行。成本是每次启动一次文件追加，收益是能立刻判断静默退出发生在
XAML 解析还是窗口构造。

```csharp
public App()
{
    LogStartup("App 构造开始");
    try { InitializeComponent(); }
    catch (Exception ex) { LogCrash("App.InitializeComponent", ex); throw; }
    LogStartup("App 构造完成");

    UnhandledException += (s, e) => { LogCrash("UnhandledException", e.Exception); e.Handled = true; };
}

protected override void OnLaunched(LaunchActivatedEventArgs args)
{
    LogStartup("OnLaunched 开始");
    // ... 互斥体、窗口 ...
    LogStartup("MainWindow 激活完成");
}
```

`UnhandledException` 必须在**构造函数里**挂。挂在 `OnLaunched` 会漏掉解析
`App.xaml` 期间抛的异常——而这恰恰是最常见的一类启动崩溃。

## 崩溃日志格式

要写完整的 InnerException 链，不能只写 `ex.Message`。`XamlParseException` 会把真实原因
裹在一到两层里面，而 HResult 能区分「资源没找到」和「类型没找到」。

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

落盘位置：`%LocalAppData%\<应用名>\crash.log`。**不要写在 exe 旁边**——装到
`Program Files` 后那个目录对普通用户是只读的。

## 单实例

需要三种行为，其中只有一种是互斥体本身：

| 场景 | 期望行为 |
|---|---|
| 第二次启动，没带参数 | 静默退出；已有实例到前台 |
| 第二次启动，带激活参数（点了通知） | 通过命名管道把参数转给已有实例，再退出 |
| 第一次启动本身就是被参数激活的 | 窗口创建后处理暂存的参数 |

```csharp
_mutex = new Mutex(true, @"Local\MyApp_SingleInstance", out bool createdNew);
if (!createdNew)
{
    if (ShouldForwardActivation()) ForwardActivationAndExit();
    else Environment.Exit(0);
    return;
}
```

用 `Local\` 前缀。`Global\` 会跨会话生效，同一台机器上的第二个 Windows 用户
会被锁在自己的应用之外。

### 命名管道转发

```csharp
private const string PipeName = "MyApp_ToastActivation";

// 服务端（第一个实例），在窗口创建之前启动
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
                _pending ??= arg;              // 窗口创建后再处理
            server.Disconnect();
        }
        catch { break; }
    }
});
```

客户端要重试几次：第二个实例来写的时候，第一个实例可能还在启动中。

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

## 验证陷阱

互斥体会让手工测试说谎。重新编译后，如果一个旧实例还活在托盘里，它会把你这次启动
吞掉并静默退出——于是你会对着一个「从没真正运行过的新构建」调半天。

**验证新构建之前，先彻底退出所有实例，包括托盘图标。**

## 检查清单

- [ ] `UnhandledException` 在构造函数里挂上。
- [ ] 正常启动时 `startup.log` 走到「MainWindow 激活完成」。
- [ ] 正常启动后 `crash.log` 没有新内容。
- [ ] 第二次启动在 1 秒内退出，第一个窗口到前台。
- [ ] exe 所在目录没有任何日志文件。
