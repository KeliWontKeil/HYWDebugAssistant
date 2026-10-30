using Avalonia;
using HYWDebugAssistant.Ui.Controls;
using HYWDebugAssistant.Sdk.Text;
using HYWDebugAssistant.Ui.Geometry;
using HYWDebugAssistant.Ui.ViewModels;
using Shouldly;
using Xunit;

namespace HYWDebugAssistant.Tests;

/// <summary>
/// T1：叠加层的**投影几何**——功能块布局、`m/n` 文本、
/// **线段共线**（"两根错位的线"的回归网）、信息卡内容与信息区。
/// </summary>
/// <remarks>全部是**纯几何 / 纯视图模型**判据：不起窗口（真实观感由 `HYWDebugAssistant.VisualSnapshots` 的 PNG 覆盖）。</remarks>
public class RoleProjectionTests
{
    private const string TypeKeyA = "hyw/analog-sample@v1";

    private const string TypeTextA = "{seq:u64, value:f32}";

    // ------------------------------------------------------------ 判据 5：线段共线（错位回归网）

    [Fact]
    public void 判据5_两端算出的线段必须落在同一条直线上_相邻时端点相接()
    {
        // 两个窗口并排：A 世界原点 (0,0)、B 世界原点 (320,0)。
        // **关键**：裁剪传的是各窗**自己的局部矩形**（画布即本窗，原点恒为 0,0）；
        // 世界原点只用于把"同一个锚点"换算进本窗局部坐标 —— 混用就会得到"两根错位的线"。
        var originA = new Point(0, 0);
        var originB = new Point(320, 0);
        var local = new Rect(0, 0, 320, 220);

        var aOutput = new Point(272, 110);          // A 的角色**输出侧锚点**（A 局部）
        var bInput = new Point(48, 110);            // B 的角色**输入侧锚点**（B 局部）

        // A 窗：本端锚点 → 对端锚点（换算到 A 局部）
        var (nearA, farA) = ConnectionOverlay.ClipSegment(local, aOutput, ToLocal(bInput, originB, originA));

        // B 窗：**同一条**线段，起点换算到 B 局部
        var (nearB, farB) = ConnectionOverlay.ClipSegment(local, ToLocal(aOutput, originA, originB), bInput);

        // ① 共线：两段方向向量的叉积 ≈ 0（否则就是"两根错位的线"）
        var cross = ((farA.X - nearA.X) * (farB.Y - nearB.Y)) - ((farA.Y - nearA.Y) * (farB.X - nearB.X));
        Math.Abs(cross).ShouldBeLessThan(1e-6, "两侧算出的必须是**同一条直线**");

        // ② 相邻窗口 ⇒ 两段在公共边**正好接上**（把 A 段末端换算到 B 局部）
        var farAInB = ToLocal(farA, originA, originB);
        farAInB.X.ShouldBe(nearB.X, 0.001, "公共边处两段相接（视觉上是一根连续的线）");
        farAInB.Y.ShouldBe(nearB.Y, 0.001);

        // ③ 各段的外端仍是自己那侧的锚点（本端不缩进、不漂移）
        nearA.X.ShouldBe(aOutput.X, 0.001, "A 段从本端输出侧锚点出发");
        nearA.Y.ShouldBe(aOutput.Y, 0.001);
        farA.X.ShouldBe(local.Right, 0.001, "A 段止于自己的右边界");
        farB.X.ShouldBe(bInput.X, 0.001, "B 窗那段止于本端输入侧锚点");
        farB.Y.ShouldBe(bInput.Y, 0.001);
    }

    [Fact]
    public void 判据5b_未拼接时两段各自被自己的边界裁断_中间恰好空出两窗间距()
    {
        var originA = new Point(0, 0);
        var originB = new Point(520, 0);            // 与 A 相隔 200px（未拼接）
        var local = new Rect(0, 0, 320, 220);

        var aOutput = new Point(272, 110);
        var bInput = new Point(48, 110);
        var bInputInA = ToLocal(bInput, originB, originA);

        var (_, farA) = ConnectionOverlay.ClipSegment(local, aOutput, bInputInA);

        farA.X.ShouldBe(local.Right, 0.001, "A 窗那段止于自己的右边界");
        bInputInA.X.ShouldBeGreaterThan(local.Right, "对端锚点在窗外 ⇒ 本窗只画到边界为止");

        var gap = originB.X - originA.X - local.Width;
        gap.ShouldBe(200, 0.001, "中间空出的长度 = 两窗间距（'看起来缺了一部分'的正当表现）");
    }

