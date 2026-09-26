using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.VisualTree;
using HYWDebugAssistant.Ui.Geometry;
using HYWDebugAssistant.Ui.ViewModels;

namespace HYWDebugAssistant.Ui.Controls;

/// <summary>
/// **叠加信息层**【ADR-0039 D5 → **ADR-0045 重设计**】：调试 / 观测用的附加信息层，**默认关闭**
/// （快捷键 <c>L</c> 切换；用户裁决："从用户理解上这不是某种应该常驻的层"）。
/// </summary>
/// <remarks>
/// <list type="bullet">
///   <item><b>玻璃</b>：由 XAML 的 <c>OverlayScrim</c>（半透明深色 Border）提供 —— 即"模块上盖一层灰玻璃"的
///     最简实现（同"弹窗时主窗口变暗"的常见做法，不做背景模糊）。本控件只画**图形与文字**，
///     故"玻璃浓度"仍是 XAML/token 里的一行；</item>
///   <item><b>层级</b>：本控件排在**端口标记之下**（ADR-0045 修订 ADR-0039 的层级）：标记是唯一建网入口，
///     必须保持清晰、可双击；连线从中心穿过标记带、在**窗口边界**与对端相接；</item>
///   <item><b>几何</b>：起点 = 该口**数据类型对应的中心方块**中心，终点 = **窗口边界**上端口带中心线
///     （旧实现从"标记中心"画到边界 ⇒ 线只有 <c>Band/2 = 10px</c>，看起来像坏掉 —— 本轮修掉）；</item>
///   <item><b>颜色</b>：线色由**数据流方向**决定（发送 = 发送色、接收 = 接收色），并沿"中心 → 边界"渐变到
///     **两端方向色的中点色**（<see cref="MidColor"/>）—— 两个窗口各自画自己那半段，却因为**同一条中点色规则**
///     在公共边颜色**恰好相接**（ADR-0045 D4）；</item>
///   <item><b>流动</b>：粗虚线 + <see cref="OverlayAnimationClock"/> 的共享 20Hz 时钟推进
///     <c>DashStyle.Offset</c> ⇒ "看起来在流动"，方向与数据流一致（发送向外、接收向内）；</item>
///   <item>本层 <c>IsHitTestVisible=false</c>：不参与命中（双击改网络名、拖动、缩放照常）。</item>
/// </list>
/// </remarks>
public sealed class ConnectionOverlay : Control
{
    // ------------------------------------------------------------------ 调参常量（改观感只改这里）

    /// <summary>连线粗细（DIP）：1 px 在 125% 缩放下太细，视觉上"像断了"。</summary>
    public const double LineThickness = 2.5;

    /// <summary>虚线段长（DIP）。</summary>
    public const double DashLength = 6;

    /// <summary>虚线间隙（DIP）。</summary>
    public const double DashGap = 4;

    /// <summary>端部流向箭头大小（DIP）。</summary>
    public const double ArrowSize = 6;

    /// <summary>中心类型方块边长（DIP）。</summary>
    public const double TypeBoxSize = 14;

    /// <summary>类型方块之间的水平间距（DIP）。</summary>
    public const double TypeBoxGap = 10;

    /// <summary>类型文本与方块底边的间距（DIP）。</summary>
    public const double TypeLabelGap = 2;

    /// <summary>多行类型方块之间的行距（DIP）。</summary>
    public const double TypeRowGap = 12;

    /// <summary>文字与窗口边缘的安全距离（DIP）——避开 20 px 的端口标记带。</summary>
    public const double Gutter = 24;

    /// <summary>未连接的口：短虚线残段长度（DIP）。</summary>
    public const double UnconnectedStubLength = 26;

    /// <summary>未连接残段的透明度。</summary>
    public const double UnconnectedAlpha = 0.4;

    /// <summary>未拼接时末端淡出长度（DIP）。</summary>
    public const double FadeLength = 14;

    /// <summary>标题（`实例 · 模块名`）字号。</summary>
    public const double CaptionFontSize = 12;

    /// <summary>网络名 / 类型文本字号。</summary>
    public const double LabelFontSize = 11;

    /// <summary>速率 / 对端 / 汇总字号。</summary>
    public const double SmallFontSize = 10;

