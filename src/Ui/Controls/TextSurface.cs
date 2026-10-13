using System.Collections.Specialized;
using System.Text;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml.MarkupExtensions;
using Avalonia.Media;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.Input;
using HYWDebugAssistant.Sdk.Views;
using HYWDebugAssistant.Ui.ModuleSurface.Theme;

namespace HYWDebugAssistant.Ui.Controls;

/// <summary>
/// **唯一一块文本面**：输入与日志行为基本一致，只在数据写入者 / 只读属性 / 滚动条行为上不同。
/// </summary>
/// <remarks>
/// <list type="bullet">
///   <item><b>它内部有且只有一个 <see cref="TextBox"/> + 一个 <see cref="NewlineMarkOverlay"/></b>
///     （可选再加自建竖向滚动条 + 分页窗口）⇒ 模块文本**输入框**与 A5 **日志**共用同一份实现，
///     不再各写一套（重复实现 = 测试成本翻倍 + 观感/手感各自漂移）；</item>
///   <item><b>差异只用属性表达</b>：数据来源（<see cref="Lines"/> 模块缓冲 vs <see cref="Text"/> 用户键入）、
///     <see cref="IsReadOnly"/>、文本层的滚动（<see cref="ScrollMode"/>：`window` = 自建竖条 + 分页窗口，
///     `native` = 文本框自己的滚动条）；</item>
///   <item><b>换行标记 / 字符级选择 / 复制 = 同一份实现</b>：`↵` 一律由 <see cref="NewlineMarkOverlay"/>
///     画在**同格的只读绘制层**里（不进文本、不参与选择、光标跳过 —— 与 Word 的 ¶ 同构）；</item>
///   <item><b>一行多长由文本框宽度决定</b>（自动换行、**无横向滚动条**）—— 数据侧不再规定字节数；
///     数据里**真的有换行字符**的地方才画 `↵`（通道换行不是字符，见 <see cref="NewlineMarkOverlay.UnmarkedNewlines"/>）。</item>
///   <item><b>A5 不再有自己的外壳视图</b>：那层"标题 + 外框 + 丢弃提示 + 复制全部回调"的壳
///     （`TextLogView`）在该控件同时支持输入与显示之后**整个删掉**，日志那一块现在就是
///     `ModuleContentView.axaml` 里 A5 的 `DataTemplate` 直接放一个本控件；
///     "复制全部"的取数也就地收回本类（<see cref="Lines"/> 对分页窗口而言**就是整段缓冲**）。</item>
/// </list>
/// <para>
/// <b>分页窗口</b>（<see cref="ScrollMode"/> = <see cref="ViewScrollMode.Window"/>，日志用）：实测只读 `TextBox` 的更新代价 ≈ **0.47 ms/行**
/// （赋 `Text` 本身便宜，**贵的是布局**；20 000 行一次要十几秒）⇒ 文本框**每次只吃一屏多**，
/// 更早的内容靠右侧自建滚动条换窗口内容，代价于是与缓冲长度**无关**。
/// 追加只标"待刷新"，由 **20 Hz 的节拍**统一落地（<see cref="DispatcherTimer"/> + "到节拍边界落地"的排程两条腿）。
/// </para>
/// </remarks>
public sealed partial class TextSurface : UserControl
{
    // ---------------------------------------------------------------- 属性（差异只用属性表达）

    /// <summary>**当前文本**（用户键入的那一份；日志模式不用它，见 <see cref="Lines"/>）。</summary>
    public static readonly StyledProperty<string?> TextProperty =
        AvaloniaProperty.Register<TextSurface, string?>(nameof(Text));

    /// <summary>是否**只读**（日志 = true；输入框 = false）。</summary>
    public static readonly StyledProperty<bool> IsReadOnlyProperty =
        AvaloniaProperty.Register<TextSurface, bool>(nameof(IsReadOnly));

    /// <summary>是否多行（= `AcceptsReturn` + `multiline` 样式类）。</summary>
    public static readonly StyledProperty<bool> IsMultilineProperty =
        AvaloniaProperty.Register<TextSurface, bool>(nameof(IsMultiline));

    /// <summary>
    /// 长行是否**按宽度折行**（<c>true</c> = 自动换行、无横向滚动条；<c>false</c> = 一行到底、横向滚）。
    /// </summary>
    /// <remarks>
    /// 折行与否是**模块作者的声明**（日志的 <c>wrap</c> 键），不是控件固定的观感：
    /// 一屏要看全一行的场合（十六进制、定宽记录）必须能关掉它。
    /// </remarks>
    public static readonly StyledProperty<bool> WrapsProperty =
        AvaloniaProperty.Register<TextSurface, bool>(nameof(Wraps), defaultValue: true);

    /// <summary>最小高度（DIP；0 = 不设 —— 单行框的行为）。</summary>
    public static readonly StyledProperty<double> EditorMinHeightProperty =
        AvaloniaProperty.Register<TextSurface, double>(nameof(EditorMinHeight));

    /// <summary>是否画行末换行标记 `↵`（多行 + 纯文本输入 = true）。</summary>
    public static readonly StyledProperty<bool> ShowNewlineMarksProperty =
        AvaloniaProperty.Register<TextSurface, bool>(nameof(ShowNewlineMarks));

    /// <summary>
    /// **文本层用哪一套滚动**（声明面 <c>scrollBar</c>；**必写** —— 框架不按 <c>readonly</c> 推）。
    /// </summary>
    /// <remarks>
    /// 它与**元素层**的溢出（框比格子高时由外层滚动 / 裁剪）是**两级不同的事实**；
    /// 两档互斥 ⇒ 同一个文本面只可能有一根用户看得见的条。
    /// </remarks>
    public static readonly StyledProperty<ViewScrollMode> ScrollModeProperty =
        AvaloniaProperty.Register<TextSurface, ViewScrollMode>(nameof(ScrollMode));

    /// <summary>
    /// 文字横向对齐（声明面 <c>textAlign</c>）—— **落在内层编辑器上**（它才是排版文字的那一层）。
    /// </summary>
    public static readonly StyledProperty<TextAlignment> AlignProperty =
        AvaloniaProperty.Register<TextSurface, TextAlignment>(nameof(Align));

    /// <summary>**分页窗口的数据来源**（模块缓冲的行；仅在 <see cref="ScrollMode"/> = `window` 时有消费者）。</summary>
    public static readonly StyledProperty<IReadOnlyList<string>?> LinesProperty =
        AvaloniaProperty.Register<TextSurface, IReadOnlyList<string>?>(nameof(Lines));

    /// <summary>窗口最多行数（**性能闸门**）—— 实测 ≈ 0.47 ms/行 ⇒ 一屏 + 一页余量，且绝不越过这个上限。</summary>
    private const int MaxWindowLines = 200;

    /// <summary>刷新节拍（20 Hz）：追加只标"待刷新"，由它统一落地。</summary>
    private static readonly TimeSpan RefreshInterval = TimeSpan.FromMilliseconds(50);

