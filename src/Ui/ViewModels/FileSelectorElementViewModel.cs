using CommunityToolkit.Mvvm.ComponentModel;
using HYWDebugAssistant.Core.Api;
using HYWDebugAssistant.Sdk.Views;

namespace HYWDebugAssistant.Ui.ViewModels;

/// <summary>
/// **B6 文件选择器**（G4 · 2026-09）：一个会弹**系统文件对话框**的输入元素。
/// </summary>
/// <remarks>
/// <list type="bullet">
///   <item><b>分工</b>：**视图层**负责弹对话框（本类只暴露 <see cref="Submit"/> 与模式/过滤器），
///     用户选/存的**路径**经既有输入通道（`SubmitInput`）投递到模块 —— 模块在活动体里 `TryDequeueInput` 拿到；
///     模块**不接触文件对话框 API**；</item>
///   <item><b>产出的值</b> = 一个**路径字符串**；故它绑定到一个**文本型数据源**（`ViewDataSourceKind.Value`）；</item>
///   <item><b>与"无活动循环的模块不消费收件箱"一致</b>：要收路径，模块必须实现 <c>IActivitySource</c>；</item>
///   <item><b>弹窗本身无法无头测试</b>（系统对话框）：无头用例断言**渲染**（标签 + 只读路径框 + 浏览按钮可见）
///     与 **`Submit` 路径**（模块真的收到路径）；真正"点浏览弹窗"归**手工验收**（`implementation/04`）。</item>
/// </list>
/// </remarks>
public sealed partial class FileSelectorElementViewModel : ViewElementViewModel
{
    private readonly Func<string, string, CommandResult>? _submit;

    /// <summary>创建文件选择器视图模型。</summary>
    /// <param name="element">元素声明（`DataSource` = 收路径的数据源名；`FileMode` / `FileFilter`）。</param>
    /// <param name="submit">提交回调（源名 + 路径 → 结果）。</param>
    public FileSelectorElementViewModel(ViewElement element, Func<string, string, CommandResult>? submit = null)
        : base(ViewElementKind.FileSelector, element.Title, element.DataSource)
    {
        ArgumentNullException.ThrowIfNull(element);
        Source = element.DataSource ?? string.Empty;
        Mode = element.FileMode;
        Filter = element.FileFilter;
        _submit = submit;
    }

    /// <summary>收路径的数据源名（= 视图声明里该元素的 <c>DataSource</c>）。</summary>
    public string Source { get; }

    /// <summary>打开 / 保存。</summary>
    public FilePickerMode Mode { get; }

    /// <summary>文件类型过滤（空 = 不过滤）。</summary>
    public string? Filter { get; }

    /// <summary>最近一次选/存的路径（展示在只读框里；也方便用户确认）。</summary>
    [ObservableProperty]
    public partial string? SelectedPath { get; set; }

    /// <summary>就地提示（如"无法打开文件选择器"）。</summary>
    [ObservableProperty]
    public partial string? LastResult { get; set; }

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
        LastResult = result.Succeeded
            ? result.Notices.Count > 0 ? result.Notices[^1] : "已写入"
            : $"未生效：{string.Join("；", result.Failures)}";
    }
}