    [Fact]
    public void 判据5c_窗口重叠时两段仍共线()
    {
        var originA = new Point(0, 0);
        var originB = new Point(200, 60);           // 与 A 重叠
        var local = new Rect(0, 0, 320, 220);

        var aOutput = new Point(272, 110);
        var bInputInA = ToLocal(new Point(48, 110), originB, originA);

        var (nearA, farA) = ConnectionOverlay.ClipSegment(local, aOutput, bInputInA);
        var (nearB, farB) = ConnectionOverlay.ClipSegment(
            local,
            ToLocal(aOutput, originA, originB),
            new Point(48, 110));

        var direction = (X: bInputInA.X - aOutput.X, Y: bInputInA.Y - aOutput.Y);
        var cross = (direction.X * (farA.Y - nearA.Y)) - (direction.Y * (farA.X - nearA.X));
        Math.Abs(cross).ShouldBeLessThan(1e-6, "重叠也不得产生'两根错位的线'");

        var crossB = (direction.X * (farB.Y - nearB.Y)) - (direction.Y * (farB.X - nearB.X));
        Math.Abs(crossB).ShouldBeLessThan(1e-6, "对端那半段必须与同一条方向向量平行");
    }

    // ------------------------------------------------------------ 判据 5d：线的端点与流动方向

    [Fact]
    public void 判据5d_线以两个角色方块中心为端点_且绘制方向恒为数据流方向()
    {
        // 线从角色方块中心出发；输入/输出均为中心块向外
        // ⇒ 线以**两个方块中心**为端点；绘制的那一段按**数据流方向**排列
        // （发送：方块 → 窗外；接收：窗外 → 方块），因此两侧虚线看起来一外一内。
        var window = new Rect(0, 0, 320, 220);
        var box = new Rect(96, 93, 128, 34);              // 本端方块（局部坐标）
        var peerCenter = new Point(900, 110);             // 对端方块中心（本窗局部坐标，在窗外右侧）

        var send = ConnectionOverlay.LinkSegment(window, box, peerCenter, localIsSend: true);

        send.Start.X.ShouldBe(box.Right, 0.001, "发送侧：从**方块边界**（朝对端的那一点）出发 —— 几何上以方块中心为端点，绘制不压进方块");
        send.Start.Y.ShouldBe(box.Center.Y, 0.001);
        send.End.X.ShouldBe(window.Right, 0.001, "发送侧：那一段止于**本窗边界**");
        send.End.X.ShouldBeGreaterThan(send.Start.X, "发送侧的绘制方向 = 向外");

        var receive = ConnectionOverlay.LinkSegment(window, box, peerCenter, localIsSend: false);

        receive.Start.X.ShouldBe(window.Right, 0.001, "接收侧：那一段从**本窗边界**开始");
        receive.End.X.ShouldBe(box.Right, 0.001, "接收侧：终点落在**方块边界**上（不压进方块）");
        receive.End.X.ShouldBeLessThan(receive.Start.X, "接收侧的绘制方向 = 向内（与发送侧相反）");

        // 两侧算出的端点都在"方块中心 ↔ 对端中心"这条直线上
        var direction = (X: peerCenter.X - box.Center.X, Y: peerCenter.Y - box.Center.Y);
        var crossSend = (direction.X * (send.End.Y - send.Start.Y)) - (direction.Y * (send.End.X - send.Start.X));
        var crossReceive = (direction.X * (receive.End.Y - receive.Start.Y)) - (direction.Y * (receive.End.X - receive.Start.X));
        Math.Abs(crossSend).ShouldBeLessThan(1e-6, "发送侧那段与'方块中心 → 对端中心'共线");
        Math.Abs(crossReceive).ShouldBeLessThan(1e-6, "接收侧那段同样共线（两端因此严格同一条线）");

        // 虚线相位：两侧**同一个值**（因为绘制方向都已经顺着数据流），取负相位 ⇒ 图案沿绘制方向前进；
        // 相位由**真实时间**算出（用户第三十四轮："虚线流动一卡一卡" ⇒ 改为 60Hz 重绘 + 时间相位）
        OverlayAnimationClock.DashOffsetAt(0).ShouldBe(0, "相位 0 ⇒ 偏移 0（两端同相启动）");
        OverlayAnimationClock.DashOffsetAt(0.02).ShouldBeLessThan(0, "取负 ⇒ 图案从起点朝终点前进");

        // 一个**真实图案周期**（= (段长 + 间隙) × 线宽，DIP）之后回到同一点 ⇒ 环绕处不跳变
        OverlayAnimationClock.Period.ShouldBe(
            (ConnectionOverlay.DashLength + ConnectionOverlay.DashGap) * ConnectionOverlay.LineThickness,
            "图案周期必须含**线宽**因子（`DashStyle` 的段长/间隙与偏移都以笔宽倍数计）");
        OverlayAnimationClock.PhaseAt(OverlayAnimationClock.Period / OverlayAnimationClock.SpeedDipsPerSecond)
            .ShouldBe(0, 1e-9, "整数个周期后相位相同");
        OverlayAnimationClock.DashOffsetAt(OverlayAnimationClock.Period).ShouldBe(
            -OverlayAnimationClock.Period / ConnectionOverlay.LineThickness,
            1e-9,
            "周期末的偏移 = 一个完整图案周期（视觉上与 0 相同 —— 由渲染实测用例证明）");
    }

