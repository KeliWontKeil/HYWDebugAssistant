using Avalonia;
using IconControl = HYWDebugAssistant.Sdk.Ui.Controls.Icon;

namespace HYWDebugAssistant.Ui.Controls;

/// <summary>
/// **一个图标**：<see cref="Icon"/> 是键，图形来自 Ui 的图标表（显式 SVG）。
/// </summary>
/// <remarks>
/// <para>
/// 它只是 <see cref="IconControl"/> 的**薄子类**：把 owner 固定成 Ui 程序集，并保留外壳一直用的
/// `Icon=` 属性名（`{x:Static}` / `{Binding AnchorIconKey}` / 目录分类都按这个名字写）。
/// </para>
/// <para>
/// 它是 `Icon` 的子类而**不是** `TextBlock`：尺寸/颜色由 `ui|Glyph.icon`、`Button.rail ui|Glyph.icon`
/// 这些样式给（Avalonia 的类型选择器按精确类型匹配，所以样式必须显式写 `ui|Glyph`）；
/// 缺键由基类显示键名。
/// </para>
/// </remarks>
public sealed class Glyph : IconControl
{
    /// <summary>图标键（见 <see cref="ShellIcons"/>）。</summary>
    public static readonly StyledProperty<string> IconProperty =
        AvaloniaProperty.Register<Glyph, string>(nameof(Icon), string.Empty);

    static Glyph()
    {
        IconProperty.Changed.AddClassHandler<Glyph>(static (glyph, _) => glyph.IconKey = glyph.Icon);
    }

    /// <summary>造一个 Ui 图标（owner = 本类所在程序集）。</summary>
    public Glyph()
    {
        Owner = typeof(Glyph).Assembly;
    }

    /// <summary>图标键（空 = 不画任何东西）。</summary>
    public string Icon
    {
        get => GetValue(IconProperty);
        set => SetValue(IconProperty, value);
    }
}
