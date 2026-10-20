# WinUI 3 Architecture Skill

把一套跑过生产环境的 WinUI 3 桌面应用架构，封装成可以直接被 AI Agent 加载的 Skill。

A production-tested WinUI 3 desktop architecture, packaged as an Agent Skill.

---

## 这是什么 / What this is

这是一个 [WorkBuddy](https://www.workbuddy.cn) / Claude Code 风格的 **Skill 包**。装上之后，
Agent 在新建或改造 WinUI 3 项目时会自动加载本技能，按同一套架构写代码，并避开那些
"编译期零警告、运行期直接崩"的坑。

An Agent Skill that teaches AI coding assistants a proven WinUI 3 architecture — including the
non-obvious runtime pitfalls that produce no compiler error.

## 两个版本 / Two editions

| 目录 | 语言 | 适用 |
|---|---|---|
| `winui3-architecture/` | English | 英文对话、国际化团队 |
| `winui3-architecture-zh/` | 简体中文 | 中文对话 |

两版内容对等，`assets/template/` 共用同一套骨架代码（注释为英文）。

## 安装 / Install

**One command / 一条命令**（skills CLI，支持 Claude Code / Codex / Cursor / Windsurf 等数十种 Agent）：

```bash
npx skills add ChuanShuoShuai/winui3-architecture-skill
```

只要其中一个版本 / Just one edition:

```bash
npx skills add ChuanShuoShuai/winui3-architecture-skill --skill winui3-architecture     # English
npx skills add ChuanShuoShuai/winui3-architecture-skill --skill winui3-architecture-zh  # 简体中文
```

**Manual copy / 手动复制**：

```bash
# 用户级：所有项目可用
cp -r winui3-architecture ~/.workbuddy/skills/

# 项目级：跟随仓库共享
cp -r winui3-architecture .workbuddy/skills/
```

装好后无需配置，Agent 会在相关任务上自动匹配。

## 覆盖内容 / What it covers

```
SKILL.md                      架构总览、搭建顺序、硬规矩、验收清单
references/
├─ 01-startup-and-single-instance.md    单实例互斥体、激活转发、崩溃日志
├─ 02-shell-and-navigation.md           NavigationView 调优、页面宿主、标题栏
├─ 03-theming-and-backdrop.md           Mica/亚克力/无三档与降级、主题字典
├─ 04-settings-persistence.md           JSON 配置、默认值迁移标记
├─ 05-diagnostics-and-crash-forensics.md 启动日志、崩溃研判、静默退出定位
├─ 06-packaging-and-deployment.md       自包含发布、Inno Setup、包体积排查
├─ 07-tray-and-toast.md                 托盘、通知激活、冷启动转发
├─ 08-custom-controls.md                转换器、卡片模板、ContentDialog 规矩
└─ 09-window-targeting.md               顶层窗口命中、截屏、全屏取景器
assets/template/             可直接编译的最小骨架（8 个文件）
```

## 架构原则 / Design principles

- **`Core/` 不引用 `Microsoft.UI.*`** —— 纯逻辑保持可无头测试。
- **外壳与功能分离** —— `MainWindow` 只放导航和窗口管线，大功能拆 partial 文件。
- **存用户意图，显示实际能力** —— Win10 没有 Mica，自动降级为亚克力并置灰选项。
- **先建取证，再写功能** —— 启动链路逐步打日志，崩溃写完整 InnerException 链。
- **发布物可解释** —— 输出目录文件数、安装包体积都要有已知基线，超出即查。

## 那些代价很大的坑 / Hard-won pitfalls

| 陷阱 | 后果 |
|---|---|
| `x:Name` 与同类方法重名 | CS0102 + 一串"未知类型"的假 XAML 错误 |
| 引用不存在的 `ThemeResource` | 编译通过，运行期整页崩 |
| 同一个 `Geometry` 赋给两条 `Path` | `0x80070057`，异常在 `set_Data` 里抛，接不住 |
| `ContentDialog` 嵌套 `ShowAsync` | 直接抛异常；内容用 `StackPanel` 还会被裁剪 |
| 用 `WindowFromPoint` 做窗口拾取 | 永远返回自己的遮罩窗口 |
| 打包时顺着 `*.WebView2` 符号链接递归 | 安装包平白多出几十兆用户数据 |

## 环境要求 / Requirements

- Windows 10 1809 (17763) 及以上
- .NET 10 SDK
- Windows App SDK 1.7+（模板里写的是 `Microsoft.WindowsAppSDK`）
- 打包需 Inno Setup 6/7

模板面向非打包（`WindowsPackageType=None`）x64 部署。

## 许可证 / License

MIT — 见 [LICENSE](LICENSE)。

---

如果这套架构帮你省下了排查"启动就闪退"的一下午，给个 ⭐ 就行。