    // ------------------------------------------------------------ 判据 5e：颜色渐变按语义取端（与绘制方向无关）

    [Fact]
    public void 判据5e_颜色渐变必须方块端用本端色_边界端用中点色()
    {
        // 【用户第三十四轮实测："虚线的衔接反了，但是流动方向是对的……只简单地反转了方向，却没有考虑颜色的衔接"】
        // 绘制方向**恒为数据流方向**（发送：方块→窗外；接收：窗外→方块），但**颜色**永远"方块端 = 本端色、
        // 边界端 = 中点色" ⇒ 两窗在公共边都是中点色 ⇒ 颜色相接。
        var window = new Rect(0, 0, 320, 220);
        var box = new Rect(96, 93, 128, 34);
        var peerCenter = new Point(900, 110);

        var send = ConnectionOverlay.LinkSegment(window, box, peerCenter, localIsSend: true);
        var sendGradient = ConnectionOverlay.GradientEnds(send.Start, send.End, localIsSend: true);

        sendGradient.OwnEnd.ShouldBe(send.Start, "发送侧：绘制起点就是方块端 ⇒ 本端色在方块端");
        sendGradient.MidEnd.ShouldBe(send.End, "发送侧：边界端用中点色");

        // 接收侧：绘制起点是**窗口边界**（数据从窗外进来）⇒ 颜色必须**反过来**取端
        var receive = ConnectionOverlay.LinkSegment(window, box, peerCenter, localIsSend: false);
        var receiveGradient = ConnectionOverlay.GradientEnds(receive.Start, receive.End, localIsSend: false);

        receive.Start.X.ShouldBe(window.Right, 0.001, "接收侧绘制起点 = 窗口边界（数据流方向）");
        receiveGradient.OwnEnd.ShouldBe(receive.End, "接收侧：**方块端**才是本端色（不是绘制起点）");
        receiveGradient.MidEnd.ShouldBe(receive.Start, "接收侧：**窗口边界端**用中点色 ⇒ 与对端那半段颜色相接");
        receiveGradient.OwnEnd.X.ShouldBeLessThan(
            receiveGradient.MidEnd.X,
            "接收侧的本端色在靠方块的一侧（上一版反了：公共边变成了本端色）");
    }

    // ------------------------------------------------------------ 判据 6：功能块布局与 `m/n`

