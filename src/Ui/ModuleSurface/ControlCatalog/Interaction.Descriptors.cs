using static HYWDebugAssistant.Ui.ModuleSurface.ControlCatalog.ControlRole;

namespace HYWDebugAssistant.Ui.ModuleSurface.ControlCatalog;

/// <summary>
/// 交互族（7 个）：吃 <c>input</c> 或 <c>command</c> 角色键，把用户的操作投递给模块。
/// </summary>
internal static class InteractionDescriptors
{
    /// <summary>配置项编辑（按配置 Schema 自动生成字段行）。</summary>
    internal static readonly ControlDescriptor ConfigurationEditor = new(
        Kind: "configurationEditor",
        AcceptsChildren: false,
        OwnsLayoutKeys: false,
        Roles: [Data],
        SemanticProperties: [new("title", "string", Default: ""), new("stateKey", "string"), new("data", "string", Required: true), new("textPlacement", "enum", Default: "none"), new("textAnchor", "enum", Default: "center") ],
        GeometryStrategy: SurfaceGeometryStrategy.Adaptive,
        PresentationSources: [PresentationSource.SourceAvailability, PresentationSource.Appearance],
        FormatCapabilities: [],
        ThemeSlot: "ControlTheme.ConfigurationEditor",
        RenderState: ControlRenderState.Rendered);

    /// <summary>命令按钮（触发模块定义的动作）。</summary>
    /// <remarks>
    /// <b>它的字是 <c>text</c>，不是 <c>title</c></b>：按钮身上的字就是按钮本身，
    /// 与"这个元素的说明文字"是两件事。同一个键承担两种语义时，界面上会出现双份文案。
    /// </remarks>
    internal static readonly ControlDescriptor CommandButton = new(
        Kind: "commandButton",
        AcceptsChildren: false,
        OwnsLayoutKeys: false,
        Roles: [Command],
        SemanticProperties:
        [
            new("text", "string", Default: "命令名"),
            new("stateKey", "string"),
            new("command", "string", Required: true),
            new("fontSize", "number", Required: true),
            new("textColor", "string", Required: true),
            new("titleFontSize", "number", Required: true),
            new("titleColor", "string", Required: true),
            new("textPlacement", "enum", Default: "none"),
            new("textAnchor", "enum", Default: "center"),
        ],
        GeometryStrategy: SurfaceGeometryStrategy.Adaptive,
        PresentationSources: [PresentationSource.Appearance],
        FormatCapabilities: [],
        ThemeSlot: "ControlTheme.CommandButton",
        RenderState: ControlRenderState.Rendered);

    /// <summary>开关 / 滑块（勾选框形态，保留给复选语义）。</summary>
    internal static readonly ControlDescriptor ToggleSlider = new(
        Kind: "toggleSlider",
        AcceptsChildren: false,
        OwnsLayoutKeys: false,
        Roles: [Input],
        SemanticProperties: [new("title", "string", Default: ""), new("stateKey", "string"), new("input", "string", Required: true), new("titleFontSize", "number", Required: true), new("titleColor", "string", Required: true), new("textPlacement", "enum", Default: "none"), new("textAnchor", "enum", Default: "center") ],
        GeometryStrategy: SurfaceGeometryStrategy.Adaptive,
        PresentationSources: [PresentationSource.StateStore, PresentationSource.Appearance],
        FormatCapabilities: [],
        ThemeSlot: "ControlTheme.ToggleSlider",
        RenderState: ControlRenderState.Rendered);

    /// <summary>
    /// 文本框 —— **模块里唯一的一块文本面**（输入与输出都是它）。
    /// </summary>
    /// <remarks>
    /// 角色键是 <c>input</c> **或** <c>data</c>（二选一）：绑前者就是输入（用户打的字提交给模块），
    /// 绑后者就是输出（模块写进来的行）。以前这是两个 kind（<c>textInput</c> / <c>textLog</c>），
    /// 于是同一个界面里出现两种文本行为，声明面也逼着模块作者先决定"我这块算输入还是输出"。
    /// </remarks>
    internal static readonly ControlDescriptor TextBox = new(
        Kind: "textBox",
        AcceptsChildren: false,
        OwnsLayoutKeys: false,
        Roles: [Input, Data],
        SemanticProperties:
        [
            new("title", "string", Default: ""),
            new("stateKey", "string"),
            new("multiline", "bool", Default: "false"),
            new("textAlign", "enum", Default: "default"),
            new("wrap", "bool", Default: "true"),
            new("showNewlineMark", "bool", Default: "true"),
            new("readonly", "bool", Default: "false"),
            new("scrollBar", "enum", Required: true),
            new("minEditorHeight", "number", Default: "0"),
            new("fontSize", "number", Required: true),
            new("textColor", "string", Required: true),
            new("titleFontSize", "number", Required: true),
            new("titleColor", "string", Required: true),
            new("textPlacement", "enum", Default: "none"),
            new("textAnchor", "enum", Default: "center"),
        ],
        GeometryStrategy: SurfaceGeometryStrategy.Adaptive,
        PresentationSources:
            [PresentationSource.InputPolicy, PresentationSource.StateStore, PresentationSource.Appearance],
        FormatCapabilities: [],
        ThemeSlot: "ControlTheme.TextBox",
        RenderState: ControlRenderState.Rendered);

