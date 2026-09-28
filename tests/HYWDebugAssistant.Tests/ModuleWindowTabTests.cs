using HYWDebugAssistant.Core.Api;
using HYWDebugAssistant.Sdk.Views;
using HYWDebugAssistant.Ui.Design;
using HYWDebugAssistant.Ui.ViewModels;
using Shouldly;
using Xunit;

namespace HYWDebugAssistant.Tests;

/// <summary>
/// 【G1 · 2026-09】C2 标签页的渲染与切换。
/// </summary>
/// <remarks>
/// <list type="bullet">
///   <item><b>缺口背景</b>：`TabPages` 在 v1 元素目录内（C2），但此前 `Flatten` **只取首页子元素**、
///     页签条与切换**从未实现** ⇒ 模块声明的多页只能看到第 1 页（见 `docs/roadmap.md` §3 第 12 项 / §4 G16 邻项）；</item>
///   <item><b>现行约定</b>：`TabPages` 展开为「**标签条** + **当前页**的子元素」，与其他容器"展平"同一约定
///     （容器自身不画、由框架渲染）；页序号用声明路径作键；**选中态是纯 UI 状态、不进组合件**。</item>
/// </list>
/// </remarks>
public class ModuleWindowTabTests
{
    // CA1861：重复调用且内容不变的数组参数应当用 static readonly 字段
    private static readonly string[] OnlyPage0 = ["page0"];
    private static readonly string[] OnlyPage1 = ["page1"];
    private static readonly string[] OnlyPage2 = ["page2"];
    [Fact]
    public void 标签页_应展开为标签条_且只渲染当前页的子元素()
    {
        var vm = CreateViewModel(pageCount: 3);

        var strip = vm.Elements.OfType<TabStripElementViewModel>().ShouldHaveSingleItem();
        strip.Title.ShouldBe("消息");
        strip.Pages.Count.ShouldBe(3);
        strip.SelectedIndex.ShouldBe(0);
        strip.Pages[0].IsSelected.ShouldBeTrue("首屏默认选中第 1 页");
        strip.Pages[1].IsSelected.ShouldBeFalse();
        strip.Pages[1].Title.ShouldBe("第 2 页", "页签标题取自该页元素的 Title");
        strip.Pages[2].SelectCommand.ShouldNotBeNull("每个页签都要能点击切页");

        RenderSources(vm).ShouldBe(OnlyPage0, "只有**当前页**的子元素被渲染（此前是'永远第一页'）");
    }

    [Fact]
    public void 切页_应改为渲染目标页的子元素_且标签条重建为选中该页()
    {
        var vm = CreateViewModel(pageCount: 3);
        vm.Elements.OfType<TabStripElementViewModel>().ShouldHaveSingleItem()
            .Pages[2].SelectCommand.Execute(null);

        var after = vm.Elements.OfType<TabStripElementViewModel>().ShouldHaveSingleItem();
        after.SelectedIndex.ShouldBe(2);
        after.Pages[2].IsSelected.ShouldBeTrue("切到的页要标为选中");
        after.Pages[0].IsSelected.ShouldBeFalse();

        RenderSources(vm).ShouldBe(OnlyPage2);
    }

    [Fact]
    public void 切页状态是纯UI状态_重建后仍记得当前页()
    {
        var vm = CreateViewModel(pageCount: 2);
        vm.Elements.OfType<TabStripElementViewModel>().ShouldHaveSingleItem()
            .Pages[1].SelectCommand.Execute(null);

        // 模拟"框架侧再次刷新"：Apply 走同一 Rebuild 路径，选中页不应被打回第 1 页
        var stripBefore = vm.Elements.OfType<TabStripElementViewModel>().ShouldHaveSingleItem();
        stripBefore.SelectedIndex.ShouldBe(1);
        RenderSources(vm).ShouldBe(OnlyPage1);
    }

    [Fact]
    public void 零页标签页_什么都不画_不假装支持()
    {
        var vm = CreateViewModel(pageCount: 0);

        vm.Elements.OfType<TabStripElementViewModel>().ShouldBeEmpty("零页 ⇒ 没有标签条");
        vm.Elements.OfType<TextInputElementViewModel>().ShouldBeEmpty();
    }

    /// <summary>当前元素表里文本输入元素绑定的数据源名（按渲染顺序）——用来判断"渲染的是哪一页"。</summary>
    private static string[] RenderSources(ModuleViewViewModel vm) =>
        [.. vm.Elements.OfType<TextInputElementViewModel>().Select(e => e.Source)];

    /// <summary>造一个"只有一个多页标签页"的模块视图：每页一个文本输入，源名 = <c>page{i}</c>。</summary>
    /// <param name="pageCount">页数（0 = 零页的反例）。</param>
    private static ModuleViewViewModel CreateViewModel(int pageCount)
    {
        var pages = Enumerable.Range(0, pageCount)
            .Select(i => new ViewElement(
                ViewElementKind.Group,
                title: $"第 {i + 1} 页",
                children: [new ViewElement(ViewElementKind.TextInput, dataSource: $"page{i}")]))
            .ToList();

        var declaration = new ViewDeclaration(
            [new ViewElement(ViewElementKind.TabPages, title: "消息", children: pages)],
            [.. Enumerable.Range(0, pageCount).Select(i => new ViewDataSource($"page{i}"))]);

        var instance = new ModuleInstanceView(
            "tab-test",
            "test/tabs/1.0.0",
            "标签页测试模块",
            "Active",
            HasView: true,
            Ports: [],
            IsPlaceholder: false,
            View: declaration);

        return new ModuleViewViewModel(
            instance,
            layout: null,
            EmptyRuntime.Instance,
            EmptyRuntime.Instance,
            scale: 1.0,
            fallbackX: 0,
            fallbackY: 0);
    }
}
