using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.VisualTree;
using HYWDebugAssistant.Sdk.Ui.Formatting;
using HYWDebugAssistant.Sdk.Views;

namespace HYWDebugAssistant.Sdk.Ui.Controls;

/// <summary>数值控件的**数量种类**（Bytes = 自动 B/KB/MB/GB，单元已含在值文本里）。</summary>
public enum ModuleQuantity
{
    /// <summary>原样显示（可选小数）。</summary>
    None,

    /// <summary>字节数量：按数量级自动选 B/KB/MB/GB，单位并进同一段值文本。</summary>
    Bytes,
}

/// <summary>
/// **模块文字元件的共同底座**：`TextBlock` 派生，统一 `Source`、运行期外观与**文字垂直对齐**。
/// </summary>
/// <remarks>
/// <para>
/// `TextBlock` 本身没有“文字在自己的矩形里垂直对齐”的属性：`VerticalAlignment` 只决定控件在父容器里的位置。
/// 模块标签的矩形通常是固定高度（并放在 `Canvas` 里），所以旧模板里内层 `TextBlock` 的居中能力必须由本底座补回来。
/// </para>
/// <para>
/// <see cref="TextVerticalAlignment"/> 默认 <see cref="VerticalAlignment.Center"/>，与旧模板行为一致；
/// 需要贴顶或贴底时在视图里显式声明。`Padding` 仍会影响文字可用的内容盒，但不要用 `Padding` 代替本属性。
/// </para>
/// </remarks>
public abstract class ModuleTextElement : TextBlock, IModuleViewElement, IModuleElementInternal
{
    /// <summary>文字在控件矩形内的垂直对齐（默认居中）。</summary>
    public static readonly StyledProperty<VerticalAlignment> TextVerticalAlignmentProperty =
        AvaloniaProperty.Register<ModuleTextElement, VerticalAlignment>(
            nameof(TextVerticalAlignment),
            defaultValue: VerticalAlignment.Center);

    /// <summary>造一个文字元件。</summary>
    protected ModuleTextElement()
    {
        // 透明底：整个矩形都能命中，悬停提示才不用"指到字上"才出来。
        Background = Brushes.Transparent;
    }

    /// <summary>本元件绑定的名字（显示源名；空 = 不绑）。</summary>
    /// <remarks>它同时是**运行期外观的寻址键**。</remarks>
    public string Source { get; set; } = string.Empty;

    /// <summary>
    /// 文字在控件矩形内的垂直对齐；`Top` / `Center` / `Bottom`。
    /// </summary>
    /// <remarks>
    /// 与控件自身的 <see cref="Layoutable.VerticalAlignment"/> 是两个维度：后者决定控件在父容器里的位置。
    /// </remarks>
    public VerticalAlignment TextVerticalAlignment
    {
        get => GetValue(TextVerticalAlignmentProperty);
        set => SetValue(TextVerticalAlignmentProperty, value);
    }

    /// <inheritdoc />
    public abstract void Refresh(IModuleViewContext context);

    /// <inheritdoc />
    string IModuleElementInternal.Source => Source;

    /// <inheritdoc />
    void IModuleElementInternal.ApplyModuleAppearance(ViewAppearance appearance) =>
        ModuleTextAppearance.Apply(this, appearance);

    /// <inheritdoc />
    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);

        if (change.Property == TextVerticalAlignmentProperty)
        {
            InvalidateVisual();
        }
    }

    /// <summary>
    /// 在 `TextBlock` 已算好的文字原点（含左/上内边距）上追加垂直偏移。
    /// </summary>
    protected override void RenderTextLayout(DrawingContext context, Point origin)
    {
        var offset = ComputeVerticalOffset();

        base.RenderTextLayout(
            context,
            offset == 0 ? origin : new Point(origin.X, origin.Y + offset));
    }

    /// <summary>按当前对齐与内容盒计算文字需要下移多少。</summary>
    private double ComputeVerticalOffset()
    {
        if (TextVerticalAlignment == VerticalAlignment.Top)
        {
            return 0;
        }

        var padding = Padding;
        var available = Bounds.Height - padding.Top - padding.Bottom;
        var free = available - TextLayout.Height;

        // 文字比内容盒还高时交给 TextBlock 自己的默认行为，不再二次偏移。
        if (free <= 0)
        {
            return 0;
        }

        return TextVerticalAlignment == VerticalAlignment.Bottom ? free : free / 2;
    }
}

