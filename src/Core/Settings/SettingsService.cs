using System.Globalization;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;
using HYWDebugAssistant.Core.Api;
using HYWDebugAssistant.Core.Seeding;
using HYWDebugAssistant.Core.Themes;
using HYWDebugAssistant.Core.Composition;
using HYWDebugAssistant.Core.Runtime;
using HYWDebugAssistant.DataPlane.Carriers;
using HYWDebugAssistant.DataPlane.Queues;
using HYWDebugAssistant.Hosting.Diagnostics;
using HYWDebugAssistant.Infrastructure.Json;
using HYWDebugAssistant.Infrastructure.Storage;
using HYWDebugAssistant.Sdk.Reporting;
using HYWDebugAssistant.Sdk.Text;

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

    /// <summary>文件来自更高版本 → 使用默认值 + Issue，并**禁止写回**（保护原文件）。</summary>
    FutureVersion = 3,
}

/// <summary>
/// 应用级设置的加载、校验、即时生效与持久化（主文档 §9.5）。
/// </summary>
/// <remarks>
/// <list type="bullet">
///   <item><b>载体</b>：设置根（`StoragePaths.SettingsFile`）的单个 JSON 文件（便携式）；**与组合件完全分离**；</item>
///   <item><b>未知字段保留</b>：以 <see cref="JsonObject"/> 为底稿，只更新已知字段 → 未识别字段原样保留（AC-4 同策略）；</item>
///   <item><b>原子写</b>：写失败 → **内存态保留**（已生效）+ 提示未持久化；</item>
///   <item><b>未来版本</b>：文件版本更高 ⇒ 默认值 + Warning + **禁止写回**（保护原文件）；</item>
///   <item><b>低版本（v3 及更低）</b>：**不做内联主题迁移** —— 只读已知标量键（主题选择 / 跟随系统方案 / 字体），
///     外观主题回落内置，记一条 Warning，且**载入时不写回**（原文件保持不变）；</item>
///   <item><b>主题</b>：主题是设置内部的一个子项，住 `<c>&lt;settings&gt;\appearance\themes\&lt;id&gt;\theme.json</c>`；
///     设置文件只记"选了哪套 / 跟随系统各用哪套 / 投放过什么"（本类持有 <see cref="Themes"/>，UI 经查询面读）；</item>
///   <item><b>出厂种子投放</b>：`resources\seeds\` 与程序目录同构 —— 单文件（设置文件）缺失才复制，
///     叶子目录（主题）缺失才整目录投放并记进 `appearance.seeded`；失败 ⇒ 一条 Warning、不记记录、下次重试；</item>
///   <item><b>多实例</b>：直接覆写（最后写入获胜）——不做冲突检测。</item>
/// </list>
/// </remarks>
public sealed class SettingsService
{
    private readonly ModuleRuntime _runtime;
    private readonly IIssueSink _issues;
    private readonly SeedService _seeds;

    private JsonObject _preserved = new();

    /// <summary>文件版本高于当前支持版本时置位：**禁止任何写回**（保护原文件）。</summary>
    private bool _writesBlocked;

    /// <summary>创建设置服务。</summary>
    /// <param name="runtime">运行时（设置中的参数即时应用到它）。</param>
    /// <param name="settingsFilePath">设置文件路径（生产 = `StoragePaths.SettingsFile`）。</param>
    /// <param name="issues">框架侧上报口（缺省用运行时自带的 Issue 中心）。</param>
    /// <param name="seedsRoot">出厂种子根（缺省 = `StoragePaths.SeedsRoot`；不存在 = 没有出厂种子）。</param>
    /// <param name="themesRoot">主题根（缺省 = 设置文件同级的 `appearance\themes`；生产即 `StoragePaths.ThemesRoot`）。</param>
    /// <param name="seedTargetRoot">种子投放目标根（缺省 = `StoragePaths.AppRoot`，即程序目录）。</param>
    public SettingsService(
        ModuleRuntime runtime,
        string settingsFilePath,
        IIssueSink? issues = null,
        string? seedsRoot = null,
        string? themesRoot = null,
        string? seedTargetRoot = null)
    {
        ArgumentNullException.ThrowIfNull(runtime);
        ArgumentException.ThrowIfNullOrWhiteSpace(settingsFilePath);

        _runtime = runtime;
        _issues = issues ?? runtime.Issues;
        FilePath = Path.GetFullPath(settingsFilePath);

        var settingsDirectory = Path.GetDirectoryName(FilePath)!;

        SeedTargetRoot = Path.GetFullPath(seedTargetRoot ?? StoragePaths.AppRoot);
        SeedsRoot = Path.GetFullPath(seedsRoot ?? StoragePaths.SeedsRoot);
        _seeds = new SeedService(SeedTargetRoot, SeedsRoot, _issues, ValidateSeedUnit);

        Themes = new ThemeStore(
            Path.GetFullPath(
                themesRoot
                ?? Path.Combine(settingsDirectory, StoragePaths.AppearanceDirectoryName, StoragePaths.ThemesDirectoryName)),
            _issues);
    }

    /// <summary>设置文件路径。</summary>
    public string FilePath { get; }

    /// <summary>出厂种子根（只读；不存在 = 没有出厂种子）。</summary>
    public string SeedsRoot { get; }

    /// <summary>出厂种子的投放目标根（生产 = 程序目录）。</summary>
    public string SeedTargetRoot { get; }

    /// <summary>主题文件夹存储（`&lt;settings&gt;\appearance\themes\&lt;id&gt;\theme.json`）。</summary>
    public ThemeStore Themes { get; }

    /// <summary>当前生效的设置。</summary>
    public ApplicationSettings Current { get; private set; } = new();

    /// <summary>最近一次 <see cref="Update"/> 是否成功写入文件（内存生效但写盘失败 ⇒ false）。</summary>
    public bool LastWriteSucceeded { get; private set; } = true;

