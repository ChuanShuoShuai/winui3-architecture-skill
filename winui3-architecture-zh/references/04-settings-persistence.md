# 04 — 设置持久化

配置存 `%LocalAppData%\<应用名>\settings.json`。不要放 exe 旁边（安装目录只读），
不要进注册表（卸载留垃圾），不要用 `AppData\Roaming`（这是本机 UI 状态）。

## 结构

静态类 + 普通属性，不引依赖注入容器、不用 `IOptionsMonitor`——一个只有一个配置文件的
桌面应用不需要那套仪式。

```csharp
public static class AppSettings
{
    private static readonly string Dir = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "MyApp");
    private static readonly string FilePath = Path.Combine(Dir, "settings.json");

    public static string SettingsFilePath => FilePath;   // 供导入/导出

    public static int ThemeIndex { get; set; }                 // 0 系统, 1 浅色, 2 深色
    public static int BackdropMode { get; set; } = 2;          // 0 无, 1 亚克力, 2 Mica
    public static int ScanIntervalSec { get; set; } = 3;
    public static List<string> PinnedApps { get; set; } = new();
}
```

## 读取：逐键，不要整体反序列化

`JsonSerializer.Deserialize<AppSettings>()` 只要有一个属性类型不符就整个文件失败，
而且类里已经删掉的键会被它静默丢弃。用 `TryGetProperty` 逐键读，文件不完整或偏旧时
也能拿到它确实包含的部分：

```csharp
public static void Load()
{
    try
    {
        if (!File.Exists(FilePath)) return;
        using var doc = JsonDocument.Parse(File.ReadAllText(FilePath));
        var root = doc.RootElement;

        if (root.TryGetProperty("ThemeIndex", out var t)) ThemeIndex = t.GetInt32();
        if (root.TryGetProperty("BackdropMode", out var b)) BackdropMode = b.GetInt32();
        if (root.TryGetProperty("ScanIntervalSec", out var s)) ScanIntervalSec = s.GetInt32();

        if (root.TryGetProperty("PinnedApps", out var p) && p.ValueKind == JsonValueKind.Array)
        {
            PinnedApps = p.EnumerateArray()
                .Where(x => x.ValueKind == JsonValueKind.String)
                .Select(x => x.GetString()!)
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .ToList();
        }
    }
    catch { /* 文件损坏：保留默认值 */ }
}
```

这个空 `catch` 是刻意的：损坏的配置文件绝不能挡住启动，下一次 `Save()` 会把它写回合法内容。

## 保存

序列化一个匿名对象，让落盘的键集合显式、可评审：

```csharp
File.WriteAllText(FilePath, JsonSerializer.Serialize(new
{
    ThemeIndex,
    BackdropMode,
    ScanIntervalSec,
    PinnedApps
}));
```

改动时写一次、退出时再写一次。滑杆每动一格就写也没关系——文件只有几百字节。

## 默认值变更的迁移陷阱

这一条很隐蔽，但每个活得久的应用都会撞上。

假设刷新间隔默认 2 秒。用户装上后从没动过这个设置，`settings.json` 里记着
`ScanIntervalSec: 2`。后来你把默认值改成 3 秒。升级后 `Load()` 读到 `2`——
你无法区分这是旧默认值，还是用户特意选的 2。**新默认值永远到不了老用户手里。**

用版本标记解决：

```csharp
private const int IntervalDefaultsVersion = 2;   // 改一次默认值就 +1
public static int IntervalDefaultsApplied { get; set; } = IntervalDefaultsVersion;

// Load() 里读完各项之后
bool migrated = false;
if (IntervalDefaultsApplied < IntervalDefaultsVersion)
{
    ScanIntervalSec = 3;                          // 新默认值
    IntervalDefaultsApplied = IntervalDefaultsVersion;
    migrated = true;
}
if (ScanIntervalSec is not (1 or 2 or 3))         // 防御性收敛
{
    ScanIntervalSec = 3;
    migrated = true;
}

if (migrated) Save();
```

### 顺序规矩

`if (migrated) Save();` 必须是 `Load()` 的**最后一句**，所有属性都读过之后才能执行。
在读取中途调 `Save()`，会把还没读到的属性按默认值序列化出去，等于静默清空用户配置。

## 校验与收敛

永远不要信任文件内容。所有驱动 UI 和计时的数值都要收敛：

```csharp
if (ScanIntervalSec is not (1 or 2 or 3)) ScanIntervalSec = 3;
if (BackdropMode is < 0 or > 2) BackdropMode = 2;
```

手工改出来的 `ScrollViewer.ZoomFactor = 0` 会让界面冻结，看起来像卡死，
而不像配置问题。

## 导出 / 导入

把 `SettingsFilePath` 暴露出去，让用户自己拷 JSON。需要正式保存对话框时，
用 Win32 经典对话框而不是 `FileSavePicker`——后者在部分 Win10 版本的非打包应用里会抛异常。

```csharp
// Services/ClassicFileDialog.cs —— P/Invoke GetSaveFileNameW
var path = ClassicFileDialog.PickSaveFile(ownerHandle,
    filter: "JSON (*.json)|*.json|所有文件 (*.*)|*.*",
    defaultExt: "json");
```

文本落盘用 `new UTF8Encoding(true)`（**带 BOM**）。没有 BOM 时，Excel 和不少中文编辑器
打开导出的 CSV/JSON 会乱码。

## 检查清单

- [ ] 删掉文件 → 应用按默认值启动，不崩。
- [ ] 把文件截断成 `{` → 应用按默认值启动，不崩。
- [ ] 改一项 → 重启 → 值保留。
- [ ] 改默认值 + 版本标记 → 老配置迁一次，之后尊重用户选择。
- [ ] 配置文件在 `%LocalAppData%` 下，不在 exe 旁边。
