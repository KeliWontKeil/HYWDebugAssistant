using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Layout;
using HYWDebugAssistant.Sdk.Views;
using HYWDebugAssistant.Ui.Controls;
using HYWDebugAssistant.Ui.ModuleSurface.Layout;
using HYWDebugAssistant.Ui.ModuleSurface.Theme;

namespace HYWDebugAssistant.Ui.ModuleSurface.Controls;

/// <summary>
/// **数据驱动控件的底座** —— 从某个数据源读值、把结果写进 <see cref="SurfaceElementControl.State"/>。
/// </summary>
/// <remarks>
/// 它认识的是"我绑了哪个源"（<see cref="Source"/>）与"怎么取数"（<see cref="Host"/>），
/// **不认识模块、不认识服务、也不认识别的控件类型**。
/// </remarks>
public abstract class SurfaceSourceControl : SurfaceElementControl
{
    private readonly ISurfaceHost _host;

    /// <summary>造一个绑数据源的控件。</summary>
    /// <param name="node">声明节点。</param>
    /// <param name="state">数据面。</param>
    /// <param name="host">表面宿主（取数经它）。</param>
    protected SurfaceSourceControl(ViewNode node, SurfaceElementState state, ISurfaceHost host)
        : base(node, state)
    {
        ArgumentNullException.ThrowIfNull(host);
        _host = host;
        Source = node.DataSource ?? string.Empty;

        // **读数这块文字的外观**：主文字的键（`fontSize` / `textColor`）说了算 ——
        // 声明是初值、运行期可覆盖、都没有则回主题。
        ApplyLook(Readout, MainTextLook());
    }

    /// <inheritdoc />
    protected override void OnMainTextLookChanged() => ApplyLook(Readout, MainTextLook());

    /// <summary>本控件绑的数据源名（声明面 <c>data</c>；空 = 没绑）。</summary>
    public string Source { get; }

    /// <summary>表面宿主。</summary>
    protected ISurfaceHost Host => _host;

    /// <summary>取最新读数（宿主给的是"这个实例 + 这个源"的读法）。</summary>
    /// <returns>最新读数；无值 ⇒ <c>null</c>。</returns>
    protected double? ReadLatest() => Source.Length == 0 ? null : _host.ReadLatest(Source);

    /// <summary>取序列（宿主给的是"这个实例 + 这个源"的读法）。</summary>
    /// <returns>等间隔降采样后的序列（无值 ⇒ 空）。</returns>
    protected IReadOnlyList<double> ReadSeries() => Source.Length == 0 ? [] : _host.ReadSeries(Source);
}

/// <summary>
/// **数值读数**（<c>numeric</c>）：一行"标题 + 数值"。
/// </summary>
/// <remarks>
/// 一个 kind 一块砖：旧实现里它是内联在模板里的一段 <c>Grid</c>，于是"数值"没有身份 ——
/// 别处想改它的对齐或字体只能去改那张模板。这里它是**一个控件**，
/// 外观全由 <see cref="SurfaceClasses.Numeric"/> 的选择器给。
/// </remarks>
public sealed class SurfaceNumericControl : SurfaceSourceControl
{
    /// <summary>造一个数值读数控件。</summary>
    /// <param name="node">声明节点。</param>
    /// <param name="state">数据面。</param>
    /// <param name="host">表面宿主。</param>
    public SurfaceNumericControl(ViewNode node, SurfaceElementState state, ISurfaceHost host)
        : base(node, state, host)
    {
        // 标题由基类画一次（见 `SurfaceElementControl.BuildChrome`）—— 这里只排
        // "读数占右边、左边留白"这一件事。
        var row = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto") };
        Grid.SetColumn(Readout, 1);
        row.Children.Add(Readout);

        // 读数是**内部零件**（不是主体），所以自己按"文字型内容"报一次口径：
        // 声明了固定高 ⇒ 行盒取那个值，视觉中心 = 框中心。
        FitTextToContent(Readout);

        Content = BuildChrome(row);
        Classes.Add(SurfaceClasses.Numeric);
    }

