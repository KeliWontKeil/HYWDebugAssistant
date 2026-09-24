using HYWDebugAssistant.Core.Api;
using HYWDebugAssistant.Sdk.Ports;

namespace HYWDebugAssistant.Ui.Geometry;

/// <summary>端口所在边缘（框架渲染约定：**发送口在右、接收口在左**）。</summary>
/// <remarks>
/// 口声明不含"边"信息（<see cref="PortDeclaration"/> 只有方向/契约/语义），故由框架按数据流惯例渲染：
/// 数据从左进、从右出——这样"左右相邻贴合"才与"端口对齐"自洽（05 §C5-2）。
/// <para>
/// <see cref="Top"/>/<see cref="Bottom"/> 为**布局留口**（标记块在上/下边缘时改为横排）：
/// 当前只把发送口放右、接收口放左；将来开放"端口可拖到任意边缘"（C5-7）时无需再改渲染规则。
/// </para>
/// </remarks>
public enum PortSide
{
    /// <summary>左边缘（接收口）。</summary>
    Left = 0,

    /// <summary>右边缘（发送口）。</summary>
    Right = 1,

    /// <summary>上边缘（横排；当前未使用，为端口任意边能力留口）。</summary>
    Top = 2,

    /// <summary>下边缘（横排；当前未使用）。</summary>
    Bottom = 3,
}

/// <summary>端口锚点（几何）：连线与标记层的端点来源（Q5-4：模块只提供锚点）。</summary>
/// <param name="InstanceId">实例 ID。</param>
/// <param name="PortName">口名。</param>
/// <param name="Side">所在边缘。</param>
/// <param name="X">锚点 X（画布坐标）。</param>
/// <param name="Y">锚点 Y（画布坐标）。</param>
/// <param name="IsConnected">是否已加入网络。</param>
/// <param name="IsStalled">是否停流（合法状态）。</param>
public readonly record struct PortAnchor(
    string InstanceId,
    string PortName,
    PortSide Side,
    double X,
    double Y,
    bool IsConnected,
    bool IsStalled);

/// <summary>
/// 端口锚点计算：沿边缘**按端口数等分**（09 §5），用户在组合件里调整过则用其偏移（C5-7/C5-10）。
/// </summary>
/// <remarks>
/// 只保存**被调整过**的端口几何（M1-8 的 `ports` 段），未被调整的按等分规则即算即用——
/// 这样模块拉伸时默认锚点自动重排，而用户摆过的锚点保持（除非被锁定）。
/// </remarks>
public static class PortAnchorCalculator
{
    /// <summary>计算某实例的全部端口锚点（按"接收口在前、发送口在后"的顺序无关，仅按边缘分组）。</summary>
    /// <param name="rect">模块视图矩形。</param>
    /// <param name="ports">口视图列表。</param>
    /// <param name="geometryOf">取端口几何（未调整过时返回默认值）。</param>
    public static IReadOnlyList<PortAnchor> Compute(
        CanvasRect rect,
        IReadOnlyList<PortView> ports,
        Func<string, PortGeometryView>? geometryOf = null)
    {
        ArgumentNullException.ThrowIfNull(ports);

        if (ports.Count == 0)
        {
            return [];
        }

        var result = new List<PortAnchor>(ports.Count);

        foreach (var side in (PortSide[])[PortSide.Left, PortSide.Right])
        {
            var onSide = ports
                .Where(p => SideOf(p.Direction) == side)
                .ToList();

            for (var index = 0; index < onSide.Count; index++)
            {
                var port = onSide[index];
                var geometry = geometryOf?.Invoke(port.Name);
                var offset = geometry is { Offset: var adjusted } && Math.Abs(adjusted - 0.5) > double.Epsilon
                    ? adjusted
                    : EvenOffset(index, onSide.Count);

                var x = side == PortSide.Left ? rect.Left : rect.Right;
                var y = rect.Top + (rect.Height * offset);

                result.Add(new PortAnchor(
                    string.Empty,
                    port.Name,
                    side,
                    x,
                    y,
                    port.IsConnected,
                    port.IsStalled));
            }
        }

        return result;
    }

    /// <summary>边上的**等分**位置（第 <paramref name="index"/> 个，共 <paramref name="count"/> 个）。</summary>
    /// <remarks>等分而非两端对齐：n+1 段的中点，保证不与边缘重合（视觉上与模块边框留出余量）。</remarks>
    public static double EvenOffset(int index, int count)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(index);
        ArgumentOutOfRangeException.ThrowIfLessThan(count, 1);

        return (index + 1d) / (count + 1d);
    }

    /// <summary>口方向 → 所在边缘。</summary>
    public static PortSide SideOf(PortDirection direction) =>
        direction == PortDirection.Send ? PortSide.Right : PortSide.Left;

    /// <summary>口方向**字面量**（<see cref="PortView.Direction"/>）→ 所在边缘。</summary>
    public static PortSide SideOf(string direction) =>
        string.Equals(direction, nameof(PortDirection.Send), StringComparison.Ordinal)
            ? PortSide.Right
            : PortSide.Left;
}
