using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.VisualTree;
using HYWDebugAssistant.Sdk.Text;
using HYWDebugAssistant.Sdk.Views;

namespace HYWDebugAssistant.Sdk.Ui.Controls;

/// <summary>文本面的用途：输入一条输入源，或显示一条显示源。</summary>
public enum TextSurfaceMode
{
    /// <summary>输入源：可编辑、可提交，或按只读属性显示为只读。</summary>
    Input,

    /// <summary>显示源：日志/结果，强制只读，跟随尾部。</summary>
    Display,
}

/// <summary>
/// **模块文本面：一个控件，两种呈现叶子**。
/// </summary>
/// <remarks>
/// <para>
/// 可编辑面用 <see cref="TextBox"/>：它才有光标、编辑选择和提交时机。
/// 只读面用 <see cref="IncrementalTextView"/>：文本仍可跨行选中/复制，但没有编辑器光标；
/// 它按逻辑行/视觉行增量布局，追加不会重排旧内容，只绘制可见行。两种叶子共用同一个模板、
/// 边框、错误圈与刷新路径。
/// </para>
/// <para>
/// **声明只读才换叶子**：`Mode=Display` 或 `IsReadOnly=true` 用只读叶子显示；
/// **运行期只读**（外观通道的 `ReadOnly`）只停止编辑 —— 输入叶子保持可见、边框不变、
/// 仍可用鼠标选中复制（只是没有光标与提交）。
/// </para>
/// <para>
/// **失焦不提交**：编辑内容逐键写进视图状态，失焦只是"编辑结束"；提交只由 Enter（单行）/
/// Ctrl+Enter（多行）或显式 <see cref="Commit"/> 触发 —— 否则"填完点别处"会变成一次发送。
/// </para>
/// <para>
/// **程序写入 ≠ 用户编辑**：给 <see cref="Text"/> 赋值（视图刷新/模块回写）只更新叶子显示，
/// 不写视图状态；只有用户真的改字才写 `ViewState`。Avalonia 12 的 `TextBox.TextChanged`
/// 由 `Dispatcher.Post` **异步投递**，一次性抑制标志盖不住它 —— 程序写入后本面已知文本
/// 已经前移，迟到的回调读到相同文本即可判定不是用户编辑。
/// </para>
/// </remarks>
public sealed class ModuleTextSurface : ModuleElement
{
    private TextBox? _input;
    private IncrementalTextView? _readText;
    private ScrollViewer? _readScroll;
    private Border? _frame;
    private Border? _focusRing;
    private Border? _errorRing;
    private NewlineMarkOverlay? _marks;
    private IModuleViewContext? _context;
    private ViewAppearance _appearance = ViewAppearance.None;
    private TextSurfaceMode _mode = TextSurfaceMode.Input;
    private bool _isReadOnly;
    private bool _appearanceReadOnly;
    private bool _multiline;
    private bool _showNewlineMarks = true;
    private bool _restored;
    private bool _userEdited;
    private bool _followTail;
    private string _textValue = string.Empty;
    private string _unmarkedKey = string.Empty;
    private long _displayRevision = -1;
    private TextCursor _cursor;
    private readonly List<string> _sealedScratch = [];
    private bool _appearanceApplied;

    /// <summary>单帧最多为新增文本付出多少字符的排版代价；超过则跳读尾部窗口。</summary>
    private const int MaxLayoutCharsPerFrame = 8_192;

    /// <summary>跳读时保留的尾部行数（只用于显示；保存仍以显示缓冲为准）。</summary>
    private const int TailLineLimit = 512;
    private TextAlignment _textAlignment = TextAlignment.Left;
    private double _minEditorHeight;

    /// <summary>造一个文本面（视觉部件在模板应用时取得）。</summary>
    public ModuleTextSurface()
    {
        AttachedToVisualTree += (_, _) =>
        {
            ResolveContext();
            Restore();
        };
    }

    /// <summary>输入源 / 显示源模式。</summary>
    public TextSurfaceMode Mode
    {
        get => _mode;
        set
        {
            if (_mode == value)
            {
                return;
            }

            _mode = value;
            _followTail = value == TextSurfaceMode.Display;
            _displayRevision = -1;
            ApplyPresentation();
        }
    }

    /// <summary>静态只读（运行期外观通道可再覆盖为只读）。</summary>
    public bool IsReadOnly
    {
        get => _isReadOnly;
        set
        {
            if (_isReadOnly == value)
            {
                return;
            }

            _isReadOnly = value;
            ApplyPresentation();
        }
    }