    /// <summary>
    /// 加载设置（启动时调用一次）。失败不阻断启动。
    /// </summary>
    /// <remarks>
    /// 顺序（阶段 C 契约）：① 投放**单文件**种子（`settings/settings.json` 不存在才复制）→
    /// ② 读设置（没有则代码默认值）→ ③ 投放**主题单元**并记 `seeded` → ④ 有变化则持久化 → ⑤ 应用到运行时。
    /// <para>
    /// **损坏 / 更高版本的文件不投放**：这两种情况下没有一份可信的设置底稿，投放记录无处安全合并
    /// （写回会破坏用户原文件）⇒ 保持原文件不动、只用默认值运行。
    /// </para>
    /// </remarks>
    public SettingsLoadOutcome Load()
    {
        // ① 单文件种子：必须在读设置之前（出厂设置文件就是靠这一步出现的）
        _ = _seeds.SeedFiles();

        // ② **首运行由文件存在性判定，不看 JsonFileStore 的错误文案**：
        // "文件不存在"只是诊断内容，不能参与控制流（否则 L0 的文案一改，首运行就变成"读坏了"）。
        if (!File.Exists(FilePath))
        {
            Current = new ApplicationSettings();
            _preserved = new JsonObject();
            _writesBlocked = false;

            // ③ 主题单元照投（出厂主题不该因为缺设置文件而缺席）
            if (ApplySeedUnits())
            {
                PersistAfterSeed();
            }

            ApplyToRuntime(Current);
            return SettingsLoadOutcome.DefaultsCreated;
        }

        if (!JsonFileStore.TryReadObject(FilePath, out var root, out var readError))
        {
            Current = new ApplicationSettings();
            _preserved = new JsonObject();
            _writesBlocked = false;

            _issues.Report("settings", new IssueReport(
                IssueCategory.ConfigurationError,
                IssueSeverity.Warning,
                CoreText.Get("core.settings.readFailed", readError, FilePath)));

            ApplyToRuntime(Current);
            return SettingsLoadOutcome.DefaultsAfterFailure;
        }

        var version = ReadInt(root!, "schemaVersion", ApplicationSettings.CurrentSchemaVersion);
        if (version > ApplicationSettings.CurrentSchemaVersion)
        {
            // **未来版本**：用默认值跑，但绝不回写（否则会用旧结构覆盖新文件）。
            _writesBlocked = true;
            Current = new ApplicationSettings();
            _preserved = new JsonObject();
            ApplyToRuntime(Current);

            _issues.Report("settings", new IssueReport(
                IssueCategory.ConfigurationError,
                IssueSeverity.Warning,
                CoreText.Get(
                    "core.settings.futureVersion",
                    version,
                    ApplicationSettings.CurrentSchemaVersion,
                    FilePath)));

            return SettingsLoadOutcome.FutureVersion;
        }

        _writesBlocked = false;
        _preserved = root!;
        Current = FromJson(root!);

        if (version is 4 or 5)
        {
            // **v4 / v5 → v6**：字体轴折叠（v4 的回退列表）＋ 数据面参数收进 `flow` 块（v5 的三个根键）。
            // 两者都是兼容扩展（读旧写新）：载入即写回新形状；v3 及更低版本仍走下面的"不迁移"分支。
            _ = NormalizeAppearance(Current.Appearance);
            ApplyToRuntime(Current);
            PersistAfterSeed();
            return SettingsLoadOutcome.Loaded;
        }

        if (version < 4)
        {
            // **v3 及更低版本**：旧文件把主题方案与色板覆盖内联在 `appearance` 里，
            // 而新形状把主题收回"一个主题一个文件夹"。**不做内联迁移**（那会把用户数据悄悄改写成另一种形状）：
            // 只读已知标量键（theme / systemLightScheme / systemDarkScheme / fonts），
            // 外观主题回落内置；**载入时不写回**（原文件保持原样，保护用户数据）。
            _ = NormalizeAppearance(Current.Appearance);
            ApplyToRuntime(Current);

            _issues.Report("settings", new IssueReport(
                IssueCategory.ConfigurationError,
                IssueSeverity.Warning,
                CoreText.Get(
                    "core.settings.oldVersion",
                    version,
                    ApplicationSettings.CurrentSchemaVersion,
                    FilePath)));

            return SettingsLoadOutcome.Loaded;
        }

        // ③ 主题单元投放 —— 必须在归一化**之前**：否则"选了刚投放的出厂主题"会被判成不存在而回落内置。
        var seeded = ApplySeedUnits();
        var normalized = NormalizeAppearance(Current.Appearance);
        if (seeded || normalized)
        {
            PersistAfterSeed();   // ④
        }

        ApplyToRuntime(Current);   // ⑤
        return SettingsLoadOutcome.Loaded;
    }

    /// <summary>
    /// ③ 投放叶子目录单元，把**新投放**的相对路径记进 `appearance.seeded`。
    /// </summary>
    /// <returns>是否改动了外观块（调用方据此决定是否落盘）。</returns>
    private bool ApplySeedUnits()
    {
        var changed = false;

        foreach (var unit in _seeds.SeedUnits(SeededUnitKeys(Current.Appearance)))
        {
            AddSeeded(Current.Appearance, unit);
            changed = true;
        }

        return changed;
    }

    /// <summary>
    /// 已有 `seeded` 记录，按投放器的**相对程序目录**口径归一（兼容 B 阶段写入的"相对设置根"旧值）。
    /// </summary>
    /// <param name="appearance">外观块。</param>
    /// <remarks>B 阶段记的是 `appearance/themes/&lt;id&gt;`；现在投放单元是 `settings/appearance/themes/&lt;id&gt;` ⇒ 两种都认，老记录照样挡复活。</remarks>
    private static List<string> SeededUnitKeys(AppearanceSettings appearance)
    {
        var keys = new List<string>(appearance.Seeded.Count * 2);

        foreach (var entry in appearance.Seeded)
        {
            if (string.IsNullOrWhiteSpace(entry))
            {
                continue;
            }

            var value = entry.Trim().Replace('\\', '/').Trim('/');
            if (value.Length == 0)
            {
                continue;
            }

            keys.Add(value);

            if (!value.StartsWith(StoragePaths.SettingsDirectoryName + "/", StringComparison.OrdinalIgnoreCase))
            {
                keys.Add(StoragePaths.SettingsDirectoryName + "/" + value);
            }
        }

        return keys;
    }

