using System.Buffers.Binary;
using HYWDebugAssistant.Core.Api;
using HYWDebugAssistant.Core.Composition;
using HYWDebugAssistant.Core.Runtime;
using HYWDebugAssistant.Core.Services;
using HYWDebugAssistant.Sdk;
using HYWDebugAssistant.Sdk.Context;
using HYWDebugAssistant.Sdk.Contracts;
using HYWDebugAssistant.Sdk.Lifecycle;
using HYWDebugAssistant.Sdk.Ports;
using HYWDebugAssistant.Sdk.Views;
using HYWDebugAssistant.Ui.Geometry;
using HYWDebugAssistant.Ui.ViewModels;
using HYWDebugAssistant.Ui.Views;
using Shouldly;
using Xunit;

namespace HYWDebugAssistant.Tests;

/// <summary>
/// M1-15：**拼图式模块窗口集**（屏幕坐标、拖动/缩放/吸附、锁定与隐藏、口上标记与建网）的端到端验证。
/// </summary>
/// <remarks>
/// 用**真实运行时**（L4）驱动 UI 层：模块 → 数据面 → 订阅缓冲 → 视图模型 → 几何。
/// 本测试**不创建窗口**（窗口只在 `ModuleWindowManager` 里，属平台相关），
/// 因此几何与交互逻辑可无头验证；窗口本身的拖动/缩放由手工验收覆盖。
/// </remarks>
public class ModuleWindowsTests
{
    // ------------------------------------------------------------ 模块窗口与声明渲染

    [Fact]
    public void 模块窗口_未摆放时只分配位置_尺寸留给XAML默认()
    {
        using var harness = new UiHarness();
        harness.Services.AddModule("s1", "test/ui-source/1.0.0").Succeeded.ShouldBeTrue();

        harness.Windows.Refresh();

        var module = harness.Windows.Modules.ShouldHaveSingleItem();
        module.InstanceId.ShouldBe("s1");

        // 【用户第九轮反馈 · UI 开发方式】窗口尺寸**不由内容/代码计算**：
        // 未摆放的新模块只有"位置"（错位摆放避免叠在一起），尺寸 = XAML 默认值（常量与 XAML 同步）
        module.HasLayout.ShouldBeFalse("未摆放的模块不应有布局记录（尺寸由窗口的 XAML 默认值决定）");
        module.Width.ShouldBe(ModuleViewViewModel.DefaultWidth, "尺寸默认值 = ModuleWindow.axaml 的 Width");
        module.Height.ShouldBe(ModuleViewViewModel.DefaultHeight, "尺寸默认值 = ModuleWindow.axaml 的 Height");
        harness.Services.GetLayout().ShouldBeEmpty("打开窗口之前不写盘（由窗口把**实际尺寸**读回后再落盘）");

        // 窗口打开后由管理器把实际尺寸读回落盘（此处直接调用等价入口）
        harness.Windows.CommitInitialLayout(module);

        var layout = harness.Services.GetLayout().ShouldHaveSingleItem();
        layout.InstanceId.ShouldBe("s1");
        layout.X.ShouldBe(module.X);
        layout.Y.ShouldBe(module.Y);
        layout.Width.ShouldBe(module.Width);

        // 按声明渲染：数值 + 曲线 + 未渲染元素 + **框架补的配置表单**（声明里没有 B1 元素时补一个）
        module.Elements.Count.ShouldBe(4);
        module.Elements[0].ShouldBeOfType<ValueElementViewModel>();
        module.Elements[1].ShouldBeOfType<CurveElementViewModel>();
        module.Elements[2].ShouldBeOfType<UnsupportedElementViewModel>();
        module.Elements[3].ShouldBeOfType<ConfigurationEditorElementViewModel>();
    }

    [Fact]
    public void 新模块摆放_应在外壳右侧排开且互不堆叠()
    {
        // 【用户第十七轮反馈】原先新模块都落在左上角（48,48 起、每 40px 斜叠）⇒ 压在主窗口上、彼此重叠。
        using var harness = new UiHarness();
        harness.Windows.ShellRect = new CanvasRect(0, 0, 400, 900);   // 外壳（模拟）
        harness.Windows.WorkArea = new CanvasRect(0, 0, 2600, 1400);  // 工作区（模拟，够宽）

        for (var i = 1; i <= 3; i++)
        {
            harness.Services.AddModule($"s{i}", "test/ui-source/1.0.0").Succeeded.ShouldBeTrue();
        }

        harness.Windows.Refresh();

        var modules = harness.Windows.Modules.ToList();
        modules.Count.ShouldBe(3);

        modules.ShouldAllBe(m => m.X >= 400, "新模块必须排在外壳**右侧**（外壳右边缘 = 400），不得压在主窗口上");

        // 两两不相交（真矩形相交判定，而不是"错位一点"）
        for (var i = 0; i < modules.Count; i++)
        {
            for (var j = i + 1; j < modules.Count; j++)
            {
                Intersects(modules[i], modules[j]).ShouldBeFalse(
                    $"新模块不得互相堆叠：{modules[i].InstanceId}({modules[i].Rect}) 与 {modules[j].InstanceId}({modules[j].Rect})");
            }
        }

        // 同一列内是**竖向**排开（X 相同、Y 递增）
        modules.Select(m => m.X).Distinct().Count().ShouldBe(1, "一列放得下时应竖着排开");
        modules.Select(m => m.Y).ShouldBeInOrder(SortDirection.Ascending);
    }

    private static bool Intersects(ModuleViewViewModel a, ModuleViewViewModel b) =>
        a.X < b.Rect.Right - 0.001 && b.X < a.Rect.Right - 0.001
        && a.Y < b.Rect.Bottom - 0.001 && b.Y < a.Rect.Bottom - 0.001;

    [Fact]
    public void 模块窗口_未声明视图的模块也应开窗并可改配置()
    {
        using var harness = new UiHarness();
        harness.Services.AddModule("r1", "test/ui-sink/1.0.0").Succeeded.ShouldBeTrue();

        harness.Windows.Refresh();

        // **每个模块实例都有窗口**（用户裁决 M1-15）：无声明视图 ≠ 无窗口
        var module = harness.Windows.Modules.ShouldHaveSingleItem();
        module.Elements.ShouldHaveSingleItem().ShouldBeOfType<ConfigurationEditorElementViewModel>(
            "未声明视图时窗口内至少给出配置表单（无配置则说明原因），不引入「无界面模块」这一例外");
        module.Ports.ShouldNotBeEmpty("口仍存在 → 口上标记仍可用于建网");
        module.IsEnabled.ShouldBeTrue();

        // 运行 / 停用在**它自己的窗口**上操作（不需要任何实例清单）
        harness.Windows.ToggleEnabled("r1");
        harness.Services.GetInstances().ShouldHaveSingleItem().State.ShouldBe("Disabled");

        harness.Windows.Refresh();
        harness.Windows.Modules.ShouldHaveSingleItem().IsEnabled.ShouldBeFalse();

        harness.Windows.CloseModule("r1");
        harness.Services.GetInstances().ShouldBeEmpty();
    }

