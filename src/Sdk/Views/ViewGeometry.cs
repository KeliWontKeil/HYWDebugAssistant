namespace HYWDebugAssistant.Sdk.Views;

/// <summary>一个轴（宽/高）上的**尺寸口径**：三条优先级 + 可空约束。</summary>
/// <remarks>
/// <para>
/// 三个优先级从高到低显式声明：是否文本自适应 → 是否填满格子 → 像素宽度，前一个为否再判断
/// 下一个；**不能静默声明** —— 三选一必须在 json 里看得见（避免多个事实源）⇒ 落地成每轴一个对象：
/// <c>"width": { "adaptive": true }</c> /
/// <c>{ "fill": true }</c> / <c>{ "pixels": 128 }</c>，约束 <c>min</c>/<c>max</c> 写在同一对象里。
/// </para>
/// <para>
/// 最小尺寸/默认尺寸/最大尺寸全部进 .json，其中最小/最大**允许声明为空** ⇒
/// <see cref="Min"/>/<see cref="Max"/> 可空 = 框架缺省（不夹）。
/// </para>
/// </remarks>
public sealed record AxisLayout
{
    /// <summary>最高优先级：**随内容自适应**（控件按自己的内容尺寸占位）。</summary>
    public bool Adaptive { get; init; }

    /// <summary>次优先级：**填满**（网格里 = 填满格子；流式里 = 满宽/分走剩余高度）。</summary>
    public bool Fill { get; init; }

    /// <summary>最低优先级：**固定像素**（DIP；必须 ≥ 1；绝对坐标模式两轴必须是它）。</summary>
    public double? Pixels { get; init; }

    /// <summary>可选下限（DIP；空 = 框架缺省：填满型的日志 88 / 多行输入编辑器最小高，其余 0）。</summary>
    public double? Min { get; init; }

    /// <summary>可选上限（DIP；空 = 不夹）。</summary>
    public double? Max { get; init; }

    /// <summary>
    /// 解析后的口径（三优先级：<c>adaptive</c> &gt; <c>fill</c> &gt; <c>pixels</c>；
    /// 三条都没写 ⇒ <see cref="AxisMode.Undeclared"/> = 框架缺省；读取器把这种写法拦成硬伤）。
    /// </summary>
    public AxisMode ResolvedMode =>
        Adaptive ? AxisMode.Adaptive
        : Fill ? AxisMode.Fill
        : Pixels is not null ? AxisMode.Pixels
        : AxisMode.Undeclared;
}

/// <summary>一个轴上的尺寸口径（三优先级 + 未声明）。</summary>
public enum AxisMode
{
    /// <summary>没声明这个轴（框架缺省：宽度满宽；日志/多行输入的高度填满）。</summary>
    Undeclared = 0,

    /// <summary>随内容自适应。</summary>
    Adaptive = 1,

    /// <summary>填满格子/剩余空间。</summary>
    Fill = 2,

    /// <summary>固定像素。</summary>
    Pixels = 3,
}

/// <summary>容器的排布模式。</summary>
public enum ViewLayoutMode
{
    /// <summary>单列满宽、自上而下（缺省；与引入本机制之前**逐像素一致**）。</summary>
    Flow = 0,

    /// <summary>绝对坐标（每个元素给出 X/Y/宽/高）。</summary>
    Absolute = 1,

    /// <summary>表格（列/行轨道 + 元素落在格子里）。</summary>
    Grid = 2,
}

/// <summary>绝对坐标越界时的处理。</summary>
public enum ViewLayoutOverflow
{
    /// <summary>交给外层滚动（缺省；模块内容根自身就在滚动）。</summary>
    Scroll = 0,

    /// <summary>裁剪（不产生滚动范围；内容超出部分直接不可见）。</summary>
    Clip = 1,
}

/// <summary><see cref="ViewLayoutMode"/> 的字面量解析。</summary>
public static class ViewLayoutModeExtensions
{
    /// <summary>解析排布模式字面量（不认识的值 ⇒ <paramref name="fallback"/>；读取器已拦未知值）。</summary>
    /// <param name="text">字面量（<c>flow</c> / <c>absolute</c> / <c>grid</c>）。</param>
    /// <param name="fallback">不认识时的回退值。</param>
    public static ViewLayoutMode Parse(string? text, ViewLayoutMode fallback) => text?.Trim().ToLowerInvariant() switch
    {
        "flow" => ViewLayoutMode.Flow,
        "absolute" => ViewLayoutMode.Absolute,
        "grid" => ViewLayoutMode.Grid,
        _ => fallback,
    };

    /// <summary>是否是认识的排布模式字面量。</summary>
    /// <param name="text">字面量（空 = 缺省，算认识）。</param>
    public static bool IsKnown(string? text) =>
        string.IsNullOrWhiteSpace(text) || text.Trim().ToLowerInvariant() is "flow" or "absolute" or "grid";
}

/// <summary><see cref="ViewLayoutOverflow"/> 的字面量解析。</summary>
public static class ViewLayoutOverflowExtensions
{
    /// <summary>解析越界处理字面量（不认识的值 ⇒ <paramref name="fallback"/>；读取器已拦未知值）。</summary>
    /// <param name="text">字面量（<c>scroll</c> / <c>clip</c>）。</param>
    /// <param name="fallback">不认识时的回退值。</param>
    public static ViewLayoutOverflow Parse(string? text, ViewLayoutOverflow fallback) => text?.Trim().ToLowerInvariant() switch
    {
        "scroll" => ViewLayoutOverflow.Scroll,
        "clip" => ViewLayoutOverflow.Clip,
        _ => fallback,
    };