    /// <summary>线标签沿线位置（0 = 中心方块，1 = 窗口边界）。</summary>
    public const double LabelLerp = 0.45;

    // ------------------------------------------------------------------ 属性

    /// <summary>端口标记集合（类型方块、连线端点与信息都由它派生）。</summary>
    public static readonly StyledProperty<IReadOnlyList<PortMarkerViewModel>?> PortsProperty =
        AvaloniaProperty.Register<ConnectionOverlay, IReadOnlyList<PortMarkerViewModel>?>(nameof(Ports));

    /// <summary>是否绘制文字信息（图形始终绘制）。</summary>
    public static readonly StyledProperty<bool> ShowLabelsProperty =
        AvaloniaProperty.Register<ConnectionOverlay, bool>(nameof(ShowLabels), defaultValue: true);

    /// <summary>**发送**方向色（线色渐变起点 + 该侧箭头色）——ADR-0044/0045。</summary>
    public static readonly StyledProperty<IBrush?> SendArrowBrushProperty =
        AvaloniaProperty.Register<ConnectionOverlay, IBrush?>(nameof(SendArrowBrush));

    /// <summary>**接收**方向色（同上）。</summary>
    public static readonly StyledProperty<IBrush?> ReceiveArrowBrushProperty =
        AvaloniaProperty.Register<ConnectionOverlay, IBrush?>(nameof(ReceiveArrowBrush));

    /// <summary>叠层主文字色（标题 / 网络名 / 类型文本）。</summary>
    public static readonly StyledProperty<IBrush?> InkBrushProperty =
        AvaloniaProperty.Register<ConnectionOverlay, IBrush?>(nameof(InkBrush));

    /// <summary>叠层次文字色（速率 / 对端 / 汇总 / "未连"）。</summary>
    public static readonly StyledProperty<IBrush?> InkMutedBrushProperty =
        AvaloniaProperty.Register<ConnectionOverlay, IBrush?>(nameof(InkMutedBrush));

    /// <summary>模块实例名（标题左半）。</summary>
    public static readonly StyledProperty<string?> InstanceIdProperty =
        AvaloniaProperty.Register<ConnectionOverlay, string?>(nameof(InstanceId));

    /// <summary>模块标题（标题右半）。</summary>
    public static readonly StyledProperty<string?> ModuleTitleProperty =
        AvaloniaProperty.Register<ConnectionOverlay, string?>(nameof(ModuleTitle));

    /// <summary>任一绘制属性变化即重绘（缺它会"改了颜色不刷新" —— P39）。</summary>
    static ConnectionOverlay() => AffectsRender<ConnectionOverlay>(
        PortsProperty,
        ShowLabelsProperty,
        SendArrowBrushProperty,
        ReceiveArrowBrushProperty,
        InkBrushProperty,
        InkMutedBrushProperty,
        InstanceIdProperty,
        ModuleTitleProperty);

    /// <summary>端口标记集合。</summary>
    public IReadOnlyList<PortMarkerViewModel>? Ports
    {
        get => GetValue(PortsProperty);
        set => SetValue(PortsProperty, value);
    }

    /// <summary>是否绘制文字信息。</summary>
    public bool ShowLabels
    {
        get => GetValue(ShowLabelsProperty);
        set => SetValue(ShowLabelsProperty, value);
    }

    /// <summary>发送方向色。</summary>
    public IBrush? SendArrowBrush
    {
        get => GetValue(SendArrowBrushProperty);
        set => SetValue(SendArrowBrushProperty, value);
    }

    /// <summary>接收方向色。</summary>
    public IBrush? ReceiveArrowBrush
    {
        get => GetValue(ReceiveArrowBrushProperty);
        set => SetValue(ReceiveArrowBrushProperty, value);
    }

    /// <summary>叠层主文字色。</summary>
    public IBrush? InkBrush
    {
        get => GetValue(InkBrushProperty);
        set => SetValue(InkBrushProperty, value);
    }

    /// <summary>叠层次文字色。</summary>
    public IBrush? InkMutedBrush
    {
        get => GetValue(InkMutedBrushProperty);
        set => SetValue(InkMutedBrushProperty, value);
    }

    /// <summary>模块实例名。</summary>
    public string? InstanceId
    {
        get => GetValue(InstanceIdProperty);
        set => SetValue(InstanceIdProperty, value);
    }