    /// <summary>当前文本（只读叶子返回文本层的内容，编辑叶子返回编辑器内容）。</summary>
    public string Text
    {
        get => DeclaredReadOnly
            ? _readText?.Text ?? _textValue
            : _input?.Text ?? _textValue;
        set
        {
            _textValue = value ?? string.Empty;
            SetSurfaceText(_textValue, null);
        }
    }

    /// <summary>是否多行（只影响编辑叶子；显示叶子始终折行）。</summary>
    public bool Multiline
    {
        get => _multiline;
        set
        {
            if (_multiline == value)
            {
                return;
            }

            _multiline = value;
            ApplyMultiline();
        }
    }

    /// <summary>文本水平对齐。</summary>
    public TextAlignment TextAlignment
    {
        get => _textAlignment;
        set
        {
            _textAlignment = value;
            ApplyTextAlignment();
        }
    }

    /// <summary>是否画真实换行字符的 `↵`。</summary>
    public bool ShowNewlineMarks
    {
        get => _showNewlineMarks;
        set
        {
            _showNewlineMarks = value;
            ApplyMarks();
        }
    }

    /// <summary>编辑叶子的最小高度（显示叶子不受此限制）。</summary>
    public double MinEditorHeight
    {
        get => _minEditorHeight;
        set
        {
            _minEditorHeight = value;
            ApplyMinHeight();
        }
    }

    private bool IsReadOnlyEffective =>
        _mode == TextSurfaceMode.Display || _isReadOnly || _appearanceReadOnly;

    /// <summary>声明期只读（`Display` 或 `IsReadOnly`）：决定用哪个叶子；运行期只读不换叶子。</summary>
    private bool DeclaredReadOnly => _mode == TextSurfaceMode.Display || _isReadOnly;

    /// <inheritdoc />
    protected override void OnApplyTemplate(TemplateAppliedEventArgs e)
    {
        base.OnApplyTemplate(e);

        UnhookInput();

        _input = e.NameScope.Find<TextBox>("PART_Input");
        _readText = e.NameScope.Find<IncrementalTextView>("PART_ReadText");
        _readScroll = e.NameScope.Find<ScrollViewer>("PART_ReadScroll");
        _frame = e.NameScope.Find<Border>("PART_Frame");
        _focusRing = e.NameScope.Find<Border>("PART_FocusRing");
        _errorRing = e.NameScope.Find<Border>("PART_ErrorRing");
        _marks = e.NameScope.Find<NewlineMarkOverlay>("PART_Marks");
        HookInput();

        _displayRevision = -1;

        // 模板重建 ⇒ 只读叶子是新的（里面没有内容）：游标必须退回起点，
        // 否则增量会"从内容末尾之后开始读"，新叶子永远是空的。
        _cursor = TextCursor.Empty;
        _appearanceApplied = false;
        ApplyPresentation();
        ApplyAppearance(_appearance);
        ApplyMultiline();
        ApplyTextAlignment();
        ApplyMinHeight();
        ApplyMarks();

        if (_textValue.Length > 0)
        {
            SetSurfaceText(_textValue, null);
        }

        Restore();
    }

    /// <inheritdoc />
    public override void Refresh(IModuleViewContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        _context = context;

        if (Source.Length == 0)
        {
            return;
        }

        if (Mode == TextSurfaceMode.Display)
        {
            RefreshDisplay(context);
            return;
        }

        Restore();
    }

    /// <summary>把当前编辑文本提交给模块（仅可编辑 Input 面；**失焦不会调用它**，只由 Enter / 显式调用触发）。</summary>
    public void Commit()
    {
        if (_context is null || Source.Length == 0 || _input is null || IsReadOnlyEffective)
        {
            return;
        }

        _ = _context.SubmitInput(Source, _input.Text ?? string.Empty);
    }

    /// <inheritdoc />
    protected internal override void ApplyAppearance(ViewAppearance appearance)
    {
        ArgumentNullException.ThrowIfNull(appearance);

        // 框架每帧都会把外观下发一次；值没变时不能重复写 Foreground/FontSize ——
        // 增量显示控件对这两个属性的变化会重建视觉行（每帧重建等于白做增量）。
        if (_appearanceApplied && _appearance.Equals(appearance) && _input is not null && _readText is not null)
        {
            return;
        }

        _appearance = appearance;
        _appearanceApplied = true;

        if (_input is null && _readText is null)
        {
            return;
        }

        ApplyTextAppearance(_input, appearance, isTextBox: true);
        ApplyTextAppearance(_readText, appearance, isTextBox: false);
        ApplyErrorRing(appearance);
        ApplyReadOnly(appearance.ReadOnly is true);
    }

