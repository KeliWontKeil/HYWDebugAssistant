using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Shapes;
using Avalonia.Markup.Xaml.MarkupExtensions;
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
/// **一块会变的文字**：绑一条显示源，或者一句静态文字。
/// </summary>
/// <remarks>
/// <para>
/// 标签是**独立元素**，不寄生在别的控件里：它有自己的矩形、自己的对齐、自己的省略号。
/// 视图要在哪儿显示标签，就在哪儿摆一个本控件 —— 字段标题、读数标题、区标题、状态、单位都是它。
/// </para>
/// <para>
/// **文案来源优先级：<see cref="Field"/> &gt; <see cref="ModuleElement.Source"/> &gt; <see cref="Text"/>**，视图只用其中一条：
/// 配置字段的标题不复制到视图里（<see cref="Field"/> 读配置元数据）；动态一句话走 <see cref="ModuleElement.Source"/>；
/// 固定一句话走 <see cref="Text"/>。
/// </para>
/// <para>
/// <b>单位不是特殊概念</b>：`ms` / `条` / `个` 就是一个标签，摆在该摆的地方。
/// </para>
/// <para>
/// 视觉结构在 `Sdk.Ui/Themes/Controls.axaml`；排印档位由视图给的类决定（`caption` / `hint` / 默认）。
/// </para>
/// </remarks>
public sealed class ModuleLabel : ModuleElement
{
    private TextBlock? _text;
    private Rectangle? _placeholder;
    private IModuleViewContext? _context;
    private string _textValue = string.Empty;
    private string _field = string.Empty;
    private string? _tip;
    private long _textRevision = -1;