    [Fact]
    public void 判据6_功能块单列居中_方块数等于角色数_m与n用文字表达()
    {
        var roles = new List<RoleBoxViewModel>
        {
            Block("a/hub", "hub", "融合", ["in1", "in2"], ["out"], inbound: 2, outbound: 1),
            Block("a/show", "show", "显示", ["in"], [], inbound: 3, outbound: null),
        };

        const double width = 320;
        const double height = 220;
        var layout = ConnectionOverlay.LayoutRoles(roles, width, height);

        layout.Boxes.Count.ShouldBe(2, "声明几个角色就画几个方块");
        layout.Boxes.Select(b => Math.Round(b.Box.Center.X, 3)).Distinct().Count().ShouldBe(1, "单列 ⇒ 同一个 X");
        layout.Boxes.Average(b => b.Box.Center.Y).ShouldBe(height / 2, 0.001, "整组竖直居中");

        var hub = layout.Boxes[0];
        hub.HasInput.ShouldBeTrue();
        hub.HasOutput.ShouldBeTrue();
        hub.InputCount.ShouldBe(2, "两个输入口（左侧画两条刻痕）");
        hub.OutputCount.ShouldBe(1, "一个输出口 ⇒ 常态不画刻痕（不加噪）");

        var show = layout.Boxes[1];
        show.HasInput.ShouldBeTrue();
        show.HasOutput.ShouldBeFalse("纯汇分块：右侧没有接口边");

        roles[0].InboundRoleCount.ShouldBe(2, "`m` = 输入侧连接的对端角色数");
        roles[0].OutboundRoleCount.ShouldBe(1, "`n` = 输出侧连接的对端角色数");
        roles[1].InboundRoleCount.ShouldBe(3);
        roles[1].OutboundRoleCount.ShouldBeNull("没有输出口 ⇒ null（与「有口未连 = 0」含义不同）");
        roles[0].Title.ShouldNotBeNullOrWhiteSpace("第一行 = 功能名");

        // 宽度**随文本自适应**（不是固定 128），块高统一且更矮
        layout.Boxes[0].Box.Width.ShouldBe(roles[0].BoxWidth, 0.001, "块宽 = 该角色自己的文本估算宽度");
        layout.Boxes[0].Box.Height.ShouldBe(ConnectionOverlay.RoleBoxHeight, 0.001);
        layout.Boxes[1].Box.Width.ShouldBe(roles[1].BoxWidth, 0.001);
        roles[0].BoxWidth.ShouldBeGreaterThanOrEqualTo(RoleTextMetrics.MinBoxWidth);
        roles[0].BoxWidth.ShouldBeLessThanOrEqualTo(RoleTextMetrics.MaxBoxWidth);

        Block("a/long", "long", "一个很长的功能分块名字", ["in"], ["out"], 0, 0)
            .BoxWidth.ShouldBeGreaterThan(
                Block("a/tiny", "tiny", "短", ["in"], ["out"], 0, 0).BoxWidth,
                "文字更长 ⇒ 块更宽（用户要求：文本自适应宽度）");
    }

    [Fact]
    public void 判据6e_块宽按文本估算_CJK比拉丁宽_并有上下限()
    {
        RoleTextMetrics.Estimate("中", RoleTextMetrics.TitleFontSize).ShouldBeGreaterThan(
            RoleTextMetrics.Estimate("a", RoleTextMetrics.TitleFontSize),
            "CJK 一个字宽 ≈ 字号；拉丁 ≈ 0.6 字号");

        RoleTextMetrics.BoxWidth("短", "1/1").ShouldBe(RoleTextMetrics.MinBoxWidth, "极短文本 ⇒ 取下限（否则不像'块'）");
        RoleTextMetrics.BoxWidth(new string('长', 60), "1/1")
            .ShouldBe(RoleTextMetrics.MaxBoxWidth, "超长文本 ⇒ 取上限（再长靠省略号）");
    }

    [Fact]
    public void 判据6b_无功能名时第一行回退为类型短名_超长省略()
    {
        Block("a/x", "x", string.Empty, ["in"], ["out"], 1, 1).Title.ShouldBe(TypeTextA);

        var longType = new RoleBoxViewModel(
            "a/y", "y", string.Empty, TypeKeyA, new string('x', 60), ["in"], ["out"], 0, 0);

        longType.Title.Length.ShouldBeLessThanOrEqualTo(24, "超长类型文本在方块里省略（详情进悬停）");
        longType.Title.ShouldEndWith("…");
    }

