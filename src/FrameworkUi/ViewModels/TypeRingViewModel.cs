using System.ComponentModel;
using Avalonia;
using Avalonia.Media;
using Avalonia.Media.Immutable;
using HYWDebugAssistant.Ui.ViewModels;

namespace HYWDebugAssistant.FrameworkUi.ViewModels;

/// <summary>
/// **模块清单预览图块的类型色外圈**：把"这个模块承载哪些数据类型"画成图块外圈的色环。
/// </summary>
/// <remarks>
/// <list type="bullet">
///   <item><b>颜色唯一来源</b>：一律经 <see cref="PacketTypePalette"/> 取色 ——
///     与端口标记的窄色条、叠加层角色方块的填充**同源**；用户改色后三处一起变，
///     不存在"清单自己的一套配色"。</item>
///   <item><b>一色一半</b>：多类型（如转换器的入口字节 + 出口波形）沿横向**等分**，
///     每段一色、硬切换（相邻停靠点同偏移）—— 用户一眼能看出"它同时管两种东西"，
///     而不是把两种颜色混成一个说不清的新色。</item>
///   <item><b>相等按"签名"</b>：清单列表用 <see cref="CollectionSync"/> 做增量同步，
///     行模型的值相等决定"要不要换实例"。故本类型只以 <see cref="Signature"/> 判等 ——
///     同一组类型每次刷新得到的环**彼此相等**，列表不会因为"新建了一个等价的环"
///     而整批重建（那会表现为悬停时全列表乱闪）。</item>
///   <item><b>可变的是画刷，不是身份</b>：用户改色后由 <see cref="RefreshBrush"/> 就地换画刷并
///     发通知（不重建行、不换实例）；身份仍是那组类型标识。</item>
///   <item><b>画刷必须不可变</b>：<see cref="LinearGradientBrush"/> 是 <c>AvaloniaObject</c>，
///     有线程亲和性；本类型可能被任意线程首次构造，故只构造 <c>Immutable*</c> 实例。</item>
/// </list>
/// </remarks>
public sealed class TypeRingViewModel : INotifyPropertyChanged
{
    private IImmutableBrush? _brush;

    /// <summary>造环时用到的**逐段颜色**（改色后与之一比就知道要不要重画）。</summary>
    private Color[] _colors;

    /// <summary>创建类型色环。</summary>
    /// <param name="typeKeys">
    /// 该模块声明端口承载的数据类型标识（已去重、已排序）；空 = 没有类型（不画环）。
    /// </param>
    public TypeRingViewModel(IReadOnlyList<string> typeKeys)
    {
        ArgumentNullException.ThrowIfNull(typeKeys);

        TypeKeys = typeKeys;
        Signature = string.Join(',', typeKeys);
        _colors = ResolveColors(typeKeys);
        _brush = BuildBrush(_colors);
    }

    /// <inheritdoc />
    public event PropertyChangedEventHandler? PropertyChanged;

    /// <summary>参与判等的**身份签名**（类型标识按序用逗号连接）。</summary>
    /// <remarks>只用于相等判定与调试；界面不显示它（显示类型标识会挤占行宽）。</remarks>
    public string Signature { get; }

    /// <summary>该模块承载的数据类型标识（按序）。</summary>
    public IReadOnlyList<string> TypeKeys { get; }

    /// <summary>是否画环（没有类型的模块保留图块自带的中性边框，不画"某种类型"的环）。</summary>
    public bool HasRing => TypeKeys.Count > 0;

    /// <summary>环分成几段（无类型为 0；单类型为 1；多类型按类型数等分）。</summary>
    public int SegmentCount => TypeKeys.Count;

    /// <summary>环的画刷（无类型为 <see langword="null"/>）。</summary>
    public IImmutableBrush? Brush => _brush;

    /// <inheritdoc />
    public override bool Equals(object? obj) =>
        obj is TypeRingViewModel other && string.Equals(Signature, other.Signature, StringComparison.Ordinal);

    /// <inheritdoc />
    public override int GetHashCode() => StringComparer.Ordinal.GetHashCode(Signature);

    /// <summary>
    /// **用户改色后重新取色**：同一组类型标识重新查一次色板，颜色真的变了才换画刷并发通知。
    /// </summary>
    /// <remarks>
    /// 改色会写 <see cref="PacketTypePalette"/> 的覆盖表，而色环缓存的是**已算好的画刷**
    /// ⇒ 不重取就仍显示旧色（表现为"改了颜色，清单里没变"）。
    /// 没改时逐段颜色与原来完全相同 ⇒ 不重建画刷、不发通知、不重绘（清单按刷新节拍刷新，
    /// 每拍都重建画刷会是持续的分配与重绘）。
    /// </remarks>
    public void RefreshBrush()
    {
        var colors = ResolveColors(TypeKeys);
        if (colors.Length == _colors.Length)
        {
            var same = true;
            for (var index = 0; index < colors.Length; index++)
            {
                if (colors[index] != _colors[index])
                {
                    same = false;
                    break;
                }
            }

            if (same)
            {
                return;
            }
        }

        _colors = colors;
        _brush = BuildBrush(colors);
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Brush)));
    }

    /// <summary>按类型标识逐段取色（顺序与 <paramref name="typeKeys"/> 一致）。</summary>
    /// <param name="typeKeys">类型标识（按序）。</param>
    /// <returns>每段一色；没有类型时为空数组。</returns>
    private static Color[] ResolveColors(IReadOnlyList<string> typeKeys)
    {
        if (typeKeys.Count == 0)
        {
            return [];
        }

        var colors = new Color[typeKeys.Count];
        for (var index = 0; index < typeKeys.Count; index++)
        {
            colors[index] = PacketTypePalette.ColorFor(typeKeys[index]);
        }

        return colors;
    }

    /// <summary>按逐段颜色造画刷：0 段 ⇒ 无；1 段 ⇒ 纯色；N 段 ⇒ 横向等分的硬切换渐变。</summary>
    /// <param name="colors">每段颜色（按序）。</param>
    /// <returns>不可变画刷；没有类型时为 <see langword="null"/>。</returns>
    private static IImmutableBrush? BuildBrush(Color[] colors)
    {
        if (colors.Length == 0)
        {
            return null;
        }

        if (colors.Length == 1)
        {
            return new ImmutableSolidColorBrush(colors[0]);
        }

        // 每段占 **两** 个停靠点（起点 + 紧挨着的终点），相邻两段在同一条分界线上切换
        // ⇒ 视觉上是"硬分界"而不是渐变过渡。
        var stops = new ImmutableGradientStop[colors.Length * 2];
        var step = 1d / colors.Length;

        for (var index = 0; index < colors.Length; index++)
        {
            var start = index * step;
            var end = index == colors.Length - 1 ? 1d : start + step;

            stops[index * 2] = new ImmutableGradientStop(start, colors[index]);
            stops[(index * 2) + 1] = new ImmutableGradientStop(end, colors[index]);
        }

        return new ImmutableLinearGradientBrush(
            stops,
            opacity: 1,
            transform: null,
            transformOrigin: null,
            spreadMethod: GradientSpreadMethod.Pad,
            startPoint: new RelativePoint(0, 0.5, RelativeUnit.Relative),
            endPoint: new RelativePoint(1, 0.5, RelativeUnit.Relative));
    }
}