    /// <summary>
    /// 一块文字（<c>label</c>）—— 可静态、可动态。
    /// </summary>
    /// <remarks>
    /// 它存在的理由是**解耦**：描述性文本不必再寄生在别的控件的 <c>title</c> 上，
    /// 于是"要不要给这个控件配一行说明"变成模块作者的排布选择，而不是框架的固定动作。
    /// 绑了 <c>data</c> 之后它还是"给用户看一句话"的落点 —— 消息不必再借只读文本框来假装。
    /// </remarks>
    internal static readonly ControlDescriptor Label = new(
        Kind: "label",
        AcceptsChildren: false,
        OwnsLayoutKeys: false,
        Roles: [Data],
        SemanticProperties:
        [
            new("data", "string"),
            new("text", "string", Default: ""),
            new("title", "string", Default: ""),
            new("fontSize", "number", Required: true),
            new("muted", "bool", Default: "false"),
            new("textColor", "string", Required: true),
            new("titleFontSize", "number", Required: true),
            new("titleColor", "string", Required: true),
            new("textPlacement", "enum", Default: "none"),
            new("textAnchor", "enum", Default: "center"),
        ],
        GeometryStrategy: SurfaceGeometryStrategy.Adaptive,
        PresentationSources: [PresentationSource.StateStore, PresentationSource.Appearance],
        FormatCapabilities: [],
        ThemeSlot: "ControlTheme.Label",
        RenderState: ControlRenderState.Rendered);

    /// <summary>枚举 / 下拉选择 —— 仅登记（专有键 <c>options</c> 待实现时定）。</summary>
    internal static readonly ControlDescriptor EnumSelector = new(
        Kind: "enumSelector",
        AcceptsChildren: false,
        OwnsLayoutKeys: false,
        Roles: [Input],
        SemanticProperties:
            [new("title", "string", Default: ""), new("stateKey", "string"), new("input", "string", Required: true), new("options", "array"), new("textPlacement", "enum", Default: "none"), new("textAnchor", "enum", Default: "center") ],
        GeometryStrategy: SurfaceGeometryStrategy.Adaptive,
        PresentationSources: [PresentationSource.InputPolicy],
        FormatCapabilities: [],
        ThemeSlot: "ControlTheme.EnumSelector",
        RenderState: ControlRenderState.DeclarationOnly);

    /// <summary>开关式滑块（与 <c>toggleSlider</c> 语义相同、外观不同）。</summary>
    internal static readonly ControlDescriptor ToggleSwitch = new(
        Kind: "toggleSwitch",
        AcceptsChildren: false,
        OwnsLayoutKeys: false,
        Roles: [Input],
        SemanticProperties: [new("title", "string", Default: ""), new("stateKey", "string"), new("input", "string", Required: true), new("textPlacement", "enum", Default: "none"), new("textAnchor", "enum", Default: "center") ],
        GeometryStrategy: SurfaceGeometryStrategy.Adaptive,
        PresentationSources: [PresentationSource.StateStore, PresentationSource.Appearance],
        FormatCapabilities: [],
        ThemeSlot: "ControlTheme.ToggleSwitch",
        RenderState: ControlRenderState.Rendered);

    /// <summary>本类登记的全部描述（顺序即文本顺序）。</summary>
    /// <remarks>与 <c>DataPresentationDescriptors</c> 的同名字段同理：独立类型让静态初始化的顺序由类型依赖写死。</remarks>
    internal static readonly ControlDescriptor[] All =
    [
        ConfigurationEditor,
        CommandButton,
        ToggleSlider,
        EnumSelector,
        TextBox,
        Label,
        ToggleSwitch,
    ];
}
