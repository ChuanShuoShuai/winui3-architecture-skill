# 05 — 诊断与崩溃取证

WinUI 3 应用在首帧之前死掉，几乎不留痕迹：没有对话框，往往没有 WER 记录，
也不一定有托管转储。下面这些东西的存在意义就是让这种失败变得可观测。

## 取证顺序

按这个顺序查，每一步都比下一步快。

| 顺序 | 产物 | 能告诉你什么 |
|---|---|---|
| 1 | `%LocalAppData%\<应用>\crash.log` | 托管异常 + 完整 InnerException 链 |
| 2 | `%LocalAppData%\<应用>\startup.log` | 最后一个完成的启动步骤，它之后的一切都可疑 |
| 3 | `%LocalAppData%\CrashDumps\*.dmp` | 到底是真崩溃，还是被环境回收 |
| 4 | 事件查看器 → Windows 日志 → 应用程序 | 没进托管代码的原生错误 |
| 5 | `dotnet-dump analyze <dmp> -c "pe"` | 最后手段；XAML 解析失败经常什么都拿不到 |

**先看 `crash.log`。** 它在出问题的那一帧同步写入，会直接点出是哪个属性、哪个资源。

## 读 XamlParseException

```
[0] Microsoft.UI.Xaml.Markup.XamlParseException: Cannot find a Resource with the Name/Key FooBrush
HResult: 0x802B000A
   at Microsoft.UI.Xaml.Application.LoadComponent(...)
[1] System.Runtime.InteropServices.COMException: ...
```

看**最内层**的 message 和 HResult：

| HResult | 含义 |
|---|---|
| `0x802B000A` | 资源键不存在——引用了不存在的 `ThemeResource` / `StaticResource` |
| `0x80004005` | 通用 COM 失败，常见于类型加载失败 |
| `0x80070057` | 参数非法，常见于把同一个 `Geometry` 赋给第二个 `Path` |

页面级 `XamlParseException` 通常意味着这个键在 `App.xaml` 里有、但当前主题的
主题字典里没有。浅色、深色各测一遍。

## startup.log

一行一步，带时间戳。看末尾：

```
2026-10-19 12:17:20.798  App 构造开始
2026-10-19 12:17:20.811  App 构造完成
2026-10-19 12:17:20.902  OnLaunched 开始
2026-10-19 12:17:21.140  MainWindow 构造开始
2026-10-19 12:17:21.367  MainWindow 构造完成
2026-10-19 12:17:21.402  MainWindow 激活完成
```

最后一行停在 `OnLaunched 开始` → 问题在 `new MainWindow()` 里，多半是 XAML 解析。
停在 `MainWindow 构造开始` → 问题在字段初始化器或构造函数里调用的某个服务。

窗口和页面的构造函数里**不要做 I/O、不要发网络请求、不要 `await`**。推迟到 `Loaded`
或 `OnNavigatedTo`。

## 区分「真崩溃」和「被回收」

当应用是自动化命令的子进程时，命令会话结束会把子进程一起回收。进程消失了，
但应用从未崩溃。判据：

- `%LocalAppData%\CrashDumps\` 里该进程有 `.dmp` → 真崩溃。
- 没有转储，**并且**同环境下另一个以同样方式启动的 GUI 进程也一起消失 → 环境回收，不是 bug。
- 事件查看器 → 应用程序 → Windows Error Reporting 有记录 → 确认真实故障。

别花一下午调一个根本没发生的崩溃。

## 重新编译后的静默退出

症状：编译完成，运行，什么都没有，`crash.log` 也没有新增。

原因：单实例互斥体。一个旧实例（常常缩在托盘里）吞掉了这次启动并 `Environment.Exit(0)`。

排查：

```bash
tasklist //FI "IMAGENAME eq MyApp.exe"          # 有没有在跑？
tasklist //V //FI "IMAGENAME eq MyApp.exe"      # 有没有窗口 / 托盘？
```

然后杀掉所有实例再启动。「进程 1 秒内退出，但 `startup.log` 是正常的」就是这个陷阱的签名。

## 排查界面卡死

窗口卡死时什么都不会报。加一个看门狗，记录 UI 线程停止泵消息的时刻：

```csharp
// 在 UI 线程上，每 500 ms 抛一次
DispatcherQueue.TryEnqueue(() =>
{
    long now = Environment.TickCount64;
    if (now - _lastWatchdogTick > 3000)
        LogStartup($"UI 线程停摆 {now - _lastWatchdogTick} ms");
    _lastWatchdogTick = now;
});
```

配合卡死期间对进程做一次采样转储（`dotnet-dump collect -p <pid>`），
看 UI 线程的托管调用栈。

## 日志文件卫生

- 限制大小：`crash.log` 超过约 1 MB 就轮转成 `crash.old.log` 再追加。
- 绝不记录密钥。配置里可能有 API Key，不要整体 dump `AppSettings`。
- 用 `File.AppendAllText` 并吞掉 IO 异常——日志不能变成新的崩溃源。
- 日志不要写进安装目录。`Program Files` 对普通用户是只读的。

## 检查清单

- [ ] 正常启动后 `crash.log` 没有新增内容。
- [ ] 在页面构造函数里故意抛异常 → 日志点出页面名和内部异常。
- [ ] 故意引用不存在的 `ThemeResource` → 日志里出现 `0x802B000A`。
- [ ] 留一个旧实例再启动 → 一分钟内能识别出是互斥体陷阱。
