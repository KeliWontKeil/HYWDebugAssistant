using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Styling;
using HYWDebugAssistant.Sdk.Views;
using HYWDebugAssistant.Ui.Theming;

namespace HYWDebugAssistant.Ui.ModuleSurface.Theme;

/// <summary>
/// **表面主题** —— 模块内容区里数值字面量的唯一读出点。
/// </summary>
/// <remarks>
/// <para>
/// 规则：<c>ModuleSurface</c> 下的任何排布/外观数值**不得**以内联字面量出现在代码里，
/// 只能经由本类按 key 读出。<see cref="ModuleSurfacePanel"/> 与各表面控件都只用它。
/// </para>
/// <para>
/// 键来自 <c>ModuleSurface/Theme/ModuleSurfaceMetrics.axaml</c>（**尺寸**）与
/// <c>Styles/Tokens.axaml</c>（**颜色**：模块表面那几支住色板的"模块表面"分区）；
/// 两份都由 App 与快照测试宿主**各自显式合并**（两边都要挂，缺一边快照测试就会大声失败 —— 这是刻意的）。
/// </para>
/// <para>
/// 每个读取都带 <paramref name="fallback"/>：资源缺失时不静默取 0（那会让布局看起来"没生效"
/// 却毫无线索），而是取一个**与主题缺省相同的**值继续跑 —— 于是缺资源只表现为"主题改动不生效"，
/// 而不会让界面塌掉。缺资源本身由 T3 的门禁与快照测试负责发现。
/// </para>
/// </remarks>
public static class SurfaceTheme
{
    /// <summary>
    /// 把声明里的颜色字面量（<c>#RRGGBB</c> / <c>#AARRGGBB</c>）解析成颜色。
    /// </summary>
    /// <param name="literal">声明里的字面量；空串 = 没写。</param>
    /// <param name="color">解析结果。</param>
    /// <returns>解析成功为 <see langword="true"/>。</returns>
    /// <remarks>
    /// 字面量的**合法性**在读取器里就判过了（不合法是硬伤）；这里只做"把它变成颜色"这一件事，
    /// 解析不了时返回 <see langword="false"/> 让调用方保留主题给的那一份，而不是画成一个黑色。
    /// </remarks>
    public static bool TryParseColor(string literal, out Color color)
    {
        color = default;

        if (string.IsNullOrWhiteSpace(literal))
        {
            return false;
        }

        var digits = literal.StartsWith('#') ? literal[1..] : literal;

        if (digits.Length is 6)
        {
            digits = "FF" + digits;   // 不写 alpha ⇒ 不透明
        }

        if (digits.Length is not 8 || !uint.TryParse(digits, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var value))
        {
            return false;
        }

        color = Color.FromUInt32(value);
        return true;
    }

    /// <summary>
    /// 内容区四周的预留带（DIP）—— 挡住叠在内容之上的端口标记与"运行指示"。
    /// </summary>
    /// <param name="target">取资源的作用域。</param>
    /// <remarks>
    /// 上/下比左右厚（旧值 <c>20,22,20,24</c>）：见 <c>ModuleSurfaceMetrics.axaml</c> 里
    /// <c>SurfaceContentInset*</c> 的说明。**它必须落在 <c>ScrollViewer</c> 之外的 <c>Margin</c> 上**，
    /// 落在 <c>Padding</c> 上会让内容恒大于视口 ⇒ 默认尺寸下常驻滚动条。
    /// </remarks>
    public static Thickness ContentInset(Visual target) => new(
        Dip(target, "SurfaceContentInsetLeft", 20),
        Dip(target, "SurfaceContentInsetTop", 22),
        Dip(target, "SurfaceContentInsetRight", 20),
        Dip(target, "SurfaceContentInsetBottom", 24));

    /// <summary>标题字号（px）。</summary>
    /// <param name="target">取资源的作用域。</param>
    public static double TitleFontSize(Visual target) => Dip(target, "SurfaceTitleFontSize", 11);

    /// <summary>
    /// 文本面的行高（DIP）—— **文本层**（分页窗口 / 编辑器本身）唯一的行高来源。
    /// </summary>
    /// <param name="target">取资源的作用域。</param>
    /// <remarks>
    /// 它同时决定两件事：编辑器自己的行高，以及"视口放得下几行"（⇒ 窗口行数）。
    /// 两处各写一个数时，改字号的那个会把末行裁掉，而屏幕上只表现为"最后一行看不见"。
    /// </remarks>
    public static double TextLineHeight(Visual target) => Dip(target, TextLineHeightKey, 14);

    /// <summary>曲线绘图区四周内边距（DIP）—— 只剩框线那 1px（"框 = 内容"标准，见 `排布与坐标.md` §1.3）。</summary>
    /// <param name="target">取资源的作用域。</param>
    public static double PlotInset(Visual target) => Dip(target, "SurfacePlotInset", 1);

    /// <summary>曲线最小高（DIP）—— 它是**内容下限**：声明比它还矮时绘图区放不下（标准 S4 要把它报出来）。</summary>
    /// <param name="target">取资源的作用域。</param>
    public static double CurveMinHeight(Visual target) => Dip(target, "SurfaceCurveMinHeight", 64);

    /// <summary>曲线绘图区底色。</summary>
    /// <param name="target">取资源的作用域。</param>
    /// <remarks>资源缺失时回退到透明（"没有底色"）而不是黑色 —— 宁可少一层底色，也不要一整块黑。</remarks>
    public static IBrush CurvePlotBrush(Visual target) => Brush(target, CurvePlotBrushKey, Brushes.Transparent);

