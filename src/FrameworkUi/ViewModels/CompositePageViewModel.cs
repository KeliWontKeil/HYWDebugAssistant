using System.Collections.ObjectModel;
using System.Globalization;
using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using HYWDebugAssistant.Core.Api;
using HYWDebugAssistant.Sdk.Text;
using HYWDebugAssistant.Ui.ViewModels;

namespace HYWDebugAssistant.FrameworkUi.ViewModels;

/// <summary>本文件各视图模型的文本表入口（拥有者 = FrameworkUi 程序集）。</summary>
internal static class CompositeText
{
    /// <summary>取一条文案。</summary>
    public static string Get(string key) => TextCatalog.Get(typeof(CompositeText).Assembly, key);

    /// <summary>取一条带占位符的文案。</summary>
    public static string Get(string key, params object?[] args) =>
        TextCatalog.Get(typeof(CompositeText).Assembly, key, args);
}

/// <summary>
/// 组合页的一行：**已选模块**（只有名称；不报告运行状态）。
/// </summary>
public sealed partial class CompositeModuleRowViewModel : ViewModelBase
{
    /// <summary>创建一行。</summary>
    /// <param name="instanceId">实例 ID。</param>
    /// <param name="displayName">显示名（重名已加序号）。</param>
    public CompositeModuleRowViewModel(string instanceId, string displayName)
    {
        InstanceId = instanceId;
        DisplayName = displayName;
    }

    /// <summary>实例 ID（内部键，不显示）。</summary>
    public string InstanceId { get; }

    /// <summary>显示名（重名已加序号）。</summary>
    [ObservableProperty]
    public partial string DisplayName { get; set; }

    /// <summary>就地更新显示名（不重建行对象）。</summary>
    public void Apply(string displayName)
    {
        if (!string.Equals(DisplayName, displayName, StringComparison.Ordinal))
        {
            DisplayName = displayName;
        }
    }
}

/// <summary>
/// **组合页**：搜索/选择已有组合 → 加载；维护已选模块 → 保存。
/// </summary>
/// <remarks>
/// <list type="bullet">
///   <item>选择集是**模块窗口与组合页共用**的那个（<see cref="ModuleWindowsViewModel"/>）；本页只镜像已选项；</item>
///   <item>没有库、没有最近/当前文件/脏标记；保存/加载都是一次性动作；</item>
///   <item>可用组合只来自默认目录；外部文件先导入复制，之后才出现在列表。</item>
/// </list>
/// </remarks>
public sealed partial class CompositePageViewModel : ViewModelBase
{
    private readonly IRuntimeQuery _query;
    private readonly IRuntimeCommand _command;
    private readonly ModuleWindowsViewModel _windows;
    private readonly List<CompositeModuleRowViewModel> _rows = [];
    private readonly List<CompositeFileRowViewModel> _allCompositeFiles = [];
    private readonly string _suggestedFileName =
        CompositeText.Get("composite.suggestedName", DateTime.Now.ToString("yyyyMMdd-HHmmss", CultureInfo.CurrentCulture));

    /// <summary>创建组合页视图模型。</summary>
    public CompositePageViewModel(
        IRuntimeQuery query,
        IRuntimeCommand command,
        ModuleWindowsViewModel windows)
    {
        ArgumentNullException.ThrowIfNull(query);
        ArgumentNullException.ThrowIfNull(command);
        ArgumentNullException.ThrowIfNull(windows);

        _query = query;
        _command = command;
        _windows = windows;
        CompositeName = _suggestedFileName;
        _windows.SelectionChanged += (_, _) => Refresh();
    }

    /// <summary>已选模块（顺序 = 选择集顺序）。</summary>
    public ObservableCollection<CompositeModuleRowViewModel> Rows { get; } = [];

    /// <summary>可用组合（按搜索过滤；只来自默认目录）。</summary>
    public ObservableCollection<CompositeFileRowViewModel> CompositeFiles { get; } = [];

    /// <summary>搜索可用组合。</summary>
    [ObservableProperty]
    public partial string? CompositeSearchText { get; set; }

    partial void OnCompositeSearchTextChanged(string? value)
    {
        ApplyCompositeFilter();
        OnPropertyChanged(nameof(CompositeFilesEmptyText));
    }

    /// <summary>已选模块数。</summary>
    [ObservableProperty]
    public partial int SelectedCount { get; set; }

    /// <summary>现场模块总数。</summary>
    [ObservableProperty]
    public partial int TotalCount { get; set; }