    [Fact]
    public void 模块窗口_订阅数据应刷新到数值与曲线元素()
    {
        using var harness = new UiHarness();
        harness.Services.AddModule("s1", "test/ui-source/1.0.0").Succeeded.ShouldBeTrue();
        harness.Windows.Refresh();

        var module = harness.Windows.Modules.ShouldHaveSingleItem();
        var value = module.Elements.OfType<ValueElementViewModel>().Single();
        var curve = module.Elements.OfType<CurveElementViewModel>().Single();

        var ready = WaitUntil(
            () =>
            {
                harness.Windows.Refresh();
                return curve.Points.Count > 2;
            },
            TimeSpan.FromSeconds(10));

        // 说明：这是**时间相关**断言（等订阅缓冲攒够样本）。机器忙时（满量程跑测试 + 应用在跑）
        // 曾出现一次偶发失败，故把预算放宽到 10 s 并带上现场信息，便于再出现时定位。
        ready.ShouldBeTrue(
            $"曲线元素应按刷新节拍取到环形缓冲里的样本（当前 {curve.Points.Count} 个点；"
            + $"模块状态 {module.StateText}；数值显示 {value.Display}）");

        value.Value.ShouldNotBeNull();
        value.Display.ShouldNotBe("—", "数值元素应显示最新值");
        value.Unit.ShouldBe("V");

        curve.Max.ShouldBeGreaterThan(curve.Min, "自动量程（数据优先，不做固定刻度）");
    }

    [Fact]
    public void 模块窗口_文本日志元素应按定长样本解码成行()
    {
        using var harness = new UiHarness();
        harness.Services.AddModule("l1", "test/ui-logger/1.0.0").Succeeded.ShouldBeTrue();
        harness.Windows.Refresh();

        var module = harness.Windows.Modules.ShouldHaveSingleItem();
        var log = module.Elements.OfType<TextLogElementViewModel>().Single();

        var ready = WaitUntil(
            () =>
            {
                harness.Windows.Refresh();
                return log.Lines.Count > 0;
            },
            TimeSpan.FromSeconds(5));

        ready.ShouldBeTrue("文本日志应按刷新节拍取到定长文本样本");
        log.Lines[0].ShouldStartWith("line-");
        log.Lines[0].ShouldNotEndWith(" ", "尾部补齐的空格应被去掉");
    }

    [Fact]
    public void 配置表单_应按Schema生成字段并可写回()
    {
        using var harness = new UiHarness();
        harness.Services.AddModule("c1", "test/ui-config/1.0.0").Succeeded.ShouldBeTrue();
        harness.Windows.Refresh();

        var module = harness.Windows.Modules.ShouldHaveSingleItem();
        var editor = module.Elements.OfType<ConfigurationEditorElementViewModel>().Single();

        editor.HasFields.ShouldBeTrue("有配置的模块应在自己的窗口里给出表单（C5-4）");
        editor.Fields.Select(f => f.Name).ShouldBe(["Interval", "Label", "Verbose"], ignoreOrder: true);

        var interval = editor.Fields.Single(f => f.Name == "Interval");
        interval.Kind.ShouldBe(ConfigurationFieldKind.WholeNumber);
        interval.Title.ShouldBe("采样周期", "字段标题来自模块标注（界面不出现代码化标识）");
        interval.RangeHint.ShouldNotBeNull();

        var verbose = editor.Fields.Single(f => f.Name == "Verbose");
        verbose.Kind.ShouldBe(ConfigurationFieldKind.Boolean);
        verbose.IsChecked.ShouldBeFalse();

        // 改一个字段 → 应用 → 落在实例配置里（经变更入口）
        interval.Value = "250";
        interval.IsDirty.ShouldBeTrue();
        editor.ApplyCommand.Execute(null);

        editor.StatusIsOk.ShouldBeTrue(editor.StatusMessage);
        interval.IsDirty.ShouldBeFalse();

        var schema = harness.SchemaOf("c1");
        schema.Fields.Single(f => f.Name == "Interval").Value.ShouldBe("250");
    }

    [Fact]
    public void 配置表单_非法值应被拒绝并给出原因()
    {
        using var harness = new UiHarness();
        harness.Services.AddModule("c1", "test/ui-config/1.0.0").Succeeded.ShouldBeTrue();
        harness.Windows.Refresh();

        var editor = harness.Windows.Modules.ShouldHaveSingleItem()
            .Elements.OfType<ConfigurationEditorElementViewModel>().Single();

        var interval = editor.Fields.Single(f => f.Name == "Interval");
        interval.Value = "abc";
        editor.ApplyCommand.Execute(null);

        editor.StatusIsOk.ShouldBeFalse();
        editor.StatusMessage.ShouldNotBeNull().ShouldContain("整数");
        interval.IsDirty.ShouldBeTrue("被拒绝的字段保持「未提交」状态，便于改正后重试");

        harness.SchemaOf("c1").Fields.Single(f => f.Name == "Interval").Value.ShouldBe("100", "拒绝 = 零副作用");
    }

    // ------------------------------------------------------------ 口上标记与建网

    [Fact]
    public void 口上标记_应挂在正确边缘并携带网络标签()
    {
        using var harness = new UiHarness();
        harness.Place("s1", "test/ui-source/1.0.0", 0, 0);
        harness.Place("r1", "test/ui-sink/1.0.0", 400, 0);

        harness.Services.UpdatePortNetwork("s1", UiSourceModule.OutPort, "bus-1").Succeeded.ShouldBeTrue();
        harness.Windows.Refresh();

        var source = harness.Windows.Modules.Single(m => m.InstanceId == "s1");
        var sourceMarker = source.Ports.ShouldHaveSingleItem();
        sourceMarker.IsSend.ShouldBeTrue("发送口在右边缘");
        sourceMarker.Side.ShouldBe(PortSide.Right);
        sourceMarker.Label.ShouldBe("bus-1");
        sourceMarker.Display.ShouldBe($"{UiSourceModule.OutPort}·bus-1");

        var sink = harness.Windows.Modules.Single(m => m.InstanceId == "r1");
        var sinkMarker = sink.Ports.ShouldHaveSingleItem();
        sinkMarker.IsSend.ShouldBeFalse("接收口在左边缘");
        sinkMarker.Side.ShouldBe(PortSide.Left);
        sinkMarker.Label.ShouldBeNull();
    }

    [Fact]
    public void 建网_在模块窗口里填网络名应自动连上同名口()
    {
        using var harness = new UiHarness();
        harness.Place("s1", "test/ui-source/1.0.0", 0, 0);
        harness.Place("r1", "test/ui-sink/1.0.0", 400, 0);
        harness.Windows.Refresh();

        var source = harness.Windows.Modules.Single(m => m.InstanceId == "s1");
        var sink = harness.Windows.Modules.Single(m => m.InstanceId == "r1");

        var sourceMarker = source.Ports.ShouldHaveSingleItem();
        sourceMarker.IsEditing = true;
        sourceMarker.EditText = "bus-9";
        var first = harness.Windows.CommitPortLabel(source, sourceMarker);
        first.ShouldContain("bus-9");
        harness.Services.GetNetworks().ShouldBeEmpty("只有发送端命名时还不成网（提示待发送端/接收端）");

        var sinkMarker = sink.Ports.ShouldHaveSingleItem();
        sinkMarker.IsEditing = true;
        sinkMarker.EditText = "bus-9";
        _ = harness.Windows.CommitPortLabel(sink, sinkMarker);

        var network = harness.Services.GetNetworks().ShouldHaveSingleItem();
        network.Name.ShouldBe("bus-9");
        network.Source.ShouldBe($"s1.{UiSourceModule.OutPort}");
        network.Targets.ShouldBe([$"r1.{UiSinkModule.InPort}"]);

        // 清空标签 = 退出该网络（网络无成员即消失）
        sinkMarker.IsEditing = true;
        sinkMarker.EditText = string.Empty;
        _ = harness.Windows.CommitPortLabel(sink, sinkMarker);

        harness.Services.GetNetworks().ShouldBeEmpty("清除标签即退出命名网络");
    }

