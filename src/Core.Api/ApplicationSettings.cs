namespace HYWDebugAssistant.Core.Api;

/// <summary>
/// **应用级设置**（独立于组合件；便携式存放于应用目录的 <c>settings.json</c>）——主文档 §9.5 / ADR-0030。
/// </summary>
/// <remarks>
/// <list type="bullet">
///   <item>与 <b>Configuration</b> 的区别：Configuration 是**实例参数**（随组合件保存）；Settings 是**应用行为偏好**（跨组合件共享）；</item>
///   <item>范围：**仅应用级**——不提供"按模块类型的默认设置"（模块自身默认值由模块实现提供）；</item>
///   <item>生效：**在线即时生效**；异常（缺失/损坏）→ 用默认值 + Issue，**不阻断启动**；</item>
///   <item>清单可增长：新增设置项属**兼容扩展**；删除或改语义属破坏性变更（须 ADR）。</item>
/// </list>
/// </remarks>
public sealed class ApplicationSettings
{
    /// <summary>当前设置 Schema 版本（写入文件，用于迁移判断）。</summary>
    public const int CurrentSchemaVersion = 1;

    /// <summary>设置 Schema 版本。</summary>
    public int SchemaVersion { get; set; } = CurrentSchemaVersion;

    /// <summary>停止协议的有界等待时限（秒）。默认 5。</summary>
    public double StopTimeoutSeconds { get; set; } = 5;

    /// <summary>输入队列容量（单元数）。默认 1024；修改后对**新建**队列生效。</summary>
    public int InputQueueCapacity { get; set; } = 1024;

    /// <summary>UI 刷新率（Hz）。默认 60（UI 按此节拍从订阅缓冲抽样读取）。</summary>
    public int UiRefreshRateHz { get; set; } = 60;

    /// <summary>诊断提示阈值（队列深度持续达到该值即提示"缓冲持续为满"）。默认 8。</summary>
    public int DiagnosticHintThreshold { get; set; } = 8;

    /// <summary>
    /// **对齐自动建网**（默认开）——ADR-0037：**接触且对齐**的未打标签口自动连上。
    /// </summary>
    /// <remarks>
    /// 原语义为"快速建网 R0（加入模块即自动连线）"，因**反直觉**已移除（用户裁决）；
    /// JSON 键 <c>alignAutoNetwork</c>，旧键 <c>autoNetworkEnabled</c> 兼容读取。
    /// </remarks>
    public bool AlignAutoNetworkEnabled { get; set; } = true;

    /// <summary>
    /// 是否显示**叠加信息层**【ADR-0039 D5】（原 `showPortMarkers`）。
    /// </summary>
    /// <remarks>
    /// 语义变更原因：**端口标记改为常显**（它是模块的标准件，也是唯一建网入口），
    /// 开关改为控制**叠加信息层**（窗内连接线 + 流向 + 对端信息）。
    /// 旧键 `showPortMarkers` 与更旧的 `showLinkLines` 均**兼容读取**。
    /// </remarks>
    public bool ShowInfoOverlay { get; set; } = true;

    /// <summary>
    /// **主题**【M1-16 / ADR-0038 D4】：<c>Light</c>（默认）/ <c>Dark</c> / <c>System</c>（跟随系统）。
    /// </summary>
    public string Theme { get; set; } = "Light";

    /// <summary>**主题色**（十六进制，如 <c>#2563EB</c>）；非法或为空则用内置主题色。</summary>
    public string AccentColor { get; set; } = "#2563EB";

    /// <summary>
    /// **背景档位**：<c>Theme</c>（默认，跟随主题）/ <c>LightGray</c> / <c>DarkGray</c> / <c>Custom</c>。
    /// </summary>
    public string BackgroundMode { get; set; } = "Theme";

    /// <summary>**自定义背景色**（十六进制；仅 <see cref="BackgroundMode"/> = <c>Custom</c> 时生效）。</summary>
    public string BackgroundColor { get; set; } = "#F3F4F6";

    /// <summary>
    /// **外壳窗口比例**【M1-16 / ADR-0038 D2】：<c>9:19.5</c>（默认，手机竖屏常用比例）/ <c>9:16</c> / <c>16:9</c>。
    /// </summary>
    public string ShellAspect { get; set; } = "9:19.5";

    /// <summary>
    /// **外壳尺寸是否固定**【ADR-0038 D2】：默认 <c>true</c>（不可缩放，尺寸由比例推导）；
    /// <c>false</c> = 可自由缩放（比例仅作初值）。
    /// </summary>
    public bool ShellFixedSize { get; set; } = true;

    /// <summary>
    /// **模块来源根列表**（**有序**；ADR-0036 D1）——启动与「立即重扫」按顺序逐根扫描：
    /// 每根只扫**直接子目录**中含模块清单文件者；先到者胜（同 ID 同版本重复只记 Info）。
    /// </summary>
    /// <remarks>
    /// 空列表 = 只用**内置包目录**（应用目录下的 <c>modules\</c>，由构建产出）。
    /// 内置目录始终**排在首位**（由组合根拼接），不可由用户移除。
    /// </remarks>
    public IList<string> ModuleSourceDirectories { get; set; } = [];

    /// <summary>日志级别（Trace/Debug/Information/Warning/Error）。默认 Information。</summary>
    public string LogLevel { get; set; } = "Information";

    /// <summary>
    /// **应用整体置顶**（ADR-0036 D5）：为 true 时本应用**全部**窗口（外壳 + 模块窗口 + 弹窗，
    /// 含之后新开的）一起置顶 —— **不提供单窗口置顶**，避免"一部分被覆盖、一部分置顶"的混合状态。
    /// </summary>
    public bool AppTopmost { get; set; }

    /// <summary>
    /// **应用内快捷键绑定**（ADR-0036 D7）：动作标识 → 绑定文本（如 <c>Ctrl+L</c>、<c>M</c>）。
    /// </summary>
    /// <remarks>
    /// 缺失的动作使用**内置默认绑定**；绑定为空串 = **不绑定**。动作目录见 `HotkeyCatalog`（UI 层）。
    /// 作用域 = 应用内（任一应用窗口聚焦即生效）；**不实现系统级全局热键**。
    /// </remarks>
    public IDictionary<string, string> Hotkeys { get; set; } = new Dictionary<string, string>(StringComparer.Ordinal);

    /// <summary>复制一份（避免外部持有共享实例）。</summary>
    public ApplicationSettings Clone() => new()
    {
        SchemaVersion = SchemaVersion,
        StopTimeoutSeconds = StopTimeoutSeconds,
        InputQueueCapacity = InputQueueCapacity,
        UiRefreshRateHz = UiRefreshRateHz,
        DiagnosticHintThreshold = DiagnosticHintThreshold,
        AlignAutoNetworkEnabled = AlignAutoNetworkEnabled,
        ShowInfoOverlay = ShowInfoOverlay,
        Theme = Theme,
        AccentColor = AccentColor,
        BackgroundMode = BackgroundMode,
        BackgroundColor = BackgroundColor,
        ShellAspect = ShellAspect,
        ShellFixedSize = ShellFixedSize,
        ModuleSourceDirectories = [.. ModuleSourceDirectories],
        LogLevel = LogLevel,
        AppTopmost = AppTopmost,
        Hotkeys = new Dictionary<string, string>(Hotkeys, StringComparer.Ordinal),
    };
}
