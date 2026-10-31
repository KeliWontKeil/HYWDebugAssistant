using Avalonia.Controls;
using HYWDebugAssistant.Sdk.Text;
using HYWDebugAssistant.Ui.Views;

namespace HYWDebugAssistant.FrameworkUi.Views;

/// <summary>关闭工作区时的三选一：保存退出 / 不保存退出 / 取消。</summary>
public enum WorkspaceCloseChoice
{
    /// <summary>取消关闭。</summary>
    Cancel = 0,

    /// <summary>保存工作区并退出。</summary>
    Save = 1,

    /// <summary>丢弃本次修改并退出。</summary>
    Discard = 2,
}

/// <summary>关闭弹窗的完整结果：本次选择 + 是否记住。</summary>
/// <param name="Choice">本次选择。</param>
/// <param name="Remember">是否勾选「下次不再提醒」。</param>
public readonly record struct WorkspaceCloseResult(WorkspaceCloseChoice Choice, bool Remember);

/// <summary>关闭提示框。</summary>
public partial class WorkspaceCloseDialog : Window
{
    /// <summary>设计器用。</summary>
    public WorkspaceCloseDialog() => InitializeComponent();

    /// <summary>显示三选一并返回是否记住。</summary>
    /// <param name="owner">属主窗口。</param>
    /// <param name="moduleCount">当前模块数（用于说明）。</param>
    public static async Task<WorkspaceCloseResult> ShowAsync(Window owner, int moduleCount)
    {
        var dialog = new WorkspaceCloseDialog();
        dialog.MessageText.Text =
            TextCatalog.Get(typeof(WorkspaceCloseDialog).Assembly, "workspaceClose.message", moduleCount);

        WorkspaceCloseResult Result(WorkspaceCloseChoice choice) =>
            new(choice, dialog.RememberCheckBox.IsChecked == true);

        dialog.SaveButton.Click += (_, _) => dialog.Close(Result(WorkspaceCloseChoice.Save));
        dialog.DiscardButton.Click += (_, _) => dialog.Close(Result(WorkspaceCloseChoice.Discard));
        dialog.CancelButton.Click += (_, _) => dialog.Close(Result(WorkspaceCloseChoice.Cancel));

        var group = AppWindowGroup.Of(owner);
        group?.Track(dialog, AppWindowLayer.Modal);
        using var modal = group?.BeginModal(dialog);

        return await dialog.ShowDialog<WorkspaceCloseResult>(owner);
    }
}
