using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using HYWDebugAssistant.FrameworkUi.ViewModels;
using HYWDebugAssistant.Ui.Hotkeys;

namespace HYWDebugAssistant.FrameworkUi.Views;

/// <summary>
/// 设置弹窗视图的代码后置：**快捷键捕获**（按下组合键即写入绑定）。
/// </summary>
/// <remarks>
/// 只有窗口相关的一点点逻辑在这里（按键捕获必须拿到真实按键事件）；其余全是绑定。
/// </remarks>
public partial class SettingsView : UserControl
{
    /// <summary>创建视图。</summary>
    public SettingsView() => InitializeComponent();

    /// <summary>
    /// 在「按键」框里按下组合键 → 记录绑定（支持 Ctrl/Shift/Alt + 键；Delete/Backspace = 清除绑定）。
    /// </summary>
    private void OnHotkeyBoxKeyDown(object? sender, KeyEventArgs e)
    {
        if (sender is not TextBox { DataContext: HotkeyRowViewModel row } || DataContext is not SettingsPanelViewModel settings)
        {
            return;
        }

        e.Handled = true;

        switch (e.Key)
        {
            case Key.Delete or Key.Back:
                settings.SetHotkey(row.ActionId, null);
                row.IsCapturing = false;
                return;

            case Key.Escape:
                row.IsCapturing = false;
                return;

            default:
                break;
        }

        var binding = HotkeyBinding.From(e.Key, e.KeyModifiers);
        if (binding.IsEmpty)
        {
            return;   // 只按了修饰键
        }

        settings.SetHotkey(row.ActionId, binding.ToString());
        row.IsCapturing = false;
    }

    /// <summary>焦点进入按键框 → 提示"等待按键"。</summary>
    private void OnHotkeyBoxFocused(object? sender, RoutedEventArgs e)
    {
        if (sender is TextBox { DataContext: HotkeyRowViewModel row })
        {
            row.IsCapturing = true;
        }
    }

    /// <summary>恢复该动作的默认绑定。</summary>
    private void OnResetHotkey(object? sender, RoutedEventArgs e)
    {
        if (sender is Button { DataContext: HotkeyRowViewModel row } && DataContext is SettingsPanelViewModel settings)
        {
            settings.ResetHotkey(row);
        }
    }
}
