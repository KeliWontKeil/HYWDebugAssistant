using Avalonia.Controls;
using HYWDebugAssistant.FrameworkUi.ViewModels;

namespace HYWDebugAssistant.FrameworkUi.Views;

/// <summary>
/// **模块清单列表**：把分类标题行明确做成不可选容器，模块行保持正常选择。
/// </summary>
/// <remarks>
/// 分类标题是独立列表项，但 ListBox 默认会把每个项都当成可选中对象。这里在容器准备阶段
/// 只对标题容器关闭启用、焦点与命中，避免出现“选中模块时标题也被框住”或键盘能落到标题上。
/// 容器可能被虚拟化复用，所以模块行分支必须把状态显式还原。
/// </remarks>
public sealed class ModuleCatalogListBox : ListBox
{
    /// <summary>
    /// **外观与模板复用普通 ListBox**：本类只增加容器选择行为，不另建一套列表皮肤。
    /// </summary>
    /// <remarks>
    /// Avalonia 默认按实际类型找 <c>ControlTheme</c>。不覆盖这个属性时，子类没有列表模板，
    /// ItemsSource 明明有数据，界面却是空白；这是“计数正常、列表空白”的根因。
    /// 同类先例见 <c>FontFamilyPicker</c> / <c>ModuleSelect</c>。
    /// </remarks>
    protected override Type StyleKeyOverride => typeof(ListBox);

    /// <summary>准备列表项容器：标题行不可选，模块行可正常选择。</summary>
    /// <param name="container">列表项容器。</param>
    /// <param name="item">绑定的列表项。</param>
    /// <param name="index">项在列表中的位置。</param>
    protected override void PrepareContainerForItemOverride(Control container, object? item, int index)
    {
        base.PrepareContainerForItemOverride(container, item, index);

        if (container is not ListBoxItem listItem)
        {
            return;
        }

        var isHeader = item is ModuleCatalogListItem { IsHeader: true };
        listItem.IsEnabled = !isHeader;
        listItem.Focusable = !isHeader;
        listItem.IsHitTestVisible = !isHeader;

        if (isHeader)
        {
            listItem.Classes.Add("catalogHeader");
        }
        else
        {
            listItem.Classes.Remove("catalogHeader");
        }
    }
}