    /// <summary>曲线外框色。</summary>
    /// <param name="target">取资源的作用域。</param>
    public static IBrush CurveFrameBrush(Visual target) => Brush(target, CurveFrameBrushKey, Brushes.Transparent);

    /// <summary>曲线线色。</summary>
    /// <param name="target">取资源的作用域。</param>
    public static IBrush CurveStrokeBrush(Visual target) => Brush(target, CurveStrokeBrushKey, Brushes.Transparent);

    /// <summary>仅登记控件的占位字号（px）。</summary>
    /// <param name="target">取资源的作用域。</param>
    public static double PlaceholderFontSize(Visual target) => Dip(target, "SurfacePlaceholderFontSize", 10);

    /// <summary>说明文字与主体之间的间距（仅**上下堆叠**档用）的**资源键名**。</summary>
    /// <remarks>
    /// 并排档**不用**它：那时文字与主体同一行盒，间距由两列各自的宽度决定 ——
    /// 再用边距会把它顶高半个边距（"框与文字没对齐"）。
    /// </remarks>
    public const string LabelGapKey = "SurfaceLabelGap";

    /// <summary>标题字号的**资源键名**（给要写 <c>DynamicResource</c> 的 XAML/控件用；值见 <see cref="TitleFontSize"/>）。</summary>
    public const string TitleFontSizeKey = "SurfaceTitleFontSize";

    /// <summary>文本面行高的**资源键名**（值见 <see cref="TextLineHeight"/>）。</summary>
    public const string TextLineHeightKey = "SurfaceTextLineHeight";

    /// <summary>纯文本说明字号的资源键名（<c>label</c>；不给它会回落到 Fluent 缺省，比别处大一圈）。</summary>
    public const string LabelFontSizeKey = "SurfaceLabelFontSize";

    /// <summary>结果行字号的资源键名。</summary>

    /// <summary>结果行常驻最小高的资源键名。</summary>

    /// <summary>占位字号的资源键名。</summary>
    public const string PlaceholderFontSizeKey = "SurfacePlaceholderFontSize";

    /// <summary>分组框前景色的资源键名（**引全局角色**：`TextSecondary`；本类不再自带一份拷贝）。</summary>
    public const string ContainerTitleBrushKey = "TextSecondary";

    /// <summary>分组框边框色的资源键名（引全局角色 `BorderDefault`）。</summary>
    public const string ContainerBorderBrushKey = "BorderDefault";

    /// <summary>曲线绘图区底色的资源键名（引全局角色 `BgWorkspace`）。</summary>
    public const string CurvePlotBrushKey = "BgWorkspace";

    /// <summary>曲线外框色的资源键名（引全局角色 `BorderDefault`）。</summary>
    public const string CurveFrameBrushKey = "BorderDefault";

    /// <summary>曲线线色的资源键名（引全局角色 `AccentSecondary` ⇒ **改主题色时跟着变**）。</summary>
    public const string CurveStrokeBrushKey = "AccentSecondary";

    /// <summary>
    /// **角色词 → 主题键**（**只有这一处映射**：声明解析与运行期角色解析共用它）。
    /// </summary>
    /// <remarks>
    /// 词表本体在 <see cref="ViewInkRoles"/>（`Sdk`）；**哪个词承担哪一支 token** 由色板契约给
    /// （`Styles/palette.json` 里每一支的 <c>roles</c>）—— 这里不再手抄第二张表（手抄必然与契约漂，
    /// 而"两边是否一致"由 `audit-colors` 钉住）。
    /// </remarks>
    /// <param name="word">角色词。</param>
    /// <param name="key">主题键（token 名）。</param>
    public static bool TryRoleKey(string? word, out string key) => PaletteManifest.TryRoleKey(word, out key);

    /// <summary>取一个 DIP 数值资源；缺失或类型不符 ⇒ <paramref name="fallback"/>。</summary>
    /// <param name="target">取资源的作用域。</param>
    /// <param name="key">资源键。</param>
    /// <param name="fallback">缺失时的回退值。</param>
    public static double Dip(Visual target, string key, double fallback)
    {
        ArgumentNullException.ThrowIfNull(target);

        var scope = target as IResourceHost;
        return scope?.TryFindResource(key, out var value) == true && value is double dip ? dip : fallback;
    }

    /// <summary>取一个画刷资源；缺失 ⇒ <paramref name="fallback"/>。</summary>
    /// <param name="target">取资源的作用域。</param>
    /// <param name="key">资源键。</param>
    /// <param name="fallback">缺失时的回退刷。</param>
    public static IBrush Brush(Visual target, string key, IBrush fallback)
    {
        ArgumentNullException.ThrowIfNull(target);

        var scope = target as IResourceHost;
        return scope?.TryFindResource(key, out var value) == true && value is IBrush brush ? brush : fallback;
    }

    /// <summary>取一个字体资源；缺失 ⇒ <paramref name="fallback"/>。</summary>
    /// <param name="target">取资源的作用域。</param>
    /// <param name="key">资源键。</param>
    /// <param name="fallback">缺失时的回退字体。</param>
    public static FontFamily Font(Visual target, string key, FontFamily fallback)
    {
        ArgumentNullException.ThrowIfNull(target);

        var scope = target as IResourceHost;
        return scope?.TryFindResource(key, out var value) == true && value is FontFamily font ? font : fallback;
    }
}