    /// <summary>是否是认识的越界处理字面量。</summary>
    /// <param name="text">字面量（空 = 缺省，算认识）。</param>
    public static bool IsKnown(string? text) =>
        string.IsNullOrWhiteSpace(text) || text.Trim().ToLowerInvariant() is "scroll" or "clip";
}

/// <summary>元素落在格子里时的停靠方式（对应 WinForms 的 `Anchor` 单边/居中取值）。</summary>
public enum CellAlign
{
    /// <summary>填满格子（缺省）。</summary>
    Stretch = 0,

    /// <summary>靠左/靠上（配合显式 `width`/`height` 使用）。</summary>
    Start = 1,

    /// <summary>居中。</summary>
    Center = 2,

    /// <summary>靠右/靠下。</summary>
    End = 3,
}

/// <summary>网格轨道规格的种类。</summary>
public enum GridTrackKind
{
    /// <summary>等分/按权重分剩余空间。</summary>
    Star = 0,

    /// <summary>按内容大小。</summary>
    Auto = 1,

    /// <summary>固定 DIP。</summary>
    Fixed = 2,
}

/// <summary>
/// 一条网格轨道规格（列或行）。
/// </summary>
/// <param name="Kind">轨道种类。</param>
/// <param name="Value">权重（<see cref="GridTrackKind.Star"/>）或固定尺寸（<see cref="GridTrackKind.Fixed"/>）；<see cref="GridTrackKind.Auto"/> 恒为 0。</param>
public readonly record struct GridTrack(GridTrackKind Kind, double Value)
{
    /// <summary>等分轨道（<c>*</c>）。</summary>
    /// <param name="weight">权重（<c>2*</c> ⇒ 2）。</param>
    public static GridTrack Star(double weight = 1) => new(GridTrackKind.Star, weight);

    /// <summary>按内容轨道（<c>auto</c>）。</summary>
    public static GridTrack Auto() => new(GridTrackKind.Auto, 0);

    /// <summary>固定尺寸轨道（如 <c>72</c>）。</summary>
    /// <param name="size">尺寸（DIP，必须 &gt; 0）。</param>
    public static GridTrack Fixed(double size) => new(GridTrackKind.Fixed, size);

    /// <summary>
    /// 解析规格字面量：<c>*</c> · <c>2*</c> · <c>auto</c> · <c>≥1 的数值</c>。
    /// </summary>
    /// <param name="spec">规格字面量（大小写不敏感、允许首尾空白）。</param>
    /// <param name="track">解析结果（失败时为默认值）。</param>
    /// <returns>是否解析成功。</returns>
    public static bool TryParse(string? spec, out GridTrack track)
    {
        track = Star();

        if (string.IsNullOrWhiteSpace(spec))
        {
            return false;
        }

        var text = spec.Trim();
        if (string.Equals(text, "auto", StringComparison.OrdinalIgnoreCase))
        {
            track = Auto();
            return true;
        }

        if (text.EndsWith('*'))
        {
            var head = text[..^1].Trim();
            if (head.Length == 0)
            {
                track = Star();
                return true;
            }

            if (double.TryParse(head, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var weight)
                && weight > 0
                && !double.IsInfinity(weight))
            {
                track = Star(weight);
                return true;
            }

            return false;
        }

        if (double.TryParse(text, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var size)
            && size > 0
            && !double.IsInfinity(size))
        {
            track = Fixed(size);
            return true;
        }

        return false;
    }

    /// <summary>批量解析（**任一非法项 ⇒ 返回空列表**，由读取器决定口径 —— 校验期已拦硬伤）。</summary>
    /// <param name="specs">规格字面量列表。</param>
    public static IReadOnlyList<GridTrack> ParseAll(IEnumerable<string>? specs)
    {
        if (specs is null)
        {
            return [];
        }

        var tracks = new List<GridTrack>();
        foreach (var spec in specs)
        {
            if (!TryParse(spec, out var track))
            {
                return [];
            }

            tracks.Add(track);
        }

        return tracks;
    }
}

/// <summary><see cref="CellAlign"/> 的字面量解析。</summary>
public static class CellAlignExtensions
{
    /// <summary>解析停靠字面量（不认识的值 ⇒ <paramref name="fallback"/>；读取器已拦未知值）。</summary>
    /// <param name="text">字面量（<c>stretch</c> / <c>left</c> / <c>center</c> / <c>right</c>，垂直方向用 <c>top</c> / <c>bottom</c>）。</param>
    /// <param name="fallback">不认识时的回退值。</param>
    public static CellAlign Parse(string? text, CellAlign fallback) => text?.Trim().ToLowerInvariant() switch
    {
        "stretch" or "fill" => CellAlign.Stretch,
        "left" or "top" or "start" => CellAlign.Start,
        "center" => CellAlign.Center,
        "right" or "bottom" or "end" => CellAlign.End,
        _ => fallback,
    };

    /// <summary>是否是认识的停靠字面量。</summary>
    /// <param name="text">字面量（空 = 缺省，算认识）。</param>
    public static bool IsKnown(string? text) =>
        string.IsNullOrWhiteSpace(text)
        || text.Trim().ToLowerInvariant() is "stretch" or "fill" or "left" or "top" or "start" or "center" or "right" or "bottom" or "end";
}
