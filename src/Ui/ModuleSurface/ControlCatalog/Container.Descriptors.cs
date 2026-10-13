namespace HYWDebugAssistant.Ui.ModuleSurface.ControlCatalog;

/// <summary>
/// 容器族（4 个）：接受 <c>children</c>，各自渲染出自己的外壳 + 内部独立排布（**不展平**子元素）。
/// </summary>
internal static class ContainerDescriptors
{
    /// <summary>分组 / 分栏（语义包装，不自带排布键）。</summary>
    internal static readonly ControlDescriptor Group = new(
        Kind: "group",
        AcceptsChildren: true,
        OwnsLayoutKeys: false,
        Roles: [],
        SemanticProperties:
            [new("title", "string"), new("padding", "number"), new("spacing", "number"), new("children", "array", Required: true), new("textPlacement", "enum", Default: "none"), new("textAnchor", "enum", Default: "center") ],
        GeometryStrategy: SurfaceGeometryStrategy.Adaptive,
        PresentationSources: [PresentationSource.Content],
        FormatCapabilities: [],
        ThemeSlot: "ControlTheme.Group",
        RenderState: ControlRenderState.Rendered);

    /// <summary>标签页（每子节点一页签，页签名取子节点的 <c>title</c>）。</summary>
    internal static readonly ControlDescriptor TabPages = new(
        Kind: "tabs",
        AcceptsChildren: true,
        OwnsLayoutKeys: false,
        Roles: [],
        SemanticProperties:
            [new("title", "string", Default: ""), new("stateKey", "string"), new("children", "array", Required: true), new("textPlacement", "enum", Default: "none"), new("textAnchor", "enum", Default: "center") ],
        GeometryStrategy: SurfaceGeometryStrategy.Fill(),
        PresentationSources: [PresentationSource.StateStore],
        FormatCapabilities: [],
        ThemeSlot: "ControlTheme.TabPages",
        RenderState: ControlRenderState.Rendered);

    /// <summary>折叠面板。</summary>
    internal static readonly ControlDescriptor Collapsible = new(
        Kind: "collapsible",
        AcceptsChildren: true,
        OwnsLayoutKeys: false,
        Roles: [],
        SemanticProperties:
        [
            new("title", "string", Default: ""),
            new("stateKey", "string"),
            new("expanded", "bool", Default: "true"),
            new("children", "array", Required: true),
            new("textPlacement", "enum", Default: "none"),
            new("textAnchor", "enum", Default: "center"),
        ],
        GeometryStrategy: SurfaceGeometryStrategy.Adaptive,
        PresentationSources: [PresentationSource.StateStore],
        FormatCapabilities: [],
        ThemeSlot: "ControlTheme.Collapsible",
        RenderState: ControlRenderState.Rendered);

    /// <summary>排布容器 —— 声明树里唯一自带排布键的容器。</summary>
    internal static readonly ControlDescriptor Container = new(
        Kind: "container",
        AcceptsChildren: true,
        OwnsLayoutKeys: true,
        Roles: [],
        SemanticProperties:
        [
            new("title", "string"),
            new("stateKey", "string"),
            new("layout", "enum"),
            new("overflow", "enum"),
            new("padding", "number"),
            new("spacing", "number"),
            new("columns", "array"),
            new("rows", "array"),
            new("columnSpacing", "number"),
            new("rowSpacing", "number"),
            new("children", "array", Required: true),
            new("textPlacement", "enum", Default: "none"),
            new("textAnchor", "enum", Default: "center"),
        ],
        GeometryStrategy: SurfaceGeometryStrategy.Fill(),
        PresentationSources: [PresentationSource.Content],
        FormatCapabilities: [],
        ThemeSlot: "ControlTheme.Container",
        RenderState: ControlRenderState.Rendered);

    /// <summary>本类登记的全部描述（顺序即文本顺序）。</summary>
    /// <remarks>与 <c>DataPresentationDescriptors</c> 的同名字段同理：独立类型让静态初始化的顺序由类型依赖写死。</remarks>
    internal static readonly ControlDescriptor[] All =
    [
        Group,
        TabPages,
        Collapsible,
        Container,
    ];
}
