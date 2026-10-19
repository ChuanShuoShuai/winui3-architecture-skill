# Changelog

All notable changes to this project are documented here.
本项目的所有重要变更都记录在此。

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

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

[1.0.0]: https://github.com/ChuanShuoShuai/winui3-architecture-skill/releases/tag/v1.0.0