/// <summary>
/// **一块会变的文字**：绑一条显示源，或者一句静态文字。
/// </summary>
/// <remarks>
/// <para>
/// 它是 <see cref="ModuleTextElement"/> 的派生控件（再往上仍是 <see cref="TextBlock"/>），不是模板化控件：
/// 文字排版、换行、对齐、字号全部沿用 `TextBlock` 的既有属性，模块视图不必为了"多行 / 微型字 / 右对齐"退回到裸 `TextBlock`。
/// </para>
/// <para>
/// 标签是**独立元素**，不寄生在别的控件里：它有自己的矩形、自己的对齐、自己的省略号。
/// 视图要在哪儿显示标签，就在哪儿摆一个本控件 —— 字段标题、读数标题、区标题、状态、单位都是它。
/// </para>
/// <para>
/// **文案来源优先级：<see cref="Field"/> &gt; <see cref="ModuleTextElement.Source"/> &gt; <see cref="Text"/>**，视图只用其中一条：
/// 配置字段的标题不复制到视图里（<see cref="Field"/> 读配置元数据）；动态一句话走 <see cref="ModuleTextElement.Source"/>；
/// 固定一句话或预览初值走 <see cref="Text"/>。带 <see cref="ModuleTextElement.Source"/> 时，<see cref="Text"/> 是预览/初值，
/// 运行期第一次 <see cref="Refresh"/> 会用真实数据覆盖它。
/// </para>
/// <para>
/// <b>单位不是特殊概念</b>：`ms` / `条` / `个` 就是一个标签，摆在该摆的地方。
/// </para>
/// <para>
/// 视觉结构就是控件本身：排印档位由视图给的类决定（`caption` / `hint` / `micro` / `right` / `wrap`）。
/// </para>
/// </remarks>
public sealed class ModuleLabel : ModuleTextElement
{
    private IModuleViewContext? _context;
    private string _field = string.Empty;
    private string? _tip;
    private long _textRevision = -1;

    /// <summary>造一块标签。</summary>
    public ModuleLabel()
    {
        // 预览器没有宿主：配置字段的标题从视图 DataContext（设计时上下文）取，
        // 与运行时走同一个 IModuleViewContext.ReadField，文案仍只有一份。
        DataContextChanged += (_, _) =>
        {
            if (_field.Length > 0)
            {
                ResolveContext();
                ApplyFieldText();
            }
        };

        AttachedToVisualTree += (_, _) =>
        {
            if (_field.Length > 0)
            {
                ResolveContext();
                ApplyFieldText();
            }
        };
    }

    /// <summary>
    /// 标签文案取自**配置字段的标题**（`[ConfigurationField]` 的标题 / 表里的键）。
    /// </summary>
    /// <remarks>
    /// 它是"视图不抄文案"的唯一通路：同一个标题既不会被视图再写一遍，也不会因为语言切换而漏改。
    /// 悬停提示取字段的说明（没有说明时取标题本身）。
    /// </remarks>
    public string Field
    {
        get => _field;
        set
        {
            if (string.Equals(_field, value, StringComparison.Ordinal))
            {
                return;
            }

            _field = value;
            ResolveContext();
            ApplyFieldText();
        }
    }

    /// <inheritdoc />
    public override void Refresh(IModuleViewContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        _context = context;

        if (_field.Length > 0)
        {
            ApplyFieldText();
            return;
        }

        if (Source.Length == 0)
        {
            return;
        }

        // **变更号没变就不读**：全量行表在长缓冲上是一次整表复制，静止的标签不该每帧付这笔钱。
        var revision = context.ReadTextRevision(Source);

        if (revision == _textRevision)
        {
            return;
        }

        _textRevision = revision;

        var lines = context.ReadText(Source).Lines;
        var text = lines.Count == 0 ? string.Empty : string.Join(Environment.NewLine, lines);

        // **内容没变就一个字都不动**：赋 Text 会让布局失效，静止的文字每帧白排一次。
        if (!string.Equals(Text, text, StringComparison.Ordinal))
        {
            Text = text;
        }

        ApplyTip(NullIfEmpty(text));
    }

    /// <summary>字段标题（来自配置元数据）落到标签上。</summary>
    private void ApplyFieldText()
    {
        if (_field.Length == 0)
        {
            return;
        }

        var field = _context?.ReadField(_field);
        var title = field?.Title ?? string.Empty;
        var tip = string.IsNullOrWhiteSpace(field?.Description) ? NullIfEmpty(title) : field!.Description;

        if (!string.Equals(Text, title, StringComparison.Ordinal))
        {
            Text = title;
        }

        ApplyTip(tip);
    }

    /// <summary>提示只在真的变了才写：字段标签每帧都会重新读一次元数据。</summary>
    /// <param name="tip">悬停提示（空 = 不挂）。</param>
    private void ApplyTip(string? tip)
    {
        if (string.Equals(_tip, tip, StringComparison.Ordinal))
        {
            return;
        }

        _tip = tip;
        ToolTip.SetTip(this, tip);
    }

    /// <summary>找上下文：运行时的宿主祖先优先；预览器/测试按 DataContext 直接提供。</summary>
    private void ResolveContext()
    {
        if (_context is not null)
        {
            return;
        }

        if (this.FindAncestorOfType<ModuleViewHost>() is { } host)
        {
            _context = host.Context;
            return;
        }

        if (DataContext is IModuleViewContext context)
        {
            _context = context;
        }
    }

    private static string? NullIfEmpty(string text) => text.Length == 0 ? null : text;
}