    /// <summary>滚轮一次移动的行数（一屏内的手感粒度，与 WinForms 多行框接近）。</summary>
    private const int WheelLines = 3;

    private readonly RelayCommand _copyAllCommand;

    private readonly TextBox _editor;
    private readonly NewlineMarkOverlay _marks;
    private readonly ScrollBar _scroll;

    /// <summary>文本面这一行的排布（编辑器 + 标记层 + 边框叠加层 + 自建竖条）。</summary>
    private readonly SurfaceTextRow _row;

    /// <summary>边框叠加层（常态 1px / 聚焦·标红 2px）—— **不参与内容矩形**。</summary>
    private readonly Border _ring;

    /// <summary>正在把**编辑器**的文本回写 <see cref="Text"/>（避免两条同步互相打架）。</summary>
    private bool _syncingText;

    /// <summary>**文本层**的行高（DIP；由主题给，读一次同时用于编辑器与窗口行数）。</summary>
    private double _lineHeight;

    /// <summary>**分页窗口**：窗口行数 / 起点 / 是否跟随尾部。</summary>
    private int _windowSize = 1;
    private int _windowStart;

    private bool _followTail = true;

    /// <summary>有"待落地的新内容"（追加/丢弃/清空/视口变化都会置位）。</summary>
    private bool _pending;

    /// <summary>已经排了"到节拍边界落地"的任务（同一拍内的多次追加据此合并成一次）。</summary>
    private bool _flushScheduled;

    /// <summary>上一次真正重排文本框的时刻（20 Hz 节流的判据）。</summary>
    private DateTime _lastFlushUtc = DateTime.MinValue;

    /// <summary>上一次判定为"用户正在文本框里交互"（有选区 —— 重写会把选区抽走）。</summary>
    private bool _paused;

    /// <summary>暂停期间用户自己动过滚动位置（回看历史）—— 恢复时**不能**把他的位置抢回底部。</summary>
    private bool _scrolledWhilePaused;

    /// <summary>正在程序化写 <see cref="ScrollBar"/> 的值（避免把自己的写入当成用户拖动）。</summary>
    private bool _suppressScrollEvent;

    /// <summary>正在把窗口内容写进文本框（期间的一切通知都不算"用户交互"）。</summary>
    private bool _rewriting;

    /// <summary>正在 <see cref="FlushWindowCore"/> 里（防重入：写 `Text` 会同步把通知打回来）。</summary>
    private bool _flushing;

    private DispatcherTimer? _timer;
    private IReadOnlyList<string>? _subscribedLines;

    /// <summary>创建文本面（**内部只有一个文本框 + 一个换行标记绘制层**）。</summary>
    public TextSurface()
    {
        // 本控件自己不参与焦点与命中：一切键鼠都必须落到内层文本框上（否则"点框里没反应"）
        Focusable = false;

        InitializeComponent();

        // **孩子由代码给、量排由 `SurfaceTextRow` 定**，不走手工加可视子这条路。
        // `ContentControl` 为了托管 `Content` 会插一个自己的内部 presenter；手工 `VisualChildren.Add`
        // 顶不过它（实测：可视子仍是那个 `ContentPresenter 0x0`，整棵内容树**不进可视树**，
        // 屏幕上什么都看不到，而 `Content`、文本框的爹看上去都正常）。
        var editor = new TextBox { Name = "PartEditor" };

        if (this.TryFindResource("FontData", out var font) && font is Avalonia.Media.FontFamily family)
        {
            editor.FontFamily = family;
        }

        editor.Classes.Add("surface");

        // **单行：文字与光标在框内垂直居中**；多行在下面按 `IsMultiline` 改成贴顶（见那一段的说明）。
        // 这是"内容怎么摆在框里" ⇒ **属控件、不属主题**（主题里那两条 `VerticalContentAlignment=Top`
        // 已删，否则就是两处口径）。缺省先给居中：单行是缺省档，而"把属性设成与缺省相同的值"
        // **不会**触发属性变更回调 —— 只靠回调设，会漏掉最常用的那一种。
        editor.VerticalContentAlignment = Avalonia.Layout.VerticalAlignment.Center;

        _editor = editor;

        _marks = new NewlineMarkOverlay { Name = "PartMarks", IsHitTestVisible = false };
        _scroll = new ScrollBar { Name = "PartScroll", Orientation = Avalonia.Layout.Orientation.Vertical, IsVisible = false };

        // **边框是叠加层**：与编辑器同格、不参与命中、**不参与内容矩形** ——
        // 聚焦 / 标红时它变粗，而文字一格都不动（厚度进布局的话内容区会缩 2px）。
        _ring = new Border { Name = "PartRing", IsHitTestVisible = false };
        _ring.Classes.Add(RingClass);

        _row = new SurfaceTextRow(_editor, _marks, _ring, _scroll);
        Content = _row;

        // **文本层那一档由 `ScrollMode` 唯一决定**（内层竖条的可见性、行高、自建条都在
        // `ApplyScrollMode` 一处写入）—— 构造期先落一次，属性变化时再落。
        _marks.Editor = _editor;
        _marks.IsActive = false;

        // 标记口径**只有一套（¶）**，与只读/可写无关（输入框与日志面是同一个控件，
        // 逻辑表现必须完全相同）⇒ 这里不按 `IsReadOnly` 切换任何东西。

        _copyAllCommand = new RelayCommand(() => _ = CopyAllToClipboardAsync());

        // `Ctrl+Shift+C` = 复制全部（缓冲）；`Ctrl+C` 保持文本框原生的"复制选中"
        KeyBindings.Add(new KeyBinding
        {
            Gesture = new KeyGesture(Key.C, KeyModifiers.Control | KeyModifiers.Shift),
            Command = _copyAllCommand,
        });

        // 滚轮在**隧道**阶段拦下（先于 TextBox 内部的 `ScrollViewer`）：滚轮 = 移动窗口起点，
        // 而不是在文本框内部滚出一块窗口之外的内容（那会让"窗口"模型错位）
        _editor.AddHandler(PointerWheelChangedEvent, OnEditorWheel, RoutingStrategies.Tunnel);

        _editor.TextChanged += OnEditorTextChanged;
        _editor.PropertyChanged += OnEditorPropertyChanged;
        _editor.GotFocus += (_, _) => Pump();
        _editor.LostFocus += (_, _) => Pump();
        _editor.GotFocus += (_, _) => UpdateFrame();
        _editor.LostFocus += (_, _) => UpdateFrame();
        _scroll.ValueChanged += OnScrollValueChanged;

        // 折行是"值变了才应用"的属性，得在构造期先落一次：默认 `Wraps = true`，
        // 不落的话样式表里那条 `TextWrapping = Wrap` 是唯一来源，声明面关掉折行时视觉不会变。
        ApplyWrapping(Wraps);

        // **行高先取一次**：`ScrollMode` 是在对象初始化器里设的 ⇒ `ApplyScrollMode` 会在挂上可视树
        // **之前**先跑一遍，那时从主题里读不到合并字典（拿到回退值），而 `LineHeight` 不接受 0
        // （只接受 > 0 或 NaN）。挂上之后再读一次真值。
        _lineHeight = SurfaceTheme.TextLineHeight(this);

        AttachedToVisualTree += (_, _) => Attach();
        DetachedFromVisualTree += (_, _) => Detach();

        ApplyCaret();
        UpdateFrame();
        ApplyScrollMode();
    }