    /// <summary>校验一个**刚投放**的单元：主题清单不可用 ⇒ 撤掉并重试；不认识的单元不做判断。</summary>
    /// <param name="relativeUnit">相对程序目录的单元路径。</param>
    private string? ValidateSeedUnit(string relativeUnit)
    {
        if (!TryGetThemeId(relativeUnit, out var themeId))
        {
            return null;
        }

        return Themes.Read(themeId) is null
            ? CoreText.Get("core.settings.themeUnavailable", ThemeStore.ManifestFileName)
            : null;
    }

    /// <summary>投放单元是不是一个主题文件夹（`settings/appearance/themes/&lt;id&gt;`）。</summary>
    /// <param name="relativeUnit">相对程序目录的单元路径。</param>
    /// <param name="themeId">主题 id（成功时）。</param>
    private static bool TryGetThemeId(string relativeUnit, out string themeId)
    {
        themeId = string.Empty;

        var prefix = StoragePaths.SettingsDirectoryName + "/"
            + StoragePaths.AppearanceDirectoryName + "/"
            + StoragePaths.ThemesDirectoryName + "/";

        if (!relativeUnit.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var tail = relativeUnit[prefix.Length..];
        if (tail.Length == 0 || tail.Contains('/'))
        {
            return false;
        }

        themeId = tail;
        return true;
    }

    /// <summary>种子投放 / 外观归一化之后落盘；失败不阻断启动，但记 Error。</summary>
    private void PersistAfterSeed()
    {
        Current.SchemaVersion = ApplicationSettings.CurrentSchemaVersion;
        MergeInto(_preserved, Current);

        if (!JsonFileStore.TryWriteObjectAtomic(FilePath, _preserved, out var writeError))
        {
            LastWriteSucceeded = false;
            _issues.Report("settings", new IssueReport(
                IssueCategory.ConfigurationError,
                IssueSeverity.Error,
                CoreText.Get("core.settings.seedWriteFailed", writeError, FilePath)));
        }
    }

    /// <summary>更新设置：校验 → 即时生效 → 原子写盘。</summary>
    public CommandResult Update(ApplicationSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        LastWriteSucceeded = true;

        if (_writesBlocked)
        {
            return CommandResult.Fail([CoreText.Get("core.settings.writeBlockedUpdate")]);
        }

        var failures = Validate(settings);
        if (failures.Count > 0)
        {
            return CommandResult.Fail(failures);
        }

        Current = settings.Clone();
        Current.SchemaVersion = ApplicationSettings.CurrentSchemaVersion;
        Current.UiLanguage = NormalizeUiLanguage(Current.UiLanguage);
        ApplyToRuntime(Current);

        MergeInto(_preserved, Current);

        if (!JsonFileStore.TryWriteObjectAtomic(FilePath, _preserved, out var writeError))
        {
            LastWriteSucceeded = false;
            _issues.Report("settings", new IssueReport(
                IssueCategory.ConfigurationError,
                IssueSeverity.Error,
                CoreText.Get("core.settings.writeFailed", writeError)));
            return CommandResult.Ok([CoreText.Get("core.settings.notPersisted")]);
        }

        return CommandResult.Ok();
    }

    /// <summary>
    /// **严格持久化设置**：写盘失败即回滚内存态并返回失败；用于“下次不再提醒”等
    /// 必须确认已落盘的机制动作。
    /// </summary>
    public CommandResult UpdateStrict(ApplicationSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);

        var previous = Current.Clone();
        var preserved = (JsonObject)_preserved.DeepClone();
        var result = Update(settings);
        if (!result.Succeeded)
        {
            return result;
        }

        if (LastWriteSucceeded)
        {
            return result;
        }

        Current = previous;
        _preserved = preserved;
        ApplyToRuntime(Current);
        return CommandResult.Fail([CoreText.Get("core.settings.strictRollback")]);
    }

    /// <summary>保存当前设置（首次落盘用）。</summary>
    public bool Save(out string? error)
    {
        if (_writesBlocked)
        {
            error = CoreText.Get("core.settings.writeBlockedSave");
            return false;
        }

        MergeInto(_preserved, Current);
        return JsonFileStore.TryWriteObjectAtomic(FilePath, _preserved, out error);
    }

    // ---------------------------------------------------------------- 默认设置发射（发布打包 / 出厂种子）

