using HYWDebugAssistant.Sdk.Views;

namespace HYWDebugAssistant.Ui.ModuleSurface.Layout;

/// <summary>
/// **块轴（高度）的唯一解析者** —— 纯函数、不认识 Avalonia，也不认识控件类型。
/// </summary>
/// <remarks>
/// <para>
/// 存在的理由是"高度只有一个权威"：从前这件事被拆成五处（面板里的几个私有函数加上读
/// <c>DesiredSize</c> 缓存的分配），于是同一个声明在不同上下文得到不同高度，
/// 且没人能指着某一层说"这个高是它算的"。
/// </para>
/// <para>
/// <b>两个语义各只有一个答案</b>：
/// <list type="number">
///   <item><b>地板</b>（<see cref="SpecOf"/>）：这块内容**最少**要占多高 —— 窗口压小时不许比它更矮。
///     声明了 <c>pixels</c> 就是那个像素；<c>adaptive</c> 就是内容量到的高；
///     <c>fill</c> 就是声明的 <c>min</c>（没写 = 0）。</item>
///   <item><b>分配</b>（<see cref="Plan"/>）：在实得高度下，每个兄弟**实际**占多高 ——
///     非填满型拿自己的（像素 / 内容），余量在填满型之间**等分**，且每个填满型**不小于自己的地板**。</item>
/// </list>
/// </para>
/// <para>
/// <b>地板只有声明一个来源</b>（<c>height.min</c>，缺省 0）：目录只说"这一种控件吃不吃余量"，
/// 不带高度数值；容器内部的结构高度（页签条、标题行）与子项的地板也**不进**这里 ——
/// 那两样都由作者算进自己的声明（见 <see cref="SurfaceMetrics.FromNode"/> 的说明）。
/// </para>
/// <para>
/// <b>本类不测量</b>：内容量到多高由调用方（面板）量好传进来。这样"决定"与"询问"分开 ——
/// 决定是纯的（可断言），询问是框架的事。
/// </para>
/// </remarks>
public static class SurfaceBlockSolver
{
    /// <summary>求一块内容的**块轴口径**。</summary>
    /// <param name="node">声明节点（几何与约束的唯一来源）。</param>
    /// <param name="measured">该内容**在有限可用高下**量到的高。</param>
    /// <returns>口径。</returns>
    public static BlockSpec SpecOf(ViewNode node, double measured)
    {
        ArgumentNullException.ThrowIfNull(node);

        var axis = node.Height;
        var floor = Math.Max(0, axis.Min ?? 0);
        var ceiling = axis.Max ?? double.PositiveInfinity;
        var content = double.IsInfinity(measured) ? 0 : Math.Max(0, measured);

        return axis.ResolvedMode switch
        {
            // 声明写死的像素：它就是要那么高，与可用空间无关（上下限仍夹得住）。
            AxisMode.Pixels => new BlockSpec(AxisMode.Pixels, Clamp(axis.Pixels ?? 0), Growable: false),

            // 按内容：内容多高就多高，不吃余量。
            AxisMode.Adaptive => new BlockSpec(AxisMode.Adaptive, Clamp(content), Growable: false),

            // 填满：地板 = 声明的 min（**不含内容量到的高**，也不含子项的地板）。
            // 填满的语义是"吃掉容器余量"，它与"这一块的内容有多高"无关：
            // 内容超出时由它**自己内部的滚动**消化，绝不用内容高去撑大父层
            // （否则日志/输入框会把整块表面撑到几千像素，兄弟被推出视口）。
            AxisMode.Fill => new BlockSpec(AxisMode.Fill, Clamp(floor), Growable: true),

            // 未声明：按内容（读取器已把"三档都没写"拦成硬伤，这一档只是防御）。
            _ => new BlockSpec(AxisMode.Adaptive, Clamp(content), Growable: false),
        };

        double Clamp(double value) => Math.Max(0, Math.Min(ceiling, value));
    }

    /// <summary>在实得高度下，把高度分给这一层的每个孩子（**唯一分配规则**）。</summary>
    /// <param name="specs">每个孩子的口径（顺序 = 声明顺序）。</param>
    /// <param name="spacing">兄弟之间的间距。</param>
    /// <param name="containerHeight">这一层实得的高度。</param>
    /// <returns>每个孩子实得的高度（同一个顺序）。</returns>
    /// <remarks>
    /// <para>
    /// 规则只有一条：**先扣非填满型与间距，余量在填满型之间等分，且每个填满型不小于自己的地板**。
    /// 余量不足（<c>fixed + 各填满型地板 &gt; 容器高</c>）时各填满型**守住自己的地板** ——
    /// 于是各孩子实得高度之和大于容器高，这一层如实上报"我需要这么高"，由**外层滚动**接管。
    /// 绝不把元素压成 0 或压到地板以下。
    /// </para>
    /// <para>
    /// 没有填满型时余量**留在底部**（内容按内容高排）—— 与既有观感一致。
    /// </para>
    /// </remarks>
    public static BlockPlan Plan(IReadOnlyList<BlockSpec> specs, double spacing, double containerHeight)
    {
        ArgumentNullException.ThrowIfNull(specs);

        var count = specs.Count;
        var heights = new double[count];

        if (count == 0)
        {
            return new BlockPlan(heights);
        }

        var gap = Math.Max(0, spacing);
        var total = double.IsInfinity(containerHeight) ? 0 : Math.Max(0, containerHeight);

        // ① 非填满型拿自己的；顺手数出填满型有几个、它们的地板合计多少。
        var fixedPart = gap * (count - 1);
        var flexible = 0;
        var flexibleFloor = 0.0;

        for (var i = 0; i < count; i++)
        {
            var spec = specs[i];

            if (spec.Growable)
            {
                flexible++;
                flexibleFloor += spec.Floor;
                continue;
            }

            heights[i] = spec.Floor;
            fixedPart += spec.Floor;
        }

        if (flexible == 0)
        {
            // 全部非填满型：余量留在底部（不自作主张拉高任何一个）。
            return new BlockPlan(heights);
        }

        // ② 余量等分给填满型；不足则各自守地板（之和超出容器高 ⇒ 外层滚动接手）。
        var leftover = total - fixedPart;

        if (leftover <= flexibleFloor)
        {
            for (var i = 0; i < count; i++)
            {
                if (specs[i].Growable)
                {
                    heights[i] = specs[i].Floor;
                }
            }

            return new BlockPlan(heights);
        }

        var share = (leftover - flexibleFloor) / flexible;

        for (var i = 0; i < count; i++)
        {
            if (specs[i].Growable)
            {
                heights[i] = specs[i].Floor + share;
            }
        }

        return new BlockPlan(heights);
    }
}

/// <summary>一块内容在块轴上的口径（地板 + 是否可以涨到余量）。</summary>
/// <param name="Mode">解析后的轴口径（<c>pixels</c> / <c>adaptive</c> / <c>fill</c>）。</param>
/// <param name="Floor">地板：窗口压小时不许比它更矮（DIP）。</param>
/// <param name="Growable">是否吃容器余量（只有 <c>fill</c> 为真）。</param>
public readonly record struct BlockSpec(AxisMode Mode, double Floor, bool Growable);

/// <summary>一层的高度分配结果。</summary>
/// <param name="Heights">每个孩子实得的高度（顺序 = 传入顺序）。</param>
public readonly record struct BlockPlan(double[] Heights);