    /// <summary>“已选 N/M”摘要。</summary>
    public string SelectionText => CompositeText.Get("composite.selection", SelectedCount, TotalCount);

    /// <summary>是否已选任何模块。</summary>
    public bool HasSelection => SelectedCount > 0;

    /// <summary>是否没有选中模块。</summary>
    public bool IsSelectionEmpty => SelectedCount == 0;

    /// <summary>是否可保存。</summary>
    public bool CanSave => HasSelection;

    /// <summary>当前选中的组合文件。</summary>
    [ObservableProperty]
    public partial CompositeFileRowViewModel? SelectedCompositeFile { get; set; }

    partial void OnSelectedCompositeFileChanged(CompositeFileRowViewModel? value)
    {
        OnPropertyChanged(nameof(CanLoadFile));
        OnPropertyChanged(nameof(CanDeleteFile));
        OnPropertyChanged(nameof(CanRenameFile));
    }

    /// <summary>是否可加载组合（选中一行后才启用）。</summary>
    public bool CanLoadFile => SelectedCompositeFile is not null;

    /// <summary>是否可删除组合（选中一行后才启用）。</summary>
    public bool CanDeleteFile => SelectedCompositeFile is not null;

    /// <summary>是否可重命名组合（选中一行后才启用）。</summary>
    public bool CanRenameFile => SelectedCompositeFile is not null;

    /// <summary>默认目录是否没有组合文件。</summary>
    [ObservableProperty]
    public partial bool IsCompositeFilesEmpty { get; set; } = true;

    /// <summary>可用组合空态文案（搜索无结果 ≠ 从未保存）。</summary>
    public string CompositeFilesEmptyText =>
        string.IsNullOrWhiteSpace(CompositeSearchText)
            ? CompositeText.Get("composite.empty.none")
            : CompositeText.Get("composite.empty.noMatch");

    /// <summary>是否存在“成员未选全”的网络。</summary>
    [ObservableProperty]
    public partial bool HasPartialNetworks { get; set; }

    /// <summary>部分网络提示（空 = 没有）。</summary>
    [ObservableProperty]
    public partial string? PartialNetworkHint { get; set; }

    /// <summary>默认组合文件目录。</summary>
    public string DefaultCompositeDirectory => _query.GetStorageLocations().CompositesRoot;

    /// <summary>保存对话框的建议文件名。</summary>
    public string SuggestedFileName => _suggestedFileName;

    /// <summary>组合名（保存到默认目录时使用；可编辑）。</summary>
    [ObservableProperty]
    public partial string CompositeName { get; set; } = string.Empty;

    /// <summary>状态提示（单行短句）。</summary>
    [ObservableProperty]
    public partial string? StatusMessage { get; set; }

    /// <summary>状态详情（ToolTip；完整路径/逐条失败）。</summary>
    [ObservableProperty]
    public partial string? StatusDetail { get; set; }

    /// <summary>本次操作是否成功。</summary>
    [ObservableProperty]
    public partial bool StatusIsOk { get; set; } = true;

    /// <summary>状态是否为错误。</summary>
    public bool StatusIsError => !StatusIsOk;

    partial void OnStatusIsOkChanged(bool value) => OnPropertyChanged(nameof(StatusIsError));

    partial void OnSelectedCountChanged(int value)
    {
        OnPropertyChanged(nameof(SelectionText));
        OnPropertyChanged(nameof(HasSelection));
        OnPropertyChanged(nameof(CanSave));
        OnPropertyChanged(nameof(IsSelectionEmpty));
    }

    partial void OnTotalCountChanged(int value) => OnPropertyChanged(nameof(SelectionText));

    /// <summary>组合页被激活：首次进入默认全选，刷新模块与组合列表。</summary>
    public void Activate()
    {
        _windows.Refresh(includeOverview: false);
        _windows.EnsureSelectionInitialized();
        Refresh();
        RefreshCompositeFiles();
    }

    /// <summary>按当前选择刷新已选模块行（不重建未变化行）。</summary>
    public void Refresh()
    {
        var instances = _query.GetInstances();
        TotalCount = instances.Count;

        var nameCounts = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var instance in instances)
        {
            var rawName = DisplayNameOf(instance);
            nameCounts.TryGetValue(rawName, out var count);
            nameCounts[rawName] = count + 1;
        }

