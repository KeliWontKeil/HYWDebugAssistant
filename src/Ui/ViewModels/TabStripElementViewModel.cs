using CommunityToolkit.Mvvm.Input;
using HYWDebugAssistant.Sdk.Views;

namespace HYWDebugAssistant.Ui.ViewModels;

/// <summary>
/// 标签条上的**一个页签**（只承载标题、选中态与"切到我这页"的命令）——G1 · 2026-09。
/// </summary>
/// <remarks>
/// **只呈现**：标签是框架侧的**纯 UI 状态**，不进组合件、不下发模块（与"视图声明由框架渲染"同一口径）。
/// </remarks>
public sealed partial class TabPageItemViewModel : ViewModelBase
{
    private readonly Action<int>? _select;

    /// <summary>创建页签。</summary>
    /// <param name="index">页序号（0 基）。</param>
    /// <param name="title">页签标题（空标题已由调用方回退为序号）。</param>
    /// <param name="isSelected">是否当前选中页。</param>
    /// <param name="select">切换回调（参数 = 页序号）。</param>
    public TabPageItemViewModel(int index, string title, bool isSelected, Action<int>? select)
    {
        Index = index;
        Title = title;
        IsSelected = isSelected;
        _select = select;
    }

    /// <summary>页序号（0 基）。</summary>
    public int Index { get; }

    /// <summary>页签标题。</summary>
    public string Title { get; }

    /// <summary>是否当前选中页（视图用于 `Button.selected` 选中态）。</summary>
    /// <remarks>选中变化时**整条标签条会被重建**，故它是只读值、不是可变状态。</remarks>
    public bool IsSelected { get; }

    /// <summary>切到本页（框架内部命令；纯 UI 状态变更后重建元素表）。</summary>
    [RelayCommand]
    private void Select() => _select?.Invoke(Index);
}

/// <summary>
/// **C2 标签页**的标签条 —— G1 · 2026-09。
/// </summary>
/// <remarks>
/// <list type="bullet">
///   <item><b>为什么要它</b>：`ViewElementKind.TabPages` 早在 v1 元素目录内（C2），但此前**只取首页子元素**、
///     页签条与切换**从未实现** ⇒ 模块声明的多页只能看到第 1 页（G1 缺口，见 `roadmap.md` §4）；</item>
///   <item><b>与既有约定一致</b>：容器本身不画、由框架渲染（`Group`/`Collapsible`/`Scroll` 的子元素**展平**显示）；
///     标签页按同一约定展开为「**标签条**（本类）+ **当前页的子元素**」，因此不需要嵌套模板；</item>
///   <item><b>只呈现</b>：标签条不参与建网/投递/计量，也不进组合件；未声明的页不出现。</item>
/// </list>
/// </remarks>
public sealed class TabStripElementViewModel : ViewElementViewModel
{
    /// <summary>创建标签条。</summary>
    /// <param name="title">标签条标题（可空；来自 `TabPages` 元素声明）。</param>
    /// <param name="pages">页签清单（顺序 = 声明顺序）。</param>
    /// <param name="selectedIndex">当前页序号。</param>
    public TabStripElementViewModel(string? title, IReadOnlyList<TabPageItemViewModel> pages, int selectedIndex)
        : base(ViewElementKind.TabPages, title, dataSourceName: null)
    {
        ArgumentNullException.ThrowIfNull(pages);

        Pages = pages;
        SelectedIndex = selectedIndex;
    }

    /// <summary>页签清单。</summary>
    public IReadOnlyList<TabPageItemViewModel> Pages { get; }

    /// <summary>当前页序号。</summary>
    public int SelectedIndex { get; }
}
