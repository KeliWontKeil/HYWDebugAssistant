using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.VisualTree;
using HYWDebugAssistant.Sdk.Text;
using HYWDebugAssistant.Sdk.Values;
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
/// **提交是控件行为**：<see cref="CommitMode"/> 为 <see cref="ValueCommitMode.OnChange"/> 时逐键交值，
/// 为 <see cref="ValueCommitMode.OnExplicit"/> 时只由 Enter（单行）/ Ctrl+Enter（多行）或显式
/// <see cref="Commit"/> 交值；<see cref="ValueCommitMode.OnExplicit"/> 在失焦时也提交。
/// </para>
/// <para>
/// **控件不碰后端**：值从 <see cref="IModuleViewContext.ReadValue"/> 来，用户修改经
/// <see cref="IModuleViewContext.CommitValue"/> 交出去；控件不知道值落在配置还是视图状态里，
/// 也不判断错误。给 <see cref="Text"/> 赋值只更新显示。
/// </para>
/// </remarks>
public sealed class ModuleTextSurface : ModuleElement
{
    /// <summary>不可显示字符显示为警示色标记（true）还是方框占位（false）。</summary>
    public static readonly StyledProperty<bool> ShowControlCharacterMarkersProperty =
        AvaloniaProperty.Register<ModuleTextSurface, bool>(nameof(ShowControlCharacterMarkers), defaultValue: true);

    /// <summary>该输入面按 C 字符串转义输入：识别并着色合法转义组（不改变值）。</summary>
    public static readonly StyledProperty<bool> EscapeInputProperty =
        AvaloniaProperty.Register<ModuleTextSurface, bool>(nameof(EscapeInput));

