using HYWDebugAssistant.Core.Api;

namespace HYWDebugAssistant.Ui.Theming;

/// <summary>外壳宽度/高度/可缩放性的纯计算结果（不碰窗口对象，便于单测）。</summary>
/// <param name="Width">DIP 宽。</param>
/// <param name="Height">DIP 高。</param>
/// <param name="CanResize">是否可自由缩放。</param>
public readonly record struct ShellGeometry(double Width, double Height, bool CanResize);

/// <summary>
/// **外壳几何的纯规划器**：工作区优先、显式改比例才重算。
/// </summary>
/// <remarks>
/// 有工作区快照时，窗口尺寸/位置是“保存的现场形态”，**不按当前比例重算**；
/// <c>ShellAspect</c>/<c>ShellFixedSize</c> 是全局偏好，只在无工作区或用户显式改比例时参与。
/// </remarks>
public static class ShellGeometryPlanner
{
    /// <summary>启动时的几何。</summary>
    /// <param name="aspect">当前比例设置。</param>
    /// <param name="fixedSize">当前尺寸固定设置。</param>
    /// <param name="workspace">工作区中的外壳矩形（可为空）。</param>
    public static ShellGeometry Startup(
        string? aspect,
        bool fixedSize,
        WorkspacePlacementView? workspace)
    {
        if (workspace is { Width: > 0, Height: > 0 } saved)
        {
            return new ShellGeometry(saved.Width, saved.Height, CanResize: !fixedSize);
        }

        var (width, height) = ShellLayout.Measure(aspect);
        return new ShellGeometry(width, height, CanResize: !fixedSize);
    }

    /// <summary>设置应用后的几何：只有显式改比例才重新测量；切固定只改可缩放性。</summary>
    /// <param name="oldAspect">上一次已生效比例。</param>
    /// <param name="oldFixedSize">上一次已生效的固定状态。</param>
    /// <param name="newAspect">新的已生效比例。</param>
    /// <param name="newFixedSize">新的已生效固定状态。</param>
    /// <param name="currentWidth">当前窗口宽（DIP）。</param>
    /// <param name="currentHeight">当前窗口高（DIP）。</param>
    public static ShellGeometry AfterSettings(
        string? oldAspect,
        bool oldFixedSize,
        string? newAspect,
        bool newFixedSize,
        double currentWidth,
        double currentHeight)
    {
        _ = oldFixedSize;
        _ = newFixedSize;

        if (!string.Equals(oldAspect, newAspect, StringComparison.Ordinal))
        {
            var (width, height) = ShellLayout.Measure(newAspect);
            return new ShellGeometry(width, height, CanResize: !newFixedSize);
        }

        return new ShellGeometry(currentWidth, currentHeight, CanResize: !newFixedSize);
    }
}
