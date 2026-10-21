using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.Threading;
using HYWDebugAssistant.Core.Api;

namespace HYWDebugAssistant.Ui.Theming;

/// <summary>
/// **外壳窗口比例**：把"档位字符串"换算为具体尺寸。
/// </summary>
/// <remarks>
/// 比例与"是否固定"都是设置项（默认 <c>9:19.5</c> + 固定）；将来换比例只改这里。
/// </remarks>
public static class ShellLayout
{
    /// <summary>比例档位（设置界面下拉用）。</summary>
    public static readonly IReadOnlyList<string> Aspects = ["9:19.5", "9:16", "16:9"];

    /// <summary>短边基准尺寸（DIP）。</summary>
    public const double BaseShortSide = 396;

    // 外壳窗口的最小尺寸**不在这里**（唯一来源 = 窗口自己的声明）：同一件事两处定义时，"哪个生效"没有答案。

    /// <summary>按档位换算窗口尺寸（短边 = <see cref="BaseShortSide"/>，长边按比例；竖屏/横屏都成立）。</summary>
    /// <param name="aspect">比例档位（如 <c>9:19.5</c>）。</param>
    public static (double Width, double Height) Measure(string? aspect)
    {
        var (w, h) = Parse(aspect);
        var ratio = h / w;   // 长边 / 短边

        return ratio >= 1
            ? (BaseShortSide, Math.Round(BaseShortSide * ratio))
            : (Math.Round(BaseShortSide / ratio), BaseShortSide);
    }

    private static (double Width, double Height) Parse(string? aspect)
    {
        if (!string.IsNullOrWhiteSpace(aspect))
        {
            var parts = aspect.Split(':', StringSplitOptions.TrimEntries);
            if (parts.Length == 2
                && double.TryParse(parts[0], System.Globalization.CultureInfo.InvariantCulture, out var w)
                && double.TryParse(parts[1], System.Globalization.CultureInfo.InvariantCulture, out var h)
                && w > 0
                && h > 0)
            {
                return (w, h);
            }
        }

        return (9, 19.5);   // 默认：手机竖屏常用比例
    }
}
