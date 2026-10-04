using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Controls.Primitives;
using Avalonia.Media;
using Avalonia.VisualTree;

namespace HYWDebugAssistant.Ui.Controls;

/// <summary>
/// **行末换行标记（`↵`）的绘制层**。
/// </summary>
/// <remarks>
/// <list type="bullet">
///   <item><b>它解决什么</b>：要在输入框里看见"哪里有换行符"（与显示区同一口径）。把 `↵`
///     **塞进文本**（显示文本 ↔ 真实文本映射）会连带接管光标，且光标变化规律难以预测 ⇒ 不可取。
///     本控件把 `↵` 画成**纯装饰**：**不进文本、不参与选择、光标直接跳过、删不掉** —— 与 Word 的 ¶ 同构。</item>
///   <item><b>怎么定位</b>：目标 `TextBox` 的可视树里有 `PART_TextPresenter`，其 `TextLayout` 公开可读、
///     `TextLines` 给出逐行几何。**实测注意**：本版本 `TextLine.Start` **恒为 0**（不可用于定位）⇒
///     逐行内容用**累计 `Length` + 源文本**自算；`Length` **含行末换行符**、**折行续行不含**。</item>
///   <item>
///     <b>标记挂在哪：只有一套口径（¶）</b>——**每一行因换行符结束就在它自己的行末画一个 `↵`**，
///     空行也一样（空行没有宽度 ⇒ 画在行首，即 Word 里空段落的 ¶ 位置）。
///     <list type="bullet">
///       <item>数据 `甲\n\n乙` ⇒ `甲↵` / `↵` / `乙`。</item>
///       <item>**只有数据里真有的换行符才画**：显示层把多行拼成一块连续文本时插进去的"通道换行"
///         由 <see cref="UnmarkedNewlines"/> 排除，永远不画 `↵`（通道引进的换行
///         **不是字符**）。</item>
///       <item>**输入框与输出框必须同源同口径**：按"只读面 / 可写面"分成**合并口径**（`甲↵↵` / `乙`）
///         与 **¶ 口径**两套，会让输出差一个换行符 ⇒ 只能有一套（同一控件、同源）。</item>
///     </list>
///   </item>
///   <item><b>不改编辑行为</b>：本控件 `IsHitTestVisible=false` 且不参与布局（与文本框同格、同层），
///     任何鼠标/键盘事件都落到下面的文本框上。</item>
///   <item><b>跟着文本框内部滚动走</b>：文本框的正文由模板里的
///     `ScrollViewer`（`PART_ScrollViewer`）承载 ⇒ 它一滚，**呈现器就平移、而本层不会自己收到通知**
///     ⇒ 标记会**留在旧坐标上**（表现为标记错位，要把焦点重新移上去才会刷新一次）。修法两条**缺一不可**：
///     ① 订阅内层 `ScrollViewer.ScrollChanged`（偏移与范围变化都要重绘），并在模板后到时补挂
///        （本层在构造函数里就拿到文本框，那时模板还没应用 ⇒ 一次性查找必然拿不到内层滚动条）；
///     ② 位置**一律在 <see cref="Render"/> 里按呈现器当前的平移重算**（本层不缓存任何"上次的几何"）。</item>
/// </list>
/// </remarks>
public sealed class NewlineMarkOverlay : Control
{
    /// <summary>**行末换行标记**（与显示区同一个字符）。</summary>
    public const string NewlineMark = "↵";

    /// <summary>标记与文本末尾之间的间距（DIP）。</summary>
    private const double MarkGap = 2.0;

    /// <summary>标记相对正文的字号比例（小一点，不喧宾夺主）。</summary>
    private const double MarkFontScale = 0.85;

    /// <summary>要标注的文本框。</summary>
    public static readonly StyledProperty<TextBox?> EditorProperty =
        AvaloniaProperty.Register<NewlineMarkOverlay, TextBox?>(nameof(Editor));

    /// <summary>是否启用（多行 + 纯文本输入才画）。</summary>
    public static readonly StyledProperty<bool> IsActiveProperty =
        AvaloniaProperty.Register<NewlineMarkOverlay, bool>(nameof(IsActive), defaultValue: true);

