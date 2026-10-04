using CommunityToolkit.Mvvm.ComponentModel;
using HYWDebugAssistant.Core.Api;
using HYWDebugAssistant.Sdk.Views;

namespace HYWDebugAssistant.Ui.ViewModels;

/// <summary>
/// **B6 文件选择器**：一个会弹**系统文件对话框**的输入元素 —— 界面上就是**一个按钮**。
/// </summary>
/// <remarks>
/// <list type="bullet">
///   <item><b>分工</b>：**视图层**负责弹对话框（本类只暴露 <see cref="Submit"/> 与模式/过滤器/默认文件名），
///     用户选/存的**路径**经既有输入通道（`SubmitInput`）投递到模块 —— 模块在活动体里 `TryDequeueInput` 拿到；
///     模块**不接触文件对话框 API**；</item>
///   <item><b>产出的值</b> = 一个**路径字符串**；故它绑定到一个**文本型数据源**；</item>
///   <item><b>为什么只有一个按钮</b>：多控件方案（标签 + 只读路径框 + 「浏览…」+ 独立的「保存」按钮）
///     要两步操作、更占位置，而且只读路径框会让人误以为路径可以手改 ⇒ 只留**一个按钮**：
///     点击后弹窗选位置 → **选择完成即自动保存在此处**。
///     ⇒ 按**按钮文案 = 声明标题**渲染，路径不单独占一行，落点确认由按钮下方那一行就地给出
///     （`LastResult` / <see cref="IsInvalid"/>，来自模块的输入反馈）；</item>
///   <item><b>与"无活动循环的模块不消费收件箱"一致</b>：要收路径，模块必须实现 <c>IActivitySource</c>；</item>
///   <item><b>弹窗本身无法无头测试</b>（系统对话框）：无头用例断言**渲染**（按钮可见、文案 = 声明标题）
///     与 **`Submit` 路径**（模块真的收到路径）；真正"点按钮弹窗"归**手工验收**。</item>
/// </list>
/// </remarks>
public sealed partial class FileSelectorElementViewModel : ViewElementViewModel
{
    /// <summary>声明面 `fileMode` 的取值 —— 打开文件（schema 的缺省）。</summary>
    public const string FileOpenMode = "open";

    /// <summary>声明面 `fileMode` 的取值 —— 保存文件。</summary>
    public const string FileSaveMode = "save";

    /// <summary>声明面 `fileMode` 的取值 —— 选目录。</summary>
    public const string FileFolderMode = "folder";

    private readonly Func<string, string, CommandResult>? _submit;

    /// <summary>创建文件选择器视图模型。</summary>
    /// <param name="node">
    /// 元素声明（输入绑定 = 收路径的数据源名；`Title` = 按钮文案；`FileMode` / `FileFilter` / `SuggestedFileName`）。
    /// </param>
    /// <param name="submit">提交回调（源名 + 路径 → 结果）。</param>
    public FileSelectorElementViewModel(ViewNode node, Func<string, string, CommandResult>? submit = null)
        : base(node)
    {
        ArgumentNullException.ThrowIfNull(node);
        Source = node.Input ?? string.Empty;
        FileMode = node.FileMode;
        Filter = node.FileFilter;
        SuggestedFileName = node.SuggestedFileName;
        _submit = submit;
    }

    /// <summary>收路径的数据源名（= 视图声明里该元素的输入绑定名）。</summary>
    public string Source { get; }

    /// <summary>打开 / 保存（声明面字面量：`open` / `save` / `folder`）。</summary>
    public string FileMode { get; }

    /// <summary>是否**打开**对话框（`open` / `folder` = 打开；其余 = 保存）。</summary>
    public bool IsOpen => FileMode is FileOpenMode or FileFolderMode;

    /// <summary>是否选**目录**（`folder`）。</summary>
    public bool IsFolder => string.Equals(FileMode, FileFolderMode, StringComparison.Ordinal);

    /// <summary>文件类型过滤（空 = 不过滤）。</summary>
    public string? Filter { get; }

    /// <summary>保存对话框的**默认文件名**（空 = 不预填）。</summary>
    public string? SuggestedFileName { get; }

    /// <summary>按钮文案（= 声明里的 `Title`；没写标题时回退到「浏览…」）。</summary>
    public string ButtonText => Title is { Length: > 0 } title ? title : "浏览…";

    /// <summary>最近一次选/存的路径（按钮的悬停提示用它确认"上次是哪儿"）。</summary>
    [ObservableProperty]
    public partial string? SelectedPath { get; set; }

    /// <summary>
    /// 就地结果行（模块回报的落点确认 / 失败原因）。
    /// </summary>
    /// <remarks>失败态由 <see cref="IsInvalid"/> 标红；成功态也显示（弹过系统对话框 ⇒ 值得给一次落点确认）。</remarks>
    [ObservableProperty]
    public partial string? LastResult { get; set; }

    /// <summary>
    /// **处理失败态**：模块经 <c>IModuleContext.InputFeedback</c> 回报"这次选的位置没用上"⇒ 红字。
    /// </summary>
    /// <remarks>由**框架**从查询面（<c>IRuntimeQuery.GetInputFeedback</c>）写入；模块不持 UI，无法自己标红。</remarks>
    [ObservableProperty]
    public partial bool IsInvalid { get; set; }

    /// <summary>把用户选/存的路径提交给模块（视图层在拿到对话框结果后调用）。</summary>
    /// <param name="path">路径。</param>
    public void Submit(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        SelectedPath = path;

        if (_submit is not { } submit)
        {
            LastResult = "内部错误：未连接到运行时";
            return;
        }

        var result = submit(Source, path);

        // **成功不在这里下结论**：路径只是"投递出去了"，真正的结果（写盘成功 / 失败）由**模块**回报，
        // 经输入反馈通道回到 `ApplyInputFeedback` ⇒ 这里只报"没投出去"这种就地可判的失败。
        LastResult = result.Succeeded
            ? null
            : $"未生效：{string.Join("；", result.Failures)}";
    }
}
