using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using HYWDebugAssistant.Ui.Geometry;

namespace HYWDebugAssistant.Ui.Controls;

/// <summary>
/// 信息类层绘制的公共基类：**只负责视觉、不承担逻辑**（05 §5.2）。
/// </summary>
/// <remarks>
/// 本类与派生类**不读取框架对象**：所有几何都由视图模型算好后传进来（纯数值），
/// 因此层可以随时开关/重绘而不触碰数据面与模块。
/// </remarks>
public abstract class LayerControlBase : Control
{
    /// <summary>状态 → 画刷（颜色 + 形状双重编码，09 §7）。</summary>
    /// <param name="state">状态语义。</param>
    protected IBrush? BrushFor(StateVisual state) => state switch
    {
        StateVisual.Active => OkBrush,
        StateVisual.Warn => WarnBrush,
        StateVisual.Dangling => WarnBrush,
        StateVisual.Error => ErrorBrush,
        StateVisual.Placeholder => WarnBrush,
        _ => IdleBrush,
    };

    /// <summary>正常/活动色（semantic.ok）。</summary>
    public static readonly StyledProperty<IBrush?> OkBrushProperty =
        AvaloniaProperty.Register<LayerControlBase, IBrush?>(nameof(OkBrush));

    /// <summary>空闲/停流色（semantic.idle）。</summary>
    public static readonly StyledProperty<IBrush?> IdleBrushProperty =
        AvaloniaProperty.Register<LayerControlBase, IBrush?>(nameof(IdleBrush));

    /// <summary>告警色（semantic.warn）。</summary>
    public static readonly StyledProperty<IBrush?> WarnBrushProperty =
        AvaloniaProperty.Register<LayerControlBase, IBrush?>(nameof(WarnBrush));

    /// <summary>故障色（semantic.error）。</summary>
    public static readonly StyledProperty<IBrush?> ErrorBrushProperty =
        AvaloniaProperty.Register<LayerControlBase, IBrush?>(nameof(ErrorBrush));

    /// <summary>次级强调色（accent.secondary；信息类层区分用）。</summary>
    public static readonly StyledProperty<IBrush?> AccentBrushProperty =
        AvaloniaProperty.Register<LayerControlBase, IBrush?>(nameof(AccentBrush));

    /// <summary>主色（accent.primary；辅助线/选中）。</summary>
    public static readonly StyledProperty<IBrush?> PrimaryBrushProperty =
        AvaloniaProperty.Register<LayerControlBase, IBrush?>(nameof(PrimaryBrush));

    /// <summary>主文本色。</summary>
    public static readonly StyledProperty<IBrush?> TextBrushProperty =
        AvaloniaProperty.Register<LayerControlBase, IBrush?>(nameof(TextBrush));

    /// <summary>弱化文本色。</summary>
    public static readonly StyledProperty<IBrush?> MutedTextBrushProperty =
        AvaloniaProperty.Register<LayerControlBase, IBrush?>(nameof(MutedTextBrush));

    /// <summary>正常/活动色。</summary>
    public IBrush? OkBrush
    {
        get => GetValue(OkBrushProperty);
        set => SetValue(OkBrushProperty, value);
    }

    /// <summary>空闲/停流色。</summary>
    public IBrush? IdleBrush
    {
        get => GetValue(IdleBrushProperty);
        set => SetValue(IdleBrushProperty, value);
    }

    /// <summary>告警色。</summary>
    public IBrush? WarnBrush
    {
        get => GetValue(WarnBrushProperty);
        set => SetValue(WarnBrushProperty, value);
    }

    /// <summary>故障色。</summary>
    public IBrush? ErrorBrush
    {
        get => GetValue(ErrorBrushProperty);
        set => SetValue(ErrorBrushProperty, value);
    }

    /// <summary>次级强调色。</summary>
    public IBrush? AccentBrush
    {
        get => GetValue(AccentBrushProperty);
        set => SetValue(AccentBrushProperty, value);
    }

    /// <summary>主色。</summary>
    public IBrush? PrimaryBrush
    {
        get => GetValue(PrimaryBrushProperty);
        set => SetValue(PrimaryBrushProperty, value);
    }

    /// <summary>主文本色。</summary>
    public IBrush? TextBrush
    {
        get => GetValue(TextBrushProperty);
        set => SetValue(TextBrushProperty, value);
    }

    /// <summary>弱化文本色。</summary>
    public IBrush? MutedTextBrush
    {
        get => GetValue(MutedTextBrushProperty);
        set => SetValue(MutedTextBrushProperty, value);
    }

    /// <summary>创建一段可绘制文本（数据区等宽字体，09 §3）。</summary>
    /// <param name="text">文本。</param>
    /// <param name="size">字号。</param>
    /// <param name="brush">画刷。</param>
    /// <param name="monospace">是否使用等宽（数据区）。</param>
    protected static FormattedText CreateText(string text, double size, IBrush? brush, bool monospace = false)
    {
        var typeface = new Typeface(
            monospace ? new FontFamily("Cascadia Mono, Consolas, Courier New, monospace") : FontFamily.Default,
            FontStyle.Normal,
            FontWeight.Normal);

        return new FormattedText(
            text,
            CultureInfo.CurrentCulture,
            FlowDirection.LeftToRight,
            typeface,
            size,
            brush);
    }
}
