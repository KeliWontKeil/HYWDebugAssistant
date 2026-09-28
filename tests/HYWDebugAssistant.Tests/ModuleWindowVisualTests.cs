using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;
using HYWDebugAssistant.Core.Api;
using HYWDebugAssistant.Core.Runtime;
using HYWDebugAssistant.Core.Services;
using HYWDebugAssistant.FrameworkUi.ViewModels;
using HYWDebugAssistant.FrameworkUi.Views;
using HYWDebugAssistant.Hosting.Registry;
using HYWDebugAssistant.Sdk;
using HYWDebugAssistant.Sdk.Context;
using HYWDebugAssistant.Sdk.Contracts;
using HYWDebugAssistant.Sdk.Lifecycle;
using HYWDebugAssistant.Sdk.Ports;
using HYWDebugAssistant.Sdk.Views;
using HYWDebugAssistant.Ui.Controls;
using HYWDebugAssistant.Ui.Geometry;
using HYWDebugAssistant.Ui.Theming;
using HYWDebugAssistant.Ui.ViewModels;
using HYWDebugAssistant.Ui.Views;
using Shouldly;
using Xunit;


namespace HYWDebugAssistant.Tests;

/// <summary>
/// 无头 UI 测试**专用集合**（禁止与其他测试并行）。
/// </summary>
/// <remarks>
/// 无头会话会在自己的线程上创建 `Application`；若与其他测试类并行，会出现
/// "The calling thread cannot access this object because a different thread owns it"（实测踩过）。
/// </remarks>
[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class AvaloniaUiGroup
{
    /// <summary>集合名（xunit 的 Collection 约定要求定义类名不以 Collection 结尾，CA1711）。</summary>
    public const string Name = "AvaloniaUi";
}

/// <summary>
/// **视图层兜底测试**【M1-16 / ADR-0039 · 用户裁决 D-9】：
/// 在无头会话里构造**真实窗口** → 跑真实布局 → 断言"标记可见、尺寸正确、命中落在标记上"。
/// </summary>
/// <remarks>
/// <para><b>立项原因</b>（两次真实缺陷都没有任何自动化能拦住，而且是用户先撞上的）：
/// ① 口标记被 6 px 拖动带与 12×12 边中点手柄压住 → 双击改标签无效；
/// ② `CornerRadius="{DynamicResource ModuleCornerRadius}"` 引用的是 `x:Double` token →
/// <b>运行时</b> InvalidCastException（点"加入现场"即崩）。
/// 这两类只有"装载真实 XAML + 真实布局 + 真实命中测试"才拦得住 —— 本文件即该最小集。</para>
/// <para><b>技术选型</b>：用 `Avalonia.Headless` 的 <see cref="HeadlessUnitTestSession"/>（自带调度线程，不绑定测试框架）。
/// **不引** `Avalonia.Headless.XUnit`：它在 12.1.2 依赖 xunit v3，与本工程 xunit v2 冲突（CS0433）。
/// 应用宿主用**真实 `App`**，从而加载真实的 Tokens/Controls 资源与 FluentTheme —— 这正是能发现"资源键类型写错"的关键。</para>
/// <para><b>不做的部分</b>：鼠标输入模拟（双击 → 弹窗 → 提交链路）——用户裁决 D-9 明确本批不做（成本更高、收益见后）。</para>
/// </remarks>
[Collection(AvaloniaUiGroup.Name)]
public class ModuleWindowVisualTests : IClassFixture<HeadlessUiFixture>
{
    /// <summary>视觉测试模块的数据类型身份（角色必须挂在一个**已声明**的类型上，否则声明被拒 ⇒ 叠层无线可画）。</summary>
    private const string VisualTypeId = "test/visual-sample";

    /// <summary>视觉测试模块的数据形状。</summary>
    private static readonly ContractShape VisualShape = ContractShape.OfPrimitive(ContractPrimitive.F32);

    private readonly HeadlessUiFixture _ui;

    /// <summary>创建测试。</summary>
    /// <param name="ui">无头会话夹具。</param>
    public ModuleWindowVisualTests(HeadlessUiFixture ui) => _ui = ui;

    [Fact]
    public void 标签页_真实窗口应渲染出标签条与当前页内容()
    {
        _ui.Run(() =>
        {
            using var harness = new VisualHarness();
            harness.PlaceTabsAt("t1", 0);

            var window = harness.OpenWindow(harness.Windows.Modules.ShouldHaveSingleItem());
            VisualHarness.Settle();

            // 【技能 §7.1】真实资源 + 真实窗口：**模板没挂上时不报错、只是什么都不画** ⇒
            // 必须断言"标签条真的出现在可视树里、可见、且有非零尺寸"（VM 里有元素 ≠ 画面上有）。
            var tabs = TabButtons(window);
            tabs.Count.ShouldBe(3, "三页 ⇒ 三个页签（模板缺失时这里会是 0）");
            tabs.ShouldAllBe(b => b.IsEffectivelyVisible);
            tabs.ShouldAllBe(b => b.Bounds.Width > 0 && b.Bounds.Height > 0, "页签必须有非零尺寸（被裁/被压成 0 高＝看不见）");

            tabs[0].Content.ShouldBe("第 1 页", "页签标题取自页节点 Title");
            tabs[0].Classes.Contains("selected").ShouldBeTrue("首屏选中第 1 页");
            tabs[1].Classes.Contains("selected").ShouldBeFalse();

            // 只渲染当前页：窗内文本输入只剩 1 个，且绑定的是 page0
            var inputs = window.GetVisualDescendants().OfType<TextBox>().ToList();
            inputs.Count.ShouldBe(1, "只渲染**当前页**的子元素（此前 bug 是'永远只有第一页'，本次修复后切页才换）");
        });
    }

    [Fact]
    public void 标签条_点击第2个页签应真的切换内容()
    {
        _ui.Run(() =>
        {
            using var harness = new VisualHarness();
            harness.PlaceTabsAt("t1", 0);

            var window = harness.OpenWindow(harness.Windows.Modules.ShouldHaveSingleItem());
            VisualHarness.Settle();

            var target = TabButtons(window)[1];
            var center = target.TranslatePoint(new Point(target.Bounds.Width / 2, target.Bounds.Height / 2), window)
                .ShouldNotBeNull("页签必须在窗口坐标系里可定位（否则点不到）");

            // **真指针点击**（不是 `Command.Execute`）：这样同时覆盖"命中层级 / 是否被盖住"这类只在视觉树上暴露的缺陷。
            window.MouseDown(center, MouseButton.Left);
            window.MouseUp(center, MouseButton.Left);
            VisualHarness.Settle();

            var tabs = TabButtons(window);
            tabs[1].Classes.Contains("selected").ShouldBeTrue("点击后第 2 页应为选中态");
            tabs[0].Classes.Contains("selected").ShouldBeFalse();

            // 内容真的换了：仍然是 1 个输入框，但它是第 2 页那个（可见文本输入对应 page1 的绑定）
            window.GetVisualDescendants().OfType<TextBox>().Count()
                .ShouldBe(1, "切页后仍只渲染当前页");
            harness.Windows.Modules.ShouldHaveSingleItem().Elements
                .OfType<TextInputElementViewModel>().ShouldHaveSingleItem()
                .Source.ShouldBe("page1", "切页后渲染的必须是第 2 页的子元素");
        });
    }

    [Fact]
    public void 多行输入_真实窗口应按声明渲染多行框_且单行不受影响()
    {
        _ui.Run(() =>
        {
            using var harness = new VisualHarness();
            harness.PlaceMultilineAt("m1", 0);

            var window = harness.OpenWindow(harness.Windows.Modules.ShouldHaveSingleItem());
            VisualHarness.Settle();

            var boxes = window.GetVisualDescendants().OfType<TextBox>().ToList();
            boxes.Count.ShouldBe(2, "声明了两个输入元素 ⇒ 两个输入框");

            var multiline = boxes.Single(b => b.DataContext is TextInputElementViewModel { IsMultiline: true });
            var single = boxes.Single(b => b.DataContext is TextInputElementViewModel { IsMultiline: false });

            // 【G2】多行框：`AcceptsReturn` 必须真的打开（否则回车换行/提交都无从谈起）+ 有最小高度
            multiline.AcceptsReturn.ShouldBeTrue("多行输入框必须接受回车（否则永远只是一行）");
            multiline.MinHeight.ShouldBe(
                TextInputElementViewModel.MultilineEditorMinHeight,
                "多行框要有最小高度（约三行），否则只有一个单行高度、看着不像多行框");

            // 单行框行为**不变**（ADR-0040 D-5）
            single.AcceptsReturn.ShouldBeFalse("单行输入不得接受回车（回车 = 提交）");
            single.MinHeight.ShouldBe(0, "单行不设最小高度");
        });
    }

    [Fact]
    public void 多行输入_应用级提交键应下发到模块窗口并重建输入元素()
    {
        _ui.Run(() =>
        {
            using var harness = new VisualHarness();
            harness.PlaceMultilineAt("m1", 0);

            var module = harness.Windows.Modules.ShouldHaveSingleItem();
            _ = harness.OpenWindow(module);
            VisualHarness.Settle();

            Input(module).SubmitOnEnter.ShouldBeFalse("默认档位 `CtrlEnter` ⇒ 回车是换行");

            // 走**用户的真实路径**改设置（设置页 → `UpdateSettings`）⇒ 刷新后必须下发并重建元素。
            // 注意：不能在测试里直接改 `Windows.MultilineSubmitKey` —— 设置是唯一事实源，刷新会用真实设置覆盖它。
            var settings = harness.Services.GetSettings();
            settings.MultilineSubmitKey = "Enter";
            harness.Services.UpdateSettings(settings).Succeeded.ShouldBeTrue();

            harness.Windows.Refresh();
            VisualHarness.Settle();

            Input(module).SubmitOnEnter.ShouldBeTrue("`Enter` 档位 ⇒ 回车提交");
            Input(module).SubmitOnCtrlEnter.ShouldBeFalse("`Enter` 档位 ⇒ Ctrl+回车不提交（换行键是 Shift+回车）");
        });

        static TextInputElementViewModel Input(ModuleViewViewModel module) =>
            module.Elements.OfType<TextInputElementViewModel>().Single(e => e.IsMultiline);
    }

    [Fact]
    public void 输入失败态_模块回报处理失败后_输入框应真的画出红边框()
    {
        _ui.Run(() =>
        {
            using var harness = new VisualHarness();
            harness.PlaceFeedbackAt("f1", 0);

            var module = harness.Windows.Modules.ShouldHaveSingleItem();
            var window = harness.OpenWindow(module);
            VisualHarness.Settle();

            var input = module.Elements.OfType<TextInputElementViewModel>().ShouldHaveSingleItem();
            input.IsInvalid.ShouldBeFalse("**没有回报过就没有失败态**（零回归：不实现 G3 的模块完全不受影响）");

            // 提交一个"非法"输入 ⇒ 模块在自己的活动体里消费并回报失败
            harness.Services.SubmitInput("f1", VisualFeedbackModule.TextSource, "坏值").Succeeded.ShouldBeTrue();
            WaitForInvalid(harness, module, expected: true);

            var box = FindInputBox(window, VisualFeedbackModule.TextSource);
            box.Classes.Contains("invalid").ShouldBeTrue("失败态必须落到**渲染**上（VM 有标志 ≠ 画面有红框）");

            // 【"显著标红"的可执行判据】不看 VM 标志，直接看**负责画边框的那两个属性**：
            // ① 边框必须从样式表拿到了更粗的框（样式选择器写错 ⇒ 这里仍是默认 1px）；
            // ② 边框颜色必须**显著偏红**（未生效时它是默认中性色 / 透明）。
            // 这样断言不依赖"能不能查到 token 资源"，测的仍是用户看得见的东西。
            box.BorderThickness.ShouldBe(
                new Avalonia.Thickness(2),
                "失败态样式必须真的生效（`TextBox.invalid` 选择器没匹配上时边框仍是 1px）");

            var borderColor = ColorOf(box.BorderBrush);
            borderColor.R.ShouldBeGreaterThan(
                (byte)0x80, $"失败态边框必须显著偏红，实际 R=0x{borderColor.R:X2}");
            borderColor.R.ShouldBeGreaterThan(
                (byte)(borderColor.G + 0x40), $"失败态边框必须显著偏红（G 太亮，实际 0x{borderColor.G:X2}）");
            borderColor.R.ShouldBeGreaterThan(
                (byte)(borderColor.B + 0x40), $"失败态边框必须显著偏红（B 太亮，实际 0x{borderColor.B:X2}）");
        });
    }

    [Fact]
    public void 输入失败态_下一次提交即刷新_不再回报就自动恢复()
    {
        _ui.Run(() =>
        {
            using var harness = new VisualHarness();
            harness.PlaceFeedbackAt("f1", 0);

            var module = harness.Windows.Modules.ShouldHaveSingleItem();
            _ = harness.OpenWindow(module);
            VisualHarness.Settle();

            var input = module.Elements.OfType<TextInputElementViewModel>().ShouldHaveSingleItem();

            harness.Services.SubmitInput("f1", VisualFeedbackModule.TextSource, "坏值").Succeeded.ShouldBeTrue();
            WaitForInvalid(harness, module, expected: true);
            input.LastResult.ShouldBe("非法输入：坏值", "模块给的原因要就地显示");

            // 再提交一次（这次合法）⇒ 框架在**投递入队时**已清旧结果，模块回报成功 ⇒ 红框消失
            harness.Services.SubmitInput("f1", VisualFeedbackModule.TextSource, "ok").Succeeded.ShouldBeTrue();
            WaitForInvalid(harness, module, expected: false);
        });
    }

    [Fact]
    public void 文件选择器_真实窗口应渲染出标签与只读路径框与浏览按钮()
    {
        _ui.Run(() =>
        {
            using var harness = new VisualHarness();
            harness.PlaceFilePickerAt("p1", 0);

            var module = harness.Windows.Modules.ShouldHaveSingleItem();
            var window = harness.OpenWindow(module);
            VisualHarness.Settle();

            // 【G4】此前 B6 落"未渲染"占位（只显示一行"未渲染"文字）⇒ 这里断言它**真的被画出来**
            var pickers = module.Elements.OfType<FileSelectorElementViewModel>().ToList();
            pickers.Count.ShouldBe(2, "声明了两个文件选择器 ⇒ 两个元素");
            pickers[0].Mode.ShouldBe(FilePickerMode.Save, "第一个声明的是「保存」");
            pickers[1].Mode.ShouldBe(FilePickerMode.Open, "第二个声明的是「打开」");
            pickers[1].Filter.ShouldBe("文本文件 (*.txt)|*.txt", "过滤器原样带到视图模型（供弹窗用）");

            var browseButtons = window.GetVisualDescendants()
                .OfType<Button>()
                .Where(b => b.DataContext is FileSelectorElementViewModel)
                .ToList();

            browseButtons.Count.ShouldBe(2, "每个选择器一个「浏览」按钮（没渲染时这里会是 0）");
            browseButtons.ShouldAllBe(b => b.IsEffectivelyVisible);
            browseButtons.ShouldAllBe(b => b.Bounds.Width > 0 && b.Bounds.Height > 0, "按钮必须有非零尺寸（被裁/压成 0 高＝看不见）");
            browseButtons[0].Content.ShouldBe("浏览…");

            // 路径框必须是**只读**的：路径由系统对话框给出，不允许手打（否则就不是"选择器"了）
            var pathBoxes = window.GetVisualDescendants()
                .OfType<TextBox>()
                .Where(t => t.DataContext is FileSelectorElementViewModel)
                .ToList();

            pathBoxes.Count.ShouldBe(2, "每个选择器一个路径框");
            pathBoxes.ShouldAllBe(t => t.IsReadOnly);
        });
    }

    [Fact]
    public void 文件选择器_提交路径应被框架接受_且就地给出结果()
    {
        _ui.Run(() =>
        {
            using var harness = new VisualHarness();
            harness.PlaceFilePickerAt("p1", 0);

            var module = harness.Windows.Modules.ShouldHaveSingleItem();
            _ = harness.OpenWindow(module);
            VisualHarness.Settle();

            var picker = module.Elements.OfType<FileSelectorElementViewModel>().First();

            // 等价于"用户在对话框里选好了文件" ⇒ `Submit` 走**既有输入通道**（与 B5 同一条路）
            picker.Submit(@"C:\temp\out.txt");

            picker.SelectedPath.ShouldBe(@"C:\temp\out.txt", "选中的路径要显示在只读框里");
            picker.LastResult.ShouldNotBeNullOrEmpty();
            picker.LastResult!.ShouldContain("已写入", Case.Insensitive, "框架必须接受该路径（该源名已在视图里声明）⇒ 投递入队成功");
            picker.LastResult!.ShouldNotContain("未生效", Case.Insensitive);

            // 未声明的源名 ⇒ 明确失败、不静默（与 B5 输入同一口径；走公开面直接验框架校验）
            var rejected = harness.Services.SubmitInput("p1", "not-declared", @"C:\temp\x.txt");
            rejected.Succeeded.ShouldBeFalse("未声明的输入必须被拒绝");
            string.Join("；", rejected.Failures).ShouldContain("未声明该输入", Case.Insensitive);
        });
    }

    /// <summary>轮询等待失败态变成期望值（模块的活动体在**自己的线程**上跑，需要给它时间）。</summary>
    private static void WaitForInvalid(VisualHarness harness, ModuleViewViewModel module, bool expected)
    {
        for (var i = 0; i < 100; i++)
        {
            Thread.Sleep(10);
            harness.Windows.Refresh();

            if (module.Elements.OfType<TextInputElementViewModel>().Single().IsInvalid == expected)
            {
                return;
            }
        }

        throw new InvalidOperationException($"等待输入失败态 = {expected} 超时（1 秒）");
    }

    /// <summary>窗内绑定到指定输入源的输入框。</summary>
    private static TextBox FindInputBox(Window window, string source) =>
        window.GetVisualDescendants()
            .OfType<TextBox>()
            .Single(b => b.DataContext is TextInputElementViewModel input
                         && string.Equals(input.Source, source, StringComparison.Ordinal));

    /// <summary>窗内**标签条**上的页签按钮（按样式类定位；未渲染时返回空表）。</summary>
    /// <param name="window">模块窗口。</param>
    private static List<Button> TabButtons(Window window) =>
        [.. window.GetVisualDescendants()
            .OfType<Button>()
            .Where(b => b.Classes.Contains("tabStripItem"))];

    [Fact]
    public void 模块窗口_应能装载真实XAML并显示端口标记()
    {
        _ui.Run(() =>
        {
            using var harness = new VisualHarness();
            harness.Place("s1");

            var module = harness.Windows.Modules.ShouldHaveSingleItem();
            var window = harness.OpenWindow(module);

            var marker = FindMarker(window);
            marker.ShouldNotBeNull("模块窗口必须渲染出口标记（资源类型写错 / 层级被盖住时根本到不了这一步）");
            marker.IsEffectivelyVisible.ShouldBeTrue("标记必须**可见**（底色与边框对比过低＝等于看不见）");

            // 左右边缘：宽 20 · 长 114（ADR-0039 D3：长度比原 76 **加长 50%**）
            marker.Bounds.Width.ShouldBe(20, 1e-6);
            marker.Bounds.Height.ShouldBe(114, 1e-6);

            // 贴边：标记外沿与窗口边框重合（EdgeInset = 0）
            marker.Bounds.Left.ShouldBe(0, 1e-6, "标记应与外边框完全重合");
        });
    }

    [Fact]
    public void 端口标记_不应被交互带或缩放手柄抢走命中()
    {
        _ui.Run(() =>
        {
            using var harness = new VisualHarness();
            harness.Place("s1");

            var module = harness.Windows.Modules.ShouldHaveSingleItem();
            var window = harness.OpenWindow(module);
            var marker = FindMarker(window).ShouldNotBeNull();

            var center = marker.TranslatePoint(new Point(marker.Bounds.Width / 2, marker.Bounds.Height / 2), window);
            center.ShouldNotBeNull();

            VisualHarness.Settle();
            var hit = window.InputHitTest(center.Value);
            hit.ShouldNotBeNull(
                $"标记中心必须命得到元素（命中为空 = 被透明层吃掉）；"
                + $"窗口 {window.Bounds.Width}×{window.Bounds.Height}，标记 {marker.Bounds.Left},{marker.Bounds.Top} {marker.Bounds.Width}×{marker.Bounds.Height}，"
                + $"探测点 {center.Value.X:F1},{center.Value.Y:F1}");
            IsInsideMarker(hit).ShouldBeTrue(
                $"标记中心的命中必须落在标记上（实际命中 {Describe(hit)}）：此前边中点缩放手柄（同为边缘垂直居中）"
                + "与 6 px 拖动带压在标记之上，双击被吃掉 → 表现为\"网络标签改不了\"");
        });
    }

    [Fact]
    public void 模块窗口_未选中时手柄不得可命中_选中后放开()
    {
        _ui.Run(() =>
        {
            using var harness = new VisualHarness();
            harness.Place("s1");

            var module = harness.Windows.Modules.ShouldHaveSingleItem();
            var window = harness.OpenWindow(module);

            var handle = FindByName<Avalonia.Controls.Shapes.Rectangle>(window, "HandleLeft").ShouldNotBeNull();
            handle.IsHitTestVisible.ShouldBeFalse("未选中时手柄不得可命中（否则压住端口标记中心 → 双击失效）");

            module.IsSelected = true;
            Dispatcher.UIThread.RunJobs();

            handle.IsHitTestVisible.ShouldBeTrue("选中后手柄可命中（可缩放）");
        });
    }

    [Fact]
    public void 外壳窗口_应能装载真实XAML并完成布局()
    {
        _ui.Run(() =>
        {
            using var harness = new VisualHarness();

            var shell = new ShellViewModel(harness.Services, harness.Services);
            var window = new MainWindow { DataContext = shell };
            window.Show();
            VisualHarness.Settle();

            window.IsVisible.ShouldBeTrue();
            window.Bounds.Width.ShouldBeGreaterThan(0, "外壳必须完成布局（XAML/资源异常会在此前抛出）");

            // 侧边栏形态【ADR-0038 D1】：**活动栏** 6 个图标入口（按 Name 定位，避免把页面里的同类按钮算进来）
            var rail = window.GetVisualDescendants()
                .OfType<StackPanel>()
                .FirstOrDefault(p => string.Equals(p.Name, "RailButtons", StringComparison.Ordinal));

            rail.ShouldNotBeNull("活动栏容器必须存在（Name=RailButtons）");
            rail!.GetVisualDescendants().OfType<Button>().Count()
                .ShouldBe(6, "活动栏应有 6 个入口（模块清单 / 交互 / 拓扑 / Issue / 设置 / 帮助）");

            shell.IsCatalogPage.ShouldBeTrue("默认停在模块清单页");
            window.GetVisualDescendants().OfType<ModuleCatalogView>().ShouldNotBeEmpty("模块清单是 B 区常驻页面");

            // 【用户第七轮反馈】模块清单顶部：**分类栏**（图标无文字）+ 搜索框
            shell.Catalog.Categories.Count.ShouldBe(5, "分类 = 全部 / 数据源 / 显示与接收 / 记录与断言 / 其他");
            shell.Catalog.SelectedCategory.ShouldBe(ModuleCategories.All);

            // 切到交互页：页面切换必须真的换视图（滑块高亮状态由 Classes.active 表达）
            shell.ShowPageCommand.Execute(ShellPages.Interaction);
            VisualHarness.Settle();

            shell.IsInteractionPage.ShouldBeTrue();
            window.GetVisualDescendants().OfType<InteractionView>().ShouldNotBeEmpty("交互也是 B 区常驻页面");
        });
    }

    [Fact]
    public void 叠加信息层_默认关闭且开关真正控制可见性()
    {
        // 【ADR-0045 D1 · 用户第十五轮裁决】叠加层是**调试/观测层**：默认关闭（快捷键 L 切换），
        // 且**不进常驻界面**；端口标记**常显**、不受开关影响。
        _ui.Run(() =>
        {
            using var harness = new VisualHarness();
            harness.PlaceAt("s1", 0);

            var module = harness.Windows.Modules.ShouldHaveSingleItem();
            var window = harness.OpenWindow(module);

            var overlay = window.GetVisualDescendants().OfType<ConnectionOverlay>().SingleOrDefault();
            overlay.ShouldNotBeNull("模块窗口必须包含叠加信息层（原'口上标记'开关是死开关：没有任何绑定）");

            overlay.IsVisible.ShouldBeFalse("**默认关闭**（ADR-0045 D1）");
            module.ShowInfoOverlay.ShouldBeFalse();
            new ApplicationSettings().ShowInfoOverlay.ShouldBeFalse("设置模型的默认值也必须是关闭");

            // 打开开关 → 叠加层显示（端口标记始终显示）
            harness.Windows.ShowInfoOverlay = true;
            harness.Windows.Refresh();
            VisualHarness.Settle();

            overlay.IsVisible.ShouldBeTrue("开关必须真的控制叠加层的可见性");
            FindMarker(window).ShouldNotBeNull("端口标记是模块标准件，始终显示");

            // 关掉 → 隐藏
            harness.Windows.ShowInfoOverlay = false;
            harness.Windows.Refresh();
            VisualHarness.Settle();

            overlay.IsVisible.ShouldBeFalse();
        });
    }

    [Fact]
    public void 叠加信息层_遮罩在标记之下而连线在标记之上()
    {
        // 【用户第三十四轮："网络标签不高亮**指的不是在上面覆盖一层**……我的意思是**做减法**，将网络标签也埋入灰色玻璃下方"】
        // 层级（自下而上）：内容 → **端口标记** → **遮罩** → **连线/角色层** → 运行指示/按钮簇。
        // · 遮罩在标记**之上** ⇒ 叠加层开启时标记跟着**沉下去**（不再"高亮"）；
        // · 连线层仍在一切之上 ⇒ 连线**跨过网络标签**直达窗口边界：标记是不透明块（20px 宽），
        //   连线若在其下，拼接处会被**两个标记遮掉 40px**，整条连接看起来"从标签连到中心"、失去意义；
        // · 两层都 `IsHitTestVisible=false` ⇒ 标记**照样可双击建网**（命中穿透）。
        _ui.Run(() =>
        {
            using var harness = new VisualHarness();
            harness.PlaceAt("s1", 0);

            var module = harness.Windows.Modules.ShouldHaveSingleItem();
            var window = harness.OpenWindow(module);

            var frame = FindByName<Border>(window, "Frame").ShouldNotBeNull();
            var root = frame!.Child.ShouldBeOfType<Grid>();

            var scrim = root.Children.OfType<Border>().SingleOrDefault(b => b.Name == "OverlayScrim");
            scrim.ShouldNotBeNull("必须有一层遮罩（'灰玻璃' = 半透明深色 + 其上浅色信息）");

            var overlay = root.Children.OfType<ConnectionOverlay>().SingleOrDefault();
            overlay.ShouldNotBeNull();

            var markers = root.Children.OfType<ItemsControl>().SingleOrDefault(c => c.Name == "PortMarkers");
            markers.ShouldNotBeNull();

            var overlaps = root.Children.OfType<ConnectionOverlay>().Select(root.Children.IndexOf).ToList();
            overlaps.ShouldHaveSingleItem("叠加层图形/连线应当只有一层（连线压在标记之上）");

            var scrimIndex = root.Children.IndexOf(scrim!);
            var markerIndex = root.Children.IndexOf(markers!);
            var overlayIndex = root.Children.IndexOf(overlay!);

            markerIndex.ShouldBeLessThan(scrimIndex, "端口标记必须在**遮罩之下**（做减法：埋进灰色玻璃里，不再单独调色）");
            scrimIndex.ShouldBeLessThan(overlayIndex, "连线/角色层必须在遮罩之上（角色层才是叠加层的主体）");
            markerIndex.ShouldBeLessThan(overlayIndex, "连线层必须在端口标记之上（连线要跨过网络标签）");
            overlay!.IsHitTestVisible.ShouldBeFalse("叠加层不得参与命中（双击改网络名/拖动/缩放照常）");
            scrim!.IsHitTestVisible.ShouldBeFalse("遮罩同样不得参与命中（命中穿透到标记）");
        });
    }

    // 【已删除：叠加信息层_未连标签必须朝窗口外且不压住角色方块】
    // 「未连」文字随用户第三十三轮口径一并删除（没连上时指示灯会灭，文字多余，且会压住 20px 端口标记带）；
    // 该断言随被删功能一起带走，不留双轨（ADR-0049 Q8/Q9）。叠加层的**文字纪律**改由
    // `RoleProjectionTests.判据7b`（信息区只放身份 + 必要事实）与下方"叠加信息层_信息区只画身份与必要事实"守卫。

    [Fact]
    public void 叠加信息层_角色方块单列居中且线从本端接口侧锚点裁到窗口边界()
    {
        // 【ADR-0052 §5 + ADR-0049 D14】**整体替换**后的几何：
        // 方块 = **模块声明的功能分块**（左入右出的处理块）；**单列竖直堆叠、居中**（不分左右侧）；
        // 线 = 「本端接口侧锚点 → 对端接口侧锚点」这一条线段**裁到本窗内**的那一段。
        // 旧断言（发送靠右/接收靠左 + 方位分类 + 10px 残段）已随实现整体替换（不留双轨）。
        _ui.Run(() =>
        {
            using var harness = new VisualHarness();
            harness.PlaceAt("s1", 0);

            var module = harness.Windows.Modules.ShouldHaveSingleItem();
            var window = harness.OpenWindow(module);
            VisualHarness.Settle();

            var width = window.Bounds.Width;
            var height = window.Bounds.Height;

            // 手工构造一个"两个角色"的投影（视觉测试不依赖真实拓扑即可断言几何）
            var roles = new List<RoleBoxViewModel>
            {
                new("s1/source", "source", "发送", "hyw/analog-sample@v1", "{seq:u64, value:f32}", [], ["out"], null, 1),
                new("s1/sink", "sink", "显示", "hyw/analog-sample@v1", "{seq:u64, value:f32}", ["in"], [], 1, null),
            };

            var layout = ConnectionOverlay.LayoutRoles(roles, width, height);

            layout.Boxes.Count.ShouldBe(2, "**声明的角色数 = 方块数**（不发明角色）");
            layout.Hidden.ShouldBe(0);

            // 单列：两块同一个 X，整组竖直居中（"分侧摆放"已废弃 ⇒ 不再有左右分区）
            layout.Boxes.Select(b => Math.Round(b.Box.Center.X, 3)).Distinct().Count().ShouldBe(1, "单列 ⇒ 同一个 X");
            layout.Boxes.Average(b => b.Box.Center.Y).ShouldBe(height / 2, 0.001, "整组竖直居中");
            layout.Boxes[0].Box.Bottom.ShouldBeLessThanOrEqualTo(layout.Boxes[1].Box.Y + 0.001, "按声明顺序自上而下");
            layout.Boxes[0].Box.Y.ShouldBeGreaterThanOrEqualTo(ConnectionOverlay.RoleBoxMargin - 0.001);
            layout.Boxes[1].Box.Bottom.ShouldBeLessThanOrEqualTo(height - ConnectionOverlay.RoleBoxMargin + 0.001);

            var source = layout.Boxes[0];
            var sink = layout.Boxes[1];

            source.HasOutput.ShouldBeTrue("纯源分块 ⇒ 有输出侧");
            source.HasInput.ShouldBeFalse("纯源分块 ⇒ 没有输入侧（左边不画方向边）");
            sink.HasInput.ShouldBeTrue();
            sink.HasOutput.ShouldBeFalse();

            // 【ADR-0053】端点 = **两个角色方块中心**；绘制段裁到本窗内、并裁掉本端方块内部
            var overlayWindow = new Avalonia.Rect(0, 0, width, height);
            var target = new Avalonia.Point(width * 2, sink.Box.Center.Y);
            var (near, far) = ConnectionOverlay.ClipSegment(overlayWindow, source.Center, target);

            near.X.ShouldBe(source.Center.X, 0.001, "端点 = **本端方块中心**（用户第三十四轮：连线从角色方块中心出发）");
            near.Y.ShouldBe(source.Center.Y, 0.001);
            far.X.ShouldBe(width, 0.001, "本窗那段止于**窗口边界**（拼接后与对端那半段相接）");
            (far.X - near.X).ShouldBeGreaterThan(
                PortMarkerPanel.BandThickness * 2,
                "线长必须显著大于旧实现的 10px 残段（否则又'像 bug'）");

            // 接收侧：同一套算法，方向自然反过来（无需方位分类）
            var inbound = ConnectionOverlay.LinkSegment(
                overlayWindow,
                sink.Box,
                new Avalonia.Point(-width, source.Box.Center.Y),
                localIsSend: false);

            inbound.End.X.ShouldBe(sink.Box.Left, 0.001, "接收侧：终点落在**方块边界**（不压进方块）");
            inbound.Start.X.ShouldBe(0, 0.001, "对端在左 ⇒ 本窗那段从左边界开始");
            Math.Abs(inbound.End.X - sink.Box.Center.X).ShouldBeLessThan(
                Math.Abs(inbound.Start.X - sink.Box.Center.X),
                "接收侧的绘制方向 = **朝本端方块**（向内）");

            // **角色方块边界交点**：朝方块外一点射出 ⇒ 交点落在方块上（内部处理顺序线的端部）
            var edge = ConnectionOverlay.EdgePoint(source.Box, new Avalonia.Point(-width, source.Box.Center.Y));
            edge.X.ShouldBe(source.Box.Left, 0.001, "交点落在方块边界（线不压进方块里）");
            edge.Y.ShouldBe(source.Box.Center.Y, 0.001);

            // 未被声明的端口 ⇒ 不画（叠加层只表达角色；口 → 网络那层是端口标记的职责）
            ConnectionOverlay.LayoutRoles([], width, height).Boxes.ShouldBeEmpty("没有角色 ⇒ 空叠加层（不发明方块）");
        });
    }

    [Fact]
    public void 叠加信息层_中点色与虚线相位应是两端一致的纯函数()
    {
        // 【ADR-0045 D4 · ADR-0053 修正】两个窗口各画自己那半段，靠"**同一条中点色规则**"在公共边颜色相接；
        // 虚线**两侧用同一个偏移量**（因为线的几何恒按数据流方向绘制 ⇒ 看起来一外一内），
        // 这些必须是**纯函数**，否则两端会各算各的。
        var send = Color.Parse("#EA580C");
        var receive = Color.Parse("#0891B2");

        var mid = ConnectionOverlay.MidColor(send, receive);
        mid.ShouldBe(ConnectionOverlay.MidColor(receive, send), "中点色对两端对称（否则拼接处会出现色差）");
        mid.R.ShouldBe((byte)((send.R + receive.R) / 2));
        mid.G.ShouldBe((byte)((send.G + receive.G) / 2));
        mid.B.ShouldBe((byte)((send.B + receive.B) / 2));

        OverlayAnimationClock.DashOffsetAt(0).ShouldBe(0, "相位 0 ⇒ 偏移 0");
        OverlayAnimationClock.DashOffsetAt(1).ShouldBeLessThan(0, "取负 ⇒ 图案沿绘制方向前进");
        OverlayAnimationClock.DashOffsetAt(3).ShouldBe(
            -3 / ConnectionOverlay.LineThickness,
            1e-9,
            "偏移 = 负相位 ÷ 线宽（`DashStyle.Offset` 的单位是**笔宽倍数**，由渲染实测确认）");

        for (var second = 0; second < 200; second++)
        {
            var phase = OverlayAnimationClock.PhaseAt(second * 0.031);
            phase.ShouldBeInRange(0, OverlayAnimationClock.Period, "相位必须落在 [0, 周期)");
        }

        OverlayAnimationClock.PhaseAt(0).ShouldBe(0, "相位零点 ⇒ 0（两端同相启动）");
        OverlayAnimationClock.PhaseAt(OverlayAnimationClock.Period / OverlayAnimationClock.SpeedDipsPerSecond)
            .ShouldBe(0, 1e-9, "一个周期后回到同一相位");
    }

    [Fact]
    public void 叠加信息层_动画时钟只在可见时订阅()
    {
        // 【ADR-0045】动画纪律：一个**共享** 20Hz 时钟；叠加层关闭时**退订**（不能白烧 CPU）。
        _ui.Run(() =>
        {
            using var harness = new VisualHarness();
            harness.PlaceAt("s1", 0);

            var module = harness.Windows.Modules.ShouldHaveSingleItem();
            var window = harness.OpenWindow(module);
            VisualHarness.Settle();

            var baseline = OverlayAnimationClock.SubscriberCount;

            // 默认关闭 ⇒ 不订阅
            OverlayAnimationClock.SubscriberCount.ShouldBe(baseline, "叠加层关闭时不得订阅动画时钟");

            // 打开 ⇒ 订阅 +1
            harness.Windows.ShowInfoOverlay = true;
            harness.Windows.Refresh();
            VisualHarness.Settle();

            OverlayAnimationClock.SubscriberCount.ShouldBe(baseline + 1, "可见的叠加层应订阅共享时钟");
            OverlayAnimationClock.IsRunning.ShouldBeTrue("有订阅者时时钟才跑");

            // 关闭 ⇒ 退订
            harness.Windows.ShowInfoOverlay = false;
            harness.Windows.Refresh();
            VisualHarness.Settle();

            OverlayAnimationClock.SubscriberCount.ShouldBe(baseline, "关闭后必须退订");
        });
    }

    [Fact]
    public void 叠加信息层_连线应对准对端窗口中心()
    {
        // 【用户第三十三轮口径 · ADR-0049 D4 简化】线是**两点一线**：起点 = 本端角色方块中心、
        // 瞄准点 = **对端窗口中心**（本窗局部坐标）—— 长度与角度由此自然得出，不再有"方位分类"。
        _ui.Run(() =>
        {
            using var harness = new VisualHarness();
            harness.PlaceAt("s1", 0);
            harness.PlaceSinkAt("r1", 400);   // 接收端在 s1 右侧

            harness.Services.UpdatePortNetwork("s1", "out", "bus").Succeeded.ShouldBeTrue();
            harness.Services.UpdatePortNetwork("r1", "in", "bus").Succeeded.ShouldBeTrue();

            // 叠加层开启（用户第三十四轮：标记在叠加层里显示**通路速率**并降权）
            harness.Windows.ShowInfoOverlay = true;
            harness.Windows.Refresh();
            VisualHarness.Settle();

            var sender = harness.Windows.Modules.Single(m => m.InstanceId == "s1");
            var marker = sender.Ports.ShouldHaveSingleItem();

            marker.IsConnected.ShouldBeTrue();
            marker.PeerText.ShouldBe("r1.in", "标记只保留**悬停提示**用的对端文本（连线改由角色对驱动）");

            var link = sender.RoleLinks.ShouldHaveSingleItem();
            link.Target.X.ShouldBeGreaterThan(
                sender.Width / 2,
                "对端在右侧 ⇒ 瞄准点（对端方块中心）在**本窗局部坐标的右半边**（线自然朝右走，无需方位分类）");
            link.PeerAdjacent.ShouldBeFalse("两窗相隔 400px ⇒ 未拼接 ⇒ 线末端画断口并标注对端");
            link.PeerText.ShouldBe("r1");
            link.PortPairCount.ShouldBe(1, "这一对角色之间只有一条口对投递（线上不标条数）");

            // 【ADR-0053 · 用户第三十四轮】**角色自身的指标画在角色方块上**（线不承载文字）：
            // 速率/类型标签取框架的**角色维度聚合**（Core 算、UI 只格式化）
            var role = sender.Roles.ShouldHaveSingleItem();
            role.RateText.ShouldNotBeNull();
            role.InfoLines.Count.ShouldBeGreaterThanOrEqualTo(2, "①速率 ②`输入 m / 输出 n`（口数）");
            role.InfoLines[1].Text.ShouldBe("输入 0 / 输出 1", "纯源分块的**口数**（与方块上的连接数 `m/n` 不同）");
            role.InfoLines[0].FontSize.ShouldBeGreaterThan(
                role.InfoLines[1].FontSize,
                "速率用大字（用户：字体更大一些）");

            // 通路速率（**通路维度**）常驻在端口标记上
            marker.RateText.ShouldNotBeNull();
        });
    }

    [Fact]
    public void 叠加信息层_端口标记应常驻显示通路速率()
    {
        // 【ADR-0053 · 用户第三十四轮】用户裁定两件事：
        // ① "网络标签上缺少速率显示，这才是'**数据通路速率**'" + "速率显示**常驻**于网络标签上，**它不属于叠加层**"
        //    ⇒ 速率与叠加层开关**无关**，始终显示；
        // ② "网络标签不高亮 = **做减法**（埋进灰色玻璃下方）" ⇒ 由**层级**实现（见"层级与遮罩"用例），
        //    **不再**给标记加降权样式（那是做加法）。
        _ui.Run(() =>
        {
            using var harness = new VisualHarness();
            harness.PlaceAt("s1", 0);

            var module = harness.Windows.Modules.ShouldHaveSingleItem();
            var window = harness.OpenWindow(module);
            VisualHarness.Settle();

            // 叠加层**关闭**时：速率照样在（常驻）
            FindMarker(window).ShouldNotBeNull();
            var rateOff = FindVisibleByClass<TextBlock>(window, "markerRate").ShouldNotBeNull();
            rateOff!.Text.ShouldNotBeNullOrEmpty("速率**常驻**（与叠加层开关无关）");

            var name = FindVisibleByClass<TextBlock>(window, "markerName").ShouldNotBeNull();
            name!.Text.ShouldBe("无网络", "名称仍然在（速率是**增加**，不是取代）");
            rateOff.FontSize.ShouldBeLessThan(name.FontSize, "速率字号比名称小一号（层级清楚）");

            // 叠加层**开启**后：速率依然在（不被叠加层"接管"）
            harness.Windows.ShowInfoOverlay = true;
            harness.Windows.Refresh();
            VisualHarness.Settle();

            FindVisibleByClass<TextBlock>(window, "markerRate").ShouldNotBeNull("叠加层开启后速率依然常驻");
        });
    }

    [Fact]
    public void 叠加信息层_对端方块中心的坐标系必须与本窗一致()
    {
        // 【用户第三十四轮："角色方块间的连线仍然没有对齐……我怀疑是当前代码中**多套坐标系相互冲突**，请确定唯一事实源"】
        // 对齐的**唯一事实源**（本用例把它钉住）：
        //   ① **一个模块的"画布尺寸"只有一个**：`ModuleViewViewModel.Width/Height`，叠加层以 `CanvasWidth/Height` 接收它
        //      （叠加层控件自身在 `Frame` 边框**之内** ⇒ `Bounds` 比窗口小 2 DIP；若一端按 `Bounds`、另一端按 VM 尺寸布局，
        //       两端算出的方块位置就不同 ⇒ 线错位。实测：`Bounds.Width = 318` vs `Width = 320`）；
        //   ② **世界坐标只在换算时出现**：本窗局部 ↔ 对端局部经 `Rect.X/Y` 之差换算，其余一律本地坐标。
        _ui.Run(() =>
        {
            using var harness = new VisualHarness();
            harness.PlaceAt("s1", 0);
            harness.PlaceSinkAt("r1", 400);

            harness.Services.UpdatePortNetwork("s1", "out", "bus").Succeeded.ShouldBeTrue();
            harness.Services.UpdatePortNetwork("r1", "in", "bus").Succeeded.ShouldBeTrue();
            harness.Windows.ShowInfoOverlay = true;
            harness.Windows.Refresh();
            VisualHarness.Settle();

            var sender = harness.Windows.Modules.Single(m => m.InstanceId == "s1");
            var sink = harness.Windows.Modules.Single(m => m.InstanceId == "r1");

            var senderWindow = harness.OpenWindow(sender);
            var sinkWindow = harness.OpenWindow(sink);
            VisualHarness.Settle();

            var senderOverlay = FindByName<ConnectionOverlay>(senderWindow, "Overlay").ShouldNotBeNull();
            var sinkOverlay = FindByName<ConnectionOverlay>(sinkWindow, "Overlay").ShouldNotBeNull();

            // ① 画布尺寸唯一：叠加层的 `CanvasWidth/Height` 必须 = 视图模型的**DIP** 画布尺寸
            //    （= 模型物理像素 ÷ 缩放；对端算方块位置时用的是同一对数字）
            senderOverlay!.CanvasWidth.ShouldBe(sender.CanvasWidth, 0.001, "叠加层画布宽必须 = 视图模型的 CanvasWidth（唯一事实源）");
            senderOverlay.CanvasHeight.ShouldBe(sender.CanvasHeight, 0.001, "叠加层画布高必须 = 视图模型的 CanvasHeight");
            sinkOverlay!.CanvasWidth.ShouldBe(sink.CanvasWidth, 0.001);
            sinkOverlay.CanvasHeight.ShouldBe(sink.CanvasHeight, 0.001);
            sender.CanvasWidth.ShouldBe(sender.Width / sender.Scale, 0.001, "DIP 画布 = 物理像素 ÷ 缩放");

            // 叠加层的**实际画布**必须等于窗口客户区（`Margin="-1"` 抵消 `Frame` 的 1px 边框）——
            // 否则连线会停在离窗口外沿 1px 处，两窗拼接时接不上（缩放 1 时 DIP 与绘制度量一致）
            senderOverlay.Bounds.Width.ShouldBe(sender.CanvasWidth, 0.5, "画布必须铺满窗口客户区（负边距抵消边框）");
            senderOverlay.Bounds.Height.ShouldBe(sender.CanvasHeight, 0.5);

            // ② 本窗算出的"对端方块中心"必须 = 对端自己画的那个方块中心（换算到本窗局部坐标）
            var link = sender.RoleLinks.ShouldHaveSingleItem();

            var peerLayout = ConnectionOverlay.LayoutRoles(sink.Roles, sinkOverlay.CanvasWidth, sinkOverlay.CanvasHeight);
            var peerBox = peerLayout.Boxes.ShouldHaveSingleItem();
            var peerCenterAsDrawn = new Point(
                peerBox.Center.X + sender.ToDip(sink.Rect.X - sender.Rect.X),
                peerBox.Center.Y + sender.ToDip(sink.Rect.Y - sender.Rect.Y));

            link.Target.X.ShouldBe(peerCenterAsDrawn.X, 0.5, "瞄准点必须落在对端**实际画出来**的方块中心（否则线端错位）");
            link.Target.Y.ShouldBe(peerCenterAsDrawn.Y, 0.5, "瞄准点必须落在对端**实际画出来**的方块中心（否则线端错位）");
        });
    }

    [Fact]
    public void 叠加信息层_贴合的对端应识别为已拼接()
    {
        // 【ADR-0045 D4】拼接（接触）时：线到边界直接接过去、**不标对端**（标在缝上会糊）。
        _ui.Run(() =>
        {
            using var harness = new VisualHarness();
            harness.PlaceAt("s1", 0);
            harness.PlaceSinkAt("r1", ModuleViewViewModel.DefaultWidth);   // 紧贴 s1 右边缘

            harness.Services.UpdatePortNetwork("s1", "out", "bus").Succeeded.ShouldBeTrue();
            harness.Services.UpdatePortNetwork("r1", "in", "bus").Succeeded.ShouldBeTrue();
            harness.Windows.Refresh();
            VisualHarness.Settle();

            var sender = harness.Windows.Modules.Single(m => m.InstanceId == "s1");
            var link = sender.RoleLinks.ShouldHaveSingleItem();

            link.PeerAdjacent.ShouldBeTrue("两窗接触 ⇒ 已拼接 ⇒ 连线在公共边直接接过去（不淡出、不标对端）");
            link.PeerText.ShouldBeNull("已拼接 ⇒ 不标注对端");
        });
    }

    [Fact]
    public void 主题应用器_应把主题色与背景写到资源上()
    {
        _ui.Run(() =>
        {
            var settings = new ApplicationSettings
            {
                AccentColor = "#16A34A",
                BackgroundMode = ThemeApplier.BackgroundLightGray,
            };

            ThemeApplier.Apply(settings);

            Application.Current!.TryGetResource("AccentPrimary", ThemeVariant.Light, out var accentResource).ShouldBeTrue();
            accentResource.ShouldBeAssignableTo<ISolidColorBrush>()!.Color
                .ShouldBe(Color.Parse("#16A34A"), "主题色必须落到角色 token 上（视图只取 token，故全体窗口一并跟随）");

            Application.Current.TryGetResource("BgWorkspace", ThemeVariant.Light, out var workspaceResource).ShouldBeTrue();
            workspaceResource.ShouldBeAssignableTo<ISolidColorBrush>()!.Color
                .ShouldBe(Color.Parse("#E8EAED"), "背景档位必须覆盖 BgWorkspace");

            // 跟随主题（默认）→ 撤销覆盖：资源回到内置主题字典的值
            ThemeApplier.Apply(new ApplicationSettings());
            Application.Current.TryGetResource("BgWorkspace", ThemeVariant.Light, out var restored).ShouldBeTrue();
            restored.ShouldBeAssignableTo<ISolidColorBrush>()!.Color
                .ShouldBe(Color.Parse("#F3F4F6"), "档位回到 Theme 后必须还原内置 token 值");
        });
    }

    [Fact]
    public void 人机操作三通道_应在模块窗口渲染并投递()
    {
        _ui.Run(() =>
        {
            using var harness = new VisualHarness();
            harness.PlaceInputsAt("in1", 0);

            var module = harness.Windows.Modules.ShouldHaveSingleItem();

            // ① 声明驱动的渲染：文本输入 / 命令按钮 / 开关各一个，且**没有**"未渲染"占位
            module.Elements.OfType<TextInputElementViewModel>().Count().ShouldBe(1);
            module.Elements.OfType<CommandElementViewModel>().Count().ShouldBe(1);
            module.Elements.OfType<ToggleElementViewModel>().Count().ShouldBe(1);
            module.Elements.ShouldNotContain(e => e is UnsupportedElementViewModel);

            // ② 真窗口里确实渲染出输入控件（不只是视图模型存在）
            var window = harness.OpenWindow(module);
            window.GetVisualDescendants().OfType<TextBox>().ShouldNotBeEmpty("文本输入必须渲染成可输入的控件");

            // ③ 命令按钮：点击 = 投递到模块线程（成功即无错误提示）
            var command = module.Elements.OfType<CommandElementViewModel>().Single();
            command.InvokeCommand.Execute(null);
            command.HasError.ShouldBeFalse($"命令投递应成功（实际：{command.LastResult}）");
            command.LastResult.ShouldNotBeNullOrEmpty("必须给出投递反馈（不静默）");

            // ④ 文本输入：提交后不再处于"未提交"状态；成功后清空错误
            var input = module.Elements.OfType<TextInputElementViewModel>().Single();
            input.Text = "2.5";
            input.IsDirty.ShouldBeTrue("编辑后应标记未提交（D-5：回车/失焦才写入）");
            input.CommitCommand.Execute(null);
            input.IsDirty.ShouldBeFalse("提交成功后不应再显示未提交");

            // ⑤ 未声明的输入源 → 就地报错（不静默）
            var bogus = new TextInputElementViewModel(
                new HYWDebugAssistant.Sdk.Views.ViewElement(HYWDebugAssistant.Sdk.Views.ViewElementKind.TextInput, "nope", "假输入"),
                (source, text) => harness.Services.SubmitInput("in1", source, text));
            bogus.Text = "1";
            bogus.CommitCommand.Execute(null);
            bogus.LastResult.ShouldNotBeNull().ShouldContain("未生效");
        });
    }

    [Fact]
    public void 端口标记_类型色条与指示灯应贴在标记两端()
    {
        // 【用户第十三轮反馈】标记内容重排：窄颜色条（数据包类型）· 网络名 · 放大箭头 · 加大且贴边的指示灯。
        // 本测试把**几何**钉住（尺寸 / 贴边 / 槽位顺序），避免"文本一变排版就抖"与"灯又缩回中间"。
        _ui.Run(() =>
        {
            using var harness = new VisualHarness();
            harness.Place("s1");

            var module = harness.Windows.Modules.ShouldHaveSingleItem();
            var window = harness.OpenWindow(module);

            var marker = FindMarker(window).ShouldNotBeNull();
            var markerBox = ScreenRect(marker, window);

            var strip = FindVisibleByClass<Border>(window, "typeStrip").ShouldNotBeNull("标记上必须有类型窄色条（颜色 = PacketTypePalette 按类型身份取值，ADR-0049 D6）");
            var lamp = FindVisibleByClass<StateIndicator>(window, "markerLamp").ShouldNotBeNull("标记上必须有连接指示灯");

            // ① 色条 = **窄条**：沿标记长边 6 px（token PortMarkerStripWidth）、跨满标记内宽 18 px
            //    （标记 20 宽 − 1 px 边框 ×2 = 18）
            var stripBox = ScreenRect(strip, window);
            stripBox.Height.ShouldBe(6, 1.0, "竖排（旋转后）色条沿标记长边必须是 6 px 窄条");
            stripBox.Width.ShouldBe(18, 1.0, "竖排色条应跨满标记内宽（20 − 边框 1×2）");

            // ② 指示灯 6×6 → **8×8**（半径 +1），且**离端部留 5px**（复验："过于靠边" ⇒ 由贴边改为留间距）
            var lampBox = ScreenRect(lamp, window);
            lampBox.Width.ShouldBe(8, 1.0, "指示灯 6×6 → 8×8（StateIndicator 半径 = 边长/2 − 1 ⇒ 2 → 3）");
            lampBox.Height.ShouldBe(8, 1.0);
            lamp.Margin.ShouldBe(new Thickness(0, 0, 5, 0), "指示灯离标记端部留 5px（内容坐标右边距）");

            // ③ 两端贴齐：色条贴标记一端、灯在另一端内侧 5px（各留 1 px 边框）
            stripBox.Top.ShouldBeGreaterThan(markerBox.Top, "色条与灯必须分居标记两端");
            lampBox.Top.ShouldBeLessThan(stripBox.Top, "槽位顺序 = 读数顺序：内容旋转 −90° ⇒ 色条在下端、指示灯在上端");
            lampBox.Top.ShouldBe(markerBox.Top + 6, 1.5, "指示灯距端部 = 1 px 边框 + 5 px 间距");
            stripBox.Bottom.ShouldBe(markerBox.Bottom - 1, 1.5, "色条必须贴到标记另一端部");

            // ④ 标记上不再有"数据包类型文本"与行内提示文本（类型只进悬停；提示进 `?` 帮助弹窗）
            var markerVm = marker.DataContext.ShouldBeOfType<PortMarkerViewModel>();
            var texts = window.GetVisualDescendants().OfType<TextBlock>()
                .Where(t => t.IsEffectivelyVisible)
                .Select(t => t.Text)
                .ToList();

            texts.ShouldNotContain(markerVm.TypeText, "类型文本已从标记上移除（标签名通常很长，塞不下；类型只在悬停里给出）");
            texts.ShouldNotContain(markerVm.Hint, "编辑提示文本必须收进 `?` 帮助弹窗（不再行内展开、不吃版面）");
            texts.ShouldContain(markerVm.NetworkText, "标记上的显示文本 = 网络名（无\"未连\"后缀）");
        });
    }

    [Fact]
    public void 端口标记_方向色应来自方向语义色且与主题色无关()
    {
        // 【ADR-0044 · 用户第十四轮 D1a/D2c】：标记**边框**与**箭头**都按方向着色，
        // 且颜色**只**由方向语义色驱动（改主题色不得影响它）。
        _ui.Run(() =>
        {
            using var harness = new VisualHarness();

            // 发送色 / 接收色设成两个**彼此不同、也与主题色不同**的值
            ThemeApplier.Apply(new ApplicationSettings
            {
                AccentColor = "#16A34A",          // 主题色故意选第三个颜色，验证"方向色不跟随主题色"
                DirectionSendColor = "#123456",
                DirectionReceiveColor = "#654321",
            });

            try
            {
                // ① 发送口（右边缘）
                harness.Place("s1");
                var sender = harness.Windows.Modules.ShouldHaveSingleItem();
                var sendWindow = harness.OpenWindow(sender);
                var sendMarker = FindMarker(sendWindow).ShouldNotBeNull();

                ColorOf(sendMarker.BorderBrush).ShouldBe(Color.Parse("#123456"),
                    "标记**边框**必须用发送方向色（D2c）");

                var sendArrow = FindVisibleArrow(sendWindow).ShouldNotBeNull();
                ColorOf(sendArrow.Foreground).ShouldBe(Color.Parse("#123456"),
                    "标记**箭头**必须用发送方向色");

                // 建网预览描边 = 同一个方向色（D3b）
                var sendVm = sendMarker.DataContext.ShouldBeOfType<PortMarkerViewModel>();
                sendVm.IsDockCandidate = true;
                VisualHarness.Settle();

                var highlight = FindVisibleByClass<Border>(sendWindow, "dockHighlight");
                highlight.ShouldNotBeNull("建网预览描边必须存在（即将自动建网时可见）");
                ColorOf(highlight!.BorderBrush).ShouldBe(Color.Parse("#123456"),
                    "建网预览描边用**同一方向色**（仅以线宽区分，D3b）");

                // ② 接收口（左边缘）
                harness.PlaceSinkAt("r1", 400);
                var sink = harness.Windows.Modules.Single(m => m.InstanceId == "r1");
                var receiveWindow = harness.OpenWindow(sink);
                var receiveMarker = FindMarker(receiveWindow).ShouldNotBeNull();

                ColorOf(receiveMarker.BorderBrush).ShouldBe(Color.Parse("#654321"),
                    "接收口标记边框必须用接收方向色");
                ColorOf(FindVisibleArrow(receiveWindow)!.Foreground).ShouldBe(Color.Parse("#654321"),
                    "接收口箭头必须用接收方向色");
            }
            finally
            {
                // 还原为"跟随主题默认值"（空串），并验证**深浅两套内置默认值**真的生效
                ThemeApplier.Apply(new ApplicationSettings());
            }

            using var restored = new VisualHarness();
            restored.Place("s2");
            var restoredWindow = restored.OpenWindow(restored.Windows.Modules.ShouldHaveSingleItem());
            var fallbackMarker = FindMarker(restoredWindow).ShouldNotBeNull();

            Application.Current!.TryGetResource("DirectionSend", ThemeVariant.Light, out var tokenBrush).ShouldBeTrue();
            ColorOf(tokenBrush as IBrush).ShouldBe(ColorOf(fallbackMarker.BorderBrush),
                "空设置 = 跟随主题默认 ⇒ 必须回到 Tokens.axaml 的内置方向色（否则深色默认值就成了死值）");
        });
    }

    // ---------------------------------------------------------------- G5 · C5-7 端口锚点（真指针）

    [Fact]
    public void 端口锚点_真指针拖动应改偏移并换边_且写进运行时()
    {
        _ui.Run(() =>
        {
            using var harness = new VisualHarness();
            harness.PlaceAnchorAt("a1", 0);

            var module = harness.Windows.Modules.ShouldHaveSingleItem();
            var window = harness.OpenWindow(module);
            VisualHarness.Settle();

            var marker = FindMarker(window).ShouldNotBeNull();
            var vm = marker.DataContext.ShouldBeOfType<PortMarkerViewModel>();

            vm.IsAnchorFixed.ShouldBeFalse("模块声明了 `AnchorDraggable = true` ⇒ 默认可拖");
            vm.Edge.ShouldBe(PortEdge.Right, "发送口默认在右边缘（数据流惯例）");
            vm.Offset.ShouldBe(0.5);

            // 真指针：从标记中心**按下 → 移到窗口上方**（最近边 = 上边）→ 松手
            var from = marker.TranslatePoint(new Point(marker.Bounds.Width / 2, marker.Bounds.Height / 2), window)
                .ShouldNotBeNull();
            var to = new Point(window.Bounds.Width / 2, 2);

            window.MouseDown(from, MouseButton.Left);
            window.MouseMove(to);
            VisualHarness.Settle();
            window.MouseUp(to, MouseButton.Left);
            VisualHarness.Settle();

            vm.Edge.ShouldBe(PortEdge.Top, "拖到上边附近 ⇒ 最近边 = 上边（换边后标记变横排）");
            vm.IsVertical.ShouldBeFalse("换到上下边 ⇒ 形态必须跟着变横排");

            // **屏幕上真的换过来了**（只断言视图模型会漏掉"排布没失效"这一类缺陷 —— 用户实测报过）
            var moved = ScreenRect(FindMarker(window).ShouldNotBeNull(), window);
            moved.Width.ShouldBe(114, 1e-6, "横排标记宽 114");
            moved.Height.ShouldBe(20, 1e-6, "横排标记高 20");
            moved.Y.ShouldBeLessThan(2, "拖到上边 ⇒ 标记必须真的贴到窗口上沿");

            // **真的写进了运行时**（不只是改了个视图模型）：查询面与组合件几何都要跟上
            var port = harness.Services.GetInstances().ShouldHaveSingleItem().Ports.ShouldHaveSingleItem();
            port.Edge.ShouldBe(PortEdge.Top, "框架查询面必须拿到新边（否则刷新一次就跳回去）");
            harness.Runtime.Content.Layout.GetPort("a1", port.Name).Edge.ShouldBe(
                PortEdge.Top,
                "组合件几何必须记下覆盖的边（否则重载后跳回默认边）");

            // 并且**不再是拖动中**（松手必须解锁节拍同步，否则后续刷新全被挡住）
            vm.IsDraggingAnchor.ShouldBeFalse();

            // ② **沿同一条边再拖一次**（只改偏移、不换边）：这是"屏幕上真的动了吗"的关键场景 ——
            //    换边会因宽高绑定变化而**看起来能动**，掩盖"排布没失效"这一类缺陷（用户实测报过"拖不动"）。
            var alongEdge = marker.TranslatePoint(new Point(marker.Bounds.Width / 2, marker.Bounds.Height / 2), window)
                .ShouldNotBeNull();
            var target = new Point(alongEdge.X + 60, alongEdge.Y);

            var hit = window.InputHitTest(alongEdge);
            IsInsideMarker(hit).ShouldBeTrue(
                $"同边拖动的按下点必须落在标记上（实际命中：{Describe(hit)}）");

            window.MouseDown(alongEdge, MouseButton.Left);
            window.MouseMove(target);
            VisualHarness.Settle();

            vm.IsDraggingAnchor.ShouldBeTrue("越过阈值后必须进入拖动（否则预览与提交都无从发生）");
            vm.Offset.ShouldBeGreaterThan(0.5, "移动中预览就该已经变大（松手只是把它提交）");

            window.MouseUp(target, MouseButton.Left);
            VisualHarness.Settle();

            vm.Edge.ShouldBe(PortEdge.Top, "仍是同一条边（本段就是要验同边拖动）");
            vm.Offset.ShouldBeGreaterThan(0.5, "往右拖 ⇒ 偏移必须增大");

            var slid = ScreenRect(FindMarker(window).ShouldNotBeNull(), window);
            slid.X.ShouldBeGreaterThan(
                moved.X + 10,
                "同边拖动后标记必须**真的在屏幕上换了位置** —— 只改视图模型不算（实测漏过这一类）");
        });
    }

    [Fact]
    public void 端口锚点_默认为固定的模块_拖动不生效且提示如何解锁()
    {
        _ui.Run(() =>
        {
            using var harness = new VisualHarness();
            harness.Place("t1");   // `VisualSenderModule` 没声明 `AnchorDraggable` ⇒ 默认固定

            var module = harness.Windows.Modules.ShouldHaveSingleItem();
            var window = harness.OpenWindow(module);
            VisualHarness.Settle();

            var vm = FindMarker(window).ShouldNotBeNull().DataContext.ShouldBeOfType<PortMarkerViewModel>();
            vm.IsAnchorFixed.ShouldBeTrue("未声明 `AnchorDraggable` ⇒ 默认固定（保守默认）");

            string? notice = null;
            harness.Windows.NoticeRaised += (_, e) => notice = e.Message;

            var from = FindMarker(window).ShouldNotBeNull()
                .TranslatePoint(new Point(10, 57), window).ShouldNotBeNull();
            var to = new Point(window.Bounds.Width / 2, 2);

            window.MouseDown(from, MouseButton.Left);
            window.MouseMove(to);
            VisualHarness.Settle();
            window.MouseUp(to, MouseButton.Left);
            VisualHarness.Settle();

            vm.Edge.ShouldBe(PortEdge.Right, "固定锚点**拖不动**：边不得改变");
            vm.Offset.ShouldBe(0.5, "固定锚点**拖不动**：偏移不得改变");

            notice.ShouldNotBeNull("拖不动必须**给出反馈**，否则用户以为程序卡了");
            notice.ShouldContain("固定");
        });
    }

    [Fact]
    public void 端口锚点_按住不移动的点击不得提交几何_把双击留给改网名()
    {
        _ui.Run(() =>
        {
            using var harness = new VisualHarness();
            harness.PlaceAnchorAt("a1", 0);

            var module = harness.Windows.Modules.ShouldHaveSingleItem();
            var window = harness.OpenWindow(module);
            VisualHarness.Settle();

            var marker = FindMarker(window).ShouldNotBeNull();
            var vm = marker.DataContext.ShouldBeOfType<PortMarkerViewModel>();
            var center = marker.TranslatePoint(new Point(marker.Bounds.Width / 2, marker.Bounds.Height / 2), window)
                .ShouldNotBeNull();

            // ① 纯点击（不移动）
            window.MouseDown(center, MouseButton.Left);
            window.MouseUp(center, MouseButton.Left);
            VisualHarness.Settle();

            vm.IsDraggingAnchor.ShouldBeFalse("纯点击不进入拖动");
            vm.Offset.ShouldBe(0.5, "纯点击**不得**改几何（否则双击改网名永远被拖动抢先）");

            // ② 阈值内的小抖动（3px < 4px 阈值）同样不进入拖动
            window.MouseDown(center, MouseButton.Left);
            window.MouseMove(center + new Point(0, 3));
            VisualHarness.Settle();
            window.MouseUp(center + new Point(0, 3), MouseButton.Left);
            VisualHarness.Settle();

            vm.IsDraggingAnchor.ShouldBeFalse("阈值内的抖动仍算点击");
            vm.Offset.ShouldBe(0.5, "阈值内的抖动**不得**改几何");
        });
    }

    [Fact]
    public void 端口锚点_全局固定与解锁_应一次切换全部模块的所有口()
    {
        _ui.Run(() =>
        {
            using var harness = new VisualHarness();
            harness.PlaceAnchorAt("a1", 0);
            harness.PlaceAnchorAt("a2", 400);

            var windows = harness.Windows.Modules.Select(m => harness.OpenWindow(m)).ToList();
            VisualHarness.Settle();

            var markers = windows
                .Select(w => FindMarker(w).ShouldNotBeNull().DataContext.ShouldBeOfType<PortMarkerViewModel>())
                .ToList();

            markers.Count.ShouldBe(2);
            markers.ShouldAllBe(m => !m.IsAnchorFixed);

            // C5-7 的"可通过快捷键/操作锁定与解锁"= **全局**一次切换
            harness.Windows.ToggleAllAnchorsFixed().ShouldContain("固定");
            markers.ShouldAllBe(m => m.IsAnchorFixed, "全局操作后**所有模块的所有口**都固定");
            harness.Runtime.Content.Layout.GetPort("a1", "out").IsAnchorFixed.ShouldBe(
                true,
                "固定态必须写进运行时（否则刷新一次就回到声明值）");

            harness.Windows.ToggleAllAnchorsFixed().ShouldContain("解锁");
            markers.ShouldAllBe(m => !m.IsAnchorFixed, "再按一次 ⇒ 全部解锁");
            harness.Runtime.Content.Layout.GetPort("a2", "out").IsAnchorFixed.ShouldBe(
                false,
                "解锁（显式 false）同样要落进运行时几何 —— 不能只改视图模型");
        });
    }

    [Fact]
    public void 端口锚点_拖动期间刷新节拍不得把预览拽回原位()
    {
        _ui.Run(() =>
        {
            using var harness = new VisualHarness();
            harness.PlaceAnchorAt("a1", 0);

            var module = harness.Windows.Modules.ShouldHaveSingleItem();
            var window = harness.OpenWindow(module);
            VisualHarness.Settle();

            var vm = FindMarker(window).ShouldNotBeNull().DataContext.ShouldBeOfType<PortMarkerViewModel>();

            // 模拟"拖动进行中"：本地已经改到上边 + 偏移 0.2（**尚未提交**）
            vm.IsDraggingAnchor = true;
            vm.Edge = PortEdge.Top;
            vm.Offset = 0.2;

            // 刷新节拍（框架权威值仍是"右边缘 0.5"）——**不得**覆盖拖动预览
            harness.Windows.Refresh();
            VisualHarness.Settle();

            vm.Edge.ShouldBe(PortEdge.Top, "拖动期间不得被拽回原位");
            vm.Offset.ShouldBe(0.2);

            // 松手（解锁同步）后，框架权威值接管
            vm.IsDraggingAnchor = false;
            harness.Windows.Refresh();
            VisualHarness.Settle();

            vm.Edge.ShouldBe(PortEdge.Right, "松手后由框架权威值接管（未提交 ⇒ 回到已提交的边）");
            vm.Offset.ShouldBe(0.5);
        });
    }

    [Fact]
    public void 端口锚点_真双击标记仍然打开网名编辑框()
    {
        _ui.Run(() =>
        {
            using var harness = new VisualHarness();
            harness.PlaceAnchorAt("a1", 0);

            var module = harness.Windows.Modules.ShouldHaveSingleItem();
            var window = harness.OpenWindow(module);
            VisualHarness.Settle();

            var marker = FindMarker(window).ShouldNotBeNull();
            var vm = marker.DataContext.ShouldBeOfType<PortMarkerViewModel>();
            var center = marker.TranslatePoint(new Point(marker.Bounds.Width / 2, marker.Bounds.Height / 2), window)
                .ShouldNotBeNull();

            // 真双击（两次短间隔点击）：**拖动阈值**就是为了不把它吃掉
            window.MouseDown(center, MouseButton.Left);
            window.MouseUp(center, MouseButton.Left);
            window.MouseDown(center, MouseButton.Left);
            window.MouseUp(center, MouseButton.Left);
            VisualHarness.Settle();

            vm.IsEditing.ShouldBeTrue("双击标记必须仍然打开网名编辑框（拖动不得抢走双击）");
            vm.Offset.ShouldBe(0.5, "双击不是拖动 ⇒ 几何不得变");
        });
    }

    [Fact]
    public void 端口锚点_改偏移的预览必须在屏幕上真的重排()
    {
        _ui.Run(() =>
        {
            using var harness = new VisualHarness();
            harness.PlaceAnchorAt("a1", 0);

            var module = harness.Windows.Modules.ShouldHaveSingleItem();
            var window = harness.OpenWindow(module);
            VisualHarness.Settle();

            var marker = FindMarker(window).ShouldNotBeNull();
            var vm = marker.DataContext.ShouldBeOfType<PortMarkerViewModel>();
            vm.Edge.ShouldBe(PortEdge.Right);

            var before = ScreenRect(marker, window);
            before.Y.ShouldBeGreaterThan(0, "初始偏移 0.5 ⇒ 标记不在端部");

            // ① **拖动进行中**的预览：只改偏移（同一条边内移动）。此时手势会把节拍同步冻住
            //    （`IsDraggingAnchor`，见 `SyncPorts`）——**不设它就会被刷新节拍按框架权威值拽回 0.5**，
            //    那正是"预览看不见"的另一种表现（本用例第一版就踩了这个）。
            //    【2026-09 缺陷】`PortMarkerPanel` 直接读 VM 的值，若没人让排布失效，屏幕上**纹丝不动**
            //    ⇒ 用户实测"解锁后指针变了但拖不动"。此前用例只断言视图模型 ⇒ 漏掉该缺陷。
            vm.IsDraggingAnchor = true;
            vm.Offset = 0.15;
            VisualHarness.Settle();

            var after = ScreenRect(marker, window);
            vm.Offset.ShouldBe(0.15, "拖动期间节拍同步必须被冻住（否则 Offset 会被拽回 0.5）");
            after.Y.ShouldBeLessThan(before.Y - 10, "偏移 0.5 → 0.15 ⇒ 标记必须真的往上移动（不只是视图模型改了）");

            vm.Offset = 0.85;
            VisualHarness.Settle();

            var previewed = ScreenRect(marker, window);
            previewed.Y.ShouldBeGreaterThan(after.Y + 10, "偏移调回下端 ⇒ 屏幕上必须跟着下来（反向也要成立）");

            vm.IsDraggingAnchor = false;
        });
    }

    // ---------------------------------------------------------------- 辅助

    /// <summary>取画刷颜色（非纯色/为空时返回 <see cref="Colors.Transparent"/>，便于断言直接失败）。</summary>
    private static Color ColorOf(IBrush? brush) =>
        brush is ISolidColorBrush solid ? solid.Color : Colors.Transparent;

    /// <summary>找**真正可见**的方向箭头（标记模板的竖排/横排两套分支只有一套可见）。</summary>
    private static TextBlock? FindVisibleArrow(Visual root) =>
        root.GetVisualDescendants()
            .OfType<TextBlock>()
            .FirstOrDefault(t => t.IsEffectivelyVisible && t.Text is "↓" or "→");

    private static Border? FindMarker(Visual root) =>
        root.GetVisualDescendants()
            .OfType<Border>()
            .FirstOrDefault(b => b.Classes.Contains("portMarker"));

    private static bool IsInsideMarker(IInputElement? hit)
    {
        var current = hit as Visual;
        while (current is not null)
        {
            if (current is Control { DataContext: PortMarkerViewModel })
            {
                return true;
            }

            current = current.GetVisualParent();
        }

        return false;
    }

    private static T? FindByName<T>(Visual root, string name)
        where T : Control =>
        root.GetVisualDescendants().OfType<T>().FirstOrDefault(c => c.Name == name);

    /// <summary>
    /// 按 `Classes` 找**真正可见**的控件（标记模板里竖排/横排两套分支同时存在，只有一套可见；
    /// 两套分支**同名会撞命名域**，故模板内元素用 Classes 标识）。
    /// </summary>
    private static T? FindVisibleByClass<T>(Visual root, string className)
        where T : Control =>
        root.GetVisualDescendants().OfType<T>()
            .FirstOrDefault(c => c.Classes.Contains(className) && c.IsEffectivelyVisible);

    /// <summary>控件在给定根坐标系里的**变换后**外框（竖排标记内容带 −90° 旋转变换，Bounds 是变换前的）。</summary>
    private static Rect ScreenRect(Visual visual, Visual root)
    {
        var corners = new[]
        {
            new Point(0, 0),
            new Point(visual.Bounds.Width, 0),
            new Point(0, visual.Bounds.Height),
            new Point(visual.Bounds.Width, visual.Bounds.Height),
        }
        .Select(p => visual.TranslatePoint(p, root) ?? default)
        .ToList();

        var left = corners.Min(p => p.X);
        var top = corners.Min(p => p.Y);
        return new Rect(left, top, corners.Max(p => p.X) - left, corners.Max(p => p.Y) - top);
    }

    /// <summary>命中元素的诊断描述（断言失败时能一眼看出"被谁挡住了"）。</summary>
    private static string Describe(IInputElement? hit) =>
        hit is Control control
            ? $"{control.GetType().Name}(Name={control.Name ?? "-"}, DataContext={control.DataContext?.GetType().Name ?? "-"})"
            : hit?.GetType().Name ?? "null";

    /// <summary>视觉测试现场：真实运行时 + 真实服务面 + 真实窗口。</summary>
    private sealed class VisualHarness : IDisposable
    {
        public VisualHarness()
        {
            Runtime = new ModuleRuntime();
            Services = new RuntimeServices(Runtime);
            Register("visual-sender", typeof(VisualSenderModule));
            Register("visual-sink", typeof(VisualSinkModule));
            Register("visual-inputs", typeof(VisualInputsModule));
            Register("visual-tabs", typeof(VisualTabsModule));
            Register("visual-multiline", typeof(VisualMultilineModule));
            Register("visual-feedback", typeof(VisualFeedbackModule));
            Register("visual-file-picker", typeof(VisualFilePickerModule));
            Register("visual-anchor", typeof(VisualAnchorModule));
            Windows = new ModuleWindowsViewModel(Services, Services);
        }

        public ModuleRuntime Runtime { get; }

        public RuntimeServices Services { get; }

        public ModuleWindowsViewModel Windows { get; }

        /// <summary>加入一个发送模块并摆放（端口在右边缘）。</summary>
        public void Place(string instanceId) => PlaceAt(instanceId, 0);

        /// <summary>加入一个发送模块并**显式摆放**到给定屏幕位置（叠加层方位判定需要确定几何）。</summary>
        public void PlaceAt(string instanceId, double x, double y = 0) =>
            PlaceModuleAt(instanceId, "test/visual-sender/1.0.0", x, y);

        /// <summary>加入一个接收模块并显式摆放（建网测试用）。</summary>
        public void PlaceSinkAt(string instanceId, double x, double y = 0) =>
            PlaceModuleAt(instanceId, "test/visual-sink/1.0.0", x, y);

        /// <summary>加入一个"人机操作三通道"模块并摆放（命令/输入/开关渲染与投递测试用）。</summary>
        public void PlaceInputsAt(string instanceId, double x, double y = 0) =>
            PlaceModuleAt(instanceId, "test/visual-inputs/1.0.0", x, y);

        /// <summary>加入一个"多行标签页"模块并摆放（G1：标签条渲染与切页测试用）。</summary>
        public void PlaceTabsAt(string instanceId, double x, double y = 0) =>
            PlaceModuleAt(instanceId, "test/visual-tabs/1.0.0", x, y);

        /// <summary>加入一个"多行 + 单行输入"模块并摆放（G2：多行提交键测试用）。</summary>
        public void PlaceMultilineAt(string instanceId, double x, double y = 0) =>
            PlaceModuleAt(instanceId, "test/visual-multiline/1.0.0", x, y);

        /// <summary>加入一个"会回报输入处理结果"的模块并摆放（G3：失败态测试用）。</summary>
        public void PlaceFeedbackAt(string instanceId, double x, double y = 0) =>
            PlaceModuleAt(instanceId, "test/visual-feedback/1.0.0", x, y);

        /// <summary>加入一个"声明文件选择器"的模块并摆放（G4：B6 渲染与提交测试用）。</summary>
        public void PlaceFilePickerAt(string instanceId, double x, double y = 0) =>
            PlaceModuleAt(instanceId, "test/visual-file-picker/1.0.0", x, y);

        /// <summary>加入一个"锚点可拖动"的模块并摆放（G5：锚点拖动/固定测试用）。</summary>
        public void PlaceAnchorAt(string instanceId, double x, double y = 0) =>
            PlaceModuleAt(instanceId, "test/visual-anchor/1.0.0", x, y);

        private void PlaceModuleAt(string instanceId, string moduleIdLiteral, double x, double y)
        {
            Services.AddModule(instanceId, moduleIdLiteral).Succeeded.ShouldBeTrue();
            Windows.Refresh();

            Services.UpdateModuleLayout(new ModuleLayoutView(
                instanceId,
                x,
                y,
                ModuleViewViewModel.DefaultWidth,
                ModuleViewViewModel.DefaultHeight)).Succeeded.ShouldBeTrue();

            Windows.Refresh();
        }

        /// <summary>按窗口管理器的方式开窗（**赋值顺序与生产代码一致**，故也能覆盖接线类缺陷）。</summary>
        public ModuleWindow OpenWindow(ModuleViewViewModel module)
        {
            var window = new ModuleWindow
            {
                DataContext = module,
                Controller = Windows,
            };

            window.Show();
            Settle();
            return window;
        }

        /// <summary>
        /// 把无头环境推进到"布局完成"：调度队列 + **渲染帧**。
        /// </summary>
        /// <remarks>
        /// 只跑 <c>RunJobs</c> 不够：布局/渲染由渲染帧驱动，无头环境下渲染定时器不会自己走，
        /// 于是"单独跑通过、多个测试一起跑却命不中"（实测踩过：命中为 null）。
        /// </remarks>
        public static void Settle()
        {
            Dispatcher.UIThread.RunJobs();
            AvaloniaHeadlessPlatform.ForceRenderTimerTick();
            Dispatcher.UIThread.RunJobs();
        }

        public void Dispose() => Runtime.Dispose();

        private void Register(string name, Type entryType)
        {
            var definition = new ModuleDefinition(
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

    /// <summary>视觉测试用发送模块（一个发送口，右边缘；**声明一个纯源角色**——叠加层的线以角色为端）。</summary>
    [ModuleDefinition("test", "visual-sender", "1.0.0")]
    public sealed class VisualSenderModule : IModule
    {
        /// <inheritdoc />
        public void OnRegistered(IModuleContext context)
        {
            context.Declaration.DeclareDataType(new DataTypeDeclaration(VisualTypeId, 1, VisualShape));
            context.Declaration.DeclarePort(new PortDeclaration(
                "out",
                PortDirection.Send,
                VisualShape)
            {
                Type = new DataTypeRef(VisualTypeId, 1),
            });

            context.Declaration.DeclareRole(RoleDeclaration.Output(
                "visual-send", new DataTypeRef(VisualTypeId, 1), "out", "发送"));
        }
    }

    /// <summary>视觉测试用接收模块（一个接收口，左边缘；**声明一个纯汇角色**）。</summary>
    [ModuleDefinition("test", "visual-sink", "1.0.0")]
    public sealed class VisualSinkModule : IModule
    {
        /// <inheritdoc />
        public void OnRegistered(IModuleContext context)
        {
            context.Declaration.DeclareDataType(new DataTypeDeclaration(VisualTypeId, 1, VisualShape));
            context.Declaration.DeclarePort(new PortDeclaration(
                "in",
                PortDirection.Receive,
                VisualShape)
            {
                Type = new DataTypeRef(VisualTypeId, 1),
            });

            context.Ports.SetReceiveHandler("in", _ => { });

            context.Declaration.DeclareRole(RoleDeclaration.Input(
                "visual-receive", new DataTypeRef(VisualTypeId, 1), "in", "接收"));
        }
    }

    /// <summary>视觉测试用"人机操作三通道"模块（文本输入 + 命令 + 开关）——ADR-0040。</summary>
    [ModuleDefinition("test", "visual-inputs", "1.0.0")]
    public sealed class VisualInputsModule : IModule
    {
        /// <inheritdoc />
        public void OnRegistered(IModuleContext context) =>
            context.Declaration.DeclareView(new ViewDeclaration(
                [
                    new ViewElement(ViewElementKind.TextInput, "input", "值"),
                    new ViewElement(ViewElementKind.CommandButton, "fire"),
                    new ViewElement(ViewElementKind.ToggleSlider, "auto", "自动"),
                ],
                commands: [new ViewCommand("fire", "触发")]));
    }

    /// <summary>视觉测试用"多行 + 单行输入"模块（**G2**：多行提交键与多行框渲染）。</summary>
    [ModuleDefinition("test", "visual-multiline", "1.0.0")]
    public sealed class VisualMultilineModule : IModule
    {
        /// <inheritdoc />
        public void OnRegistered(IModuleContext context) =>
            context.Declaration.DeclareView(new ViewDeclaration(
                [
                    new ViewElement(ViewElementKind.TextInput, "text", "多行") { Multiline = true },
                    new ViewElement(ViewElementKind.TextInput, "single", "单行"),
                ],
                dataSources: [new ViewDataSource("text"), new ViewDataSource("single")]));
    }

    /// <summary>
    /// 视觉测试用"会回报输入处理结果"的模块（**G3**）：收到输入后，文本不是 <c>ok</c> 就回报**失败**。
    /// </summary>
    /// <remarks>
    /// 它同时是"模块如何用 G3 通道"的**可执行示例**：实现 <see cref="IActivitySource"/> 在活动体里消费收件箱，
    /// 然后调 <c>context.InputFeedback.Report(source, false, 原因)</c> —— 模块**不碰 UI**。
    /// </remarks>
    [ModuleDefinition("test", "visual-feedback", "1.0.0")]
    public sealed class VisualFeedbackModule : IModule, IActivitySource
    {
        /// <summary>输入元素的源名（与视图声明的 `DataSource` 一致）。</summary>
        public const string TextSource = "text";

        /// <inheritdoc />
        public ActivityKind Kind => ActivityKind.Periodic;

        /// <inheritdoc />
        public TimeSpan Interval => TimeSpan.FromMilliseconds(10);

        /// <inheritdoc />
        public void OnRegistered(IModuleContext context) =>
            context.Declaration.DeclareView(new ViewDeclaration(
                [new ViewElement(ViewElementKind.TextInput, TextSource, "输入")],
                dataSources: [new ViewDataSource(TextSource)]));

        /// <inheritdoc />
        public void RunActivity(IModuleContext context)
        {
            ArgumentNullException.ThrowIfNull(context);

            while (!context.StopSignal.IsStopRequested)
            {
                while (context.Inbox.TryDequeueInput(out var source, out var text))
                {
                    var ok = string.Equals(text, "ok", StringComparison.Ordinal);

                    // 【G3】回报处理结果 —— 框架据此把该输入元素渲染成失败态（红框）
                    context.InputFeedback.Report(
                        source,
                        ok,
                        ok ? null : $"非法输入：{text}");
                }

                _ = context.StopSignal.Wait(Interval);
            }
        }
    }

    /// <summary>
    /// 视觉测试用"声明文件选择器"的模块（**G4**）：一个"保存到"的文件选择器 + 一个"打开"的。
    /// </summary>
    /// <remarks>
    /// 它同时是"模块如何用 B6"的**可执行示例**：模块只声明元素 + 在自己的活动体里 `TryDequeueInput` 收**路径字符串**，
    /// **完全不接触文件对话框 API**（弹窗与 `StorageProvider` 都属 UI）。
    /// </remarks>
    [ModuleDefinition("test", "visual-file-picker", "1.0.0")]
    public sealed class VisualFilePickerModule : IModule, IActivitySource
    {
        /// <summary>"保存到"选择器的数据源名。</summary>
        public const string SaveSource = "savePath";

        /// <summary>"打开"选择器的数据源名。</summary>
        public const string OpenSource = "openPath";

        /// <inheritdoc />
        public ActivityKind Kind => ActivityKind.Periodic;

        /// <inheritdoc />
        public TimeSpan Interval => TimeSpan.FromMilliseconds(10);

        /// <inheritdoc />
        public void OnRegistered(IModuleContext context) =>
            context.Declaration.DeclareView(new ViewDeclaration(
                [
                    new ViewElement(ViewElementKind.FileSelector, SaveSource, "保存到")
                    {
                        FileMode = FilePickerMode.Save,
                        FileFilter = "*.txt",
                    },
                    new ViewElement(ViewElementKind.FileSelector, OpenSource, "打开")
                    {
                        FileMode = FilePickerMode.Open,
                        FileFilter = "文本文件 (*.txt)|*.txt",
                    },
                ],
                dataSources: [new ViewDataSource(SaveSource), new ViewDataSource(OpenSource)]));

        /// <inheritdoc />
        public void RunActivity(IModuleContext context)
        {
            ArgumentNullException.ThrowIfNull(context);

            while (!context.StopSignal.IsStopRequested)
            {
                // 收路径（模块侧唯一要做的事）
                while (context.Inbox.TryDequeueInput(out _, out _))
                {
                }

                _ = context.StopSignal.Wait(Interval);
            }
        }
    }

    /// <summary>视觉测试用"多页标签页"模块（**G1**：3 页，每页一个文本输入）。</summary>
    /// <remarks>
    /// 声明结构 = `TabPages > [页节点 …]`，**每个页节点的子元素才是该页内容**（`ViewElement.children` 的语义）。
    /// 页节点本身是内容元素的写法也被容忍（见 `ModuleViewViewModel.Expand` 的兜底）。
    /// </remarks>
    [ModuleDefinition("test", "visual-tabs", "1.0.0")]
    public sealed class VisualTabsModule : IModule
    {
        /// <inheritdoc />
        public void OnRegistered(IModuleContext context) =>
            context.Declaration.DeclareView(new ViewDeclaration(
                [
                    new ViewElement(
                        ViewElementKind.TabPages,
                        title: "消息",
                        children:
                        [
                            new ViewElement(
                                ViewElementKind.Group,
                                title: "第 1 页",
                                children: [new ViewElement(ViewElementKind.TextInput, "page0", "输入")]),
                            new ViewElement(
                                ViewElementKind.Group,
                                title: "第 2 页",
                                children: [new ViewElement(ViewElementKind.TextInput, "page1", "输入")]),
                            new ViewElement(
                                ViewElementKind.Group,
                                title: "第 3 页",
                                children: [new ViewElement(ViewElementKind.TextInput, "page2", "输入")]),
                        ]),
                ],
                dataSources:
                [
                    new ViewDataSource("page0"),
                    new ViewDataSource("page1"),
                    new ViewDataSource("page2"),
                ]));
    }

    /// <summary>
    /// 视觉测试用"**锚点可拖动**"模块（**G5 · C5-7**）：一个发送口，声明 <c>AnchorDraggable = true</c>。
    /// </summary>
    /// <remarks>
    /// **为什么单独一个模块**：`AnchorDraggable` 默认 <see langword="false"/>（= 固定）⇒ 用别的夹具口
    /// 根本拖不动，会把"拖动没生效"误判成"拖动没实现"。这里显式声明可拖，与"默认固定"形成对照。
    /// </remarks>
    [ModuleDefinition("test", "visual-anchor", "1.0.0")]
    public sealed class VisualAnchorModule : IModule
    {
        /// <inheritdoc />
        public void OnRegistered(IModuleContext context)
        {
            context.Declaration.DeclareDataType(new DataTypeDeclaration(VisualTypeId, 1, VisualShape));
            context.Declaration.DeclarePort(new PortDeclaration(
                "out",
                PortDirection.Send,
                VisualShape)
            {
                Type = new DataTypeRef(VisualTypeId, 1),
                AnchorDraggable = true,
            });

            context.Declaration.DeclareRole(RoleDeclaration.Output(
                "visual-anchor-send", new DataTypeRef(VisualTypeId, 1), "out", "发送"));
        }
    }
}

/// <summary>
/// 无头会话夹具（M1-16 · ADR-0039 · D-9）：**一个会话跑完一个测试类**，用真实 `App` 作宿主。
/// </summary>
public sealed class HeadlessUiFixture : IDisposable
{
    private readonly HeadlessUnitTestSession _session =
        HeadlessUnitTestSession.StartNew(typeof(HYWDebugAssistant.App.App));

    /// <summary>在 UI 线程上执行测试体（Avalonia 控件只能在 UI 线程上操作）。</summary>
    /// <param name="action">测试体。</param>
    public void Run(Action action)
    {
        ArgumentNullException.ThrowIfNull(action);
        _session.Dispatch(action, CancellationToken.None).GetAwaiter().GetResult();
    }

    /// <inheritdoc />
    public void Dispose() => _session.Dispose();
}