    /// <summary>
    /// **不该画 `↵` 的换行符偏移**（文本里的字符下标）——即"**通道换行**"那批。
    /// </summary>
    /// <remarks>
    /// 通道引进的换行**不是字符**，因此永远不该有 `↵`。
    /// 显示层把"行"拼成一块连续文本时**必须**插入真正的 `\n`（否则文本框不会断行），
    /// 于是这些 `\n` 看上去和数据里真有的 `\n` 一模一样 ⇒ 用这张偏移表把两者分开：
    /// **表里的不画，表外（= 数据里真的有换行字符）的画**。
    /// 输入框场景没有通道换行 ⇒ 恒为 <see langword="null"/> ⇒ 行为与没有通道换行时逐字一致。
    /// </remarks>
    public static readonly StyledProperty<IReadOnlyCollection<int>?> UnmarkedNewlinesProperty =
        AvaloniaProperty.Register<NewlineMarkOverlay, IReadOnlyCollection<int>?>(nameof(UnmarkedNewlines));

    private HashSet<int>? _unmarked;
    private TextBox? _subscribed;
    private ScrollViewer? _scrollViewer;
    private FormattedText? _mark;
    private double _markFontSize;

    /// <summary>创建绘制层（**不参与命中、不参与布局输入**）。</summary>
    public NewlineMarkOverlay()
    {
        IsHitTestVisible = false;
        Focusable = false;
        ClipToBounds = true;

        // 模板在我们拿到文本框之后才应用 ⇒ 内层滚动条要等"进了可视树/模板后到"再补挂
        AttachedToVisualTree += (_, _) => EnsureScrollViewer();
    }

    /// <summary>要标注的文本框。</summary>
    public TextBox? Editor
    {
        get => GetValue(EditorProperty);
        set => SetValue(EditorProperty, value);
    }

    /// <summary>是否启用（关 = 什么都不画）。</summary>
    public bool IsActive
    {
        get => GetValue(IsActiveProperty);
        set => SetValue(IsActiveProperty, value);
    }

    /// <summary>**不该画 `↵` 的换行符偏移**（通道换行；见该属性的说明）。</summary>
    public IReadOnlyCollection<int>? UnmarkedNewlines
    {
        get => GetValue(UnmarkedNewlinesProperty);
        set => SetValue(UnmarkedNewlinesProperty, value);
    }

    /// <summary>
    /// 当前应当画出的标记（**供测试与调试读取**）：位置在**本控件的坐标系**里，`Text` 是标记文字。
    /// </summary>
    /// <remarks>这就是"真的算出了像素位置"的那份数据 —— 回归网据此断言"行末真有 `↵`、且在框内"。</remarks>
    public IReadOnlyList<(Point Position, string Text)> Marks => _marks;

    private readonly List<(Point Position, string Text)> _marks = [];