    // ------------------------------------------------------------ 拖动与几何吸附（屏幕坐标）

    [Fact]
    public void 锁定_不随拼接组整体移动()
    {
        using var harness = new UiHarness();
        harness.Place("a", "test/ui-source/1.0.0", 0, 0);
        harness.Place("b", "test/ui-sink/1.0.0", ModuleViewViewModel.DefaultWidth, 0);   // 与 a 贴合 → 同组
        harness.Windows.Refresh();

        var b = harness.Windows.Modules.Single(m => m.InstanceId == "b");
        harness.Windows.ToggleLock("b");
        b.IsLocked.ShouldBeTrue();

        harness.Windows.BeginDrag("a", 10, 10, independent: false).ShouldBeTrue();
        harness.Windows.DragTo(110, 60);
        harness.Windows.EndDrag();

        harness.Windows.Modules.Single(m => m.InstanceId == "a").X.ShouldBe(100, "被拖的模块照常移动");
        b.X.ShouldBe(ModuleViewViewModel.DefaultWidth, "**锁定的模块不随拼接组移动**（用户第十一轮反馈：这很反直觉）");
        b.Y.ShouldBe(0);
    }

    [Fact]
    public void 外壳移动_贴靠其上的模块应跟随()
    {
        using var harness = new UiHarness();
        harness.Place("a", "test/ui-source/1.0.0", 600, 100);

        // 外壳：右边缘 = 600（模块 a 的左边缘贴在上面）
        harness.Windows.ShellRect = new CanvasRect(200, 100, 400, 160);
        harness.Windows.Refresh();

        // 外壳右移 120、下移 40：ShellRect 已由管理器刷新为**移动后**的矩形，
        // 而 FollowShellMove 内部用"移动前"的几何做锚定判定（用户第十二轮修"拉脱"）
        harness.Windows.ShellRect = new CanvasRect(320, 140, 400, 160);
        harness.Windows.TranslateShellGroup(120, 40, new CanvasRect(200, 100, 400, 160));

        var a = harness.Windows.Modules.ShouldHaveSingleItem();
        a.X.ShouldBe(720, "拼接在主窗口上的模块应**跟随主窗口移动**（用户第十一轮反馈）");
        a.Y.ShouldBe(140);

        // 落盘现在由调用方防抖后合并提交（用户第十二轮：不逐帧提交）
        harness.Windows.CommitSceneTranslation().ShouldBeTrue();
        harness.Services.GetLayout().Single(l => l.InstanceId == "a").X.ShouldBe(720, "跟随后的位置要落盘");
    }

    [Fact]
    public void 外壳移动_未贴靠的模块不应被带走()
    {
        using var harness = new UiHarness();
        harness.Place("a", "test/ui-source/1.0.0", 1200, 600);   // 离外壳很远

        harness.Windows.ShellRect = new CanvasRect(200, 100, 400, 160);
        harness.Windows.Refresh();

        harness.Windows.ShellRect = new CanvasRect(320, 140, 400, 160);
        harness.Windows.TranslateShellGroup(120, 40, new CanvasRect(200, 100, 400, 160));

        var a = harness.Windows.Modules.ShouldHaveSingleItem();
        a.X.ShouldBe(1200);
        a.Y.ShouldBe(600);
    }

    [Fact]
    public void 还原_尺寸应回到模块首选尺寸()
    {
        using var harness = new UiHarness();
        harness.Place("a", "test/ui-source/1.0.0", 0, 0);
        harness.Windows.Refresh();

        var module = harness.Windows.Modules.ShouldHaveSingleItem();

        // 用户把它拉大好一会儿
        module.Width = 900;
        module.Height = 700;
        module.HasLayout = true;

        harness.Windows.ApplyReset(module, ModuleResetScope.SizeOnly);

        module.Width.ShouldBe(module.PreferredWidth, "还原 = 回到**模块声明的首选尺寸**（不是框架按元素猜的）");
        module.Height.ShouldBe(module.PreferredHeight);
        harness.Services.GetLayout().Single(l => l.InstanceId == "a").Width.ShouldBe(module.PreferredWidth, "还原后要落盘");
    }

    [Fact]
    public void 吸附_模块应能贴到外壳的右边缘()
    {
        using var harness = new UiHarness();
        harness.Place("s1", "test/ui-source/1.0.0", 0, 0);

        // 外壳：右边缘在 x = 600（拼图起始点只提供这一条吸附参照 —— ADR-0038 D5 / 用户裁决 D-2）
        harness.Windows.ShellRect = new CanvasRect(200, 0, 400, 160);
        harness.Windows.WorkArea = new CanvasRect(0, 0, 1920, 1080);
        harness.Windows.Refresh();

        // 抓住模块内 (10,10) 拖到 (616, 80) → 模块左边 = 606，距外壳右边缘 6 px（容差 8）→ 应吸附到 600
        harness.Windows.BeginDrag("s1", 10, 10, independent: true).ShouldBeTrue();
        harness.Windows.DragTo(616, 80);
        harness.Windows.EndDrag();

        harness.Windows.Modules.ShouldHaveSingleItem().X.ShouldBe(
            600,
            "模块的左边必须能吸附到**外壳的右边缘**（此前实测无法吸附到主窗口 —— 缺陷单 14 §9.1）");
    }

    [Fact]
    public void 吸附_屏幕工作区不再参与吸附()
    {
        using var harness = new UiHarness();
        harness.Place("s1", "test/ui-source/1.0.0", 0, 0);

        harness.Windows.WorkArea = new CanvasRect(0, 0, 1920, 1080);
        harness.Windows.Refresh();

        // 拖到"距工作区左边缘 4 px"（容差内）→ 不得吸附（屏幕工作区已退出候选，ADR-0038 D5）
        harness.Windows.BeginDrag("s1", 10, 10, independent: true).ShouldBeTrue();
        harness.Windows.DragTo(14, 80);
        harness.Windows.EndDrag();

        harness.Windows.Modules.ShouldHaveSingleItem().X.ShouldBe(
            4,
            "屏幕工作区只用于默认摆放与不出屏钳制，**不再产生吸附**");
    }
    [Fact]
    public void 拖动_应把组整体平移并一次性提交()
    {
        using var harness = new UiHarness();
        harness.Place("a", "test/ui-source/1.0.0", 0, 0);
        harness.Place("b", "test/ui-sink/1.0.0", ModuleViewViewModel.DefaultWidth, 0);   // 与 a 右边缘贴合 → 同组
        harness.Windows.Refresh();

        harness.Windows.BeginDrag("a", 10, 10, independent: false).ShouldBeTrue();
        harness.Windows.DragTo(110, 60);

        harness.Windows.Modules.Single(m => m.InstanceId == "a").X.ShouldBe(100);
        harness.Windows.Modules.Single(m => m.InstanceId == "b").X.ShouldBe(ModuleViewViewModel.DefaultWidth + 100, "整组一起移动");
        harness.Windows.Modules.Single(m => m.InstanceId == "b").Y.ShouldBe(50);

        harness.Windows.EndDrag();
        harness.Windows.IsInteracting.ShouldBeFalse();

        var layout = harness.Services.GetLayout().ToDictionary(l => l.InstanceId);
        layout["a"].X.ShouldBe(100);
        layout["b"].X.ShouldBe(ModuleViewViewModel.DefaultWidth + 100);
    }

