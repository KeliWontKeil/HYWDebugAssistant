using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

namespace HYWDebugAssistant.Sdk.Ui.Controls;

/// <summary>
/// **预览范围框覆盖层**：只画矩形，不参与布局与命中。
/// </summary>
/// <remarks>
/// <para>
/// 它被放在模块视图内容之上，目标列表由 <see cref="ModuleView"/> 在预览环境收集；运行期不会创建。
/// 位置在每次布局后重算，因此窗口/内容尺寸变化后仍贴合控件。
/// </para>
/// <para>
/// **两种角色、两种线型**：控件框 = 虚线（控件最终边界）；容器框 = 点线 + 半透明（布局槽位）。
/// 两者若同型，用户会把"一整片空白槽位"误当成"控件边界"（踩过）。
/// </para>
/// </remarks>
internal sealed class PreviewBoundsOverlay : Control
{
    private readonly IReadOnlyList<PreviewBoundsTarget> _targets;

    /// <summary>造一个覆盖层。</summary>
    /// <param name="targets">要画框的目标（控件 + 角色）。</param>
    public PreviewBoundsOverlay(IReadOnlyList<PreviewBoundsTarget> targets)
    {
        Targets = targets;
        _targets = targets;
        IsHitTestVisible = false;
        ClipToBounds = false;
        LayoutUpdated += (_, _) => InvalidateVisual();
    }

    /// <summary>当前收集到的框目标（预览会话断言用；运行期不创建本层）。</summary>
    internal IReadOnlyList<PreviewBoundsTarget> Targets { get; }

    /// <inheritdoc />
    protected override Size MeasureOverride(Size availableSize) => availableSize;

    /// <inheritdoc />
    public override void Render(DrawingContext context)
    {
        base.Render(context);

        if (_targets.Count == 0)
        {
            return;
        }

        // 绝对色：主题资源缺失时的范围框兜底（正常路径使用 TextMuted token）。
        var brush = this.TryFindResource("TextMuted", out var value) && value is IBrush found
            ? found
            : Brushes.Gray;

        // 容器框先画、线更淡：它在"槽位"这一层，不能抢控件框的注意力。
        DrawTargets(context, PreviewBoundsRole.Container, new Pen(WithAlpha(brush, 0.55), 1, new DashStyle([1, 3], 0)));
        DrawTargets(context, PreviewBoundsRole.Control, new Pen(brush, 1, new DashStyle([2, 2], 0)));
    }

    /// <summary>按角色画一遍框（顺序：容器在控件之下）。</summary>
    private void DrawTargets(DrawingContext context, PreviewBoundsRole role, IPen pen)
    {
        foreach (var target in _targets)
        {
            if (target.Role != role)
            {
                continue;
            }

            var control = target.Control;
            if (!control.IsVisible || control.Bounds.Width <= 0 || control.Bounds.Height <= 0)
            {
                continue;
            }

            if (control.TranslatePoint(default, this) is not { } origin)
            {
                continue;
            }

            context.DrawRectangle(null, pen, new Rect(origin, control.Bounds.Size));
        }
    }

    /// <summary>同一色降透明度（容器框用；非实色画刷无法降透明度时原样使用）。</summary>
    private static IBrush WithAlpha(IBrush brush, double opacity) =>
        brush is ISolidColorBrush solid ? new SolidColorBrush(solid.Color, opacity) : brush;
}