    /// <inheritdoc />
    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);

        if (change.Property == EditorProperty)
        {
            Resubscribe(change.GetNewValue<TextBox?>());
            return;
        }

        if (change.Property == IsActiveProperty)
        {
            InvalidateVisual();
            return;
        }

        if (change.Property == UnmarkedNewlinesProperty)
        {
            var offsets = change.GetNewValue<IReadOnlyCollection<int>?>();
            _unmarked = offsets is { Count: > 0 } ? [.. offsets] : null;
            InvalidateVisual();
        }
    }

    /// <inheritdoc />
    public override void Render(DrawingContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        _marks.Clear();

        if (!IsActive || Editor is not { } editor || Bounds.Width <= 0 || Bounds.Height <= 0)
        {
            return;
        }

        // **兜底补挂**：内层滚动条可能**比本层晚**出现在模板里（`FindPresenter` 同理），
        // 不在这里再确认一次的话，那一次"晚到"就会让整条订阅链永远缺失（= 标记永远不随滚动重算）。
        // 注意这是"**没挂上才去找**"：挂上之后每帧只付一次引用/挂树检查（绘制路径上不做整树遍历）。
        EnsureScrollViewerIfMissing();

        if (FindPresenter(editor) is not { } presenter
            || presenter.TextLayout is not { } layout
            || editor.Text is not { Length: > 0 } text)
        {
            return;
        }

        // presenter 的局部坐标 → 本层坐标：**每次都现算**（文本框内部滚动时 presenter 会平移，这里跟着走）
        // ⇒ 本层不缓存任何几何，"上一帧画在哪"绝不参与这一帧的计算。
        if (presenter.TranslatePoint(default, this) is not { } origin)
        {
            return;
        }

        var font = MarkFormatted(editor);
        var marks = MarkLines(layout, text, _unmarked);
        var top = origin.Y;

        for (var index = 0; index < layout.TextLines.Count; index++)
        {
            var line = layout.TextLines[index];
            var height = line.Height;

            if (marks.TryGetValue(index, out var count))
            {
                // 一律画在**该行的行末**：内容行 ⇒ 文本末尾之后；空行（纯换行造出来的）没有宽度
                // ⇒ 自然落在行首 —— 这正是 Word 里空段落的 ¶ 位置。
                // **只有一套口径**（输入框与输出框的逻辑表现必须完全相同）：一个视觉行至多
                // 以一个换行符结束 ⇒ 每行至多一个标记；`count` 循环只是防御性写法。
                var x = origin.X + line.Width + MarkGap;
                var y = top + ((height - font.Height) / 2);

                if (y + font.Height >= 0 && y <= Bounds.Height)
                {
                    context.DrawText(font, new Point(x, y));

                    for (var i = 0; i < count; i++)
                    {
                        _marks.Add((new Point(x + (i * font.Width), y), NewlineMark));
                    }
                }
            }

            top += height;
        }
    }

    /// <summary>
    /// 逐**视觉行**算出"每行该画几个 `↵`"（行号 → 标记个数）。
    /// </summary>
    /// <param name="layout">文本框的文本布局（逐行几何）。</param>
    /// <param name="text">文本框的源文本（**真实文本**，不含任何标记）。</param>
    /// <param name="unmarked">**通道换行**的偏移表（这些 `\n` **不画** `↵`）；<see langword="null"/> = 一个都不排除。</param>
    /// <returns>行号 → 该行要画的标记个数。</returns>
    /// <remarks>
    /// <list type="bullet">
    ///   <item><b>为什么不用 `TextLine.Start`</b>：本版本实测**恒为 0** ⇒ 只能用**累计 `Length`** 切源文本；</item>
    ///   <item><b>`Length` 含行末换行符、折行续行不含</b>（实测）⇒ "这一行是不是因换行符结束"由切片**末字符**判定；
    ///     一个视觉行**最多以一个换行符结束** ⇒ ¶ 口径下每行至多一个标记；</item>
    ///   <item>
    ///     <b>空行画在它自己那一行</b>（¶ 口径）：输入框与日志面是同一个控件，
    ///     逻辑表现**必须完全相同** —— 数据 `甲\n\n乙` 显示成 `甲↵` / `↵` / `乙`（不把空行的 `↵`
    ///     并到上一行末尾——合并口径会让输出差一个换行符）。
    ///   </item>
    ///   <item><b>通道换行不画</b>：那些 `\n` 是显示层拼行时插进去的，不是数据里的字符
    ///     —— 端口/通道引进的换行**永远不该有 `↵`**（只有数据里真的有换行字符才画）。</item>
    /// </list>
    /// </remarks>
    private static Dictionary<int, int> MarkLines(
        Avalonia.Media.TextFormatting.TextLayout layout,
        string text,
        HashSet<int>? unmarked)
    {
        var marks = new Dictionary<int, int>();
        var offset = 0;

        for (var index = 0; index < layout.TextLines.Count; index++)
        {
            // **字符数**（含行末换行符；实测 `Start` 恒为 0 ⇒ 只用它做累计）
            var length = layout.TextLines[index].Length;
            var end = Math.Min(offset + length, text.Length);

            if (length <= 0 || end <= offset)
            {
                continue;
            }

            var slice = text[offset..end];
            offset = end;

            // 只有**因换行符结束**的行才有标记：软折行的续行不含换行符 ⇒ 不标
            if (!slice.EndsWith('\n'))
            {
                continue;
            }

            // **通道换行不产生标记**：那个 `\n` 是拼行时插的，不是数据里的字符
            if (unmarked is not null && unmarked.Contains(end - 1))
            {
                continue;
            }

            // **每一行因换行符结束 ⇒ 都有一个 `↵`**，空行也画在它自己那一行（¶ 口径）：空行是真实存在的
            // 一行，它的 `↵` 属于它自己（该行没有宽度 ⇒ 画在行首，与输入框逐行一致）；内容行 ⇒ 画在文本末尾之后。
            Add(index);
        }

        return marks;

        void Add(int line) => marks[line] = marks.TryGetValue(line, out var count) ? count + 1 : 1;
    }

    /// <summary>找到文本框里的文本呈现器（`PART_TextPresenter`）。</summary>
    private static TextPresenter? FindPresenter(TextBox editor) =>
        editor.GetVisualDescendants().OfType<TextPresenter>().FirstOrDefault();

    /// <summary>找到文本框模板里的内层滚动条（`PART_ScrollViewer`；**滚动发生的那一层**）。</summary>
    private static ScrollViewer? FindScrollViewer(TextBox editor) =>
        editor.GetVisualDescendants().OfType<ScrollViewer>().FirstOrDefault();

    /// <summary>取（并缓存）标记用的 `FormattedText`。</summary>
    private FormattedText MarkFormatted(TextBox editor)
    {
        var size = Math.Max(1, editor.FontSize * MarkFontScale);

        if (_mark is not null && Math.Abs(_markFontSize - size) < 0.01)
        {
            return _mark;
        }

        _markFontSize = size;
        _mark = new FormattedText(
            NewlineMark,
            CultureInfo.CurrentCulture,
            FlowDirection.LeftToRight,
            new Typeface(editor.FontFamily),
            size,
            Brushes.Gray);

        return _mark;
    }

    /// <summary>订阅/退订文本框的变化（文本、尺寸、**内部滚动**都要重画）。</summary>
    private void Resubscribe(TextBox? editor)
    {
        if (ReferenceEquals(_subscribed, editor))
        {
            return;
        }

        if (_subscribed is { } previous)
        {
            previous.TextChanged -= OnEditorChanged;
            previous.SizeChanged -= OnEditorSizeChanged;
            previous.PropertyChanged -= OnEditorPropertyChanged;
            previous.TemplateApplied -= OnEditorTemplateApplied;
        }

        _subscribed = editor;

        if (editor is not null)
        {
            editor.TextChanged += OnEditorChanged;
            editor.SizeChanged += OnEditorSizeChanged;
            editor.PropertyChanged += OnEditorPropertyChanged;
            editor.TemplateApplied += OnEditorTemplateApplied;
        }

        EnsureScrollViewer();
        InvalidateVisual();
    }

    /// <summary>模板应用完 ⇒ 这时文本框里才真的有内层滚动条（`PART_ScrollViewer`）。</summary>
    private void OnEditorTemplateApplied(object? sender, TemplateAppliedEventArgs e) => EnsureScrollViewer();

    /// <summary>
    /// 确认"内层滚动条"的订阅是最新的：**模板后到 / 首次落到可视树**都由它兜住。
    /// </summary>
    /// <remarks>
    /// 必须是"每次确认"而不是"只查一次"：本层在 `TextSurface` 的构造函数里就被赋了 `Editor`，
    /// 那时文本框**还没有模板** ⇒ 一次性查找必然拿到 `null`，于是整个滚动订阅链缺失 ——
    /// 症状正是"标记只在别的重绘（如焦点变化）时才刷新一次"。
    /// </remarks>
    private void EnsureScrollViewer()
    {
        var viewer = Editor is { } editor ? FindScrollViewer(editor) : null;

        if (ReferenceEquals(_scrollViewer, viewer))
        {
            return;
        }

        if (_scrollViewer is { } previous)
        {
            previous.ScrollChanged -= OnEditorScrollChanged;
        }

        _scrollViewer = viewer;

        if (viewer is not null)
        {
            viewer.ScrollChanged += OnEditorScrollChanged;
        }
    }

    /// <summary>
    /// **绘制路径上的廉价兜底**：只有"还没挂上 / 挂的那一个已经离开可视树"时才去找一遍。
    /// </summary>
    /// <remarks>绘制会被频繁调用 ⇒ 正常帧里这里只付一次引用判空 + 一次挂树检查，不做整树遍历。</remarks>
    private void EnsureScrollViewerIfMissing()
    {
        if (_scrollViewer is null || !_scrollViewer.IsAttachedToVisualTree())
        {
            EnsureScrollViewer();
        }
    }

    private void OnEditorChanged(object? sender, TextChangedEventArgs e) => InvalidateVisual();

    private void OnEditorSizeChanged(object? sender, SizeChangedEventArgs e) => InvalidateVisual();

    /// <summary>
    /// **内层滚动**（正文滚动条被拖动/被滚动/被 `BringIntoView` 带着走）⇒ 标记必须重画。
    /// </summary>
    /// <remarks>
    /// 这是"只读框的标记不随滚动变化"那条缺陷的**唯一**修法：文本框内部的平移**不会**冒泡成
    /// `TextChanged`/`SizeChanged`/光标变化，本层不订阅它就永远不知道正文已经移过位置。
    /// </remarks>
    private void OnEditorScrollChanged(object? sender, ScrollChangedEventArgs e) => InvalidateVisual();

    /// <summary>光标/选择变化会让文本框内部滚动 ⇒ 标记位置要跟着重算。</summary>
    private void OnEditorPropertyChanged(object? sender, AvaloniaPropertyChangedEventArgs e)
    {
        if (e.Property == TextBox.CaretIndexProperty || e.Property == TextBox.SelectionStartProperty
            || e.Property == TextBox.SelectionEndProperty)
        {
            InvalidateVisual();
        }
    }
}
