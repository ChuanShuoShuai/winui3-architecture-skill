# 06 — 打包与部署

非打包 WinUI 3（不上架商店时的现实选择）有两个容易混淆的决策：

- **.NET 运行时** —— 框架依赖，还是自包含。
- **Windows App SDK 运行时** —— 独立的开关（`WindowsAppSDKSelfContained`）。

两者互不相干。常见的出货形态是：**.NET 框架依赖 + WindowsAppSDK 自包含**，
再在安装包里做运行时前置检查。

## 关键项目属性

```xml
<WindowsPackageType>None</WindowsPackageType>
<TargetFramework>net10.0-windows10.0.19041.0</TargetFramework>
<TargetPlatformMinVersion>10.0.17763.0</TargetPlatformMinVersion>
<Platforms>x64</Platforms>
<RuntimeIdentifier>win-x64</RuntimeIdentifier>
<UseWinUI>true</UseWinUI>
<WindowsAppSDKSelfContained>true</WindowsAppSDKSelfContained>
<SatelliteResourceLanguages>zh-Hans;zh-Hant;en-US</SatelliteResourceLanguages>
```

`SatelliteResourceLanguages` 是性价比最高的一行：WinUI 自带几十种语言目录，
裁到实际支持的语言能省几十兆。

## 两段式发布

只跑 `dotnet build` 不会产出可直接运行的自包含目录，必须显式 publish：

```bash
dotnet build MyApp.sln -c Release -p:Platform=x64
dotnet publish MyApp/MyApp.csproj -c Release -p:Platform=x64
```

再用 Inno Setup 把 publish 输出打成安装包。

## 包体积虚高的两个来源

### 1. 输出目录里的残留子目录

用 `recursesubdirs` 的安装脚本会把输出路径下的一切都吞进去。一个早先实验留下的
`publish/` 或 `runtimes/` 目录，能让安装包无声无息翻倍。

打包前先列输出目录，心里要有一个**已知的文件数和已知的语言子目录集合**。
多出来的都是残留，删掉重新构建。

```bash
ls "MyApp/bin/x64/Release/net10.0-windows10.0.19041.0/win-x64" | wc -l
ls "MyApp/bin/x64/Release/net10.0-windows10.0.19041.0/win-x64" | grep -v '\.'
```

### 2. WebView2 用户数据符号链接

应用用了 `WebView2` 时，运行时会在 exe 旁边创建一个目录联接：

```
MyApp.exe.WebView2 -> %LocalAppData%\MyApp\WebView2Data
```

`recursesubdirs` 会顺着它把整个运行时缓存打进包——几百个文件、几十兆，
里面还含用户会话数据。必须在安装脚本里显式排除：

```ini
[Files]
Source: "path\to\publish\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs; \
  Excludes: "*.WebView2\*,*.pdb,*.xml"
```

验证方式是数 Inno Setup 构建日志里的 `Compressing:` 行数。那个数字才是真相；
目录列表不是（普通 `find` 不跟随符号链接，会把带链接的目录算少）。

## Inno Setup 最小可用配置

```ini
#define MyAppName "MyApp"
#define MyAppVersion "1.0.0"
#define MyAppExeName "MyApp.exe"

[Setup]
AppId={{GUID-HERE}}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
DefaultDirName={autopf}\{#MyAppName}
OutputBaseFilename={#MyAppName}_setup_{#MyAppVersion}
Compression=lzma2/max
SolidCompression=yes
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
PrivilegesRequired=admin
WizardStyle=modern
SetupIconFile=app.ico
UninstallDisplayIcon={app}\{#MyAppExeName}

[Files]
Source: "publish\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs; \
  Excludes: "*.WebView2\*"

[Icons]
Name: "{autoprograms}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"
Name: "{autodesktop}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; Tasks: desktopicon

[Tasks]
Name: "desktopicon"; Description: "创建桌面快捷方式"; Flags: unchecked

[Run]
Filename: "{app}\{#MyAppExeName}"; Flags: nowait postinstall skipifsilent
```

命令行构建：

```bash
"F:/Inno Setup 7/ISCC.exe" MyApp.iss
```

## 版本号有四处

只改 csproj 里的 `<Version>` 不够。一次发版要动：

1. `.csproj` → `<Version>`
2. 关于页 / 设置页的显示文字
3. 更新检查用的 `LocalVersion` 常量
4. `.iss` → `#define MyAppVersion`（同时决定安装包文件名和版本资源）

漏掉第 4 处，安装包文件名和二进制文件版本不一致，更新检测会失效。

## 出货时的运行时前置检查

`WindowsAppSDKSelfContained=true` 让 WinAppSDK 跟着应用走，但 **.NET 桌面运行时不会**，
除非同时打开 `<SelfContained>true</SelfContained>`（体积会冲到 150 MB 以上）。

二选一：

- **瘦包**：.NET 框架依赖 + 安装脚本里的 `[Code]` 段检查运行时（跑
  `dotnet --list-runtimes` 或查注册表），缺失时给出下载链接。
- **胖包**：`<SelfContained>true</SelfContained>`，150 MB 左右，无前置条件。

把这个选择写进 README。「在我机器上打不开」十有八九是缺 .NET 桌面运行时。

## 发布检查清单

- [ ] `dotnet build -c Release -p:Platform=x64` → 0 错误。
- [ ] publish 输出里没有 `coreclr.dll`（除非确实自包含）。
- [ ] 输出目录文件数与上一版一致（± 本次改动）。
- [ ] publish 目录里没有 `*.WebView2` 联接。
- [ ] 安装包体积与上一版相差不超过约 5 MB，超出就要查。
- [ ] 干净虚拟机（无 .NET）→ 安装包能跑 → 应用能启动（或检查逻辑给出明确提示）。
- [ ] 关于页里的版本号与安装包文件名一致。
- [ ] 卸载只删 `%LocalAppData%\<应用>`（用户勾选时才删），否则保留配置。
