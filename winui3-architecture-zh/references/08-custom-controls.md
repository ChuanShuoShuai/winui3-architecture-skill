# 08 — 自定义控件与 UI 约定

WinUI 3 内置控件能覆盖八成需求。剩下的两成，靠下面这些约定保持一致，
并绕开 XAML 里的运行期陷阱。

## 转换器

两个转换器撑起大多数应用：

```csharp
public sealed class NullToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language)
        => value is null ? Visibility.Visible : Visibility.Collapsed;

    public object ConvertBack(object value, Type targetType, object parameter, string language)
        => throw new NotSupportedException();
}

public sealed class BoolToVisibilityConverter : IValueConverter
{
    public bool Invert { get; set; }

    public object Convert(object value, Type targetType, object parameter, string language)
    {
        bool b = value is bool v && v;
        if (Invert) b = !b;
        return b ? Visibility.Visible : Visibility.Collapsed;
    }

    public object ConvertBack(object value, Type targetType, object parameter, string language)
        => throw new NotSupportedException();
}
```

在 `App.xaml` 里注册一次：

```xml
<conv:NullToVisibilityConverter x:Key="NullToVisibleConverter" />
<conv:BoolToVisibilityConverter x:Key="BoolToVisibleConverter" />
```

### 图标占位模式

程序图标是带透明像素的位图。在下面垫一个字形、指望透明区透出来，
结果是被图标自身的空白区域把字形露出来。让两者互斥：

```xml
<Grid Width="22" Height="22">
    <FontIcon Glyph="&#xE71D;" FontSize="14"
              Foreground="{ThemeResource TextFillColorTertiaryBrush}"
              Visibility="{Binding Icon, Converter={StaticResource NullToVisibleConverter}}" />
    <Image Source="{Binding Icon}" Width="22" Height="22" Stretch="Uniform" />
</Grid>
```

`Icon` 为 `null` 时才显示字形；`Image` 源为空时本身不渲染。

## 卡片模板

重复行用 `DataTemplate` 资源。每个文字元素的水平和 `TextTrimming="CharacterEllipsis"`
都要显式写——会随长名字撑开的卡片会毁掉整个视觉节奏。

```xml
<DataTemplate x:Key="ItemCardTemplate">
    <Border Background="{ThemeResource CardBackgroundFillColorDefaultBrush}"
            CornerRadius="8" Padding="12,10" Margin="0,0,0,8">
        <Grid>
            <Grid.ColumnDefinitions>
                <ColumnDefinition Width="4" />
                <ColumnDefinition Width="*" />
            </Grid.ColumnDefinitions>
            <Border Width="4" CornerRadius="2"
                    Background="{ThemeResource AccentFillColorDefaultBrush}" />
            <StackPanel Grid.Column="1" Margin="10,0,0,0" Spacing="2">
                <TextBlock Text="{Binding Title}" FontWeight="SemiBold"
                           TextTrimming="CharacterEllipsis" />
                <TextBlock Text="{Binding Subtitle}"
                           Style="{StaticResource CaptionTextBlockStyle}"
                           Foreground="{ThemeResource TextFillColorSecondaryBrush}" />
            </StackPanel>
        </Grid>
    </Border>
</DataTemplate>
```

## 图标 + 文字按钮

全应用统一用「Segoe Fluent Icons + 文字」，图标才能对齐。

**XAML：**

```xml
<Button>
    <StackPanel Orientation="Horizontal" Spacing="6">
        <FontIcon Glyph="&#xE8E5;" FontSize="14" />
        <TextBlock Text="刷新" />
    </StackPanel>
</Button>
```

**代码：**

```csharp
public static TextBlock SetButtonIconText(Button button, string glyph, string text)
{
    var panel = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 };
    panel.Children.Add(new FontIcon { Glyph = glyph, FontSize = 14 });
    var label = new TextBlock { Text = text };
    panel.Children.Add(label);
    button.Content = panel;
    return label;
}
```

文案要变的时候，拿到返回的 `TextBlock` 改 `.Text`。
直接 `button.Content = "新文案"` 会把图标一起冲掉。

表头排序按钮是例外：不加图标，只有文字加排序箭头。

## 用代码构建 UI

优先 XAML。结构取决于数据时（列数不定、动态表单）才用代码。

```csharp
var panel = new StackPanel { Spacing = 8 };
foreach (var field in schema.Fields)
{
    panel.Children.Add(new TextBlock { Text = field.Label, FontWeight = FontWeights.SemiBold });
    panel.Children.Add(new TextBox { Text = field.Value ?? string.Empty });
}
Host.Children.Add(panel);
```

规矩：

- 间距设在容器上（`Spacing` / `Margin`），不要逐个孩子设。
- 颜色走 `ThemeHelper.Brush(...)`，不要硬编码。
- 需要真正的 `Geometry` 或模板时用 `Microsoft.UI.Xaml.Markup.XamlReader.Load(string)`；
  没有公开的 `Geometry.Parse`。

## ContentDialog

对话框是自找 bug 的重灾区。

**禁止嵌套。** 已有 `ContentDialog` 打开时再调 `ShowAsync` 会抛异常。下钻要这么做：
记下导航意图，关掉当前框，等关闭完成后再开下一个：

```csharp
private async Task ShowDetail(Item item)
{
    while (true)
    {
        var dialog = BuildDetailDialog(item);
        var result = await dialog.ShowAsync();

        if (_pendingDrillDown is { } child) { _pendingDrillDown = null; item = child; continue; }
        if (_pendingBack) { _pendingBack = false; item = Stack.Pop(); continue; }
        return;
    }
}
```

**内容容器必须是 `Grid`（`Auto/*/Auto`），不能是 `StackPanel`。** `ContentDialog`
对超长内容是**裁剪**而不是滚动，`StackPanel` 末尾的按钮会直接看不见，且没有滚动条。

**行距用各元素自己的 `Margin`**，不要用 `RowSpacing`：`RowSpacing` 对折叠的行也会
生效，出现空隙后看起来像渲染 bug。

**防连点。** 第一个对话框还没渲染出来时连点两次，`ShowAsync` 会抛
「只能打开一个 ContentDialog」。在 `await` 之前同步置一个 `_showing` 标志。

## 资源键：用之前先验证

引用不存在的 `ThemeResource` 键：

- 编译**零警告**。
- 运行期抛 `XamlParseException`，通常整页崩掉。

要加不熟悉的键，先在项目里确认它确实存在：

```bash
grep -c "SomeControlBrush" -r .
```

计数大于 1 说明这个键已经被验证过能解析。

## 检查清单

- [ ] XAML 里没有字面颜色，全部是 `{ThemeResource ...}` 或语义画刷。
- [ ] 图标+文字按钮统一走辅助方法。
- [ ] 对话框内容是 `Grid`，没有用 `RowSpacing`。
- [ ] 每个新增的 `ThemeResource` 键都 grep 验证过。
- [ ] 新界面在浅色和深色下各渲染一次再交付。
