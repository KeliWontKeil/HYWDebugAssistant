using Avalonia.Media;

namespace HYWDebugAssistant.Ui.Theming;

/// <summary>
/// **派生规则的种类**（色板契约里 <c>derive.mode</c> 的取值）。
/// </summary>
/// <remarks>
/// "加一个种类 = 加一行枚举 + <see cref="PaletteDerivation"/> 里一处算法 + 契约里挂上它"，
/// **不改调用方**（应用器与设置面板都不认识具体 token）。
/// </remarks>
public enum PaletteDeriveMode
{
    /// <summary>不派生（用户可直接改；未改 = 用色板里的值）。</summary>
    None,

    /// <summary>源色的**淡底**：色取源色，浓度取该支**内置的不透明度**。</summary>
    Tint,

    /// <summary>源色向黑靠 <c>amount</c>（"更重一档"）。</summary>
    Darken,

    /// <summary>源色的**邻近色**：色相按"该支内置色相对源内置色的偏移"平移，饱和/亮度取内置值。</summary>
    Shift,
}

/// <summary>一条派生规则（<c>from</c> 必须是<b>不派生</b>的支 ⇒ 链条只有一层，无环）。</summary>
/// <param name="Mode">规则种类。</param>
/// <param name="Source">源 token 名。</param>
/// <param name="Amount">参数（只有 <see cref="PaletteDeriveMode.Darken"/> 用）。</param>
public sealed record PaletteDeriveRule(PaletteDeriveMode Mode, string Source, double Amount = 0);

/// <summary>
/// **派生算法**（唯一一处）：规则种类 → 值。数据在契约里（"哪一支跟随谁"），算法在这里。
/// </summary>
/// <remarks>
/// <para>
/// 所有规则都只吃**四个输入**：源色的当前值、这一支的内置值、源色的内置值、规则参数。
/// "当前值"可能是内置值、也可能是用户的覆盖 —— 于是"用户改了主题色 ⇒ 派生支跟着变"与
/// "用户没改 ⇒ 派生结果 == 内置值"这两件事**同时成立**（后者由审计钉住，见 `audit-colors` 第③段）。
/// </para>
/// <para>
/// **浓度（不透明度）不在这里**：它对所有规则都是同一个口径 —— 取**该支内置的不透明度**，
/// 由调用方（应用器）装进画刷。
/// </para>
/// </remarks>
public static class PaletteDerivation
{
    /// <summary>算出一支的派生色。</summary>
    /// <param name="rule">规则。</param>
    /// <param name="source">源色**当前**的值。</param>
    /// <param name="builtIn">这一支**内置**的值。</param>
    /// <param name="sourceBuiltIn">源色**内置**的值（`shift` 用它算色相偏移）。</param>
    /// <returns>派生出来的颜色。</returns>
    public static Color Derive(PaletteDeriveRule rule, Color source, Color builtIn, Color sourceBuiltIn)
    {
        ArgumentNullException.ThrowIfNull(rule);

        return rule.Mode switch
        {
            PaletteDeriveMode.Tint => source,
            PaletteDeriveMode.Darken => Darken(source, rule.Amount),
            PaletteDeriveMode.Shift => ShiftHue(source, builtIn, sourceBuiltIn),
            _ => builtIn,
        };
    }

    /// <summary>向黑靠 <paramref name="amount"/>（0–1）。</summary>
    /// <param name="color">源色。</param>
    /// <param name="amount">比例。</param>
    public static Color Darken(Color color, double amount) => Color.FromRgb(
        (byte)Math.Clamp(color.R * (1 - amount), 0, 255),
        (byte)Math.Clamp(color.G * (1 - amount), 0, 255),
        (byte)Math.Clamp(color.B * (1 - amount), 0, 255));

    /// <summary>
    /// **邻近色**：色相 = 源色色相 + 偏移，偏移 = 该支内置色相 − 源内置色相；饱和与亮度取内置值。
    /// </summary>
    /// <param name="source">源色当前值。</param>
    /// <param name="builtIn">该支内置值。</param>
    /// <param name="sourceBuiltIn">源色内置值。</param>
    /// <remarks>
    /// 于是**默认状态下结果 == 内置值**（源没被改 ⇒ 色相平移量正好抵消），
    /// 而源一旦被改，这一支就带着**同样的色相关系**跟着走 —— "第二主题色"由此得来，
    /// 且它的"关系"由色板里的那一对默认值定义（不是代码里另写一个角度）。
    /// <para>
    /// **HSL 换算自己算**（`H` 0–360、`S`/`L` 0–1）：`Avalonia.Media.HslColor` 的构造与
    /// `Color.ToHsl()` **约定不一致**（实测：把 `ToHsl()` 的三个分量原样交给构造函数回代，
    /// 得到的是**白色**）—— 猜第三方的约定不如自己写这二十行。
    /// </para>
    /// </remarks>
    public static Color ShiftHue(Color source, Color builtIn, Color sourceBuiltIn)
    {
        var offset = Hue(builtIn) - Hue(sourceBuiltIn);
        var (_, saturation, lightness) = ToHsl(builtIn);

        return FromHsl(NormalizeHue(Hue(source) + offset), saturation, lightness);
    }

    /// <summary>色相（0–360）。</summary>
    private static double Hue(Color color) => ToHsl(color).H;

    /// <summary>RGB → HSL（`H` 0–360、`S`/`L` 0–1）。</summary>
    private static (double H, double S, double L) ToHsl(Color color)
    {
        var r = color.R / 255.0;
        var g = color.G / 255.0;
        var b = color.B / 255.0;

        var max = Math.Max(r, Math.Max(g, b));
        var min = Math.Min(r, Math.Min(g, b));
        var lightness = (max + min) / 2;
        var delta = max - min;

        if (delta == 0)
        {
            return (0, 0, lightness);
        }

        var saturation = lightness > 0.5 ? delta / (2 - max - min) : delta / (max + min);

        var hue = max == r
            ? ((g - b) / delta) + (g < b ? 6 : 0)
            : max == g
                ? ((b - r) / delta) + 2
                : ((r - g) / delta) + 4;

        return (hue * 60, saturation, lightness);
    }

    /// <summary>HSL → RGB（`H` 0–360、`S`/`L` 0–1）。</summary>
    private static Color FromHsl(double hue, double saturation, double lightness)
    {
        if (saturation <= 0)
        {
            var gray = (byte)Math.Round(Math.Clamp(lightness, 0, 1) * 255);
            return Color.FromRgb(gray, gray, gray);
        }

        var h = NormalizeHue(hue) / 360.0;
        var q = lightness < 0.5 ? lightness * (1 + saturation) : lightness + saturation - (lightness * saturation);
        var p = (2 * lightness) - q;

        return Color.FromRgb(
            Channel(p, q, h + (1.0 / 3)),
            Channel(p, q, h),
            Channel(p, q, h - (1.0 / 3)));

        static byte Channel(double p, double q, double t)
        {
            if (t < 0)
            {
                t += 1;
            }
            else if (t > 1)
            {
                t -= 1;
            }

            var value = t < (1.0 / 6) ? p + ((q - p) * 6 * t)
                : t < (1.0 / 2) ? q
                : t < (2.0 / 3) ? p + ((q - p) * ((2.0 / 3) - t) * 6)
                : p;

            return (byte)Math.Round(Math.Clamp(value, 0, 1) * 255);
        }
    }

    /// <summary>把色相折回 0–360。</summary>
    private static double NormalizeHue(double hue)
    {
        var value = hue % 360;
        return value < 0 ? value + 360 : value;
    }
}