    [Fact]
    public void 拖动_独立模式只移动被拖的那一个()
    {
        using var harness = new UiHarness();
        harness.Place("a", "test/ui-source/1.0.0", 0, 0);
        harness.Place("b", "test/ui-sink/1.0.0", ModuleViewViewModel.DefaultWidth, 0);
        harness.Windows.Refresh();

        harness.Windows.BeginDrag("a", 10, 10, independent: true).ShouldBeTrue();
        harness.Windows.DragTo(110, 60);

        harness.Windows.Modules.Single(m => m.InstanceId == "a").X.ShouldBe(100);
        harness.Windows.Modules.Single(m => m.InstanceId == "b").X.ShouldBe(ModuleViewViewModel.DefaultWidth, "Ctrl 独立拖动（C5-1 约束 3）");
    }

    [Fact]
    public void 拖动_拖动中按Ctrl应立刻拆分并把其余成员放回原位()
    {
        // 【用户第十七轮反馈】原交互要求"**拖动前**按住 Ctrl"；拖动开始后按 Ctrl 无效。
        // 现语义：拖动期间按 Ctrl ⇒ **立刻断开**，等价于"从一开始就按住 Ctrl"：
        // 其余成员（含外壳）**回到本次拖动的起点**，被拖模块继续跟着指针，且松手后不会重新合组。
        using var harness = new UiHarness();
        harness.Place("a", "test/ui-source/1.0.0", 0, 0);
        harness.Place("b", "test/ui-sink/1.0.0", ModuleViewViewModel.DefaultWidth, 0);
        harness.Windows.Refresh();

        // 不带 Ctrl 开始拖动（整组一起走）
        harness.Windows.BeginDrag("a", 10, 10, independent: false).ShouldBeTrue();
        harness.Windows.DragTo(60, 10);

        var a = harness.Windows.Modules.Single(m => m.InstanceId == "a");
        var b = harness.Windows.Modules.Single(m => m.InstanceId == "b");
        b.X.ShouldBe(ModuleViewViewModel.DefaultWidth + 50, "未按 Ctrl 时 b 跟着走（整组）");

        // 拖动**期间**按 Ctrl ⇒ 立刻拆分
        harness.Windows.DetachDrag();

        b.X.ShouldBe(ModuleViewViewModel.DefaultWidth, "拆分后 b 回到**拖动起点**（等价于一开始就按住 Ctrl）");
        b.Y.ShouldBe(0);
        a.X.ShouldBe(50, "被拖的 a 不动（仍由指针主导）");

        // 拆分后继续拖：只有 a 动，b 留在原地（且松开 Ctrl 也不重新合组）
        harness.Windows.DragTo(160, 10);
        a.X.ShouldBe(150);
        b.X.ShouldBe(ModuleViewViewModel.DefaultWidth, "拆分是**单向**的：松开 Ctrl 不会重新合组");

        // 幂等：重复调用不改变任何东西
        harness.Windows.DetachDrag();
        b.X.ShouldBe(ModuleViewViewModel.DefaultWidth);
    }

    [Fact]
    public void 拖动_应吸附到邻近窗口的边缘并给出辅助线()
    {
        using var harness = new UiHarness();
        harness.Place("a", "test/ui-source/1.0.0", 0, 0);
        harness.Place("b", "test/ui-sink/1.0.0", 600, 400);
        harness.Windows.Refresh();

        // 把 b 拖到"左边缘离 a 右边缘 3 px"的位置 → 应被吸附成贴合
        harness.Windows.BeginDrag("b", 610, 410, independent: false).ShouldBeTrue();
        harness.Windows.DragTo(ModuleViewViewModel.DefaultWidth + 3 + 10, 410);

        var b = harness.Windows.Modules.Single(m => m.InstanceId == "b");
        b.X.ShouldBe(ModuleViewViewModel.DefaultWidth, "3 px 落在容差 8 px 内 → 贴合到 a 的右边缘");
        harness.Windows.Guides.ShouldNotBeEmpty("吸附命中时显示对齐辅助线");
        harness.Windows.Guides.ShouldContain(g => g.IsVertical);
    }

    [Fact]
    public void 拖动_停用吸附时应保持自由位置()
    {
        using var harness = new UiHarness();
        harness.Place("a", "test/ui-source/1.0.0", 0, 0);
        harness.Place("b", "test/ui-sink/1.0.0", 600, 400);
        harness.Windows.Refresh();

        harness.Windows.BeginDrag("b", 610, 410, independent: false).ShouldBeTrue();
        harness.Windows.DragTo(ModuleViewViewModel.DefaultWidth + 3 + 10, 410, snapEnabled: false);   // 按住 Alt

        harness.Windows.Modules.Single(m => m.InstanceId == "b").X.ShouldBe(ModuleViewViewModel.DefaultWidth + 3, "停用吸附 → 精确摆放");
        harness.Windows.Guides.ShouldBeEmpty();
    }

    [Fact]
    public void 拖动_应吸附到外壳窗口与屏幕工作区()
    {
        using var harness = new UiHarness();
        harness.Place("a", "test/ui-source/1.0.0", 400, 300);
        harness.Windows.WorkArea = new CanvasRect(0, 0, 1920, 1040);
        harness.Windows.ShellRect = new CanvasRect(600, 200, 900, 200);
        harness.Windows.Refresh();

        // 贴到**外壳的右边缘**（600+900 = 1500；给的 1504 → 容差内）
        // 【M1-16 / ADR-0038 D5】外壳只提供右边缘一条参照：左/上/下边缘不再吸附（用户裁决 D-2）
        harness.Windows.BeginDrag("a", 410, 310, independent: false).ShouldBeTrue();
        harness.Windows.DragTo(1504 + 10, 310);
        harness.Windows.Modules.Single().X.ShouldBe(1500, "外壳是拼图的「起始点」，其右边缘可被贴靠");
    }

    [Fact]
    public void 拖动_取消应回到提交前的位置()
    {
        using var harness = new UiHarness();
        harness.Place("a", "test/ui-source/1.0.0", 100, 100);
        harness.Windows.Refresh();

        harness.Windows.BeginDrag("a", 110, 110, independent: false).ShouldBeTrue();
        harness.Windows.DragTo(300, 300);
        harness.Windows.CancelDrag();

        var module = harness.Windows.Modules.ShouldHaveSingleItem();
        module.X.ShouldBe(100);
        module.Y.ShouldBe(100);
        harness.Services.GetLayout().ShouldHaveSingleItem().X.ShouldBe(100, "取消不提交");
    }

