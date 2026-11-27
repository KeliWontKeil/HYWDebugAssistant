using Avalonia.Controls;
using HYWDebugAssistant.FrameworkUi.Views;
using Xunit;

namespace HYWDebugAssistant.VisualSnapshots;

/// <summary>
/// 导入同名项的询问框截图（<c>dialogs\import-conflict.png</c>）。
/// </summary>
/// <remarks>
/// <list type="bullet">
///   <item><b>不是判据</b>：这里只出图；按钮顺序、勾选框位置、说明文字换行好不好看，都由人眼看图。</item>
///   <item>这个窗口的风险面是"三键一勾"的排布：勾选框藏在按钮上方、说明行在正文下面，
///     漏掉任何一件都不会编译报错，只是用户看不见那个选项。</item>
/// </list>
/// </remarks>
[Collection("visual-snapshots")]
public class ImportConflictSnapshots
{
    /// <summary>生成同名询问框的截图。</summary>
    [Fact]
    public async Task 生成快照_导入同名项()
    {
        await SnapshotSession.Shared.Dispatch(
            () =>
            {
                // 直接构造并填内容：ShowAsync 会进模态循环，截图不需要那个。
                var dialog = new ImportConflictDialog
                {
                    Title = TextCatalogGet("importConflict.title"),
                };

                // 具名控件是 XAML 生成的内部字段，外部只能按名字取。
                dialog.FindControl<TextBlock>("MessageText")!.Text =
                    TextCatalogGet("importConflict.message", "串口示波器");
                dialog.FindControl<CheckBox>("ApplyToAllCheckBox")!.IsVisible = true;

                dialog.Show();
                SnapshotTool.Pump();
                SnapshotTool.Save(dialog, "dialogs/import-conflict.png");
                SnapshotTool.DumpTree(dialog, "dialogs/import-conflict-tree.txt");
                dialog.Close();
            },
            CancellationToken.None);
    }

    private static string TextCatalogGet(string key, params object?[] args) =>
        HYWDebugAssistant.Sdk.Text.TextCatalog.Get(
            typeof(ImportConflictDialog).Assembly,
            key,
            args);
}