    /// <summary>边框叠加层的类名（与 <c>TextSurface.axaml</c> 里的选择器同一处口径）。</summary>
    public const string RingClass = "surface-text-ring";

    /// <summary>是不是"自建竖条 + 分页窗口"那一档。</summary>
    private bool IsWindowMode => ScrollMode is ViewScrollMode.Window;

    /// <summary>
    /// **这一圈边框被标出来了**（模块说"这一格不对"）。
    /// </summary>
    /// <remarks>
    /// 颜色由模块给（框架不认识"红 = 失败"），这里只负责**怎么画**：标红时同时加粗，
    /// 并且**优先于聚焦态** —— 用户正在改那一格时，它仍然得是醒目的。
    /// </remarks>
    public uint? FrameAlert { get; set; }

    /// <summary>
    /// **这一圈被标出来了**（**角色词**版：颜色取主题里那一支 ⇒ 换主题 / 用户改色都跟随）。
    /// </summary>
    /// <remarks>
    /// 与 <see cref="FrameAlert"/> 是同一圈的两种写法（模块侧后写的清掉另一种）：
    /// 那条给"我就要这个色"（绝对色），这条给"要配色表里的 <c>danger</c>"（相对色）。
    /// 值是**主题键**（解析在 <c>SurfaceElementControl</c> 一处做），这里只负责把它画出来。
    /// </remarks>
    public string? FrameAlertRole { get; set; }

    /// <summary>
    /// **模块给的文字色**（`null` = 没设 ⇒ 主题那一档）。
    /// </summary>
    /// <remarks>
    /// 必须落在**内层编辑器**上：真正显示文字的是它，而它自带一条样式前景（Fluent 的半透明黑），
    /// 那条**优先于从元素继承** ⇒ 只改外层元素的前景等于没改（用户实测："文字是深灰色而不是黑色"、
    /// 模块设了颜色也不生效）。判据同 <see cref="FrameAlert"/>：**用户看得见的那一块是谁画的**。
    /// </remarks>
    public uint? TextColor
    {
        get => _textColor;
        set
        {
            _textColor = value;

            if (_textColor is { } argb)
            {
                _editor.Foreground = new SolidColorBrush(Color.FromUInt32(argb));
                return;
            }

            // `null` ⇒ 交还主题（主题在 `TextBox.surface` 上写了 `TextPrimary`）。
            _editor.ClearValue(ForegroundProperty);
        }
    }

    /// <summary>
    /// **模块给的文字大小**（DIP；`null` = 没设 ⇒ 主题那一档）。
    /// </summary>
    /// <remarks>与 <see cref="TextColor"/> 同一条口径：落到内层编辑器上，`null` 交还主题。</remarks>
    public double? TextSize
    {
        get => _textSize;
        set
        {
            _textSize = value;

            if (_textSize is { } dips)
            {
                _editor.FontSize = dips;
                return;
            }

            _editor.ClearValue(FontSizeProperty);
        }
    }

    private uint? _textColor;
    private double? _textSize;

    /// <summary>
    /// **模块给的文字角色词**（**主题键**；<see langword="null"/> = 没设 ⇒ 主题那一档）。
    /// </summary>
    /// <remarks>
    /// 与 <see cref="TextColor"/> 同一落点、同一理由（必须落在**内层编辑器**上，样式前景压过继承）；
    /// 区别只在写法：那条是局部色值（刻意固定），这条是**主题资源绑定**（随主题 / 用户改色跟随）。
    /// </remarks>
    public string? TextColorRole
    {
        get => _textColorRole;
        set
        {
            _textColorRole = value;

            if (_textColorRole is { Length: > 0 } key)
            {
                _editor[!ForegroundProperty] = new DynamicResourceExtension(key);
                return;
            }

            _editor.ClearValue(ForegroundProperty);
        }
    }

    private string? _textColorRole;

    /// <summary>
    /// **按当前状态把那一圈边框画出来**：常态浅灰 / 可写深灰 / 聚焦加粗 / 被标红时鲜艳加粗。
    /// </summary>
    /// <remarks>
    /// <para>
    /// 这几档是**同一圈的几个状态**，用代码算而不是用选择器：可写与聚焦各自要改色和粗细，
    /// 而"当前是第几档"只有控件自己知道。
    /// </para>
    /// <para>
    /// <b>颜色一律走 <see cref="DynamicResourceExtension"/></b>：主题字典由框架解析，
    /// 于是换主题 / 换深浅色时不必重建控件树。**不要在代码里 `TryFindResource` 取色** ——
    /// 那个查找在控件挂进可视树之前解析不到合并字典，结果是"常态那一圈一点颜色都没有"，
    /// 而屏幕上表现为"边框没了、只剩标红的"。
    /// </para>
    /// <para>
    /// **可写与可读必须一眼分得出来**：只读面（日志 / 结果框）是"我给你看的"，可写面是"你要填的"。
    /// 两圈一样深的时候，用户要读一眼才敢下手；深浅差一档就不必读。
    /// </para>
    /// <para>
    /// <b>只读面不用"聚焦加粗"</b>：加粗是"你现在在改这一格"的信号，而只读面**改不了** ——
    /// 给它加粗等于告诉用户一件不成立的事。只读面点进去仍然能选中文字（那是读的行为）。
    /// </para>
    /// </remarks>
    public void UpdateFrame()
    {
        // 加粗 = "正在改这一格"。只读面没有这件事，所以它的**叠加层**只有 1px。
        var focused = !IsReadOnly && _editor.IsFocused;

        // **厚度只落在叠加层上**：本控件自己一圈都不画（`BorderThickness` 恒为 0）——
        // 画在这里就等于"状态改变了布局矩形"，聚焦瞬间内容区缩 2px、文字跟着移。
        BorderThickness = default;
        _ring.BorderThickness = FrameAlert is not null || FrameAlertRole is not null || focused
            ? new Thickness(2)
            : new Thickness(1);

        // **角色词优先走主题绑定**（`danger` 这类相对色）：换主题 / 用户改主题色时由资源系统重新解析。
        if (FrameAlertRole is { Length: > 0 } alertRole)
        {
            _ring[!Border.BorderBrushProperty] = new DynamicResourceExtension(alertRole);
            return;
        }

        if (FrameAlert is { } alert)
        {
            // 标红：颜色由模块给（框架不认识"红 = 失败"），**优先于聚焦态** ——
            // 用户正在改那一格时它仍然得醒目。
            _ring.BorderBrush = new SolidColorBrush(Color.FromUInt32(alert));
            return;
        }

        _ring[!Border.BorderBrushProperty] = new DynamicResourceExtension(
            IsReadOnly ? FrameBrushKey : EditableFrameBrushKey);
    }