    [Fact]
    public void 拖动_几何一律落在整数物理像素上()
    {
        using var harness = new UiHarness();
        harness.Services.AddModule("a", "test/ui-source/1.0.0").Succeeded.ShouldBeTrue();
        harness.Windows.Refresh();

        // 布局里存着小数（历史数据 / 125% 缩放下的旧值）→ 读入时必须量化到整数物理像素
        harness.Services.UpdateModuleLayout(new ModuleLayoutView("a", 100.5, 200.25, 412.5, 275.5)).Succeeded.ShouldBeTrue();
        harness.Windows.Refresh();

        var module = harness.Windows.Modules.ShouldHaveSingleItem();
        module.X.ShouldBe(101, "0.5 向上取整（AwayFromZero），避免两条边各自取整后差 1px");
        module.Y.ShouldBe(200);
        module.Width.ShouldBe(413, "125% 缩放下的 412.5px 必须量化（否则窗口落位与模型不一致）");
        module.Height.ShouldBe(276);

        // 指针本身带小数时，写回模型的位置同样必须是整数
        harness.Windows.BeginDrag("a", 110.4, 210.7, independent: true).ShouldBeTrue();
        harness.Windows.DragTo(150.6, 260.2);

        (module.X % 1).ShouldBe(0.0, "拖动中的自由位置也必须是整数物理像素");
        (module.Y % 1).ShouldBe(0.0);
        harness.Windows.EndDrag();
        (module.X % 1).ShouldBe(0.0, "落盘前的几何也必须是整数");
        (module.Y % 1).ShouldBe(0.0);
    }

    [Fact]
    public void 拖动_整组平移时组内相对偏移保持不变()
    {
        using var harness = new UiHarness();
        harness.Place("a", "test/ui-source/1.0.0", 0, 0);
        harness.Place("b", "test/ui-sink/1.0.0", ModuleViewViewModel.DefaultWidth, 0);   // 严丝合缝贴在 a 的右边
        harness.Windows.Refresh();

        var a = harness.Windows.Modules.Single(m => m.InstanceId == "a");
        var b = harness.Windows.Modules.Single(m => m.InstanceId == "b");
        var offsetX = b.X - a.X;
        var offsetY = b.Y - a.Y;

        harness.Windows.BeginDrag("a", 10.3, 10.7, independent: false).ShouldBeTrue();
        harness.Windows.DragTo(210.8, 160.4);   // 目标带小数
        harness.Windows.EndDrag();

        (b.X - a.X).ShouldBe(offsetX, "组内相对偏移不得因取整而漂移（缝/错位的直接来源）");
        (b.Y - a.Y).ShouldBe(offsetY);
        foreach (var module in new[] { a, b })
        {
            (module.X % 1).ShouldBe(0.0);
            (module.Y % 1).ShouldBe(0.0);
        }
    }

    [Fact]
    public void 整体平移_只平移与外壳拼成一体的那一组()
    {
        using var harness = new UiHarness();
        harness.Place("a", "test/ui-source/1.0.0", 400, 0);          // 左边缘 400 = 外壳右边缘 → 拼上了
        harness.Place("b", "test/ui-sink/1.0.0", 1200, 700);         // 离外壳很远 → 没拼上
        harness.Windows.ShellRect = new CanvasRect(0, 0, 400, 300);
        harness.Windows.Refresh();

        harness.Windows.ShellRect = new CanvasRect(40, 0, 400, 300);
        harness.Windows.TranslateShellGroup(40, 0, new CanvasRect(0, 0, 400, 300));

        harness.Windows.Modules.Single(m => m.InstanceId == "a").X.ShouldBe(
            440,
            "单侧吸附完成后，外壳与模块是一个整体：移动外壳 = 整组一起动（用户第十二轮第四轮反馈）");
        harness.Windows.Modules.Single(m => m.InstanceId == "b").X.ShouldBe(
            1200,
            "**没吸附上的模块留在原处** —— 只有形成整体后才整体运动");
    }

    [Fact]
    public void 整体平移_经由中间模块与外壳相连的模块也跟着走()
    {
        using var harness = new UiHarness();
        harness.Place("a", "test/ui-source/1.0.0", 400, 0);          // 贴外壳
        harness.Place("b", "test/ui-sink/1.0.0", 720, 0);            // 贴 a（a 宽 320 → a.Right = 720 = b.Left）
        harness.Place("c", "test/ui-logger/1.0.0", 1040, 0);         // 贴 b
        harness.Windows.ShellRect = new CanvasRect(0, 0, 400, 300);
        harness.Windows.Refresh();

        harness.Windows.ShellRect = new CanvasRect(40, 0, 400, 300);
        harness.Windows.TranslateShellGroup(40, 0, new CanvasRect(0, 0, 400, 300));

        foreach (var id in new[] { "a", "b", "c" })
        {
            harness.Windows.Modules.Single(m => m.InstanceId == id).X.ShouldBe(
                (id switch { "a" => 400, "b" => 720, _ => 1040 }) + 40,
                $"{id} 与外壳同属一个接触连通分量 → 一起走");
        }
    }

    [Fact]
    public void 拖动_贴在外壳上的模块会带着外壳一起走()
    {
        using var harness = new UiHarness();
        harness.Place("a", "test/ui-source/1.0.0", 400, 0);          // 贴外壳右边缘
        harness.Windows.ShellRect = new CanvasRect(0, 0, 400, 300);
        harness.Windows.Refresh();

        var shell = harness.Windows.ShellRect!.Value;
        (double Dx, double Dy) requested = default;
        harness.Windows.ShellTranslationRequested += (_, delta) => requested = delta;

        harness.Windows.BeginDrag("a", 410, 10, independent: false).ShouldBeTrue();
        harness.Windows.DragTo(470, 10);   // +60
        harness.Windows.EndDrag();

        requested.Dx.ShouldBe(60, "拖动组包含外壳 → 外壳窗口也要一起走（视觉整体）");
        harness.Windows.Modules.Single().X.ShouldBe(460);
        harness.Windows.ShellRect!.Value.X.ShouldBe(shell.X + 60, "模型里的外壳矩形同步乐观更新");
    }

    [Fact]
    public void 拖动_独立拖动贴壳模块时不带动外壳()
    {
        using var harness = new UiHarness();
        harness.Place("a", "test/ui-source/1.0.0", 400, 0);
        harness.Windows.ShellRect = new CanvasRect(0, 0, 400, 300);
        harness.Windows.Refresh();

        var requested = false;
        harness.Windows.ShellTranslationRequested += (_, _) => requested = true;

        harness.Windows.BeginDrag("a", 410, 10, independent: true).ShouldBeTrue();   // Ctrl 独立拖动
        harness.Windows.DragTo(470, 10);
        harness.Windows.EndDrag();

        requested.ShouldBeFalse("独立拖动（Ctrl）只动自己，不带动组、也不带动外壳");
        harness.Windows.Modules.Single().X.ShouldBe(460);
    }

    [Fact]
    public void 拖动_拖动中按Ctrl拆分时外壳也要回到原位()
    {
        // 【用户第十七轮】拆分 = "等价于一开始就按住 Ctrl" ⇒ 已经跟着走的外壳必须**还回去**
        // （模型 ShellRect + 通过 ShellTranslationRequested 让真实窗口回去）。
        using var harness = new UiHarness();
        harness.Place("a", "test/ui-source/1.0.0", 400, 0);   // 贴住外壳右边缘（外壳宽 400）
        harness.Windows.ShellRect = new CanvasRect(0, 0, 400, 300);
        harness.Windows.Refresh();

        var deltas = new List<(double Dx, double Dy)>();
        harness.Windows.ShellTranslationRequested += (_, d) => deltas.Add(d);

        harness.Windows.BeginDrag("a", 410, 10, independent: false).ShouldBeTrue();
        harness.Windows.DragTo(470, 10);

        deltas.ShouldNotBeEmpty("拖动贴壳模块（未按 Ctrl）时外壳应一起走");
        harness.Windows.ShellRect!.Value.X.ShouldBe(60, "外壳已被带走 60");

        deltas.Clear();
        harness.Windows.DetachDrag();

        deltas.ShouldHaveSingleItem().Dx.ShouldBe(-60, "拆分时外壳按累计位移**还回原位**");
        harness.Windows.ShellRect!.Value.X.ShouldBe(0, "外壳回到拖动起点");
        harness.Windows.Modules.Single().X.ShouldBe(460, "被拖的模块不动（仍由指针主导）");
    }