    [Fact]
    public void 判据6c_放不下时降级并如实报出未画出的数量()
    {
        var roles = new List<RoleBoxViewModel>();
        for (var i = 0; i < 8; i++)
        {
            roles.Add(Block($"a/r{i}", $"r{i}", $"块{i}", ["in"], ["out"], 0, 0));
        }

        var layout = ConnectionOverlay.LayoutRoles(roles, width: 320, height: 150);

        (layout.Boxes.Count + layout.Hidden).ShouldBe(8, "画出来的 + 报出来的 = 声明的总数（不静默丢弃）");
        layout.Hidden.ShouldBeGreaterThan(0);
    }

    [Fact]
    public void 判据6d_零角色_不画方块()
    {
        var layout = ConnectionOverlay.LayoutRoles([], 320, 220);

        layout.Boxes.ShouldBeEmpty("没有声明角色 ⇒ 不画任何方块（叠加层不发明角色）");
        layout.Hidden.ShouldBe(0);
    }

    // ------------------------------------------------------------ 判据 7：角色自身的信息 + 信息区

    [Fact]
    public void 判据7_角色自身信息画在方块上_线不再承载文字()
    {
        var link = new RoleLinkViewModel("a/hub", true, "b", "b/show", 2);
        var receive = new RoleLinkViewModel("a/hub", false, "b", "b/show", 2);

        link.LocalIsSend.ShouldBeTrue("本端是发送侧（虚线朝外流动）");
        receive.LocalIsSend.ShouldBeFalse("对端是接收侧（虚线朝内流动）");
        link.PeerRoleKey.ShouldBe("b/show");
        link.PortPairCount.ShouldBe(2, "同一角色对之间的口对数是派生量（线上不标网络名与条数）");

        // 这些信息表达的不是「数据通路信息」，而是「角色本身速率信息」
        // ⇒ 指标画在**角色方块**上（每个块显示其自身的信息），线只表达"有这条流动"。
        link.PeerText.ShouldBeNull("刷新前不标对端名");
        link.PeerAdjacent.ShouldBeFalse("刷新前一律按未拼接处理（宁可多画一个断口，也不假装已拼接）");
    }

    [Fact]
    public void 判据7a_角色信息_速率大字打头_口数一行_类型标签短且可省()
    {
        var role = Block("a/hub", "hub", "融合", ["in1", "in2"], ["out"], 2, 1);

        role.RateText.ShouldBe("---", "未回填 ⇒ 速率是**占位符**而不是 0（'0 B/s' 与'没测到'不可混）");
        role.MetaText.ShouldBeEmpty("类型未声明语义 ⇒ 不占位");
        role.InfoLines.Count.ShouldBe(2, "①速率 ②口数（类型标签未声明 ⇒ 不占位）");
        role.InfoLines[0].Primary.ShouldBeTrue("速率是**主行**（大字 + 主文字色）");
        role.InfoLines[0].FontSize.ShouldBe(RoleTextMetrics.RateFontSize);
        role.InfoLines[0].FontSize.ShouldBeGreaterThan(
            role.InfoLines[1].FontSize,
            "用户第三十四轮：速率**字体更大**一些");
        role.InfoLines[1].Text.ShouldBe(
            TextCatalog.Get(typeof(RoleTextMetrics).Assembly, "role.info.ports", 2, 1),
            "②行 = 该角色的**口数**（与方块上的连接数 `m/n` 不同）");
        role.InfoLines[1].Primary.ShouldBeFalse("次要行 10px、次要色");

        role.RateText = "12.0 kB/s";
        role.MetaText = RoleTextMetrics.TypeTagOf("样本含序号与值 / value 为无单位实数");

        var semantic = "样本含序号与值";
        role.InfoLines.Count.ShouldBe(3, "①速率 ②口数 ③类型标签");
        role.InfoLines[0].Text.ShouldBe(role.RateText);
        role.InfoLines[1].Text.ShouldBe(TextCatalog.Get(typeof(RoleTextMetrics).Assembly, "role.info.ports", 2, 1));
        role.InfoLines[2].Text.ShouldBe(RoleTextMetrics.TypeTagOf(semantic));
    }