    /// <summary>
    /// **只读面不显示插入光标**（把它的颜色设成透明）。
    /// </summary>
    /// <remarks>
    /// 闪烁的竖线在只读面上是纯粹的噪声：它说"你可以在这里打字"，而这恰恰是不成立的。
    /// **只改光标的颜色，不动 `Focusable`** —— 关掉焦点会连"选中文字"一起失去，
    /// 而"能选中、能复制"正是只读面该有的读法。回切到可写面时必须还原。
    /// </remarks>
    private void ApplyCaret()
    {
        if (IsReadOnly)
        {
            _editor.CaretBrush = Brushes.Transparent;
            return;
        }

        _editor.ClearValue(TextBox.CaretBrushProperty);
    }

    /// <summary>常态（只读面）边框色的资源键（**引全局角色** `BorderStrong`；本控件不再自带一份拷贝）。</summary>
    private const string FrameBrushKey = "BorderStrong";

    /// <summary>可写面边框色的资源键（比常态深一档 ⇒ 一眼看出"这一格能填"）。</summary>
    private const string EditableFrameBrushKey = "SurfaceTextFrameEditableBrush";

    /// <summary>内层**唯一的**文本框（测试与调试读取；输入框的提交策略挂在它冒泡/隧道的路由事件上）。</summary>
    public TextBox Editor => _editor;

    /// <summary>换行标记绘制层（测试与调试读取）。</summary>
    public NewlineMarkOverlay Marks => _marks;

    /// <summary>自建竖向滚动条（**只在 `window` 档、且真的有回看余地时**才可见 —— 见 <see cref="UpdateScrollBarRange"/>）。</summary>
    public ScrollBar ScrollBar => _scroll;

    /// <summary>当前窗口行数（**一屏 + 一页余量**；测试与诊断读取）。</summary>
    public int WindowSize => _windowSize;

    /// <summary>当前窗口起点（缓冲内的行索引；测试与诊断读取）。</summary>
    public int WindowStart => _windowStart;

    /// <summary>是否处于"跟随尾部"状态（测试与诊断读取）。</summary>
    public bool IsFollowingTail => _followTail;

    /// <summary>当前文本（用户键入的那一份）。</summary>
    public string? Text
    {
        get => GetValue(TextProperty);
        set => SetValue(TextProperty, value);
    }

    /// <summary>是否只读。</summary>
    public bool IsReadOnly
    {
        get => GetValue(IsReadOnlyProperty);
        set => SetValue(IsReadOnlyProperty, value);
    }

    /// <summary>是否多行。</summary>
    public bool IsMultiline
    {
        get => GetValue(IsMultilineProperty);
        set => SetValue(IsMultilineProperty, value);
    }

    /// <summary>长行是否按宽度折行。</summary>
    public bool Wraps
    {
        get => GetValue(WrapsProperty);
        set => SetValue(WrapsProperty, value);
    }

    /// <summary>最小高度（DIP）。</summary>
    public double EditorMinHeight
    {
        get => GetValue(EditorMinHeightProperty);
        set => SetValue(EditorMinHeightProperty, value);
    }

    /// <summary>是否画 `↵`。</summary>
    public bool ShowNewlineMarks
    {
        get => GetValue(ShowNewlineMarksProperty);
        set => SetValue(ShowNewlineMarksProperty, value);
    }

    /// <summary>文本层用哪一套滚动（声明面 <c>scrollBar</c>）。</summary>
    public ViewScrollMode ScrollMode
    {
        get => GetValue(ScrollModeProperty);
        set => SetValue(ScrollModeProperty, value);
    }

    /// <summary>文字横向对齐（声明面 <c>textAlign</c>）。</summary>
    public TextAlignment Align
    {
        get => GetValue(AlignProperty);
        set => SetValue(AlignProperty, value);
    }

    /// <summary>分页窗口的数据来源（行）。</summary>
    public IReadOnlyList<string>? Lines
    {
        get => GetValue(LinesProperty);
        set => SetValue(LinesProperty, value);
    }

    /// <inheritdoc />
    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        ArgumentNullException.ThrowIfNull(change);
        base.OnPropertyChanged(change);

        if (change.Property == TextProperty)
        {
            PushTextToEditor(change.GetNewValue<string?>());
            return;
        }

        if (change.Property == IsReadOnlyProperty)
        {
            var readOnly = change.GetNewValue<bool>();
            _editor.IsReadOnly = readOnly;
            ApplyCaret();     // 只读面不显示插入光标（但文字仍可选中）
            UpdateFrame();    // 可写/可读的深浅是这一圈的两个状态
            return;
        }

        if (change.Property == IsMultilineProperty)
        {
            var multiline = change.GetNewValue<bool>();
            _editor.AcceptsReturn = multiline;
            SetClass(_editor, "multiline", multiline);

            // **单行：文字与光标在框内垂直居中；多行：从第一行起排（贴顶）**。
            // 多行必须贴顶：内容的高随行数变，居中会让"滚到哪儿"与眼睛看到的位置对不上；
            // 单行的框往往比一行高（例：声明 24 的间隔框），贴顶就永远偏上。
            _editor.VerticalContentAlignment = multiline
                ? Avalonia.Layout.VerticalAlignment.Top
                : Avalonia.Layout.VerticalAlignment.Center;

            return;
        }

        if (change.Property == WrapsProperty)
        {
            ApplyWrapping(change.GetNewValue<bool>());
            return;
        }

        if (change.Property == EditorMinHeightProperty)
        {
            _editor.MinHeight = change.GetNewValue<double>();
            return;
        }

        if (change.Property == ShowNewlineMarksProperty)
        {
            _marks.IsActive = change.GetNewValue<bool>();
            return;
        }

        if (change.Property == ScrollModeProperty)
        {
            ApplyScrollMode();
            return;
        }

        if (change.Property == AlignProperty)
        {
            _editor.TextAlignment = change.GetNewValue<TextAlignment>();
            return;
        }

