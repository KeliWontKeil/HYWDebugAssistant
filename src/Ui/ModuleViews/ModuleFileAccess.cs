using Avalonia.Controls;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using HYWDebugAssistant.Sdk.Context;

namespace HYWDebugAssistant.Ui.ModuleViews;

/// <summary>
/// **文件选择的宿主实现**：模块说"帮我弹一次选择器"，这里在模块窗口上弹一次、把路径投回输入通道。
/// </summary>
/// <remarks>
/// <para>
/// 路径**只经输入通道回去**（与用户自己敲一个路径同一条路）：于是模块只需要认识一种收件方式。
/// </para>
/// <para>
/// 模块在自己的线程上调用，而选择器必须在 UI 线程上弹 ⇒ 这里切到 UI 线程执行；
/// 结果再经调用方给的投递函数回去（那条函数自己负责线程安全的入队）。
/// </para>
/// </remarks>
public sealed class ModuleFileAccess : IFileAccess
{
    private readonly Func<string, Window?> _windowFor;
    private readonly Action<string, string> _submit;

    /// <summary>造一个文件选择面。</summary>
    /// <param name="instanceId">实例 ID。</param>
    /// <param name="windowFor">按实例取它自己的窗口（对话框挂在那上面）。</param>
    /// <param name="submit">把选中的路径投回输入通道（源名, 路径）。</param>
    public ModuleFileAccess(
        string instanceId,
        Func<string, Window?> windowFor,
        Action<string, string> submit)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(instanceId);
        ArgumentNullException.ThrowIfNull(windowFor);
        ArgumentNullException.ThrowIfNull(submit);

        InstanceId = instanceId;
        _windowFor = windowFor;
        _submit = submit;
    }

    /// <summary>本面所属实例。</summary>
    public string InstanceId { get; }

    /// <inheritdoc />
    public void Request(FilePickRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        // 模块线程 → UI 线程（选择器只能在 UI 线程弹）。
        Dispatcher.UIThread.Post(() => _ = ShowAsync(request));
    }

    /// <summary>弹一次选择器并把结果投回。</summary>
    /// <param name="request">请求内容。</param>
    private async Task ShowAsync(FilePickRequest request)
    {
        if (_windowFor(InstanceId) is not { } window
            || TopLevel.GetTopLevel(window)?.StorageProvider is not { } storage
            || !storage.CanOpen)
        {
            return;
        }

        var types = BuildFileTypes(request.Filter);
        IStorageItem? item;

        switch (request.Mode)
        {
            case FilePickMode.Open:
                var opened = await storage.OpenFilePickerAsync(new FilePickerOpenOptions
                {
                    AllowMultiple = false,
                    FileTypeFilter = types,
                });
                item = opened.Count > 0 ? opened[0] : null;
                break;

            case FilePickMode.Save:
                item = await storage.SaveFilePickerAsync(new FilePickerSaveOptions
                {
                    SuggestedFileName = request.SuggestedFileName,
                    FileTypeChoices = types,
                });
                break;

            case FilePickMode.Folder:
                var folders = await storage.OpenFolderPickerAsync(new FolderPickerOpenOptions
                {
                    AllowMultiple = false,
                });
                item = folders.Count > 0 ? folders[0] : null;
                break;

            default:
                return;
        }

        if (item?.TryGetLocalPath() is { Length: > 0 } path)
        {
            _submit(request.SourceName, path);
        }
    }

    /// <summary>
    /// 把声明里的过滤串翻成系统选择器认识的类型表。
    /// </summary>
    /// <param name="filter">形如 <c>文本文件 (*.txt)|*.txt</c>；空 = 不过滤。</param>
    /// <remarks>
    /// 解析失败时**返回空表**（"不过滤"），而不是抛：过滤串只是提示，弹不出过滤器不该让选文件失败。
    /// </remarks>
    private static List<FilePickerFileType>? BuildFileTypes(string? filter)
    {
        if (string.IsNullOrWhiteSpace(filter))
        {
            return null;
        }

        var parts = filter.Split('|');
        if (parts.Length < 2)
        {
            return null;
        }

        var patterns = parts[1]
            .Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .ToArray();

        return patterns.Length == 0
            ? null
            : [new FilePickerFileType(parts[0].Trim()) { Patterns = patterns }];
    }
}
