using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.VisualTree;

namespace HYWDebugAssistant.Sdk.Ui.Controls;

/// <summary>
/// **命令按钮**：点一下触发模块的一条命令。
/// </summary>
/// <remarks>
/// <para>
/// <b>按钮只管触发</b>：它不显示结果 —— 结果由模块写成一句话落在它自己的显示源上
/// （"消息不是控件的能力"）。框架的"已投递"这类回执也不上屏：用户要看的是**产生了什么结果**。
/// </para>
/// <para>
/// 它派生自 <see cref="Button"/> 而不是 <see cref="ModuleElement"/>：按钮没有"值"要按帧刷新，
/// 只有"哪条命令"与外观；外观直接由应用级按钮主题给（五态、焦点、禁用），不必再造一遍。
/// </para>
/// </remarks>
public sealed class ModuleButton : Button
{
    private IModuleViewContext? _context;

    /// <summary>造一个命令按钮。</summary>
    public ModuleButton()
    {
        HorizontalAlignment = HorizontalAlignment.Stretch;
        VerticalAlignment = VerticalAlignment.Stretch;
        Click += (_, _) => Invoke();
    }

    /// <summary>本按钮触发的命令名（模块在 <c>OnRegistered</c> 里声明过）。</summary>
    /// <remarks>
    /// 刻意不叫 <c>Command</c>：那个名字在 Avalonia 里已经是 <c>ICommand</c> 绑定槽，
    /// 两者同名会让"按钮为什么没反应"变成一个很难查的问题（一个是命令名、一个是命令对象）。
    /// </remarks>
    public string CommandName { get; set; } = string.Empty;

    /// <summary>挂在视图里时记住取数入口（点下去要用）。</summary>
    /// <param name="context">视图上下文。</param>
    internal void Attach(IModuleViewContext context) => _context = context;

    /// <inheritdoc />
    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);

        // 自己找宿主要上下文：模块视图里不必为此写任何代码（按钮一挂树就能用）。
        if (_context is null && this.FindAncestorOfType<ModuleViewHost>() is { } host)
        {
            Attach(host.Context);
        }
    }

    /// <summary>触发一次（供测试直接调用）。</summary>
    public void Invoke()
    {
        if (_context is null || CommandName.Length == 0)
        {
            return;
        }

        // 结论由框架收进 Issue（失败时用户能看到为什么）；按钮自己不解释、不弹窗。
        _ = _context.InvokeCommand(CommandName);
    }
}