        if (change.Property == LinesProperty)
        {
            ResubscribeLines(change.GetNewValue<IReadOnlyList<string>?>());
        }
    }

    /// <summary>按开关切换样式类（只增删该类，不动别的类）。</summary>
    private static void SetClass(Control control, string name, bool on)
    {
        if (on)
        {
            if (!control.Classes.Contains(name))
            {
                control.Classes.Add(name);
            }

            return;
        }

        _ = control.Classes.Remove(name);
    }

    // ---------------------------------------------------------------- 文本同步（输入框那一半）

    /// <summary>把 <see cref="Text"/> 推进编辑器（**只在真的不同才写**：赋 `Text` 会重置光标/选区）。</summary>
    private void PushTextToEditor(string? text)
    {
        if (IsWindowMode || _syncingText)
        {
            return;     // 分页窗口模式下文本由窗口写，`Text` 不是它的来源
        }

        var value = text ?? string.Empty;

        if (string.Equals(_editor.Text, value, StringComparison.Ordinal))
        {
            return;
        }

        _syncingText = true;

        try
        {
            _editor.Text = value;
        }
        finally
        {
            _syncingText = false;
        }
    }

    /// <summary>用户（或平台）改了编辑器里的文本 ⇒ 回写 <see cref="Text"/>（两向绑定的一半）。</summary>
    private void OnEditorTextChanged(object? sender, TextChangedEventArgs e)
    {
        // 分页窗口模式下 `Text` 不是数据源（数据源是 `Lines`）⇒ 不回写，否则会把窗口内容当"用户输入"
        if (IsWindowMode || _syncingText)
        {
            return;
        }

        var value = _editor.Text ?? string.Empty;

        if (string.Equals(Text, value, StringComparison.Ordinal))
        {
            return;
        }

        _syncingText = true;

        try
        {
            // `SetCurrentValue`：保住外部的 TwoWay 绑定（写的是"当前值"，同时推回绑定源 = 视图模型）
            SetCurrentValue(TextProperty, value);
        }
        finally
        {
            _syncingText = false;
        }
    }

    /// <summary>切换"长行按宽度折行"（关 ⇒ 一行到底、横向滚动）。</summary>
    /// <param name="wraps">是否折行。</param>
    /// <remarks>
    /// 折行与否同时定两件事：文本框自己的排版（<c>TextWrapping</c>）与**横向滚动条**要不要出现。
    /// 只改前者会让"关掉折行"变成"长行被切掉"（用户看不到被切掉的那半行）。
    /// </remarks>
    private void ApplyWrapping(bool wraps)
    {
        _editor.TextWrapping = wraps ? TextWrapping.Wrap : TextWrapping.NoWrap;
        _editor.SetValue(
            ScrollViewer.HorizontalScrollBarVisibilityProperty,
            wraps ? ScrollBarVisibility.Disabled : ScrollBarVisibility.Auto);
    }

    // ---------------------------------------------------------------- 分页窗口（日志那一半）

    /// <summary>
    /// **把文本层那一档落到控件上**（唯一的写入点）：内层竖条、行高、自建条的可见性。
    /// </summary>
    /// <remarks>
    /// <para>
    /// 两档互斥，并且各自只有一条可见性判据：
    /// `window` ⇒ 内层条 `Hidden`（仍可程序化滚动，但没有条）+ **只有自建条**可能可见
    /// （判据 = `Maximum &gt; 0`，见 <see cref="UpdateScrollBarRange"/>）；
    /// `native` ⇒ 自建条不显示 + 内层条 `Auto`（由 Fluent 自己按需出现）。
    /// </para>
    /// <para>
    /// **不在样式表里写 `VerticalScrollBarVisibility`**：本地值优先级更高，写在那里只会是一条
    /// 永远不生效的第二来源（"配了却没生效"）。行高同理 —— 主题给一个数，这里读一次同时用于
    /// 编辑器与"一屏放得下几行"。
    /// </para>
    /// </remarks>
    private void ApplyScrollMode()
    {
        var window = IsWindowMode;

        SetClass(_editor, "paged", window);

        // 行高与那一档一并在**这一处**落到编辑器上（写入合法性见 `ApplyLineHeight`）。
        ApplyLineHeight();

        _editor.SetValue(
            ScrollViewer.VerticalScrollBarVisibilityProperty,
            window ? ScrollBarVisibility.Hidden : ScrollBarVisibility.Auto);

        if (window)
        {
            _editor.ContextMenu = BuildContextMenu();
            _pending = true;
            Attach();
            UpdateScrollBarRange(CountLines(), _windowSize, _windowStart);
            return;
        }

        _editor.ContextMenu = null;
        SetBarVisible(false);

        if (_timer is not null)
        {
            _timer.Stop();
            _timer = null;
        }
    }

    /// <summary>
    /// 把行高落到编辑器上 —— **只写合法值**。
    /// </summary>
    /// <remarks>
    /// `LineHeight` 只接受「&gt; 0 或 NaN」：写 0 会当场抛
    /// （`0 is not a valid value for 'LineHeight'`）。而主题值在**挂上可视树之前**读不到
    /// （`DynamicResource` 还没解析到合并字典），构造期拿到的是回退值 ⇒ 两个入口都必须走这里。
    /// </remarks>
    private void ApplyLineHeight() =>
        _editor.LineHeight = IsWindowMode && _lineHeight > 0 ? _lineHeight : double.NaN;

    /// <summary>把"自建条占不占宽"写给文本行（它是布局输入，写一次就够，重复写会白白重排）。</summary>
    /// <param name="visible">要不要占宽。</param>
    private void SetBarVisible(bool visible)
    {
        if (_scroll.IsVisible == visible && _row.BarVisible == visible)
        {
            return;
        }

        _scroll.IsVisible = visible;
        _row.BarVisible = visible;
        _row.InvalidateMeasure();
    }

    /// <summary>日志用的右键菜单（只读时 Fluent 内置菜单大半是灰的，且不含"复制全部"）。</summary>
    private ContextMenu BuildContextMenu()
    {
        var copy = new MenuItem { Header = "复制" };
        copy.Click += (_, _) => _editor.Copy();

        var copyAll = new MenuItem { Header = "复制全部（缓冲）" };
        copyAll.Click += (_, _) => _ = CopyAllToClipboardAsync();

        var menu = new ContextMenu { ItemsSource = new[] { copy, copyAll } };

        // 菜单开着时焦点会离开文本框，但那**不算**"用户结束交互"（他正要复制选中的内容）
        menu.Closed += (_, _) => Pump();

        return menu;
    }

    private void Attach()
    {
        if (!IsWindowMode || _timer is not null)
        {
            return;
        }

        // 行高从主题读一次（挂上可视树之后才读得到合并字典）—— 它同时决定编辑器与窗口行数。
        _lineHeight = SurfaceTheme.TextLineHeight(this);
        ApplyLineHeight();

        _windowSize = ComputeWindowSize();
        _followTail = true;
        _paused = false;
        _scrolledWhilePaused = false;
        _flushScheduled = false;
        _pending = true;

        _timer = new DispatcherTimer(RefreshInterval, DispatcherPriority.Background, OnTick);
        _timer.Start();

        // 首次填充**立即**做（不等一个节拍）：视图重建时先空一拍会闪一下空白
        FlushWindow();
    }

    private void Detach()
    {
        if (_timer is not null)
        {
            _timer.Stop();
            _timer = null;
        }

        _pending = false;
    }

    private void OnTick(object? sender, EventArgs e) => Pump();

    /// <summary>
    /// 刷新判定的**唯一入口**（定时器节拍与交互事件都走它）：暂停判定 → 恢复补齐 → 节流落地。
    /// </summary>
    private void Pump()
    {
        if (!IsWindowMode || _rewriting)
        {
            return;
        }

        if (IsInteractionPaused)
        {
            // **暂停**：用户正在文本框里选字 ⇒ 绝不重写（数据照旧进缓冲，`_pending` 挂着）
            _paused = true;
            return;
        }

        if (_paused)
        {
            // 交互结束：把暂停期间攒下的内容一次补齐，并**回到跟随尾部**——
            // 除非他在暂停期间自己动过滚动位置（那是在回看历史，不能抢他的位置）。
            _paused = false;

            if (!_scrolledWhilePaused)
            {
                _followTail = true;
            }

            _scrolledWhilePaused = false;
            FlushWindow();
            return;
        }

        if (_pending)
        {
            FlushWindow();
        }
    }

    /// <summary>
    /// 用户是否正在与文本框交互 ⇒ 窗口内容不得被重写。
    /// </summary>
    /// <remarks>
    /// <b>判据收窄为"有选区"</b>：若把"有键盘焦点"也算交互 ⇒ 点一下日志框
    /// （哪怕一个字都没选）日志就**永不刷新**，看起来像"卡住了"。而重写窗口**只有"把选区抽走"这一个真实副作用**
    /// ⇒ 判据就只剩它。
    /// </remarks>
    private bool IsInteractionPaused =>
        _editor.SelectionStart != _editor.SelectionEnd
        || (_editor.ContextMenu?.IsOpen ?? false);

    /// <summary>缓冲有新行/被丢弃/被清空 —— 只**标一下**，落地时机由节拍或"交互结束"决定。</summary>
    private void OnLinesChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        _pending = true;
        ScheduleFlush();
    }

    /// <summary>
    /// 把"**最迟一个节拍之后**落地"排进调度队列：同一拍内来的几十行只会重排**一次**。
    /// </summary>
    /// <remarks>
    /// 除了 50 ms 的 <see cref="DispatcherTimer"/>（"没有新事件时也会推进"的那条腿），这里再排一条
    /// **事件驱动**的腿：追加一到就等到节拍边界落地。两条腿都走 <see cref="Pump"/>，且受同一个
    /// "距上次重排不足一个节拍就先等"的判据约束 ⇒ 幂等，不会把 20 Hz 上限顶破。
    /// （另一条腿在无头测试会话里**不会自己走**，事件驱动的这条才是测试能驱动的路径。）
    /// </remarks>
    private void ScheduleFlush()
    {
        if (_flushScheduled || !IsWindowMode)
        {
            return;
        }

        _flushScheduled = true;
        Dispatcher.UIThread.Post(FlushAtTickBoundary, DispatcherPriority.Background);
    }

    private async void FlushAtTickBoundary()
    {
        _flushScheduled = false;

        if (!IsWindowMode)
        {
            return;
        }

        var remain = RefreshInterval - (DateTime.UtcNow - _lastFlushUtc);
        if (remain > TimeSpan.Zero)
        {
            await Task.Delay(remain);
        }

        Pump();
    }

    private void OnEditorPropertyChanged(object? sender, AvaloniaPropertyChangedEventArgs e)
    {
        if (!IsWindowMode)
        {
            return;
        }

        // 正在**我们自己**重写文本：由此产生的光标/选区变化不是"用户交互"（否则会被误判成"用户在选字"
        // ⇒ 白白暂停一拍，暂停结束时还会把"跟随尾部"强按回去，把用户刚拖到的回看位置顶掉）
        if (_rewriting)
        {
            return;
        }

        if (e.Property == Visual.BoundsProperty)
        {
            // 视口高度变了 ⇒ 窗口行数跟着变（拉伸模块窗口时就该多显示几行）
            var size = ComputeWindowSize();
            if (size != _windowSize)
            {
                _windowSize = size;
                _pending = true;
                Pump();
            }

            return;
        }

        if (e.Property.Name is nameof(TextBox.SelectionStart) or nameof(TextBox.SelectionEnd))
        {
            Pump();
        }
    }

    /// <summary>窗口行数 = 视口放得下的行数 × 2（一屏 + 一页余量），夹在 [1, <see cref="MaxWindowLines"/>]。</summary>
    /// <remarks>
    /// **没有"最少 20 行"这一档**：视口只有几行时把窗口撑到 20 行，等于让窗口内容必然高于视口
    /// ⇒ 内层被迫滚动 ⇒ 与"这一档只有一根看得见的条"直接冲突（屏幕上就是"忽然多出一根条"）。
    /// </remarks>
    private int ComputeWindowSize()
    {
        // 首帧还没布局时 Bounds 为 0 ⇒ 报 1 行，随后 Bounds 变化会再算一次
        var line = _lineHeight > 0 ? _lineHeight : 1;
        var visible = (int)Math.Ceiling(Math.Max(0, _editor.Bounds.Height) / line);
        return Math.Clamp(visible * 2, 1, MaxWindowLines);
    }

    private void OnScrollValueChanged(object? sender, RangeBaseValueChangedEventArgs e)
    {
        if (_suppressScrollEvent)
        {
            return;
        }

        MoveWindow((int)Math.Round(e.NewValue));
    }

    private void OnEditorWheel(object? sender, PointerWheelEventArgs e)
    {
        if (!IsWindowMode)
        {
            return; // `native` 档：滚轮归文本框自己（那是它的条在驱动）
        }

        // **这一档的滚轮一律由窗口消费**：内层那个偏移是私有实现（折行留下的余量），
        // 放行给内层会让"窗口起点"与"实际显示到哪"两套模型错位（同一个滚轮动了两处）。
        e.Handled = true;

        if (Math.Max(0, CountLines() - _windowSize) <= 0)
        {
            return; // 内容不足一屏：滚轮无事可做（也不该把窗口推离底部）
        }

        MoveWindow(_windowStart + (e.Delta.Y > 0 ? -WheelLines : WheelLines));
    }

    /// <summary>把窗口起点移到指定行（拖滚动条与滚轮都走这里）。</summary>
    private void MoveWindow(int start)
    {
        var max = Math.Max(0, CountLines() - _windowSize);
        var clamped = Math.Clamp(start, 0, max);

        // 用户主动移动 = 明确的回看意图：即使此刻有选区也照做，只记下"暂停期间他动过位置"，
        // 好让交互结束时的"回到尾部"不要抢走他正在看的地方。
        if (_paused)
        {
            _scrolledWhilePaused = true;
        }

        _followTail = max <= 0 || clamped >= max;

        if (clamped != _windowStart || !_followTail)
        {
            _windowStart = clamped;
            FlushWindow();
        }
    }

    private int CountLines() => Lines?.Count ?? 0;

    /// <summary>把窗口内容写进文本框（**唯一**会触发整段重排的地方）。</summary>
    /// <remarks>
    /// **必须防重入**：赋 `Text` 会同步触发文本框自己的属性变化通知，而我们的处理器会去重新判定
    /// "现在能不能刷" ⇒ 递归回到这里。重入的那一层用的是**旧的** <c>_windowStart</c>/跟随状态，
    /// 外层接着又把它的结果覆盖掉 ⇒ 状态错乱（实测：拖到顶部后"跟随"标志莫名回弹成 true）。
    /// </remarks>
    private void FlushWindow()
    {
        if (_flushing)
        {
            return;
        }

        _flushing = true;

        try
        {
            FlushWindowCore();
        }
        finally
        {
            _flushing = false;
        }
    }

    private void FlushWindowCore()
    {
        _pending = false;

        var lines = Lines;
        var total = lines?.Count ?? 0;
        var size = _windowSize;
        var maxStart = Math.Max(0, total - size);
        var start = _followTail ? maxStart : Math.Clamp(_windowStart, 0, maxStart);
        var count = Math.Clamp(total - start, 0, size);

        var builder = new StringBuilder();

        // **通道换行的偏移**：窗口文本是用 `\n` 把"行"拼起来的，这些 `\n` 是**通道换行**（不是数据里的字符）
        // ⇒ 显示层据此**不画** `↵`（通道引进的换行不是字符，永远不该有 ↵）。
        var channelBreaks = new List<int>(Math.Max(0, count - 1));

        for (var i = 0; i < count; i++)
        {
            if (i > 0)
            {
                channelBreaks.Add(builder.Length);
                _ = builder.Append('\n');
            }

            _ = builder.Append(lines![start + i]);
        }

        var text = builder.ToString();

        // **只在内容真的变了才赋值**：赋 `Text` 就是一次整段重排（实测 ≈ 0.47 ms/行），
        // 无变化的重排纯属浪费，还会顺手清掉用户的光标与选区。
        if (!string.Equals(_editor.Text, text, StringComparison.Ordinal))
        {
            _rewriting = true;

            try
            {
                _editor.Text = text;
                _marks.UnmarkedNewlines = channelBreaks;

                // 折行会让窗口内容略高于视口 ⇒ 跟随尾部时把光标放到末尾，让文本框自己滚到底，
                // 保证"最新那行真的看得见"；回看时相反，把光标放回开头，让窗口**从顶对齐**（内容才连续）。
                if (text.Length > 0)
                {
                    _editor.CaretIndex = _followTail ? text.Length : 0;
                }
            }
            finally
            {
                _rewriting = false;
            }
        }
        else
        {
            _marks.UnmarkedNewlines = channelBreaks;
        }

        _windowStart = start;
        _lastFlushUtc = DateTime.UtcNow;
        UpdateScrollBarRange(total, size, start);
    }

    private void UpdateScrollBarRange(int total, int size, int start)
    {
        _suppressScrollEvent = true;

        try
        {
            _scroll.Maximum = Math.Max(0, total - size);
            _scroll.ViewportSize = size;
            _scroll.Value = start;
        }
        finally
        {
            _suppressScrollEvent = false;
        }

        // 范围变了 ⇒ 可见性跟着变（连"没什么可滚"的那一根都不该常驻）。
        // 判据只用 `Maximum` 本身（= 总行数 − 窗口行数，唯一事实源），不另存一个"能不能滚"的标志。
        SetBarVisible(IsWindowMode && _scroll.Maximum > 0);
    }

    /// <summary>订阅/退订分页数据源（集合变化 ⇒ 标"待刷新"）。</summary>
    private void ResubscribeLines(IReadOnlyList<string>? lines)
    {
        if (ReferenceEquals(_subscribedLines, lines))
        {
            return;
        }

        if (_subscribedLines is INotifyCollectionChanged previous)
        {
            previous.CollectionChanged -= OnLinesChanged;
        }

        _subscribedLines = lines;

        if (lines is INotifyCollectionChanged current)
        {
            current.CollectionChanged += OnLinesChanged;
        }

        _pending = true;
        Pump();
    }

    /// <summary>把**整段缓冲**放进剪贴板 —— 与"复制选中"分开，因为它是这条视图唯一的"全量"出口。</summary>
    /// <remarks>
    /// <para>
    /// 取数**就是那一刻现读** <see cref="Lines"/>（不是开机时的快照）：缓冲一直在长，
    /// 菜单点下去的时候读到的必须是最新的全量。文本面因此**不需要外部再传一个取数回调**
    /// （不再由外壳视图注入 `Func&lt;string&gt;`——外壳已删掉，取数就地收在本类，见下）。
    /// </para>
    /// <para>
    /// Avalonia 12 的剪贴板是**数据包**口径（<c>SetDataAsync(IDataTransfer)</c>，没有 <c>SetTextAsync</c>）：
    /// 平台侧可能**延迟取数据**，所以这份载荷必须活到被替换为止 ⇒ 存在字段里、下次复制时再释放（否则粘贴拿到空）。
    /// </para>
    /// </remarks>
    private async Task CopyAllToClipboardAsync()
    {
        var clipboard = TopLevel.GetTopLevel(this)?.Clipboard;
        if (clipboard is null)
        {
            return; // 还没接到窗口：静默不做（不是用户能修的错）
        }

        // `DataTransfer` 的 `IDisposable` 是显式实现 ⇒ 释放旧载荷要显式转接口
        if (_clipboardPayload is { } previous)
        {
            ((IDisposable)previous).Dispose();
        }

        _clipboardPayload = new DataTransfer();
        _clipboardPayload.Add(DataTransferItem.CreateText(ReadBufferText()));

        await clipboard.SetDataAsync(_clipboardPayload);
    }

    /// <summary>
    /// 缓冲的全量文本（"复制全部（缓冲）"的内容）：<see cref="Lines"/> 用**换行符**接起来。
    /// </summary>
    /// <remarks>
    /// <see cref="Lines"/> 对分页窗口而言**就是整段缓冲**（窗口只是它的一页）⇒ 这里拿到的就是全量；
    /// 与"文本框里那份窗口文本"不同，后者只有一屏多。
    /// </remarks>
    private string ReadBufferText() =>
        Lines is { Count: > 0 } lines ? string.Join(Environment.NewLine, lines) : string.Empty;

    /// <summary>
    /// 最近一次放进剪贴板的数据包（**平台可能延迟取数** ⇒ 必须活到被下一次复制替换）。
    /// </summary>
    /// <remarks>
    /// 放**静态**字段是有意的：它的寿命就该是"进程级剪贴板载荷"（活到下一次复制），
    /// 而不是某一次视图实例 —— 否则视图重建时会连带把它放掉，粘贴随即拿到空内容。
    /// </remarks>
    private static DataTransfer? _clipboardPayload;
}

