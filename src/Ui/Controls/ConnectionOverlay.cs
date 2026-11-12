using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.VisualTree;
using HYWDebugAssistant.Sdk.Ui.Geometry;
using HYWDebugAssistant.Ui.Geometry;
using HYWDebugAssistant.Ui.ViewModels;

namespace HYWDebugAssistant.Ui.Controls;

/// <summary>
/// **叠加信息层**：模块的**功能分块**（角色）+ 角色间的数据流 + **角色自身的信息文字**。
/// </summary>
/// <remarks>
/// <list type="bullet">
///   <item><b>画什么</b>：**角色方块** = 模块声明的功能分块（左入右出，**宽度随文本自适应**）·
///     **角色间数据流线**（两个角色方块**中心**之间的线段，各窗自裁）·
///     **角色自身的信息文字**（速率 / 投递·丢弃 / 类型语义，画在方块**上方**，**不画文本框**）· 左上身份；</item>
///   <item><b>不画什么</b>：**网络标签**（那是**接口层**的概念，由**端口标记**表达 —— 两者不是同一层级）·
///     类型长文本 · "几个口/几条连接"的文字（图上可见）· 模块级汇总（同一事实不两处表达）·
///     **线上一律不写文字**（线上的字会被读成"这条通路的信息"，与"角色自身的信息"混淆）；</item>
///   <item><b>两个信息维度</b>：**角色维度**（这块功能自己在跑多快）画在方块上；
///     **通路维度**（这条网络在跑多快）画在**端口标记**上 —— 读同一份框架诊断，但表达对象不同，
///     因此在界面上**各有一处**出处，且接口层在叠加层里被灰玻璃统一压暗（不做单独降权样式）；</item>
///   <item><b>线的几何</b>：一条线 = **本端角色方块中心 ↔ 对端角色方块中心**（两个点，两端用**同一套**布局算，
///     故两个窗口各画自己窗内那段时**严格共线**）；绘制时裁掉落在**本端方块内部**的那一段
///     （从方块边界出发）⇒ 视觉上"从方块中心出发"，且不会在方块里留一道淡线；
///     虚线的绘制方向**恒为数据流方向**（发送：方块 → 窗外；接收：窗外 → 方块），故两侧虚线看起来**一个向外、一个向内**；</item>
///   <item><b>未拼接的末端</b>：**不淡出**，改为在边界处画**断口竖杠** + 对端名 —— 表达"线被窗口裁断，另一端在窗外"；</item>
///   <item><b>层级与开关</b>：默认关闭（快捷键 `L`）、灰玻璃遮罩、排在端口标记**之下**、**不参与命中**、
///     共享 60 Hz 时钟（关闭即停表）。</item>
/// </list>
/// </remarks>
public sealed class ConnectionOverlay : Control
{
    // ------------------------------------------------------------------ 调参常量（改观感只改这里）

    /// <summary>角色间数据流线粗细（DIP）。</summary>
    public const double LineThickness = 2.5;

    /// <summary>虚线段长（DIP）。</summary>
    public const double DashLength = 6;

    /// <summary>虚线间隙（DIP）。</summary>
    public const double DashGap = 4;

    /// <summary>端部流向箭头大小（DIP）。</summary>
    public const double ArrowSize = 6;

    /// <summary>角色方块高度（DIP）——**两行文本 + 内边距**（宽度随文本自适应，见 <see cref="RoleTextMetrics"/>）。</summary>
    public const double RoleBoxHeight = 34;

    /// <summary>同列内角色方块间距（DIP）。</summary>
    public const double RoleBoxGap = 14;

    /// <summary>多列时的列间距（DIP）。</summary>
    public const double RoleBoxColumnGap = 16;

    /// <summary>角色组与窗口上下边缘的留白（DIP）。</summary>
    public const double RoleBoxMargin = 16;

    /// <summary>角色方块圆角（DIP）。</summary>
    public const double RoleBoxRadius = 3;

    /// <summary>接口边的宽（DIP）——左=输入、右=输出，用**方向色**画粗边。</summary>
    public const double RoleSideThickness = 3;

    /// <summary>接口边上的**口数刻痕**长（DIP；每口一条，&gt;1 口时画）。</summary>
    public const double RolePortTickLength = 5;

    /// <summary>角色方块填充的不透明度（留出底下的玻璃）。</summary>
    public const double RoleFillAlpha = 0.85;

    /// <summary>角色信息文字与方块之间的间隙（DIP）。</summary>
    public const double RoleInfoGap = 5;

    /// <summary>角色信息行距（DIP）。</summary>
    public const double RoleInfoLineGap = 2;

    /// <summary>**内部处理顺序**线的粗细（DIP）——比对外线细：它是**结构**，不是流量。</summary>
    public const double OrderLineThickness = 1.2;

    /// <summary>未拼接时末端**断口竖杠**的高（DIP）。</summary>
    public const double BreakMarkLength = 9;

    /// <summary>自回环横向外挑（DIP）——从输出边绕到输入边的弧线最远伸出方块多少。</summary>
    public const double SelfLoopReach = 14;

    /// <summary>自回环弧顶相对方块边缘的期望留白（DIP）；空间不足时按可用空间收窄（贴方块的半环）。</summary>
    public const double SelfLoopApexGap = 11;

    /// <summary>自回环与窗口边缘的最小安全距离（DIP）——保证环与标记不出窗。</summary>
    public const double SelfLoopMargin = 6;

    /// <summary>自回环标记半径（DIP）——只表达"数据回到自己"，不承载指标。</summary>
    public const double SelfLoopMarkerRadius = 3.5;

    /// <summary>文字与窗口边缘的安全距离（DIP）——避开 20 px 的端口标记带。</summary>
    public const double Gutter = 24;

    /// <summary>标题（`实例 · 模块名`）字号。</summary>
    public const double CaptionFontSize = 12;

    /// <summary>方块标题 / 角色速率字号。</summary>
    public const double LabelFontSize = RoleTextMetrics.TitleFontSize;

    /// <summary>`m/n` / 角色次要信息字号。</summary>
    public const double SmallFontSize = RoleTextMetrics.DetailFontSize;

    // ------------------------------------------------------------------ 属性

    /// <summary>**角色方块**清单（功能分块；位置与线的锚点都由它派生）。</summary>
    public static readonly StyledProperty<IReadOnlyList<RoleBoxViewModel>?> RolesProperty =
        AvaloniaProperty.Register<ConnectionOverlay, IReadOnlyList<RoleBoxViewModel>?>(nameof(Roles));

    /// <summary>**角色间数据流**清单（一条 = 一条线；已按角色对聚合）。</summary>
    public static readonly StyledProperty<IReadOnlyList<RoleLinkViewModel>?> RoleLinksProperty =
        AvaloniaProperty.Register<ConnectionOverlay, IReadOnlyList<RoleLinkViewModel>?>(nameof(RoleLinks));

    /// <summary>**内部处理顺序**清单（`角色 → 角色`；零声明 ⇒ 空）。</summary>
    public static readonly StyledProperty<IReadOnlyList<RoleOrderViewModel>?> RoleOrderProperty =
        AvaloniaProperty.Register<ConnectionOverlay, IReadOnlyList<RoleOrderViewModel>?>(nameof(RoleOrder));

    /// <summary>合称（左上第一行；由视图模型算好）。</summary>
    public static readonly StyledProperty<string?> IdentityTextProperty =
        AvaloniaProperty.Register<ConnectionOverlay, string?>(nameof(IdentityText));

    /// <summary>左上第二行的**说明**（零口 ⇒ `无数据口`；有口未声明角色 ⇒ 提示补声明；否则为空）。</summary>
    public static readonly StyledProperty<string?> NoticeTextProperty =
        AvaloniaProperty.Register<ConnectionOverlay, string?>(nameof(NoticeText));

