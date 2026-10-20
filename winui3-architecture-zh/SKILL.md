---
name: winui3-architecture-zh
description: 按经过生产验证的架构编写 WinUI 3 桌面应用。适用于：新建 WinUI 3 / Windows App SDK 项目、把现有项目改造成标准外壳、排查 WinUI 3 运行时崩溃。覆盖单实例启动与崩溃取证、NavigationView 外壳导航、Mica/亚克力/无三档背景材质与降级、设置持久化与默认值迁移、自包含发布与 Inno Setup 打包、托盘与 Toast 通知、自定义控件约定。面向非打包（WindowsPackageType=None）x64 应用，支持 Windows 10 1809 至 Windows 11。
agent_created: true
---

# WinUI 3 应用架构

写一个启动可靠、在 Windows 10 上优雅降级、并且不会栽在"编译期毫无征兆、运行时直接崩"的应用。

## 何时使用本技能

- 新建必须非打包运行（不用 MSIX）的 WinUI 3 桌面项目。
- 给已有应用补外壳：导航、主题、设置、托盘。
- 排查「程序静默退出」「启动即 XamlParseException」「Mica 没效果」这类问题。
- 准备带自包含运行时和安装包的发布版本。

不适用于 UWP、WPF、MAUI —— XAML 方言和生命周期都不一样。

## 架构总览

```
MyApp/
├─ App.xaml / App.xaml.cs        # 组合根：互斥体、崩溃日志、创建窗口
├─ MainWindow.xaml(.cs)          # 只放外壳：NavigationView + 页面宿主
├─ MainWindow.<Feature>.cs       # 大功能拆成 partial 文件
├─ MicaSystemBackdrop.cs         # SystemBackdrop 子类：Mica/亚克力/无
├─ AppSettings.cs                # 静态设置 + JSON 落盘 + 迁移标记
├─ ThemeHelper.cs                # 代码侧主题资源查找
├─ Models/                       # 实现 INotifyPropertyChanged 的视图模型
├─ Converters/                   # IValueConverter 实现
├─ Services/                     # 系统互操作、对话框、渲染
└─ Core/                         # 纯逻辑，不依赖 UI（可无头单测）
```

两条最关键的规矩：

1. **`Core/` 不引用 `Microsoft.UI.*`** —— 纯逻辑保持可无头测试。
2. **`MainWindow.xaml.cs` 只放外壳，不放功能** —— 单文件超过约 800 行就拆成
   `MainWindow.<Feature>.cs`。

## 搭建顺序

严格按顺序来，每一步都依赖上一步能编译通过。

### 1. 项目文件

起点是 `assets/template/Project.csproj.template`。几个不显然但关键的属性：

```xml
<WindowsPackageType>None</WindowsPackageType>          <!-- 非打包：无 MSIX、不上架 -->
<TargetFramework>net10.0-windows10.0.19041.0</TargetFramework>
<TargetPlatformMinVersion>10.0.17763.0</TargetPlatformMinVersion>  <!-- Win10 1809 -->
<Platforms>x64</Platforms>
<RuntimeIdentifier>win-x64</RuntimeIdentifier>
<WindowsAppSDKSelfContained>true</WindowsAppSDKSelfContained>
<SatelliteResourceLanguages>zh-Hans;zh-Hant;en-US</SatelliteResourceLanguages>
```

只靠 `SatelliteResourceLanguages` 这一行就能省掉几十兆用不上的语言目录。
注意 **Windows App SDK 的自包含（`WindowsAppSDKSelfContained`）和 .NET 的自包含
（`SelfContained`）是两个独立开关**，别混为一谈。

### 2. App：互斥体、崩溃日志、创建窗口

照抄 `assets/template/App.xaml.cs`，它按这个顺序建立：

1. `UnhandledException` 在**构造函数里**挂上，早于任何 XAML 解析。
2. `OnLaunched` 第一件事取命名互斥体（`Local\<名称>_SingleInstance`）。
3. 命名管道，接收第二个实例转发来的激活参数。
4. `new MainWindow()` 包在 try/catch 里，异常写 `crash.log`。

启动链路要一步一步打日志（`App 构造 → OnLaunched → 窗口创建 → 激活完成`）。
WinUI 应用在首帧之前死掉时，这份日志是唯一留下的证据。

### 3. 外壳：NavigationView

照抄 `assets/template/MainWindow.xaml`。外壳是一个两行 `Grid`：

- 第 0 行：自定义标题栏 / 头部条（`Auto`）。
- 第 1 行：`NavigationView`，`MenuItems` 放主页面，`FooterMenuItems` 放设置等次级入口。

保持 `IsSettingsVisible="False"`，把「设置」放进 `FooterMenuItems`，这样它的位置
和图标都在你手里。用 `Tag` 标识目标页，所有导航收敛到一个 `SelectionChanged` 里。

### 4. 主题与背景材质

照抄 `assets/template/MicaSystemBackdrop.cs` 和 `ThemeHelper.cs`。材质要把用户意图
折算成这台机器真能用的东西：

```csharp
Mica    → 亚克力   当 MicaController.IsSupported() 为 false
亚克力  → 无       当 DesktopAcrylicController.IsSupported() 为 false
```