    /// <summary>模块标题。</summary>
    public string? ModuleTitle
    {
        get => GetValue(ModuleTitleProperty);
        set => SetValue(ModuleTitleProperty, value);
    }

    // ------------------------------------------------------------------ 内部状态

    /// <summary>端口标记带厚度（与 <see cref="PortMarkerPanel.BandThickness"/> 一致：太小不画）。</summary>
    private const double Band = PortMarkerPanel.BandThickness;

    /// <summary>方向色取不到纯色时的兜底线色。</summary>
    private static readonly Color FallbackDirection = Color.Parse("#94A3B8");

    /// <summary>发送方向的虚线（所有同向线条共用，只改 <c>Offset</c>，不重建画刷）。</summary>
    private readonly DashStyle _sendDash = new([DashLength, DashGap], 0);

    /// <summary>接收方向的虚线。</summary>
    private readonly DashStyle _receiveDash = new([DashLength, DashGap], 0);

    /// <summary>文本缓存（`(文本, 字号, 画刷)` → 已排版文本）：动画每帧只改偏移，文本不该每帧重排。</summary>
    private readonly Dictionary<(string Text, double Size, IBrush Brush), FormattedText> _textCache = [];

    /// <summary>一个**类型方块**（模块中心处，每个数据类型一个）。</summary>
    /// <param name="TypeText">类型文本（如 <c>f32</c>）。</param>
    /// <param name="Box">方块矩形（窗口局部坐标）。</param>
    public readonly record struct TypeBox(string TypeText, Rect Box);

    // ------------------------------------------------------------------ 纯几何（可单独断言）

    /// <summary>
    /// **类型方块组**：模块中心处每个数据类型一个方块，水平居中；放不下时自动换行。
    /// </summary>
    /// <param name="ports">模块的所有口。</param>
    /// <param name="width">窗口宽（DIP）。</param>
    /// <param name="height">窗口高（DIP）。</param>
    /// <returns>方块列表（顺序 = 类型首次出现顺序）。</returns>
    public static IReadOnlyList<TypeBox> TypeBoxesOf(IReadOnlyList<PortMarkerViewModel> ports, double width, double height)
    {
        ArgumentNullException.ThrowIfNull(ports);

        var types = new List<string>();
        foreach (var port in ports)
        {
            if (!types.Contains(port.TypeText, StringComparer.Ordinal))
            {
                types.Add(port.TypeText);
            }
        }

        if (types.Count == 0)
        {
            return [];
        }

        var available = Math.Max(TypeBoxSize, width - (Gutter * 2));
        var perRow = Math.Max(1, (int)Math.Floor((available + TypeBoxGap) / (TypeBoxSize + TypeBoxGap)));
        var rows = (int)Math.Ceiling(types.Count / (double)perRow);
        var rowHeight = TypeBoxSize + TypeLabelGap + LabelFontSize + 2;
        var totalHeight = (rows * rowHeight) + ((rows - 1) * TypeRowGap);

        var boxes = new List<TypeBox>(types.Count);
        var top = (height - totalHeight) / 2;

        for (var index = 0; index < types.Count; index++)
        {
            var row = index / perRow;
            var column = index % perRow;
            var inRow = Math.Min(perRow, types.Count - (row * perRow));
            var rowWidth = (inRow * TypeBoxSize) + ((inRow - 1) * TypeBoxGap);
            var left = ((width - rowWidth) / 2) + (column * (TypeBoxSize + TypeBoxGap));
            var boxTop = top + (row * (rowHeight + TypeRowGap));

            boxes.Add(new TypeBox(types[index], new Rect(left, boxTop, TypeBoxSize, TypeBoxSize)));
        }

        return boxes;
    }

    /// <summary>取某个类型方块的**中心**（找不到时回退到模块中心）。</summary>
    /// <param name="boxes">方块组。</param>
    /// <param name="typeText">类型文本。</param>
    /// <param name="width">窗口宽。</param>
    /// <param name="height">窗口高。</param>
    /// <returns>方块中心。</returns>
    public static Point BoxCenter(IReadOnlyList<TypeBox> boxes, string typeText, double width, double height)
    {
        ArgumentNullException.ThrowIfNull(boxes);

        foreach (var box in boxes)
        {
            if (string.Equals(box.TypeText, typeText, StringComparison.Ordinal))
            {
                return box.Box.Center;
            }
        }

        return new Point(width / 2, height / 2);
    }

