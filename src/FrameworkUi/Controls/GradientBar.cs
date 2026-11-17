using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;

namespace HYWDebugAssistant.FrameworkUi.Controls;

/// <summary>
/// **一条可直接拖动的渐变条**（调色板专用）。
/// </summary>
/// <remarks>
/// <para>
/// 刻意不继承/复用普通 <see cref="Slider"/>：默认滑块主题会带轨道端点、圆形拇指，
/// 在紧凑调色板里会被看成多余的滚动条。这里只画"渐变矩形 + 2 DIP 指示线"。
/// </para>
/// <para>
/// 指示线由 1px 黑边 + 1px 白边并排组成：既保持 2 像素的简单观感，又能在任何渐变上看见。
/// 颜色值本身由调用方通过 <see cref="Gradient"/> 给入；控件不内置主题色。
/// </para>
/// </remarks>
public sealed class GradientBar : Control
{
    /// <summary>渐变画刷。</summary>
    public static readonly StyledProperty<IBrush?> GradientProperty =
        AvaloniaProperty.Register<GradientBar, IBrush?>(nameof(Gradient));

    /// <summary>外框颜色；主题缺失时用灰色兜底。</summary>
    public static readonly StyledProperty<IBrush?> BorderBrushProperty =
        AvaloniaProperty.Register<GradientBar, IBrush?>(nameof(BorderBrush));

    /// <summary>当前值。</summary>
    public static readonly StyledProperty<double> ValueProperty =
        AvaloniaProperty.Register<GradientBar, double>(
            nameof(Value),
            defaultBindingMode: Avalonia.Data.BindingMode.TwoWay);

    /// <summary>最小值。</summary>
    public static readonly StyledProperty<double> MinimumProperty =
        AvaloniaProperty.Register<GradientBar, double>(nameof(Minimum));

    /// <summary>最大值。</summary>
    public static readonly StyledProperty<double> MaximumProperty =
        AvaloniaProperty.Register<GradientBar, double>(nameof(Maximum), 1d);

    /// <summary>方向：横向为左→右，纵向为下→上。</summary>
    public static readonly StyledProperty<Orientation> OrientationProperty =
        AvaloniaProperty.Register<GradientBar, Orientation>(
            nameof(Orientation),
            Orientation.Horizontal);

    /// <summary>
    /// 反向：为 <c>true</c> 时横向变为右→左、纵向变为上→下。
    /// 调色板的 Value 条用它表达"顶部最暗、底部最亮"。
    /// </summary>
    public static readonly StyledProperty<bool> InvertProperty =
        AvaloniaProperty.Register<GradientBar, bool>(nameof(Invert));

    private bool _dragging;

    /// <summary>创建渐变条。</summary>
    public GradientBar()
    {
        Focusable = false;
        ClipToBounds = true;
    }

    /// <summary>渐变画刷。</summary>
    public IBrush? Gradient
    {
        get => GetValue(GradientProperty);
        set => SetValue(GradientProperty, value);
    }

    /// <summary>外框颜色。</summary>
    public IBrush? BorderBrush
    {
        get => GetValue(BorderBrushProperty);
        set => SetValue(BorderBrushProperty, value);
    }

    /// <summary>当前值。</summary>
    public double Value
    {
        get => GetValue(ValueProperty);
        set => SetValue(ValueProperty, value);
    }

    /// <summary>最小值。</summary>
    public double Minimum
    {
        get => GetValue(MinimumProperty);
        set => SetValue(MinimumProperty, value);
    }

    /// <summary>最大值。</summary>
    public double Maximum
    {
        get => GetValue(MaximumProperty);
        set => SetValue(MaximumProperty, value);
    }

    /// <summary>横向或纵向。</summary>
    public Orientation Orientation
    {
        get => GetValue(OrientationProperty);
        set => SetValue(OrientationProperty, value);
    }

    /// <summary>是否反向（横向右→左、纵向上→下）。</summary>
    public bool Invert
    {
        get => GetValue(InvertProperty);
        set => SetValue(InvertProperty, value);
    }