        var ordinals = new Dictionary<string, int>(StringComparer.Ordinal);
        var displayById = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var instance in instances)
        {
            var rawName = DisplayNameOf(instance);
            nameCounts.TryGetValue(rawName, out var total);
            ordinals.TryGetValue(rawName, out var ordinal);
            ordinal++;
            ordinals[rawName] = ordinal;
            displayById[instance.InstanceId] = total > 1 ? $"{rawName} ({ordinal})" : rawName;
        }

        var selectedIds = _windows.SelectedInstanceIds;
        var existing = _rows.ToDictionary(row => row.InstanceId, StringComparer.Ordinal);
        var desired = new List<CompositeModuleRowViewModel>(selectedIds.Count);

        foreach (var instanceId in selectedIds)
        {
            if (!displayById.TryGetValue(instanceId, out var displayName))
            {
                continue;
            }

            if (existing.TryGetValue(instanceId, out var row))
            {
                row.Apply(displayName);
            }
            else
            {
                row = new CompositeModuleRowViewModel(instanceId, displayName);
            }

            desired.Add(row);
        }

        _rows.Clear();
        _rows.AddRange(desired);

        if (!Rows.SequenceEqual(desired))
        {
            Rows.Clear();
            foreach (var row in desired)
            {
                Rows.Add(row);
            }
        }

        SelectedCount = selectedIds.Count;
        UpdatePartialNetworkHint();
    }

    /// <summary>重新扫描默认目录中的组合文件（进入页面/刷新/保存/导入后调用）。</summary>
    /// <param name="preferredPath">希望选中的文件路径；空 = 保留当前选中项（若仍存在）。</param>
    public void RefreshCompositeFiles(string? preferredPath = null)
    {
        var previous = preferredPath ?? SelectedCompositeFile?.Path;

        _allCompositeFiles.Clear();
        foreach (var file in _query.GetCompositeFiles())
        {
            _allCompositeFiles.Add(new CompositeFileRowViewModel(file));
        }

        ApplyCompositeFilter(previous);
    }

    private void ApplyCompositeFilter(string? preferredPath = null)
    {
        var search = CompositeSearchText?.Trim();
        var filtered = string.IsNullOrEmpty(search)
            ? _allCompositeFiles.ToList()
            : _allCompositeFiles
                .Where(file => file.DisplayName.Contains(search, StringComparison.OrdinalIgnoreCase))
                .ToList();

        CompositeFiles.Clear();
        foreach (var file in filtered)
        {
            CompositeFiles.Add(file);
        }

        IsCompositeFilesEmpty = filtered.Count == 0;
        OnPropertyChanged(nameof(CompositeFilesEmptyText));

        var wanted = preferredPath ?? SelectedCompositeFile?.Path;
        SelectedCompositeFile = wanted is { Length: > 0 }
            ? filtered.FirstOrDefault(file => string.Equals(file.Path, wanted, StringComparison.OrdinalIgnoreCase))
            : null;
    }

    /// <summary>加载当前选中的组合文件。</summary>
    public void LoadSelectedComposite()
    {
        if (SelectedCompositeFile is not { } file)
        {
            SetStatus(false, CompositeText.Get("composite.status.selectFirst"));
            return;
        }

        Load(file.Path);
    }

    /// <summary>列出这次导入会撞名的项（只读；空 = 直接导入即可）。</summary>
    /// <param name="sourcePath">外部组合文件夹或组合文件。</param>
    /// <remarks>
    /// 询问由视图负责（VM 没有窗口）：先问这里，再把用户的选择作为决定表交给
    /// <see cref="ImportComposite(string, IReadOnlyDictionary{string, ImportConflictChoice}?)"/>。
    /// </remarks>
    public IReadOnlyList<ImportConflictView> GetImportConflicts(string sourcePath) =>
        _query.GetImportConflicts(sourcePath);

    /// <summary>导入外部组合：源是组合文件夹或组合文件；校验后复制到默认目录并选中。</summary>
    /// <param name="sourcePath">外部组合文件夹或组合文件。</param>
    /// <param name="conflicts">同名冲突的处置（键 = 来源全路径）；缺省 = 没有冲突。</param>
    /// <remarks>
    /// 状态行写的是**目标**名（不是源名）：同名换了名字时，用户看到的必须与列表里出现的一致。
    /// </remarks>
    public void ImportComposite(
        string sourcePath,
        IReadOnlyDictionary<string, ImportConflictChoice>? conflicts = null)
    {
        var result = _command.ImportComposite(sourcePath, conflicts);
        var name = DescribeTargetName(result.TargetPath, sourcePath);

        SetStatus(
            result.Succeeded,
            result.Skipped
                ? CompositeText.Get("composite.status.importSkipped", name)
                : result.Succeeded
                    ? CompositeText.Get("composite.status.imported", name)
                    : CompositeText.Get("composite.status.importFailed"),
            result.Succeeded
                ? string.Join(Environment.NewLine, result.Notices)
                : string.Join(Environment.NewLine, result.Failures));

        if (result.Succeeded && result.TargetPath is { Length: > 0 })
        {
            RefreshCompositeFiles(result.TargetPath);
        }
    }

    /// <summary>目标显示名：优先用落地的目标（目录名 / 去掉 .json）；没有目标时回落到源名。</summary>
    private static string DescribeTargetName(string? targetPath, string sourcePath)
    {
        if (targetPath is { Length: > 0 })
        {
            var directory = Path.GetDirectoryName(targetPath);
            return Path.GetFileName(directory) is { Length: > 0 } name
                ? name
                : Path.GetFileNameWithoutExtension(targetPath);
        }

        return Directory.Exists(sourcePath)
            ? Path.GetFileName(Path.TrimEndingDirectorySeparator(sourcePath))
            : Path.GetFileNameWithoutExtension(sourcePath);
    }

    /// <summary>删除当前选中的组合文件（只删文件，不影响现场模块）。</summary>
    public void DeleteSelectedComposite()
    {
        if (SelectedCompositeFile is not { } file)
        {
            SetStatus(false, CompositeText.Get("composite.status.selectFirst"));
            return;
        }

        var result = _command.DeleteCompositeFile(file.Path);
        SetStatus(
            result.Succeeded,
            result.Succeeded
                ? CompositeText.Get("composite.status.deleted", file.DisplayName)
                : CompositeText.Get("composite.status.deleteFailed"),
            string.Join(Environment.NewLine, result.Succeeded ? result.Notices : result.Failures));

        if (result.Succeeded)
        {
            RefreshCompositeFiles();
        }
    }

    /// <summary>重命名当前选中的组合文件（只改文件名）。</summary>
    /// <param name="newName">新名称（可带/不带 .json）。</param>
    public void RenameSelectedComposite(string newName)
    {
        if (SelectedCompositeFile is not { } file)
        {
            SetStatus(false, CompositeText.Get("composite.status.selectFirst"));
            return;
        }

        var result = _command.RenameCompositeFile(file.Path, newName);
        SetStatus(
            result.Succeeded,
            result.Succeeded
                ? CompositeText.Get("composite.status.renamed", newName)
                : CompositeText.Get("composite.status.renameFailed"),
            string.Join(Environment.NewLine, result.Failures));

        if (result.Succeeded && result.TargetPath is { Length: > 0 } target)
        {
            CompositeName = DisplayNameOfPath(target);
            RefreshCompositeFiles(target);
        }
    }

    /// <summary>按文档路径取显示名（包用目录名，旧件用文件主名）。</summary>
    public string DisplayNameOfPath(string path) =>
        _allCompositeFiles.FirstOrDefault(file => string.Equals(file.Path, path, StringComparison.OrdinalIgnoreCase))?.DisplayName
        ?? Path.GetFileNameWithoutExtension(path);

    /// <summary>更换当前选中组合的封面（全局图片选择器裁切后的 PNG）。</summary>
    /// <param name="png">PNG 字节。</param>
    public void SetSelectedCover(byte[] png)
    {
        if (SelectedCompositeFile is not { } file)
        {
            SetStatus(false, CompositeText.Get("composite.status.selectFirst"));
            return;
        }

        var result = _command.SetCompositeCoverPng(file.Path, png);
        SetStatus(
            result.Succeeded,
            result.Succeeded
                ? CompositeText.Get("composite.status.coverChanged")
                : CompositeText.Get("composite.status.coverFailed"),
            string.Join(Environment.NewLine, result.Succeeded ? result.Notices : result.Failures));

        if (result.Succeeded)
        {
            RefreshCompositeFiles(file.Path);
        }
    }

    /// <summary>全选现场全部模块（含隐藏/占位）。</summary>
    [RelayCommand]
    private void SelectAllLive() => _windows.SelectAll();

    /// <summary>清空选择。</summary>
    [RelayCommand]
    private void ClearSelection() => _windows.ClearSelection();

    /// <summary>从保存范围移出一个模块。</summary>
    /// <param name="instanceId">实例 ID。</param>
    [RelayCommand]
    private void RemoveModule(string? instanceId)
    {
        if (instanceId is { Length: > 0 })
        {
            _windows.ToggleSelection(instanceId);
        }
    }

    /// <summary>保存当前勾选为一个组合文件。</summary>
    public void Save(string filePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);

        var ids = _windows.SelectedInstanceIds;
        if (ids.Count == 0)
        {
            SetStatus(false, CompositeText.Get("composite.status.selectAtLeastOne"));
            return;
        }

        var name = Path.GetFileName(filePath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
        if (name.EndsWith(".json", StringComparison.OrdinalIgnoreCase))
        {
            name = name[..^5];
        }

        var result = _command.SaveComposite(filePath, name, ids);
        if (result.Succeeded)
        {
            CompositeName = name;
        }

        SetStatus(
            result.Succeeded,
            result.Succeeded
                ? CompositeText.Get("composite.status.saved", name)
                : CompositeText.Get("composite.status.saveFailed"),
            result.Succeeded
                ? filePath + FormatNotices(result.Notices)
                : string.Join(Environment.NewLine, result.Failures));

        Refresh();
        if (result.Succeeded)
        {
            RefreshCompositeFiles(filePath);
        }
    }

    /// <summary>默认目录下的目标**包目录**（组合名清洗后）。</summary>
    public string BuildDefaultSavePath()
    {
        var raw = string.IsNullOrWhiteSpace(CompositeName) ? _suggestedFileName : CompositeName.Trim();
        if (raw.EndsWith(".json", StringComparison.OrdinalIgnoreCase))
        {
            raw = raw[..^5];
        }

        var invalid = Path.GetInvalidFileNameChars();
        var clean = new string(raw.Select(ch => invalid.Contains(ch) ? '_' : ch).ToArray()).Trim();
        if (clean.Length == 0)
        {
            clean = _suggestedFileName;
        }

        return Path.Combine(DefaultCompositeDirectory, clean);
    }

    /// <summary>从默认列表加载一批模块（追加到当前现场）。</summary>
    public void Load(string filePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);

        var result = _command.LoadComposite(filePath);
        var displayName = DisplayNameOfPath(filePath);
        if (result.Succeeded)
        {
            _windows.Refresh(includeOverview: false);
            _windows.EnsureSelectionInitialized();
            _windows.SelectAll();
            CompositeName = displayName;
        }

        SetStatus(
            result.Succeeded,
            result.Succeeded
                ? CompositeText.Get("composite.status.loaded", displayName)
                : CompositeText.Get("composite.status.loadFailed"),
            result.Succeeded
                ? filePath + FormatNotices(result.Notices)
                : string.Join(Environment.NewLine, result.Failures));
        Refresh();
    }

    /// <summary>部分网络提示：只提示“成员被选了一部分”的网络（全部未选不算）。</summary>
    private void UpdatePartialNetworkHint()
    {
        var selected = _windows.SelectedInstanceIds.ToHashSet(StringComparer.Ordinal);
        var partial = 0;

        foreach (var network in _query.GetNetworks())
        {
            var ids = network.Sources
                .Concat(network.Targets)
                .Select(ParseInstanceId)
                .Where(id => id.Length > 0)
                .Distinct(StringComparer.Ordinal)
                .ToList();

            if (ids.Count == 0)
            {
                continue;
            }

            var selectedInNetwork = ids.Count(id => selected.Contains(id));
            if (selectedInNetwork > 0 && selectedInNetwork < ids.Count)
            {
                partial++;
            }
        }

        HasPartialNetworks = partial > 0;
        PartialNetworkHint = partial > 0
            ? CompositeText.Get("composite.partialNetworks", partial)
            : null;
    }

    private static string ParseInstanceId(string memberLiteral)
    {
        var separator = memberLiteral.LastIndexOf('.');
        return separator > 0 ? memberLiteral[..separator] : memberLiteral;
    }

    private static string DisplayNameOf(ModuleInstanceView instance) =>
        instance.IsPlaceholder ? CompositeText.Get("composite.missingModule") : instance.DisplayName;

    private static string FormatNotices(IReadOnlyList<string> notices) =>
        notices.Count == 0 ? string.Empty : Environment.NewLine + string.Join(Environment.NewLine, notices);

    private void SetStatus(bool ok, string message, string? detail = null)
    {
        StatusIsOk = ok;
        StatusMessage = message;
        StatusDetail = string.IsNullOrWhiteSpace(detail) ? message : detail;
    }

    /// <summary>由视图报告文件选择器失败（不抛到界面）。</summary>
    public void ReportError(string message) => SetStatus(false, message, message);
}