    /// <summary>是否绘制文字（图形始终绘制）。</summary>
    public static readonly StyledProperty<bool> ShowLabelsProperty =
        AvaloniaProperty.Register<ConnectionOverlay, bool>(nameof(ShowLabels), defaultValue: true);

    /// <summary>**发送**方向色（输出侧接口边 / 发送段线色 / 箭头）。</summary>
    public static readonly StyledProperty<IBrush?> SendArrowBrushProperty =
        AvaloniaProperty.Register<ConnectionOverlay, IBrush?>(nameof(SendArrowBrush));

    /// <summary>**接收**方向色（输入侧接口边 / 接收段线色）。</summary>
    public static readonly StyledProperty<IBrush?> ReceiveArrowBrushProperty =
        AvaloniaProperty.Register<ConnectionOverlay, IBrush?>(nameof(ReceiveArrowBrush));

    /// <summary>**内部处理顺序**线色（中性低对比；与对外线明确区分）。</summary>
    public static readonly StyledProperty<IBrush?> FlowLineBrushProperty =
        AvaloniaProperty.Register<ConnectionOverlay, IBrush?>(nameof(FlowLineBrush));

    /// <summary>叠层主文字色。</summary>
    public static readonly StyledProperty<IBrush?> InkBrushProperty =
        AvaloniaProperty.Register<ConnectionOverlay, IBrush?>(nameof(InkBrush));

    /// <summary>叠层次文字色。</summary>
    public static readonly StyledProperty<IBrush?> InkMutedBrushProperty =
        AvaloniaProperty.Register<ConnectionOverlay, IBrush?>(nameof(InkMutedBrush));

    /// <summary>
    /// **叠层文字字体**（叠层自绘 `FormattedText` 用的字体族）。
    /// </summary>
    /// <remarks>
    /// <b>若写死 <c>Typeface.Default</c>，设置里的数据字体就毫无作用</b>（自绘层是"改了没生效"的第二现场）。
    /// 现在与颜色**同一手法**：由 XAML 用 <c>{DynamicResource FontData}</c> 注入（颜色也全是这么进来的），
    /// 未注入时回退 <see cref="Typeface.Default"/>（老 XAML / 无头用例不至于画不出字）。
    /// <para>字体变了必须**清文字缓存**（<c>_textCache</c> 的键是"文本 + 字号 + 画刷"，不含字体）。</para>
    /// </remarks>
    public static readonly StyledProperty<FontFamily?> DataFontFamilyProperty =
        AvaloniaProperty.Register<ConnectionOverlay, FontFamily?>(nameof(DataFontFamily));

    /// <summary>模块实例名（左上；也可由 <see cref="IdentityText"/> 直接给整行）。</summary>
    public static readonly StyledProperty<string?> InstanceIdProperty =
        AvaloniaProperty.Register<ConnectionOverlay, string?>(nameof(InstanceId));

    /// <summary>模块标题。</summary>
    public static readonly StyledProperty<string?> ModuleTitleProperty =
        AvaloniaProperty.Register<ConnectionOverlay, string?>(nameof(ModuleTitle));

    /// <summary>
    /// **画布宽**（DIP）—— **必须 = `ModuleViewViewModel.Width`**（几何的唯一事实源，见 <see cref="CanvasWidthProperty"/> 说明）。
    /// </summary>
    public static readonly StyledProperty<double> CanvasWidthProperty =
        AvaloniaProperty.Register<ConnectionOverlay, double>(nameof(CanvasWidth));

    /// <summary>**画布高**（DIP）—— 必须 = `ModuleViewViewModel.Height`。</summary>
    public static readonly StyledProperty<double> CanvasHeightProperty =
        AvaloniaProperty.Register<ConnectionOverlay, double>(nameof(CanvasHeight));

    /// <summary>任一绘制属性变化即重绘（缺它会"改了颜色不刷新" —— P39）。</summary>
    static ConnectionOverlay() => AffectsRender<ConnectionOverlay>(
        RolesProperty,
        RoleLinksProperty,
        RoleOrderProperty,
        IdentityTextProperty,
        NoticeTextProperty,
        ShowLabelsProperty,
        SendArrowBrushProperty,
        ReceiveArrowBrushProperty,
        FlowLineBrushProperty,
        InkBrushProperty,
        InkMutedBrushProperty,
        DataFontFamilyProperty,
        InstanceIdProperty,
        ModuleTitleProperty,
        CanvasWidthProperty,
        CanvasHeightProperty);

    /// <summary>角色方块清单。</summary>
    public IReadOnlyList<RoleBoxViewModel>? Roles
    {
        get => GetValue(RolesProperty);
        set => SetValue(RolesProperty, value);
    }

    /// <summary>角色间数据流清单。</summary>
    public IReadOnlyList<RoleLinkViewModel>? RoleLinks
    {
        get => GetValue(RoleLinksProperty);
        set => SetValue(RoleLinksProperty, value);
    }

    /// <summary>内部处理顺序清单。</summary>
    public IReadOnlyList<RoleOrderViewModel>? RoleOrder
    {
        get => GetValue(RoleOrderProperty);
        set => SetValue(RoleOrderProperty, value);
    }

    /// <summary>左上第一行（`实例 · 模块名`）。</summary>
    public string? IdentityText
    {
        get => GetValue(IdentityTextProperty);
        set => SetValue(IdentityTextProperty, value);
    }

    /// <summary>左上第二行说明（可空）。</summary>
    public string? NoticeText
    {
        get => GetValue(NoticeTextProperty);
        set => SetValue(NoticeTextProperty, value);
    }

    /// <summary>是否绘制文字。</summary>
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

    /// <summary>内部顺序线色。</summary>
    public IBrush? FlowLineBrush
    {
        get => GetValue(FlowLineBrushProperty);
        set => SetValue(FlowLineBrushProperty, value);
    }

    /// <summary>主文字色。</summary>
    public IBrush? InkBrush
    {
        get => GetValue(InkBrushProperty);
        set => SetValue(InkBrushProperty, value);
    }

    /// <summary>次文字色。</summary>
    public IBrush? InkMutedBrush
    {
        get => GetValue(InkMutedBrushProperty);
        set => SetValue(InkMutedBrushProperty, value);
    }

    /// <summary>叠层文字字体（由 XAML 注入 `{DynamicResource FontData}`；null = 用系统默认）。</summary>
    public FontFamily? DataFontFamily
    {
        get => GetValue(DataFontFamilyProperty);
        set => SetValue(DataFontFamilyProperty, value);
    }

    /// <summary>
    /// **画布宽**（**DIP**）—— **必须 = `ModuleViewViewModel.CanvasWidth`**（= 模型物理像素 ÷ 缩放）。
    /// </summary>
    /// <remarks>
    /// <b>为什么需要它（用户第三十四轮："连线仍然没有对齐……多套坐标系相互冲突，请确定唯一事实源"）</b>：
    /// <list type="number">
    ///   <item>叠加层控件挂在 `Frame` 边框**之内** ⇒ `Bounds` 比窗口小 **2 DIP**；而对端方块的位置是**另一侧**用
    ///     `LayoutRoles(peer.Roles, peer.Canvas…)` 算的 ⇒ 若一端用 `Bounds`、另一端用视图模型尺寸，两端**布局尺寸不同** ⇒ 线错位；</item>
    ///   <item>更隐蔽的是**单位**：模型几何（`ModuleViewViewModel.X/Y/Width/Height`）是**物理像素**
    ///     （窗口位置本身就是 `PixelPoint`），而控件绘制是 **DIP** ⇒ 缩放 ≠ 100% 时两者差一个缩放因子，
    ///     "完全水平/垂直的线"也会错位。⇒ **叠加层几何一律 DIP**，px 只在视图模型的两处换算。</item>
    /// </list>
    /// ⇒ 本属性由视图模型绑定提供；`Bounds` 只在**未绑定**时兜底。
    /// </remarks>
    public double CanvasWidth
    {
        get => GetValue(CanvasWidthProperty);
        set => SetValue(CanvasWidthProperty, value);
    }

