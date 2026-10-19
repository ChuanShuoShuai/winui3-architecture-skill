# 09 — Window Targeting and Screen Overlay

A picker that lets the user click any window on screen: freeze a screenshot, show a
full-screen overlay, hit-test the cursor against real top-level windows, and highlight the
result. Useful for window managers, inspectors, recorders, and accessibility tools.

## The two mistakes

1. **Transparent overlay + `WindowFromPoint`.** Your own overlay is on top, so
   `WindowFromPoint` always returns the overlay. You must either exclude your process or
   avoid hit-testing through the live screen.
2. **`GetForegroundWindow`.** Returns one window, not the one under the cursor.

## Recommended architecture

```
1. Capture the entire virtual screen into a bitmap.
2. Create one borderless, topmost window per monitor, each showing its slice.
3. Track mouse movement over the overlay; convert screen coords to virtual-screen coords.
4. Hit-test with GetTopWindow + descend, skipping your own process.
5. Draw the highlight by punching a hole in a dim layer (keep the region at full brightness).
6. On click, return the resolved HWND.
```

### Freeze first

A frozen screenshot avoids the overlay being captured in its own screenshot, avoids
flicker while highlighting, and makes hit-testing deterministic.

```csharp
// Capture the whole virtual screen (all monitors)
int x = GetSystemMetrics(SM_XVIRTUALSCREEN);
int y = GetSystemMetrics(SM_YVIRTUALSCREEN);
int w = GetSystemMetrics(SM_CXVIRTUALSCREEN);
int h = GetSystemMetrics(SM_CYVIRTUALSCREEN);

using var bmp = new Bitmap(w, h, PixelFormat.Format32bppArgb);
using (var g = Graphics.FromImage(bmp))
    g.CopyFromScreen(x, y, 0, 0, new Size(w, h), CopyPixelOperation.SourceCopy);
```

### Hit-testing without WindowFromPoint

Walk the top-level window chain and descend into child windows:

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
                // descend: the visible surface may be a child (e.g. a worker window
                // parked under the desktop window by a wallpaper tool)
                IntPtr child = RealChildWindowFromPoint(hwnd, screenPt);
                return child != IntPtr.Zero ? child : hwnd;
            }
        }
        hwnd = GetWindow(hwnd, GW_HWNDNEXT);
    }
    return IntPtr.Zero;
}
```

**Descend into children.** Wallpaper engines and shell replacements draw into child windows
of `Progman`/`WorkerW`. Reporting the parent yields `explorer.exe` for what visually looks
like a third-party app.

### Accurate highlight rectangle

`GetWindowRect` includes the invisible drop-shadow margin (about 7 px on modern Windows),
so the highlight sits outside the visible window edge. Use DWM extended frame bounds:

```csharp
const int DWMWA_EXTENDED_FRAME_BOUNDS = 9;
DwmGetWindowAttribute(hwnd, DWMWA_EXTENDED_FRAME_BOUNDS, out RECT r, Marshal.SizeOf<RECT>());
```

Fall back to `GetWindowRect` when the DWM call fails (window is cloaked or owns no frame).

### Dim with a punched hole

Do not draw four rectangles around the target by hand — compute them from the target rect:

```
top    = (0, 0, W, r.Top)
bottom = (0, r.Bottom, W, H - r.Bottom)
left   = (0, r.Top, r.Left, r.Height)
right  = (r.Right, r.Top, W - r.Right, r.Height)
```

Fill those four with 40 % black. The target region keeps its original brightness, which
reads as "spotlight" rather than "selection rectangle".

## Per-monitor DPI

`CopyFromScreen` and screen coordinates are physical pixels. A WPF/WinForms overlay window
positioned in DIPs will drift on monitors with a different scale factor.

- Set the overlay window to **per-monitor DPI aware v2** in the manifest.
- Position each overlay window using physical pixels (`SetWindowPos`), not XAML
  coordinates.
- Convert mouse coordinates with `PhysicalToLogicalPointForPerMonitorDPI` only when you
  need logical values for XAML elements.

Known limitation: mixed-DPI multi-monitor setups can be off by a few pixels on the
secondary monitor. Same-DPI setups are exact.

## Returning the result

The picker should hand back more than an `HWND`:

```csharp
public sealed record PickedWindow(
    IntPtr Handle,
    int ProcessId,
    string ProcessName,
    string Title,
    RECT Bounds);
```

Callers almost always need the process, and resolving it inside the picker keeps the
`OpenProcess`/`QueryFullProcessImageName` calls in one place.

## Safety rules

- Skip windows owned by your own process (`GetWindowThreadProcessId` + compare PID).
- Skip cloaked windows (`DwmGetWindowAttribute(hwnd, DWMWA_CLOAKED, ...)` non-zero).
- Handle `WM_MOUSEMOVE` throttling: hit-testing on every pixel is fine (it is a handful of
  `GetWindowRect` calls), but redrawing the dim layer is not. Only redraw when the resolved
  `HWND` changes.
- Always provide an escape path: `Esc` cancels, and closing every overlay window must
  restore the original state.

## Checklist

- [ ] Overlay covers all monitors, including negative-coordinate ones.
- [ ] Hovering the desktop wallpaper reports the wallpaper engine, not `explorer.exe`.
- [ ] Highlight edges sit on the visible window border, not 7 px outside.
- [ ] `Esc` exits cleanly; no orphaned topmost windows remain.
- [ ] Works when a window is maximised, minimised (skipped), and partially off-screen.
