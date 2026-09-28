using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using HYWDebugAssistant.Sdk.Views;
using HYWDebugAssistant.Ui.ViewModels;

namespace HYWDebugAssistant.Ui.Views;

/// <summary>
/// 模块窗口的**内容层**：按声明渲染元素（+ 窗内虚线参考线 + 人机操作三通道的输入行为）。
/// </summary>
/// <remarks>
/// 【缺陷修复 · M1-17 后】口标记与标签编辑已上移到 <see cref="ModuleWindow"/>：
/// 标记必须压在 6 px 拖动带与缩放手柄之上，否则双击标记会被边缘中点手柄吃掉（详见该类的说明）。
/// 【M1-16 / ADR-0040】单行文本输入：**回车 / 失焦一次提交**（D-5 裁决），不逐字符写入。
/// 【G2 · 2026-09】多行文本输入：提交键由**应用级设置**决定（`CtrlEnter` 默认 / `Enter` / `None`），
/// 判定依据全部来自 <see cref="TextInputElementViewModel"/> 的三个只读标志 —— XAML 里不写任何策略。
/// </remarks>
public partial class ModuleContentView : UserControl
{
    /// <summary>创建视图。</summary>
    public ModuleContentView() => InitializeComponent();

    /// <summary>输入框按键：按视图模型的提交策略决定"提交"还是"交给输入框换行"。</summary>
    private void OnInputKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter || sender is not TextBox { DataContext: TextInputElementViewModel input })
        {
            return;
        }

        var withControl = (e.KeyModifiers & KeyModifiers.Control) != 0;

        // 多行 + Ctrl+Enter 档位：只有 Ctrl+Enter 提交，裸回车**放行**给 TextBox 换行
        var shouldCommit = withControl ? input.SubmitOnCtrlEnter : input.SubmitOnEnter;

        if (!shouldCommit)
        {
            return;
        }

        e.Handled = true;
        input.CommitCommand.Execute(null);
    }

    /// <summary>输入框失焦：按策略提交（单行恒提交；多行仅在"回车提交"档位下提交）。</summary>
    private void OnInputLostFocus(object? sender, RoutedEventArgs e)
    {
        if (sender is TextBox { DataContext: TextInputElementViewModel { IsDirty: true } input }
            && input.SubmitOnLostFocus)
        {
            input.CommitCommand.Execute(null);
        }
    }

    /// <summary>
    /// **B6 文件选择器**的「浏览」点击（G4 · 2026-09）：弹**系统文件对话框**，把选/存的路径提交给模块。
    /// </summary>
    /// <remarks>
    /// <list type="bullet">
    ///   <item>这里才接触 `StorageProvider`（UI 概念）；模块**不碰**文件对话框 API，只在收件箱里收路径；</item>
    ///   <item>`async void` 是**事件处理**的合法形态（编码规则允许）；内部捕获异常转就地提示，**不逸出**；</item>
    ///   <item>无头环境没有真实对话框 ⇒ `StorageProvider` 可能抛/返回空 ⇒ 就地提示，不崩（无头用例只测渲染与 `Submit` 路径）。</item>
    /// </list>
    /// </remarks>
    private async void OnBrowseClick(object? sender, RoutedEventArgs e)
    {
        if (sender is not Button { DataContext: FileSelectorElementViewModel picker })
        {
            return;
        }

        var topLevel = TopLevel.GetTopLevel(this);
        if (topLevel?.StorageProvider is not { } storage)
        {
            picker.LastResult = "无法打开文件选择器";
            return;
        }

        try
        {
            var filter = BuildFileType(picker.Filter);
            string? path;

            if (picker.Mode == FilePickerMode.Open)
            {
                var files = await storage.OpenFilePickerAsync(new FilePickerOpenOptions
                {
                    Title = picker.Title,
                    AllowMultiple = false,
                    FileTypeFilter = filter is null ? null : [filter],
                });
                path = files is { Count: > 0 } ? files[0].TryGetLocalPath() : null;
            }
            else
            {
                var file = await storage.SaveFilePickerAsync(new FilePickerSaveOptions
                {
                    Title = picker.Title,
                    DefaultExtension = ExtensionOf(picker.Filter),
                    FileTypeChoices = filter is null ? null : [filter],
                });
                path = file?.TryGetLocalPath();
            }

            if (path is { Length: > 0 })
            {
                picker.Submit(path);
            }
        }
        catch (Exception ex)
        {
            // 事件处理不逸出：无头/无对话框环境或平台不支持 ⇒ 就地提示
            picker.LastResult = $"无法打开文件选择器：{ex.GetType().Name}";
        }
    }

    /// <summary>把声明的过滤文本转成 Avalonia 的 `FilePickerFileType`（空/非法 ⇒ null = 不过滤）。</summary>
    /// <param name="filter">形如 <c>*.txt</c> 或 <c>文本文件(*.txt)|*.txt</c>。</param>
    private static FilePickerFileType? BuildFileType(string? filter)
    {
        if (string.IsNullOrWhiteSpace(filter))
        {
            return null;
        }

        var patterns = filter
            .Split('|', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(p => p.StartsWith('*') || p.Contains('*', StringComparison.Ordinal))
            .ToArray();

        if (patterns.Length == 0)
        {
            patterns = [filter];
        }

        return new FilePickerFileType(filter) { Patterns = patterns };
    }

    /// <summary>从过滤文本猜一个默认扩展名（如 <c>*.txt</c> → <c>txt</c>；找不到 ⇒ null）。</summary>
    private static string? ExtensionOf(string? filter)
    {
        if (string.IsNullOrWhiteSpace(filter))
        {
            return null;
        }

        var star = filter.IndexOf('*', StringComparison.Ordinal);
        if (star >= 0 && star + 1 < filter.Length)
        {
            return filter[(star + 1)..].TrimStart('.').Split('|')[0].Trim();
        }

        return null;
    }
}