    [Fact]
    public void 整体平移_锁定的模块留在原地()
    {
        using var harness = new UiHarness();
        harness.Place("a", "test/ui-source/1.0.0", 400, 0);
        harness.Place("b", "test/ui-sink/1.0.0", 900, 0);
        harness.Windows.ShellRect = new CanvasRect(0, 0, 400, 300);
        harness.Windows.Refresh();

        harness.Windows.ToggleLock("b");

        harness.Windows.ShellRect = new CanvasRect(40, 0, 400, 300);
        harness.Windows.TranslateShellGroup(40, 0, new CanvasRect(0, 0, 400, 300));

        harness.Windows.Modules.Single(m => m.InstanceId == "a").X.ShouldBe(440, "未锁定的跟着走");
        harness.Windows.Modules.Single(m => m.InstanceId == "b").X.ShouldBe(
            900,
            "锁定 = 钉在桌面上（用户第十一轮口径：不参与拖动组，也不随整体移动）");
    }

    [Fact]
    public void 整体平移_未落盘期间刷新不得把模块拉回原位()
    {
        using var harness = new UiHarness();
        harness.Place("a", "test/ui-source/1.0.0", 400, 0);
        harness.Windows.ShellRect = new CanvasRect(0, 0, 400, 300);
        harness.Windows.Refresh();

        harness.Windows.ShellRect = new CanvasRect(40, 0, 400, 300);
        harness.Windows.TranslateShellGroup(40, 0, new CanvasRect(0, 0, 400, 300));
        harness.Windows.Modules.Single().X.ShouldBe(440);

        // 刷新是 60Hz 的：若它用"已落盘的旧布局"覆盖内存位置，模块会被一帧帧拉回去 ——
        // 表现就是"移动主窗口时模块跟不动"（用户第十二轮第四轮实测现象）
        for (var i = 0; i < 5; i++)
        {
            harness.Windows.Refresh();
            harness.Windows.Modules.Single().X.ShouldBe(440, $"未落盘期间刷新第 {i + 1} 次仍不得回写");
        }

        harness.Windows.CommitSceneTranslation().ShouldBeTrue();
        harness.Windows.Refresh();
        harness.Windows.Modules.Single().X.ShouldBe(440, "落盘之后刷新当然也不回退");
    }

    [Fact]
    public void 整体平移_移动中不落盘_停止后才合并提交()
    {
        using var harness = new UiHarness();
        harness.Place("a", "test/ui-source/1.0.0", 400, 0);
        harness.Windows.ShellRect = new CanvasRect(0, 0, 400, 300);
        harness.Windows.Refresh();

        // 移动中（连续两次位移）：内存已动，但**不落盘**（逐帧提交会触发建网重算并让拖动卡顿）
        harness.Windows.ShellRect = new CanvasRect(10, 0, 400, 300);
        harness.Windows.TranslateShellGroup(10, 0, new CanvasRect(0, 0, 400, 300));
        harness.Windows.ShellRect = new CanvasRect(40, 0, 400, 300);
        harness.Windows.TranslateShellGroup(30, 0, new CanvasRect(10, 0, 400, 300));

        harness.Windows.Modules.Single().X.ShouldBe(440, "内存里已经跟着走");
        harness.Services.GetLayout().Single(l => l.InstanceId == "a").X.ShouldBe(
            400,
            "整体平移过程中的**未落盘**状态：由调用方防抖后合并提交（见 ModuleWindowManager.OnShellMoved）");

        harness.Windows.CommitSceneTranslation().ShouldBeTrue();
        harness.Services.GetLayout().Single(l => l.InstanceId == "a").X.ShouldBe(440, "停止移动后合并落盘一次");

        harness.Windows.CommitSceneTranslation().ShouldBeFalse("没有待提交的平移变化 → 不产生空变更");
    }

    [Fact]
    public void 吸附距离_设置越小越不吸()
    {
        using var harness = new UiHarness();
        harness.Place("a", "test/ui-source/1.0.0", 0, 0);
        harness.Place("b", "test/ui-sink/1.0.0", 600, 400);   // 与 a 竖直不重叠 → 只可能是"边缘对齐"类

        // 把 b 拖到"左边缘离 a 右边缘 6px"的位置：对齐类容差 = 吸附距离
        void DragToGap6()
        {
            _ = harness.Windows.BeginDrag("b", 610, 410, independent: false);
            harness.Windows.DragTo(ModuleViewViewModel.DefaultWidth + 6 + 10, 410);
            harness.Windows.EndDrag();
        }

        // 默认 4：6px 的偏移**不吸**
        harness.Windows.Refresh();
        harness.Windows.SnapDistance.ShouldBe(4, "默认吸附距离 4（ADR-0042 D6）");
        DragToGap6();
        harness.Windows.Modules.Single(m => m.InstanceId == "b").X.ShouldBe(
            ModuleViewViewModel.DefaultWidth + 6,
            "6px > 吸附距离 4 → 不吸，保持自由位置");

        // 调到 8：同一个 6px 偏移现在**吸上**
        var settings = harness.Services.GetSettings().Clone();
        settings.SnapDistance = 8;
        harness.Services.UpdateSettings(settings).Succeeded.ShouldBeTrue();
        harness.Windows.Refresh();
        harness.Windows.SnapDistance.ShouldBe(8, "设置在线即时生效");

        _ = harness.Services.UpdateModuleLayout(new ModuleLayoutView(
            "b",
            600,
            400,
            ModuleViewViewModel.DefaultWidth,
            ModuleViewViewModel.DefaultHeight));
        harness.Windows.Refresh();

        DragToGap6();

        harness.Windows.Modules.Single(m => m.InstanceId == "b").X.ShouldBe(
            ModuleViewViewModel.DefaultWidth,
            "吸附距离调到 8 后，6px 的偏移被吸到对齐位置（用户可自行调手感）");
    }

    // ------------------------------------------------------------ 缩放（边中点/角点热区）

    [Fact]
    public void 缩放_按右下角手柄应改变几何并提交()
    {
        using var harness = new UiHarness();
        harness.Place("a", "test/ui-source/1.0.0", 100, 100);
        harness.Windows.Refresh();

        harness.Windows.BeginResize("a", ResizeHandle.BottomRight).ShouldBeTrue();
        harness.Windows.ResizeTo(100 + 260 + 80, 100 + 140 + 60);

        var module = harness.Windows.Modules.ShouldHaveSingleItem();
        module.Width.ShouldBe(340);
        module.Height.ShouldBe(200);
        module.X.ShouldBe(100, "右下角缩放不动左上角");

        harness.Windows.EndResize();

        var layout = harness.Services.GetLayout().ShouldHaveSingleItem();
        layout.Width.ShouldBe(340);
        layout.Height.ShouldBe(200);
    }

