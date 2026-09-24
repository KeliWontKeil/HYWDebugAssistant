using Avalonia;
using Avalonia.Controls;
using HYWDebugAssistant.Ui.Geometry;
using HYWDebugAssistant.Ui.ViewModels;

namespace HYWDebugAssistant.Ui.Controls;

/// <summary>
/// 口标记的排版面板：标记块**只落在预留带内**（不覆盖模块内容），沿边缘按**归一化偏移**定位。
/// </summary>
/// <remarks>
/// <list type="bullet">
///   <item><b>预留带</b>：左右边缘各留 <see cref="BandThickness"/> px、上下边缘各留 <see cref="BandThickness"/> px；
///     内容区由视图侧加同宽内边距 —— 因此标记与内容**永不重叠**（用户反馈：标记"像覆盖了正常内容"）；</item>
///   <item><b>形态随边缘变化</b>（简单规则，不做过度设计）：左右边缘 = **竖排**（块窄而高、文字旋转 90°）；
///     上下边缘 = **横排**（块宽而扁、文字横排）。当前框架把发送口放右、接收口放左，上下分支为将来留口；</item>
///   <item>位置只依赖边缘与偏移，窗口拉伸时按比例重排（与 <c>PortAnchorCalculator</c> 的等分规则一致）。</item>
/// </list>
/// </remarks>
public sealed class PortMarkerPanel : Panel
{
    /// <summary>预留带厚度（px）——内容区需加同宽内边距，否则标记会压住内容。</summary>
    public const double BandThickness = 20;

    /// <summary>标记与窗口外沿的间距（px）——**【ADR-0039 D3】0 = 标记与外边框完全重合**。</summary>
    private const double EdgeInset = 0;

    /// <inheritdoc />
    protected override Size MeasureOverride(Size availableSize)
    {
        foreach (var child in Children)
        {
            child.Measure(availableSize);
        }

        return availableSize;
    }

    /// <inheritdoc />
    protected override Size ArrangeOverride(Size finalSize)
    {
        foreach (var child in Children)
        {
            var marker = child.DataContext as PortMarkerViewModel;
            var offset = marker?.Offset ?? 0.5;
            var side = marker?.Side ?? PortSide.Left;
            var size = child.DesiredSize;

            double x;
            double y;

            switch (side)
            {
                case PortSide.Left:
                case PortSide.Right:
                    // 竖排：沿边缘上下分布，块贴左/右外沿
                    y = (finalSize.Height * Math.Clamp(offset, 0, 1)) - (size.Height / 2);
                    y = Math.Clamp(y, 0, Math.Max(0, finalSize.Height - size.Height));
                    x = side == PortSide.Right
                        ? Math.Max(0, finalSize.Width - size.Width - EdgeInset)
                        : EdgeInset;
                    break;

                default:
                    // 横排：沿边缘左右分布，块贴上/下外沿
                    x = (finalSize.Width * Math.Clamp(offset, 0, 1)) - (size.Width / 2);
                    x = Math.Clamp(x, 0, Math.Max(0, finalSize.Width - size.Width));
                    y = side == PortSide.Bottom
                        ? Math.Max(0, finalSize.Height - size.Height - EdgeInset)
                        : EdgeInset;
                    break;
            }

            child.Arrange(new Rect(x, y, size.Width, size.Height));
        }

        return finalSize;
    }
}