/// <summary>
/// **数值读数**：只显示值本身（字节数量时，单位已含在值文本里）。
/// </summary>
/// <remarks>
/// <para>
/// 与 <see cref="ModuleLabel"/> 一样是 <see cref="ModuleTextElement"/> 派生控件：`Text` 既是**预览/初值**，
/// 也是运行期显示值；运行期 <see cref="Refresh"/> 拿到真实数值后覆盖它。
/// </para>
/// <para>
/// 读数**不自带标题**：标题是一个独立的 <see cref="ModuleLabel"/>，摆在哪由视图决定 ——
/// 这样标题和值才能各自对齐，而不是被控件的内部 `*` 列挤出一个空洞。
/// </para>
/// <para>
/// **单位不是控件的属性**：静态单位（`条` / `个`）就是视图里的另一个标签；
/// 字节计数的 B/KB/MB 是"值文本"的一部分（<see cref="ByteQuantity"/> 唯一实现），不单独成块、不单独设色。
/// </para>
/// </remarks>
public sealed class ModuleNumeric : ModuleTextElement
{
    /// <summary>数量种类（默认 <see cref="ModuleQuantity.None"/> = 原样显示）。</summary>
    public ModuleQuantity Quantity { get; set; }

    /// <inheritdoc />
    public override void Refresh(IModuleViewContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        if (Source.Length == 0)
        {
            return;
        }

        var latest = context.ReadLatest(Source);
        var text = latest is { } number ? Format(number) : "-";

        if (!string.Equals(Text, text, StringComparison.Ordinal))
        {
            Text = text;
        }
    }

    /// <summary>值的显示文本（字节数量含单位；其余按当前文化显示小数）。</summary>
    /// <param name="number">值。</param>
    private string Format(double number) => Quantity == ModuleQuantity.Bytes
        ? ByteQuantity.Format(number)
        : number.ToString("0.###", System.Globalization.CultureInfo.CurrentCulture);
}

/// <summary>模块文本元件共用的运行期外观落点（<see cref="ModuleLabel"/> / <see cref="ModuleNumeric"/>）。</summary>
internal static class ModuleTextAppearance
{
    /// <summary>把文字色 / 字号 / 启用状态落到 TextBlock 自身。</summary>
    /// <param name="target">文本控件。</param>
    /// <param name="appearance">外观。</param>
    public static void Apply(TextBlock target, ViewAppearance appearance)
    {
        ArgumentNullException.ThrowIfNull(target);
        ArgumentNullException.ThrowIfNull(appearance);

        if (appearance.Text is { } text)
        {
            target.Foreground = new SolidColorBrush(Color.FromUInt32(text));
        }
        else if (appearance.TextToken is { Length: > 0 } token)
        {
            target[!TextBlock.ForegroundProperty] =
                new Avalonia.Markup.Xaml.MarkupExtensions.DynamicResourceExtension(token);
        }
        else
        {
            target.ClearValue(TextBlock.ForegroundProperty);
        }

        if (appearance.TextSize is { } size)
        {
            target.FontSize = size;
        }
        else
        {
            target.ClearValue(TextBlock.FontSizeProperty);
        }

        if (appearance.Enabled is { } enabled)
        {
            target.IsEnabled = enabled;
        }
    }
}

/// <summary>
/// **状态灯**：一个圆点，底色由模块设的**角色词**决定（框架不认识"红 = 失败"）。
/// </summary>
/// <remarks>
/// 没设过角色 ⇒ 空闲档。角色词 → token 的解析在宿主（`PaletteManifest`），
/// 控件只接收 token；用户改主题色时它跟着变（走 `DynamicResource` 绑定，不是写死的色值）。
/// 圆点在 `ControlTheme` 的 `PART_Dot` 里。
/// </remarks>
public sealed class ModuleStatusLight : ModuleElement
{
    private Avalonia.Controls.Shapes.Ellipse? _dot;
    private string? _token;

    /// <inheritdoc />
    protected override void OnApplyTemplate(TemplateAppliedEventArgs e)
    {
        base.OnApplyTemplate(e);

        _dot = e.NameScope.Find<Avalonia.Controls.Shapes.Ellipse>("PART_Dot");
        ApplyRole(_token);
    }

    /// <inheritdoc />
    protected internal override void ApplyAppearance(ViewAppearance appearance)
    {
        ArgumentNullException.ThrowIfNull(appearance);

        // 灯体只认**角色词解析后的 token**（它是相对色：换主题、用户改配色都跟着走）；
        // 绝对色那一档对灯没有意义（灯表达的就是"哪一档语义"），故不接管。
        _token = appearance.OutlineToken;
        ApplyRole(_token);
    }

    /// <summary>把 token 落成灯体底色（<see langword="null"/> ⇒ 空闲档）。</summary>
    /// <param name="token">主题 token 名。</param>
    private void ApplyRole(string? token)
    {
        if (_dot is null)
        {
            return;
        }

        var effective = token is { Length: > 0 } ? token : "SemanticIdle";

        _dot[!Avalonia.Controls.Shapes.Shape.FillProperty] =
            new Avalonia.Markup.Xaml.MarkupExtensions.DynamicResourceExtension(effective);
    }
}
