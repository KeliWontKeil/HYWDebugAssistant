using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using HYWDebugAssistant.Core.Api;
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

    // ------------------------------------------------------------ 背景预设（动作：一次写六格）

    /// <summary>
    /// 套一档背景预设：把三支底色写进**两套主题**的色板行（真正的生效仍走「应用 / 确认」）。
    /// </summary>
    private void OnApplyBackgroundPreset(object? sender, RoutedEventArgs e)
    {
        if (sender is Button { DataContext: ThemePresets.BackgroundPreset preset })
        {
            Settings?.ApplyBackgroundPreset(preset);
        }
    }

    // ------------------------------------------------------------ 主题色板（逐项：取色 / 恢复 / 两套同色）

    /// <summary>色板某一行的**浅色**格：打开调色板窗口（窗口里「跟随内置」= 恢复跟随）。</summary>
    private async void OnPickPaletteLight(object? sender, RoutedEventArgs e) =>
        await PickPaletteAsync(sender, dark: false);

    /// <summary>色板某一行的**深色**格（同上）。</summary>
    private async void OnPickPaletteDark(object? sender, RoutedEventArgs e) =>
        await PickPaletteAsync(sender, dark: true);

    /// <summary>色板一格的取色路径：初值 = 这一格**当前有效色**（已改 ⇒ 改过的；跟随 ⇒ 该套内置值）。</summary>
    private async Task PickPaletteAsync(object? sender, bool dark)
    {
        if (sender is not Button { DataContext: PaletteRowViewModel row } || Settings is not { } settings)
        {
            return;
        }

        var owner = TopLevel.GetTopLevel(this) as Window;
        var initial = ThemeApplier.Parse(row.Cell(dark).EffectiveHex ?? string.Empty) ?? Colors.Gray;

        var result = await ColorPickerDialog.ShowAsync(
            owner,
            initial,
            $"编辑颜色 · {row.Key}（{(dark ? "深色" : "浅色")}）",
            resetText: "跟随内置");

        if (result.Confirmed)
        {
            settings.SetPaletteColor(row, dark, result.Reset ? null : DataTypeColorRowViewModel.ToHex(result.Color));
        }
    }

    /// <summary>浅色那一格恢复跟随。</summary>
    private void OnResetPaletteLight(object? sender, RoutedEventArgs e) => ResetPaletteCell(sender, dark: false);

    /// <summary>深色那一格恢复跟随。</summary>
    private void OnResetPaletteDark(object? sender, RoutedEventArgs e) => ResetPaletteCell(sender, dark: true);

    /// <summary>某一格恢复跟随内置值。</summary>
    private void ResetPaletteCell(object? sender, bool dark)
    {
        if (sender is Button { DataContext: PaletteRowViewModel row })
        {
            Settings?.SetPaletteColor(row, dark, null);
        }
    }

    /// <summary>「两套同色」：浅色那一格的当前色写成深色那一格的覆盖值。</summary>
    private void OnSyncPaletteRow(object? sender, RoutedEventArgs e)
    {
        if (sender is Button { DataContext: PaletteRowViewModel row })
        {
            Settings?.SyncPaletteToDark(row);
        }
    }

    /// <summary>「本区恢复默认」：这一区全部回到跟随内置值。</summary>
    private void OnResetPaletteGroup(object? sender, RoutedEventArgs e)
    {
        if (sender is Button { DataContext: PaletteGroupViewModel group })
        {
            Settings?.ResetPaletteGroup(group);
        }
    }
}