    private TextBox? _input;
    private ProjectedTextEditing? _editing;
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
    private bool _valueRegistered;
    private string _committedValue = string.Empty;
    private NewlineMode? _lastNewlineMode;
    private object? _baseToolTip;
    private bool _noticeToolTipActive;
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
        AttachedToVisualTree += (_, _) => ResolveContext();
    }

    /// <summary>
    /// 绑定的**值名**（输入模式）。不可为空；显示模式用 <c>Source</c>。
    /// </summary>
    /// <remarks>控件不知道值最终存在哪里；它只读 <see cref="IModuleViewContext.ReadValue"/>。</remarks>
    public string Value { get; set; } = string.Empty;

    /// <summary>用户修改交出去的时机（仅值通道）。</summary>
    public ValueCommitMode CommitMode { get; set; } = ValueCommitMode.OnExplicit;

    /// <summary>
    /// **提交键的值名**（可选）：非空时从该值读取 <c>Enter</c> / <c>CtrlEnter</c> 档位。
    /// </summary>
    /// <remarks>
    /// 值未知/为空时回退到框架设置（<see cref="IModuleViewContext.MultilineSubmitKey"/>）。
    /// 模块因此可以持有自己的可覆盖档位，控件仍只认值名。
    /// </remarks>
    public string SubmitKeyValue { get; set; } = string.Empty;

    /// <summary>
    /// **回车提交时触发的命令名**（可选）：非空时按提交档位触发该命令。
    /// </summary>
    /// <remarks>
    /// 先交值再触发命令（模块的回读因此总能拿到最新编辑）；空 = 保持旧的"只交值"行为。
    /// </remarks>
    public string SubmitCommandName { get; set; } = string.Empty;

    /// <summary>
    /// **回车写入哪种换行**的值名（可选）：值取 <c>crlf</c> / <c>cr</c> / <c>lf</c> / <c>none</c>；
    /// 空/未知 ⇒ <c>crlf</c>。
    /// </summary>
    /// <remarks>
    /// 单行输入不处理换行；多行输入按 Enter 时由控件把这段文本插入光标处，
    /// 不再交给原生 TextBox 插入统一的 `\n`。
    /// </remarks>
    public string EnterNewlineValue { get; set; } = string.Empty;

    /// <summary>值通道用值名寻址外观；显示模式仍用显示源名。</summary>
    protected override string AppearanceKey => Value.Length > 0 ? Value : base.AppearanceKey;

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

    /// <summary>当前文本（只读叶子返回文本层的内容，编辑叶子返回原始值）。</summary>
    public string Text
    {
        get => DeclaredReadOnly
            ? _readText?.Text ?? _textValue
            : _editing?.RawText ?? _textValue;
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

    /// <summary>不可显示字符显示为标记（true）还是方框（false）。</summary>
    public bool ShowControlCharacterMarkers
    {
        get => GetValue(ShowControlCharacterMarkersProperty);
        set => SetValue(ShowControlCharacterMarkersProperty, value);
    }

    /// <summary>该输入面按 C 字符串转义输入：合法转义组画蓝色；值本身不变。</summary>
    public bool EscapeInput
    {
        get => GetValue(EscapeInputProperty);
        set => SetValue(EscapeInputProperty, value);
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
    }

    /// <inheritdoc />
    public override void Refresh(IModuleViewContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        _context = context;

        if (Mode == TextSurfaceMode.Display)
        {
            if (Source.Length > 0)
            {
                RefreshDisplay(context);
            }

            return;
        }

        if (Value.Length > 0)
        {
            RefreshValue(context);
        }
    }

    /// <summary>值通道的一帧：登记 XAML 初值、读有效值、把结论落到框外圈与悬停提示。</summary>
    private void RefreshValue(IModuleViewContext context)
    {
        if (_input is null)
        {
            return;
        }

        if (!_valueRegistered)
        {
            _valueRegistered = true;
            context.RegisterViewDefault(Value, Text);
        }

        var normalized = NormalizeNewlinesForChoice(context);
        var currentText = _editing?.RawText ?? string.Empty;

        var state = context.ReadValue(Value);

        if (normalized
            && !_input.IsKeyboardFocusWithin
            && !string.Equals(state.Value, currentText, StringComparison.Ordinal))
        {
            // 值层若还没回显这次归一，本帧先用输入框的新文本；下一帧再接受值层。
            ApplyValueNotice(state.Notice);
            return;
        }

        if (!state.IsKnown)
        {
            ApplyValueNotice(null);
            return;
        }

        // 焦点内且文本尚未被值层接受时，保留本地上次提交值；否则 Enter 后失焦会重复提交。
        if (!_input.IsKeyboardFocusWithin
            || string.Equals(currentText, state.Value, StringComparison.Ordinal))
        {
            _committedValue = state.Value;
        }

        // 用户正在输入时不覆盖本地意图；失焦后下一帧接受值层的有效值。
        if (!_input.IsKeyboardFocusWithin
            && !string.Equals(currentText, state.Value, StringComparison.Ordinal))
        {
            SetSurfaceText(state.Value, null);
        }
        ApplyValueNotice(state.Notice);
    }

    /// <summary>
    /// 换行档变化时，把当前输入框里的全部换行替换成目标换行（或删除）。
    /// </summary>
    /// <returns>本次是否改写了文本。</returns>
    /// <remarks>
    /// 只处理带 <see cref="EnterNewlineValue"/> 的多行可编辑面；单行、只读和没有换行档的输入不参与。
    /// 值首次出现时也归一一次，避免旧持久化内容继续混着三种换行。
    /// </remarks>
    private bool NormalizeNewlinesForChoice(IModuleViewContext context)
    {
        if (EnterNewlineValue.Length == 0
            || _input is null
            || _editing is null
            || !Multiline
            || IsReadOnlyEffective
            || Value.Length == 0)
        {
            return false;
        }

        var state = context.ReadValue(EnterNewlineValue);
        if (!state.IsKnown)
        {
            return false;
        }

        var mode = TextNewlines.ParseChoice(state.Value);
        if (_lastNewlineMode == mode)
        {
            return false;
        }

        _lastNewlineMode = mode;

        if (!_editing.NormalizeNewlines(mode))
        {
            return false;
        }

        // 投影/值同步是同步完成的；这里直接交值并前移本地基线，避免同一帧后面的
        // RefreshValue 用旧值把刚归一好的文本覆盖回去。
        var normalized = _editing.RawText;
        _context?.CommitValue(Value, normalized);
        _committedValue = normalized;
        _textValue = normalized;
        return true;
    }
    private void ApplyValueNotice(ValueNotice? notice)
    {
        // 外观通道先于本方法执行：Notice 为空时不清圈，否则模块刚设的红框会被本控件擦掉。
        if (notice is { Severity: ValueNoticeSeverity.Error or ValueNoticeSeverity.Warning }
            && _errorRing is not null)
        {
            SetTokenBrush(
                _errorRing,
                Border.BorderBrushProperty,
                notice.Severity == ValueNoticeSeverity.Error ? "SemanticError" : "SemanticWarn");
            _errorRing.IsVisible = true;
        }

        if (notice is { Message.Length: > 0 })
        {
            if (!_noticeToolTipActive)
            {
                _baseToolTip = ToolTip.GetTip(this);
                _noticeToolTipActive = true;
            }

            ToolTip.SetTip(this, notice.Message);
        }
        else if (_noticeToolTipActive)
        {
            ToolTip.SetTip(this, _baseToolTip);
            _noticeToolTipActive = false;
        }
    }

    /// <summary>把当前编辑文本提交给模块（仅可编辑 Input 面；Enter、显式调用或 OnExplicit 失焦触发）。</summary>
    public void Commit()
    {
        if (_context is null || _input is null || IsReadOnlyEffective)
        {
            return;
        }

        if (Value.Length == 0)
        {
            return;
        }

        var text = _editing?.RawText ?? string.Empty;
        _context.CommitValue(Value, text);
        _committedValue = text;
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

        _editing = new ProjectedTextEditing(_input);
        _editing.UserEdited += OnEditingUserEdited;
        _editing.ProjectionChanged += OnEditingProjectionChanged;
        _editing.SetMarkerMode(ShowControlCharacterMarkers);

        _input.LostFocus += OnInputLostFocus;
        _input.GotFocus += OnInputGotFocus;

        // **Tunnel** 才能抢在 TextBox 自己处理 Enter（插入 `\n`）之前；
        // 冒泡阶段它已经标了 Handled，提交档位永远收不到回车。
        _input.AddHandler(KeyDownEvent, OnInputKeyDown, RoutingStrategies.Tunnel);
    }

    private void UnhookInput()
    {
        if (_input is null)
        {
            return;
        }

        _input.LostFocus -= OnInputLostFocus;
        _input.GotFocus -= OnInputGotFocus;
        _input.RemoveHandler(KeyDownEvent, OnInputKeyDown);

        if (_editing is not null)
        {
            _editing.UserEdited -= OnEditingUserEdited;
            _editing.ProjectionChanged -= OnEditingProjectionChanged;
            _editing.Detach();
            _editing = null;
        }
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

        // 只读面没有"改"这回事 ⇒ 不需要撤销栈；输入面使用 Raw 快照撤销（见 ProjectedTextEditing），
        // 原生 TextBox 撤销会把显示标记当成文本历史，必须关闭。
        _input.IsUndoEnabled = false;

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

        if (_editing is not null && !string.Equals(_editing.RawText, _textValue, StringComparison.Ordinal))
        {
            _editing.SetRaw(_textValue, clearHistory: false);
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
        else if (_editing is not null && !string.Equals(_editing.RawText, text, StringComparison.Ordinal))
        {
            _editing.SetRaw(text, clearHistory: true);
        }

        SetUnmarked(unmarked);
        UpdateMarkOverlays();
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

    private void OnEditingUserEdited(object? sender, EventArgs e)
    {
        if (_context is null || Value.Length == 0 || _mode != TextSurfaceMode.Input || IsReadOnlyEffective)
        {
            return;
        }

        var text = _editing?.RawText ?? string.Empty;

        if (string.Equals(text, _textValue, StringComparison.Ordinal))
        {
            return;
        }

        _textValue = text;

        // 提交时机是控件的操作属性：即时档每次修改都交给值层，显式档等 Enter/Commit。
        if (CommitMode == ValueCommitMode.OnChange)
        {
            _context.CommitValue(Value, text);
        }
    }

    private void OnEditingProjectionChanged(object? sender, EventArgs e)
    {
        UpdateMarkOverlays();
    }

    private void OnInputLostFocus(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (_focusRing is not null)
        {
            _focusRing.IsVisible = false;
        }

        // 显式提交档：失焦提交，避免“改了输入框、点了别处却没提交”。
        // 即时档已经在 Raw 编辑事件里逐键提交，这里不重复。
        if (Value.Length > 0
            && CommitMode == ValueCommitMode.OnExplicit
            && !IsReadOnlyEffective
            && _editing is not null
            && !string.Equals(_editing.RawText, _committedValue, StringComparison.Ordinal))
        {
            Commit();
        }
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
        if (_input is null || _editing is null)
        {
            return;
        }

        var control = (args.KeyModifiers & KeyModifiers.Control) != 0;
        var shift = (args.KeyModifiers & KeyModifiers.Shift) != 0;

        // 复制/剪切一律走 Raw；只读时仍允许复制。
        if (control && args.Key == Key.C)
        {
            _editing.CopySelection();
            args.Handled = true;
            return;
        }

        if (IsReadOnlyEffective)
        {
            return;
        }

        if (control && args.Key == Key.X)
        {
            _editing.CutSelection();
            args.Handled = true;
            return;
        }

        if (control && args.Key == Key.Z && !shift)
        {
            _editing.Undo();
            args.Handled = true;
            return;
        }

        if (control && (args.Key == Key.Y || (args.Key == Key.Z && shift)))
        {
            _editing.Redo();
            args.Handled = true;
            return;
        }

        if (args.Key != Key.Enter)
        {
            return;
        }

        if (!Multiline)
        {
            args.Handled = true;
            Submit();
            return;
        }

        var submitKey = ResolveSubmitKey();
        var newline = ResolveEnterNewline();

        if (string.Equals(submitKey, "Enter", StringComparison.Ordinal) && !shift)
        {
            args.Handled = true;
            Submit();
            return;
        }

        if (string.Equals(submitKey, "CtrlEnter", StringComparison.Ordinal) && control)
        {
            args.Handled = true;
            Submit();
            return;
        }

        // 其余情况（含 SubmitKey=None / Shift+Enter / Ctrl+Enter 档的普通 Enter）
        // 都是"输入换行"：由模块的档位决定写入 \r\n / \r / \n，或什么都不写。
        args.Handled = true;

        if (newline.Length > 0)
        {
            _editing.ReplaceSelectionRaw(newline);
        }
    }

    /// <summary>当前生效的回车换行序列；空值 / 未知档位回到平台默认 <c>\r\n</c>。</summary>
    private string ResolveEnterNewline()
    {
        var choice = "crlf";

        if (EnterNewlineValue.Length > 0 && _context is { } context)
        {
            var state = context.ReadValue(EnterNewlineValue);
            if (state.IsKnown && state.Value.Length > 0)
            {
                choice = state.Value;
            }
        }

        return choice switch
        {
            "cr" => "\r",
            "lf" => "\n",
            "none" => string.Empty,
            _ => "\r\n",
        };
    }
    /// <summary>按提交档位交值；配置了命令时再触发命令（先值后命令）。</summary>
    private void Submit()
    {
        Commit();

        if (SubmitCommandName.Length > 0 && _context is { } context)
        {
            _ = context.InvokeCommand(SubmitCommandName);
        }
    }

    /// <summary>当前生效的提交档位：模块值优先，空/未知时回退框架设置。</summary>
    private string ResolveSubmitKey()
    {
        if (SubmitKeyValue.Length > 0 && _context is { } context)
        {
            var state = context.ReadValue(SubmitKeyValue);
            if (state.IsKnown && state.Value.Length > 0)
            {
                return state.Value;
            }
        }

        return _context?.MultilineSubmitKey ?? "CtrlEnter";
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
        _readText.ShowControlCharacterMarkers = ShowControlCharacterMarkers;
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
            _readText.ShowControlCharacterMarkers = ShowControlCharacterMarkers;
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
        UpdateMarkOverlays();
    }

    private void UpdateMarkOverlays()
    {
        if (_marks is null)
        {
            return;
        }

        if (IsReadOnlyEffective || _editing is null)
        {
            _marks.MarkerSpans = null;
            _marks.EscapeSpans = null;
            _marks.InvalidateVisual();
            return;
        }

        _marks.MarkerSpans = _editing.MarkerSpans;
        _marks.EscapeSpans = EscapeInput ? MapEscapeSpans(_editing) : null;
        _marks.InvalidateVisual();
    }

    private static List<TextMarkerSpan>? MapEscapeSpans(ProjectedTextEditing editing)
    {
        var raw = editing.RawText;
        var escapes = TextEscapes.FindEscapes(raw);

        if (escapes.Count == 0)
        {
            return null;
        }

        var projection = editing.Projection;
        var spans = new List<TextMarkerSpan>(escapes.Count);

        foreach (var escape in escapes)
        {
            var start = projection.RawToDisplay(escape.Start, preferRight: false);
            var end = projection.RawToDisplay(escape.Start + escape.Length, preferRight: true);

            if (end > start)
            {
                spans.Add(new TextMarkerSpan(start, end - start));
            }
        }

        return spans.Count > 0 ? spans : null;
    }

    /// <inheritdoc />
    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);

        if (change.Property == ShowControlCharacterMarkersProperty)
        {
            _editing?.SetMarkerMode(ShowControlCharacterMarkers);

            if (_readText is not null)
            {
                _readText.ShowControlCharacterMarkers = ShowControlCharacterMarkers;
            }

            UpdateMarkOverlays();
        }
        else if (change.Property == EscapeInputProperty)
        {
            UpdateMarkOverlays();
        }
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