    /// <summary>每帧读一次最新值（写进数据面 ⇒ 读数块自己跟着变）。</summary>
    public override void Refresh() => State.Text = SurfaceElementState.Format(ReadLatest());
}

/// <summary>
/// **曲线**（<c>curve</c>）：一块绘图区 + 一条折线。
/// </summary>
/// <remarks>
/// <para>
/// 画线本身复用既有的 <see cref="CurveControl"/>（它只做"点序列 → 折线"这一件事，
/// 属可保留的实现）；本控件负责的是<b>取数、定窗口、给外观</b>：
/// 窗口 <c>Min</c>/<c>Max</c> 由本次序列的极值决定，线色/底色/外框色全部走主题。
/// </para>
/// <para>
/// 旧模板里的 <c>Height="64"</c>、<c>Margin="0,2,0,2"</c>、<c>Margin="1"</c> 三个字面量在这里消失：
/// 64 → 主题键 <c>SurfaceCurveMinHeight</c>，外间距归排布面板，内间距归 <c>SurfacePlotInset</c>
/// （<see cref="CurveControl"/> 自己再按主题的绘图区内边距留白）。
/// </para>
/// </remarks>
public sealed class SurfaceCurveControl : SurfaceSourceControl
{
    private readonly CurveControl _curve;
    private readonly Border _frame;

    /// <summary>造一个曲线控件。</summary>
    /// <param name="node">声明节点。</param>
    /// <param name="state">数据面。</param>
    /// <param name="host">表面宿主。</param>
    public SurfaceCurveControl(ViewNode node, SurfaceElementState state, ISurfaceHost host)
        : base(node, state, host)
    {
        _curve = new CurveControl { MinHeight = SurfaceTheme.CurveMinHeight(this) };

        _frame = new Border
        {
            Background = SurfaceTheme.CurvePlotBrush(this),
            BorderBrush = SurfaceTheme.CurveFrameBrush(this),
            BorderThickness = new Thickness(1),
            Padding = new Thickness(SurfaceTheme.PlotInset(this)),
            Child = _curve,
        };
        _frame.Classes.Add(SurfaceClasses.Curve);

        _curve.Stroke = SurfaceTheme.CurveStrokeBrush(this);

        // 曲线也要"当前读数"：折线说明趋势，数字说明此刻是几 —— 两者缺一都得让人去数格子。
        var body = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto") };
        Grid.SetColumn(_frame, 0);
        body.Children.Add(_frame);
        Grid.SetColumn(Readout, 1);
        body.Children.Add(Readout);

        FitTextToContent(Readout);

        Content = BuildChrome(body);
    }

    /// <summary>每帧读一次序列。</summary>
    public override void Refresh()
    {
        var points = ReadSeries();

        if (points.Count <= 1)
        {
            _curve.Points = null;
            State.Text = SurfaceElementState.Format(null);
            return;
        }

        // 窗口取本次序列的极值。等值序列（全是同一个数）会给出一条零高度的窗口，
        // 那时把窗口撑到 ±1 —— 画中线比画一条压在框上的线更能说明"读数确实没变"。
        var min = points[0];
        var max = points[0];

        for (var i = 1; i < points.Count; i++)
        {
            min = Math.Min(min, points[i]);
            max = Math.Max(max, points[i]);
        }

        if (max - min <= double.Epsilon)
        {
            min -= 1;
            max += 1;
        }

        _curve.Points = points;
        _curve.Min = min;
        _curve.Max = max;
        State.Text = SurfaceElementState.Format(points[^1]);
    }
}

/// <summary>读数格式化的小工具（多处共用，避免各自写一套小数位规则）。</summary>
public static class SurfaceNumberFormat
{
    /// <summary>把读数写成固定小数位。</summary>
    /// <param name="value">读数。</param>
    /// <param name="decimals">小数位。</param>
    /// <returns>文本。</returns>
    public static string Fixed(double value, int decimals) =>
        value.ToString("F" + decimals.ToString(CultureInfo.InvariantCulture), CultureInfo.CurrentCulture);
}
