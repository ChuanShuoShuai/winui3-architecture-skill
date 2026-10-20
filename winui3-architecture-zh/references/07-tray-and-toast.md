# 07 — 托盘与 Toast 通知

这两个功能都会打破正常的窗口生命周期，也都会和单实例互斥体打交道。
**先让启动和设置稳定下来，再动它们。**

## 托盘图标

用 `H.NotifyIcon.WinUI`。这是目前仍在维护、且与 WinUI 3 配合良好的包，
可以直接在 XAML 里声明 `TaskbarIcon`。

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
                    <MenuFlyoutItem Text="打开" Click="TrayOpen_Click" />
                    <MenuFlyoutItem Text="退出" Click="TrayExit_Click" />
                </MenuFlyout>
            </tb:TaskbarIcon.ContextFlyout>
        </tb:TaskbarIcon>
    </Grid>
</Window>
```

### 隐藏到托盘

`Microsoft.UI.Xaml.Window` **没有** `Visibility` 属性。隐藏意味着把窗口从任务栏移除并
移到屏幕外，状态要自己维护。

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

所有「窗口是否可见」的分支都要读 `_hiddenToTray`，不要读 `AppWindow.IsVisible`
（在某些版本上窗口已移到屏幕外它仍然是 true）。

### 关闭即最小化到托盘

拦 `AppWindow.Closing`，除非是主动退出否则取消：

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

托盘菜单的「退出」要先置 `_reallyExiting = true` 再 `Close()`。
少了这个标志，退出菜单只会把窗口再藏一次，进程永远不退。

## Toast 通知

包：`Microsoft.Toolkit.Uwp.Notifications`（7.x）。在非打包应用里，它会在首次使用时
自动注册 COM 激活器。

> **传递依赖漏洞**：7.1.3 会带入 `System.Drawing.Common` 4.7.0，该版本有已知严重漏洞
> （GHSA-rxg9-xrhp-64gj），NuGet 报 `NU1904`。在自己的 csproj 里显式引用最新版
> `System.Drawing.Common` 把传递版本顶上去——不要因此降级通知包。

### 弹出

```csharp
new ToastContentBuilder()
    .AddText("发现高风险进程")
    .AddText($"{name}（PID {pid}）")
    .AddArgument("action", "open")
    .AddArgument("pid", pid.ToString())
    .AddButton(new ToastButton()
        .SetContent("结束进程")
        .AddArgument("action", "kill")
        .AddArgument("pid", pid.ToString()))
    .Show();
```

参数要短、要平铺。它会被序列化进激活字符串，并跨进程边界传递。

### 激活

```csharp
ToastNotificationManagerCompat.OnActivated += e =>
{
    var args = e.Argument;                     // "action=kill&pid=1234"
    if (MainWindow != null)
        MainWindow.DispatcherQueue.TryEnqueue(() => HandleActivation(args));
    else
        _pendingActivation = args;             // 窗口还没建好
};
```

回调在 **COM 线程**上到达，不是 UI 线程。必须经 `DispatcherQueue` 中转。
在回调线程里直接碰 XAML 会抛一个看起来跟通知毫无关系的 COM 异常。

### 冷启动

应用没在运行时点通知，会启动一个新进程并带上激活参数。这个进程会撞上单实例互斥体：
已经有实例在跑就通过命名管道转发（见 `01-startup-and-single-instance.md`），
自己就是第一个实例则暂存参数，等窗口建好后再处理。

时序：

```
点通知（应用未运行）
  → 新进程启动
  → 互斥体：createdNew == true（它是唯一实例）
  → ToastNotificationManagerCompat.WasCurrentProcessToastActivated() == true
  → OnActivated 触发，_pendingActivation 为空 → 暂存
  → 窗口创建完成
  → 消费 _pendingActivation
```

**四种组合都要测**：应用在跑 / 没跑 × 点通知本体 / 点通知按钮。

## 注册前置条件

非打包 Toast 需要注册 AppUserModelID。Toolkit 会处理注册，但 AUMID 必须跨版本稳定——
用固定 GUID 派生，不要用安装路径。

```csharp
// 启动时执行一次
ToastNotificationManagerCompat.CreateToastNotifier();
```

通知完全不出现时按顺序查：

1. Windows 设置 → 通知 → 应用是否在列表里且已启用。
2. 专注助手 / 免打扰会静默吞掉一切。
3. AUMID 在不同构建之间变了 → 旧注册成了孤儿。清掉
   `HKCU\Software\Classes\AppUserModelId\<你的 AUMID>` 后重新注册。

## 检查清单

- [ ] 托盘图标在 100% 和 150% DPI 下都显示正确。
- [ ] 关闭按钮隐藏到托盘；托盘「退出」真的结束进程。
- [ ] 带按钮的通知，应用在跑 → 按钮动作在 UI 线程执行。
- [ ] 带按钮的通知，应用关闭 → 应用启动、窗口打开、动作只执行一次。
- [ ] 第二个实例转发激活时不会重复执行。
