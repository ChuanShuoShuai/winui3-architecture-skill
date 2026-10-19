# 09 — 窗口定位与全屏遮罩

一个"点哪里选哪个窗口"的取景器：冻结截图 → 全屏遮罩 → 按光标位置命中真实顶层窗口 →
高亮结果。窗口管理器、检查工具、录屏、无障碍工具都用得上。

## 两个最常见的错

1. **透明遮罩 + `WindowFromPoint`。** 自己的遮罩在最上层，`WindowFromPoint` 永远返回遮罩自己。
   必须排除本进程，或者干脆不要透过实时屏幕去命中。
2. **`GetForegroundWindow`。** 它返回的是当前激活窗口，不是光标下面那个。

## 推荐架构

```
1. 把整个虚拟屏幕截成一张位图。
2. 每台显示器建一个无边框置顶窗口，各显示自己那一块。
3. 跟踪遮罩上的鼠标移动，把屏幕坐标换算成虚拟屏坐标。
4. 用 GetTopWindow 逐层下探命中，跳过本进程的窗口。
5. 高亮用"暗层挖洞"，目标区域保持原亮度。
6. 点击时返回解析出的 HWND。
```

### 先冻结截图

冻结截图能避免遮罩被拍进自己的截图、避免高亮时闪烁，也让命中判定变成确定性的。

```csharp
int x = GetSystemMetrics(SM_XVIRTUALSCREEN);
int y = GetSystemMetrics(SM_YVIRTUALSCREEN);
int w = GetSystemMetrics(SM_CXVIRTUALSCREEN);
int h = GetSystemMetrics(SM_CYVIRTUALSCREEN);

using var bmp = new Bitmap(w, h, PixelFormat.Format32bppArgb);
using (var g = Graphics.FromImage(bmp))
    g.CopyFromScreen(x, y, 0, 0, new Size(w, h), CopyPixelOperation.SourceCopy);
```

### 不用 WindowFromPoint 的命中方式

遍历顶层窗口链，并下探到子窗口：

```csharp
IntPtr HitTest(Point screenPt)
{
    IntPtr hwnd = GetTopWindow(IntPtr.Zero);
    while (hwnd != IntPtr.Zero)
    {
        if (IsWindowVisible(hwnd) && !IsOwnProcess(hwnd))
        {
            GetWindowRect(hwnd, out RECT r);
            if (screenPt.X >= r.Left && screenPt.X < r.Right &&
                screenPt.Y >= r.Top && screenPt.Y < r.Bottom)
            {
                // 下探：可见表面可能是子窗口（例如壁纸工具把绘制窗
                // 塞在桌面窗口之下）
                IntPtr child = RealChildWindowFromPoint(hwnd, screenPt);
                return child != IntPtr.Zero ? child : hwnd;
            }
        }
        hwnd = GetWindow(hwnd, GW_HWNDNEXT);
    }
    return IntPtr.Zero;
}
```

**一定要下探子窗口。** 壁纸引擎和外壳替换工具往 `Progman` / `WorkerW` 的子窗口里绘制。
只报父窗口的话，视觉上是第三方应用，结果永远显示 `explorer.exe`。

### 高亮框要准

`GetWindowRect` 包含不可见的阴影边距（现代 Windows 约 7 px），照它画框会画到
可见边缘外面。用 DWM 的扩展边框：

```csharp
const int DWMWA_EXTENDED_FRAME_BOUNDS = 9;
DwmGetWindowAttribute(hwnd, DWMWA_EXTENDED_FRAME_BOUNDS, out RECT r, Marshal.SizeOf<RECT>());
```

DWM 调用失败时（窗口被 cloaked、没有边框）回退到 `GetWindowRect`。

### 压暗用"挖洞"

不要手算目标周围的四条矩形，直接从目标矩形推：

```
上   = (0, 0, W, r.Top)
下   = (0, r.Bottom, W, H - r.Bottom)
左   = (0, r.Top, r.Left, r.Height)
右   = (r.Right, r.Top, W - r.Right, r.Height)
```

这四块填 40% 黑。目标区域保持原亮度，看起来是"聚光灯"，而不是"选择框"。

## 多显示器与 DPI

`CopyFromScreen` 和屏幕坐标都是物理像素。用 DIP 定位的 WPF/WinForms 遮罩窗口，
在缩放比例不同的显示器上会错位。

- 清单文件里把遮罩窗口声明为 **per-monitor DPI aware v2**。
- 用物理像素（`SetWindowPos`）而不是 XAML 坐标定位每个遮罩窗口。
- 只有需要给 XAML 元素用逻辑坐标时，才调 `PhysicalToLogicalPointForPerMonitorDPI`。

已知限制：多显示器且各屏 DPI 不同时，副屏会出现几像素偏差。同 DPI 下是精确的。

## 返回结果

取景器应该返回的不止一个 `HWND`：

```csharp
public sealed record PickedWindow(
    IntPtr Handle,
    int ProcessId,
    string ProcessName,
    string Title,
    RECT Bounds);
```

调用方几乎总需要进程信息，在取景器内部解析能把 `OpenProcess` /
`QueryFullProcessImageName` 集中在一处。

## 安全规则

- 跳过本进程拥有的窗口（`GetWindowThreadProcessId` 比对 PID）。
- 跳过 cloaked 窗口（`DwmGetWindowAttribute(hwnd, DWMWA_CLOAKED, ...)` 非零）。
- 处理 `WM_MOUSEMOVE` 的去抖：光标每移动一像素就命中一次没问题（几次 `GetWindowRect` 而已），
  但**每帧重画暗层不行**。只在解析出的 `HWND` 变化时才重画。
- 永远留退出路径：`Esc` 取消；关闭所有遮罩窗口必须恢复到原始状态。

## 检查清单

- [ ] 遮罩覆盖所有显示器，包括坐标为负的那块。
- [ ] 悬停在桌面壁纸上返回的是壁纸引擎，不是 `explorer.exe`。
- [ ] 高亮框贴在可见窗口边缘上，而不是外面 7 px。
- [ ] `Esc` 能干净退出，没有残留的置顶窗口。
- [ ] 窗口最大化、最小化（应跳过）、部分移出屏幕时都正常。
