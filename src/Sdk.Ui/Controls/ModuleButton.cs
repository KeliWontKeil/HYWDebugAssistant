using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.VisualTree;
using HYWDebugAssistant.Sdk.Resources;
using IconControl = HYWDebugAssistant.Sdk.Ui.Controls.Icon;

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
/// <para>
/// <b>图标是数据</b>：<see cref="Icon"/> 只存一个键，图形来自**本模块自己的图标表**指向的 SVG
/// （见 <see cref="IconCatalog"/>）——视图里不再出现任何几何，换图标只换 SVG 文件。
/// </para>
/// </remarks>
public sealed class ModuleButton : Button, IModuleViewElement
{
    private IModuleViewContext? _context;
    private string _icon = string.Empty;
    private bool _hasVisualAncestor;

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

    /// <summary>
    /// 图标键（来自**本模块自己的图标表**）：非空 ⇒ 把 <see cref="ContentControl.Content"/> 换成字形。
    /// </summary>
    /// <remarks>
    /// 空 = 文本模式：`Content` 由视图给（打开 / 关闭这类按钮走它），本类绝不覆盖。
    /// 非空时按键查表，**缺表 / 缺键把键名本身写进 `Content`**——宁可界面上露出缺的是哪个键，
    /// 也不要一枚看不见的空白；此时字体退回界面字体（图标字体里的字母是空白或方块）。
    /// </remarks>
    public string Icon
    {
        get => _icon;
        set
        {
            var normalized = value ?? string.Empty;
            if (string.Equals(_icon, normalized, StringComparison.Ordinal))
            {
                return;
            }

            _icon = normalized;

            if (_hasVisualAncestor)
            {
                ApplyIcon();
            }
        }
    }

    /// <summary>挂在视图里时记住取数入口（点下去要用）。</summary>
    /// <param name="context">视图上下文。</param>
    internal void Attach(IModuleViewContext context) => _context = context;

    /// <inheritdoc />
    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);

        _hasVisualAncestor = true;

        // 自己找宿主要上下文：模块视图里不必为此写任何代码（按钮一挂树就能用）。
        if (_context is null && this.FindAncestorOfType<ModuleViewHost>() is { } host)
        {
            Attach(host.Context);
        }

        // 图标要在树里才查得到 owner（按钮的 owner = 承载它的模块视图所在程序集）。
        ApplyIcon();
    }

    /// <inheritdoc />
    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnDetachedFromVisualTree(e);
        _hasVisualAncestor = false;
    }

    /// <inheritdoc />
    public void Refresh(IModuleViewContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        _context ??= context;

        if (CommandName.Length == 0)
        {
            return;
        }

        var enabled = context.Appearance(CommandName).Enabled ?? true;
        if (IsEnabled != enabled)
        {
            IsEnabled = enabled;
        }
    }

    /// <summary>触发一次（供测试直接调用）。</summary>
    public void Invoke()
    {
        if (!IsEnabled || _context is null || CommandName.Length == 0)
        {
            return;
        }

        // 结论由框架收进 Issue（失败时用户能看到为什么）；按钮自己不解释、不弹窗。
        _ = _context.InvokeCommand(CommandName);
    }

    /// <summary>图标模式的落点：把内容换成**一个图标控件**（键 + 本模块程序集）。</summary>
    /// <remarks>
    /// 缺表 / 缺键由 <see cref="IconControl"/> 自己降级（显示键名 + 诊断），按钮不重复判断；
    /// 图标的前景色跟随按钮的 `Foreground`（继承）⇒ 按钮的指针/按下态自动带上图标。
    /// </remarks>
    private void ApplyIcon()
    {
        if (_icon.Length == 0)
        {
            // 文本模式：`Content` 是视图给的文字，这里不碰。
            return;
        }

        Content = new IconControl
        {
            IconKey = _icon,
            Owner = this.FindAncestorOfType<ModuleView>()?.GetType().Assembly,
        };
    }
}
