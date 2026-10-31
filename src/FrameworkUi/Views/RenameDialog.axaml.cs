using Avalonia.Controls;
using Avalonia.Input;
using HYWDebugAssistant.Ui.Views;

namespace HYWDebugAssistant.FrameworkUi.Views;

/// <summary>组合重命名用的小输入框（按文本自适应大小）。</summary>
public partial class RenameDialog : Window
{
    /// <summary>创建对话框。</summary>
    public RenameDialog() => InitializeComponent();

    /// <summary>显示重命名对话框并返回新名称（取消 = null）。</summary>
    /// <param name="owner">属主窗口。</param>
    /// <param name="initial">当前名称（不含 .json）。</param>
    public static async Task<string?> ShowAsync(Window? owner, string initial)
    {
        var dialog = new RenameDialog();
        dialog.NameBox.Text = initial;
        dialog.Opened += (_, _) =>
        {
            dialog.NameBox.Focus();
            dialog.NameBox.SelectAll();
        };

        dialog.CancelButton.Click += (_, _) => dialog.Close(null);
        dialog.RenameButton.Click += (_, _) => dialog.Close(dialog.NameBox.Text);

        var group = AppWindowGroup.Of(owner);
        group?.Track(dialog, AppWindowLayer.Modal);
        using var modal = group?.BeginModal(dialog);

        if (owner is null)
        {
            dialog.Show();
            return null;
        }

        return await dialog.ShowDialog<string?>(owner);
    }

    private void OnKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            Close(NameBox.Text);
            e.Handled = true;
        }
        else if (e.Key == Key.Escape)
        {
            Close(null);
            e.Handled = true;
        }
    }
}
