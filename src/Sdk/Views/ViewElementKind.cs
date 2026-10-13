namespace HYWDebugAssistant.Sdk.Views;

/// <summary>
/// 视图**预置元素**目录（v1 = 档 3 完整）。
/// </summary>
/// <remarks>
/// 模块**不提供 View 类型**：视图只能由框架按本目录中的元素组合渲染。
/// 新增元素属**兼容扩展**；删除或改语义属破坏性变更（须 ADR）。
/// 声明面另有更细的 kind 名与角色划分（见框架 schema）；本枚举是**渲染侧的目录**。
/// </remarks>
public enum ViewElementKind
{
    // ---- 数据呈现类（A） ----

    /// <summary>A1 数值显示（单/多值 + 格式单位）。</summary>
    Numeric = 0,

    /// <summary>A2 状态灯/指示器。</summary>
    StatusLight = 1,

    /// <summary>A3 曲线/波形（多通道、时间轴）。</summary>
    Curve = 2,

    /// <summary>A4 表格（行集）。</summary>
    Table = 3,

    /// <summary>A5 文本日志窗口。</summary>
    TextLog = 4,

    /// <summary>A6 十六进制/字节视图。</summary>
    HexView = 5,

    /// <summary>A7 仪表/进度条。</summary>
    Gauge = 6,

    /// <summary>A8 图像/位图帧。</summary>
    Image = 7,

    // ---- 交互控制类（B） ----

    /// <summary>B1 配置项编辑（按配置 Schema 自动生成）。</summary>
    ConfigurationEditor = 8,

    /// <summary>B2 命令按钮（触发模块定义的动作）。</summary>
    CommandButton = 9,

    /// <summary>B3 开关/滑块。</summary>
    ToggleSlider = 10,

    /// <summary>B4 枚举/下拉选择。</summary>
    EnumSelector = 11,

    /// <summary>B5 文本输入（如 AT 指令）。</summary>
    TextInput = 12,

    // ---- 结构容器类（C） ----

    /// <summary>C1 分组/分栏。</summary>
    Group = 14,

    /// <summary>C2 标签页。</summary>
    TabPages = 15,

    /// <summary>C3 折叠面板。</summary>
    Collapsible = 16,

    // ---- 交互控制类（B · 续） ----

    /// <summary>
    /// **B7 开关式滑块** —— 与 B3 同为「开 / 关」语义（值 = `true`/`false`），
    /// 只是**外观**是滑块开关（比勾选框更美观）。
    /// </summary>
    /// <remarks>
    /// <list type="bullet">
    ///   <item><b>为什么与 B3 并存</b>：B3 `ToggleSlider` = **勾选框**形态（保留给「复选 / 多选」语义），
    ///     两者**语义相同、外观不同** ⇒ 拆成两个元素类型，模块按场景自选；</item>
    ///   <item><b>枚举值在末尾</b>（`18`）⇒ 只增不改，模块的声明字节不变。</item>
    /// </list>
    /// </remarks>
    ToggleSwitch = 18,

    /// <summary>
    /// **C4 排布容器** —— 声明树里唯一**自己带排布键**的容器（`layout` / `overflow` / `padding` / `spacing` /
    /// `columns` / `rows` / 轨道间距），其余容器是**语义包装**（分组框 / 标签页 / 折叠面板），排布键取自本类。
    /// </summary>
    Container = 19,
}

/// <summary>
/// 渲染目录与**声明文件 kind 名**之间的唯一换算处。
/// </summary>
/// <remarks>
/// 声明名由框架 schema 登记（`container` / `group` / `tabs` / `collapsible` + 原子控件），
/// 本类只做名字 ↔ 枚举的换算：原子控件的声明名是其枚举名的首字母小写写法，
/// **只有 `tabs` 与 `TabPages` 的名字对不上**，作为显式例外写在这里（不另立一张表）。
/// </remarks>
public static class ViewElementKindExtensions
{
    /// <summary>标签页容器在声明文件里的 kind 名（与枚举名不一致的那一个）。</summary>
    public const string TabPagesName = "tabs";

    private static readonly Dictionary<string, ViewElementKind> ByDeclarationName = Build();

    /// <summary>声明文件里的 kind 名 → 渲染目录（未登记 ⇒ <see langword="null"/>）。</summary>
    public static ViewElementKind? TryParse(string? declarationName) =>
        declarationName is { Length: > 0 } name && ByDeclarationName.TryGetValue(name, out var kind) ? kind : null;

    /// <summary>声明文件里的 kind 名 → 渲染目录（未登记 ⇒ <paramref name="fallback"/>）。</summary>
    public static ViewElementKind Parse(string? declarationName, ViewElementKind fallback = ViewElementKind.Container) =>
        TryParse(declarationName) ?? fallback;

    /// <summary>渲染目录 → 声明文件里的 kind 名。</summary>
    public static string DeclarationName(this ViewElementKind kind) =>
        kind == ViewElementKind.TabPages
            ? TabPagesName
            : string.Concat(char.ToLowerInvariant(kind.ToString()[0]), kind.ToString()[1..]);

    /// <summary>是否是**自己带排布键**的容器（即 <see cref="ViewElementKind.Container"/>）。</summary>
    public static bool IsLayoutContainer(this ViewElementKind kind) => kind == ViewElementKind.Container;

    /// <summary>是否是标签页容器（<c>tabs</c>）—— 它内部有"页签条 + 页内容"这条分法。</summary>
    /// <param name="declarationName">声明文件里的 kind 名。</param>
    /// <returns>是标签页容器为 <see langword="true"/>。</returns>
    public static bool IsTabs(string? declarationName) =>
        string.Equals(declarationName, TabPagesName, StringComparison.Ordinal);

    /// <summary>是否是容器（含语义包装：分组 / 标签页 / 折叠）。</summary>
    public static bool IsContainer(this ViewElementKind kind) => kind
        is ViewElementKind.Container or ViewElementKind.Group or ViewElementKind.TabPages or ViewElementKind.Collapsible;

    private static Dictionary<string, ViewElementKind> Build()
    {
        var map = new Dictionary<string, ViewElementKind>(StringComparer.OrdinalIgnoreCase);

        foreach (var kind in Enum.GetValues<ViewElementKind>())
        {
            map[kind.DeclarationName()] = kind;
        }

        // 同义登记：声明文件里的 `tabs` 已由上一步映射；这里只补"枚举名本身"的容错拼写。
        map.TryAdd(nameof(ViewElementKind.TabPages), ViewElementKind.TabPages);
        return map;
    }
}
