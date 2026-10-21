using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Markup.Xaml.MarkupExtensions;
using Avalonia.Media;

namespace HYWDebugAssistant.Sdk.Ui.Controls;

/// <summary>
/// **一块会变的文字**：绑一条显示源，显示它当前的内容。
/// </summary>
/// <remarks>
/// <b>读法与只读文本面同一条显示缓冲</b>：一个源在"一行字"与"一块日志"上表现一致，
/// 免得模块作者为同一份数据准备两种写法。
/// <para>
/// 视觉结构在 `Sdk.Ui/Themes/Controls.axaml` 的 `ControlTheme` 里（`PART_Text`）；
/// 这里只负责"把哪段字放进去"与运行期外观。
/// </para>
/// </remarks>
public sealed class ModuleLabel : ModuleElement
{
    private TextBlock? _text;
    private string _textValue = string.Empty;
    private bool _muted;

    /// <summary>不绑源时的静态文字（绑了源就以源为准）。</summary>
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

            if (_text is not null)
            {
                _text.Text = value;
            }
        }
    }

    /// <summary>弱化显示（次要说明）。</summary>
    public bool Muted
    {
        get => _muted;
        set
        {
            if (_muted == value)
            {
                return;
            }

            _muted = value;
            UpdateMuted();
        }
    }

    /// <inheritdoc />
    protected override void OnApplyTemplate(TemplateAppliedEventArgs e)
    {
        base.OnApplyTemplate(e);

        _text = e.NameScope.Find<TextBlock>("PART_Text");

        if (_text is not null)
        {
            _text.Text = _textValue;
            UpdateMuted();
        }
    }

    /// <inheritdoc />
    public override void Refresh(IModuleViewContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        if (Source.Length == 0 || _text is null)
        {
            return;
        }

        var lines = context.ReadText(Source).Lines;
        var text = lines.Count == 0 ? string.Empty : string.Join(Environment.NewLine, lines);

        // **内容没变就一个字都不动**：赋 Text 会让布局失效，静止的文字每帧白排一次。
        if (!string.Equals(_text.Text, text, StringComparison.Ordinal))
        {
            _text.Text = text;
            _textValue = text;
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

    private void UpdateMuted()
    {
        if (_text is null)
        {
            return;
        }

        if (_muted)
        {
            _text.Classes.Add("muted");
        }
        else
        {
            _text.Classes.Remove("muted");
        }
    }
}

/// <summary>
/// **数值读数**：一行"标题 + 值 + 单位"，标题可选。
/// </summary>
/// <remarks>
/// 单位是**独立的一块小字**（与读数分开）：两者字号不同、墨色不同、对齐方式不同 ——
/// 拼进同一个 <see cref="TextBlock"/> 时单位只能跟着读数一起放大。
/// 视觉结构在 `ControlTheme` 的 `PART_Title` / `PART_Value` / `PART_Unit` 里。
/// </remarks>
public sealed class ModuleNumeric : ModuleElement
{
    private TextBlock? _value;
    private TextBlock? _unit;
    private TextBlock? _title;
    private string _unitText = string.Empty;
    private string _titleText = string.Empty;

    /// <summary>单位文本（空 = 不显示）。</summary>
    public string Unit
    {
        get => _unitText;
        set
        {
            if (string.Equals(_unitText, value, StringComparison.Ordinal))
            {
                return;
            }

            _unitText = value;
            ApplyUnit();
        }
    }

    /// <summary>放在读数**之前**的一行说明（空 = 不显示）。</summary>
    /// <remarks>
    /// 用简单的"标题在左"排法：模块需要更复杂的排布时，自己在视图里摆一块 <see cref="ModuleLabel"/>。
    /// </remarks>
    public string Title
    {
        get => _titleText;
        set
        {
            if (string.Equals(_titleText, value, StringComparison.Ordinal))
            {
                return;
            }

            _titleText = value;
            ApplyTitle();
        }
    }

    /// <inheritdoc />
    protected override void OnApplyTemplate(TemplateAppliedEventArgs e)
    {
        base.OnApplyTemplate(e);

        _value = e.NameScope.Find<TextBlock>("PART_Value");
        _unit = e.NameScope.Find<TextBlock>("PART_Unit");
        _title = e.NameScope.Find<TextBlock>("PART_Title");

        ApplyTitle();
        ApplyUnit();
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
            ? number.ToString("0.###", System.Globalization.CultureInfo.CurrentCulture)
            : "-";

        if (!string.Equals(_value.Text, text, StringComparison.Ordinal))
        {
            _value.Text = text;
        }
    }

    /// <summary>运行期文字外观落到**读数那一块**（标题/单位是说明，不跟着变号）。</summary>
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

    private void ApplyTitle()
    {
        if (_title is null)
        {
            return;
        }

        _title.Text = _titleText;
        _title.IsVisible = _titleText.Length > 0;
    }

    private void ApplyUnit()
    {
        if (_unit is null)
        {
            return;
        }

        _unit.Text = _unitText;
        _unit.IsVisible = _unitText.Length > 0;
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