    /// <summary>**画布高**（DIP）—— 必须 = `ModuleViewViewModel.CanvasHeight`。</summary>
    public double CanvasHeight
    {
        get => GetValue(CanvasHeightProperty);
        set => SetValue(CanvasHeightProperty, value);
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

    // ------------------------------------------------------------------ 几何类型

    /// <summary>一个**角色方块**（窗口局部坐标）：左边缘 = 输入侧、右边缘 = 输出侧。</summary>
    /// <param name="RoleKey">角色键（回查视图模型用）。</param>
    /// <param name="InputCount">输入口数（0 = 该侧没有接口）。</param>
    /// <param name="OutputCount">输出口数（0 = 该侧没有接口）。</param>
    /// <param name="Box">方块矩形。</param>
    public readonly record struct RoleBox(string RoleKey, int InputCount, int OutputCount, Rect Box)
    {
        /// <summary>是否有输入侧（有才画左侧接口边与刻痕）。</summary>
        public bool HasInput => InputCount > 0;

        /// <summary>是否有输出侧。</summary>
        public bool HasOutput => OutputCount > 0;

        /// <summary>方块中心——**连线的端点**（用户第三十四轮裁定："连线从角色方块中心出发"）。</summary>
        public Point Center => Box.Center;
    }

    /// <summary>角色布局结果。</summary>
    /// <param name="Boxes">已摆放的方块（按声明顺序，单列或多列）。</param>
    /// <param name="Hidden">放不下、未画出的角色数（供 `+K` 徽标）。</param>
    /// <param name="BoxHeight">方块高（各块一致；**宽度随文本自适应**，逐块不同）。</param>
    public readonly record struct RoleLayout(
        IReadOnlyList<RoleBox> Boxes,
        int Hidden,
        double BoxHeight);

    /// <summary>
    /// **自回环几何**（DIP）：从方块**输出侧边缘**出发，绕到**输入侧边缘**。
    /// </summary>
    /// <remarks>
    /// 用**折线 + 圆角**而不是单条三次贝塞尔：单条贝塞尔从边缘出发后会先"切进"方块（端点处切线方向
    /// 朝内），方块角上会露出一小段压进方块的线；折线路径在右侧先外挑、再上行，整条曲线都在方块外。
    /// </remarks>
    /// <param name="Points">折线点（起点 = 输出侧边缘，终点 = 输入侧边缘；全部在方块外）。</param>
    /// <param name="Apex">环顶（`↻` 标记画在这里）。</param>
    /// <param name="Degraded">空间不足 ⇒ 退化为贴方块的半环。</param>
    /// <param name="Below">环画在方块下方（上方没有可用空间时的回退）。</param>
    public readonly record struct SelfLoopGeometry(
        IReadOnlyList<Point> Points,
        Point Apex,
        bool Degraded,
        bool Below)
    {
        /// <summary>输出侧边缘上的起点。</summary>
        public Point Start => Points is { Count: > 0 } points ? points[0] : default;

        /// <summary>输入侧边缘上的终点。</summary>
        public Point End => Points is { Count: > 0 } points ? points[^1] : default;

        /// <summary>是否可画（至少两点、端点有限且非零长度）。</summary>
        public bool IsUsable =>
            Points is { Count: > 1 }
            && double.IsFinite(Start.X) && double.IsFinite(Start.Y)
            && double.IsFinite(End.X) && double.IsFinite(End.Y)
            && Math.Abs(Start.X - End.X) > 0.01;

        /// <summary>折线总长（DIP；用于"非零长度"判据）。</summary>
        public double Length()
        {
            var length = 0.0;

            for (var i = 1; i < (Points?.Count ?? 0); i++)
            {
                var dx = Points![i].X - Points[i - 1].X;
                var dy = Points[i].Y - Points[i - 1].Y;
                length += Math.Sqrt((dx * dx) + (dy * dy));
            }

            return length;
        }

        /// <summary>沿折线按长度比例取点（0 ⇒ 起点，1 ⇒ 终点）。</summary>
        /// <param name="t">参数（0–1）。</param>
        public Point PointAt(double t)
        {
            if (Points is not { Count: > 0 } points)
            {
                return default;
            }

            if (points.Count == 1)
            {
                return points[0];
            }

            var total = Length();

            if (total <= 0)
            {
                return points[0];
            }

            var target = Math.Clamp(t, 0, 1) * total;
            var walked = 0.0;

            for (var i = 1; i < points.Count; i++)
            {
                var dx = points[i].X - points[i - 1].X;
                var dy = points[i].Y - points[i - 1].Y;
                var segment = Math.Sqrt((dx * dx) + (dy * dy));

                if (segment <= 0)
                {
                    continue;
                }

                if (walked + segment >= target)
                {
                    var k = (target - walked) / segment;

                    return new Point(
                        points[i - 1].X + (dx * k),
                        points[i - 1].Y + (dy * k));
                }

                walked += segment;
            }

            return points[^1];
        }
    }

    // ------------------------------------------------------------------ 内部状态

    /// <summary>端口标记带厚度（与 <see cref="PortMarkerPanel.BandThickness"/> 一致）。</summary>
    private const double Band = PortMarkerPanel.BandThickness;

    /// <summary>**绝对色**：方向色取不到纯色时的兜底线色（只在"主题资源缺失"时出现）。</summary>
    private static readonly Color FallbackDirection = Color.Parse("#94A3B8");

    /// <summary>发送方向的虚线（同向线条共用，只改 <c>Offset</c>）。</summary>
    private readonly DashStyle _sendDash = new([DashLength, DashGap], 0);

    /// <summary>接收方向的虚线。</summary>
    private readonly DashStyle _receiveDash = new([DashLength, DashGap], 0);

    /// <summary>文本缓存（`(文本, 字号, 画刷)` → 已排版文本）：动画每帧只改相位，文本不该每帧重排。</summary>
    private readonly Dictionary<(string Text, double Size, IBrush Brush), FormattedText> _textCache = [];

    // ------------------------------------------------------------------ 纯几何（可单独断言）

    /// <summary>
    /// **角色方块布局**：**单列竖直堆叠、居中**（每块左入右出，**宽度随文本自适应**）；放不下 ⇒ 加列 → 报未画出的数量。
    /// </summary>
    /// <remarks>
    /// ① 宽度不能固定 128，而由 <see cref="RoleBoxViewModel.BoxWidth"/>
    /// （<see cref="RoleTextMetrics"/> 的确定性估算）给出 ⇒ "块跟着文字走"；
    /// ② 去掉"先缩小方块再隐藏"的中间档（文字自适应后已无意义）——放不下就**如实报出** `+K`，不静默丢弃；
    /// ③ 块高统一，故多列的容量计算仍按"每列几个"来算。
    /// </remarks>
    /// <param name="roles">角色清单（声明顺序）。</param>
    /// <param name="width">窗口宽（DIP）。</param>
    /// <param name="height">窗口高（DIP）。</param>
    /// <returns>布局结果。</returns>
    public static RoleLayout LayoutRoles(IReadOnlyList<RoleBoxViewModel> roles, double width, double height)
    {
        ArgumentNullException.ThrowIfNull(roles);

        if (roles.Count == 0)
        {
            return new RoleLayout([], 0, RoleBoxHeight);
        }

        const double boxHeight = RoleBoxHeight;

        var availableHeight = Math.Max(boxHeight, height - (RoleBoxMargin * 2));
        var availableWidth = Math.Max(RoleTextMetrics.MinBoxWidth, width - (Gutter * 2));

        var perColumn = Math.Max(1, (int)Math.Floor((availableHeight + RoleBoxGap) / (boxHeight + RoleBoxGap)));
        var hidden = Math.Max(0, roles.Count - perColumn);
        var shown = Math.Min(roles.Count, perColumn);

        // 一列放得下就一列；放不下才按"每列能放几个"分列（列宽 = 该列最宽的块）
        var columns = Math.Max(1, (int)Math.Ceiling(roles.Count / (double)perColumn));
        var columnWidths = new double[columns];

        for (var index = 0; index < shown; index++)
        {
            var column = index / perColumn;
            columnWidths[column] = Math.Max(columnWidths[column], roles[index].BoxWidth);
        }

        // 列数受可用宽度限制：超出 ⇒ 少画几列（未画出的角色计入 `+K`）
        var fittingColumns = 0;
        var used = 0.0;

        foreach (var columnWidth in columnWidths)
        {
            var next = fittingColumns == 0 ? columnWidth : used + RoleBoxColumnGap + columnWidth;
            if (next > availableWidth)
            {
                break;
            }

            used = next;
            fittingColumns++;
        }

        if (fittingColumns == 0)
        {
            // 连一列都放不下（窗口极窄）：只画第一个块，其余如实报出
            fittingColumns = 1;
            columnWidths[0] = Math.Min(columnWidths[0], availableWidth);
            shown = Math.Min(shown, perColumn);
            hidden = roles.Count - shown;
        }
        else
        {
            var capacity = fittingColumns * perColumn;
            shown = Math.Min(shown, capacity);
            hidden = roles.Count - shown;
        }

        var groupWidth = columnWidths.Take(fittingColumns).Sum() + (RoleBoxColumnGap * (fittingColumns - 1));
        var groupLeft = Math.Max(0, (width - groupWidth) / 2);
        var boxes = new List<RoleBox>(shown);

        for (var index = 0; index < shown; index++)
        {
            var column = index / perColumn;
            var row = index % perColumn;
            var inColumn = Math.Min(perColumn, shown - (column * perColumn));
            var columnHeight = (inColumn * boxHeight) + ((inColumn - 1) * RoleBoxGap);
            var top = (height - columnHeight) / 2;

            var colLeft = groupLeft;
            for (var c = 0; c < column; c++)
            {
                colLeft += columnWidths[c] + RoleBoxColumnGap;
            }

            var role = roles[index];
            var boxWidth = columnWidths[column];

            boxes.Add(new RoleBox(
                role.Key,
                role.Inputs.Count,
                role.Outputs.Count,
                new Rect(colLeft, top + (row * (boxHeight + RoleBoxGap)), boxWidth, boxHeight)));
        }

        return new RoleLayout(boxes, Math.Max(0, hidden), boxHeight);
    }

    /// <summary>
    /// **一条线在本窗内的那一段**（按**数据流方向**给出起点与终点）。
    /// </summary>
    /// <remarks>
    /// <list type="bullet">
    ///   <item><b>两个端点</b>：本端**角色方块中心** ↔ 对端**角色方块中心**（连线从角色方块中心出发）；</item>
    ///   <item><b>绘制时裁掉方块内部</b>：起点/终点落在**本端方块边界**（朝对端的那一点）⇒
    ///     线看起来从方块中心方向出发，又不会在方块里留一道淡线、更不会横穿方块；</item>
    ///   <item><b>方向恒为数据流方向</b>：**发送** ⇒ 方块边界 → 窗外；**接收** ⇒ 窗外 → 方块边界。
    ///     虚线相位因此只需一个值（<see cref="OverlayAnimationClock.DashOffset"/>），
    ///     两侧看起来自然"一个向外、一个向内"；</item>
    ///   <item><b>共线</b>：两端用同一个函数、同一套布局算 ⇒ 两窗各画的那段落在同一条直线上。</item>
    /// </list>
    /// </remarks>
    /// <param name="window">本窗矩形（**局部坐标**，原点恒为 0,0）。</param>
    /// <param name="box">本端角色方块（局部坐标）。</param>
    /// <param name="peerCenter">对端角色方块中心（已换算到本窗局部坐标）。</param>
    /// <param name="localIsSend">本端是否为发送侧。</param>
    /// <returns>按数据流方向排列的（起点, 终点）；整段不可见时两点相同。</returns>
    public static (Point Start, Point End) LinkSegment(Rect window, Rect box, Point peerCenter, bool localIsSend)
    {
        var (near, far) = ClipSegment(window, box.Center, peerCenter);

        if (Math.Abs(near.X - far.X) < 0.01 && Math.Abs(near.Y - far.Y) < 0.01)
        {
            return (near, far);   // 整段都在窗外（防御：调用方跳过绘制）
        }

        // 本端方块边界上、朝对端的那一点；对端落进方块里时退回中心（避免出现反向的线段）
        var edge = box.Contains(far) ? box.Center : EdgePoint(box, far);

        return localIsSend ? (edge, far) : (far, edge);
    }

    /// <summary>
    /// **线段裁剪**：取线段"本端锚点 → 对端锚点"落在**本窗矩形内**的那一段。
    /// </summary>
    /// <remarks>
    /// 两端用**同一套**布局算出**同一条线段**，各窗只画自己窗内的部分 ⇒
    /// 两段**严格共线**：相邻时无缝接上，未拼接时只在两窗之间缺一段（而不是两根错位的线）。
    /// </remarks>
    /// <param name="window">本窗矩形（局部坐标，通常 0,0,W,H）。</param>
    /// <param name="from">线段起点（本端锚点，通常在本窗内）。</param>
    /// <param name="to">线段终点（对端锚点，通常在本窗外）。</param>
    /// <returns>落在窗内的那一段（完全在窗外 ⇒ 两端点相同，调用方跳过绘制）。</returns>
    public static (Point Near, Point Far) ClipSegment(Rect window, Point from, Point to)
    {
        var dx = to.X - from.X;
        var dy = to.Y - from.Y;

        if (Math.Abs(dx) < double.Epsilon && Math.Abs(dy) < double.Epsilon)
        {
            return (from, from);
        }

        double t0 = 0, t1 = 1;

        if (!Clip(-dx, from.X - window.Left, ref t0, ref t1)
            || !Clip(dx, window.Right - from.X, ref t0, ref t1)
            || !Clip(-dy, from.Y - window.Top, ref t0, ref t1)
            || !Clip(dy, window.Bottom - from.Y, ref t0, ref t1))
        {
            return (from, from);
        }

        return (
            new Point(from.X + (dx * t0), from.Y + (dy * t0)),
            new Point(from.X + (dx * t1), from.Y + (dy * t1)));

        static bool Clip(double p, double q, ref double t0, ref double t1)
        {
            if (Math.Abs(p) < double.Epsilon)
            {
                return q >= 0;   // 平行于该边界：在界内才保留
            }

            var r = q / p;

            if (p < 0)
            {
                if (r > t1)
                {
                    return false;
                }

                if (r > t0)
                {
                    t0 = r;
                }
            }
            else
            {
                if (r < t0)
                {
                    return false;
                }

                if (r < t1)
                {
                    t1 = r;
                }
            }

            return true;
        }
    }

    /// <summary>从矩形中心朝 <paramref name="target"/> 射出，与矩形边界的交点。</summary>
    /// <param name="box">矩形。</param>
    /// <param name="target">目标点。</param>
    /// <returns>边界交点（重合时返回中心）。</returns>
    public static Point EdgePoint(Rect box, Point target)
    {
        var center = box.Center;
        var dx = target.X - center.X;
        var dy = target.Y - center.Y;

        if (Math.Abs(dx) < double.Epsilon && Math.Abs(dy) < double.Epsilon)
        {
            return center;
        }

        var scaleX = Math.Abs(dx) < double.Epsilon ? double.PositiveInfinity : (box.Width / 2) / Math.Abs(dx);
        var scaleY = Math.Abs(dy) < double.Epsilon ? double.PositiveInfinity : (box.Height / 2) / Math.Abs(dy);
        var scale = Math.Min(scaleX, scaleY);

        return new Point(center.X + (dx * scale), center.Y + (dy * scale));
    }

    /// <summary>
    /// **自回环几何**（纯函数、DIP）：本端角色方块**输出侧边缘 → 输入侧边缘**，弧顶在方块上方
    /// （上方不够时退化为贴方块的半环；仍不够则落到下方）。所有点都在**本窗内、方块外**。
    /// </summary>
    /// <remarks>
    /// <b>单位纪律</b>：输入/输出全部是 **DIP**（与 <see cref="LayoutRoles"/> / <see cref="CanvasWidth"/> 同源）；
    /// 本函数**不做任何物理像素换算**。
    /// </remarks>
    /// <param name="window">本窗矩形（局部坐标，DIP）。</param>
    /// <param name="box">本端角色方块（局部坐标，DIP）。</param>
    /// <param name="preferBelow">角色文本在上方时传 true，把环放到文字的另一侧。</param>
    /// <returns>自回环几何；输入退化时 <see cref="SelfLoopGeometry.IsUsable"/> 为 false。</returns>
    public static SelfLoopGeometry ComputeSelfLoop(Rect window, Rect box, bool preferBelow = false)
    {
        if (!(window.Width > 0) || !(window.Height > 0) || !(box.Width > 0) || !(box.Height > 0))
        {
            return default;
        }

        // 标记半径 + 安全距离：弧顶必须能在窗内画出 `↻`，不得贴边/出窗。
        var clearance = SelfLoopMargin + SelfLoopMarkerRadius;
        var above = box.Top - window.Top - clearance;
        var below = window.Bottom - box.Bottom - clearance;

        // **默认上方**（"从输出绕到输入"的常态）；角色文本在上方时优先下方，避开文字块。
        const double minGap = 2;
        var useAbove = preferBelow
            ? below < minGap && above >= below
            : above >= minGap || above >= below;
        var available = useAbove ? above : below;
        var gap = Math.Clamp(available, 0.5, SelfLoopApexGap);

        // 横向外挑受窗内可用空间约束（贴边方块 ⇒ 外挑收窄；即使 0 也仍是贴边的半环）。
        var horizontalRoom = Math.Min(box.Left - window.Left, window.Right - box.Right) - SelfLoopMargin;
        var reach = Math.Clamp(horizontalRoom, 0.5, SelfLoopReach);
        var corner = Math.Min(Math.Min(reach, gap), Math.Min(box.Height / 3, 8));
        var centerY = box.Center.Y;
        var edgeY = useAbove ? box.Top - gap : box.Bottom + gap;
        var sign = useAbove ? -1.0 : 1.0;

        var points = new List<Point>(32) { new(box.Right, centerY) };

        void Line(Point point) => points.Add(point);

        // 圆角：从 from 经 vertex 弯到 to（二次贝塞尔采样；弯内始终在方块外）。
        void Turn(Point from, Point vertex, Point to)
        {
            const int Steps = 6;

            for (var i = 1; i <= Steps; i++)
            {
                var t = i / (double)Steps;
                var u = 1 - t;
                points.Add(new Point(
                    (u * u * from.X) + (2 * u * t * vertex.X) + (t * t * to.X),
                    (u * u * from.Y) + (2 * u * t * vertex.Y) + (t * t * to.Y)));
            }
        }

        // 右侧：输出边 → 外挑 → 上行。
        Line(new Point(box.Right + reach - corner, centerY));
        Turn(
            new Point(box.Right + reach - corner, centerY),
            new Point(box.Right + reach, centerY),
            new Point(box.Right + reach, centerY + (sign * corner)));
        Line(new Point(box.Right + reach, edgeY - (sign * corner)));
        Turn(
            new Point(box.Right + reach, edgeY - (sign * corner)),
            new Point(box.Right + reach, edgeY),
            new Point(box.Right + reach - corner, edgeY));

        // 顶部/底部横段。
        Line(new Point(box.Left - reach + corner, edgeY));
        Turn(
            new Point(box.Left - reach + corner, edgeY),
            new Point(box.Left - reach, edgeY),
            new Point(box.Left - reach, edgeY - (sign * corner)));

        // 左侧：下行 → 回到输入边。
        Line(new Point(box.Left - reach, centerY + (sign * corner)));
        Turn(
            new Point(box.Left - reach, centerY + (sign * corner)),
            new Point(box.Left - reach, centerY),
            new Point(box.Left - reach + corner, centerY));
        Line(new Point(box.Left, centerY));

        return new SelfLoopGeometry(
            points,
            new Point(box.Center.X, edgeY),
            Degraded: gap < SelfLoopApexGap - 0.001,
            Below: !useAbove);
    }

    /// <summary>两端方向色的**中点色**（拼接处两侧都用它 ⇒ 颜色必然连续）。</summary>
    /// <param name="send">发送方向色。</param>
    /// <param name="receive">接收方向色。</param>
    /// <returns>50% 混合色。</returns>
    public static Color MidColor(Color send, Color receive) => Color.FromArgb(
        (byte)((send.A + receive.A) / 2),
        (byte)((send.R + receive.R) / 2),
        (byte)((send.G + receive.G) / 2),
        (byte)((send.B + receive.B) / 2));

    // ------------------------------------------------------------------ 动画订阅

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

        // 字体换了 ⇒ 缓存里的 `FormattedText` 全是旧字体的（键不含字体，故必须整体丢弃）。
        if (change.Property == DataFontFamilyProperty)
        {
            _textCache.Clear();
        }
    }

