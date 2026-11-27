using System.Collections.Concurrent;
using Avalonia.Media;
using Avalonia.Media.Immutable;

namespace HYWDebugAssistant.Ui.ViewModels;

/// <summary>
/// **数据包类型 → 颜色** 的调色板（端口标记上的窄颜色条 + 叠加层角色方块的填充）。
/// </summary>
/// <remarks>
/// <list type="bullet">
///   <item><b>键 = 类型标识</b>（`DataRoleView.TypeKey` / `PortView.TypeKey`；未声明类型 ⇒ 结构描述）。
///     **不能用呈现文本** —— `{seq:u64, value:f32}` 会把"不同标识但形状相同"的两种类型涂成同一色，
///     而**角色合并**规则明确二者**不是同一种量** ⇒ 按呈现文本配色会与它自相矛盾；</item>
///   <item><b>默认 = 稳定哈希配色</b>：同一 `TypeKey` **恒得同一色**（跨模块、跨进程、跨重启一致），
///     用户不必维护任何表就能分辨类型。哈希用 **FNV-1a**（<c>string.GetHashCode</c> **每个进程都变**
///     ⇒ 每次启动颜色都不同，绝对不能用）；</item>
///   <item><b>覆盖表（接口，只留口子）</b>：日后在侧边栏的「**数据包类型表**」里手动指定颜色时，
///     经 <see cref="SetOverride"/> 填入即可覆盖哈希色。**映射入口只有本类**：端口标记与角色方块共用它，
///     所以两处颜色**永远一致**；</item>
///   <item><b>静态画刷必须是"不可变"画刷</b>：<see cref="SolidColorBrush"/> 是 <c>AvaloniaObject</c>，
///     **有线程亲和性**——若它在 A 线程被创建，渲染线程读它就会抛
///     <c>The calling thread cannot access this object because a different thread owns it</c>
///     （本类是静态单例、会被任意线程首次触碰 —— 这个坑踩过）；</item>
///   <item><b>可读性</b>：叠加层是**深色玻璃**（`OverlayScrim`），故自动配色固定取"中等饱和 + 较高亮度"，
///     保证浅色文字压在其上可读；浅/深主题各配一套是**产品决策**，留待「数据包类型表」落地时再定。</item>
/// </list>
/// </remarks>
public static class PacketTypePalette
{
    /// <summary>
    /// **用户覆盖表**（键 = 类型身份）：只提供接口，侧边栏「数据包类型表」落地时直接填这里。
    /// </summary>
    private static readonly ConcurrentDictionary<string, IBrush> Overrides = new(StringComparer.Ordinal);

    /// <summary>自动配色的缓存（同一键复用同一画刷：颜色稳定 + 不做重复计算）。</summary>
    private static readonly ConcurrentDictionary<string, IBrush> Auto = new(StringComparer.Ordinal);

    /// <summary>
    /// **未映射类型**的占位色（空键 / 未知）：中性灰蓝，刻意保持中性。
    /// </summary>
    /// <remarks>
    /// **绝对色**：本类整支都是"按类型标识算出来的颜色"（见 <see cref="AutoColor"/>），
    /// 它**不属于主题的命名色板** —— 主题管"界面观感"，这里管"数据身份"。
    /// 用户可以在「数据包类型表」里逐键覆盖（<see cref="Overrides"/>）。
    /// <para>
    /// 占位色的**文本形式**（`#RRGGBB`）就是 <see cref="PlaceholderHex"/>：设置面板写回设置时也用它
    /// —— 同一个色值只写一处（以前它在这个类与设置面板的兜底里各写了一遍）。
    /// </para>
    /// </remarks>
    public const string PlaceholderHex = "#9AA4B2";

    /// <summary>占位色的**颜色形式**（与 <see cref="Placeholder"/> 同一个值；需要停靠点时用它）。</summary>
    public static Color PlaceholderColor { get; } = Color.Parse(PlaceholderHex);

    /// <summary>占位画刷（<see cref="PlaceholderColor"/> 的不可变单色版）。</summary>
    public static IBrush Placeholder { get; } = new ImmutableSolidColorBrush(PlaceholderColor);

    /// <summary>自动配色的饱和度（0–1）：中等饱和 —— 深色玻璃上不刺眼、彼此可分。</summary>
    public const double AutoSaturation = 0.52;

    /// <summary>自动配色的亮度（0–1）：偏高亮度 —— 保证深色玻璃上的浅色文字与方块边线都看得清。</summary>
    public const double AutoLightness = 0.63;