    /// <inheritdoc />
    protected override void ApplyReadOnly(bool isReadOnly)
    {
        _appearanceReadOnly = isReadOnly;
        ApplyPresentation();
    }

    private void HookInput()
    {
        if (_input is null)
        {
            return;
        }

        _input.TextChanged += OnInputTextChanged;
        _input.LostFocus += OnInputLostFocus;
        _input.GotFocus += OnInputGotFocus;
        _input.KeyDown += OnInputKeyDown;
    }

    private void UnhookInput()
    {
        if (_input is null)
        {
            return;
        }

        _input.TextChanged -= OnInputTextChanged;
        _input.LostFocus -= OnInputLostFocus;
        _input.GotFocus -= OnInputGotFocus;
        _input.KeyDown -= OnInputKeyDown;
    }

    private void ApplyPresentation()
    {
        if (_input is null || _readText is null)
        {
            return;
        }

        var declaredReadOnly = DeclaredReadOnly;
        var effectiveReadOnly = declaredReadOnly || _appearanceReadOnly;

        _input.IsVisible = !declaredReadOnly;

        if (_readScroll is not null)
        {
            _readScroll.IsVisible = declaredReadOnly;
            _readScroll.Focusable = false;
        }

        _readText.IsVisible = declaredReadOnly;

        _input.IsReadOnly = effectiveReadOnly;
        _input.Focusable = !declaredReadOnly;
        _input.IsTabStop = !declaredReadOnly;

        // 只读面没有"改"这回事 ⇒ 不需要撤销栈（它按整段文本记状态，长的只读文本没有必要为它留内存）。
        _input.IsUndoEnabled = !effectiveReadOnly;

        if (declaredReadOnly)
        {
            _input.CaretBrush = Brushes.Transparent;
            _input.SelectionBrush = Brushes.Transparent;
            _input.SelectionForegroundBrush = Brushes.Transparent;
            _input.SelectionStart = 0;
            _input.SelectionEnd = 0;

            if (_focusRing is not null)
            {
                _focusRing.IsVisible = false;
            }
        }
        else
        {
            // 输入叶子可见：运行期只读只藏光标，选择色保留（用户仍要选中复制）。
            _input.ClearValue(TextBox.SelectionBrushProperty);
            _input.ClearValue(TextBox.SelectionForegroundBrushProperty);

            if (effectiveReadOnly)
            {
                _input.CaretBrush = Brushes.Transparent;
            }
            else
            {
                _input.ClearValue(TextBox.CaretBrushProperty);
            }

            if (_focusRing is not null)
            {
                _focusRing.IsVisible = _input.IsFocused && !effectiveReadOnly;
            }
        }

        ApplyMarks();
        SyncActiveText();
    }

    private void SyncActiveText()
    {
        if (DeclaredReadOnly)
        {
            // 显示面由 `RefreshDisplay` 从显示缓冲增量喂入；这里**不能**用 `_textValue` 回写，
            // 否则会在每次模板/外观变化时把日志清空。
            if (_mode == TextSurfaceMode.Display)
            {
                return;
            }

            if (_readText is not null && !string.Equals(_readText.Text, _textValue, StringComparison.Ordinal))
            {
                _readText.SetText(_textValue);
            }

            return;
        }

        if (_input is not null && !string.Equals(_input.Text, _textValue, StringComparison.Ordinal))
        {
            _input.Text = _textValue;
        }
    }

    private void SetSurfaceText(string text, IReadOnlyCollection<int>? unmarked)
    {
        _textValue = text;

        if (DeclaredReadOnly)
        {
            if (_readText is not null && !string.Equals(_readText.Text, text, StringComparison.Ordinal))
            {
                _readText.SetText(text);

                if (_followTail)
                {
                    _readScroll?.ScrollToEnd();
                }
            }
        }
        else if (_input is not null && !string.Equals(_input.Text, text, StringComparison.Ordinal))
        {
            _input.Text = text;
        }

        SetUnmarked(unmarked);
        _marks?.InvalidateVisual();
    }

