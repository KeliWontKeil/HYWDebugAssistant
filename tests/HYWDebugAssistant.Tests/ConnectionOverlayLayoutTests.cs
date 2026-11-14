using HYWDebugAssistant.Ui.Controls;
using HYWDebugAssistant.Ui.ViewModels;
using Shouldly;
using Xunit;

namespace HYWDebugAssistant.Tests;

/// <summary>叠加层角色方块的纯几何关系（不断言绝对像素坐标）。</summary>
public class ConnectionOverlayLayoutTests
{
    [Fact]
    public void 角色信息块必须与相邻方块保持净空()
    {
        var roles = new[]
        {
            CreateRole("parse", "解析", "类型：样本"),
            CreateRole("output", "输出", "类型：样本"),
        };

        var layout = ConnectionOverlay.LayoutRoles(roles, width: 400, height: 600);
        layout.Boxes.Count.ShouldBe(2);

        var first = layout.Boxes[0];
        var second = layout.Boxes[1];
        var infoHeight = ConnectionOverlay.RoleInfoHeight(roles[1]);

        // 宽高都够 ⇒ 竖向优先；第二个方块的信息不能压到第一个方块。
        first.Box.Left.ShouldBe(second.Box.Left);
        second.Box.Top.ShouldBeGreaterThan(first.Box.Bottom);

        var gap = second.Box.Top - first.Box.Bottom;
        gap.ShouldBeGreaterThanOrEqualTo(infoHeight + ConnectionOverlay.RoleInfoGap);

        var infoTop = second.Box.Top - ConnectionOverlay.RoleInfoGap - infoHeight;
        infoTop.ShouldBeGreaterThanOrEqualTo(first.Box.Bottom);
    }

    [Fact]
    public void 高度不足宽度足够时应改为横向排布()
    {
        var roles = new[]
        {
            CreateRole("parse", "解析", "类型：样本"),
            CreateRole("output", "输出", "类型：样本"),
        };

        // 高度只够一行，宽度足够并排。
        var layout = ConnectionOverlay.LayoutRoles(roles, width: 600, height: 140);

        layout.Hidden.ShouldBe(0);
        layout.Boxes[0].Box.Top.ShouldBe(layout.Boxes[1].Box.Top);
        layout.Boxes[0].Box.Left.ShouldBeLessThan(layout.Boxes[1].Box.Left);
    }

    [Fact]
    public void 竖向横向都放不下时应使用网格且不隐藏()
    {
        var roles = new[]
        {
            CreateRole("a", "解析", "类型：样本"),
            CreateRole("b", "输出", "类型：样本"),
            CreateRole("c", "缓存", "类型：样本"),
            CreateRole("d", "转发", "类型：样本"),
        };

        // 单列过高、单行过宽；2×2 网格能放下全部角色。
        var layout = ConnectionOverlay.LayoutRoles(roles, width: 300, height: 250);

        layout.Hidden.ShouldBe(0);
        layout.Boxes.Select(box => box.Box.Left).Distinct().Count().ShouldBe(2);
        layout.Boxes.Select(box => box.Box.Top).Distinct().Count().ShouldBe(2);
    }

    [Fact]
    public void 所有排布都放不下时才使用_K_隐藏()
    {
        var roles = new[]
        {
            CreateRole("a", "解析", "类型：样本"),
            CreateRole("b", "输出", "类型：样本"),
        };

        var layout = ConnectionOverlay.LayoutRoles(roles, width: 100, height: 100);

        layout.Hidden.ShouldBeGreaterThan(0);
        layout.Boxes.Count.ShouldBeLessThan(roles.Length);
    }

    private static RoleBoxViewModel CreateRole(string key, string title, string meta)
    {
        var role = new RoleBoxViewModel(
            key,
            key,
            title,
            "RawBytes",
            "RawBytes",
            ["in"],
            ["out"],
            inboundRoleCount: 1,
            outboundRoleCount: 1,
            semanticText: meta);

        role.RateText = "1.0 KB/s";
        role.MetaText = meta;
        return role;
    }
}