    /// <summary>
    /// 某个口连线的**起点（中心方块）与终点（窗口边界）**。
    /// </summary>
    /// <param name="port">口。</param>
    /// <param name="boxes">类型方块组。</param>
    /// <param name="width">窗口宽。</param>
    /// <param name="height">窗口高。</param>
    /// <returns>起点 / 终点（窗口局部坐标）。</returns>
    public static (Point Start, Point End) LineOf(
        PortMarkerViewModel port,
        IReadOnlyList<TypeBox> boxes,
        double width,
        double height)
    {
        ArgumentNullException.ThrowIfNull(port);

        var offset = Math.Clamp(port.Offset, 0, 1);
        var start = BoxCenter(boxes, port.TypeText, width, height);

        var end = port.Side switch
        {
            PortSide.Right => new Point(width, height * offset),
            PortSide.Left => new Point(0, height * offset),
            PortSide.Bottom => new Point(width * offset, height),
            _ => new Point(width * offset, 0),
        };

        return (start, end);
    }

    /// <summary>
    /// 两端方向色的**中点色**（拼接处两侧都用它 ⇒ 颜色必然连续）。
    /// </summary>
    /// <param name="send">发送方向色。</param>
    /// <param name="receive">接收方向色。</param>
    /// <returns>50% 混合色。</returns>
    public static Color MidColor(Color send, Color receive) => Color.FromArgb(
        (byte)((send.A + receive.A) / 2),
        (byte)((send.R + receive.R) / 2),
        (byte)((send.G + receive.G) / 2),
        (byte)((send.B + receive.B) / 2));

    // ------------------------------------------------------------------ 动画订阅（只在"可见 + 已挂到视觉树"时订阅）