/// <summary>
/// **文本面这一行的排布**：文本框在左、自建竖条在右，换行标记层与文本框同格叠放。
/// </summary>
/// <remarks>
/// <para>
/// 它存在的唯一理由是**折行必须是确定的**：`TextBox` 的尺寸规则是 —— **宽度受约束**时报约束宽、
/// 文字在框内折行；**宽度不受约束**时报**整行文本的宽度**。而滚动容器量孩子时会把宽度放宽成
/// "按内容"（`ScrollContentPresenter` 就是这样），于是会折行的文本框报出整行宽（实测 200 字符 ⇒ 2212，
/// 而框只有 360）⇒ **文字冲出框被外层裁掉**，屏幕上却不报任何错：**被裁的是字，不是框**，
/// 所以从框的尺寸上什么也看不出来。
/// </para>
/// <para>
/// 所以宽度**由这里算**（= 本行实得宽 − 竖条占宽），再用这个有限数去量文本框；折行于是只由
/// "这块文本面有多宽"决定，与框架的测量顺序、与父层怎么量本行都无关。
/// 标记层与文本框**同宽同位**：`↵` 是按文本框的行位置算出来的，差一个像素就会画到字外面。
/// </para>
/// </remarks>
internal sealed class SurfaceTextRow : Panel
{
    private readonly TextBox _editor;
    private readonly NewlineMarkOverlay _marks;
    private readonly Border _ring;
    private readonly ScrollBar _scroll;