    /// <summary>
    /// 取某个**数据类型**对应的颜色：① 用户覆盖 ⇒ 用它；② 否则**稳定哈希自动配色**；③ 空键 ⇒ 占位色。
    /// </summary>
    /// <param name="typeKey">
    /// 类型标识（未声明类型 ⇒ 结构描述；空/未知 ⇒ 占位色）。
    /// </param>
    /// <returns>颜色条 / 角色方块填充用的画刷。</returns>
    public static IBrush For(string? typeKey) => typeKey is { Length: > 0 } key
        ? Overrides.TryGetValue(key, out var overridden)
            ? overridden
            : Auto.GetOrAdd(key, static k => new ImmutableSolidColorBrush(AutoColor(k)))
        : Placeholder;

    /// <summary>
    /// 取某个**数据类型**对应的颜色（<see cref="For"/> 的"颜色"版：需要停靠点/渐变时必须用它）。
    /// </summary>
    /// <param name="typeKey">
    /// 类型标识（未声明类型 ⇒ 结构描述；空/未知 ⇒ 占位色）。
    /// </param>
    /// <returns>该类型对应的颜色。</returns>
    /// <remarks>
    /// **与 <see cref="For"/> 同源**：覆盖色优先、否则自动配色、空键占位 —— 两处若各算一遍，
    /// 清单的环与端口标记的窄色条就会在"用户改过色"时不一致。
    /// <para>
    /// 覆盖表只经 <see cref="SetOverride"/> 写入（存的都是不可变**单色**画刷），
    /// 因此"有覆盖 ⇒ 取它的颜色"；万一将来放进非单色画刷，则回落自动配色而不是抛异常。
    /// </para>
    /// </remarks>
    public static Color ColorFor(string? typeKey)
    {
        if (typeKey is not { Length: > 0 })
        {
            return PlaceholderColor;
        }

        if (Overrides.TryGetValue(typeKey, out var overridden) && overridden is ISolidColorBrush solid)
        {
            return solid.Color;
        }

        return AutoColor(typeKey);
    }

    /// <summary>设置一个**用户覆盖色**（"数据包类型表"用；同一键重复设置即覆盖）。</summary>
    /// <param name="typeKey">类型身份。</param>
    /// <param name="color">颜色。</param>
    public static void SetOverride(string typeKey, Color color)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(typeKey);
        Overrides[typeKey] = new ImmutableSolidColorBrush(color);
    }

    /// <summary>移除一个用户覆盖色（回落自动配色）。</summary>
    /// <param name="typeKey">类型身份。</param>
    /// <returns>是否确实移除了。</returns>
    public static bool RemoveOverride(string typeKey) =>
        !string.IsNullOrWhiteSpace(typeKey) && Overrides.TryRemove(typeKey, out _);

    /// <summary>清空全部用户覆盖色（测试与"恢复默认"用）。</summary>
    public static void ClearOverrides() => Overrides.Clear();

    /// <summary>当前用户覆盖色的键（供"类型表"呈现；不暴露画刷实例本身）。</summary>
    public static IReadOnlyCollection<string> OverriddenKeys => [.. Overrides.Keys];

    /// <summary>
    /// **稳定哈希配色**（纯函数）：同一键恒得同一色。哈希 = FNV-1a（不随进程变化）；
    /// 色相由哈希决定，饱和度/亮度取固定值（见 <see cref="AutoSaturation"/> / <see cref="AutoLightness"/>）。
    /// </summary>
    /// <param name="typeKey">类型身份（非空）。</param>
    /// <returns>该类型的自动配色。</returns>
    public static Color AutoColor(string typeKey)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(typeKey);

        // FNV-1a（32 位）：稳定、无分配、与运行时无关
        var hash = 2166136261u;
        foreach (var b in System.Text.Encoding.UTF8.GetBytes(typeKey))
        {
            hash ^= b;
            hash *= 16777619u;
        }

        var hue = hash % 360u;
        return FromHsl(hue, AutoSaturation, AutoLightness);
    }

    /// <summary>HSL → RGB（色相 0–360、饱和度/亮度 0–1）。</summary>
    private static Color FromHsl(double hue, double saturation, double lightness)
    {
        var c = (1 - Math.Abs((2 * lightness) - 1)) * saturation;
        var h = hue / 60.0;
        var x = c * (1 - Math.Abs((h % 2) - 1));
        var m = lightness - (c / 2);

        var (r, g, b) = h switch
        {
            < 1 => (c, x, 0.0),
            < 2 => (x, c, 0.0),
            < 3 => (0.0, c, x),
            < 4 => (0.0, x, c),
            < 5 => (x, 0.0, c),
            _ => (c, 0.0, x),
        };

        return Color.FromRgb(
            (byte)Math.Round((r + m) * 255),
            (byte)Math.Round((g + m) * 255),
            (byte)Math.Round((b + m) * 255));
    }
}