    /// <inheritdoc />
    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        SyncAnimationSubscription();
    }

    /// <inheritdoc />
    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnDetachedFromVisualTree(e);
        OverlayAnimationClock.Unregister(this);
    }

    /// <inheritdoc />
    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);

        if (change.Property == IsVisibleProperty)
        {
            SyncAnimationSubscription();
        }
    }

    /// <summary>时钟每 tick 回调：只在**真正可见**时重绘（隐藏的窗口不烧 CPU）。</summary>
    internal void OnAnimationTick()
    {
        if (IsEffectivelyVisible)
        {
            InvalidateVisual();
        }
    }

    private void SyncAnimationSubscription()
    {
        if (IsVisible && this.IsAttachedToVisualTree())
        {
            OverlayAnimationClock.Register(this);
        }
        else
        {
            OverlayAnimationClock.Unregister(this);
        }
    }

    // ------------------------------------------------------------------ 绘制

    /// <inheritdoc />
    public override void Render(DrawingContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        if (Ports is not { Count: > 0 } ports)
        {
            return;
        }

        var width = Bounds.Width;
        var height = Bounds.Height;
        if (width < Band * 3 || height < Band * 3)
        {
            return;
        }

        var boxes = TypeBoxesOf(ports, width, height);

        // 虚线相位：发送向外、接收向内（两个方向互为相反数）
        var (sendOffset, receiveOffset) = OverlayAnimationClock.DashOffsets(OverlayAnimationClock.TickCount);
        _sendDash.Offset = sendOffset;
        _receiveDash.Offset = receiveOffset;

        var sendColor = ColorOf(SendArrowBrush) ?? FallbackDirection;
        var receiveColor = ColorOf(ReceiveArrowBrush) ?? FallbackDirection;
        var mid = MidColor(sendColor, receiveColor);
        var ink = InkBrush ?? Brushes.White;
        var inkColor = ColorOf(ink) ?? Colors.White;
        var muted = InkMutedBrush ?? ink;

        // ① 连线（从中心方块到窗口边界；未连接的画短残段）
        foreach (var port in ports)
        {
            var (start, end) = LineOf(port, boxes, width, height);
            var own = port.IsSend ? sendColor : receiveColor;
            var dash = port.IsSend ? _sendDash : _receiveDash;

            if (!port.HasLink || port.PeerSide is not { } peerSide)
            {
                // 未连接：短虚线残段 + "未连"（D7：没连上本身就是调试信息）
                var stub = Shorten(start, end, UnconnectedStubLength);
                var stubPen = new Pen(Alpha(own, UnconnectedAlpha), LineThickness)
                {
                    DashStyle = dash,
                    LineCap = PenLineCap.Flat,
                };

                context.DrawLine(stubPen, start, stub);

                if (ShowLabels)
                {
                    // 【用户第十六轮反馈】原写法固定往右 4px ⇒ 左边缘的口会把"未连"压在中心方块上（截图可见）。
                    // 现在按**端口所在边**朝**窗口外**一侧摆，并做窗口内钳制；文字用**不透明**方向色（原来 60% 太暗）。
                    var label = Format("未连", SmallFontSize, Alpha(own, 1));
                    var origin = UnconnectedLabelOrigin(port.Side, stub, label.Width, width, height);
                    context.DrawText(label, origin);
                }

                continue;
            }

            var flow = port.IsSend ? peerSide : Opposite(peerSide);

            if (port.PeerAdjacent)
            {
                // 已拼接：直接渐变到中点色（对端那半段从同一个中点色继续 ⇒ 公共边颜色相接）
                var pen = new Pen(Gradient(start, end, own, mid), LineThickness)
                {
                    DashStyle = dash,
                    LineCap = PenLineCap.Flat,
                };

                context.DrawLine(pen, start, end);
            }
            else
            {
                // 未拼接：末端淡出（本窗内线条"出窗即止"，不装作连上了）
                var pen = new Pen(Gradient(start, end, own, mid, transparentTail: true), LineThickness)
                {
                    DashStyle = dash,
                    LineCap = PenLineCap.Flat,
                };

                context.DrawLine(pen, start, end);
            }

            DrawArrow(context, end, flow, new SolidColorBrush(own));

            if (!ShowLabels)
            {
                continue;
            }

            // 网络名 + 速率：沿线 45% 处（多口时自然散开）
            var anchor = Lerp(start, end, LabelLerp);
            var name = Format(port.NetworkText, LabelFontSize, ink);
            var nameX = Math.Clamp(anchor.X - (name.Width / 2), Gutter / 2, Math.Max(Gutter / 2, width - name.Width - (Gutter / 2)));
            context.DrawText(name, new Point(nameX, anchor.Y - (LabelFontSize + 6)));

            var rate = Format(port.RateText, SmallFontSize, muted);
            var rateX = Math.Clamp(anchor.X - (rate.Width / 2), Gutter / 2, Math.Max(Gutter / 2, width - rate.Width - (Gutter / 2)));
            context.DrawText(rate, new Point(rateX, anchor.Y - (SmallFontSize + 1)));

            // 对端：只在**未拼接**时标注（拼接后线会延续到邻窗，再标一次会压在缝上）
            if (!port.PeerAdjacent && port.PeerText is { Length: > 0 } peer)
            {
                DrawPeer(context, peer, end, peerSide, width, muted);
            }
        }

        // ② 类型方块（画在连线之上，方块本身表达"这个模块有哪几种数据类型"）
        foreach (var box in boxes)
        {
            // 方块填充 = 该类型的语义色 @85%（类型色映射待用户给出；未映射时是中性占位色）
            var typeColor = ColorOf(PacketTypePalette.For(box.TypeText)) ?? FallbackDirection;
            var outline = new Pen(Alpha(inkColor, 0.65), 1);
            context.DrawRectangle(Alpha(typeColor, 0.85), outline, box.Box, 2, 2);

            if (ShowLabels)
            {
                var text = Format(box.TypeText, LabelFontSize, ink);
                context.DrawText(text, new Point(box.Box.Center.X - (text.Width / 2), box.Box.Bottom + TypeLabelGap));
            }
        }

        if (!ShowLabels)
        {
            return;
        }

        // ③ 标题（左上）与汇总（右下）
        var caption = $"{InstanceId} · {ModuleTitle}";
        if (!string.IsNullOrWhiteSpace(caption.Trim(' ', '·')))
        {
            context.DrawText(Format(caption, CaptionFontSize, ink), new Point(Gutter, 8));
        }

        var summary = Format(Summary(ports), SmallFontSize, muted);
        context.DrawText(summary, new Point(Math.Max(Gutter, width - summary.Width - Gutter), height - SmallFontSize - 10));
    }

    /// <summary>汇总文本（投递 / 丢弃；**按网络去重**，避免同一网络在多口上重复计数）。</summary>
    private static string Summary(IReadOnlyList<PortMarkerViewModel> ports)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        long delivered = 0;
        long dropped = 0;

        foreach (var port in ports)
        {
            if (!port.HasLink)
            {
                continue;
            }

            var key = port.NetworkName is { Length: > 0 } name ? name : "（直连）";
            if (seen.Add(key))
            {
                delivered += port.Delivered;
                dropped += port.Dropped;
            }
        }

        return seen.Count == 0 ? "无连接" : $"投递 {delivered:N0} · 丢弃 {dropped:N0}";
    }

    /// <summary>
    /// **"未连"标签的原点**：摆在该口所在边的**外端**（远离模块中心/类型方块），并钳制在窗口内。
    /// </summary>
    /// <remarks>
    /// 【用户第十六轮反馈】原实现固定写在残段右侧 4px，左边缘的口因此把文字压到中心方块上（截图可见）。
    /// 规则：右边缘的口 → 文字在残段**右侧**；左边缘 → 文字在残段**左侧**（右对齐到残段）；
    /// 上/下边缘（当前框架未使用，为将来留口）同理朝外。文字始终画在连线上方（下边缘除外）。
    /// </remarks>
    /// <param name="side">口所在边。</param>
    /// <param name="stub">残段外端。</param>
    /// <param name="textWidth">文字宽度。</param>
    /// <param name="width">窗口宽。</param>
    /// <param name="height">窗口高。</param>
    /// <returns>文字绘制原点（左上角）。</returns>
    public static Point UnconnectedLabelOrigin(PortSide side, Point stub, double textWidth, double width, double height)
    {
        const double Gap = 4;

        var x = side switch
        {
            PortSide.Right => stub.X + Gap,
            PortSide.Left => stub.X - Gap - textWidth,
            _ => stub.X - (textWidth / 2),
        };

        var y = side switch
        {
            PortSide.Bottom => stub.Y + Gap,
            _ => stub.Y - (SmallFontSize + Gap),
        };

        return new Point(
            Math.Clamp(x, 2, Math.Max(2, width - textWidth - 2)),
            Math.Clamp(y, 2, Math.Max(2, height - SmallFontSize - 2)));
    }

    /// <summary>在连线的窗口侧端部标注对端 `实例.口名`（仅未拼接时；**避开 20px 标记带**）。</summary>
    private void DrawPeer(DrawingContext context, string text, Point end, PortSide side, double width, IBrush brush)
    {
        var formatted = Format(text, SmallFontSize, brush);

        // 【用户第十六轮/第十七轮】贴在窗口边缘会被标记（20px 带）压住/遮挡 ⇒ 内收到**标记带以内**
        var x = side switch
        {
            PortSide.Right => end.X - Band - formatted.Width - 6,
            PortSide.Left => end.X + Band + 6,
            _ => end.X + 4,
        };

        x = Math.Clamp(x, 2, Math.Max(2, width - formatted.Width - 2));

        var y = side switch
        {
            PortSide.Top => end.Y + Band + 4,
            PortSide.Bottom => end.Y - Band - formatted.Height - 4,
            _ => end.Y + 3,
        };

        y = Math.Clamp(y, 2, double.MaxValue);

        context.DrawText(formatted, new Point(x, y));
    }

    /// <summary>在端部画一个流向箭头（按口方向取色）。</summary>
    private static void DrawArrow(DrawingContext context, Point tip, PortSide flow, IBrush brush)
    {
        var size = ArrowSize;
        var geometry = new StreamGeometry();

        using (var sink = geometry.Open())
        {
            switch (flow)
            {
                case PortSide.Right:
                    sink.BeginFigure(new Point(tip.X, tip.Y), isFilled: true);
                    sink.LineTo(new Point(tip.X - size, tip.Y - (size / 1.6)));
                    sink.LineTo(new Point(tip.X - size, tip.Y + (size / 1.6)));
                    break;

                case PortSide.Left:
                    sink.BeginFigure(new Point(tip.X, tip.Y), isFilled: true);
                    sink.LineTo(new Point(tip.X + size, tip.Y - (size / 1.6)));
                    sink.LineTo(new Point(tip.X + size, tip.Y + (size / 1.6)));
                    break;

                case PortSide.Bottom:
                    sink.BeginFigure(new Point(tip.X, tip.Y), isFilled: true);
                    sink.LineTo(new Point(tip.X - (size / 1.6), tip.Y - size));
                    sink.LineTo(new Point(tip.X + (size / 1.6), tip.Y - size));
                    break;

                default:
                    sink.BeginFigure(new Point(tip.X, tip.Y), isFilled: true);
                    sink.LineTo(new Point(tip.X - (size / 1.6), tip.Y + size));
                    sink.LineTo(new Point(tip.X + (size / 1.6), tip.Y + size));
                    break;
            }

            sink.EndFigure(true);
        }

        context.DrawGeometry(brush, null, geometry);
    }

    // ------------------------------------------------------------------ 小工具

    /// <summary>沿连线方向的渐变：本端方向色 → **中点色**（可选末端透明 = 未拼接时淡出）。</summary>
    private static LinearGradientBrush Gradient(Point start, Point end, Color own, Color mid, bool transparentTail = false)
    {
        var brush = new LinearGradientBrush
        {
            StartPoint = new RelativePoint(start, RelativeUnit.Absolute),
            EndPoint = new RelativePoint(end, RelativeUnit.Absolute),
        };

        brush.GradientStops.Add(new GradientStop(own, 0));
        brush.GradientStops.Add(new GradientStop(mid, transparentTail ? 0.82 : 1));

        if (transparentTail)
        {
            brush.GradientStops.Add(new GradientStop(Color.FromArgb(0, mid.R, mid.G, mid.B), 1));
        }

        return brush;
    }

    /// <summary>同色但指定透明度的画刷（0 = 全透明、1 = 不透明）。</summary>
    private static SolidColorBrush Alpha(Color color, double alpha) =>
        new(Color.FromArgb((byte)Math.Clamp(255 * alpha, 0, 255), color.R, color.G, color.B));

    /// <summary>画刷的实色（非纯色画刷返回 null，调用方回退）。</summary>
    private static Color? ColorOf(IBrush? brush) => brush is ISolidColorBrush solid ? solid.Color : null;

    /// <summary>两点之间按比例取点。</summary>
    private static Point Lerp(Point start, Point end, double t) =>
        new(start.X + ((end.X - start.X) * t), start.Y + ((end.Y - start.Y) * t));

    /// <summary>从 <paramref name="start"/> 朝 <paramref name="end"/> 方向取固定长度（不足则取到终点）。</summary>
    private static Point Shorten(Point start, Point end, double length)
    {
        var dx = end.X - start.X;
        var dy = end.Y - start.Y;
        var distance = Math.Sqrt((dx * dx) + (dy * dy));

        if (distance <= length || distance <= 0)
        {
            return end;
        }

        return new Point(start.X + (dx * length / distance), start.Y + (dy * length / distance));
    }

    /// <summary>排版文本（按 `(文本, 字号, 画刷)` 缓存：动画每帧只改虚线相位，文本不必重排）。</summary>
    private FormattedText Format(string text, double size, IBrush brush)
    {
        if (_textCache.TryGetValue((text, size, brush), out var cached))
        {
            return cached;
        }

        if (_textCache.Count > 256)
        {
            _textCache.Clear();
        }

        var formatted = new FormattedText(
            text,
            CultureInfo.CurrentCulture,
            FlowDirection.LeftToRight,
            Typeface.Default,
            size,
            brush);

        _textCache[(text, size, brush)] = formatted;
        return formatted;
    }

    private void DrawText(DrawingContext context, string text, Point origin, double size, IBrush brush) =>
        context.DrawText(Format(text, size, brush), origin);

    /// <summary>两方向相反侧。</summary>
    private static PortSide Opposite(PortSide side) => side switch
    {
        PortSide.Left => PortSide.Right,
        PortSide.Right => PortSide.Left,
        PortSide.Top => PortSide.Bottom,
        _ => PortSide.Top,
    };
}
