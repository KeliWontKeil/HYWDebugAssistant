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
    public void 模块窗口_应显示模块并立即写入默认布局()
    {
        using var harness = new UiHarness();
        harness.Services.AddModule("s1", "test/ui-source/1.0.0").Succeeded.ShouldBeTrue();

        harness.Windows.Refresh();

        var module = harness.Windows.Modules.ShouldHaveSingleItem();
        module.InstanceId.ShouldBe("s1");
        module.Width.ShouldBeGreaterThanOrEqualTo(ModuleViewViewModel.DefaultWidth, "首次摆放**按内容估算尺寸**");
        module.Height.ShouldBeGreaterThanOrEqualTo(ModuleViewViewModel.DefaultHeight, "首次摆放按内容估算高度（不再写死最小高）");

        // 未摆放过的实例：UI 取默认位并**立即写回布局**（否则用户不拖就不进组合件）
        var layout = harness.Services.GetLayout().ShouldHaveSingleItem();
        layout.InstanceId.ShouldBe("s1");
        layout.X.ShouldBe(module.X);
        layout.Y.ShouldBe(module.Y);

        // 按声明渲染：数值 + 曲线 + 未渲染元素 + **框架补的配置表单**（声明里没有 B1 元素时补一个）
        module.Elements.Count.ShouldBe(4);
        module.Elements[0].ShouldBeOfType<ValueElementViewModel>();
        module.Elements[1].ShouldBeOfType<CurveElementViewModel>();
        module.Elements[2].ShouldBeOfType<UnsupportedElementViewModel>();
        module.Elements[3].ShouldBeOfType<ConfigurationEditorElementViewModel>();
    }

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
            TimeSpan.FromSeconds(5));

        ready.ShouldBeTrue("曲线元素应按刷新节拍取到环形缓冲里的样本");

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
        harness.Place("b", "test/ui-sink/1.0.0", 260, 0);   // 与 a 右边缘贴合 → 同组
        harness.Windows.Refresh();

        harness.Windows.BeginDrag("a", 10, 10, independent: false).ShouldBeTrue();
        harness.Windows.DragTo(110, 60);

        harness.Windows.Modules.Single(m => m.InstanceId == "a").X.ShouldBe(100);
        harness.Windows.Modules.Single(m => m.InstanceId == "b").X.ShouldBe(360, "整组一起移动");
        harness.Windows.Modules.Single(m => m.InstanceId == "b").Y.ShouldBe(50);

        harness.Windows.EndDrag();
        harness.Windows.IsInteracting.ShouldBeFalse();

        var layout = harness.Services.GetLayout().ToDictionary(l => l.InstanceId);
        layout["a"].X.ShouldBe(100);
        layout["b"].X.ShouldBe(360);
    }

    [Fact]
    public void 拖动_独立模式只移动被拖的那一个()
    {
        using var harness = new UiHarness();
        harness.Place("a", "test/ui-source/1.0.0", 0, 0);
        harness.Place("b", "test/ui-sink/1.0.0", 260, 0);
        harness.Windows.Refresh();

        harness.Windows.BeginDrag("a", 10, 10, independent: true).ShouldBeTrue();
        harness.Windows.DragTo(110, 60);

        harness.Windows.Modules.Single(m => m.InstanceId == "a").X.ShouldBe(100);
        harness.Windows.Modules.Single(m => m.InstanceId == "b").X.ShouldBe(260, "Ctrl 独立拖动（C5-1 约束 3）");
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
        harness.Windows.DragTo(260 + 3 + 10, 410);

        var b = harness.Windows.Modules.Single(m => m.InstanceId == "b");
        b.X.ShouldBe(260, "3 px 落在容差 8 px 内 → 贴合到 a 的右边缘");
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
        harness.Windows.DragTo(260 + 3 + 10, 410, snapEnabled: false);   // 按住 Alt

        harness.Windows.Modules.Single(m => m.InstanceId == "b").X.ShouldBe(263, "停用吸附 → 精确摆放");
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
        module.Width.ShouldBe(300, "右边缘不动 → 宽度增加 40");
        module.Height.ShouldBe(180);
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
