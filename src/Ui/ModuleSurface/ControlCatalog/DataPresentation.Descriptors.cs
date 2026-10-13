using static HYWDebugAssistant.Ui.ModuleSurface.ControlCatalog.ControlRole;

namespace HYWDebugAssistant.Ui.ModuleSurface.ControlCatalog;

/// <summary>
/// 数据呈现族：吃 <c>data</c> 角色键，把数据源的取值/行集画出来。
/// </summary>
/// <remarks>
/// 一族里既有"真的画得出来"的，也有"只登记、先给占位"的 —— 两者都要在这里有一条描述：
/// 目录守的是<b>声明面包里的每一种 kind 都有归属</b>（缺一条就是"少一种控件"或"一种 kind 静默失效"），
/// "这一种本轮画不画"由描述里的渲染账位回答，不用"有没有描述"来回答。
/// </remarks>
internal static class DataPresentationDescriptors
{
    /// <summary>数值显示（单 / 多值 + 格式单位）。</summary>
    internal static readonly ControlDescriptor Numeric = new(
        Kind: "numeric",
        AcceptsChildren: false,
        OwnsLayoutKeys: false,
        Roles: [Data],
        SemanticProperties:
            [new("title", "string", Default: ""), new("stateKey", "string"), new("data", "string", Required: true), new("unit", "string", Default: ""), new("fontSize", "number", Required: true), new("textColor", "string", Required: true), new("titleFontSize", "number", Required: true), new("titleColor", "string", Required: true), new("textPlacement", "enum", Default: "none"), new("textAnchor", "enum", Default: "center") ],
        GeometryStrategy: SurfaceGeometryStrategy.Adaptive,
        PresentationSources: [PresentationSource.SourceAvailability, PresentationSource.Appearance],
        FormatCapabilities: [FormatCapability.Numeric, FormatCapability.Unit],
        ThemeSlot: "ControlTheme.Numeric",
        RenderState: ControlRenderState.Rendered);

    /// <summary>状态灯 / 指示器 —— 仅登记（专有键 <c>states</c> 待实现时定）。</summary>
    internal static readonly ControlDescriptor StatusLight = new(
        Kind: "statusLight",
        AcceptsChildren: false,
        OwnsLayoutKeys: false,
        Roles: [Data],
        SemanticProperties:
            [new("title", "string", Default: ""), new("stateKey", "string"), new("data", "string", Required: true), new("states", "array"), new("textPlacement", "enum", Default: "none"), new("textAnchor", "enum", Default: "center") ],
        GeometryStrategy: SurfaceGeometryStrategy.Adaptive,
        PresentationSources: [PresentationSource.SourceValue],
        FormatCapabilities: [],
        ThemeSlot: "ControlTheme.StatusLight",
        RenderState: ControlRenderState.DeclarationOnly);

    /// <summary>曲线 / 波形（多通道、时间轴）。</summary>
    internal static readonly ControlDescriptor Curve = new(
        Kind: "curve",
        AcceptsChildren: false,
        OwnsLayoutKeys: false,
        Roles: [Data],
        SemanticProperties:
            [new("title", "string", Default: ""), new("stateKey", "string"), new("data", "string", Required: true), new("fontSize", "number", Required: true), new("textColor", "string", Required: true), new("titleFontSize", "number", Required: true), new("titleColor", "string", Required: true), new("textPlacement", "enum", Default: "none"), new("textAnchor", "enum", Default: "center") ],
        GeometryStrategy: SurfaceGeometryStrategy.Adaptive,
        PresentationSources: [PresentationSource.SourceAvailability],
        FormatCapabilities: [FormatCapability.TimeAxis],
        ThemeSlot: "ControlTheme.Curve",
        RenderState: ControlRenderState.Rendered);

    /// <summary>表格（行集）—— 仅登记（专有键 <c>columns</c> 待实现时定）。</summary>
    internal static readonly ControlDescriptor Table = new(
        Kind: "table",
        AcceptsChildren: false,
        OwnsLayoutKeys: false,
        Roles: [Data],
        SemanticProperties:
            [new("title", "string", Default: ""), new("stateKey", "string"), new("data", "string", Required: true), new("columns", "array"), new("textPlacement", "enum", Default: "none"), new("textAnchor", "enum", Default: "center") ],
        GeometryStrategy: SurfaceGeometryStrategy.Adaptive,
        PresentationSources: [PresentationSource.SourceAvailability],
        FormatCapabilities: [],
        ThemeSlot: "ControlTheme.Table",
        RenderState: ControlRenderState.DeclarationOnly);

    /// <summary>图像 / 位图帧 —— 仅登记（<c>stretch</c> 待实现时定）。</summary>
    internal static readonly ControlDescriptor Image = new(
        Kind: "image",
        AcceptsChildren: false,
        OwnsLayoutKeys: false,
        Roles: [Data],
        SemanticProperties:
            [new("title", "string", Default: ""), new("stateKey", "string"), new("data", "string", Required: true), new("stretch", "enum"), new("textPlacement", "enum", Default: "none"), new("textAnchor", "enum", Default: "center") ],
        GeometryStrategy: SurfaceGeometryStrategy.Adaptive,
        PresentationSources: [PresentationSource.SourceAvailability],
        FormatCapabilities: [],
        ThemeSlot: "ControlTheme.Image",
        RenderState: ControlRenderState.DeclarationOnly);

    /// <summary>本类登记的全部描述（顺序即文本顺序）。</summary>
    /// <remarks>
    /// <b>顺序保证在这里</b>：本类是一个**独立类型**，它的静态字段初始化只依赖自己 ——
    /// 把描述挂在共享的偏类（<c>partial</c>）上时，偏类的静态字段**跨文件**初始化顺序未定义，
    /// 目录的静态初始化先跑时本文件的字段还是 <see langword="null"/>，
    /// 而"按类型反射枚举"会**静默跳过**它们（19 条描述只进 4 条，无任何报错）。
    /// 目录显式读这三份清单，顺序由类型依赖写死。
    /// <b>加一条描述必须同时加进这里</b> —— 条数由目录自检与 schema 基数交叉守住。
    /// </remarks>
    internal static readonly ControlDescriptor[] All =
    [
        Numeric,
        StatusLight,
        Curve,
        Table,
        Image,
    ];
}
