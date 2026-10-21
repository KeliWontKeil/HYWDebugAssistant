using System.Text.Json.Nodes;
using HYWDebugAssistant.Core.Api;
using HYWDebugAssistant.Core.Composition;
using HYWDebugAssistant.Core.Runtime;
using HYWDebugAssistant.DataPlane.Carriers;
using HYWDebugAssistant.DataPlane.Queues;
using HYWDebugAssistant.Hosting.Diagnostics;
using HYWDebugAssistant.Infrastructure.Json;
using HYWDebugAssistant.Sdk.Reporting;

namespace HYWDebugAssistant.Core.Settings;

/// <summary>设置加载结果。</summary>
public enum SettingsLoadOutcome
{
    /// <summary>从文件成功加载。</summary>
    Loaded = 0,

    /// <summary>文件不存在 → 使用默认设置（首次运行，正常）。</summary>
    DefaultsCreated = 1,

    /// <summary>文件损坏/不可读 → 使用默认设置 + Issue（**不阻断启动**）。</summary>
    DefaultsAfterFailure = 2,
}

/// <summary>
/// 应用级设置的加载、校验、即时生效与持久化（主文档 §9.5）。
/// </summary>
/// <remarks>
/// <list type="bullet">
///   <item><b>载体</b>：应用目录下的单文件 JSON（便携式）；**与组合件完全分离**；</item>
///   <item><b>未知字段保留</b>：以 <see cref="JsonObject"/> 为底稿，只更新已知字段 → 未识别字段原样保留（AC-4 同策略）；</item>
///   <item><b>原子写</b>：写失败 → **内存态保留**（已生效）+ 提示未持久化；</item>
///   <item><b>多实例</b>：直接覆写（最后写入获胜）——不做冲突检测。</item>
/// </list>
/// </remarks>
public sealed class SettingsService
{
    private readonly ModuleRuntime _runtime;
    private readonly IIssueSink _issues;

    private JsonObject _preserved = new();

    /// <summary>创建设置服务。</summary>
    /// <param name="runtime">运行时（设置中的参数即时应用到它）。</param>
    /// <param name="settingsFilePath">设置文件路径（应用目录下 <c>settings.json</c>）。</param>
    /// <param name="issues">框架侧上报口（缺省用运行时自带的 Issue 中心）。</param>
    public SettingsService(ModuleRuntime runtime, string settingsFilePath, IIssueSink? issues = null)
    {
        ArgumentNullException.ThrowIfNull(runtime);
        ArgumentException.ThrowIfNullOrWhiteSpace(settingsFilePath);

        _runtime = runtime;
        _issues = issues ?? runtime.Issues;
        FilePath = Path.GetFullPath(settingsFilePath);
    }

    /// <summary>设置文件路径。</summary>
    public string FilePath { get; }

    /// <summary>当前生效的设置。</summary>
    public ApplicationSettings Current { get; private set; } = new();

    /// <summary>加载设置（启动时调用一次）。失败不阻断启动。</summary>
    public SettingsLoadOutcome Load()
    {
        if (!JsonFileStore.TryReadObject(FilePath, out var root, out var readError))
        {
            Current = new ApplicationSettings();
            _preserved = new JsonObject();

            if (string.Equals(readError, "文件不存在", StringComparison.Ordinal))
            {
                return SettingsLoadOutcome.DefaultsCreated;
            }

            _issues.Report("settings", new IssueReport(
                IssueCategory.ConfigurationError,
                IssueSeverity.Warning,
                $"设置文件无法读取（{readError}），已改用默认设置；原文件保持不变：{FilePath}"));

            return SettingsLoadOutcome.DefaultsAfterFailure;
        }

        _preserved = root!;
        Current = FromJson(root!);
        ApplyToRuntime(Current);
        return SettingsLoadOutcome.Loaded;
    }

    /// <summary>更新设置：校验 → 即时生效 → 原子写盘。</summary>
    public CommandResult Update(ApplicationSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);

        var failures = Validate(settings);
        if (failures.Count > 0)
        {
            return CommandResult.Fail(failures);
        }

        Current = settings.Clone();
        ApplyToRuntime(Current);

        MergeInto(_preserved, Current);

        if (!JsonFileStore.TryWriteObjectAtomic(FilePath, _preserved, out var writeError))
        {
            _issues.Report("settings", new IssueReport(
                IssueCategory.ConfigurationError,
                IssueSeverity.Warning,
                $"设置已生效但未能写入文件（{writeError}）；本次运行内有效，重启后丢失"));

            return CommandResult.Ok(["设置已生效，但未能持久化（应用目录可能不可写）"]);
        }

