# Changelog

All notable changes to this project are documented here.
本项目的所有重要变更都记录在此。

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

---

## [1.0.1] — 2026-10-20

Two real-world layout fixes driven by running the demo app, plus a new controls reference.
通过实际运行示例应用发现的两个布局修复，外加一篇新的控件参考。

### Added / 新增

- **`references/10-common-controls.md`（中英两版）**：新增常用控件参考——
  消息表面四选一对照表（`ContentDialog` / `InfoBar` / `TeachingTip` / `ToolTip`）、
  `ListView` 虚拟化两条硬规矩（禁嵌套 `ScrollViewer`、禁换 `StackPanel`）、
  轮询列表"对账而非整体替换"、`ComboBox` / `ToggleSwitch` 初始化期事件触发陷阱与 `_loading` 守卫、
  `TextChanging` 与中文输入法组合期行为、`ProgressRing` 绑 `IsActive` 而非 `Visibility`、
  `Flyout` / `MenuFlyout` / `DropDownButton`、以及发版前的控件自检单。
- **示例应用新增「组件博览」页**：上述全部控件的可交互演示
  （ContentDialog 双开守卫、TeachingTip 锚定、Windows 通知实发、扫描模拟联动 InfoBar 严重级别、30 行 ListView）。
- **skills CLI 一键安装**：README 增加安装命令，
  `npx skills add ChuanShuoShuai/winui3-architecture-skill` 实测可直接发现两个 skill。

### Fixed / 修复

- **窄窗口下导航栏不收缩**：v1.0.0 模板钉死 `PaneDisplayMode="Left"`（为修覆盖问题），
  窗口缩到 600 DIP 以下时 200 px 导航栏挤得内容竖排。
  改回 `Auto` 三档自适应（`Compact=560` / `Expanded=1008`）。
- **LeftMinimal 档汉堡按钮压住页面标题**：改用 `DisplayModeChanged` 在该档给
  `ContentFrame` 加 48 px 顶部留白，其余档为 0。
- **悬浮面板无模糊、页面锐利穿透**：`NavigationViewExpandedPaneBackground` 设为
  `Transparent` 虽让 docked 态透出 Mica，但同一画刷作用于悬浮面板时页面直接看穿。
  改为直控模板内 `RootSplitView.PaneBackground`：仅"overlay 且展开"时切
  `AcrylicBrush`（`TintOpacity=0.6`，按主题取 tint 色），其余一律透明。
  挂钩五处：`Loaded` / `PaneOpening` / `PaneOpened` / `PaneClosed` / `SizeChanged`。
- **`07-tray-and-toast` 补充**：`Microsoft.Toolkit.Uwp.Notifications` 7.1.3 的传递依赖
  `System.Drawing.Common` 4.7.0 存在已知严重漏洞（GHSA-rxg9-xrhp-64gj / `NU1904`），
  应显式引用新版顶替。

### Verified / 验证

- 模板原样拷入空目录编译 **0 警告 / 0 错误**。
- 示例应用三种窗口宽度实测：宽（Left 并排）、窄 700×900 物理像素（LeftMinimal 悬浮 +
  48 px 留白 + 展开面板亚克力模糊），页面标题与 InfoBar 全程完整可读。

---

## [1.0.0] — 2026-10-19

First public release. Two Agent Skills (English and Simplified Chinese) that teach a
production-tested WinUI 3 desktop architecture.

首个公开发布版本。包含中英文两版 Agent Skill，封装一套经过生产验证的 WinUI 3 桌面应用架构。

### Added / 新增

**Skills**
- `winui3-architecture` — English edition.
- `winui3-architecture-zh` — 简体中文版。内容与英文版对等。

**References / 参考文档（各 9 篇）**
- `01-startup-and-single-instance` — 启动链路埋点、崩溃日志、命名互斥体、激活参数管道转发。
- `02-shell-and-navigation` — NavigationView 调优、`Left` vs `Auto` 的模式对照、页面宿主、自定义标题栏。
- `03-theming-and-backdrop` — Mica / 亚克力 / 无 三档材质与能力降级、主题字典、代码侧取色。
- `04-settings-persistence` — JSON 配置、逐键容错读取、默认值版本迁移标记。
- `05-diagnostics-and-crash-forensics` — 启动日志、XamlParseException HResult 速查、静默退出定位。
- `06-packaging-and-deployment` — 自包含发布、Inno Setup、包体积虚高两大来源、运行时前置条件。
- `07-tray-and-toast` — 托盘图标、Toast 激活、冷启动参数转发。
- `08-custom-controls` — 转换器约定、卡片模板、图标+文字按钮、`ContentDialog` 硬规矩。
- `09-window-targeting` — 顶层窗口命中下探、截屏遮罩、DPI 感知。

**Templates / 模板**
- `assets/template/` — 9 个可直接编译的最小骨架文件：`App.xaml(.cs)`、`MainWindow.xaml(.cs)`、
  `MicaSystemBackdrop.cs`、`AppSettings.cs`、`ThemeHelper.cs`、`app.manifest`、`Project.csproj.template`。

**Repository**
- 中英双语 README，含安装方式、覆盖范围、坑位速查表。
- MIT License.

### Fixed / 修复

Issues found by assembling the template into a real project and launching it — none of them
surface in the skill validator:

- **缺少 `app.manifest`** — csproj 引用了它但模板没带，直接拷过去编译失败。
- **缺少 `using Microsoft.UI.Xaml.Media.Animation`** — `EntranceNavigationTransitionInfo` 报 CS0246。
- **包版本不可用** — 原先钉的 Windows App SDK 版本不在常规缓存里，改为实测通过的 `2.5.1`。
- **`ApplicationIcon` 硬引用** — 模板不带 .ico，改为可选。
- **导航栏覆盖页面内容** — `PaneDisplayMode="Auto"` 在 800 DIP 窗口下落入 LeftMinimal，
  导航栏浮在内容之上，汉堡按钮压住每个页面的标题。改为 `Left`，任何宽度都并排。
- **汉堡按钮焦点框** — 导航切换按钮启动时抢键盘焦点，画出一圈粗黑焦点矩形。
  改为加载后把焦点交给内容区（`FocusState.Pointer`）。
- **`startup.log` 重复标记** — `MainWindow ctor done` 在 App 与窗口构造里各写了一次。

### Verified / 验证

Not just "it looks fine" — the template was assembled, compiled and launched:

- 模板原样拷入空目录编译：**0 警告 / 0 错误**（.NET 10.0.402 + WindowsAppSDK 2.5.1）。
- 组装后的应用实际启动：`startup.log` 走完全链路，`crash.log` 无新增。
- Mica 在 Windows 11 上生效；浅色 / 深色切换正常；设置持久化与「恢复上次页面」正常。
- 两个 Skill 均通过 `package_skill.py` 结构校验。

### Known limitations / 已知限制

- 材质降级（Mica → 亚克力 → 无）在 Windows 11 上无法完整验收，需在 Windows 10 环境验证。
- 模板面向非打包（`WindowsPackageType=None`）x64 部署，未覆盖 MSIX 打包流程。
- `dotnet publish` 的产物体积基线为经验值，不同 SDK 版本会有差异。

---

[1.0.1]: https://github.com/ChuanShuoShuai/winui3-architecture-skill/releases/tag/v1.0.1
[1.0.0]: https://github.com/ChuanShuoShuai/winui3-architecture-skill/releases/tag/v1.0.0