    [Fact]
    public void 缩放_左上角手柄应同时移动位置与尺寸()
    {
        using var harness = new UiHarness();
        harness.Place("a", "test/ui-source/1.0.0", 300, 300);
        harness.Windows.Refresh();

        harness.Windows.BeginResize("a", ResizeHandle.TopLeft).ShouldBeTrue();
        harness.Windows.ResizeTo(260, 260);

        var module = harness.Windows.Modules.ShouldHaveSingleItem();
        module.X.ShouldBe(260);
        module.Y.ShouldBe(260);
        module.Width.ShouldBe(ModuleViewViewModel.DefaultWidth + 40, "右边缘不动 → 宽度增加 40");
        module.Height.ShouldBe(ModuleViewViewModel.DefaultHeight + 40, "下边缘不动 → 高度增加 40");
    }

    [Fact]
    public void 缩放_不得小于最小尺寸()
    {
        using var harness = new UiHarness();
        harness.Place("a", "test/ui-source/1.0.0", 100, 100);
        harness.Windows.Refresh();

        harness.Windows.BeginResize("a", ResizeHandle.BottomRight).ShouldBeTrue();
        harness.Windows.ResizeTo(0, 0);

        var module = harness.Windows.Modules.ShouldHaveSingleItem();
        module.Width.ShouldBe(ModuleViewViewModel.MinWidth);
        module.Height.ShouldBe(ModuleViewViewModel.MinHeight);
    }

    // ------------------------------------------------------------ 锁定与隐藏

    [Fact]
    public void 锁定_应禁止拖动缩放并清除选中()
    {
        using var harness = new UiHarness();
        harness.Place("a", "test/ui-source/1.0.0", 100, 100);
        harness.Windows.Refresh();

        harness.Windows.Select("a");
        harness.Windows.SelectedInstanceId.ShouldBe("a");

        harness.Windows.ToggleLock("a");
        var module = harness.Windows.Modules.ShouldHaveSingleItem();
        module.IsLocked.ShouldBeTrue();
        harness.Windows.SelectedInstanceId.ShouldBeNull("锁定即不可选中（O-3）");

        harness.Windows.BeginDrag("a", 110, 110, independent: false).ShouldBeFalse("锁定后不可拖动");
        harness.Windows.BeginResize("a", ResizeHandle.BottomRight).ShouldBeFalse("锁定后不可缩放");

        harness.Windows.ToggleLock("a");
        harness.Windows.BeginDrag("a", 110, 110, independent: false).ShouldBeTrue("解锁后可拖动");
        harness.Windows.EndDrag();
    }

    [Fact]
    public void 锁定_全部锁定与解锁()
    {
        using var harness = new UiHarness();
        harness.Place("a", "test/ui-source/1.0.0", 100, 100);
        harness.Place("b", "test/ui-sink/1.0.0", 700, 500);
        harness.Windows.Refresh();

        harness.Windows.SetAllLocked(true);
        harness.Windows.AllLocked.ShouldBeTrue();
        harness.Windows.Modules.ShouldAllBe(m => m.IsLocked);

        harness.Windows.SetAllLocked(false);
        harness.Windows.Modules.ShouldAllBe(m => !m.IsLocked);
    }

    [Fact]
    public void 隐藏_窗口隐藏后模块仍在运行并可重新显示()
    {
        using var harness = new UiHarness();
        harness.Place("s1", "test/ui-source/1.0.0", 100, 100);
        harness.Windows.Refresh();

        harness.Windows.HideWindow("s1");
        harness.Windows.Modules.ShouldHaveSingleItem().IsViewVisible.ShouldBeFalse();
        harness.Services.GetLayout().ShouldHaveSingleItem().IsViewVisible.ShouldBeFalse("隐藏是布局的一部分");
        harness.Services.GetInstances().ShouldHaveSingleItem().State.ShouldBe("Active", "隐藏只收起表现层，模块继续运行");

        harness.Windows.ShowWindow("s1");
        harness.Windows.Modules.ShouldHaveSingleItem().IsViewVisible.ShouldBeTrue();
    }

    [Fact]
    public void 几何变化_应通知窗口管理器()
    {
        using var harness = new UiHarness();
        harness.Place("a", "test/ui-source/1.0.0", 100, 100);
        harness.Windows.Refresh();

        var events = new List<ModuleWindowGeometryChange>();
        harness.Windows.GeometryChanged += (_, e) => events.Add(e);

        harness.Windows.BeginDrag("a", 110, 110, independent: false).ShouldBeTrue();
        harness.Windows.DragTo(210, 160);
        harness.Windows.EndDrag();

        events.ShouldNotBeEmpty("几何变化必须通知窗口管理器（否则窗口不会跟着动）");
        events[^1].X.ShouldBe(200);
        events[^1].Y.ShouldBe(150);
    }

    // ------------------------------------------------------------ 几何（纯函数）

    [Fact]
    public void 贴合谓词_要求边缘相邻且有重叠()
    {
        var a = new CanvasRect(0, 0, 240, 120);

        SnapEngine.AreJoined(a, new CanvasRect(240, 0, 240, 120)).ShouldBeTrue("左右相邻");
        SnapEngine.AreJoined(a, new CanvasRect(0, 120, 240, 120)).ShouldBeTrue("上下相邻");
        SnapEngine.AreJoined(a, new CanvasRect(244, 0, 240, 120)).ShouldBeTrue("4 px 在容差内");
        SnapEngine.AreJoined(a, new CanvasRect(260, 0, 240, 120)).ShouldBeFalse("20 px 超出容差");
        SnapEngine.AreJoined(a, new CanvasRect(240, 400, 240, 120)).ShouldBeFalse("边缘对齐但**没有重叠** → 不算拼接");
    }

    [Fact]
    public void 拼接组_应具有传递性()
    {
        var modules = new List<(string, CanvasRect)>
        {
            ("a", new CanvasRect(0, 0, 240, 120)),
            ("b", new CanvasRect(240, 0, 240, 120)),
            ("c", new CanvasRect(480, 0, 240, 120)),
            ("d", new CanvasRect(1200, 0, 240, 120)),
        };

        var groups = SnapEngine.FindGroups(modules);

        groups.Count.ShouldBe(1);
        groups[0].ShouldBe(["a", "b", "c"]);

        SnapEngine.GroupOf("b", modules).Count.ShouldBe(3);
        SnapEngine.GroupOf("d", modules).ShouldBe(["d"], "孤立模块自成一组（不产生组记录）");
    }

    [Fact]
    public void 端口锚点_按方向分边并按数量等分_用户调整后以几何为准()
    {
        var ports = new List<PortView>
        {
            new("rx", "Receive", 4, false, false),
            new("tx", "Send", 4, false, false),
        };

        var anchors = PortAnchorCalculator.Compute(new CanvasRect(0, 0, 240, 120), ports);

        anchors.Count.ShouldBe(2);
        var rx = anchors.Single(a => a.PortName == "rx");
        rx.Side.ShouldBe(PortSide.Left);
        rx.X.ShouldBe(0);
        rx.Y.ShouldBe(60, "单个端口位于边缘中点（等分）");

        var tx = anchors.Single(a => a.PortName == "tx");
        tx.Side.ShouldBe(PortSide.Right);
        tx.X.ShouldBe(240);

        var adjusted = PortAnchorCalculator.Compute(
            new CanvasRect(0, 0, 240, 120),
            ports,
            name => name == "tx" ? new PortGeometryView("tx", 0.25, true) : new PortGeometryView(name));

        adjusted.Single(a => a.PortName == "tx").Y.ShouldBe(30);
        adjusted.Single(a => a.PortName == "rx").Y.ShouldBe(60);

        PortAnchorCalculator.EvenOffset(0, 1).ShouldBe(0.5);
        PortAnchorCalculator.EvenOffset(0, 2).ShouldBe(1d / 3);
        PortAnchorCalculator.EvenOffset(1, 2).ShouldBe(2d / 3);
    }

