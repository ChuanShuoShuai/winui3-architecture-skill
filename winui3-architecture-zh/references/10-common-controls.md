# 10 — 常用控件：弹窗、列表、输入与反馈

选对"告诉用户一件事"的表面控件，绑列表时不杀虚拟化，避开初始化阶段的事件时序陷阱。

## 消息表面怎么选

WinUI 有四种"有事告诉用户"的表面，它们不可互换：

| 表面 | 阻断输入？ | 关闭方式 | 用途 |
|---|---|---|---|
| `ContentDialog` | **是**（模态） | 按钮 | 决策：确认删除、二选一 |
| `InfoBar` | 否 | 关闭按钮（可选） | 用户应该看到但可忽略的状态：有更新、任务完成 |
| `TeachingTip` | 否 | 轻触消失或按钮 | 锚定在控件上的一次性引导："先点这里" |
| `ToolTip` | 否 | 鼠标移开 | 悬停时的微说明 |

判断规则：**决策**用模态、**状态**用 InfoBar、**引导**用 TeachingTip、**悬停提示**用
ToolTip。最常见的误用是拿 `ContentDialog` 显示状态——它抢焦点，还会在对话框队列里排队。

`InfoBar` 严重级别：`Informational` / `Success` / `Warning` / `Error`。从代码里设 `IsOpen`；
保持 `IsClosable="True"`，除非这个条件是阻断性的。

```xml
<InfoBar x:Name="UpdateBar"
         Title="有新版本可用"
         Message="重启应用以应用更新。"
         Severity="Informational"
         IsOpen="False" />
```

`TeachingTip` 需要 `Target="{x:Bind SomeButton}"` 锚点，否则会浮在窗口中央。
"只显示一次"的做法：设置里存一个标志，`IsOpen` 只在标志未设置时打开。

## 列表

### ListView 基本用法

```xml
<ListView x:Name="ProcessList"
          ItemsSource="{x:Bind ViewModel.Items}"
          SelectionMode="Single"
          IsItemClickEnabled="False">
    <ListView.ItemTemplate>
        <DataTemplate x:DataType="local:ProcessRow">
            <Grid ColumnSpacing="8" Height="36">
                <Grid.ColumnDefinitions>
                    <ColumnDefinition Width="*" />
                    <ColumnDefinition Width="80" />
                </Grid.ColumnDefinitions>
                <TextBlock Text="{x:Bind Name}" VerticalAlignment="Center"
                           TextTrimming="CharacterEllipsis" />
                <TextBlock Grid.Column="1" Text="{x:Bind MemoryText}"
                           HorizontalAlignment="Right" VerticalAlignment="Center" />
            </Grid>
        </DataTemplate>
    </ListView.ItemTemplate>
</ListView>
```

两条硬规矩：

1. **不要在条目模板里套固定高度的 `ScrollViewer`。** `ListView` 靠自己的 `ScrollViewer`
   做虚拟化，嵌套一个会把虚拟化完全废掉，控件会给每一行都建容器——5000 行时是好几秒的冻结。
2. **不要把 ItemsPanel 换成普通 `StackPanel`。** 默认的 `ItemsStackPanel` 才做虚拟化。
   要自定义也要用 `ItemsStackPanel` 系：
   `<ListView.ItemsPanel><ItemsPanelTemplate><ItemsStackPanel /></ItemsPanelTemplate></ListView.ItemsPanel>`。

### 数据更新：对账，不要整体替换

轮询型列表（进程、消息流、行情）要在行对象上原地更新、逐属性触发 `PropertyChanged`。
重新赋值 `ItemsSource` 会触发 Reset，把所有可见容器拆掉重建、重新查图标、滚动位置归零
——表现为每个刷新周期闪一次。

整个页面生命周期内保持**一个** `ObservableCollection<T>`（或对账辅助类）。

### 选择与点击

- `SelectionMode="Single"` + 执行动作时读 `SelectedItem`。
- `IsItemClickEnabled="True"` + `ItemClick` 事件："点行 = 打开详情"场景；此模式下可以不关心选中。
- `SelectionMode="Extended"` 支持 ctrl/shift 多选；读 `SelectedItems`。

### ListView 不适用时

- 只读的多列大表格：`DataGrid`（社区包 `CommunityToolkit.Datagrid`）或手写表头 + `ListView`。
- 完全异构的布局 / 超大规模：`ItemsRepeater` —— 没有现成的选择和虚拟化管线，换 full 布局控制。

## 选择类输入

### ComboBox

```xml
<ComboBox x:Name="IntervalBox" Header="刷新间隔" Width="200"
          SelectionChanged="IntervalBox_SelectionChanged">
    <ComboBoxItem Content="1 秒" />
    <ComboBoxItem Content="2 秒" />
    <ComboBoxItem Content="3 秒" />
</ComboBox>
```

