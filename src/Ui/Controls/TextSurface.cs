using System.Collections.Specialized;
using System.Text;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.Input;

namespace HYWDebugAssistant.Ui.Controls;

/// <summary>
/// **唯一一块文本面**〔2026-09 用户裁定："两者行为基本一致，只是数据写入者/只读属性和滚动条行为不同"〕。
/// </summary>
/// <remarks>
/// <list type="bullet">
///   <item><b>它内部有且只有一个 <see cref="TextBox"/> + 一个 <see cref="NewlineMarkOverlay"/></b>
///     （可选再加自建竖向滚动条 + 分页窗口）⇒ 模块文本**输入框**与 A5 **日志**共用同一份实现，
///     不再各写一套（重复实现 = 测试成本翻倍 + 观感/手感各自漂移）；</item>
///   <item><b>差异只用属性表达</b>：数据来源（<see cref="Lines"/> 模块缓冲 vs <see cref="Text"/> 用户键入）、
///     <see cref="IsReadOnly"/>、滚动（<see cref="ShowScrollBar"/> 自建竖条 + 分页窗口 vs 内层滚动）；</item>
///   <item><b>换行标记 / 字符级选择 / 复制 = 同一份实现</b>：`↵` 一律由 <see cref="NewlineMarkOverlay"/>
///     画在**同格的只读绘制层**里（不进文本、不参与选择、光标跳过 —— 与 Word 的 ¶ 同构）；</item>
///   <item><b>一行多长由文本框宽度决定</b>（自动换行、**无横向滚动条**）—— 数据侧不再规定字节数；
///     数据里**真的有换行字符**的地方才画 `↵`（通道换行不是字符，见 <see cref="NewlineMarkOverlay.UnmarkedNewlines"/>）。</item>
///   <item><b>A5 不再有自己的外壳视图</b>：原先那层"标题 + 外框 + 丢弃提示 + 复制全部回调"的壳
///     （`TextLogView`）在该控件同时支持输入与显示之后**整个删掉**，日志那一块现在就是
///     `ModuleContentView.axaml` 里 A5 的 `DataTemplate` 直接放一个本控件；
///     "复制全部"的取数也就地收回本类（<see cref="Lines"/> 对分页窗口而言**就是整段缓冲**）。</item>
/// </list>
/// <para>
/// <b>分页窗口</b>（<see cref="ShowScrollBar"/> = true，日志用）：实测只读 `TextBox` 的更新代价 ≈ **0.47 ms/行**
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

    /// <summary>最小高度（DIP；0 = 不设 —— 单行框的行为）。</summary>
    public static readonly StyledProperty<double> EditorMinHeightProperty =
        AvaloniaProperty.Register<TextSurface, double>(nameof(EditorMinHeight));

    /// <summary>是否画行末换行标记 `↵`（多行 + 纯文本输入 = true）。</summary>
    public static readonly StyledProperty<bool> ShowNewlineMarksProperty =
        AvaloniaProperty.Register<TextSurface, bool>(nameof(ShowNewlineMarks));

    /// <summary>
    /// 是否**分页窗口 + 自建竖条**（日志 = true；输入框 = false ⇒ 内层滚动条）。
    /// </summary>
    public static readonly StyledProperty<bool> ShowScrollBarProperty =
        AvaloniaProperty.Register<TextSurface, bool>(nameof(ShowScrollBar));

    /// <summary>**分页窗口的数据来源**（模块缓冲的行；仅在 <see cref="ShowScrollBar"/> 下有消费者）。</summary>
    public static readonly StyledProperty<IReadOnlyList<string>?> LinesProperty =
        AvaloniaProperty.Register<TextSurface, IReadOnlyList<string>?>(nameof(Lines));

    /// <summary>失败态（红框样式类 `invalid`）。</summary>
    public static readonly StyledProperty<bool> IsInvalidProperty =
        AvaloniaProperty.Register<TextSurface, bool>(nameof(IsInvalid));

    /// <summary>数值输入（样式类 `numeric`）。</summary>
    public static readonly StyledProperty<bool> IsNumericProperty =
        AvaloniaProperty.Register<TextSurface, bool>(nameof(IsNumeric));

    /// <summary>十六进制输入（样式类 `hex`）。</summary>
    public static readonly StyledProperty<bool> IsHexProperty =
        AvaloniaProperty.Register<TextSurface, bool>(nameof(IsHex));

    /// <summary>显式左对齐（样式类 `alignLeft`）。</summary>
    public static readonly StyledProperty<bool> IsAlignLeftProperty =
        AvaloniaProperty.Register<TextSurface, bool>(nameof(IsAlignLeft));

    /// <summary>居中（样式类 `alignCenter`）。</summary>
    public static readonly StyledProperty<bool> IsAlignCenterProperty =
        AvaloniaProperty.Register<TextSurface, bool>(nameof(IsAlignCenter));

    /// <summary>显式右对齐（样式类 `alignRight`）。</summary>
    public static readonly StyledProperty<bool> IsAlignRightProperty =
        AvaloniaProperty.Register<TextSurface, bool>(nameof(IsAlignRight));

    /// <summary>一行的显示高度（DIP）—— 与样式里的 <c>LineHeight</c> 一致：窗口行数 = "视口放得下几行"。</summary>
    private const double LineHeightDip = 14;

    /// <summary>窗口最少行数（视口再小也留这些行，首屏不至于空）。</summary>
    private const int MinWindowLines = 20;

    /// <summary>窗口最多行数（**性能闸门**）—— 实测 ≈ 0.47 ms/行 ⇒ 一屏 + 一页余量，且绝不越过这个上限。</summary>
    private const int MaxWindowLines = 200;

    /// <summary>刷新节拍（20 Hz）：追加只标"待刷新"，由它统一落地。</summary>
    private static readonly TimeSpan RefreshInterval = TimeSpan.FromMilliseconds(50);

    /// <summary>滚轮一次移动的行数（一屏内的手感粒度，与 WinForms 多行框接近）。</summary>
    private const int WheelLines = 3;

    private readonly TextBox _editor;
    private readonly NewlineMarkOverlay _marks;
    private readonly ScrollBar _scroll;

    private readonly RelayCommand _copyAllCommand;

    /// <summary>正在把**编辑器**的文本回写 <see cref="Text"/>（避免两条同步互相打架）。</summary>
    private bool _syncingText;

    /// <summary>**分页窗口**：窗口行数 / 起点 / 是否跟随尾部。</summary>
    private int _windowSize = MinWindowLines;
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

        _editor = PartEditor;
        _marks = PartMarks;
        _scroll = PartScroll;

        _marks.Editor = _editor;
        _marks.IsActive = false;

        // 标记口径与"这一块面是只读还是可写"对齐（构造时先对一次；`IsReadOnly` 变化时再对一次）
        _marks.MergeEmptyLineMarks = IsReadOnly;

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
        _scroll.ValueChanged += OnScrollValueChanged;

        AttachedToVisualTree += (_, _) => Attach();
        DetachedFromVisualTree += (_, _) => Detach();
    }

    /// <summary>内层**唯一的**文本框（测试与调试读取；输入框的提交策略挂在它冒泡/隧道的路由事件上）。</summary>
    public TextBox Editor => _editor;

    /// <summary>换行标记绘制层（测试与调试读取）。</summary>
    public NewlineMarkOverlay Marks => _marks;

    /// <summary>自建竖向滚动条（**只在分页窗口模式、且真的有回看余地时**才可见 —— 见 <see cref="UpdateScrollBarVisibility"/>）。</summary>
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

    /// <summary>是否分页窗口 + 自建竖条。</summary>
    public bool ShowScrollBar
    {
        get => GetValue(ShowScrollBarProperty);
        set => SetValue(ShowScrollBarProperty, value);
    }

    /// <summary>分页窗口的数据来源（行）。</summary>
    public IReadOnlyList<string>? Lines
    {
        get => GetValue(LinesProperty);
        set => SetValue(LinesProperty, value);
    }

    /// <summary>失败态（红框）。</summary>
    public bool IsInvalid
    {
        get => GetValue(IsInvalidProperty);
        set => SetValue(IsInvalidProperty, value);
    }

    /// <summary>数值输入（字符放行由外壳的隧道处理器判定，这里只带样式类）。</summary>
    public bool IsNumeric
    {
        get => GetValue(IsNumericProperty);
        set => SetValue(IsNumericProperty, value);
    }

    /// <summary>十六进制输入（同上）。</summary>
    public bool IsHex
    {
        get => GetValue(IsHexProperty);
        set => SetValue(IsHexProperty, value);
    }

    /// <summary>显式左对齐。</summary>
    public bool IsAlignLeft
    {
        get => GetValue(IsAlignLeftProperty);
        set => SetValue(IsAlignLeftProperty, value);
    }

    /// <summary>居中。</summary>
    public bool IsAlignCenter
    {
        get => GetValue(IsAlignCenterProperty);
        set => SetValue(IsAlignCenterProperty, value);
    }

    /// <summary>显式右对齐。</summary>
    public bool IsAlignRight
    {
        get => GetValue(IsAlignRightProperty);
        set => SetValue(IsAlignRightProperty, value);
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

            // **空行的标记口径按"哪一块面"分工**（两条都是用户裁定，见 `NewlineMarkOverlay.MergeEmptyLineMarks`）：
            // 只读面 = A5 日志 ⇒ 连续换行的标记并到上一行末尾；可写面 = 模块文本输入框 ⇒ 空行自己显示 `↵`。
            _marks.MergeEmptyLineMarks = readOnly;
            return;
        }

        if (change.Property == IsMultilineProperty)
        {
            var multiline = change.GetNewValue<bool>();
            _editor.AcceptsReturn = multiline;
            SetClass(_editor, "multiline", multiline);
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

        if (change.Property == ShowScrollBarProperty)
        {
            ConfigurePaging(change.GetNewValue<bool>());
            return;
        }

        if (change.Property == LinesProperty)
        {
            ResubscribeLines(change.GetNewValue<IReadOnlyList<string>?>());
            return;
        }

        if (change.Property == IsInvalidProperty)
        {
            SetClass(_editor, "invalid", change.GetNewValue<bool>());
            return;
        }

        if (change.Property == IsNumericProperty)
        {
            SetClass(_editor, "numeric", change.GetNewValue<bool>());
            return;
        }

        if (change.Property == IsHexProperty)
        {
            SetClass(_editor, "hex", change.GetNewValue<bool>());
            return;
        }

        if (change.Property == IsAlignLeftProperty)
        {
            SetClass(_editor, "alignLeft", change.GetNewValue<bool>());
            return;
        }

        if (change.Property == IsAlignCenterProperty)
        {
            SetClass(_editor, "alignCenter", change.GetNewValue<bool>());
            return;
        }

        if (change.Property == IsAlignRightProperty)
        {
            SetClass(_editor, "alignRight", change.GetNewValue<bool>());
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
        if (ShowScrollBar || _syncingText)
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
        if (ShowScrollBar || _syncingText)
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

    // ---------------------------------------------------------------- 分页窗口（日志那一半）

    /// <summary>切换分页窗口模式（开 = 自建竖条 + 窗口；关 = 内层滚动条）。</summary>
    private void ConfigurePaging(bool paged)
    {
        SetClass(_editor, "paged", paged);
        UpdateScrollBarVisibility();

        if (paged)
        {
            _editor.ContextMenu = BuildContextMenu();
            _pending = true;
            Attach();
            return;
        }

        _editor.ContextMenu = null;

        if (_timer is not null)
        {
            _timer.Stop();
            _timer = null;
        }
    }

    /// <summary>
    /// **自建竖条的可见性判据**：分页模式 **且** 缓冲比窗口长（`Maximum &gt; 0`）。
    /// </summary>
    /// <remarks>
    /// 2026-09 用户实测缺陷修复：此前竖条只按"是不是分页模式"显示 ⇒ **没得可滚时也常驻一根**，
    /// 用户读成"这根条没用"（日志现在是流式追加，绝大多数时候确实没什么可回看的）。
    /// 判据就用 <see cref="ScrollBar.Maximum"/> 本身（= 总行数 − 窗口行数，唯一事实源），
    /// 不另存一个"能不能滚"的标志 ⇒ 两个来源迟早各说一套。
    /// </remarks>
    private void UpdateScrollBarVisibility() => _scroll.IsVisible = ShowScrollBar && _scroll.Maximum > 0;

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
        if (!ShowScrollBar || _timer is not null)
        {
            return;
        }

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
        if (!ShowScrollBar || _rewriting)
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
    /// <b>2026-09 用户裁定：判据收窄为"有选区"</b>。此前把"有键盘焦点"也算交互 ⇒ 用户点一下日志框
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
        if (_flushScheduled || !ShowScrollBar)
        {
            return;
        }

        _flushScheduled = true;
        Dispatcher.UIThread.Post(FlushAtTickBoundary, DispatcherPriority.Background);
    }

    private async void FlushAtTickBoundary()
    {
        _flushScheduled = false;

        if (!ShowScrollBar)
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
        if (!ShowScrollBar)
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

    /// <summary>窗口行数 = 视口放得下的行数 × 2（一屏 + 一页余量），夹在 [<see cref="MinWindowLines"/>, <see cref="MaxWindowLines"/>]。</summary>
    private int ComputeWindowSize()
    {
        // 首帧还没布局时 Bounds 为 0 ⇒ 退到最小窗口，随后 Bounds 变化会再算一次
        var visible = (int)Math.Ceiling(Math.Max(0, _editor.Bounds.Height) / LineHeightDip);
        return Math.Clamp(visible * 2, MinWindowLines, MaxWindowLines);
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
        if (!ShowScrollBar)
        {
            return;
        }

        if (Math.Max(0, CountLines() - _windowSize) <= 0)
        {
            return; // 内容不足一屏：滚轮无事可做（也不该把窗口推离底部）
        }

        e.Handled = true;
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
        // ⇒ 显示层据此**不画** `↵`（用户裁定："通道引进的换行不是字符，永远不该有 ↵"）。
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
        UpdateScrollBar(total, size, start);
    }

    private void UpdateScrollBar(int total, int size, int start)
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

        // 范围变了 ⇒ 可见性跟着变（连"没什么可滚"的那一根都不该常驻）
        UpdateScrollBarVisibility();
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
        _followTail = true;
        Pump();
    }

    /// <summary>把**整段缓冲**放进剪贴板 —— 与"复制选中"分开，因为它是这条视图唯一的"全量"出口。</summary>
    /// <remarks>
    /// <para>
    /// 取数**就是那一刻现读** <see cref="Lines"/>（不是开机时的快照）：缓冲一直在长，
    /// 菜单点下去的时候读到的必须是最新的全量。文本面因此**不需要外部再传一个取数回调**
    /// （旧版由外壳视图注入 `Func&lt;string&gt;`，外壳删掉后就地收回来，见下）。
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
