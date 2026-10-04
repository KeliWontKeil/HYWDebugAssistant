using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using HYWDebugAssistant.FrameworkUi.ViewModels;
using HYWDebugAssistant.Ui.Hotkeys;
using HYWDebugAssistant.Ui.Theming;

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

    /// <summary>本页的视图模型（设置面板）。</summary>
    private SettingsPanelViewModel? Settings => DataContext as SettingsPanelViewModel;

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

    // ------------------------------------------------------------ 调色板窗口（统一取色入口）

    /// <summary>主题色：打开调色板窗口（调色板窗口可被所有颜色选择相关位置复用）。</summary>
    private async void OnPickAccentColor(object? sender, RoutedEventArgs e) =>
        await PickAsync(
            current: Settings?.AccentColor,
            fallback: "#2563EB",
            title: "编辑颜色 · 主题色",
            resetText: null,
            apply: hex => Settings!.AccentColor = hex ?? "#2563EB");

    /// <summary>背景色（"自定义"档位用的色）。</summary>
    private async void OnPickBackgroundColor(object? sender, RoutedEventArgs e) =>
        await PickAsync(
            current: Settings?.BackgroundColor,
            fallback: "#F3F4F6",
            title: "编辑颜色 · 背景色",
            resetText: null,
            apply: hex => Settings!.BackgroundColor = hex ?? "#F3F4F6");

    /// <summary>发送方向色（**留空 = 跟随主题默认值** ⇒ 恢复按钮文案就是"跟随主题"）。</summary>
    private async void OnPickSendColor(object? sender, RoutedEventArgs e) =>
        await PickAsync(
            current: Settings?.DirectionSendColor,
            fallback: "#EA580C",
            title: "编辑颜色 · 发送色",
            resetText: "跟随主题",
            apply: hex => Settings!.DirectionSendColor = hex ?? string.Empty);

    /// <summary>接收方向色（留空 = 跟随主题默认值）。</summary>
    private async void OnPickReceiveColor(object? sender, RoutedEventArgs e) =>
        await PickAsync(
            current: Settings?.DirectionReceiveColor,
            fallback: "#0891B2",
            title: "编辑颜色 · 接收色",
            resetText: "跟随主题",
            apply: hex => Settings!.DirectionReceiveColor = hex ?? string.Empty);

    /// <summary>统一的"打开调色板窗口 → 把结果交回设置"路径（当前值为空时用默认色作初值）。</summary>
    private async Task PickAsync(string? current, string fallback, string title, string? resetText, Action<string?> apply)
    {
        if (Settings is null)
        {
            return;
        }

        var owner = TopLevel.GetTopLevel(this) as Window;
        var initial = ThemeApplier.Parse(string.IsNullOrWhiteSpace(current) ? fallback : current) ?? Colors.Gray;

        var result = await ColorPickerDialog.ShowAsync(owner, initial, title, resetText);

        if (result.Confirmed)
        {
            apply(result.Reset ? null : DataTypeColorRowViewModel.ToHex(result.Color));
        }
    }
}
