using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using HYWDebugAssistant.Sdk.Views;
using HYWDebugAssistant.Ui.ModuleSurface.Layout;
using HYWDebugAssistant.Ui.ModuleSurface.Theme;

namespace HYWDebugAssistant.Ui.ModuleSurface.Controls;

/// <summary>
/// **命令按钮**（<c>commandButton</c>）：点一下触发模块的一条命令，结果就地显示。
/// </summary>
/// <remarks>
/// <para>
/// 命令即入口：声明面写一个命令名就够了，<b>不另设命令表</b> —— 于是"这个按钮干什么"
/// 在声明处一眼可读，不需要在两处对照。
/// </para>
/// <para>
/// <b>反馈来自框架</b>：<see cref="ISurfaceHost.InvokeCommand"/> 把模块给的结论回填到
/// <see cref="SurfaceElementState.Result"/>，按钮不自己编"成功/失败"，
/// 也**不去解析结果文本里的字样** —— 靠前缀或关键词判错等于发明一种隐式字符串协议，
/// 模块换一句文案就判错失灵（对错由 <see cref="SurfaceElementState.ResultIsError"/> 承载）。
/// </para>
/// </remarks>
public sealed class SurfaceCommandButtonControl : SurfaceElementControl
{
    private readonly ISurfaceHost _host;
    private readonly Button _button;

    /// <summary>造一个命令按钮控件。</summary>
    /// <param name="node">声明节点。</param>
    /// <param name="state">数据面。</param>
    /// <param name="host">表面宿主。</param>
    public SurfaceCommandButtonControl(ViewNode node, SurfaceElementState state, ISurfaceHost host)
        : base(node, state)
    {
        ArgumentNullException.ThrowIfNull(host);
        _host = host;

        // **按钮身上的字就是按钮本身**：取声明的 `text`，不取 `title`。
        // `title` 在别处一律是"这个元素的说明文字"，两者混用会让按钮出现双份文案。
        var text = node.Text.Length > 0 ? node.Text : (node.Command ?? "命令");
        _button = new Button { Content = text };
        _button.Classes.Add(SurfaceClasses.CommandButton);
        _button.Click += (_, _) => Invoke();

        // **按钮自带的缺省是"按内容对齐"**：不填满就是"声明 `width.pixels` 静默失效"（见 `FillContentRect`）。
        FillContentRect(_button);

        // **按钮上那块字的外观**：主文字的键（`fontSize` / `textColor`）说了算，
        // 并写到**按钮自己**身上 —— 局部值压过 `Button.surface-command` 那条样式字号
        // （只设外层元素等于没设，实测"声明了 `fontSize` 却没反应"）。
        ApplyLook(_button, MainTextLook());

        // 按钮不画任何说明行：它的字在按钮上。
        Content = BuildChrome(_button);
    }

    /// <inheritdoc />
    protected override void OnMainTextLookChanged() => ApplyLook(_button, MainTextLook());

    /// <summary>本条命令绑定的命令名（声明面 <c>command</c>）。</summary>
    public string Command => Node.Command ?? string.Empty;

    /// <summary>
    /// 触发命令。
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>按钮只管触发</b>：它不显示任何结果 —— 结果由模块写成文字，落在声明面指定的那块文字上
    /// （见"消息不是控件能力"）。用户按一下按钮，要知道的是**产生了什么结果**，
    /// 而不是"底层触发了什么"，所以框架的"已投递命令"这类回执也不上屏。
    /// </para>
    /// <para>
    /// 命令本身**失败**（模块没实现这条命令之类）时，结论由框架收集成 Issue，不在这儿喊。
    /// </para>
    /// </remarks>
    public void Invoke()
    {
        if (Command.Length == 0)
        {
            return;
        }

        _ = _host.InvokeCommand(Node.Command);
    }
}

/// <summary>
/// **仅登记、尚未渲染**的控件占位（<c>DeclarationOnly</c>）。
/// </summary>
/// <remarks>
/// <para>
/// 声明面允许一个控件的实现还没跟上（<c>rendered: false</c> 的 kind）。这时框架**明确标注
/// "未渲染"**，而不是画一个看起来能用、其实什么都不做的控件 ——
/// 后者会让模块开发者以为"我声明对了，是别处坏了"。
/// </para>
/// <para>
/// 它是<b>一个控件</b>而不是模板里的一段文字：这样"哪些 kind 还没做"是运行期可数的
/// （数它的实例），而不是只能靠读 XAML 猜。
/// </para>
/// </remarks>
public sealed class SurfaceDeclarationOnlyControl : SurfaceElementControl
{
    /// <summary>造一个"仅登记"占位控件。</summary>
    /// <param name="node">声明节点。</param>
    /// <param name="state">数据面。</param>
    public SurfaceDeclarationOnlyControl(ViewNode node, SurfaceElementState state)
        : base(node, state)
    {
        var text = new TextBlock
        {
            Text = string.Create(
                System.Globalization.CultureInfo.CurrentCulture,
                $"「{(node.Title.Length > 0 ? node.Title : node.Kind)}」尚未渲染（元素类型 {node.Kind}）"),
            TextWrapping = Avalonia.Media.TextWrapping.Wrap,
            [!TextBlock.FontSizeProperty] =
                new Avalonia.Markup.Xaml.MarkupExtensions.DynamicResourceExtension(
                    SurfaceTheme.PlaceholderFontSizeKey),
        };
        text.Classes.Add(SurfaceClasses.Placeholder);

        var stack = new StackPanel { Orientation = Orientation.Vertical };
        stack.Children.Add(text);
        Content = BuildChrome(stack);
    }

    /// <summary>占位控件的通知文本（供测试与日志用）。</summary>
    public string Notice => Node.Kind;
}