    private void SetUnmarked(IReadOnlyCollection<int>? unmarked)
    {
        var key = unmarked is { Count: > 0 }
            ? string.Join(",", unmarked)
            : string.Empty;

        if (string.Equals(key, _unmarkedKey, StringComparison.Ordinal))
        {
            return;
        }

        _unmarkedKey = key;

        if (_marks is not null)
        {
            _marks.UnmarkedNewlines = unmarked;
        }
    }

    private void OnInputTextChanged(object? sender, TextChangedEventArgs e)
    {
        if (_context is null || Source.Length == 0 || _mode != TextSurfaceMode.Input || IsReadOnlyEffective)
        {
            return;
        }

        var text = _input?.Text ?? string.Empty;
        if (string.Equals(text, _textValue, StringComparison.Ordinal))
        {
            // Avalonia 12 的 TextChanged 走 `Dispatcher.Post`，程序写入的回调在抑制窗口之后才到。
            // 本面已知文本已由 SetSurfaceText/SyncActiveText 前移：相同 ⇒ 这是程序回显，不是用户编辑。
            return;
        }

        _textValue = text;
        _userEdited = true;
        _context.WriteInputText(Source, text);
    }

    private void OnInputLostFocus(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (_focusRing is not null)
        {
            _focusRing.IsVisible = false;
        }

        // **失焦 = 编辑结束，不是动作**：不提交。
        // 内容已由 OnInputTextChanged 逐键写进视图状态，模块每拍都读得到；提交只属于 Enter / 显式调用。
    }

    private void OnInputGotFocus(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (_focusRing is not null && !IsReadOnlyEffective)
        {
            _focusRing.IsVisible = true;
        }
    }

    private void OnInputKeyDown(object? sender, KeyEventArgs args)
    {
        if (IsReadOnlyEffective || args.Key != Key.Enter || _input is null)
        {
            return;
        }

        if (!Multiline)
        {
            args.Handled = true;
            Commit();
            return;
        }

        var submitKey = _context?.MultilineSubmitKey ?? "CtrlEnter";

        if (string.Equals(submitKey, "None", StringComparison.Ordinal))
        {
            return;
        }

        if (string.Equals(submitKey, "Enter", StringComparison.Ordinal))
        {
            if ((args.KeyModifiers & KeyModifiers.Shift) == 0)
            {
                args.Handled = true;
                Commit();
            }

            return;
        }

        if (string.Equals(submitKey, "CtrlEnter", StringComparison.Ordinal)
            && (args.KeyModifiers & KeyModifiers.Control) != 0)
        {
            args.Handled = true;
            Commit();
        }
    }

    private void Restore()
    {
        if (_restored || _context is null || Source.Length == 0 || _mode != TextSurfaceMode.Input)
        {
            return;
        }

        _restored = true;

        // 用户真的改过字 ⇒ 本地意图优先，不被视图状态倒灌。
        if (_userEdited)
        {
            return;
        }

        // 运行期状态（包括显式空串）覆盖 XAML 的初始/预览值；没有状态才保留声明值。
        var text = _context.ReadInputText(Source);

        if (text is null)
        {
            return;
        }

        SetSurfaceText(text, null);
    }

    private void RefreshDisplay(IModuleViewContext context)
    {
        if (_readText is null)
        {
            return;
        }

        // **窗口路径**：只取游标之后新增的已封口行与当前行的后缀/整行，
        // 不复制行表、不拼全文 —— 长缓冲下每帧的代价与"这一拍来了多少数据"成正比。
        if (!context.TryReadTextWindow(Source, _cursor, _sealedScratch, MaxLayoutCharsPerFrame, out var window))
        {
            // 未实现窗口的宿主/设计期替身：回退全量（语义不变，只是每帧多一次行表复制）。
            var snapshot = context.ReadText(Source);

            if (snapshot.Revision == _displayRevision)
            {
                return;
            }

            _displayRevision = snapshot.Revision;
            ApplyDisplayPreferences();
            _ = _readText.ApplySnapshot(snapshot.Lines, snapshot.Revision);
            return;
        }

        _cursor = new TextCursor(
            window.Version, window.Dropped, window.SealedCount, window.OpenLength, window.OpenVersion);

        // **同一个变更号 ⇒ 内容没变**：不读行、不重排。
        if (window.Revision == _displayRevision)
        {
            return;
        }

        // **呈现预算**：缓冲在复制之前已判定本拍超出预算 ⇒ 只读尾部窗口。
        if (window.Overflow)
        {
            if (context.TryReadTextTail(Source, TailLineLimit, MaxLayoutCharsPerFrame, out var tail))
            {
                _displayRevision = tail.Snapshot.Revision;
                ApplyDisplayPreferences();
                _ = _readText.ApplySnapshot(tail.Snapshot.Lines, tail.Snapshot.Revision);
                _readText.SetCursor(tail.Cursor);
                _cursor = tail.Cursor;
                _sealedScratch.Clear();
                return;
            }

            var full = context.ReadText(Source);
            _displayRevision = full.Revision;
            ApplyDisplayPreferences();
            _ = _readText.ApplySnapshot(full.Lines, full.Revision);
            _sealedScratch.Clear();
            return;
        }

        _displayRevision = window.Revision;
        ApplyDisplayPreferences();

        if (!_readText.ApplyWindow(window, _sealedScratch))
        {
            var snapshot = context.ReadText(Source);
            _displayRevision = snapshot.Revision;
            _ = _readText.ApplySnapshot(snapshot.Lines, snapshot.Revision);
            _readText.SetCursor(_cursor);
            _sealedScratch.Clear();
        }
    }