    // ------------------------------------------------------------ 测试辅助

    private static bool WaitUntil(Func<bool> condition, TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < deadline)
        {
            if (condition())
            {
                return true;
            }

            Thread.Sleep(10);
        }

        return condition();
    }

    /// <summary>UI 测试现场：真实运行时 + 真实服务面 + 模块窗口集控制器。</summary>
    private sealed class UiHarness : IDisposable
    {
        public UiHarness()
        {
            Runtime = new ModuleRuntime();
            Services = new RuntimeServices(Runtime);
            Register("ui-source", typeof(UiSourceModule));
            Register("ui-sink", typeof(UiSinkModule));
            Register("ui-logger", typeof(UiLoggerModule));
            Register("ui-config", typeof(UiConfigModule));
            Windows = new ModuleWindowsViewModel(Services, Services);
        }

        public ModuleRuntime Runtime { get; }

        public RuntimeServices Services { get; }

        public ModuleWindowsViewModel Windows { get; }

        /// <summary>加入模块并**显式摆放**（几何测试需要确定位置）。</summary>
        public void Place(string instanceId, string moduleIdLiteral, double x, double y)
        {
            Services.AddModule(instanceId, moduleIdLiteral).Succeeded.ShouldBeTrue();

            // 先让控制器记录默认布局，再覆盖为指定位置（模拟用户拖动/上一次会话的布局）
            Windows.Refresh();
            Services.UpdateModuleLayout(new ModuleLayoutView(
                instanceId,
                x,
                y,
                ModuleViewViewModel.DefaultWidth,
                ModuleViewViewModel.DefaultHeight)).Succeeded.ShouldBeTrue();
            Windows.Refresh();
        }

        /// <summary>读实例配置的 Schema（断言配置写回结果用）。</summary>
        public ConfigurationSchemaView SchemaOf(string instanceId)
        {
            Services.TryGetConfigurationSchema(instanceId, out var schema).ShouldBeTrue();
            schema.ShouldNotBeNull();
            return schema;
        }

        public void Dispose() => Runtime.Dispose();

        private void Register(string name, Type entryType)
        {
            var definition = new HYWDebugAssistant.Hosting.Registry.ModuleDefinition(
                ModuleId.Create("test", name, "1.0.0"),
                entryType.Name,
                string.Empty,
                [],
                ModuleCapability.None,
                [],
                null,
                true,
                entryType,
                string.Empty,
                string.Empty);

            Services.Registry.TryRegister(definition, out var failure).ShouldBeTrue(failure);
        }
    }

    /// <summary>测试模块：发布数值（最新值槽）与序列（环形缓冲），并有自发活动。</summary>
    [ModuleDefinition("test", "ui-source", "1.0.0")]
    public sealed class UiSourceModule : IModule, IActivitySource
    {
        public const string OutPort = "out";

        public static readonly ContractShape Shape = ContractShape.OfPrimitive(ContractPrimitive.F32);

        /// <inheritdoc />
        public ActivityKind Kind => ActivityKind.EventDriven;

        /// <inheritdoc />
        public TimeSpan Interval => TimeSpan.FromMilliseconds(1);

        public void OnRegistered(IModuleContext context)
        {
            context.Declaration.DeclarePort(new PortDeclaration(OutPort, PortDirection.Send, Shape));
            context.Declaration.DeclareView(new ViewDeclaration(
                [
                    new ViewElement(ViewElementKind.Numeric, "value", "值"),
                    new ViewElement(ViewElementKind.Curve, "wave", "波形"),
                    new ViewElement(ViewElementKind.Table, "rows", "表格"),
                ],
                [
                    new ViewDataSource("value", "V", "F2", ViewDataSourceKind.Value, ContractPrimitive.F32),
                    new ViewDataSource("wave", null, null, ViewDataSourceKind.Sequence, ContractPrimitive.F32),
                ]));
        }

        /// <inheritdoc />
        public void RunActivity(IModuleContext context)
        {
            var sequence = 0;
            var payload = new byte[4];

            while (!context.StopSignal.IsStopRequested)
            {
                BinaryPrimitives.WriteSingleLittleEndian(payload, sequence++);
                context.Ports.Publish("value", payload);
                context.Ports.Publish("wave", payload);
                _ = context.StopSignal.Wait(TimeSpan.FromMilliseconds(1));
            }
        }
    }

    /// <summary>测试模块：接收口 + 只声明数值元素（无曲线）。</summary>
    [ModuleDefinition("test", "ui-sink", "1.0.0")]
    public sealed class UiSinkModule : IModule
    {
        public const string InPort = "in";

        public static readonly ContractShape Shape = ContractShape.OfPrimitive(ContractPrimitive.F32);

        public void OnRegistered(IModuleContext context) =>
            context.Declaration.DeclarePort(new PortDeclaration(InPort, PortDirection.Receive, Shape));
    }

    /// <summary>测试模块：发布**定长**文本样本（A5 日志元素）。</summary>
    [ModuleDefinition("test", "ui-logger", "1.0.0")]
    public sealed class UiLoggerModule : IModule, IActivitySource
    {
        /// <inheritdoc />
        public ActivityKind Kind => ActivityKind.EventDriven;

        /// <inheritdoc />
        public TimeSpan Interval => TimeSpan.FromMilliseconds(1);

        public void OnRegistered(IModuleContext context)
        {
            context.Declaration.DeclareView(new ViewDeclaration(
                [new ViewElement(ViewElementKind.TextLog, "log", "日志")],
                [new ViewDataSource("log", null, null, ViewDataSourceKind.Text)]));
        }

        /// <inheritdoc />
        public void RunActivity(IModuleContext context)
        {
            var sequence = 0;

            while (!context.StopSignal.IsStopRequested)
            {
                // 定长 16 字节（框架要求同一数据源的样本长度一致）
                var line = $"line-{sequence++:D6}".PadRight(16);
                context.Ports.Publish("log", System.Text.Encoding.UTF8.GetBytes(line));
                _ = context.StopSignal.Wait(TimeSpan.FromMilliseconds(5));
            }
        }
    }

    /// <summary>测试模块：带配置与配置表单元素（B1）。</summary>
    [ModuleDefinition("test", "ui-config", "1.0.0")]
    public sealed class UiConfigModule : IModule
    {
        public void OnRegistered(IModuleContext context)
        {
            // 读一次配置 → 框架登记该配置对象（Schema 表单的来源）
            _ = context.Configuration.Read<UiConfig>();

            context.Declaration.DeclareView(new ViewDeclaration(
                [new ViewElement(ViewElementKind.ConfigurationEditor, title: "参数")],
                []));
        }
    }

    /// <summary>测试配置类型（含标注 → 标题/范围来自模块）。</summary>
    public sealed class UiConfig
    {
        [HYWDebugAssistant.Sdk.Configuration.ConfigurationField(
            "采样周期",
            Group = "采集",
            Minimum = 1,
            Maximum = 60000,
            Step = 1,
            Unit = "ms")]
        public int Interval { get; set; } = 100;

        [HYWDebugAssistant.Sdk.Configuration.ConfigurationField("标签")]
        public string Label { get; set; } = "sample";

        [HYWDebugAssistant.Sdk.Configuration.ConfigurationField("详细输出")]
        public bool Verbose { get; set; }
    }
}
