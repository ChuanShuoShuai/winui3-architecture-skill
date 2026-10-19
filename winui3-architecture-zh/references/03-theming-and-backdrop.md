# 03 — 主题与背景材质

WinUI 3 里有三个容易混淆的概念：

1. **主题（Theme）** —— 浅色 / 深色 / 跟随系统。决定文字、表面、控件配色。
2. **背景材质（Backdrop）** —— Mica / 亚克力 / 无。决定窗口**背后**透出什么。
3. **装饰层（Decoration）** —— 你自己画在材质之上的矢量图层。

三者相互独立。有材质不等于有主题，切主题也不等于必须换材质。

## 材质能力对照

| 材质 | Windows 11 22000+ | Windows 10 1809–21H2 |
|---|---|---|
| Mica | 支持 | **不支持**，`MicaController.IsSupported()` 返回 false |
| 亚克力（DesktopAcrylicController） | 支持 | 一般支持，仍要用 `IsSupported()` 判断 |
| 无 | 支持 | 支持 |

永远把用户意图折算成机器能力，不要反过来：

```csharp
private static BackdropKind Resolve(BackdropKind kind)
{
    if (kind == BackdropKind.Mica && !MicaController.IsSupported())
        kind = BackdropKind.Acrylic;
    if (kind == BackdropKind.Acrylic && !DesktopAcrylicController.IsSupported())
        kind = BackdropKind.None;
    return kind;
}
```

把 `EffectiveKind` 和用户偏好一起暴露出来。界面显示偏好，代码里做视觉判断用 `EffectiveKind`。

## 为什么要自定义 SystemBackdrop

`Window.SystemBackdrop = new MicaBackdrop()` 能用，但它不跟随应用主题（用户在运行时切
浅色/深色时背景不变），也无法在不重建窗口的前提下换材质。继承 `SystemBackdrop`：

```csharp
public sealed class MicaSystemBackdrop : SystemBackdrop
{
    public void SetKind(BackdropKind kind)
    {
        if (_kind == kind) return;          // 切主题会反复进来，不短路会闪
        _kind = kind;
        if (_target != null) ApplyControllers(_target);
        else EffectiveKind = Resolve(kind);
    }

    public void SetTheme(SystemBackdropTheme theme)
    {
        _theme = theme;
        if (_configuration != null) _configuration.Theme = theme;
    }

    protected override void OnTargetConnected(ICompositionSupportsSystemBackdrop target, XamlRoot root)
    {
        base.OnTargetConnected(target, root);
        _target = target;
        _configuration = new SystemBackdropConfiguration { IsInputActive = true, Theme = _theme };
        ApplyControllers(target);
    }

    protected override void OnTargetDisconnected(ICompositionSupportsSystemBackdrop target)
    {
        base.OnTargetDisconnected(target);
        ReleaseControllers(target);
        _target = null;
        _configuration = null;
    }
}
```

完整实现见 `assets/template/MicaSystemBackdrop.cs`。

`if (_kind == kind) return;` 不是性能优化。主题切换会反复调 `SetKind`，少了这行就会
反复重建控制器，窗口每次闪一下。

## 不透明底色的陷阱

材质是合成在内容**背后**的。材质和桌面之间只要有任何一个不透明画刷，材质就完全看不见——
窗口看起来是个纯灰矩形，于是你得出「Mica 不生效」的结论。

- `Window` —— 不设背景。
- `NavigationView` —— 把导航面板的画刷改成 `Transparent`（见下）。
- 页面根 `Grid` —— `Background="{x:Null}"`。
- `Frame` —— 不设背景。

用户选 **无** 的时候，窗口背后什么都没有，必须自己铺底色：

```csharp
RootGrid.Background = hasMaterial
    ? null
    : new SolidColorBrush(ThemeHelper.CurrentTheme == ApplicationTheme.Dark
        ? Color.FromArgb(255, 32, 32, 32)
        : Color.FromArgb(255, 243, 243, 243));
```

