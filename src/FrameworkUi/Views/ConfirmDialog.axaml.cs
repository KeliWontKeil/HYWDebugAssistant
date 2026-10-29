using Avalonia.Controls;
using HYWDebugAssistant.Sdk.Text;

namespace HYWDebugAssistant.FrameworkUi.Views;

/// <summary>两键确认框（覆盖 / 重启等）；返回 true = 点了主按钮。</summary>
public partial class ConfirmDialog : Window
{
    /// <summary>创建确认框。</summary>
    public ConfirmDialog() => InitializeComponent();

    /// <summary>显示确认框并等待结果。</summary>
    /// <param name="owner">属主窗口。</param>
    /// <param name="title">窗口标题。</param>
    /// <param name="message">正文。</param>
    /// <param name="primary">主按钮文案。</param>
    /// <param name="cancel">取消按钮文案（null/空 ⇒ 用默认“取消”）。</param>
    /// <returns>true = 主按钮；false = 取消/关闭。</returns>
    public static async Task<bool> ShowAsync(
        Window? owner,
        string title,
        string message,
        string primary,
        string? cancel = null)
    {
        var dialog = new ConfirmDialog
        {
            Title = title,
        };

        dialog.MessageText.Text = message;
        dialog.PrimaryButton.Content = primary;
        dialog.CancelButton.Content = string.IsNullOrWhiteSpace(cancel)
            ? TextCatalog.Get(typeof(ConfirmDialog).Assembly, "dialog.cancel")
            : cancel;

        dialog.PrimaryButton.Click += (_, _) => dialog.Close(true);
        dialog.CancelButton.Click += (_, _) => dialog.Close(false);

        if (owner is null)
        {
            dialog.Show();
            return false;
        }

        return await dialog.ShowDialog<bool>(owner);
    }
}
