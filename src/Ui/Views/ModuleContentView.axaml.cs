using System.Text.RegularExpressions;
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
/// 单行文本输入：**回车 / 失焦一次提交**，不逐字符写入。
/// 多行文本输入：提交键由**应用级设置**决定（`CtrlEnter` 默认 / `Enter` / `None`），
/// 判定依据全部来自 <see cref="TextInputElementViewModel"/> 的三个只读标志 —— XAML 里不写任何策略。
/// **数值输入**（`ViewElement.InputKind = Number`）：只**放行数字写法**（见 <see cref="NumberPattern"/>），
/// 挂在 `TextInput` 事件的**隧道**阶段拦截字符 —— 与"提交策略在 KeyDown 里判"同一处、同一风格。
/// </remarks>
public partial class ModuleContentView : UserControl
{
    /// <summary>
    /// **数值输入放行的文本形态**（`InputKind = Number`）：可选负号 + 数字 + 可选小数点 + 数字。
    /// </summary>
    /// <remarks>
    /// <list type="bullet">
    ///   <item>**只判形态**：范围 / 是否整数 / 业务合法性**都不在这里** —— 那是模块的事（用输入反馈通道回报 ⇒ 红框）；</item>
    ///   <item>允许"空"（用户清空）、"-"（刚敲下负号）、"1."（小数点刚敲下）这些**中间态**，
    ///     否则用户根本没法把数打完；</item>
    ///   <item>不支持科学计数法（`1e3`）：当前需求（间隔 ms 这类参数）用不上，需要时再放宽
    ///     —— 放宽属兼容扩展（只增放行字符）。</item>
    /// </list>
    /// </remarks>
    private static readonly Regex NumberPattern = new(@"^-?[0-9]*\.?[0-9]*$", RegexOptions.Compiled);

    /// <summary>
    /// **十六进制输入放行的文本形态**（`InputKind = Hex`）：只允许十六进制数字**与空白**。
    /// </summary>
    /// <remarks>
    /// **空白是排版**（HEX 的换行不作为字符，仅作为排版）⇒ 框架允许用户用空格/换行
    /// 自行分组；**分组怎么解读、每几个字符一个字节，由模块解析时定**（框架只保证"非空白字符都是 hex 数字"）。
    /// </remarks>
    private static readonly Regex HexPattern = new(@"^[0-9A-Fa-f\s]*$", RegexOptions.Compiled);

    /// <summary>按输入类型取**放行规则**（`null` = 不限制字符）。</summary>
    /// <param name="inputKind">声明面的输入类型字面量（`text` / `number` / `hex`；缺省由 schema 补 `text`）。</param>
    private static Regex? PatternOf(string inputKind) => inputKind switch
    {
        TextInputElementViewModel.NumberInputKind => NumberPattern,
        TextInputElementViewModel.HexInputKind => HexPattern,
        _ => null,
    };

    /// <summary>创建视图。</summary>
    public ModuleContentView()
    {
        InitializeComponent();

        // 数值输入的**字符放行**：隧道阶段拦在 TextBox 处理之前（`TextInput` 事件带来"将插入的文本"）
        AddHandler(TextInputEvent, OnInputTextInput, RoutingStrategies.Tunnel);

        // **提交策略挂在本层**：输入框内部的那一个 `TextBox` 由 `TextSurface` 拥有 ⇒ 不能再在模板里给它写 `KeyDown=`/`LostFocus=`；
        // 改为在本控件（输入框的祖先）上按**隧道**阶段接 `KeyDown`（多行框自己会吃掉回车 ⇒ 冒泡阶段根本到不了这里）、
        // 按冒泡阶段接 `LostFocus`。两者都用 `e.Source` 找那个输入框 —— **可见行为逐字不变**。
        AddHandler(KeyDownEvent, OnInputKeyDown, RoutingStrategies.Tunnel);
        AddHandler(LostFocusEvent, OnInputLostFocus);
    }

    /// <summary>
    /// **受限输入只放行合法字符**（`InputKind = Number` / `Hex`）——其余字符吞掉，不进入输入框。
    /// </summary>
    /// <remarks>
    /// 判定用"**插入后**的文本"是否匹配对应规则（而不是只看这一个字符）——
    /// 这样才能同时表达"负号只能在最前""小数点只能有一个"这类位置约束，规则一处写清。
    /// </remarks>
    private void OnInputTextInput(object? sender, TextInputEventArgs e)
    {
        // 隧道阶段 `sender` 是本控件（处理器挂在 UserControl 上）⇒ 用 `e.Source` 找那个输入框
        if (e.Source is not TextBox { DataContext: TextInputElementViewModel input } box
            || string.IsNullOrEmpty(e.Text)
            || PatternOf(input.InputKind) is not { } pattern)
        {
            return;
        }

        var text = box.Text ?? string.Empty;
        var start = Math.Clamp(box.SelectionStart, 0, text.Length);
        var end = Math.Clamp(box.SelectionEnd, start, text.Length);
        var after = text[..start] + e.Text + text[end..];

        if (!pattern.IsMatch(after))
        {
            e.Handled = true;   // 吞掉：非法字符不进输入框（用户不会"先写错再被拒"）
        }
    }

    /// <summary>输入框按键：按视图模型的提交策略决定"提交"还是"交给输入框换行"。</summary>
    /// <remarks>
    /// 挂在**隧道**阶段（本控件是输入框的祖先）：多行框对回车是"自己处理掉"的 ⇒ 冒泡阶段收不到；
    /// 隧道阶段则一定先经过这里，判定"不提交"就原样放行给文本框换行。用 <c>e.Source</c> 找输入框。
    /// </remarks>
    private void OnInputKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter || e.Source is not TextBox { DataContext: TextInputElementViewModel input })
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
        if (e.Source is TextBox { DataContext: TextInputElementViewModel { IsDirty: true } input }
            && input.SubmitOnLostFocus)
        {
            input.CommitCommand.Execute(null);
        }
    }

    /// <summary>
    /// **B6 文件选择器**的「浏览」点击：弹**系统文件对话框**，把选/存的路径提交给模块。
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

            if (picker.IsFolder)
            {
                var folders = await storage.OpenFolderPickerAsync(new FolderPickerOpenOptions
                {
                    Title = picker.Title,
                    AllowMultiple = false,
                });
                path = folders is { Count: > 0 } ? folders[0].TryGetLocalPath() : null;
            }
            else if (picker.IsOpen)
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

                    // 保存对话框默认预填一个文件名（如 savetext1.txt），不空着
                    // ⇒ 模块在元素声明里给一句建议名；用户仍可改成任何名字 / 换目录（框架不代替用户决定路径）。
                    SuggestedFileName = picker.SuggestedFileName,
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