陷阱：**代码里设置 `SelectedIndex` 也会触发 `SelectionChanged`。** 进页面时把已保存的
设置镜像进控件，必须先立 `_loading` 标志并在处理函数里提前返回——否则"用户改了间隔"
的逻辑在启动时就跑一次，把用户没碰过的值写进配置。

如果数据源在代码里，优先绑定普通对象列表（`DisplayMemberPath="Label"` +
`SelectedValuePath="Value"`），不要绑 `ComboBoxItem` 集合——`SelectedItem as ComboBoxItem`
再取 Content 的写法很脆。

### CheckBox、ToggleSwitch、RadioButtons

```xml
<CheckBox x:Name="AutoStartCheck" Content="开机自启" />

<ToggleSwitch x:Name="MonitorSwitch" Header="实时监控"
              OnContent="开" OffContent="已暂停" />

<RadioButtons Header="主题">
    <RadioButton Content="跟随系统" />
    <RadioButton Content="浅色" />
    <RadioButton Content="深色" />
</RadioButtons>
```

- `CheckBox.IsThreeState="True"` 会多出不确定态；`IsChecked` 是 `bool?`，要处理 `null`。
- `ToggleSwitch.Toggled` 有和 `ComboBox.SelectionChanged` 一样的初始化触发陷阱。
- `RadioButtons`（WinUI 包装控件，不是三个裸 `RadioButton`）自带整组一个
  `SelectionChanged` 和键盘导航。

## 文本输入

```xml
<TextBox Header="API 端点"
         PlaceholderText="https://..."
         TextChanging="Endpoint_TextChanging" />

<PasswordBox Header="API 密钥" PasswordRevealMode="Peek" />
```

- `TextChanged` 在文本变化提交后触发；`TextChanging` 在**组合过程中**触发（适合即时校验）。
- 两者对 IME 组合中的拼音/假名都不触发，只有上屏后才来事件。中文输入法下候选串期间没有事件，
  要跟随就接 `TextChanging` 并容忍中间值。
- `PasswordBox.Password` 是普通属性不是依赖属性，**不支持 `{x:Bind}`**，在代码里取值。
- 数字用 `NumberBox`：设 `Min`/`Max`，读 `Value`（double），
  `ValidationMode="InvalidInputOverwritten"` 自动清掉非法输入。

## 进度与忙碌状态

```xml
<ProgressBar x:Name="ScanProgress" Maximum="100" Value="0" />

<ProgressRing x:Name="BusyRing" IsActive="{x:Bind ViewModel.Busy, Mode=OneWay}" />
```

- `ProgressRing.IsActive="False"` 会整个隐藏——绑 `IsActive`，别绑 `Visibility`。
- 未知时长用 `ProgressBar.IsIndeterminate="True"`。
- 已知条数的扫描：`Maximum = 总数`、`Value = 已完成数`，并且**必须经
  `DispatcherQueue.TryEnqueue` 回 UI 线程更新**——工作线程直接碰 XAML 会抛一个
  看起来和线程毫无关系的 COM 异常。

## Flyout 与菜单

```xml
<Button Content="选项">
    <Button.Flyout>
        <MenuFlyout>
            <MenuFlyoutItem Text="导出 CSV" Click="ExportCsv_Click" />
            <MenuFlyoutItem Text="导出 HTML" Click="ExportHtml_Click" />
            <MenuFlyoutSeparator />
            <MenuFlyoutItem Text="清空历史" Click="Clear_Click" />
        </MenuFlyout>
    </Button.Flyout>
</Button>
```

- `Flyout` 装任意内容（小表单、取色器）；`MenuFlyout` 只能放菜单项。
- 在宿主上设 `LightDismissOverlayMode="On"` 可以把弹层之外的背景压暗。
- 点击浮层外部会自动关闭。点内部还要**保持**打开（比如在筛选框里打字），要自己管理
  `IsOpen`，或者改用 `Popup`。

## 页面里发 Toast

完整生命周期见 `07-tray-and-toast.md`。按钮里：

```csharp
new ToastContentBuilder()
    .AddText("扫描完成")
    .AddText($"共检查 {count} 个进程")
    .AddArgument("action", "open")
    .Show();
```

## 发版前的控件自检单

控件密集页面的交付前检查：

- [ ] 每个 `SelectionChanged` / `Toggled` 处理函数都防了初始化期触发。
- [ ] 5000 条数据下列表依然流畅（无嵌套 `ScrollViewer`）。
- [ ] 中文输入法往搜索框打字，候选期间结果就能跟着变。
- [ ] `ProgressRing` 在 `IsActive=false` 时消失，而不是等页面卸载。
- [ ] 每个 `ContentDialog` 连开两次测过（防双开异常）。
- [ ] Flyout 点外部关闭；含文本输入的 Flyout 打字时不关闭。
