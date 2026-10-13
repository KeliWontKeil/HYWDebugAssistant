using Avalonia.Controls;
using Avalonia.Media;
using HYWDebugAssistant.Sdk.Views;
using HYWDebugAssistant.Ui.ModuleSurface.Layout;
using HYWDebugAssistant.Ui.ModuleSurface.Theme;

namespace HYWDebugAssistant.Ui.ModuleSurface.Controls;

/// <summary>
/// **一块文字**（<c>label</c>）—— 可静态、可动态。
/// </summary>
/// <remarks>
/// <para>
/// 它存在的理由是**解耦**：在有它之前，想显示一句说明只能把它塞进某个控件的 <c>title</c>，
/// 于是"这块要不要一行说明"变成框架替所有控件做的固定动作，而不是模块作者的排布选择。
/// 后果实测过两处：按钮上出现两个「发送」（说明一个、按钮本身一个），勾选框的文字被顶到框上面。
/// </para>
/// <para>
/// <b>绑了 <c>data</c> 就会变</b>：内容跟着那条 `Text` 源走（模块写什么就显示什么）。
/// 于是"给用户看一句话"不必再借"只读文本框"来假装，也不必让每个控件自带一块浮动结果区 ——
/// 消息是模块写出来的文本，落在哪儿由声明面指定。不绑 <c>data</c> 时它就是一块写死的说明。
/// </para>
/// </remarks>
public sealed class SurfaceLabelControl : SurfaceElementControl
{
    private readonly TextBlock _text;
    private readonly ISurfaceHost _host;

    /// <summary>造一个文字块控件。</summary>
    /// <param name="node">声明节点（文字取 <c>text</c>，缺省退回 <c>title</c>）。</param>
    /// <param name="state">数据面。</param>
    /// <param name="host">表面宿主（绑了源时经它读内容）。</param>
    public SurfaceLabelControl(ViewNode node, SurfaceElementState state, ISurfaceHost host)
        : base(node, state)
    {
        ArgumentNullException.ThrowIfNull(host);
        _host = host;
        Source = node.DataSource ?? string.Empty;

        _text = new TextBlock
        {
            Text = node.Text,

            // **折不折行是一个声明选择，不是框架替所有标签做的固定动作**：
            // 单行标签（"间隔""ms"）落进窄格子时必须不折，否则会被折成两行、把行高顶开；
            // 长说明反过来要折行，否则它会横向溢出去。
            TextWrapping = node.Nowrap ? TextWrapping.NoWrap : TextWrapping.Wrap,
        };
        _text.Classes.Add(SurfaceClasses.Label);

        if (node.Muted)
        {
            _text.Classes.Add(SurfaceClasses.LabelMuted);
        }

        // **主文字的外观一次算清**：运行期状态 → 声明 → 主题（`fontSize` / `textColor`）。
        // 不把声明写进 `state.TextSize` 那种旁路：状态是**运行期**那一档，
        // 混在一起会让"声明给的值"在运行期清空时一起丢掉。
        ApplyLook(_text, MainTextLook());

        Content = BuildChrome(_text);
    }

    /// <summary>
    /// **主文字的外观变了就重画这一块**（声明是初值、运行期可覆盖、都没有则回主题）。
    /// </summary>
    /// <remarks>
    /// 写到 `_text` **自己**身上：主题的 `TextBlock.surface-label` 在它自身设了字号，
    /// 而**样式值压过继承** ⇒ 只设外层元素的字号等于没设（实测"声明了 `fontSize` 却没反应"）。
    /// 颜色同理，且它是局部值 ⇒ 压过 `muted` 那个类（**显式色赢**）。
    /// </remarks>
    protected override void OnMainTextLookChanged() => ApplyLook(_text, MainTextLook());

    /// <summary>本控件绑的数据源名（声明面 <c>data</c>；空 = 静态文字）。</summary>
    public string Source { get; }

    /// <summary>这块文字的内容（静态来自声明，绑了源则来自源）。</summary>
    public string Text => _text.Text ?? string.Empty;

    /// <summary>每帧：绑了源就把源里当前的内容显示出来；没绑就什么都不做（保持静态文字）。</summary>
    public sealed override void Refresh()
    {
        if (Source.Length == 0)
        {
            return;
        }

        // 读法与只读文本面**同一条**（显示缓冲）—— 一个源在两种元素上必须表现一致。
        var lines = _host.ReadBuffer(Source).Lines;
        var text = lines.Count == 0 ? string.Empty : string.Join(Environment.NewLine, lines);

        // **内容没变就一个字都不动**：赋 `Text` 会触发布局失效，静止的一块文字每帧白排一次
        // 不只是浪费 —— 它会让"这块文字是不是刚变过"变得无从判断。
        if (!string.Equals(_text.Text, text, StringComparison.Ordinal))
        {
            _text.Text = text;
        }
    }
}