    /// <summary>造一块标签。</summary>
    public ModuleLabel()
    {
        // 预览器没有宿主：配置字段的标题从视图 DataContext（设计时上下文）取，
        // 与运行时走同一个 <see cref="IModuleViewContext.ReadField"/>，文案仍只有一份。
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

    /// <summary>不绑源时的静态文字（与 <see cref="ModuleElement.Source"/> / <see cref="Field"/> 互斥）。</summary>
    public string Text
    {
        get => _textValue;
        set
        {
            if (string.Equals(_textValue, value, StringComparison.Ordinal))
            {
                return;
            }

            _textValue = value;
            Apply(value);
        }
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
    protected override void OnApplyTemplate(TemplateAppliedEventArgs e)
    {
        base.OnApplyTemplate(e);

        _text = e.NameScope.Find<TextBlock>("PART_Text");
        _placeholder = e.NameScope.Find<Rectangle>("PART_Placeholder");

        if (_field.Length > 0)
        {
            ApplyFieldText();
        }
        else
        {
            Apply(_textValue);
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

        if (Source.Length == 0 || _text is null)
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
        if (!string.Equals(_text.Text, text, StringComparison.Ordinal))
        {
            _textValue = text;
            Apply(text);
        }
    }
    /// <summary>运行期文字外观落到真正显示文字的 <see cref="TextBlock"/> 上。</summary>
    /// <param name="appearance">外观。</param>
    protected internal override void ApplyAppearance(ViewAppearance appearance)
    {
        ArgumentNullException.ThrowIfNull(appearance);

        if (_text is null)
        {
            return;
        }

        if (appearance.Text is { } text)
        {
            _text.Foreground = new SolidColorBrush(Color.FromUInt32(text));
        }
        else
        {
            SetTokenBrush(_text, TextBlock.ForegroundProperty, appearance.TextToken);
        }

        if (appearance.TextSize is { } size)
        {
            _text.FontSize = size;
        }
        else
        {
            _text.ClearValue(TextBlock.FontSizeProperty);
        }
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

        Apply(title, tip);
    }

    /// <summary>把一段文字与提示落到视觉上（空文案 ⇒ 预览里给一个占位框）。</summary>
    /// <param name="shown">要显示的文字。</param>
    /// <param name="tip">悬停提示（空 = 不挂）。</param>
    private void Apply(string shown, string? tip = null)
    {
        tip ??= NullIfEmpty(shown);

        if (_text is not null && !string.Equals(_text.Text, shown, StringComparison.Ordinal))
        {
            _text.Text = shown;
        }

        if (_text is not null)
        {
            // 提示只在真的变了才写：字段标签每帧都会重新读一次元数据。
            if (!string.Equals(_tip, tip, StringComparison.Ordinal))
            {
                _tip = tip;
                ToolTip.SetTip(this, tip);
            }
        }

        // 预览里"完全透明/没有东西"的标签也要有个框，否则没法排版。
        if (_placeholder is not null)
        {
            _placeholder.IsVisible = ModuleView.IsDesignEnvironment && shown.Length == 0;
        }
    }

    /// <summary>找上下文：运行时的宿主祖先优先；预览器/测试由 DataContext 直接提供。</summary>
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
/// 读数**不自带标题**：标题是一个独立的 <see cref="ModuleLabel"/>，摆在哪由视图决定 ——
/// 这样标题和值才能各自对齐，而不是被控件的内部 `*` 列挤出一个空洞。
/// </para>
/// <para>
/// **单位不是控件的属性**：静态单位（`条` / `个`）就是视图里的另一个标签；
/// 字节计数的 B/KB/MB 是"值文本"的一部分（<see cref="ByteQuantity"/> 唯一实现），不单独成块、不单独设色。
/// </para>
/// </remarks>
public sealed class ModuleNumeric : ModuleElement
{
    private TextBlock? _value;
    private Rectangle? _placeholder;

    /// <summary>数量种类（默认 <see cref="ModuleQuantity.None"/> = 原样显示）。</summary>
    public ModuleQuantity Quantity { get; set; }

    /// <inheritdoc />
    protected override void OnApplyTemplate(TemplateAppliedEventArgs e)
    {
        base.OnApplyTemplate(e);

        _value = e.NameScope.Find<TextBlock>("PART_Value");
        _placeholder = e.NameScope.Find<Rectangle>("PART_Placeholder");
        UpdatePlaceholder();
    }

    /// <inheritdoc />
    public override void Refresh(IModuleViewContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        if (Source.Length == 0 || _value is null)
        {
            return;
        }

        var latest = context.ReadLatest(Source);

        var text = latest is { } number
            ? Format(number)
            : "-";

        if (!string.Equals(_value.Text, text, StringComparison.Ordinal))
        {
            _value.Text = text;
        }

        UpdatePlaceholder();
    }

    /// <summary>运行期文字外观落到**值那一块**（读数是数据，跟着外观通道走）。</summary>
    /// <param name="appearance">外观。</param>
    protected internal override void ApplyAppearance(ViewAppearance appearance)
    {
        ArgumentNullException.ThrowIfNull(appearance);

        if (_value is null)
        {
            return;
        }

        if (appearance.Text is { } text)
        {
            _value.Foreground = new SolidColorBrush(Color.FromUInt32(text));
        }
        else
        {
            SetTokenBrush(_value, TextBlock.ForegroundProperty, appearance.TextToken);
        }

        if (appearance.TextSize is { } size)
        {
            _value.FontSize = size;
        }
        else
        {
            _value.ClearValue(TextBlock.FontSizeProperty);
        }
    }

    /// <summary>值的显示文本（字节数量含单位；其余按当前文化显示小数）。</summary>
    /// <param name="number">值。</param>
    private string Format(double number) => Quantity == ModuleQuantity.Bytes
        ? ByteQuantity.Format(number)
        : number.ToString("0.###", System.Globalization.CultureInfo.CurrentCulture);

    /// <summary>预览里没有值时给一个占位框（产品运行不显示）。</summary>
    private void UpdatePlaceholder()
    {
        if (_placeholder is not null)
        {
            _placeholder.IsVisible = ModuleView.IsDesignEnvironment && string.IsNullOrEmpty(_value?.Text);
        }
    }
}

/// <summary>
/// **状态灯**：一个圆点，底色由模块设的**角色词**决定（框架不认识"红 = 失败"）。
/// </summary>
/// <remarks>
/// 没设过角色 ⇒ 空闲档。角色词 → token 的解析在宿主（`PaletteManifest`），
/// 控件只接收 token；用户改主题色时它跟着变（走 <c>DynamicResource</c> 绑定，不是写死的色值）。
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

        _dot[!Avalonia.Controls.Shapes.Shape.FillProperty] = new DynamicResourceExtension(effective);
    }
}
