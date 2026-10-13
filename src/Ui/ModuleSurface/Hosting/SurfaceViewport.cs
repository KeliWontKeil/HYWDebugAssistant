using Avalonia;
using Avalonia.Controls;

namespace HYWDebugAssistant.Ui.ModuleSurface.Hosting;

/// <summary>
/// **表面视口** —— 把滚动视口的高度**当成有限的事实**交给排布面板，并让面板在空间富余时占满视口。
/// </summary>
/// <remarks>
/// <para>
/// 它存在的唯一理由是 Avalonia 布局里一条容易踩空的规则：<b><see cref="Panel"/> 的排布不会把子控件
/// 拉到超过子控件自己量到的高度</b>（<c>ArrangeOverride</c> 只按 <c>DesiredSize</c> 摆放），
/// 而 <c>ScrollViewer</c> 的内容在"可纵向滚动"时是**按无限高测量**的。于是面板拿到的可用高不是视口高，
/// 而是<b>无穷</b> —— 面板只好回退成"内容最小高度"，<c>*</c> 行与填满型因此都拿不到余量。
/// </para>
/// <para>
/// 后果有两级，两级都真实存在过：<b>①</b> 内容比视口矮时，<c>ScrollContentPresenter</c> 按
/// "内容对齐"把它**居中**（实测 400×500 的模块窗口、面板高 160、视口高 454 ⇒ 整体下移 147px，
/// 屏幕上表现为"上面一大段空白、内容挤在中间、控件之间被撑开"）；
/// <b>②</b> 更根本的是喂给排布引擎的可用高是 160 而不是 454，声明的 <c>fill</c> 与 <c>*</c> 行高
/// 全都得不到该有的空间。
/// </para>
/// <para>
/// 所以这里的做法是：**用视口高去量、用视口高去报**。测量时把视口高当作上限传下去（内容想更矮就更矮，
/// 想更高就更高 —— 于是"放不下 ⇒ 出现滚动条"照旧），如实得高比内容需要的还多，
/// 就报<b>视口高</b>作为期望高 —— 于是面板被撑到视口高，<c>DesiredSize</c> 与视角一致，
/// 居中那一步自然也就不再发生。
/// </para>
/// <para>
/// 这一层刻意做成<b>一个只知道"高度怎么来"的壳</b>：它不认识任何控件类型，也不做排布决策 ——
/// 真正的排布仍然全部由 <c>ModuleSurfacePanel</c> 按声明完成。
/// </para>
/// </remarks>
internal sealed class SurfaceViewport : Decorator
{
    /// <summary>内容想要的真实高度（由**本遍**测量记下，供排布时决定"撑满还是留出滚动"）。</summary>
    /// <remarks>
    /// 它只在**同一遍布局**里从测量交班给排布：一旦又有新的测量进来，先把它作废（见
    /// <see cref="_measured"/>）—— 否则上一遍记下的数会在这一遍被当成事实读走，
    /// 测量与排布就不再是同一组输入，正是"跨遍漂移"的形态。
    /// </remarks>
    private double _contentHeight;

    /// <summary>本遍测量的世代号：排布只认与它同代的内容高。</summary>
    private int _measured;

    /// <summary>已交付给排布的那一代（交付过的内容高不再复用）。</summary>
    private int _consumed;

    /// <summary>上一次排布给到的宽度（被无穷宽测量时的**唯一可用的事实**）。</summary>
    private double _arrangedWidth;

    /// <inheritdoc />
    protected override Size MeasureOverride(Size availableSize)
    {
        var child = Child;
        if (child is null)
        {
            _contentHeight = 0;
            return default;
        }

        // 视口高（有限时才是一个事实；真被无穷测量时只能退回原本的"按内容"口径）。
        var viewport = availableSize.Height;

        // **宽度不能把"无穷"传给内容**：`ScrollViewer` 在还没排之前（以及横向可滚时）会按**无穷宽**
        // 量内容，而"按无穷宽量"会让**会折行的控件按整行文本报宽** —— `TextBox` 的
        // `TextWrapping=Wrap` 只在宽度受约束时才折行（实测 200 字符 ⇒ 文本面 1946 宽、视口 360）。
        // 宽度这一轴一律用**上一次排布给到的宽**兜底；还没排过（第一遍）就报 0，
        // 让上一层的排布决定 —— 那正是"框多宽由声明与视口决定，文字在里面折"这条口径的落点。
        var width = double.IsInfinity(availableSize.Width)
            ? _arrangedWidth
            : availableSize.Width;

        // **父层这一遍给了有限宽 ⇒ 不许超过它**：`_arrangedWidth` 是"上一次排布"的事实，
        // 而排布宽度本身来自上一层分下来的空间 —— 不夹的话它会变成**自我引用**：
        // 上一次被排宽了 ⇒ 这一遍照那个宽量 ⇒ 内容按更宽的框报宽 ⇒ 下一遍排得更宽，
        // 每轮长几十像素（实测页内容面板长到 418，而视口只有 380，右侧整条被裁掉）。
        // 横向滚动在本项目是关闭的（`HorizontalScrollBarVisibility.Disabled`），所以"夹住"没有副作用。
        if (!double.IsInfinity(availableSize.Width) && !double.IsInfinity(width))
        {
            width = Math.Min(width, availableSize.Width);
        }

        child.Measure(new Size(width, availableSize.Height));
        _contentHeight = child.DesiredSize.Height;
        _measured++;

        // 空间富余 ⇒ 报视口高（被撑满）；放不下 ⇒ 报内容高（出现滚动条，一个像素都不少）。
        var height = double.IsInfinity(viewport) ? _contentHeight : Math.Max(_contentHeight, viewport);

        return new Size(width, height);
    }

    /// <inheritdoc />
    protected override Size ArrangeOverride(Size finalSize)
    {
        // **把整个视口交给内容**：内容自己按声明决定哪一项吃掉余量（`fill` / `*` 行）。
        // 内容真正需要的比视口还高时，给它的仍然是那更大的高度 —— 否则填满型会把自己的最小高度吃掉。
        // 宽度**一律就是视口宽**（同测量：宽度不是内容的账）。
        _arrangedWidth = finalSize.Width;

        // **内容高只认本遍测量**：这一代还没被排布取走过，才用它；取过或过期就按视口高。
        // 这样排布的输入与测量的输入是同一组，不会读到上一遍留下的数。
        var content = _measured > _consumed ? _contentHeight : 0;
        _consumed = _measured;

        var height = Math.Max(finalSize.Height, content);

        Child?.Arrange(new Rect(0, 0, finalSize.Width, height));

        return new Size(finalSize.Width, height);
    }
}