**存用户意图，显示实际生效值。** Win10 上把 Mica 选项置灰并自动降级，绝不能让用户
选了之后什么都没发生。

### 5. 设置持久化

照抄 `assets/template/AppSettings.cs`。静态类 + 普通属性 + `Load()` / `Save()`，
用 `JsonDocument` 配 `TryGetProperty` 逐键读取，文件被截断或版本较老时也不会抛异常。
两个后面一定会踩的细节：

- **默认值会变的项必须有版本标记。** 默认值从 2 秒改成 3 秒时，老配置里存的是
  *旧默认值*，不是用户的选择，不加标记新默认值永远到不了老用户手里。
- **迁移落盘必须放在所有属性读完之后。** 在 `Load()` 中间调 `Save()` 会把还没读到
  的项按默认值写回去，等于清空用户配置。

### 6. 页面与视图模型

页面放 `Pages/`，宿主是 NavigationView 内容区的 `Frame`。视图模型实现
`INotifyPropertyChanged`，并暴露**派生文本属性**（`CpuText`、`MemoryText`、
`StatusText`），在源字段变化时连带触发 `PropertyChanged`。绑定这些属性，
不要指望 `StringFormat` —— WinUI 没有 `StringFormat`。

定时刷新的集合要**原地对账**，不要整体替换。整体替换会触发 `Reset`，
把可见容器全部拆掉重建。

## 不可违反的硬规矩

下面每一条都不会在出错处报错，但每一条都真实地毁过一次发布。

| 规矩 | 失败表现 |
|---|---|
| `x:Name` 不能和同类中的方法重名 | `CS0102`，紧接着一串「未知类型」的 XAML 假错误 —— 先修重名，别管那串级联 |
| 只引用项目里确实存在的 `ThemeResource` 键 | 编译零警告，运行期抛 `XamlParseException`，整页崩掉 |
| 同一个 `Geometry` 实例不能赋给两个 `Path.Data` | `ArgumentException 0x80070057`，在 `set_Data` 内部抛出，外层 try/catch 接不住 |
| `ContentDialog` 不能嵌套 `ShowAsync` | 用「隐藏当前框 → await 返回后开下一个」的方式串起来 |
| `Microsoft.UI.Xaml.Window` 没有 `Visibility` 属性 | 自己维护「已隐藏到托盘」的标志位 |
| `PaneDisplayMode="Auto"` 在窄于紧凑阈值时导航栏浮在页面上 | 汉堡按钮压住每个页面的标题 |
| 导航栏切换按钮启动时抢走键盘焦点 | 汉堡按钮上出现一圈粗黑焦点框 |
| 用了背景材质时，不要给 `Window` / `NavigationView` / 页面根节点铺不透明底色 | 材质画了但被盖住，窗口看着是纯灰色 |
| 轮询循环里不要逐实例调 `PerformanceCounter` | 每次刷新分配几兆内存；改用 PDH 数组查询 |

## 验收清单

宣布完工之前逐条走一遍：

- [ ] `dotnet build -c Release -p:Platform=x64` → 0 错误，自己的代码 0 警告。
- [ ] 删掉 `%LocalAppData%\<应用>\settings.json`，启动，确认默认值生效。
- [ ] 连开两次 → 第二个实例退出，第一个窗口到前台。
- [ ] 浅色/深色/跟随系统 × 三档材质 各切一遍，无闪烁。
- [ ] 窗口宽度拖到 1000 以下 → 导航栏收成图标；1200 以上 → 展开。
- [ ] 自包含发布，装到没装 .NET 的干净虚拟机，能启动。
- [ ] 在页面构造函数里故意抛异常 → `crash.log` 里有完整的 InnerException 链。

## 参考文档索引

按需加载，不要一次全读。

| 文件 | 什么时候读 |
|---|---|
| `references/01-startup-and-single-instance.md` | 应用生命周期、互斥体、激活参数转发、崩溃日志格式 |
| `references/02-shell-and-navigation.md` | NavigationView 参数调优、页面宿主、标题栏、自适应导航栏 |
| `references/03-theming-and-backdrop.md` | Mica/亚克力、主题字典、代码侧取 Brush |
| `references/04-settings-persistence.md` | 配置结构、迁移标记、导入导出 |
| `references/05-diagnostics-and-crash-forensics.md` | 启动日志、崩溃日志、转储研判、静默退出定位 |
| `references/06-packaging-and-deployment.md` | 自包含发布、Inno Setup、包体积排查、运行时前置条件 |
| `references/07-tray-and-toast.md` | 托盘图标、通知激活、单实例参数转发 |
| `references/08-custom-controls.md` | 转换器约定、卡片模板、代码构建 UI、图标+文字按钮 |
| `references/09-window-targeting.md` | 顶层窗口命中测试、截屏、遮罩取景器 |
| `references/10-common-controls.md` | InfoBar/ContentDialog/TeachingTip 怎么选、ListView 虚拟化、ComboBox/ToggleSwitch 初始化事件陷阱 |

## 模板

`assets/template/` 是一套能直接编译的最小骨架。整个目录拷进新项目，改掉命名空间即可。
模板里的注释说明了代码为什么是这个形状 —— 改之前先读注释。