    /// <summary>写设置 JSON 的序列化选项（与 `JsonFileStore` 同一口径：缩进 + 中文不转义）。</summary>
    private static readonly JsonSerializerOptions ExportOptions = new()
    {
        WriteIndented = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    /// <summary>
    /// **把一份设置序列化成设置文件的 JSON 文本**（与落盘走同一份映射；不写文件）。
    /// </summary>
    /// <param name="settings">设置对象（内部会复制，不会改动它）。</param>
    public static string Serialize(ApplicationSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        return BuildDocument(settings).ToJsonString(ExportOptions);
    }

    /// <summary>
    /// **写一份独立的默认设置文件**（发布时生成 `resources/seeds/settings/settings.json` 用；原子写）。
    /// </summary>
    /// <param name="filePath">目标文件（目录不存在时自动创建）。</param>
    /// <param name="settings">设置对象（内部会复制，不会改动它）。</param>
    /// <param name="error">失败原因（成功时 <see langword="null"/>）。</param>
    /// <returns>是否写入成功。</returns>
    public static bool TryWriteDefaults(string filePath, ApplicationSettings settings, out string? error)
    {
        ArgumentNullException.ThrowIfNull(settings);
        return JsonFileStore.TryWriteObjectAtomic(filePath, BuildDocument(settings), out error);
    }

    /// <summary>一份**新**设置文件的完整 JSON 文档（只含已知字段：这是新文件，不存在要保留的未知字段）。</summary>
    /// <param name="settings">设置对象。</param>
    private static JsonObject BuildDocument(ApplicationSettings settings)
    {
        var root = new JsonObject();
        var clone = settings.Clone();
        clone.SchemaVersion = ApplicationSettings.CurrentSchemaVersion;
        MergeInto(root, clone);
        return root;
    }

    // ---------------------------------------------------------------- 即时生效

    private void ApplyToRuntime(ApplicationSettings settings)
    {
        // 界面语言：只切 UI 文化（CurrentCulture 不动）；界面文本在载入时解析一次 ⇒ 重启完全生效
        TextCatalog.Apply(settings.UiLanguage);

        // 承载参数：容量 / 满时策略**对新建队列生效**（既有队列保持自己的容量）；
        // 其余各项由两个宿主**逐承载 / 逐会话**即时下发（见各自的 UpdateOptions）。
        var flow = settings.Flow;
        var carrierOptions = new CarrierOptions
        {
            QueueCapacity = flow.QueueCapacity,
            DropPolicy = ParseDropPolicy(flow.DropPolicy),
            StopTimeout = TimeSpan.FromSeconds(flow.StopTimeoutSeconds),
            HintDepth = flow.HintDepth,
            HintHold = TimeSpan.FromMilliseconds(flow.HintHoldMs),
            MaxBacklogAge = TimeSpan.FromMilliseconds(flow.MaxBacklogAgeMs),
            ExternalWriteTimeout = TimeSpan.FromMilliseconds(flow.ExternalWriteTimeoutMs),
            SendBacklogBytes = flow.SendBacklogBytes,
            LivenessInterval = TimeSpan.FromMilliseconds(flow.LivenessIntervalMs),
            AdapterBufferBytes = flow.AdapterBufferBytes,
            AccessCloseTimeout = TimeSpan.FromMilliseconds(flow.AccessCloseTimeoutMs),
            DeviceLostMisses = flow.DeviceLostMisses,
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

        var flow = settings.Flow;

        if (flow.QueueCapacity is < 1 or > 65536)
        {
            failures.Add(CoreText.Get("core.settings.flowQueueCapacity", flow.QueueCapacity));
        }

        if (!IsDropPolicy(flow.DropPolicy))
        {
            failures.Add(CoreText.Get("core.settings.flowDropPolicy", flow.DropPolicy));
        }

        if (flow.StopTimeoutSeconds is < 0.1 or > 300)
        {
            failures.Add(CoreText.Get("core.settings.stopTimeout", flow.StopTimeoutSeconds));
        }

        if (flow.HintDepth is < 1 or > 65536)
        {
            failures.Add(CoreText.Get("core.settings.flowHintDepth", flow.HintDepth));
        }

        if (flow.HintHoldMs is < 50 or > 10000)
        {
            failures.Add(CoreText.Get("core.settings.flowHintHold", flow.HintHoldMs));
        }

        if (flow.MaxBacklogAgeMs is < 0 or > 60000)
        {
            failures.Add(CoreText.Get("core.settings.flowMaxAge", flow.MaxBacklogAgeMs));
        }

        if (flow.ExternalWriteTimeoutMs is < 50 or > 60000)
        {
            failures.Add(CoreText.Get("core.settings.flowWriteTimeout", flow.ExternalWriteTimeoutMs));
        }

        if (flow.SendBacklogBytes is < 8192 or > 4194304)
        {
            failures.Add(CoreText.Get("core.settings.flowSendBacklog", flow.SendBacklogBytes));
        }

        if (flow.LivenessIntervalMs is < 50 or > 5000)
        {
            failures.Add(CoreText.Get("core.settings.flowLiveness", flow.LivenessIntervalMs));
        }

        if (flow.AdapterBufferBytes is < 4096 or > 1048576)
        {
            failures.Add(CoreText.Get("core.settings.flowAdapterBuffer", flow.AdapterBufferBytes));
        }

        if (flow.AccessCloseTimeoutMs is < 100 or > 10000)
        {
            failures.Add(CoreText.Get("core.settings.flowAccessClose", flow.AccessCloseTimeoutMs));
        }

        if (flow.DeviceLostMisses is < 1 or > 10)
        {
            failures.Add(CoreText.Get("core.settings.flowDeviceLost", flow.DeviceLostMisses));
        }

        if (settings.UiRefreshRateHz is < 1 or > 240)
        {
            failures.Add(CoreText.Get("core.settings.uiRefresh", settings.UiRefreshRateHz));
        }

        // 吸附距离：2–8 px（越小越跟手，越大越容易吸上）
        if (settings.SnapDistance is < 2 or > 8)
        {
            failures.Add(CoreText.Get("core.settings.snapDistance", settings.SnapDistance));
        }

        // 主题的遮罩 / 表面透明度属于**主题文件夹**（`theme.json`），由 `ThemeStore.Save` 校验并收敛范围。

        if (!IsExitBehavior(settings.WorkspaceExitBehavior))
        {
            failures.Add(CoreText.Get("core.settings.exitBehavior", settings.WorkspaceExitBehavior));
        }

        return failures;
    }

    // ---------------------------------------------------------------- JSON 映射（容错 + 片段保留）

    private static ApplicationSettings FromJson(JsonObject root)
    {
        var settings = new ApplicationSettings();

        settings.SchemaVersion = ReadInt(root, "schemaVersion", settings.SchemaVersion);
        settings.Flow = ReadFlow(root);
        settings.UiRefreshRateHz = ReadInt(root, "uiRefreshRateHz", settings.UiRefreshRateHz);
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

        // 模块来源根列表已移除：模块只扫描默认目录（modules + modules/external）。
        settings.LogLevel = ReadString(root, "logLevel") ?? settings.LogLevel;
        settings.Hotkeys = ReadStringMap(root, "hotkeys");

        // 数据结构 → 颜色：键 = 类型身份，值 = `#RRGGBB`（缺失 = 全部走自动配色）
        settings.TypeColors = ReadStringMap(root, "typeColors");

        // **外观块**（主题选择 + 跟随系统方案 + 已投放 + 字体轴）
        settings.Appearance = ReadAppearance(root);

        // 界面语言：未知 / 非法值 ⇒ 跟随系统（与外观类设置同一容错口径）
        settings.UiLanguage = NormalizeUiLanguage(ReadString(root, "uiLanguage"));

        // 多行输入提交键：非法值回退默认（`Ui` 侧按三档字符串解释，不做校验、不提示）
        settings.MultilineSubmitKey = ReadString(root, "multilineSubmitKey") ?? settings.MultilineSubmitKey;
        settings.ShellAspect = NormalizeShellAspect(ReadString(root, "shellAspect"));
        settings.ShellFixedSize = ReadBool(root, "shellFixedSize", settings.ShellFixedSize);
        settings.WorkspaceExitBehavior = NormalizeExitBehavior(ReadString(root, "workspaceExitBehavior"));

        // 交互：吸附距离（缺失即默认 4；非法值由值域校验拦下并回退默认）
        settings.SnapDistance = ReadDouble(root, "snapDistance", settings.SnapDistance);

        // 窗口：应用整体置顶（界面可即时切换；必须落盘，否则重启回 false）
        settings.AppTopmost = ReadBool(root, "appTopmost", settings.AppTopmost);

        return settings;
    }

    /// <summary>
    /// 读**数据面块**：`flow { queueCapacity, dropPolicy, stopTimeoutSeconds, hintDepth,
    /// hintHoldMs, maxBacklogAgeMs, externalWriteTimeoutMs }`。
    /// </summary>
    /// <param name="root">设置文件根对象。</param>
    /// <remarks>
    /// <list type="bullet">
    ///   <item>**没有 `flow` 块时读旧根键**（`inputQueueCapacity` / `stopTimeoutSeconds` /
    ///     `diagnosticHintThreshold`）：v5 及以下文件里那三个值是唯一的一份事实，折叠进来即可；</item>
    ///   <item>其余四项（`dropPolicy` / `hintHoldMs` / `maxBacklogAgeMs` / `externalWriteTimeoutMs`）
    ///     缺失即用默认值 —— 旧文件里没有这些事实，**不猜测**；</item>
    ///   <item>`dropPolicy` 过 <see cref="NormalizeDropPolicy"/>：未知值按默认丢旧。</item>
    /// </list>
    /// </remarks>
    private static FlowSettings ReadFlow(JsonObject root)
    {
        var defaults = new FlowSettings();
        var block = root.TryGetPropertyValue("flow", out var node) && node is JsonObject nested ? nested : null;

        if (block is null)
        {
            return defaults with
            {
                QueueCapacity = ReadInt(root, "inputQueueCapacity", defaults.QueueCapacity),
                StopTimeoutSeconds = ReadDouble(root, "stopTimeoutSeconds", defaults.StopTimeoutSeconds),
                HintDepth = ReadInt(root, "diagnosticHintThreshold", defaults.HintDepth),
            };
        }

        return defaults with
        {
            QueueCapacity = ReadInt(block, "queueCapacity", defaults.QueueCapacity),
            DropPolicy = NormalizeDropPolicy(ReadString(block, "dropPolicy")),
            StopTimeoutSeconds = ReadDouble(block, "stopTimeoutSeconds", defaults.StopTimeoutSeconds),
            HintDepth = ReadInt(block, "hintDepth", defaults.HintDepth),
            HintHoldMs = ReadInt(block, "hintHoldMs", defaults.HintHoldMs),
            MaxBacklogAgeMs = ReadInt(block, "maxBacklogAgeMs", defaults.MaxBacklogAgeMs),
            ExternalWriteTimeoutMs = ReadInt(block, "externalWriteTimeoutMs", defaults.ExternalWriteTimeoutMs),
            SendBacklogBytes = ReadInt(block, "sendBacklogBytes", defaults.SendBacklogBytes),
            LivenessIntervalMs = ReadInt(block, "livenessIntervalMs", defaults.LivenessIntervalMs),
            AdapterBufferBytes = ReadInt(block, "adapterBufferBytes", defaults.AdapterBufferBytes),
            AccessCloseTimeoutMs = ReadInt(block, "accessCloseTimeoutMs", defaults.AccessCloseTimeoutMs),
            DeviceLostMisses = ReadInt(block, "deviceLostMisses", defaults.DeviceLostMisses),
        };
    }

    /// <summary>写**数据面块**（`flow` 的十二个键；语义与值域见 <see cref="FlowSettings"/>）。</summary>
    /// <param name="flow">数据面参数。</param>
    private static JsonObject WriteFlow(FlowSettings flow) => new()
    {
        ["queueCapacity"] = flow.QueueCapacity,
        ["dropPolicy"] = NormalizeDropPolicy(flow.DropPolicy),
        ["stopTimeoutSeconds"] = flow.StopTimeoutSeconds,
        ["hintDepth"] = flow.HintDepth,
        ["hintHoldMs"] = flow.HintHoldMs,
        ["maxBacklogAgeMs"] = flow.MaxBacklogAgeMs,
        ["externalWriteTimeoutMs"] = flow.ExternalWriteTimeoutMs,
        ["sendBacklogBytes"] = flow.SendBacklogBytes,
        ["livenessIntervalMs"] = flow.LivenessIntervalMs,
        ["adapterBufferBytes"] = flow.AdapterBufferBytes,
        ["accessCloseTimeoutMs"] = flow.AccessCloseTimeoutMs,
        ["deviceLostMisses"] = flow.DeviceLostMisses,
    };

    private static void MergeInto(JsonObject root, ApplicationSettings settings)
    {
        // 只更新**已知字段**；未识别字段原样保留（文件的其余内容不动）
        root["schemaVersion"] = ApplicationSettings.CurrentSchemaVersion;

        // **数据面块**：写新形状，再把三个旧根键删掉 —— 留着就会出现"两个来源"，下次读到时又得判谁优先
        root["flow"] = WriteFlow(settings.Flow);
        _ = root.Remove("inputQueueCapacity");
        _ = root.Remove("stopTimeoutSeconds");
        _ = root.Remove("diagnosticHintThreshold");

        root["uiRefreshRateHz"] = settings.UiRefreshRateHz;
        root["alignAutoNetwork"] = settings.AlignAutoNetworkEnabled;
        _ = root.Remove("autoNetworkEnabled");   // 旧键已迁移（语义由"加入即连"改为"对齐建网"）
        root["showInfoOverlay"] = settings.ShowInfoOverlay;
        root["appTopmost"] = settings.AppTopmost;

        // **外观块**（`appearance { theme, systemLightScheme, systemDarkScheme, seeded[], fonts{ui,data} }`）：
        // 写新形状，并把**全部旧外观键**删掉 —— 留着会出现"两个来源"，下次读到时又得判谁优先。
        root["appearance"] = WriteAppearance(settings.Appearance);

        foreach (var legacy in LegacyAppearanceKeys)
        {
            _ = root.Remove(legacy);
        }

        // 界面语言（值域收敛：未知 / 非法 ⇒ 跟随系统）
        root["uiLanguage"] = NormalizeUiLanguage(settings.UiLanguage);

        root["multilineSubmitKey"] = settings.MultilineSubmitKey;        root["shellAspect"] = NormalizeShellAspect(settings.ShellAspect);
        root["shellFixedSize"] = settings.ShellFixedSize;
        root["workspaceExitBehavior"] = NormalizeExitBehavior(settings.WorkspaceExitBehavior);
        root["snapDistance"] = settings.SnapDistance;

        // 模块来源根已移除；清掉旧键，避免“两个来源”
        _ = root.Remove("moduleSourceDirectories");
        _ = root.Remove("moduleSourceDirectory");

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
    /// 读**外观块**（v4）：`appearance { theme, systemLightScheme, systemDarkScheme, seeded[], fonts{ui,data} }`。
    /// </summary>
    /// <param name="root">设置文件根对象。</param>
    /// <remarks>
    /// <list type="bullet">
    ///   <item>主题名一律过 <see cref="ThemeIds.Normalize"/> ⇒ 旧值 `Light` / `Dark` 与手写大小写都落到规范名；</item>
    ///   <item>**不读内联主题方案 / 色板覆盖**：v3 及更低版本由 `Load` 走"只读标量 + 不写回"的分支；</item>
    ///   <item>`seeded` 里的旧值（`seededThemes` 的裸 id）在读时折成 `appearance/themes/&lt;id&gt;` 形式；</item>
    ///   <item>v1 的顶层 `theme` / `uiFontFamily` / `dataFontFamily` 作为**兜底**读取（新形状优先）——
    ///     只是取值，不做任何覆盖迁移。</item>
    /// </list>
    /// </remarks>
    private static AppearanceSettings ReadAppearance(JsonObject root)
    {
        var appearance = new AppearanceSettings();

        var block = root.TryGetPropertyValue("appearance", out var node) && node is JsonObject nested ? nested : null;

        // 主题选择（缺失 ⇒ 跟随系统）
        var selection = block is not null ? ReadString(block, "theme") : null;
        appearance.ThemeSelection = ThemeIds.Normalize(selection ?? ReadString(root, "theme") ?? appearance.ThemeSelection);

        // 跟随系统：浅色 / 深色模式各自使用哪套主题（必须是同基类型；非法由 NormalizeAppearance 回落内置）
        appearance.SystemLightScheme =
            (block is not null ? ReadString(block, "systemLightScheme") : null) is { Length: > 0 } light
                ? ThemeIds.Normalize(light)
                : appearance.SystemLightScheme;

        appearance.SystemDarkScheme =
            (block is not null ? ReadString(block, "systemDarkScheme") : null) is { Length: > 0 } dark
                ? ThemeIds.Normalize(dark)
                : appearance.SystemDarkScheme;

        // 字体轴
        var fonts = block is not null
            && block.TryGetPropertyValue("fonts", out var fontsNode)
            && fontsNode is JsonObject fontsObject
                ? fontsObject
                : null;

        var defaults = new AppearanceSettings();
        appearance.UiFonts = ReadFontAxis(fonts, "ui", "uiFontFamily", root, defaults.UiFonts);
        appearance.DataFonts = UpgradeLegacyDataDefault(
            ReadFontAxis(fonts, "data", "dataFontFamily", root, defaults.DataFonts),
            defaults.DataFonts);

        // 已投放的出厂主题（防复活）
        ReadSeeded(block, appearance);

        return appearance;
    }

    /// <summary>读一条字体轴：新形状 `{ latin, cjk }` → 旧形状字符串 → v1 顶层键 → 默认值。</summary>
    /// <param name="fonts">`appearance.fonts`（可为 null）。</param>
    /// <param name="name">轴名（`ui` / `data`）。</param>
    /// <param name="legacyTopLevelName">v1 的顶层键名。</param>
    /// <param name="root">设置根对象（读 v1 顶层键用）。</param>
    /// <param name="fallback">没有用户值时用的默认轴。</param>
    private static FontFamilySetting ReadFontAxis(
        JsonObject? fonts,
        string name,
        string legacyTopLevelName,
        JsonObject root,
        FontFamilySetting fallback)
    {
        if (fonts is not null && fonts.TryGetPropertyValue(name, out var node))
        {
            if (node is JsonObject axis)
            {
                return NormalizeFontAxis(
                    new FontFamilySetting
                    {
                        Latin = ReadString(axis, "latin") ?? string.Empty,
                        Cjk = ReadString(axis, "cjk") ?? string.Empty,
                    },
                    fallback);
            }

            if (node is JsonValue value && value.TryGetValue<string>(out var legacy) && !string.IsNullOrWhiteSpace(legacy))
            {
                return NormalizeFontAxis(FontFamilySetting.FromLegacy(legacy), fallback);
            }
        }

        var topLevel = ReadString(root, legacyTopLevelName);
        return string.IsNullOrWhiteSpace(topLevel)
            ? fallback.Clone()
            : NormalizeFontAxis(FontFamilySetting.FromLegacy(topLevel), fallback);
    }

    /// <summary>空的一侧回落到默认值（读旧文件时可能只写了一侧）。</summary>
    private static FontFamilySetting NormalizeFontAxis(FontFamilySetting setting, FontFamilySetting defaults)
    {
        if (string.IsNullOrWhiteSpace(setting.Latin)) { setting.Latin = defaults.Latin; }
        if (string.IsNullOrWhiteSpace(setting.Cjk)) { setting.Cjk = defaults.Cjk; }
        return setting;
    }

    /// <summary>旧内置默认数据字体（Times New Roman / 宋体）升级为新的等宽默认；用户自定义值原样保留。</summary>
    private static FontFamilySetting UpgradeLegacyDataDefault(FontFamilySetting setting, FontFamilySetting defaults) =>
        string.Equals(setting.Latin, "Times New Roman", StringComparison.OrdinalIgnoreCase)
        // i18n-exempt: 识别旧默认值（字体族名，不是界面文案）
        && string.Equals(setting.Cjk, "宋体", StringComparison.Ordinal)
            ? defaults.Clone()
            : setting;

    /// <summary>读 `appearance.seeded`（并把 v3 的 `seededThemes` 值折成路径形式）。</summary>
    /// <param name="block">外观块（可为 null）。</param>
    /// <param name="appearance">目标外观块。</param>
    private static void ReadSeeded(JsonObject? block, AppearanceSettings appearance)
    {
        if (block is null)
        {
            return;
        }

        if (block.TryGetPropertyValue("seeded", out var node) && node is JsonArray seeded)
        {
            foreach (var item in seeded)
            {
                AddSeeded(appearance, item?.ToString());
            }
        }

        // **旧字段**（v3 的 `seededThemes`，值是裸 id）：读时折进 `seeded`（`appearance/themes/<id>` 形式）。
        if (block.TryGetPropertyValue("seededThemes", out var legacyNode) && legacyNode is JsonArray seededThemes)
        {
            foreach (var item in seededThemes)
            {
                AddSeeded(appearance, item?.ToString());
            }
        }
    }

    /// <summary>把一条 seeded 记录收敛成相对路径（裸 id ⇒ `appearance/themes/&lt;id&gt;`；完整路径原样保留）。</summary>
    /// <param name="appearance">目标外观块。</param>
    /// <param name="raw">文件里的值。</param>
    private static void AddSeeded(AppearanceSettings appearance, string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return;
        }

        var value = raw.Trim().Replace('\\', '/').Trim('/');

        if (!value.Contains('/', StringComparison.Ordinal))
        {
            var id = ThemeIds.Normalize(value);
            if (ThemeIds.IsSystem(id))
            {
                return;
            }

            value = "appearance/themes/" + id;
        }

        if (!appearance.Seeded.Contains(value, StringComparer.OrdinalIgnoreCase))
        {
            appearance.Seeded.Add(value);
        }
    }

    /// <summary>
    /// 把外观的选择收敛到**真实存在的主题**：选择 / 跟随系统方案缺失或基类型不匹配 ⇒ 回落内置。
    /// </summary>
    /// <param name="appearance">外观块（就地修改）。</param>
    /// <returns>是否改动了外观块（调用方据此决定是否落盘）。</returns>
    private bool NormalizeAppearance(AppearanceSettings appearance)
    {
        var changed = false;
        var themes = Themes.LoadAll();

        var selection = ThemeIds.Normalize(appearance.ThemeSelection);
        if (ThemeIds.IsSystem(selection))
        {
            if (!string.Equals(appearance.ThemeSelection, ThemeIds.System, StringComparison.Ordinal))
            {
                appearance.ThemeSelection = ThemeIds.System;
                changed = true;
            }
        }
        else if (BaseKindOf(themes, selection) is null)
        {
            appearance.ThemeSelection = ThemeIds.System;
            changed = true;
        }
        else if (!string.Equals(appearance.ThemeSelection, selection, StringComparison.Ordinal))
        {
            appearance.ThemeSelection = selection;
            changed = true;
        }

        var light = NormalizeSystemScheme(themes, ThemeBaseKind.Light, appearance.SystemLightScheme);
        if (!string.Equals(appearance.SystemLightScheme, light, StringComparison.Ordinal))
        {
            appearance.SystemLightScheme = light;
            changed = true;
        }

        var dark = NormalizeSystemScheme(themes, ThemeBaseKind.Dark, appearance.SystemDarkScheme);
        if (!string.Equals(appearance.SystemDarkScheme, dark, StringComparison.Ordinal))
        {
            appearance.SystemDarkScheme = dark;
            changed = true;
        }

        return changed;
    }

    /// <summary>把某一模式的方案 id 收敛到"同基类型的主题（内置或文件夹）或内置方案"。</summary>
    /// <param name="themes">磁盘主题（`Themes.LoadAll()` 的结果）。</param>
    /// <param name="baseKind">模式（浅 / 深）。</param>
    /// <param name="id">设置里的值。</param>
    private static string NormalizeSystemScheme(
        IReadOnlyList<ThemeFileView> themes,
        ThemeBaseKind baseKind,
        string? id)
    {
        var normalized = ThemeIds.Normalize(id);

        return BaseKindOf(themes, normalized) == baseKind
            ? normalized
            : baseKind == ThemeBaseKind.Dark ? ThemeIds.Dark : ThemeIds.Light;
    }

    /// <summary>某个主题 id 的基类型（内置或磁盘主题；不存在 ⇒ <see langword="null"/>）。</summary>
    /// <param name="themes">磁盘主题。</param>
    /// <param name="id">已规范化的主题 id。</param>
    private static ThemeBaseKind? BaseKindOf(IReadOnlyList<ThemeFileView> themes, string id)
    {
        if (ThemeIds.BuiltInKind(id) is { } builtIn)
        {
            return builtIn;
        }

        foreach (var theme in themes)
        {
            if (string.Equals(ThemeIds.Normalize(theme.Id), id, StringComparison.OrdinalIgnoreCase))
            {
                return theme.Base;
            }
        }

        return null;
    }

    /// <summary>
    /// **旧外观键**（写侧一律删掉）：主题 / 色板覆盖（扁平表）/ 字体两项 /
    /// 以及更早的五个专项颜色字段（主题色 / 背景档位 / 方向色）。
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

    /// <summary>写**外观块**（v4：只写选择 / 已投放 / 字体 —— 主题色板住在各自的主题文件夹里）。</summary>
    /// <param name="appearance">外观块。</param>
    private static JsonObject WriteAppearance(AppearanceSettings appearance)
    {
        var seeded = new JsonArray();

        foreach (var entry in appearance.Seeded)
        {
            if (!string.IsNullOrWhiteSpace(entry))
            {
                seeded.Add(entry.Trim());
            }
        }

        return new JsonObject
        {
            ["theme"] = ThemeIds.Normalize(appearance.ThemeSelection),
            ["systemLightScheme"] = NormalizeSchemeName(appearance.SystemLightScheme, ThemeIds.Light),
            ["systemDarkScheme"] = NormalizeSchemeName(appearance.SystemDarkScheme, ThemeIds.Dark),
            ["seeded"] = seeded,
            ["fonts"] = new JsonObject
            {
                ["ui"] = WriteFontAxis(appearance.UiFonts),
                ["data"] = WriteFontAxis(appearance.DataFonts),
            },
        };
    }

    /// <summary>写一条字体轴（v5 形状：`{ latin, cjk }`）。</summary>
    private static JsonObject WriteFontAxis(FontFamilySetting setting) => new()
    {
        ["latin"] = setting.Latin,
        ["cjk"] = setting.Cjk,
    };

    /// <summary>主题名规范化（空 ⇒ 回落到该模式的内置主题，避免写出 `System` 这种非法方案）。</summary>
    /// <param name="value">设置里的值。</param>
    /// <param name="fallback">该模式的默认主题。</param>
    private static string NormalizeSchemeName(string? value, string fallback) =>
        string.IsNullOrWhiteSpace(value) ? fallback : ThemeIds.Normalize(value);

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

    private static string NormalizeShellAspect(string? value) =>
        value is "9:19.5" or "9:16" ? value : "9:19.5";

    private static bool IsExitBehavior(string? value) =>
        value is "Ask" or "Save" or "Discard";

    private static string NormalizeExitBehavior(string? value) =>
        IsExitBehavior(value) ? value! : "Ask";

    /// <summary>满时策略的合法值（`DropOldest` / `DropNewest`）。</summary>
    /// <param name="value">设置里的值。</param>
    private static bool IsDropPolicy(string? value) => value is "DropOldest" or "DropNewest";

    /// <summary>满时策略的收敛：未知 / 空 ⇒ 丢最旧（与其它字符串设置同一容错口径：不提示）。</summary>
    /// <param name="value">设置里的值。</param>
    private static string NormalizeDropPolicy(string? value) =>
        value is "DropNewest" ? "DropNewest" : "DropOldest";

    /// <summary>满时策略 → 数据面的枚举（未知 ⇒ 丢最旧）。</summary>
    /// <param name="value">设置里的值。</param>
    private static DropPolicy ParseDropPolicy(string? value) =>
        value is "DropNewest" ? DropPolicy.DropNewest : DropPolicy.DropOldest;

    /// <summary>界面语言收敛：`system`（大小写不敏感）或合法文化名之外，一律回落 `system`。</summary>
    /// <param name="value">文件里或界面给出的值。</param>
    /// <remarks>
    /// 文化名取**规范形式**（<c>zh-hans</c> → <c>zh-Hans</c>），这样设置文件里的值与文本表文件名同形。
    /// 有表但当前包没带这张表的语言不在这里拦：由 `TextCatalog` 按回退链显示中文简体。
    /// </remarks>
    private static string NormalizeUiLanguage(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return TextLanguages.System;
        }

        var trimmed = value.Trim();
        if (string.Equals(trimmed, TextLanguages.System, StringComparison.OrdinalIgnoreCase))
        {
            return TextLanguages.System;
        }

        try
        {
            return CultureInfo.GetCultureInfo(trimmed).Name;
        }
        catch (CultureNotFoundException)
        {
            return TextLanguages.System;
        }
    }

    private static int ReadInt(JsonObject root, string name, int fallback) =>
        root.TryGetPropertyValue(name, out var node) && node is not null && int.TryParse(node.ToString(), out var value)
            ? value
            : fallback;

    // TODO（阶段 C 审核项）：与 `ThemeStore` 一样改用 `CultureInfo.InvariantCulture` ——
    // 非 `.` 小数分隔符的机器会把 `snapDistance: 4.5` 读成 45。既有代码，本阶段只登记不改（不阻塞）。
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
