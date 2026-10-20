# 02 — 外壳与导航

一个 `Window`、一个 `NavigationView`、页面挂在 `Frame` 里。不要为「次要功能」再开一个
顶层窗口——所有东西都走外壳，主题、材质、设置状态才能集中在一处。

## 布局

```xml
<Grid x:Name="RootGrid">
    <Grid.RowDefinitions>
        <RowDefinition Height="Auto" />   <!-- 标题栏 / 头部 -->
        <RowDefinition Height="*" />      <!-- NavigationView -->
    </Grid.RowDefinitions>

    <Grid x:Name="TitleBarArea" Grid.Row="0" Height="32" Background="Transparent" />

    <NavigationView x:Name="NavView" Grid.Row="1"
                    PaneDisplayMode="Auto"
                    IsSettingsVisible="False"
                    IsBackButtonVisible="Collapsed"
                    IsTitleBarAutoPaddingEnabled="False"
                    OpenPaneLength="200"
                    CompactModeThresholdWidth="560"
                    ExpandedModeThresholdWidth="1008"
                    DisplayModeChanged="NavView_DisplayModeChanged"
                    SelectionChanged="NavView_SelectionChanged">
        <NavigationView.MenuItems>
            <NavigationViewItem Icon="Home" Content="监控" Tag="monitor" />
            <NavigationViewItem Content="看板" Tag="dashboard">
                <NavigationViewItem.Icon>
                    <FontIcon Glyph="&#xE8A0;" />
                </NavigationViewItem.Icon>
            </NavigationViewItem>
        </NavigationView.MenuItems>

        <NavigationView.FooterMenuItems>
            <NavigationViewItem Icon="Setting" Content="设置" Tag="settings" />
        </NavigationView.FooterMenuItems>

        <Frame x:Name="ContentFrame" />
    </NavigationView>
</Grid>
```

## 这些值为什么这么定

| 属性 | 取值 | 原因 |
|---|---|---|
| `IsSettingsVisible` | `False` | 内置「设置」项固定在底部，位置无法掌控；放进 `FooterMenuItems` 才能自定义图标和顺序 |
| `IsBackButtonVisible` | `Collapsed` | 桌面应用靠选择导航，不靠历史栈，返回按钮是噪音 |
| `IsTitleBarAutoPaddingEnabled` | `False` | 自绘标题栏时必须关掉，否则导航栏整体下移 32 px |
| `OpenPaneLength` | `200` | 默认 320 px 会在 1000 px 宽的窗口里吃掉三分之一 |
| `PaneDisplayMode` | `Auto` | 按宽度在三种标准模式间切换，见下 |
| `CompactModeThresholdWidth` | `560` | 低于此宽度导航栏悬浮（LeftMinimal） |
| `ExpandedModeThresholdWidth` | `1008` | 高于此宽度保持展开；两者之间是 48 px 图标栏 |

## 三档导航模式，以及 LeftMinimal 的覆盖陷阱

| 窗口宽度 | 模式 | 行为 |
|---|---|---|
| ≥ `ExpandedModeThresholdWidth` | Left | 导航栏并排，内容在右侧 |
| 两阈值之间 | LeftCompact | 48 px 图标栏，内容在右侧 |
| < `CompactModeThresholdWidth` | **LeftMinimal** | **导航栏浮在内容之上** |

LeftMinimal 下内容一直延伸到导航栏底下，汉堡按钮正好落在内容左上角——不加处理就会
压住每个页面的第一个标题；展开导航栏时，半透明面板还会整块盖在内容上。

标准修法：**只在 Minimal 档**给内容宿主加顶部留白，由 `DisplayModeChanged` 驱动：

```csharp
private void NavView_DisplayModeChanged(NavigationView sender, NavigationViewDisplayModeChangedEventArgs args)
{
    UpdateContentInset(args.DisplayMode);
}

private void UpdateContentInset(NavigationViewDisplayMode mode)
{
    ContentFrame.Margin = mode == NavigationViewDisplayMode.Minimal
        ? new Thickness(0, 48, 0, 0)   // 避开悬浮的汉堡按钮
        : new Thickness(0);
}
```

构造函数里还要手动调一次 `UpdateContentInset(NavView.DisplayMode)`——初始模式不触发该事件。
`assets/template/` 交付的就是这套。

不想要悬浮导航栏时的替代方案：

1. **`PaneDisplayMode="Left"`** —— 任何宽度都并排；最简单，但窗口窄到 600 DIP 以下时
   200 px 导航栏挤占内容，是另一个体验问题。
2. `Auto` 并把 `CompactModeThresholdWidth` 设到比最小窗口宽度还小 —— LeftMinimal 永远不会出现。

无论选哪个，都要在你支持的最窄窗口尺寸下验证一遍。

## 导航处理

所有导航收敛到一个按 `Tag` 分发的处理函数：

```csharp
private void NavView_SelectionChanged(NavigationView sender, NavigationViewSelectionChangedEventArgs args)
{
    if (args.SelectedItem is not NavigationViewItem item) return;
    if (item.Tag is not string tag) return;

    Type? page = tag switch
    {
        "monitor"   => typeof(MonitorPage),
        "dashboard" => typeof(DashboardPage),
        "settings"  => typeof(SettingsPage),
        _ => null
    };

    if (page is null || ContentFrame.CurrentSourcePageType == page) return;

    AppSettings.LastPage = tag;          // 下次启动恢复
    ContentFrame.Navigate(page, null, new EntranceNavigationTransitionInfo());
}
```

导航前必须判 `CurrentSourcePageType == page`。重复导航到同一页会整棵可视树重建，
滚动位置全丢。

## 恢复上次页面

窗口激活后再选中对应项，而不是直接 `Navigate`：

```csharp
DispatcherQueue.TryEnqueue(() =>
{
    var target = NavView.MenuItems.Concat(NavView.FooterMenuItems)
        .OfType<NavigationViewItem>()
        .FirstOrDefault(i => (i.Tag as string) == AppSettings.LastPage);

    NavView.SelectedItem = target ?? NavView.MenuItems[0];
});
```

设置 `SelectedItem` 会触发 `SelectionChanged`，导航走正常路径，导航栏高亮也正确。

## 自定义标题栏

```csharp
ExtendsContentIntoTitleBar = true;
SetTitleBar(TitleBarArea);
```

标题栏内容右对齐时，要在 XAML 里补上等于标题按钮宽度的左侧留白——否则在某些窗口宽度下
你的按钮会被压到最小化/关闭按钮底下。

可拖拽区域的三个条件：是 `UIElement`、背景非 null（`Transparent` 也算）、
上方没有盖住它的交互控件。

## 页面约定

- 有背景材质时，页面 `Background="{x:Null}"` 或干脆不设。
- 页面构造函数里不要做 I/O。加载放 `OnNavigatedTo`，释放放 `OnNavigatedFrom`。
- 在 `OnNavigatedTo` 订阅视图模型事件，在 `OnNavigatedFrom` 退订。否则被
  `NavigationCacheMode.Required` 缓存住的页面会随着导航次数累积处理器。

## 大文件拆分

`MainWindow.xaml.cs` 写到 6000 行是症状，不是风格。按功能拆：

```
MainWindow.xaml.cs            # 构造函数、导航、窗口级管线
MainWindow.Monitor.cs         # 监控页接线
MainWindow.Settings.cs        # 设置页接线
MainWindow.<Feature>.cs
```

partial 类共享字段和方法，没有接口成本，只是可读性的问题。
