using Avalonia.Controls;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using HYWDebugAssistant.Sdk.Context;

namespace HYWDebugAssistant.Ui.ModuleSurface.Hosting;

/// <summary>
/// **文件选择请求面的宿主实现**：把模块的"弹一次系统选择器"请求落到某个模块窗口上，
/// 选定后把路径**经输入通道投回**给模块。
/// </summary>
/// <remarks>
/// <para>
/// <b>它住在 UI 侧的唯一理由</b>：系统选择器要一个宿主窗口（<c>TopLevel.StorageProvider</c>），
/// 而"哪个窗口属于哪个实例"只有窗口集控制器知道。L4/模块都拿不到窗口，也不该拿。
/// </para>
/// <para>
/// <b>结果只有一条路</b>：选定后按 <see cref="FilePickRequest.SourceName"/> 调 <c>SubmitInput</c>
/// —— 与"用户自己敲进去一个路径"**完全同一条通道**。不另开结果通道，
/// 否则输入契约就有两份（收件箱只有出、没有入）。
/// </para>
/// <para>
/// <b>对话框是异步的</b>：请求进来时**立即返回**，真正的等待发生在 UI 线程上；
/// 同一源名上的未完成请求会被后一次替换（连点两次只该有一个结果）。
/// </para>
/// </remarks>
public sealed class ModuleFileAccess : IFileAccess
{
    private readonly Func<string, TopLevel?> _topLevelOf;
    private readonly Action<string, string> _deliver;
    private readonly Dictionary<string, int> _pending = new(StringComparer.Ordinal);

    /// <summary>造一份按实例绑定的请求面。</summary>
    /// <param name="instanceId">本份请求面所属的模块实例。</param>
    /// <param name="topLevelOf">按实例取宿主窗口（找不到 ⇒ <see langword="null"/>）。</param>
    /// <param name="deliver">把选定的路径投回模块的输入通道（参数：源名、路径）。</param>
    public ModuleFileAccess(
        string instanceId,
        Func<string, TopLevel?> topLevelOf,
        Action<string, string> deliver)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(instanceId);
        ArgumentNullException.ThrowIfNull(topLevelOf);
        ArgumentNullException.ThrowIfNull(deliver);

        InstanceId = instanceId;
        _topLevelOf = topLevelOf;
        _deliver = deliver;
    }

    /// <summary>本份请求面所属的模块实例。</summary>
    public string InstanceId { get; }

    /// <inheritdoc />
    public void Request(FilePickRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (string.IsNullOrWhiteSpace(request.SourceName))
        {
            return;
        }

        // **同源名的未完成请求被替换**：用户连点两次只该有一个结果，多弹几个对话框是明显的错。
        var generation = _pending.TryGetValue(request.SourceName, out var current) ? current + 1 : 1;
        _pending[request.SourceName] = generation;

        // 请求从模块线程来 ⇒ 落回 UI 线程执行（框架不跨线程碰 UI）。
        Dispatcher.UIThread.Post(() => _ = RunAsync(request, generation));
    }

    private async Task RunAsync(FilePickRequest request, int generation)
    {
        var top = _topLevelOf(InstanceId);

        if (top?.StorageProvider is not { } storage)
        {
            // **打不开就明说**：不能静默 —— 否则用户以为"点了没反应"，模块也永远收不到路径。
            _deliver(request.SourceName, string.Empty);
            return;
        }

        try
        {
            IStorageItem? picked = request.Mode switch
            {
                FilePickMode.Folder => await storage.OpenFolderPickerAsync(
                    new FolderPickerOpenOptions { AllowMultiple = false }) is { Count: > 0 } folders
                    ? folders[0]
                    : null,
                FilePickMode.Save => await storage.SaveFilePickerAsync(new FilePickerSaveOptions
                {
                    SuggestedFileName = request.SuggestedFileName,
                    FileTypeChoices = BuildFileTypes(request.Filter),
                }),
                _ => await storage.OpenFilePickerAsync(new FilePickerOpenOptions
                {
                    AllowMultiple = false,
                    FileTypeFilter = BuildFileTypes(request.Filter),
                }) is { Count: > 0 } files
                    ? files[0]
                    : null,
            };

            // **过期的那一次不投**：期间用户又点了一次，结果以最后一次为准。
            if (_pending.TryGetValue(request.SourceName, out var latest) && latest != generation)
            {
                return;
            }

            if (picked is null)
            {
                // 用户取消 ⇒ 投一个空串（"这次没有结果"），模块据此什么也不做。
                _deliver(request.SourceName, string.Empty);
                return;
            }

            _deliver(request.SourceName, picked.TryGetLocalPath() ?? picked.Path.ToString());
        }
        catch (Exception)
        {
            // 选择失败 ⇒ 同样以"空结果"收场（模块自己决定要不要报一句）。
            _deliver(request.SourceName, string.Empty);
        }
    }

    /// <summary>把声明面的过滤串（"说明 (*.txt)|*.txt"）翻成系统选择器认识的形状。</summary>
    /// <param name="filter">过滤串。</param>
    /// <returns>系统选择器的类型清单；无过滤 ⇒ <see langword="null"/>。</returns>
    private static List<FilePickerFileType>? BuildFileTypes(string? filter)
    {
        if (filter is not { Length: > 0 })
        {
            return null;
        }

        var parts = filter.Split('|', StringSplitOptions.TrimEntries);
        if (parts.Length < 2)
        {
            return null;
        }

        return [new FilePickerFileType(parts[0]) { Patterns = [parts[1]] }];
    }
}
