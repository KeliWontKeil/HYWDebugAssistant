using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

namespace HYWDebugAssistant.Sdk.Ui.Controls;

/// <summary>
/// **预览范围框覆盖层**：只画虚线矩形，不参与布局与命中。
/// </summary>
/// <remarks>
/// 它被放在模块视图内容之上，目标列表由 <see cref="ModuleView"/> 在预览环境收集；
/// 运行期不会创建。位置在每次布局后重算，因此窗口/内容尺寸变化后仍贴合控件。
/// </remarks>
internal sealed class PreviewBoundsOverlay : Control
{
    private readonly IReadOnlyList<Control> _targets;

    /// <summary>造一个覆盖层。</summary>
    /// <param name="targets">要画框的控件。</param>
    public PreviewBoundsOverlay(IReadOnlyList<Control> targets)
    {
        _targets = targets;
        IsHitTestVisible = false;
        ClipToBounds = false;
        LayoutUpdated += (_, _) => InvalidateVisual();
    }

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
        var pen = new Pen(brush, 1, new DashStyle([2, 2], 0));

        foreach (var target in _targets)
        {
            if (!target.IsVisible || target.Bounds.Width <= 0 || target.Bounds.Height <= 0)
            {
                continue;
            }

            if (target.TranslatePoint(default, this) is not { } origin)
            {
                continue;
            }

            context.DrawRectangle(null, pen, new Rect(origin, target.Bounds.Size));
        }
    }
}