    /// <summary>时钟每 tick 回调：只在**真正可见**时重绘。</summary>
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

        // **画布尺寸的唯一事实源 = 视图模型声明的 Width/Height**（见 CanvasWidth 说明）：
        // 对端方块的位置也是用同一对数字算的 ⇒ 两端才不会错位。未绑定时退回控件自身尺寸（兜底）。
        var width = CanvasWidth > 0 ? CanvasWidth : Bounds.Width;
        var height = CanvasHeight > 0 ? CanvasHeight : Bounds.Height;

        if (width < Band * 3 || height < Band * 3)
        {
            return;
        }

        var roles = Roles ?? [];
        var layout = LayoutRoles(roles, width, height);
        var byKey = new Dictionary<string, RoleBox>(StringComparer.Ordinal);

        foreach (var box in layout.Boxes)
        {
            _ = byKey.TryAdd(box.RoleKey, box);
        }

        // 虚线的绘制方向**恒为数据流方向**（发送：方块 → 窗外；接收：窗外 → 方块）⇒ 两侧同相、看起来一外一内；
        // 相位由**真实时间**给出（60Hz 重绘 + 时间相位 ⇒ 不再"一卡一卡"）
        var dashOffset = OverlayAnimationClock.DashOffset;
        _sendDash.Offset = dashOffset;
        _receiveDash.Offset = dashOffset;