    /// <summary>建一行。</summary>
    /// <param name="editor">内层文本框。</param>
    /// <param name="marks">行末换行标记绘制层。</param>
    /// <param name="ring">边框叠加层（与编辑器同格，只画那一圈边）。</param>
    /// <param name="scroll">自建竖向滚动条（只有 `window` 档会占宽）。</param>
    public SurfaceTextRow(TextBox editor, NewlineMarkOverlay marks, Border ring, ScrollBar scroll)
    {
        _editor = editor;
        _marks = marks;
        _ring = ring;
        _scroll = scroll;

        Children.Add(editor);
        Children.Add(marks);
        Children.Add(scroll);
        Children.Add(ring);

        HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Stretch;
        VerticalAlignment = Avalonia.Layout.VerticalAlignment.Stretch;
    }

    /// <summary>
    /// **竖条占不占宽**（由 <see cref="TextSurface"/> 唯一写入 —— 它是**布局输入**，不是绘制状态）。
    /// </summary>
    /// <remarks>
    /// 这一格是"本行要不要让出条的位置"的**唯一事实源**：从前这里读的是竖条自己的 `IsVisible`，
    /// 而那个值由节拍线程按数据改 ⇒ 布局输入在两次测量之间变来变去，
    /// 屏幕上就是"文字宽度偶尔跳一下 / 条忽隐忽现"。
    /// </remarks>
    public bool BarVisible { get; set; }