    [Fact]
    public void 判据7a2_类型标签只取第一个语义且截断_禁止外显过长描述()
    {
        // 【用户第三十四轮："禁止 UI 外显过长描述（当前第三行太长了）"】
        RoleTextMetrics.TypeTagOf(null).ShouldBeEmpty("未声明 ⇒ 不占位");
        RoleTextMetrics.TypeTagOf("  ").ShouldBeEmpty("空白 ⇒ 不占位");
        RoleTextMetrics.TypeTagOf("normal").ShouldBe(TextCatalog.Get(typeof(RoleTextMetrics).Assembly, "ui.role.typeTag", "normal"));
        RoleTextMetrics.TypeTagOf("normal / 样本含序号与值 / value 为无单位实数")
            .ShouldBe(TextCatalog.Get(typeof(RoleTextMetrics).Assembly, "ui.role.typeTag", "normal"), "只取**第一个**标签（后面那些长描述不外显）");

        var longTag = RoleTextMetrics.TypeTagOf("abcdefghijklmnop");
        longTag.Length.ShouldBeLessThanOrEqualTo(RoleTextMetrics.MaxTypeTagLength + 4, "超长按上限截断");
        longTag.Length.ShouldBeLessThanOrEqualTo(RoleTextMetrics.MaxTypeTagLength + 4, "整行保持短小");
    }

    [Fact]
    public void 判据7b_信息区只放身份_零口或缺声明时给一句事实()
    {
        var identity = "a · 融合器";
        var normal = RoleOverlayInfoBuilder.Build(identity, roleCount: 1, portCount: 2);
        normal.Identity.ShouldBe(identity);
        normal.Notice.ShouldBeEmpty("有角色 ⇒ 不需要解释文字（「几个角色/几个口」图上可见）");

        RoleOverlayInfoBuilder.Build("a · x", roleCount: 0, portCount: 0).Notice.ShouldBe(
            TextCatalog.Get(typeof(RoleTextMetrics).Assembly, "role.noPorts"));
        RoleOverlayInfoBuilder.Build("a · x", roleCount: 0, portCount: 3).Notice.ShouldBe(
            TextCatalog.Get(typeof(RoleTextMetrics).Assembly, "role.undeclared", 3));
    }

    [Fact]
    public void 判据7c_类型色按类型身份取值_同键同色()
    {
        PacketTypePalette.ClearOverrides();

        try
        {
            PacketTypePalette.For(TypeKeyA).ShouldBeSameAs(
                PacketTypePalette.For(TypeKeyA),
                "同一类型身份 ⇒ 恒取同一画刷（跨模块、跨进程一致）");

            PacketTypePalette.For(TypeKeyA).ShouldNotBeSameAs(
                PacketTypePalette.For("hyw/other@v1"),
                "不同标识 ⇒ 不同颜色（哪怕形状相同）");
        }
        finally
        {
            PacketTypePalette.ClearOverrides();
        }
    }

    /// <summary>
    /// 把点从 <paramref name="fromOrigin"/> 那个窗口的局部坐标换算到 <paramref name="toOrigin"/> 那个窗口的局部坐标。
    /// </summary>
    /// <remarks>窗口各自只知道自己那一块画布（原点 0,0）——这正是"两端各画半段"能共线的前提。</remarks>
    private static Point ToLocal(Point point, Point fromOrigin, Point toOrigin) =>
        new(point.X + fromOrigin.X - toOrigin.X, point.Y + fromOrigin.Y - toOrigin.Y);

    private static RoleBoxViewModel Block(
        string key,
        string roleId,
        string displayName,
        IReadOnlyList<string> inputs,
        IReadOnlyList<string> outputs,
        int? inbound,
        int? outbound) =>
        new(key, roleId, displayName, TypeKeyA, TypeTextA, inputs, outputs, inbound, outbound);
}
