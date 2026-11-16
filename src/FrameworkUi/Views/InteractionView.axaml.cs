using Avalonia.Controls;
using Avalonia.Interactivity;
using HYWDebugAssistant.FrameworkUi.ViewModels;
using HYWDebugAssistant.Sdk.Text;

namespace HYWDebugAssistant.FrameworkUi.Views;

/// <summary>
/// 交互页（B 区侧边界面）：显示 / 交互 / 窗口 / 快捷键分区，
/// 以及底部 **数据结构 · 颜色映射表**。
/// </summary>
public partial class InteractionView : UserControl
{
    /// <summary>创建视图。</summary>
    public InteractionView() => InitializeComponent();

    /// <summary>
    /// 点某一行的**色块** ⇒ 打开**调色板窗口**（可任选颜色 / 恢复自动色）。
    /// </summary>
    /// <param name="sender">被点的色块按钮（其 `DataContext` = 该行）。</param>
    /// <param name="e">事件参数。</param>
    /// <remarks>
    /// **弹窗由视图打开、结果交回视图模型**：视图模型不依赖窗口类型（可单测），视图负责"谁能当属主"。
    /// </remarks>
    private async void OnPickDataTypeColor(object? sender, RoutedEventArgs e)
    {
        if (sender is not Button { DataContext: DataTypeColorRowViewModel row })
        {
            return;
        }

        var owner = TopLevel.GetTopLevel(this) as Window;

        var result = await ColorPickerDialog.ShowAsync(
            owner,
            row.CurrentColor,
            TextCatalog.Get(typeof(InteractionView).Assembly, "interaction.colorDialog.title", row.TypeKey),
            resetText: TextCatalog.Get(typeof(InteractionView).Assembly, "interaction.colorDialog.reset"));

        if (!result.Confirmed)
        {
            return;
        }

        if (result.Reset)
        {
            row.ResetToAuto();
        }
        else
        {
            row.ApplyPick(result.Color);
        }
    }
}
