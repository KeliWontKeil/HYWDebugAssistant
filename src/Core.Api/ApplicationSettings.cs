using HYWDebugAssistant.Sdk.Text;

namespace HYWDebugAssistant.Core.Api;

/// <summary>
/// **应用级设置**（独立于组合件；便携式存放于 `settings\settings.json`）。
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

    /// <summary>
    /// **数据面（缓冲与流控）机制参数**：队列容量、满时策略、停止等待、饱和判据、积压上限、外部写超时。
    /// </summary>
    /// <remarks>
    /// 它是"框架怎么搬运数据"这一层的容器：每一项的语义、单位、范围与生效时机见 <see cref="FlowSettings"/>。
    /// </remarks>
    public FlowSettings Flow { get; set; } = new();

    /// <summary>
    /// **文本显示缓冲的内存上限**（行数 / 字符预算）；与 Flow 分开，但同属“高级”设置域。
    /// </summary>
    public DisplaySettings Display { get; set; } = new();

    /// <summary>UI 刷新率（Hz）。默认 60（UI 按此节拍从订阅缓冲抽样读取）。</summary>
    public int UiRefreshRateHz { get; set; } = 60;

    /// <summary>
    /// **是否在侧栏底部显示轻量性能监视**（默认开）：CPU / 内存 / 线程 / GC 计数，约 1 Hz 只读采样。
    /// </summary>
    /// <remarks>
    /// 只是观测，不参与任何机制判定；关闭后不采样、不占侧栏空间。JSON 键 <c>showPerformanceMonitor</c>。
    /// </remarks>
    public bool ShowPerformanceMonitor { get; set; } = true;

    /// <summary>
    /// **对齐自动建网**（默认开）：**接触且对齐**的未打标签口自动连上。
    /// </summary>
    /// <remarks>
    /// 「快速建网 R0」（加入模块即自动连线）因**反直觉**已移除；
    /// JSON 键 <c>alignAutoNetwork</c>，旧键 <c>autoNetworkEnabled</c> 兼容读取。
    /// </remarks>
    public bool AlignAutoNetworkEnabled { get; set; } = true;

    /// <summary>
    /// **吸附距离**（px，默认 4，范围 2–8）：拖动时的「磁吸半径」。
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
    /// **叠加信息层开关**（**默认关闭**）。
    /// </summary>
    /// <remarks>
    /// 叠加层**默认关闭**：它不是应该常驻的层，而是**调试/信息观测**用的**附加信息显示层**，
    /// 所以需要快捷键来随时切换。
    /// <list type="bullet">
    ///   <item>**旧配置兼容**：文件里显式写过 <c>showInfoOverlay</c>（或更旧的 <c>showPortMarkers</c>/<c>showLinkLines</c>）
    ///     的，仍按文件里的值生效 —— 只有**没写过**的（含全新安装）才落到"关闭"；</item>
    ///   <item>端口标记**常显**（它是模块标准件，也是唯一建网入口），不受本开关影响；</item>
    ///   <item>默认键：**L**（`HotkeyCatalog.ToggleMarkers`「切换叠加信息层」）。</item>
    /// </list>
    /// </remarks>
    public bool ShowInfoOverlay { get; set; }

    /// <summary>
    /// **外观块**（设置里"外观"这一层）：选哪套主题 + 逐项覆盖 + 字体轴。
    /// </summary>
    /// <remarks>
    /// 它是**外观**这一层的容器：主题是"值的来源"，覆盖是"用户的差异"，字体是另一个轴。
    /// 层次与解析链见 <see cref="AppearanceSettings"/> 的说明。
    /// </remarks>
    public AppearanceSettings Appearance { get; set; } = new();

    /// <summary>
    /// **界面语言**：<see cref="TextLanguages.System"/>（跟随系统，默认）或受支持的语言标识（如 <c>zh-Hans</c>）。
    /// </summary>
    /// <remarks>
    /// <list type="bullet">
    ///   <item>JSON 键 <c>uiLanguage</c>；合法值 = <see cref="TextLanguages.System"/> 或一个**合法的文化名**
    ///     （按规范形式落盘，如 <c>zh-Hans</c>）；</item>
    ///   <item>**只影响界面文案**：数值 / 日期格式仍按用户的区域设置（不动 <c>CurrentCulture</c>）；</item>
    ///   <item>界面文本在载入时解析一次 ⇒ 切换后**重启完全生效**；</item>
    ///   <item>非法值 ⇒ 回落 <see cref="TextLanguages.System"/>（与外观类设置同一容错口径：不做校验、不提示）。</item>
    /// </list>
    /// </remarks>
    public string UiLanguage { get; set; } = TextLanguages.System;

    /// <summary>
    /// **多行文本输入的提交键**：
    /// <c>CtrlEnter</c>（默认：**回车换行 · Ctrl+Enter 提交 · 失焦不提交**）/
    /// <c>Enter</c>（回车提交 · Shift+Enter 换行）/
    /// <c>None</c>（键盘都不提交，只用模块的按钮）。
    /// </summary>
    /// <remarks>
    /// <list type="bullet">
    ///   <item><b>为什么是应用级设置而不是模块声明</b>：换行极其常见，
    ///     且**不同用户习惯不一样**，故由**用户**在设置里选，模块只声明「我要多行」（`ViewElement.Multiline`）；</item>
    ///   <item><b>只影响多行输入框</b>：单行输入仍是「回车 / 失焦一次提交」**不变**；</item>
    ///   <item><c>CtrlEnter</c> 下**失焦不提交** —— 否则「点发送按钮 ⇒ 先失焦提交一次 ⇒ 按钮又发一次」会**双发**；</item>
    ///   <item>非法值 ⇒ 回落默认 <c>CtrlEnter</c>（与外观类设置同一容错口径：不做校验、不提示）。</item>
    /// </list>
    /// </remarks>
    public string MultilineSubmitKey { get; set; } = "CtrlEnter";

    /// <summary>
    /// **外壳窗口比例**：<c>9:19.5</c>（默认，手机竖屏常用比例）/ <c>9:16</c>；宽体由自由缩放解决。
    /// </summary>
    public string ShellAspect { get; set; } = "9:19.5";

    /// <summary>
    /// **外壳尺寸是否固定**：默认 <c>true</c>（不可缩放，尺寸由比例推导）；
    /// <c>false</c> = 可自由缩放（比例仅作初值）。
    /// </summary>
    public bool ShellFixedSize { get; set; } = true;

    /// <summary>
    /// **关闭主窗口时的行为**：<c>Ask</c>（每次询问，默认）/ <c>Save</c>（自动保存并退出）/
    /// <c>Discard</c>（不保存直接退出）。
    /// </summary>
    public string WorkspaceExitBehavior { get; set; } = "Ask";

    /// <summary>日志级别（Trace/Debug/Information/Warning/Error）。默认 Information。</summary>
    public string LogLevel { get; set; } = "Information";

    /// <summary>
    /// **应用整体置顶**：为 true 时本应用**全部**窗口（外壳 + 模块窗口 + 弹窗，
    /// 含之后新开的）一起置顶 —— **不提供单窗口置顶**，避免"一部分被覆盖、一部分置顶"的混合状态。
    /// </summary>
    public bool AppTopmost { get; set; }

    /// <summary>
    /// **应用内快捷键绑定**：动作标识 → 绑定文本（如 <c>Ctrl+L</c>、<c>M</c>）。
    /// </summary>
    /// <remarks>
    /// 缺失的动作使用**内置默认绑定**；绑定为空串 = **不绑定**。动作目录见 `HotkeyCatalog`（UI 层）。
    /// 作用域 = 应用内（任一应用窗口聚焦即生效）；**不实现系统级全局热键**。
    /// </remarks>
    public IDictionary<string, string> Hotkeys { get; set; } = new Dictionary<string, string>(StringComparer.Ordinal);

    /// <summary>
    /// **数据结构（类型身份）→ 颜色** 的用户映射。
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

        // **数据面块是 record**：`with { }` 复制出一个新实例（标量全拷）。
        // 不能直接赋引用，否则"读出来改一份"会改到生效值。
        Flow = Flow with { },
        Display = Display with { },
        UiRefreshRateHz = UiRefreshRateHz,
        ShowPerformanceMonitor = ShowPerformanceMonitor,
        AlignAutoNetworkEnabled = AlignAutoNetworkEnabled,
        SnapDistance = SnapDistance,
        ShowInfoOverlay = ShowInfoOverlay,
        // **外观块要深拷**：它自己持两层字典（按主题分层的覆盖），浅拷会让"改一份影响另一份"。
        // 纪律：`Clone` 必须与**全部可持久化属性**一一对应 —— `SettingsServiceTests` 有一条反射式用例钉住它。
        Appearance = Appearance.Clone(),
        UiLanguage = UiLanguage,
        MultilineSubmitKey = MultilineSubmitKey,
        ShellAspect = ShellAspect,
        ShellFixedSize = ShellFixedSize,
        WorkspaceExitBehavior = WorkspaceExitBehavior,
        LogLevel = LogLevel,
        AppTopmost = AppTopmost,
        Hotkeys = new Dictionary<string, string>(Hotkeys, StringComparer.Ordinal),
        TypeColors = new Dictionary<string, string>(TypeColors, StringComparer.Ordinal),
    };
}
