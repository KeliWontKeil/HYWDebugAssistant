using System.Diagnostics;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using HYWDebugAssistant.FrameworkUi.ViewModels;
using HYWDebugAssistant.Sdk.Text;
using HYWDebugAssistant.Ui.Hotkeys;
using HYWDebugAssistant.Ui.Images;
using HYWDebugAssistant.Ui.Theming;

namespace HYWDebugAssistant.FrameworkUi.Views;

/// <summary>
/// 设置弹窗视图的代码后置：**快捷键捕获**、**主题卡选择**、**色板取色**。
/// </summary>
/// <remarks>
/// 只有窗口相关的一点点逻辑在这里（按键捕获必须拿到真实按键事件；取色需要属主窗口）；
/// 主题 / 色板 / 方案命令本身都在视图模型里。
/// </remarks>
public partial class SettingsView : UserControl
{
    /// <summary>创建视图。</summary>
    public SettingsView() => InitializeComponent();

    /// <summary>本页的视图模型（设置面板）。</summary>
    private SettingsPanelViewModel? Settings => DataContext as SettingsPanelViewModel;

    /// <summary>取一条本程序集文本表里的文案。</summary>
    /// <param name="key">键。</param>
    private static string Loc(string key) => TextCatalog.Get(typeof(SettingsView).Assembly, key);

    /// <summary>取一条带占位符的文案。</summary>
    /// <param name="key">键。</param>
    /// <param name="args">格式化参数。</param>
    private static string Loc(string key, params object?[] args) =>
        TextCatalog.Get(typeof(SettingsView).Assembly, key, args);

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

    /// <summary>选择当前主题的主窗口壁纸（全局图片裁切器）。</summary>
    private async void OnPickThemeWallpaper(object? sender, RoutedEventArgs e)
    {
        if (Settings is not { } settings || TopLevel.GetTopLevel(this) is not Window owner)
        {
            return;
        }

        var (width, height) = ShellLayout.Measure(settings.ShellAspect);
        var aspect = width / Math.Max(1, height);
        var png = await ImagePicker.PickAsync(owner, aspect, Loc("appearance.wallpaper.pickTitle"));
        if (png is not null)
        {
            settings.SetThemeWallpaper(png);
        }
    }

    /// <summary>清除当前主题的主窗口壁纸（草稿）。</summary>
    private void OnClearThemeWallpaper(object? sender, RoutedEventArgs e) => Settings?.ClearThemeWallpaper();

    /// <summary>恢复该动作的默认绑定。</summary>
    private void OnResetHotkey(object? sender, RoutedEventArgs e)
    {
        if (sender is Button { DataContext: HotkeyRowViewModel row } && DataContext is SettingsPanelViewModel settings)
        {
            settings.ResetHotkey(row);
        }
    }

    /// <summary>在资源管理器中打开一个存储位置（只读信息区的「打开」按钮）。</summary>
    private void OnOpenStorageFolder(object? sender, RoutedEventArgs e)
    {
        if (sender is not Button { CommandParameter: string path } || string.IsNullOrWhiteSpace(path))
        {
            return;
        }

        try
        {
            _ = Process.Start(new ProcessStartInfo
            {
                FileName = path,
                UseShellExecute = true,
            });
        }
        catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception or FileNotFoundException)
        {
            // 目录尚未创建或系统无法打开：不抛到界面（设置页仍可继续用）
        }
    }

    // ------------------------------------------------------------ 主题方案

    /// <summary>点主题卡 ⇒ 切换**设置页里**显示/编辑的色板草稿（不提前改全应用）。</summary>
    private void OnSelectThemeCard(object? sender, RoutedEventArgs e)
    {
        if (sender is Button { DataContext: ThemeCardViewModel card } && Settings is { } settings)
        {
            settings.SelectTheme(card.Id);
        }
    }

    /// <summary>「⋯ → 恢复此方案默认」：清空当前自定义方案的覆盖（草稿）。</summary>
    private void OnResetScheme(object? sender, RoutedEventArgs e) =>
        Settings?.ResetSchemeCommand.Execute(null);

    /// <summary>「⋯ → 删除此方案」：删除当前自定义方案（草稿，取消可反悔）。</summary>
    private void OnDeleteScheme(object? sender, RoutedEventArgs e) =>
        Settings?.DeleteSchemeCommand.Execute(null);

    // ------------------------------------------------------------ 主题色板（逐项取色）

    /// <summary>点某一行的色块：打开调色板窗口；「恢复默认」在窗口内完成。</summary>
    private async void OnPickPaletteColor(object? sender, RoutedEventArgs e)
    {
        if (sender is not Button { DataContext: PaletteRowViewModel row } || Settings is not { } settings)
        {
            return;
        }

        var owner = TopLevel.GetTopLevel(this) as Window;
        var initial = ThemeApplier.Parse(row.Cell.EffectiveHex ?? string.Empty) ?? Colors.Gray;

        var result = await ColorPickerDialog.ShowAsync(
            owner,
            initial,
            Loc("palette.dialog.title", row.Title),
            resetText: row.Cell.IsOverridden ? Loc("palette.dialog.reset") : null);

        if (result.Confirmed)
        {
            settings.SetPaletteColor(row, result.Reset ? null : DataTypeColorRowViewModel.ToHex(result.Color));
        }
    }
}