        var sendColor = ColorOf(SendArrowBrush) ?? FallbackDirection;
        var receiveColor = ColorOf(ReceiveArrowBrush) ?? FallbackDirection;
        var mid = MidColor(sendColor, receiveColor);
        // **绝对色**（兜底）：调用方总会给 `InkBrush`（主题的 `OverlayInk`）；这一支只在资源缺失时出现。
        var ink = InkBrush ?? Brushes.White;
        var inkColor = ColorOf(ink) ?? Colors.White;
        var muted = InkMutedBrush ?? ink;
        var orderColor = ColorOf(FlowLineBrush) ?? Color.FromArgb(150, inkColor.R, inkColor.G, inkColor.B);
        var window = new Rect(0, 0, width, height);

        // ① 内部处理顺序（模块结构，画在最底层；细实线 + 中性色 + 不参与流动）
        DrawRoleOrder(context, byKey, orderColor, muted);

        // ② 角色间数据流线（各窗只画自己窗内那段 ⇒ 两端严格共线）
        DrawRoleLinks(context, byKey, window, width, height, sendColor, receiveColor, mid, ink, muted);

        // ③ 角色方块（画在线之上：方块是"这个模块有哪几块功能"的主体）
        DrawRoleBoxes(context, roles, byKey, width, height, sendColor, receiveColor, ink, muted);