        return CommandResult.Ok();
    }

    /// <summary>保存当前设置（首次落盘用）。</summary>
    public bool Save(out string? error)
    {
        MergeInto(_preserved, Current);
        return JsonFileStore.TryWriteObjectAtomic(FilePath, _preserved, out error);
    }

    // ---------------------------------------------------------------- 即时生效

    private void ApplyToRuntime(ApplicationSettings settings)
    {
        // 承载参数：对**新建**队列/后续停止操作生效；既有队列保持创建时容量
        var carrierOptions = new CarrierOptions
        {
            QueueCapacity = settings.InputQueueCapacity,
            DropPolicy = DropPolicy.DropOldest,
            StopTimeout = TimeSpan.FromSeconds(settings.StopTimeoutSeconds),
        };

        _runtime.Carriers.UpdateOptions(carrierOptions);
        _runtime.ExternalAccessHost.UpdateOptions(carrierOptions);

        // 快速建网开关即时生效
        _runtime.UpdateAlignAutoNetwork(settings.AlignAutoNetworkEnabled);
    }

    // ---------------------------------------------------------------- 值域校验

    private static List<string> Validate(ApplicationSettings settings)
    {
        var failures = new List<string>();

        if (settings.InputQueueCapacity is < 1 or > 65536)
        {
            failures.Add($"输入队列容量必须在 1–65536 之间（当前 {settings.InputQueueCapacity}）");
        }

        if (settings.StopTimeoutSeconds is < 0.1 or > 300)
        {
            failures.Add($"停止超时必须介于 0.1–300 秒之间（当前 {settings.StopTimeoutSeconds}）");
        }

        if (settings.UiRefreshRateHz is < 1 or > 240)
        {
            failures.Add($"UI 刷新率必须在 1–240 Hz 之间（当前 {settings.UiRefreshRateHz}）");
        }

        if (settings.DiagnosticHintThreshold < 1)
        {
            failures.Add($"诊断提示阈值必须 ≥ 1（当前 {settings.DiagnosticHintThreshold}）");
        }

        // 吸附距离：2–8 px（越小越跟手，越大越容易吸上）
        if (settings.SnapDistance is < 2 or > 8)
        {
            failures.Add($"吸附距离必须在 2–8 px 之间（当前 {settings.SnapDistance}）");
        }

        return failures;
    }

    // ---------------------------------------------------------------- JSON 映射（容错 + 片段保留）

    private static ApplicationSettings FromJson(JsonObject root)
    {
        var settings = new ApplicationSettings();

        settings.SchemaVersion = ReadInt(root, "schemaVersion", settings.SchemaVersion);
        settings.StopTimeoutSeconds = ReadDouble(root, "stopTimeoutSeconds", settings.StopTimeoutSeconds);
        settings.InputQueueCapacity = ReadInt(root, "inputQueueCapacity", settings.InputQueueCapacity);
        settings.UiRefreshRateHz = ReadInt(root, "uiRefreshRateHz", settings.UiRefreshRateHz);
        settings.DiagnosticHintThreshold = ReadInt(root, "diagnosticHintThreshold", settings.DiagnosticHintThreshold);
        // 对齐自动建网：新键优先、旧键（加入即连）兼容读取
        settings.AlignAutoNetworkEnabled = ReadBool(
            root,
            "alignAutoNetwork",
            ReadBool(root, "autoNetworkEnabled", settings.AlignAutoNetworkEnabled));
        // 新键优先；旧键（showPortMarkers → 更旧 showLinkLines）兼容读取：
        // 语义经两次变更（连线层 → 口上标记 → **叠加信息层**），默认值都是"显示"。
        settings.ShowInfoOverlay = ReadBool(
            root,
            "showInfoOverlay",
            ReadBool(root, "showPortMarkers", ReadBool(root, "showLinkLines", settings.ShowInfoOverlay)));

        // 新键（有序根列表）优先；旧键 moduleSourceDirectory（单值）作为首项兼容读取
        var roots = ReadStringArray(root, "moduleSourceDirectories");
        if (roots.Count == 0 && ReadString(root, "moduleSourceDirectory") is { Length: > 0 } legacyRoot)
        {
            roots.Add(legacyRoot);
        }

        settings.ModuleSourceDirectories = roots;
        settings.LogLevel = ReadString(root, "logLevel") ?? settings.LogLevel;
        settings.Hotkeys = ReadStringMap(root, "hotkeys");

        // 数据结构 → 颜色：键 = 类型身份，值 = `#RRGGBB`（缺失 = 全部走自动配色）
        settings.TypeColors = ReadStringMap(root, "typeColors");

        // **外观块**（主题选择 + 按主题分层的色板覆盖 + 字体轴）：新形状优先，旧扁平键读时迁移
        settings.Appearance = ReadAppearance(root);

        // 多行输入提交键：非法值回退默认（`Ui` 侧按三档字符串解释，不做校验、不提示）
        settings.MultilineSubmitKey = ReadString(root, "multilineSubmitKey") ?? settings.MultilineSubmitKey;
        settings.ShellAspect = ReadString(root, "shellAspect") ?? settings.ShellAspect;
        settings.ShellFixedSize = ReadBool(root, "shellFixedSize", settings.ShellFixedSize);

        // 交互：吸附距离（缺失即默认 4；非法值由值域校验拦下并回退默认）
        settings.SnapDistance = ReadDouble(root, "snapDistance", settings.SnapDistance);

        return settings;
    }

    private static void MergeInto(JsonObject root, ApplicationSettings settings)
    {
        // 只更新**已知字段**；未识别字段原样保留（文件的其余内容不动）
        root["schemaVersion"] = settings.SchemaVersion;
        root["stopTimeoutSeconds"] = settings.StopTimeoutSeconds;
        root["inputQueueCapacity"] = settings.InputQueueCapacity;
        root["uiRefreshRateHz"] = settings.UiRefreshRateHz;
        root["diagnosticHintThreshold"] = settings.DiagnosticHintThreshold;
        root["alignAutoNetwork"] = settings.AlignAutoNetworkEnabled;
        _ = root.Remove("autoNetworkEnabled");   // 旧键已迁移（语义由"加入即连"改为"对齐建网"）
        root["showInfoOverlay"] = settings.ShowInfoOverlay;

        // **外观块**（`appearance { theme, paletteOverrides{主题:{token:hex}}, fonts{ui,data} }`）：
        // 写新形状，并把**全部旧外观键**删掉（含本轮折叠进来的那五个颜色字段）——
        // 留着会出现"两个来源"，下次读到时又得判谁优先。
        root["appearance"] = WriteAppearance(settings.Appearance);

        foreach (var legacy in LegacyAppearanceKeys)
        {
            _ = root.Remove(legacy);
        }

        root["multilineSubmitKey"] = settings.MultilineSubmitKey;
        root["shellAspect"] = settings.ShellAspect;
        root["shellFixedSize"] = settings.ShellFixedSize;
        root["snapDistance"] = settings.SnapDistance;

        var roots = new JsonArray();
        foreach (var directory in settings.ModuleSourceDirectories)
        {
            if (!string.IsNullOrWhiteSpace(directory))
            {
                roots.Add(directory);
            }
        }

        root["moduleSourceDirectories"] = roots;
        _ = root.Remove("moduleSourceDirectory");   // 旧键已迁移到列表，不再写出

        root["logLevel"] = settings.LogLevel;

        var hotkeys = new JsonObject();
        foreach (var (action, binding) in settings.Hotkeys)
        {
            hotkeys[action] = binding;
        }

        root["hotkeys"] = hotkeys;

        // **数据结构 → 颜色**的用户映射：只写用户真正覆盖过的条目（缺省 = 自动配色）
        var typeColors = new JsonObject();
        foreach (var (typeKey, color) in settings.TypeColors)
        {
            if (!string.IsNullOrWhiteSpace(typeKey) && !string.IsNullOrWhiteSpace(color))
            {
                typeColors[typeKey] = color;
            }
        }

        root["typeColors"] = typeColors;
    }

    /// <summary>
    /// 读**外观块**：新形状（`appearance` 对象）优先，旧扁平键**读时迁移**。
    /// </summary>
    /// <param name="root">设置文件根对象。</param>
    /// <remarks>
    /// <list type="bullet">
    ///   <item>新形状：`appearance { theme, paletteOverrides { 主题: { token: hex } }, fonts { ui, data } }`；</item>
    ///   <item>旧扁平键：`theme` / `paletteOverrides`（键形如 <c>Light/AccentPrimary</c>）/ `uiFontFamily` / `dataFontFamily`
    ///     —— 只有在新形状**没给**该值时生效（新形状赢）；</item>
    ///   <item>主题名一律过 <see cref="ThemeIds.Normalize"/> ⇒ 旧值 `Light` / `Dark` 与手写大小写都落到规范名，
    ///     **不需要一次性迁移脚本**。</item>
    /// </list>
    /// </remarks>
    private static AppearanceSettings ReadAppearance(JsonObject root)
    {
        var appearance = new AppearanceSettings();

        var block = root.TryGetPropertyValue("appearance", out var node) && node is JsonObject nested ? nested : null;

        // 主题选择
        var selection = block is not null ? ReadString(block, "theme") : null;
        appearance.ThemeSelection = ThemeIds.Normalize(selection ?? ReadString(root, "theme") ?? appearance.ThemeSelection);

        // 字体轴
        var fonts = block is not null
            && block.TryGetPropertyValue("fonts", out var fontsNode)
            && fontsNode is JsonObject fontsObject
                ? fontsObject
                : null;

        appearance.UiFontFamily = (fonts is not null ? ReadString(fonts, "ui") : null)
            ?? ReadString(root, "uiFontFamily")
            ?? appearance.UiFontFamily;

        appearance.DataFontFamily = (fonts is not null ? ReadString(fonts, "data") : null)
            ?? ReadString(root, "dataFontFamily")
            ?? appearance.DataFontFamily;

        // 色板覆盖：新形状（按主题嵌套）
        if (block is not null
            && block.TryGetPropertyValue("paletteOverrides", out var paletteNode)
            && paletteNode is JsonObject palette)
        {
            foreach (var (themeId, entries) in palette)
            {
                if (entries is not JsonObject map)
                {
                    continue;
                }

                foreach (var (token, value) in map)
                {
                    appearance.SetOverride(themeId, token, value?.ToString());
                }
            }
        }

        // 色板覆盖：旧形状（键 `主题/token` 的扁平表）—— 新形状已给的那一支不覆盖
        foreach (var (key, hex) in ReadStringMap(root, "paletteOverrides"))
        {
            if (LegacyPaletteOverrideKeys.TryParse(key, out var themeId, out var token)
                && appearance.Override(themeId, token) is null)
            {
                appearance.SetOverride(themeId, token, hex);
            }
        }

        // 本轮删掉的五个"专项"字段（主题色 / 背景档位 / 方向色）：读时折叠进覆盖层
        FoldLegacyColorFields(root, appearance);

        return appearance;
    }

    /// <summary>
    /// **旧外观字段 → 覆盖层**（读时迁移；对应 token 已有显式覆盖时不覆盖它）。
    /// </summary>
    /// <param name="root">设置文件根对象。</param>
    /// <param name="appearance">目标外观块。</param>
    /// <remarks>
    /// <list type="bullet">
    ///   <item><c>accentColor</c>（一条值）⇒ 两套主题的 <c>AccentPrimary</c>（旧语义就是"两套同值"）；</item>
    ///   <item><c>backgroundMode</c> = <c>LightGray</c> / <c>DarkGray</c> ⇒ 三支底色 × 两套（值取自
    ///     <see cref="ThemePresets"/>）；<c>Custom</c> + <c>backgroundColor</c> ⇒ 以它推三支；<c>Theme</c> ⇒ 不写；</item>
    ///   <item><c>directionSendColor</c> / <c>directionReceiveColor</c>：**空 = 跟随**（不写）；有值 ⇒ 两套都写
    ///     （旧语义"填了就两套都用它"）。</item>
    /// </list>
    /// <para>
    /// 折叠只发生在**读**这一侧，且**不写回旧键**：第一次「应用」之后文件里只剩新形状（见 <see cref="WriteAppearance"/>）。
    /// </para>
    /// </remarks>
    private static void FoldLegacyColorFields(JsonObject root, AppearanceSettings appearance)
    {
        static void SetIfAbsent(AppearanceSettings target, string theme, string token, string hex)
        {
            if (target.Override(theme, token) is null)
            {
                target.SetOverride(theme, token, hex);
            }
        }

        // ① 主题色
        if (ReadString(root, "accentColor") is { Length: > 0 } accent)
        {
            foreach (var theme in ThemeIds.BuiltIn)
            {
                SetIfAbsent(appearance, theme, "AccentPrimary", accent);
            }
        }

        // ② 背景档位（浅灰 / 深灰 / 自定义）
        var mode = ReadString(root, "backgroundMode");
        var custom = ReadString(root, "backgroundColor");

        var background = ThemePresets.FindBackground(mode) is { } preset
            ? (preset.Workspace, preset.Module, preset.Chrome)
            : string.Equals(mode, "Custom", StringComparison.OrdinalIgnoreCase) && !string.IsNullOrWhiteSpace(custom)
                ? ThemePresets.CustomBackground(custom)
                : ((string, string, string)?)null;

        if (background is { } palette)
        {
            foreach (var theme in ThemeIds.BuiltIn)
            {
                SetIfAbsent(appearance, theme, "BgWorkspace", palette.Item1);
                SetIfAbsent(appearance, theme, "BgModule", palette.Item2);
                SetIfAbsent(appearance, theme, "BgChrome", palette.Item3);
            }
        }

        // ③ 方向语义色（空 = 跟随主题内容 ⇒ 不写）
        foreach (var (key, token) in new[]
        {
            ("directionSendColor", "DirectionSend"),
            ("directionReceiveColor", "DirectionReceive"),
        })
        {
            if (ReadString(root, key) is { Length: > 0 } hex)
            {
                foreach (var theme in ThemeIds.BuiltIn)
                {
                    SetIfAbsent(appearance, theme, token, hex);
                }
            }
        }
    }

    /// <summary>
    /// **旧外观键**（写侧一律删掉）：主题 / 色板覆盖（扁平表）/ 字体两项 /
    /// 以及本轮折叠进覆盖层的那五个专项字段。
    /// </summary>
    private static readonly string[] LegacyAppearanceKeys =
    [
        "theme",
        "paletteOverrides",
        "uiFontFamily",
        "dataFontFamily",
        "accentColor",
        "backgroundMode",
        "backgroundColor",
        "directionSendColor",
        "directionReceiveColor",
    ];

    /// <summary>写**外观块**（新形状；只写用户真正改过的覆盖项 ⇒ 跟随不落盘、不产生死数据）。</summary>
    /// <param name="appearance">外观块。</param>
    private static JsonObject WriteAppearance(AppearanceSettings appearance)
    {
        var palette = new JsonObject();

        foreach (var theme in ThemeIds.BuiltIn)
        {
            var entries = new JsonObject();

            foreach (var (token, hex) in appearance.OverridesOf(theme))
            {
                if (!string.IsNullOrWhiteSpace(token) && !string.IsNullOrWhiteSpace(hex))
                {
                    entries[token] = hex;
                }
            }

            if (entries.Count > 0)
            {
                palette[theme] = entries;
            }
        }

        return new JsonObject
        {
            ["theme"] = appearance.ThemeSelection,
            ["paletteOverrides"] = palette,
            ["fonts"] = new JsonObject
            {
                ["ui"] = appearance.UiFontFamily,
                ["data"] = appearance.DataFontFamily,
            },
        };
    }

    /// <summary>读字符串映射（缺失/类型不符 = 空表）。</summary>
    private static Dictionary<string, string> ReadStringMap(JsonObject root, string name)
    {
        var result = new Dictionary<string, string>(StringComparer.Ordinal);

        if (root.TryGetPropertyValue(name, out var node) && node is JsonObject map)
        {
            foreach (var (key, value) in map)
            {
                result[key] = value?.ToString() ?? string.Empty;
            }
        }

        return result;
    }

    /// <summary>读字符串数组（缺失/类型不符 = 空列表）。</summary>
    private static List<string> ReadStringArray(JsonObject root, string name)
    {
        var result = new List<string>();

        if (root.TryGetPropertyValue(name, out var node) && node is JsonArray array)
        {
            foreach (var item in array)
            {
                if (item is not null && item.ToString() is { Length: > 0 } text && !string.IsNullOrWhiteSpace(text))
                {
                    result.Add(text);
                }
            }
        }

        return result;
    }

    private static int ReadInt(JsonObject root, string name, int fallback) =>
        root.TryGetPropertyValue(name, out var node) && node is not null && int.TryParse(node.ToString(), out var value)
            ? value
            : fallback;

    private static double ReadDouble(JsonObject root, string name, double fallback) =>
        root.TryGetPropertyValue(name, out var node) && node is not null && double.TryParse(node.ToString(), out var value)
            ? value
            : fallback;

    private static bool ReadBool(JsonObject root, string name, bool fallback) =>
        root.TryGetPropertyValue(name, out var node) && node is not null && bool.TryParse(node.ToString(), out var value)
            ? value
            : fallback;

    private static string? ReadString(JsonObject root, string name) =>
        root.TryGetPropertyValue(name, out var node) ? node?.ToString() : null;
}
