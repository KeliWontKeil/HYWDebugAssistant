using Avalonia.Controls;
using HYWDebugAssistant.Core.Api;
using HYWDebugAssistant.Sdk.Text;
using HYWDebugAssistant.Ui.Views;

namespace HYWDebugAssistant.FrameworkUi.Views;

/// <summary>一次同名询问的结果。</summary>
/// <param name="Choice">替换还是跳过。</param>
/// <param name="ApplyToAll">是否「为所有同名项执行此操作」。</param>
public readonly record struct ImportConflictAnswer(ImportConflictChoice Choice, bool ApplyToAll);

/// <summary>
/// **导入同名项的询问**：逐条问「替换 / 跳过 / 取消」，可一次决定整批。
/// </summary>
/// <remarks>
/// 对应资源管理器复制撞名时的做法：不静默改名，也不静默覆盖。取消 = 整批中止，
/// 已经导入成功的项保持原样（不提供回滚 —— 回滚要删用户刚写入的内容，风险比留着更大）。
/// </remarks>
public partial class ImportConflictDialog : Window
{
    /// <summary>创建对话框。</summary>
    public ImportConflictDialog() => InitializeComponent();

    /// <summary>问一项。</summary>
    /// <param name="owner">属主窗口。</param>
    /// <param name="targetName">会被占用的名字。</param>
    /// <param name="remaining">这一批还剩多少项（1 = 最后一项，不显示勾选框）。</param>
    /// <returns>用户的选择；关窗取消 ⇒ <see langword="null"/>。</returns>
    public static async Task<ImportConflictAnswer?> ShowAsync(
        Window? owner,
        string targetName,
        int remaining)
    {
        var dialog = new ImportConflictDialog();

        dialog.MessageText.Text = TextCatalog.Get(
            typeof(ImportConflictDialog).Assembly,
            "importConflict.message",
            targetName);

        // 只剩一项时"应用到全部"没有意义，隐藏它以免给出错误暗示。
        dialog.ApplyToAllCheckBox.IsVisible = remaining > 1;

        ImportConflictAnswer? Answer(ImportConflictChoice choice) =>
            new ImportConflictAnswer(choice, dialog.ApplyToAllCheckBox.IsChecked == true);

        dialog.ReplaceButton.Click += (_, _) => dialog.Close(Answer(ImportConflictChoice.Replace));
        dialog.SkipButton.Click += (_, _) => dialog.Close(Answer(ImportConflictChoice.Skip));
        dialog.CancelButton.Click += (_, _) => dialog.Close(null);

        var group = AppWindowGroup.Of(owner);
        group?.Track(dialog, AppWindowLayer.Modal);
        using var modal = group?.BeginModal(dialog);

        // 没有属主（无处可弹）⇒ 按取消处理，交给调用方给出可见的原因。
        return owner is null ? null : await dialog.ShowDialog<ImportConflictAnswer?>(owner);
    }

    /// <summary>
    /// 逐条询问，直到问完、或用户勾了「应用到全部」、或用户取消。
    /// </summary>
    /// <param name="owner">属主窗口。</param>
    /// <param name="conflicts">待问的冲突（顺序即询问顺序）。</param>
    /// <returns>
    /// 决定表（键 = 来源全路径）；<see langword="null"/> = 用户取消，**整批都不应执行**。
    /// </returns>
    /// <remarks>
    /// 空表与 null 是两件事：空表表示"没有冲突要处置"（照常导入），null 表示"别导了"。
    /// 用 null 而不是空表，是为了不把"取消"变成"全部按未决定失败"的一串报错。
    /// </remarks>
    public static async Task<IReadOnlyDictionary<string, ImportConflictChoice>?> ResolveAsync(
        Window? owner,
        IReadOnlyList<ImportConflictView> conflicts)
    {
        ArgumentNullException.ThrowIfNull(conflicts);

        var decisions = new Dictionary<string, ImportConflictChoice>(StringComparer.OrdinalIgnoreCase);
        if (conflicts.Count == 0)
        {
            return decisions;
        }

        for (var index = 0; index < conflicts.Count; index++)
        {
            var conflict = conflicts[index];
            var answer = await ShowAsync(owner, conflict.TargetName, conflicts.Count - index);

            if (answer is not { } picked)
            {
                return null;
            }

            if (picked.ApplyToAll)
            {
                for (var rest = index; rest < conflicts.Count; rest++)
                {
                    decisions[conflicts[rest].SourcePath] = picked.Choice;
                }

                return decisions;
            }

            decisions[conflict.SourcePath] = picked.Choice;
        }

        return decisions;
    }
}