    /// <summary>值变化（由用户拖动或代码赋值都会触发；调用方用抑制标志避免回环）。</summary>
    public event EventHandler? ValueChanged;

    /// <inheritdoc />
    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);

        if (change.Property == ValueProperty)
        {
            InvalidateVisual();
            ValueChanged?.Invoke(this, EventArgs.Empty);
        }
        else if (change.Property == GradientProperty
                 || change.Property == BorderBrushProperty
                 || change.Property == OrientationProperty
                 || change.Property == InvertProperty
                 || change.Property == MinimumProperty
                 || change.Property == MaximumProperty)
        {
            InvalidateVisual();
        }
    }

    /// <inheritdoc />
    public override void Render(DrawingContext context)
    {
        base.Render(context);

        var bounds = new Rect(Bounds.Size);
        if (bounds.Width <= 0 || bounds.Height <= 0)
        {
            return;
        }

        var body = bounds.Deflate(0.5);

        if (Gradient is { } gradient)
        {
            context.DrawRectangle(gradient, null, body, 2, 2);
        }

        // 绝对色：主题缺失/外框未设时的中性边框兜底。
        var border = BorderBrush ?? Brushes.Gray;
        context.DrawRectangle(null, new Pen(border, 1), body, 2, 2);

        var fraction = Range <= 0 ? 0 : Math.Clamp((Value - Minimum) / Range, 0, 1);

        // 绝对色：黑色半像素与白色半像素并排，保证在亮/暗渐变上都看得见；
        // 总厚度 2 DIP，不画圆点或额外装饰。
        var black = new Pen(Brushes.Black, 1);
        var white = new Pen(Brushes.White, 1);

        if (Orientation == Orientation.Horizontal)
        {
            var x = body.Left + ((Invert ? 1 - fraction : fraction) * body.Width);
            x = Math.Clamp(x, body.Left + 1, body.Right - 1);
            context.DrawLine(black, new Point(x - 0.5, body.Top), new Point(x - 0.5, body.Bottom));
            context.DrawLine(white, new Point(x + 0.5, body.Top), new Point(x + 0.5, body.Bottom));
        }
        else
        {
            var y = Invert
                ? body.Top + (fraction * body.Height)
                : body.Bottom - (fraction * body.Height);
            y = Math.Clamp(y, body.Top + 1, body.Bottom - 1);
            context.DrawLine(black, new Point(body.Left, y - 0.5), new Point(body.Right, y - 0.5));
            context.DrawLine(white, new Point(body.Left, y + 0.5), new Point(body.Right, y + 0.5));
        }
    }

    private double Range => Maximum - Minimum;

    /// <inheritdoc />
    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);

        if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
        {
            return;
        }

        _dragging = true;
        e.Pointer.Capture(this);
        SetValueFromPoint(e.GetPosition(this));
        e.Handled = true;
    }

    /// <inheritdoc />
    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);

        if (!_dragging)
        {
            return;
        }

        SetValueFromPoint(e.GetPosition(this));
        e.Handled = true;
    }

    /// <inheritdoc />
    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);

        if (!_dragging)
        {
            return;
        }

        _dragging = false;
        e.Pointer.Capture(null);
        e.Handled = true;
    }

    /// <inheritdoc />
    protected override void OnPointerCaptureLost(PointerCaptureLostEventArgs e)
    {
        base.OnPointerCaptureLost(e);
        _dragging = false;
    }

    private void SetValueFromPoint(Point point)
    {
        if (Range <= 0 || Bounds.Width <= 0 || Bounds.Height <= 0)
        {
            return;
        }

        var fraction = Orientation == Orientation.Horizontal
            ? Math.Clamp(point.X / Bounds.Width, 0, 1)
            : Math.Clamp(Invert ? point.Y / Bounds.Height : 1 - (point.Y / Bounds.Height), 0, 1);

        if (Orientation == Orientation.Horizontal && Invert)
        {
            fraction = 1 - fraction;
        }

        Value = Minimum + (fraction * Range);
    }
}
