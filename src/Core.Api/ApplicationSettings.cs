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
    /// **吸附距离**（px，默认 4，范围 2–8）——【ADR-0042 D6 / 用户裁决】拖动时的"磁吸半径"。
    /// </summary>
    /// <remarks>
    /// <list type="bullet">
    ///   <item>**对齐类**候选（模块中心线 / 端口锚点 / 边缘对齐）的容差 = 本值；</item>
    ///   <item>**贴合类**候选（相邻边接触）的容差 = 本值 × 2 —— 拼接是主用途，值得给宽容差；</item>
    ///   <item>值越小越"跟手"（最多滞后一个容差就松手）；值越大越容易吸上；**没有额外释放带**；</item>
    ///   <item>JSON 键 <c>snapDistance</c>。</item>
    /// </list>
    /// </remarks>
    public double SnapDistance { get; set; } = 4;

    /// <summary>
    /// **叠加信息层开关**【ADR-0039 D5 · **默认值改为关闭 = ADR-0045 D1（用户裁决）**】。
    /// </summary>
    /// <remarks>
    /// 用户口径："叠加层**默认关闭** —— 从用户理解上这不是某种应该常驻的层，这也是为何要设计快捷键的原因；
    /// 叠加层是一种**附加的信息显示层**，属于**调试/信息观测**用。"
    /// <list type="bullet">
    ///   <item>**旧配置兼容**：文件里显式写过 <c>showInfoOverlay</c>（或更旧的 <c>showPortMarkers</c>/<c>showLinkLines</c>）
    ///     的，仍按文件里的值生效 —— 只有**没写过**的（含全新安装）才落到"关闭"；</item>
    ///   <item>端口标记**常显**（它是模块标准件，也是唯一建网入口），不受本开关影响；</item>
    ///   <item>默认键：**L**（`HotkeyCatalog.ToggleMarkers`「切换叠加信息层」）。</item>
    /// </list>
    /// </remarks>
    public bool ShowInfoOverlay { get; set; }

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
    /// **多行文本输入的提交键**（G2 · 2026-09）：
    /// <c>CtrlEnter</c>（默认：**回车换行 · Ctrl+Enter 提交 · 失焦不提交**）/
    /// <c>Enter</c>（回车提交 · Shift+Enter 换行）/
    /// <c>None</c>（键盘都不提交，只用模块的按钮）。
    /// </summary>
    /// <remarks>
    /// <list type="bullet">
    ///   <item><b>为什么是应用级设置而不是模块声明</b>：用户 2026-09 裁决 —— "换行是一个极其常见的行为，
    ///     且**不同用户习惯不一样**"，故由**用户**在设置里选，模块只声明"我要多行"（`ViewElement.Multiline`）；</item>
    ///   <item><b>只影响多行输入框</b>：单行输入仍是"回车 / 失焦一次提交"（ADR-0040 D-5）**不变**；</item>
    ///   <item><c>CtrlEnter</c> 下**失焦不提交** —— 否则"点发送按钮 ⇒ 先失焦提交一次 ⇒ 按钮又发一次"会**双发**；</item>
    ///   <item>非法值 ⇒ 回落默认 <c>CtrlEnter</c>（与外观类设置同一容错口径：不做校验、不提示）。</item>
    /// </list>
    /// </remarks>
    public string MultilineSubmitKey { get; set; } = "CtrlEnter";


    /// <summary>
    /// **界面字体**（2026-09 用户裁定：字体在设置里**显式可调**，读**系统已有字体**）。
    /// </summary>
    /// <remarks>
    /// <list type="bullet">
    ///   <item>作用于**框架自身的界面文字**（外壳、清单、设置页、状态栏等，资源键 <c>FontUi</c>）；</item>
    ///   <item>默认 <c>Segoe UI, Microsoft YaHei UI</c>（**保持既有观感不变** —— 用户要调的是文本框那一路）；</item>
    ///   <item>可写**回退列表**（逗号分隔）：拉丁字体在前、中文回退在后，例如 <c>Times New Roman, 宋体</c>；</item>
    ///   <item>空串 ⇒ 回落默认；设置页给系统字体下拉（**读系统已有字体**，不内置字体文件）。</item>
    /// </list>
    /// </remarks>
    public string UiFontFamily { get; set; } = "Segoe UI, Microsoft YaHei UI";

    /// <summary>
    /// **数据字体**（2026-09 用户裁定）：作用于**所有输入框与显示框**（模块内容区的文本元素 + 诊断里的数据列）。
    /// </summary>
    /// <remarks>
    /// 默认 <c>Times New Roman, 宋体</c> —— 用户原话："**建议默认中文宋体，英文 times new roman**"。
    /// 同样是回退列表（拉丁在前、中文在后）；资源键 <c>FontData</c>。
    /// </remarks>
    public string DataFontFamily { get; set; } = "Times New Roman, 宋体";
    /// <summary>
    /// **发送方向色**（十六进制，如 <c>#EA580C</c>；**空 = 跟随主题默认值**）【ADR-0044 · 用户第十四轮裁决 D1a】。
    /// </summary>
    /// <remarks>
    /// 「方向」= **稳定语义**（发送 / 接收），与「主题色」解耦：主题色只表达选中/聚焦/活动等**框架性交互**语义。
    /// **空串是默认值**（跟随 <c>Tokens.axaml</c> 里深浅两套各自的内置方向色）——这样深色主题的默认值不会被
    /// 浅色默认值"覆盖成死值"（`accentColor` 的默认非空，故其伴生色 `AccentSecondary` 实际从不生效，
    /// 属已知历史问题，见 ADR-0044 §4）。用户一旦填色，则**深浅两套主题都用它**（完全尊重用户选择，不做提示）。
    /// </remarks>
    public string DirectionSendColor { get; set; } = string.Empty;

    /// <summary>**接收方向色**（语义同 <see cref="DirectionSendColor"/>；空 = 跟随主题默认值）。</summary>
    public string DirectionReceiveColor { get; set; } = string.Empty;

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

    /// <summary>
    /// **数据结构（类型身份）→ 颜色** 的用户映射【ADR-0054 · 用户第三十四轮"添加数据结构-颜色映射表"】。
    /// </summary>
    /// <remarks>
    /// <list type="bullet">
    ///   <item>键 = **类型身份**（`标识@v版本`；未声明类型的旧模块用结构描述），值 = `#RRGGBB`；</item>
    ///   <item>**缺省 = 不配置** ⇒ 该类型走**稳定哈希自动配色**（同一身份跨模块/跨重启同色 ⇒ 不配置也能分辨类型）；</item>
    ///   <item>**只影响呈现**：它不参与建网/兼容/判定，也不进组合件；改色只改"画出来的颜色"。</item>
    /// </list>
    /// </remarks>
    public IDictionary<string, string> TypeColors { get; set; } = new Dictionary<string, string>(StringComparer.Ordinal);

    /// <summary>复制一份（避免外部持有共享实例）。</summary>
    public ApplicationSettings Clone() => new()
    {
        SchemaVersion = SchemaVersion,
        StopTimeoutSeconds = StopTimeoutSeconds,
        InputQueueCapacity = InputQueueCapacity,
        UiRefreshRateHz = UiRefreshRateHz,
        DiagnosticHintThreshold = DiagnosticHintThreshold,
        AlignAutoNetworkEnabled = AlignAutoNetworkEnabled,
        SnapDistance = SnapDistance,
        ShowInfoOverlay = ShowInfoOverlay,
        Theme = Theme,
        AccentColor = AccentColor,
        BackgroundMode = BackgroundMode,
        BackgroundColor = BackgroundColor,
        MultilineSubmitKey = MultilineSubmitKey,
        // 【2026-09 缺陷修复 · 用户实测"字体设置不生效且无法保存"】**这两行曾漏掉** ⇒ Clone 是
        // 写侧（`SettingsService.Update` 的 `Current = settings.Clone()`）与读侧（`RuntimeServices.GetSettings`）
        // 的**必经之路** ⇒ 字体在任何环节都被静默换成默认值（落盘写默认值、读回默认值、`ThemeApplier` 拿到默认值）。
        // **颜色能用只是因为它俩不在颜色那一组里**（同一条管线、同一份文件，唯一差别就是这份字段清单）。
        // 纪律：`Clone` 必须与**全部可持久化属性**一一对应 —— `SettingsServiceTests` 有一条反射式用例钉住它。
        UiFontFamily = UiFontFamily,
        DataFontFamily = DataFontFamily,
        DirectionSendColor = DirectionSendColor,
        DirectionReceiveColor = DirectionReceiveColor,
        ShellAspect = ShellAspect,
        ShellFixedSize = ShellFixedSize,
        ModuleSourceDirectories = [.. ModuleSourceDirectories],
        LogLevel = LogLevel,
        AppTopmost = AppTopmost,
        Hotkeys = new Dictionary<string, string>(Hotkeys, StringComparer.Ordinal),
        TypeColors = new Dictionary<string, string>(TypeColors, StringComparer.Ordinal),
    };
}