        if (!ShowLabels)
        {
            return;
        }

        // ④ 信息区：左上身份（12px）+（必要时）第二行说明（10px）
        // 叠加层是给用户看的：身份只使用外部语义名；实例 ID（英文模块名）只用于内部/诊断。
        var identity = IdentityText is { Length: > 0 } text
            ? text
            : ModuleTitle ?? string.Empty;

        if (identity.Length > 0)
        {
            context.DrawText(Format(identity, CaptionFontSize, ink), new Point(Gutter, 8));
        }

        if (NoticeText is { Length: > 0 } notice)
        {
            context.DrawText(Format(notice, SmallFontSize, muted), new Point(Gutter, 8 + CaptionFontSize + 5));
        }

        // ⑤ 放不下的角色数（`+K` 徽标）
        if (layout.Hidden > 0)
        {
            var badge = Format($"+{layout.Hidden}", SmallFontSize, muted);
            context.DrawText(badge, new Point(
                (width - badge.Width) / 2,
                Math.Max(2, height - SmallFontSize - 6)));
        }
    }

    /// <summary>画**内部处理顺序**（角色 → 角色；细实线 + 中性色 + 不流动）。</summary>
    private void DrawRoleOrder(
        DrawingContext context,
        Dictionary<string, RoleBox> byKey,
        Color orderColor,
        IBrush muted)
    {
        if (RoleOrder is not { Count: > 0 } order)
        {
            return;
        }

        var pen = new Pen(Alpha(orderColor, 0.9), OrderLineThickness) { LineCap = PenLineCap.Round };

        foreach (var step in order)
        {
            if (!byKey.TryGetValue(step.FromRoleKey, out var from) || !byKey.TryGetValue(step.ToRoleKey, out var to))
            {
                continue;   // 任一端没画出来（被降级隐藏）⇒ 不画（线以已画的方块为准）
            }

            var start = EdgePoint(from.Box, to.Box.Center);
            var end = EdgePoint(to.Box, from.Box.Center);
            context.DrawLine(pen, start, end);
            DrawArrow(context, end, DirectionOf(start, end), Alpha(orderColor, 0.95), ArrowSize * 0.75);
        }

        _ = muted;
    }

    /// <summary>
    /// **渐变的两端**（按**颜色语义**排，不按绘制顺序）：**本端方块端**（用本端方向色）→ **窗口边界端**（用中点色）。
    /// </summary>
    /// <remarks>
    /// 【用户第三十四轮实测反馈："虚线的衔接反了，但是流动方向是对的……只简单地反转了方向，却没有考虑颜色的衔接"】
    /// 线的绘制方向**恒为数据流方向**（<see cref="LinkSegment"/>：发送 = 方块→窗外；接收 = 窗外→方块），
    /// 但**颜色**必须永远"方块端是本端色、边界端是中点色"，否则接收侧的渐变会反过来
    /// ⇒ 公共边两侧一个本端橙、一个本端青，**颜色接不上**（正是截图里的现象）。
    /// </remarks>
    /// <param name="start">绘制起点（数据流方向）。</param>
    /// <param name="end">绘制终点。</param>
    /// <param name="localIsSend">本端是否为发送侧。</param>
    /// <returns>（本端方块端, 窗口边界端）。</returns>
    public static (Point OwnEnd, Point MidEnd) GradientEnds(Point start, Point end, bool localIsSend) =>
        localIsSend ? (start, end) : (end, start);

    /// <summary>
    /// 画**角色间数据流线**：各窗只画"两个角色方块中心之间"那条线段在**本窗内**、**且在本端方块之外**的那一段。
    /// </summary>
    /// <remarks>
    /// 线**不承载任何文字**（线上的字会被读成"这条通路的信息"，
    /// 与"角色自身的信息"混淆）；指标分别画在**角色方块**（角色维度）与**端口标记**（通路维度）上。
    /// </remarks>
    private void DrawRoleLinks(
        DrawingContext context,
        Dictionary<string, RoleBox> byKey,
        Rect window,
        double width,
        double height,
        Color sendColor,
        Color receiveColor,
        Color mid,
        IBrush ink,
        IBrush muted)
    {
        if (RoleLinks is not { Count: > 0 } links)
        {
            return;
        }

        foreach (var link in links)
        {
            if (!byKey.TryGetValue(link.LocalRoleKey, out var box))
            {
                continue;   // 本端角色被裁掉（放不下）⇒ 线也不画（避免线头指向空处）
            }

            // 自回环：两端都在本方块上 ⇒ 走"从输出侧绕到输入侧"的环线，不参与两点共线判定。
            if (link.IsSelfLoop)
            {
                var role = Roles?.FirstOrDefault(item => string.Equals(item.Key, link.LocalRoleKey, StringComparison.Ordinal));
                var infoAbove = role is not null && RoleInfoIsAbove(role, box);
                DrawSelfLoop(context, box, window, sendColor, receiveColor, muted, infoAbove);
                continue;
            }

            var (start, end) = LinkSegment(window, box.Box, link.Target, link.LocalIsSend);

            if (Math.Abs(start.X - end.X) < 0.01 && Math.Abs(start.Y - end.Y) < 0.01)
            {
                continue;   // 整段都在窗外（不该发生，防御）
            }

            // **颜色渐变按"语义"取端，不按绘制顺序**（用户第三十四轮实测："虚线的衔接反了……只简单反转了方向，
            // 却没有考虑颜色的衔接"）：**本端方块端 = 本端方向色**、**窗口边界端 = 中点色** ——
            // 于是两窗在公共边都是"中点色" ⇒ 颜色相接；绘制方向仍按数据流方向（相位/箭头不受影响）。
            var own = link.LocalIsSend ? sendColor : receiveColor;
            var (ownEnd, midEnd) = GradientEnds(start, end, link.LocalIsSend);

            var pen = new Pen(Gradient(ownEnd, midEnd, own, mid, transparentTail: false), LineThickness)
            {
                DashStyle = link.LocalIsSend ? _sendDash : _receiveDash,
                LineCap = PenLineCap.Flat,
            };

            context.DrawLine(pen, start, end);

            // 箭头画在**指向本模块的那一端**：发送 ⇒ 朝窗外；接收 ⇒ 朝本端方块
            DrawArrow(context, end, DirectionOf(start, end), new SolidColorBrush(own), ArrowSize);

            // **未拼接** ⇒ 在**窗口边界那一端**画断口竖杠 + 对端名（表达"线被窗口裁断，另一端在窗外"）
            var boundary = link.LocalIsSend ? end : start;

            if (!link.PeerAdjacent)
            {
                DrawBreakMark(context, boundary, DirectionOf(boundary, link.Target), new SolidColorBrush(own));

                if (ShowLabels && link.PeerText is { Length: > 0 } peer)
                {
                    var peerText = Format(peer, SmallFontSize, muted);
                    var peerX = Math.Clamp(boundary.X - (peerText.Width / 2) + 12, Gutter, Math.Max(Gutter, width - peerText.Width - Gutter));
                    var peerY = Math.Clamp(
                        boundary.Y > (height / 2) ? boundary.Y - (SmallFontSize + 10) : boundary.Y + 10,
                        TopInfoBottom,
                        Math.Max(TopInfoBottom, height - SmallFontSize - 4));

                    context.DrawText(peerText, new Point(peerX, peerY));
                }
            }

            _ = ink;
        }
    }

    /// <summary>
    /// 画**自回环**：从方块输出侧边缘绕到输入侧边缘（方向色/虚线/箭头与普通流线同一套语义）。
    /// </summary>
    /// <remarks>
    /// **不画"未拼接"断口、不标对端名**（对端就是它自己，这两个信息都没有意义）；
    /// 环顶只画一个小 `↻` 图形标记（**不写文字、不承载指标**）。
    /// </remarks>
    private void DrawSelfLoop(
        DrawingContext context,
        RoleBox box,
        Rect window,
        Color sendColor,
        Color receiveColor,
        IBrush muted,
        bool preferBelow)
    {
        var loop = ComputeSelfLoop(window, box.Box, preferBelow);

        if (!loop.IsUsable || loop.Length() < 1)
        {
            return;   // 退化到画不出非零长度 ⇒ 宁可不画，也不留一条零长度线
        }

        var pen = new Pen(
            Gradient(loop.Start, loop.End, sendColor, receiveColor, transparentTail: false),
            LineThickness)
        {
            DashStyle = _sendDash,
            LineCap = PenLineCap.Flat,
        };

        var path = new StreamGeometry();

        using (var sink = path.Open())
        {
            sink.BeginFigure(loop.Start, isFilled: false);

            foreach (var point in loop.Points.Skip(1))
            {
                sink.LineTo(point);
            }

            sink.EndFigure(isClosed: false);
        }

        context.DrawGeometry(null, pen, path);

        // 箭头指向**输入侧边缘**（数据回到自己）——与普通线"箭头指向本模块"同一语义。
        DrawArrow(context, loop.End, PortSide.Left, new SolidColorBrush(receiveColor), ArrowSize);
        DrawSelfLoopMarker(context, loop.Apex, muted);
    }

    /// <summary>环顶的 `↻` 图形标记（折线逼近圆弧 + 小箭头；不写文字、不承载指标）。</summary>
    private static void DrawSelfLoopMarker(DrawingContext context, Point center, IBrush brush)
    {
        const int Steps = 12;
        const double StartAngle = 45;
        const double Sweep = 285;
        var pen = new Pen(brush, 1.2) { LineCap = PenLineCap.Round };
        var arc = new StreamGeometry();

        using (var sink = arc.Open())
        {
            for (var i = 0; i <= Steps; i++)
            {
                var angle = (StartAngle + (Sweep * i / Steps)) * Math.PI / 180;
                var point = new Point(
                    center.X + (SelfLoopMarkerRadius * Math.Cos(angle)),
                    center.Y + (SelfLoopMarkerRadius * Math.Sin(angle)));

                if (i == 0)
                {
                    sink.BeginFigure(point, isFilled: false);
                }
                else
                {
                    sink.LineTo(point);
                }
            }

            sink.EndFigure(isClosed: false);
        }

        context.DrawGeometry(null, pen, arc);

        // 缺口末端的小箭头：取弧上最后一段的切线方向（不依赖引擎的弧长语义）。
        var endAngle = (StartAngle + Sweep) * Math.PI / 180;
        var tip = new Point(
            center.X + (SelfLoopMarkerRadius * Math.Cos(endAngle)),
            center.Y + (SelfLoopMarkerRadius * Math.Sin(endAngle)));
        var beforeTip = new Point(
            center.X + (SelfLoopMarkerRadius * Math.Cos(endAngle - 0.25)),
            center.Y + (SelfLoopMarkerRadius * Math.Sin(endAngle - 0.25)));

        DrawArrow(context, tip, DirectionOf(beforeTip, tip), brush, 3);
    }

    /// <summary>左上信息区（身份 + 说明）的下边界：角色信息文字/对端名不得压上去。</summary>
    private static double TopInfoBottom => 8 + CaptionFontSize + 5 + SmallFontSize + 4;

    /// <summary>
    /// 画**角色自身的信息文字**（速率 / 投递·丢弃 / 类型语义）——**不画文本框**，贴在方块**上方**（放不下则下方）。
    /// </summary>
    /// <remarks>
    /// **不显式绘制文本框**：文字的大致位置位于角色方块周围/上方。这些信息表达的不是「数据通路信息」，
    /// 而是「**角色本身速率信息**」——即每一个角色块显示其自身的信息。
    /// ⇒ 文字随方块走（方块移，文字跟着移），方块与文字之间由**间距**而非边框表达归属。
    /// </remarks>
    /// <summary>角色信息块是否落在方块上方（与 <see cref="DrawRoleInfo"/> 共用同一判据）。</summary>
    private static bool RoleInfoIsAbove(RoleBoxViewModel role, RoleBox box)
    {
        var lines = role.InfoLines.Where(line => line.Text.Length > 0).ToList();
        if (lines.Count == 0)
        {
            return true;
        }

        var blockHeight = lines.Sum(line => line.FontSize + RoleInfoLineGap) - RoleInfoLineGap;
        return box.Box.Top - RoleInfoGap - blockHeight >= TopInfoBottom;
    }

    private void DrawRoleInfo(
        DrawingContext context,
        RoleBoxViewModel role,
        RoleBox box,
        double width,
        double height,
        IBrush ink,
        IBrush muted)
    {
        var lines = role.InfoLines.Where(l => l.Text.Length > 0).ToList();

        if (lines.Count == 0)
        {
            return;
        }

        var formatted = lines
            .Select(l => (Text: Format(l.Text, l.FontSize, l.Primary ? ink : muted), l.FontSize))
            .ToList();

        var blockHeight = formatted.Sum(f => f.FontSize + RoleInfoLineGap) - RoleInfoLineGap;
        var widest = formatted.Max(f => f.Text.Width);

        // 默认放**上方**；上方空间不够（会压到左上信息区或出窗）⇒ 改放**下方**
        var above = box.Box.Top - RoleInfoGap - blockHeight;
        var below = box.Box.Bottom + RoleInfoGap;
        var useAbove = above >= TopInfoBottom;
        var top = useAbove
            ? above
            : Math.Min(below, Math.Max(TopInfoBottom, height - blockHeight - 4));

        // **左对齐**（用户第三十四轮裁定）：三行**起点同一个 X**——从方块左边缘起（越界时钳进安全区）
        var left = Math.Clamp(
            box.Box.Left,
            Gutter,
            Math.Max(Gutter, width - widest - Gutter));

        var y = top;

        foreach (var (text, fontSize) in formatted)
        {
            context.DrawText(text, new Point(left, y));
            y += fontSize + RoleInfoLineGap;
        }
    }

    /// <summary>画**角色方块**：填充 = 类型色；左边缘 = 输入（接收色，有口才画）、右边缘 = 输出（发送色）。</summary>
    private void DrawRoleBoxes(
        DrawingContext context,
        IReadOnlyList<RoleBoxViewModel> roles,
        Dictionary<string, RoleBox> byKey,
        double width,
        double height,
        Color sendColor,
        Color receiveColor,
        IBrush ink,
        IBrush muted)
    {
        foreach (var role in roles)
        {
            if (!byKey.TryGetValue(role.Key, out var box))
            {
                continue;
            }

            var fill = ColorOf(PacketTypePalette.For(role.TypeKey)) ?? FallbackDirection;
            context.DrawRectangle(Alpha(fill, RoleFillAlpha), null, box.Box, RoleBoxRadius, RoleBoxRadius);

            // 上/下边框：细中性线（把方块"框起来"，但不与方向色争）
            var neutral = new Pen(Alpha(Colors.White, 0.28), 1);
            context.DrawLine(neutral, box.Box.TopLeft, box.Box.TopRight);
            context.DrawLine(neutral, box.Box.BottomLeft, box.Box.BottomRight);

            // 左/右接口边：有该侧就画**方向色粗边**（无该侧 ⇒ 中性细边）
            var leftPen = box.HasInput ? new Pen(new SolidColorBrush(receiveColor), RoleSideThickness) : neutral;
            var rightPen = box.HasOutput ? new Pen(new SolidColorBrush(sendColor), RoleSideThickness) : neutral;
            context.DrawLine(leftPen, box.Box.TopLeft, box.Box.BottomLeft);
            context.DrawLine(rightPen, box.Box.TopRight, box.Box.BottomRight);

            // 口数刻痕（>1 口才画：一眼看出"这块有 2 个输入"）
            DrawPortTicks(context, box);

            if (!ShowLabels)
            {
                continue;
            }

            var maxWidth = Math.Max(8, box.Box.Width - 10);
            var titleHeight = LabelFontSize + 2;
            var subHeight = SmallFontSize + 2;
            var top = box.Box.Y + ((box.Box.Height - (titleHeight + subHeight)) / 2);

            var title = Format(Ellipsize(role.Title, LabelFontSize, ink, maxWidth), LabelFontSize, ink);
            context.DrawText(title, new Point(box.Box.Center.X - (title.Width / 2), top));

            var counts = Format(role.RoleCountText, SmallFontSize, muted);
            context.DrawText(counts, new Point(box.Box.Center.X - (counts.Width / 2), top + titleHeight));

            // 角色**自身的信息**（速率 / 口数 / 类型）——纯文字、左对齐，贴在方块上方（不画文本框）
            DrawRoleInfo(context, role, box, width, height, ink, muted);
        }
    }

    /// <summary>接口边上的**口数刻痕**：每口一条短横线（&gt;1 口才画；最多画 6 条 + 一个点）——"几个口"用图形表达。</summary>
    private static void DrawPortTicks(DrawingContext context, RoleBox box)
    {
        Draw(box.InputCount, box.Box.Left, inward: 1);
        Draw(box.OutputCount, box.Box.Right, inward: -1);

        void Draw(int count, double x, int inward)
        {
            if (count <= 1)
            {
                return;   // 1 个口是常态，不画刻痕（不加噪）
            }

            const int MaxTicks = 6;
            const double Inset = 5;
            var shown = Math.Min(count, MaxTicks);
            var usable = box.Box.Height - (Inset * 2);
            var pen = new Pen(Alpha(Colors.White, 0.85), 1.6);

            for (var i = 0; i < shown; i++)
            {
                var t = shown == 1 ? 0.5 : i / (double)(shown - 1);
                var y = box.Box.Top + Inset + (usable * t);
                context.DrawLine(pen, new Point(x, y), new Point(x + (RolePortTickLength * inward * -1), y));
            }

            if (count > MaxTicks)
            {
                // 超出可画数量 ⇒ 在中间补一个点，表示"还有更多"（精确值在悬停里）
                context.DrawEllipse(pen.Brush, null, new Point(x + (RolePortTickLength * inward * -1), box.Box.Center.Y), 1.6, 1.6);
            }
        }
    }

    /// <summary>画**断口竖杠**（未拼接时，在窗口边界处表达"线被裁断"）。</summary>
    private static void DrawBreakMark(DrawingContext context, Point at, PortSide direction, IBrush brush)
    {
        var (dx, dy) = direction switch
        {
            PortSide.Left or PortSide.Right => (1.0, 0.0),
            _ => (0.0, 1.0),
        };

        // 断口 = 与线垂直的一小段
        var normal = (X: -dy * (BreakMarkLength / 2), Y: dx * (BreakMarkLength / 2));
        context.DrawLine(
            new Pen(brush, 2),
            new Point(at.X + normal.X, at.Y + normal.Y),
            new Point(at.X - normal.X, at.Y - normal.Y));
    }

    // ------------------------------------------------------------------ 小工具

    /// <summary>两点之间的方向（用于箭头与断口朝向）。</summary>
    private static PortSide DirectionOf(Point start, Point end)
    {
        var dx = end.X - start.X;
        var dy = end.Y - start.Y;

        return Math.Abs(dx) >= Math.Abs(dy)
            ? dx >= 0 ? PortSide.Right : PortSide.Left
            : dy >= 0 ? PortSide.Bottom : PortSide.Top;
    }

    /// <summary>沿连线方向的渐变：本端方向色 → **中点色**。</summary>
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

    /// <summary>同色但指定透明度的画刷。</summary>
    private static SolidColorBrush Alpha(Color color, double alpha) =>
        new(Color.FromArgb((byte)Math.Clamp(255 * alpha, 0, 255), color.R, color.G, color.B));

    /// <summary>画刷的实色（非纯色画刷返回 null，调用方回退）。</summary>
    private static Color? ColorOf(IBrush? brush) => brush is ISolidColorBrush solid ? solid.Color : null;

    /// <summary>两点之间按比例取点。</summary>
    private static Point Lerp(Point start, Point end, double t) =>
        new(start.X + ((end.X - start.X) * t), start.Y + ((end.Y - start.Y) * t));

    /// <summary>端部流向箭头（沿数据流方向）。</summary>
    private static void DrawArrow(DrawingContext context, Point tip, PortSide flow, IBrush brush, double size)
    {
        var (dx, dy) = flow switch
        {
            PortSide.Right => (X: 1.0, Y: 0.0),
            PortSide.Left => (X: -1.0, Y: 0.0),
            PortSide.Bottom => (X: 0.0, Y: 1.0),
            _ => (X: 0.0, Y: -1.0),
        };

        var normal = (X: -dy, Y: dx);
        var baseX = tip.X - (dx * size);
        var baseY = tip.Y - (dy * size);
        var half = size / 2;

        var geometry = new StreamGeometry();
        using (var sink = geometry.Open())
        {
            sink.BeginFigure(tip, isFilled: true);
            sink.LineTo(new Point(baseX + (normal.X * half), baseY + (normal.Y * half)));
            sink.LineTo(new Point(baseX - (normal.X * half), baseY - (normal.Y * half)));
            sink.EndFigure(isClosed: true);
        }

        context.DrawGeometry(brush, null, geometry);
    }

    /// <summary>排版文本（按 `(文本, 字号, 画刷)` 缓存）。</summary>
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
            // 字体来自 XAML 注入的 `FontData`（改了数据字体 ⇒ 叠层文字跟着变）；未注入则回退系统默认。
            DataFontFamily is { } family ? new Typeface(family) : Typeface.Default,
            size,
            brush);

        _textCache[(text, size, brush)] = formatted;
        return formatted;
    }

    /// <summary>超宽文本**居中省略**。</summary>
    private string Ellipsize(string text, double size, IBrush brush, double maxWidth)
    {
        if (text.Length == 0 || Format(text, size, brush).Width <= maxWidth)
        {
            return text;
        }

        for (var length = text.Length - 1; length > 0; length--)
        {
            var candidate = string.Concat(text.AsSpan(0, length), "…");
            if (Format(candidate, size, brush).Width <= maxWidth)
            {
                return candidate;
            }
        }

        return "…";
    }
}
