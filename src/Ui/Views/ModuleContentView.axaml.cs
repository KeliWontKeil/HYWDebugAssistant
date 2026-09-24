using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using HYWDebugAssistant.Ui.ViewModels;

namespace HYWDebugAssistant.Ui.Views;

/// <summary>
/// 模块窗口的**内容层**：按声明渲染元素（+ 窗内虚线参考线 + 人机操作三通道的输入行为）。
/// </summary>
/// <remarks>
/// 【缺陷修复 · M1-17 后】口标记与标签编辑已上移到 <see cref="ModuleWindow"/>：
/// 标记必须压在 6 px 拖动带与缩放手柄之上，否则双击标记会被边缘中点手柄吃掉（详见该类的说明）。
/// 【M1-16 / ADR-0040】文本输入：**回车 / 失焦一次提交**（D-5 裁决），不逐字符写入。
/// </remarks>
public partial class ModuleContentView : UserControl
{
    /// <summary>创建视图。</summary>
    public ModuleContentView() => InitializeComponent();

    /// <summary>输入框：Enter = 提交。</summary>
    private void OnInputKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter || sender is not TextBox { DataContext: TextInputElementViewModel input })
        {
            return;
        }

        e.Handled = true;
        input.CommitCommand.Execute(null);
    }

    /// <summary>输入框：失焦 = 提交（仅在有未提交编辑时）。</summary>
    private void OnInputLostFocus(object? sender, RoutedEventArgs e)
    {
        if (sender is TextBox { DataContext: TextInputElementViewModel { IsDirty: true } input })
        {
            input.CommitCommand.Execute(null);
        }
    }
}
