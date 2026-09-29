using Avalonia;
using Avalonia.Controls;
using HYWDebugAssistant.Ui.ViewModels;

namespace HYWDebugAssistant.Ui.Controls;

/// <summary>
/// 模块内容区里**元素列表**的排布面板〔2026-09 用户裁定 · 见 `docs/decisions.md`〕。
/// </summary>
/// <remarks>
/// <para>
/// <b>为什么需要它</b>：此前面板是普通 `StackPanel`（只按内容高度排），于是——
/// ① <b>显示区（A5）会随行数变长</b>：内容一多就把模块内容撑长，外层滚动条动、显示区自己的滚动条不动
/// （用户："应该是显示滚动条而不是变布局吧"）；
/// ② <b>多行输入框的高度不随窗口拉伸</b>：拉大窗口时只有宽度变（用户："文本框只有宽度随动，高度却不产生变化"）。
/// </para>
/// <para>
/// <b>规则</b>（"要填满的" vs "按内容高"）：
/// <list type="bullet">
///   <item><b>填满型</b>：A5 文本日志 + **多行**文本输入 —— 它们分走"剩余高度"，内部出滚动条；</item>
///   <item><b>按内容型</b>：其余元素（数值 / 曲线 / 按钮 / 单行输入 / 开关 / 标签条…）—— 高度 = 内容高度；</item>
///   <item><b>最小高度</b>：所有填满型都有一个下限（日志 88 DIP = 既有视觉取值；多行输入 = 其
///     <c>EditorMinHeight</c>）⇒ 窗口太小时由**外层 ScrollViewer** 滚动，而不是把填满型压没。</item>
/// </list>
/// </para>
/// <para>
/// <b>间距</b>：<see cref="Spacing"/> 是**给开发者调的**（当前值写在 `ModuleContentView.axaml` 里）——
/// 位置观感问题改那里即可，不用改代码。
/// </para>
/// </remarks>
public sealed class ModuleElementPanel : Panel
{
    /// <summary>A5 文本日志的最小高度（DIP）—— 与既有视觉取值一致（`architecture/07`）。</summary>
    public const double LogMinHeight = 88;

    /// <summary>元素之间的间距（可调；默认 2）。</summary>
    public static readonly StyledProperty<double> SpacingProperty =
        AvaloniaProperty.Register<ModuleElementPanel, double>(nameof(Spacing), 2);

    /// <summary>元素之间的间距（DIP）。</summary>
    public double Spacing
    {
        get => GetValue(SpacingProperty);
        set => SetValue(SpacingProperty, value);
    }

    /// <inheritdoc />
    protected override Size MeasureOverride(Size availableSize)
    {
        var width = double.IsInfinity(availableSize.Width) ? 0 : availableSize.Width;
        var needed = 0.0;
        var visible = 0;

        foreach (var child in Children)
        {
            if (!child.IsVisible)
            {
                continue;
            }

            // 填满型按**最小高度**测量（有限视口！）—— 这一点很关键：
            //   · 用无限高测量的话，日志的 `ListBox` 会以为空间无限 ⇒ **虚拟化失效**（把 2 000 行全实例化，
            //     实测抓到）；有限视口下它只实例化可见的几行；
            //   · 其余元素用无限高测量，报自己的内容高度。
            child.Measure(IsFill(child)
                ? new Size(width, MinHeightOf(child))
                : new Size(width, double.PositiveInfinity));

            needed += visible++ > 0 ? Spacing : 0;
            needed += IsFill(child) ? MinHeightOf(child) : child.DesiredSize.Height;
        }

        // 面板的期望高度 = **最小布局高度**：窗口更大 ⇒ Arrange 把余量给填满型；更小 ⇒ 外层滚动条接管
        return new Size(width, needed);
    }

    /// <inheritdoc />
    protected override Size ArrangeOverride(Size finalSize)
    {
        var fillCount = 0;
        var fixedHeight = 0.0;
        var visible = 0;

        foreach (var child in Children)
        {
            if (!child.IsVisible)
            {
                continue;
            }

            fixedHeight += visible++ > 0 ? Spacing : 0;

            if (IsFill(child))
            {
                fillCount++;
            }
            else
            {
                fixedHeight += child.DesiredSize.Height;
            }
        }

        // 余量在"填满型"之间**等分**；没有填满型时余量留在底部（内容按内容高排，保持既有观感）
        var each = fillCount > 0 ? Math.Max(0, finalSize.Height - fixedHeight) / fillCount : 0;
        var y = 0.0;
        visible = 0;

        foreach (var child in Children)
        {
            if (!child.IsVisible)
            {
                continue;
            }

            y += visible++ > 0 ? Spacing : 0;

            var height = IsFill(child)
                ? Math.Max(MinHeightOf(child), each)
                : child.DesiredSize.Height;

            child.Arrange(new Rect(0, y, finalSize.Width, height));
            y += height;
        }

        // 实占高度可能超过 finalSize（窗口太小时各填满型仍守住最小高度）⇒ 让外层 ScrollViewer 知道真实高度
        return new Size(finalSize.Width, Math.Max(finalSize.Height, y));
    }
    /// <summary>是否参与排布（不可见的元素不占位）。</summary>
    private static bool IsMeasurable(Control child) => child.IsVisible;

    /// <summary>是否"填满型"（A5 文本日志 · 多行文本输入）。</summary>
    private static bool IsFill(Control child) => child.DataContext switch
    {
        TextLogElementViewModel => true,
        TextInputElementViewModel { IsMultiline: true } => true,
        _ => false,
    };

    /// <summary>填满型的**最小高度**。</summary>
    private static double MinHeightOf(Control child) => child.DataContext switch
    {
        TextLogElementViewModel => LogMinHeight,
        TextInputElementViewModel input => Math.Max(input.EditorMinHeight, 1),
        _ => 1,
    };
}