漏了这一步，「无」模式下会透出桌面图标。

## 主题字典

语义画刷要放在 `XamlControlsResources` **之后**的合并字典里。直接内嵌在
`Application.Resources` 里的 `ThemeDictionaries` 无法覆盖 `XamlControlsResources`
已经定义的同名键。

```xml
<ResourceDictionary.MergedDictionaries>
    <XamlControlsResources xmlns="using:Microsoft.UI.Xaml.Controls" />
    <ResourceDictionary>
        <ResourceDictionary.ThemeDictionaries>
            <ResourceDictionary x:Key="Light">
                <SolidColorBrush x:Key="RiskCriticalBrush" Color="#C42B1C" />
                <SolidColorBrush x:Key="NavigationViewDefaultPaneBackground" Color="Transparent" />
                <SolidColorBrush x:Key="NavigationViewExpandedPaneBackground" Color="Transparent" />
            </ResourceDictionary>
            <ResourceDictionary x:Key="Dark">
                <SolidColorBrush x:Key="RiskCriticalBrush" Color="#FF99A4" />
                <SolidColorBrush x:Key="NavigationViewDefaultPaneBackground" Color="Transparent" />
                <SolidColorBrush x:Key="NavigationViewExpandedPaneBackground" Color="Transparent" />
            </ResourceDictionary>
        </ResourceDictionary.ThemeDictionaries>
    </ResourceDictionary>
</ResourceDictionary.MergedDictionaries>
```

深色不是把浅色压暗。强调色和状态色在深色下需要更高明度来保证对比度
（`#C42B1C` → `#FF99A4`）。

## 代码里取主题画刷

`Application.Current.Resources[key]` **取不到** `ThemeDictionaries` 里的项。
必须按当前主题名进 `ThemeDictionaries` 查：

```csharp
public static SolidColorBrush Brush(string key)
{
    if (Application.Current.Resources.TryGetValue(key, out var v) && v is SolidColorBrush b)
        return b;

    string theme = CurrentTheme == ApplicationTheme.Dark ? "Dark" : "Light";
    if (Application.Current.Resources.ThemeDictionaries.TryGetValue(theme, out var dictObj)
        && dictObj is ResourceDictionary dict
        && dict.TryGetValue(key, out var tv) && tv is SolidColorBrush tb)
        return tb;

    return new SolidColorBrush(Colors.Gray);
}
```

`CurrentTheme` 自己维护，并在应用主题的同一处更新。

## 运行时切换主题

```csharp
RootGrid.RequestedTheme = index switch
{
    1 => ElementTheme.Light,
    2 => ElementTheme.Dark,
    _ => ElementTheme.Default
};

// 必须在赋值之后再读 ActualTheme
ThemeHelper.CurrentTheme =
    RootGrid.ActualTheme == ElementTheme.Dark ? ApplicationTheme.Dark : ApplicationTheme.Light;

Backdrop.SetTheme(RootGrid.ActualTheme == ElementTheme.Dark
    ? SystemBackdropTheme.Dark
    : SystemBackdropTheme.Light);
```

`RequestedTheme` 赋值后再读 `ActualTheme`。设成 `Default` 时 `RequestedTheme`
不会告诉你系统实际选了哪个。

## 装饰层

矢量装饰层（波纹、网格、山峦）放在 `RootGrid` 最底层、`NavigationView` 之前，
`IsHitTestVisible="False"`，只描边不填充。一填色就把 Mica 盖住了。

不想引入 Win2D 又要柔化线条，可以把同一条路径按 9 个微偏移各画一份、
每份 `Opacity="0.16"`，近似一个盒式模糊，零依赖。

**同一个 `Geometry` 实例绝不能同时赋给多个 `Path.Data`。** 几何对象带 owner，
第二次赋值会在 `set_Data` 内部抛 `ArgumentException 0x80070057`，外层 try/catch
根本接不住。要么每条路径各解析一份，要么用 `XamlReader.Load` 逐条构造 `Path`。
