using Avalonia.Controls;
using Avalonia.Styling;
using Avalonia.VisualTree;

namespace HYWDebugAssistant.Sdk.Ui.Controls;

/// <summary>
/// **模块页签**：标准 <see cref="TabControl"/> 的薄壳，把"当前选中页"接到视图状态。
/// </summary>
/// <remarks>
/// <para>
/// 模块（例如文本发送器）每拍要读"用户现在在哪一页"来决定发哪一页的内容；
/// 页签的选中态若只活在控件里，模块永远只看到第 0 页 —— 界面看着正常、发出去的全是第一页。
/// 本控件是这条事实的**唯一写者**：用户切页 ⇒ 写状态；模块读状态。
/// </para>
/// <para>
/// 选中态按 <see cref="StateKey"/> 分开保存，切页/重建视图不丢；控件本身不持有第二份"上次选中页"。
/// </para>
/// </remarks>
public sealed class ModuleTabs : TabControl, IModuleViewElement
{
    private IModuleViewContext? _context;
    private bool _syncing;
    private bool _restored;

    /// <summary>造一个模块页签。</summary>
    public ModuleTabs()
    {
        SelectionChanged += (_, _) => Remember();
        AttachedToVisualTree += (_, _) =>
        {
            ResolveContext();
            Restore();
            ApplyContainerTheme();
        };
    }

    /// <summary>页签组的**状态键**（模块与视图共用的常量；空 = 不持久化选中页）。</summary>
    public string StateKey { get; set; } = string.Empty;

    /// <inheritdoc />
    public void Refresh(IModuleViewContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        _context = context;
        Restore();
    }

    /// <summary>从视图状态恢复选中页（只做一次；之后由用户交互驱动）。</summary>
    private void Restore()
    {
        if (_restored || _context is null || StateKey.Length == 0)
        {
            return;
        }

        _restored = true;

        if (_context.SelectedPage(StateKey) is not { } page || page < 0 || page >= ItemCount)
        {
            return;
        }

        if (SelectedIndex == page)
        {
            return;
        }

        _syncing = true;
        SelectedIndex = page;
        _syncing = false;
    }

    /// <summary>用户切页 ⇒ 写回视图状态（模块随后读到的是同一页）。</summary>
    private void Remember()
    {
        if (_syncing || _context is null || StateKey.Length == 0 || SelectedIndex < 0)
        {
            return;
        }

        _context.SetSelectedPage(StateKey, SelectedIndex);
    }

    /// <summary>
    /// 把页签容器主题**本地**挂上。
    /// </summary>
    /// <remarks>
    /// Fluent 的 `TabControl` 主题会通过 `ItemContainerTheme` 给每个 `TabItem` 挂自己的容器主题；
    /// 只在样式里设 `TabItem.Theme` 或 `ItemContainerTheme` 都可能被这层主题压住。
    /// 本地值优先，才能真正换掉页签模板、内边距和下沿。
    /// </remarks>
    private void ApplyContainerTheme()
    {
        if (this.TryFindResource("ModuleTabItemTheme", out var value) && value is ControlTheme theme)
        {
            ItemContainerTheme = theme;
        }
    }

    /// <summary>从可视树里找宿主取上下文（模块视图里不必为此写代码）。</summary>
    private void ResolveContext()
    {
        if (_context is null && this.FindAncestorOfType<ModuleViewHost>() is { } host)
        {
            _context = host.Context;
        }
    }
}
