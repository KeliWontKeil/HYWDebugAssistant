using HYWDebugAssistant.Ui.Controls;
using Shouldly;
using Xunit;

namespace HYWDebugAssistant.Tests;

/// <summary>
/// **对齐参考线的绘制判据**（用户第十二轮第五·六轮反馈："底部对齐没有参考线" → "底部和右边还是没有"）。
/// </summary>
/// <remarks>
/// 根因（第二轮才定位准）：<c>GuideOverlay</c> 的父级 <c>Frame</c> 是 <c>BorderThickness=1</c> 的边框 ⇒
/// **叠加层被四周内缩 1 DIP**，而坐标按"客户区原点"算：
/// 上/左对齐的线落在 <c>0</c>（在范围内 ⇒ 可见，比真实边低 1px 看不出）；
/// **下/右**对齐的线落在 <c>范围 + 2</c> ⇒ 被 <c>position &lt;= extent</c> 的可见性判断**直接排除**（根本不画）。
/// <para>
/// 处置：① 可见性判据容忍"贴边超出"（<see cref="GuideOverlay.IsDrawable"/>，容差 = 边框内缩量级）；
/// ② 贴边时把线收进半个线宽（<see cref="GuideOverlay.InsetForDrawing"/>），否则一半线宽被裁剪、虚线不可见。
/// </para>
/// </remarks>
public class GuideOverlayTests
{
    [Fact]
    public void 贴边的参考线必须判定为可绘制()
    {
        const double height = 220;
        const double width = 320;

        // 底部/右边：线落在"范围 + 2"（叠加层被 1px 边框内缩）—— 曾经被否掉，正是用户报的现象
        GuideOverlay.IsDrawable(height + 2, height).ShouldBeTrue(
            "底部对齐的线在叠加层坐标里是「范围 + 2」，必须判定为可绘制，否则整条线不画");
        GuideOverlay.IsDrawable(width + 2, width).ShouldBeTrue("右边对齐同理");

        // 上/左：线落在 0 或 −1（叠加层顶边比客户区顶边低 1px）
        GuideOverlay.IsDrawable(0, height).ShouldBeTrue();
        GuideOverlay.IsDrawable(-1, height).ShouldBeTrue("贴边允许超出一点点（边框内缩量级）");
    }

    [Fact]
    public void 离得远的参考线不应画在本窗口()
    {
        const double height = 220;

        GuideOverlay.IsDrawable(height + 50, height).ShouldBeFalse(
            "离得远的线不属于本窗口（由对方窗口自己画），不得被夹到边缘变成假线");
        GuideOverlay.IsDrawable(-50, height).ShouldBeFalse();
    }

    [Fact]
    public void 贴边的参考线应被收进可绘制范围()
    {
        const double height = 220;

        GuideOverlay.InsetForDrawing(height, height).ShouldBeLessThan(height, "贴边必须收进半个线宽，否则一半被裁剪");
        GuideOverlay.InsetForDrawing(height + 2, height).ShouldBeLessThanOrEqualTo(height);
        GuideOverlay.InsetForDrawing(0, height).ShouldBeGreaterThan(0);
        GuideOverlay.InsetForDrawing(-1, height).ShouldBeGreaterThanOrEqualTo(0);
    }

    [Fact]
    public void 中间的参考线不应被移动()
    {
        GuideOverlay.InsetForDrawing(110, 220).ShouldBe(110, "线在窗口中间时位置不变");
        GuideOverlay.InsetForDrawing(0.6, 220).ShouldBe(0.6);
        GuideOverlay.InsetForDrawing(219.4, 220).ShouldBe(219.4);
    }

    [Fact]
    public void 收进量不超过半个线宽()
    {
        const double height = 220;

        (height - GuideOverlay.InsetForDrawing(height, height)).ShouldBeLessThanOrEqualTo(0.5);
        GuideOverlay.InsetForDrawing(0, height).ShouldBeLessThanOrEqualTo(0.5);
    }

    [Fact]
    public void 可用范围过小时不应把位置挪出范围()
    {
        // 极窄窗口（宽/高 ≈ 线宽）：没有"收进"的余地，保持原位置（宁可半像素也不挪位）
        GuideOverlay.InsetForDrawing(0.5, 0.8).ShouldBe(0.5);
    }
}
