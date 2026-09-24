using System.Globalization;
using HYWDebugAssistant.Ui.Geometry;
using Shouldly;
using Xunit;

namespace HYWDebugAssistant.Tests;

/// <summary>
/// **吸附稳定性回归（按几何批量扫）** —— 用户第十二轮反馈："靠得近就抖、左右反向对齐也抖"。
/// </summary>
/// <remarks>
/// <para>
/// 单点用例挡不住"某个尺寸组合才抖"，所以按**矩阵**扫：尺寸取 125% 缩放下的常见值，
/// 宽度差 −8…+8px（左右两条边反向对齐最容易撞在一起的场合），逐 1px 推进 + ±1px 抖动。
/// </para>
/// <para>
/// <b>当前断言 = 诚实的"上界"</b>：引擎是纯函数（只按当前原始位置选候选），
/// 因此 1px 抖动在候选交接处引起的位移**上界 = 2×该候选的容差**。
/// ADR-0042（参照线统一模型）+ A+ 参数（吸附距离进设置项、默认 4px）会把上界收紧到
/// `2×吸附距离`（默认 8px）、并消除同坐标候选的重复登记 —— 届时这些上界断言要跟着收紧。
/// </para>
/// </remarks>
public class SnapStabilityTests
{
    private static string F(double v) => v.ToString("0.##", CultureInfo.InvariantCulture);

    /// <summary>抖动引起的位置跳变不得超过上界（上界 = 2×容差）。</summary>
    [Fact]
    public void 吸附_抖动引起的跳变不得超过两倍容差()
    {
        var limit = 2 * SnapEngine.FlushTolerance;
        var worst = 0.0;
        var worstCase = string.Empty;

        foreach (var otherWidth in new double[] { 400, 413, 450 })
        {
            foreach (var widthDelta in new[] { -8.0, -4, -2, -1, 1, 2, 4, 8 })
            {
                var movingWidth = otherWidth + widthDelta;
                var other = new CanvasRect(1000, 400, otherWidth, 275);
                var moving = new CanvasRect(0, 400, movingWidth, 275);

                for (var rawX = 930.0; rawX <= 1010; rawX += 1)
                {
                    var first = SnapEngine.Snap(moving.At(rawX, moving.Y), [.. SnapEngine.LinesOf(other)]).X;
                    var jittered = SnapEngine.Snap(moving.At(rawX + 1, moving.Y), [.. SnapEngine.LinesOf(other)]).X;
                    var back = SnapEngine.Snap(moving.At(rawX, moving.Y), [.. SnapEngine.LinesOf(other)]).X;

                    var jump = Math.Max(Math.Abs(jittered - first), Math.Abs(back - first));
                    if (jump > worst)
                    {
                        worst = jump;
                        worstCase = $"otherW={F(otherWidth)} ΔW={F(widthDelta)} rawX={F(rawX)} → "
                            + $"{F(first)} → {F(jittered)} → {F(back)}";
                    }
                }
            }
        }

        worst.ShouldBeLessThanOrEqualTo(
            limit,
            $"1px 抖动引起的位移不得越界（上界 2×贴合容差 = {limit}px）；实测最大 {F(worst)}px：{worstCase}");
    }

    /// <summary>单调推进时的"回跳"不得超过上界。</summary>
    [Fact]
    public void 吸附_单调推进时的回跳不得超过上界()
    {
        var limit = 2 * SnapEngine.FlushTolerance;
        var failures = new List<string>();

        foreach (var otherWidth in new double[] { 400, 413, 450 })
        {
            foreach (var widthDelta in new[] { -8.0, -4, -2, -1, 1, 2, 4, 8 })
            {
                var movingWidth = otherWidth + widthDelta;
                var other = new CanvasRect(1000, 400, otherWidth, 275);
                var moving = new CanvasRect(0, 400, movingWidth, 275);

                var previous = double.NegativeInfinity;

                for (var rawX = 900.0; rawX <= 1020; rawX += 1)
                {
                    var position = SnapEngine.Snap(moving.At(rawX, moving.Y), [.. SnapEngine.LinesOf(other)]).X;
                    var backwards = previous - position;

                    if (backwards > limit)
                    {
                        failures.Add($"otherW={F(otherWidth)} ΔW={F(widthDelta)}：raw={F(rawX)} 回跳 {F(backwards)}px（上一步 {F(previous)}）");
                    }

                    previous = position;
                }
            }
        }

        failures.ShouldBeEmpty($"回跳越界（上界 {limit}px，共 {failures.Count} 组）：\n" + string.Join("\n", failures.Take(10)));
    }

    /// <summary>外壳参照线（**只在 X 轴上**）+ 邻居模块的组合：回跳同样受上界约束。</summary>
    [Fact]
    public void 吸附_贴壳与反向对齐并存时的回跳不得超过上界()
    {
        var limit = 2 * SnapEngine.FlushTolerance;
        var failures = new List<string>();

        var line = new SnapReferenceLine(true, 1500, 300, 900);
        CanvasRect c = new(1500, 400, 413, 275);

        foreach (var movingWidth in new double[] { 400, 413, 450 })
        {
            var moving = new CanvasRect(0, 400, movingWidth, 275);
            var previous = double.NegativeInfinity;

            for (var rawX = 1000.0; rawX <= 1520; rawX += 1)
            {
                var position = SnapEngine.Snap(moving.At(rawX, moving.Y), [.. SnapEngine.LinesOf(c), SnapEngine.LineOf(line)]).X;
                var backwards = previous - position;

                if (backwards > limit)
                {
                    failures.Add($"movingW={F(movingWidth)}：raw={F(rawX)} 回跳 {F(backwards)}px（上一步 {F(previous)}）");
                }

                previous = position;
            }
        }

        failures.ShouldBeEmpty($"贴壳 + 反向对齐组合下回跳越界（上界 {limit}px）：\n" + string.Join("\n", failures.Take(10)));
    }
}