    /// <summary>
    /// **折行与滚动必须是确定的**：宽度/高度都由"本行实得的尺寸"决定，不由内容决定。
    /// </summary>
    /// <remarks>
    /// <para>
    /// `TextBox` 的尺寸规则是：**宽度受约束**时报约束宽、文字在框内折行；**宽度不受约束**时报
    /// **整行文本的宽度**。高度同理 —— 约束高**就是它要填满的高度**，不受约束时它才按内容报。
    /// 而排布链上量"按内容"的元素时给的正是**无穷**（`adaptive`、以及下限为 0 的 `fill`），
    /// 于是会折行的文本框按整行文本报宽（实测 200 字符 ⇒ 2212，而框只有 360）、
    /// 按整份文本报高（实测 60 行 ⇒ 885，而框只有 189）。
    /// </para>
    /// <para>
    /// 后一个后果更隐蔽：**文本框把自己撑到 885，排布层就以为内容真要 885**
    /// ⇒ 内容区与视口一起被撑大 ⇒ 文本框的视口与它的滚动范围**永远一样大** ⇒
    /// **滚动条永远不会出现**（"没得可滚"）。
    /// </para>
    /// <para>
    /// 所以两个轴都在这里夹回约束：宽度 = 本行实得宽 − 竖条占宽；高度 = 约束高。
    /// 文字在框内折行、超出就在框内滚 —— 与框架的测量顺序、与父层怎么量本行都无关。
    /// </para>
    /// <para>
    /// <b>这里绝不读排布结果</b>（例如本行/文本面的 `Bounds`）：那是跨遍读 ——
    /// 第一次测量必然拿到 0 宽，而 `adaptive` 宽的文本面正好走"约束无穷"这一档
    /// ⇒ 整块文本面静默量成 0 宽、屏幕上什么都不显示。
    /// </para>
    /// </remarks>
    protected override Size MeasureOverride(Size availableSize)
    {
        var width = double.IsInfinity(availableSize.Width) ? 0 : Math.Max(0, availableSize.Width);

        // **高度不给"猜"的余地**：约束有限 ⇒ 就是它；约束无穷 ⇒ 报 0，让排布层去定。
        // 这里刻意**不用"上一次排布的高"兜底**：那会把上一次被撑大的高记下来，形成正反馈
        // （报 885 ⇒ 排布层按 885 排 ⇒ 下次还报 885 ⇒ 视口与滚动范围永远一样大，滚动条永不出来）。
        // 报 0 时排布层才知道"这一块的高度得由我决定"，于是 `fill` / `*` / 声明像素各自生效。
        var height = double.IsInfinity(availableSize.Height) ? 0 : availableSize.Height;

        // 竖条按"占不占宽"量（不占时量 0 —— 它也不显示）。
        _scroll.Measure(new Size(double.PositiveInfinity, height));

        var bar = BarVisible ? _scroll.DesiredSize.Width : 0;
        var content = new Size(Math.Max(0, width - bar), height);

        // 文本框拿到的**宽与高都是有限数**：折行与"超出就滚"于是都是确定的 ——
        // 折行由宽度决定，滚动由"内容比这个高多"决定，与框架的测量顺序无关。
        _editor.Measure(content);
        _marks.Measure(content);
        _ring.Measure(content);

        return new Size(width, height);
    }

    /// <inheritdoc />
    protected override Size ArrangeOverride(Size finalSize)
    {
        var bar = BarVisible ? _scroll.DesiredSize.Width : 0;
        var contentWidth = Math.Max(0, finalSize.Width - bar);

        _editor.Arrange(new Rect(0, 0, contentWidth, finalSize.Height));
        _marks.Arrange(new Rect(0, 0, contentWidth, finalSize.Height));

        if (BarVisible)
        {
            _scroll.Arrange(new Rect(contentWidth, 0, bar, finalSize.Height));
        }

        // 边框叠加层铺满**整行**（含条那一格）：它只画那一圈边，不占内容、不进测量。
        _ring.Arrange(new Rect(0, 0, finalSize.Width, finalSize.Height));

        return finalSize;
    }
}