    /// <summary>把"是否画 ↵ / 是否跟随尾部 / 对齐"下发到只读叶子（同值写入无副作用）。</summary>
    private void ApplyDisplayPreferences()
    {
        if (_readText is null)
        {
            return;
        }

        _readText.ShowNewlineMarks = _showNewlineMarks;
        _readText.AutoScroll = _followTail;
        _readText.TextAlignment = _textAlignment;
    }


    private void ApplyMultiline()
    {
        if (_input is null)
        {
            return;
        }

        _input.AcceptsReturn = _multiline;
        _input.TextWrapping = _multiline ? TextWrapping.Wrap : TextWrapping.NoWrap;
        _input.VerticalContentAlignment = _multiline ? VerticalAlignment.Top : VerticalAlignment.Center;
        ApplyMarks();
    }

    private void ApplyTextAlignment()
    {
        if (_input is not null)
        {
            _input.TextAlignment = _textAlignment;
        }

        if (_readText is not null)
        {
            _readText.TextAlignment = _textAlignment;
        }
    }

    private void ApplyMinHeight()
    {
        if (_input is null)
        {
            return;
        }

        _input.Height = double.NaN;
        _input.MinHeight = _minEditorHeight > 0 ? _minEditorHeight : 0;
    }

    private void ApplyMarks()
    {
        if (_readText is not null)
        {
            _readText.ShowNewlineMarks = _showNewlineMarks;
        }

        if (_marks is null)
        {
            return;
        }

        // 只读面由增量控件自己画 `↵`；覆盖层只服务可编辑 TextBox（输入面的真实换行）。
        _marks.Target = IsReadOnlyEffective ? null : _input;
        _marks.IsActive = _showNewlineMarks
            && !IsReadOnlyEffective
            && _multiline;
    }

    private static void ApplyTextAppearance(Control? target, ViewAppearance appearance, bool isTextBox)
    {
        if (target is null)
        {
            return;
        }

        var templated = isTextBox || target is IncrementalTextView;
        var foreground = templated
            ? TemplatedControl.ForegroundProperty
            : TextBlock.ForegroundProperty;
        var fontSize = templated
            ? TemplatedControl.FontSizeProperty
            : TextBlock.FontSizeProperty;

        if (appearance.Text is { } text)
        {
            target.SetValue(foreground, new SolidColorBrush(Color.FromUInt32(text)));
        }
        else
        {
            SetTokenBrush(target, foreground, appearance.TextToken);
        }

        if (appearance.TextSize is { } size)
        {
            target.SetValue(fontSize, size);
        }
        else
        {
            target.ClearValue(fontSize);
        }
    }

    private void ApplyErrorRing(ViewAppearance appearance)
    {
        if (_errorRing is null)
        {
            return;
        }

        if (appearance.Outline is { } outline)
        {
            _errorRing.BorderBrush = new SolidColorBrush(Color.FromUInt32(outline));
            _errorRing.IsVisible = true;
        }
        else if (appearance.OutlineToken is { Length: > 0 } token)
        {
            SetTokenBrush(_errorRing, Border.BorderBrushProperty, token);
            _errorRing.IsVisible = true;
        }
        else
        {
            _errorRing.ClearValue(Border.BorderBrushProperty);
            _errorRing.IsVisible = false;
        }
    }

    private void ResolveContext()
    {
        if (_context is null && this.FindAncestorOfType<ModuleViewHost>() is { } host)
        {
            _context = host.Context;
        }
    }
}
