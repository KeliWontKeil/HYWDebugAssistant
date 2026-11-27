using System.Collections.ObjectModel;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using HYWDebugAssistant.Core.Api;
using HYWDebugAssistant.Sdk.Text;
using HYWDebugAssistant.Ui.Controls;
using HYWDebugAssistant.Ui.Images;
using HYWDebugAssistant.Ui.ViewModels;

namespace HYWDebugAssistant.FrameworkUi.ViewModels;

/// <summary>本文件各视图模型的文本表入口（拥有者 = FrameworkUi 程序集）。</summary>
internal static class CatalogText
{
    /// <summary>取一条文案。</summary>
    public static string Get(string key) => TextCatalog.Get(typeof(CatalogText).Assembly, key);

    /// <summary>取一条带占位符的文案。</summary>
    public static string Get(string key, params object?[] args) =>
        TextCatalog.Get(typeof(CatalogText).Assembly, key, args);
}

/// <summary>可用模块行（**界面不显示模块标识字面量**，只在内部用于加入现场 —— 意见 3）。</summary>
/// <param name="ModuleId">模块标识字面量（加入现场用，不显示）。</param>
/// <param name="DisplayName">展示名。</param>
/// <param name="Description">描述。</param>
/// <param name="Capabilities">能力声明。</param>
/// <param name="SourceDirectory">装入该模块的来源根（诊断用）。</param>
/// <param name="NeedsRestart">是否"本次启动后被重新构建过"（需重启应用才生效；框架不实现热重载）。</param>
/// <param name="CategoryKey">分类键（由标签推导；见 <see cref="ModuleCategories"/>）。</param>
/// <param name="IsExternal">是否来自默认目录下的外部导入区（`modules\external`）。</param>
/// <param name="PreviewImage">预览图（无图 ⇒ 占位字形）。</param>
/// <param name="TypeRing">预览图块的类型色外圈（无类型的模块为 <see langword="null"/>）。</param>
public sealed record ModuleCatalogRowViewModel(
    string ModuleId,
    string DisplayName,
    string Description,
    string Capabilities,
    string? SourceDirectory = null,
    bool NeedsRestart = false,
    string CategoryKey = ModuleCategories.Other,
    bool IsExternal = false,
    Bitmap? PreviewImage = null,
    TypeRingViewModel? TypeRing = null)
{
    /// <summary>整行悬停提示：标题 + 换行 + 描述（描述为空就只标题）。</summary>
    /// <remarks>条目根 `Grid` 有透明背景 ⇒ 整行任意位置都能出提示，而不是只有压在文字上。</remarks>
    public string TooltipText
    {
        get
        {
            var text = Description.Length > 0
                ? DisplayName + Environment.NewLine + Description
                : DisplayName;

            // 类型色环是**只可意会**的视觉线索：悬停时补一句"这圈颜色是什么"，
            // 否则用户只能靠猜（类型标识本身不显示 —— 它挤占行宽且是给框架用的内部标识）。
            return TypeRing is { HasRing: true } ring
                ? text + Environment.NewLine + CatalogText.Get("catalog.typeRing.tip", string.Join('、', ring.TypeKeys))
                : text;
        }
    }

    /// <summary>来源标注（内置/外部；需要重启时并入同一行，保持列表行高一致）。</summary>
    public string OriginNotice =>
        (IsExternal ? CatalogText.Get("catalog.origin.external") : CatalogText.Get("catalog.origin.builtin"))
        + (NeedsRestart ? CatalogText.Get("catalog.origin.needsRestart") : string.Empty);

    /// <summary>居中裁切的预览画刷（无图 ⇒ null）。</summary>
    public ImageBrush? PreviewBrush => WallpaperImageCache.Preview(PreviewImage);

    /// <summary>是否有预览图（无图显示占位字形）。</summary>
    public bool HasPreview => PreviewImage is not null;
}

/// <summary>
/// **模块清单的一行**：分类标题或可用模块。
/// </summary>
/// <param name="Key">稳定键：标题行与模块行不会互相覆盖。</param>
/// <param name="Entry">模块行；标题行为 <see langword="null"/>。</param>
/// <param name="Header">分类标题；模块行为 <see langword="null"/>。</param>
/// <remarks>
/// 标题必须是**独立列表项**，不能塞进模块行模板：否则 ListBox 的选中高亮会把标题一起框住，
/// 且键盘/选择逻辑会把标题当成可选中对象。容器由 <c>ModuleCatalogListBox</c> 明确设为不可选。
/// </remarks>
public sealed record ModuleCatalogListItem(
    string Key,
    ModuleCatalogRowViewModel? Entry = null,
    string? Header = null)
{
    /// <summary>是否分类标题行。</summary>
    public bool IsHeader => Entry is null;

    /// <summary>是否模块行。</summary>
    public bool IsModule => Entry is not null;

    /// <summary>模块展示名（标题行为空）。</summary>
    public string DisplayName => Entry?.DisplayName ?? string.Empty;

    /// <summary>模块来源标注（标题行为空）。</summary>
    public string OriginNotice => Entry?.OriginNotice ?? string.Empty;

    /// <summary>模块预览画刷（标题行为空）。</summary>
    public ImageBrush? PreviewBrush => Entry?.PreviewBrush;

    /// <summary>是否显示预览占位字形（标题行不显示）。</summary>
    public bool ShowPreviewPlaceholder => Entry is { HasPreview: false };

    /// <summary>整行悬停提示。</summary>
    public string TooltipText => Entry?.TooltipText ?? Header ?? string.Empty;
}

/// <summary>
/// **模块分类**：按模块**声明的标签**归类（不声明归"其他"）。
/// </summary>
/// <remarks>
/// 分类是**界面组织方式**，不改机制；标签来自 `ModuleDefinitionAttribute.Tags`（模块作者声明）。
/// 规范标签 `port/source/terminal/converter/other` 优先；旧的 `sender/monitor/...` 仍兼容。
/// </remarks>
public static class ModuleCategories
{
    /// <summary>全部（不过滤）。</summary>
    public const string All = "all";

    /// <summary>端口（串口 / TCP / UDP 等连接端点）。</summary>
    public const string Port = "port";

    /// <summary>数据源（产生数据：发生器 / 手动发送器）。</summary>
    public const string Source = "source";

    /// <summary>终端（接收并呈现数据：显示器 / 监视器）。</summary>
    public const string Terminal = "terminal";

    /// <summary>转换器（有端口、在数据流中转换数据）。</summary>
    public const string Converter = "converter";

    /// <summary>其他（不操作数据流的离线工具，或未声明可识别标签）。</summary>
    public const string Other = "other";

    /// <summary>分类目录（键 · 图标键 · 悬停说明）。</summary>
    /// <remarks>图标键指向 Ui 的图标表；分类之间同码位的与外壳复用同一键（不新造重复条目）。</remarks>
    public static readonly IReadOnlyList<(string Key, string IconKey, string Title)> Definitions =
    [
        (All, ShellIcons.CatalogAll, CatalogText.Get("catalog.category.all")),
        (Port, ShellIcons.CatalogPort, CatalogText.Get("catalog.category.port")),
        (Source, ShellIcons.CatalogSource, CatalogText.Get("catalog.category.source")),
        (Terminal, ShellIcons.CatalogTerminal, CatalogText.Get("catalog.category.terminal")),
        (Converter, ShellIcons.CatalogConverter, CatalogText.Get("catalog.category.converter")),
        (Other, ShellIcons.CatalogOther, CatalogText.Get("catalog.category.other")),
    ];

    /// <summary>分类在「全部模块」列表里的排列序号（越小越靠前）。</summary>
    /// <param name="categoryKey">分类键。</param>
    public static int Rank(string categoryKey)
    {
        for (var index = 1; index < Definitions.Count; index++)
        {
            if (string.Equals(Definitions[index].Key, categoryKey, StringComparison.Ordinal))
            {
                return index;
            }
        }

        return Definitions.Count;
    }

    /// <summary>取分类标题（未知 → 其他）。</summary>
    /// <param name="categoryKey">分类键。</param>
    public static string Title(string categoryKey) =>
        Definitions.FirstOrDefault(item =>
            string.Equals(item.Key, categoryKey, StringComparison.Ordinal)).Title
        ?? CatalogText.Get("catalog.category.other");

    /// <summary>按标签推导分类键（不识别 → <see cref="Other"/>）。</summary>
    /// <param name="tags">模块声明的标签。</param>
    public static string FromTags(IReadOnlyList<string>? tags)
    {
        if (tags is null || tags.Count == 0)
        {
            return Other;
        }

        foreach (var tag in tags)
        {
            var value = tag.ToLowerInvariant();
            switch (value)
            {
                case "port" or "serial" or "tcp" or "udp" or "io":
                    return Port;

                case "generator" or "sender" or "source" or "input" or "producer":
                    return Source;

                case "terminal" or "monitor" or "display" or "view" or "sink" or "receiver" or "reader":
                    return Terminal;

                case "converter" or "protocol" or "transform" or "conversion":
                    return Converter;

                case "other" or "tool" or "recorder" or "logger" or "asserter" or "assert" or "test":
                    return Other;

                default:
                    break;
            }
        }

        return Other;
    }
}

/// <summary>分类按钮（界面：横向图标按钮 + 悬停说明）。</summary>
/// <param name="Key">分类键。</param>
/// <param name="IconKey">图标键（指向 Ui 的图标表）。</param>
/// <param name="Title">悬停说明。</param>
/// <param name="Count">该分类下的模块数。</param>
public sealed partial class ModuleCategoryViewModel(string key, string iconKey, string title, int count)
    : ViewModelBase
{
    /// <summary>分类键。</summary>
    public string Key { get; } = key;

    /// <summary>图标键（`Glyph` 按键取字形）。</summary>
    public string IconKey { get; } = iconKey;

    /// <summary>悬停说明。</summary>
    public string Title { get; } = title;

    /// <summary>该分类下的模块数（悬停说明里也带上）。</summary>
    [ObservableProperty]
    public partial int Count { get; set; } = count;

    /// <summary>是否当前选中（选中态视觉）。</summary>
    [ObservableProperty]
    public partial bool IsSelected { get; set; }
}

/// <summary>占位实例行（模块定义缺失/版本不兼容 → 可指定替代模块）。</summary>
/// <param name="InstanceId">实例 ID。</param>
/// <param name="ModuleId">原模块标识字面量（占位信息，诊断用）。</param>
public sealed record PlaceholderRowViewModel(string InstanceId, string ModuleId);

/// <summary>
/// **模块清单弹窗**：可用模块 · 加入现场 · 显式加载 · 占位实例补救。
/// </summary>
/// <remarks>
/// <list type="bullet">
///   <item><b>没有"连接管理"</b>：建网唯一机制 = 模块窗口里口上的网络标签；</item>
///   <item><b>没有实例清单</b>：**每个模块实例都有自己的窗口**（未声明视图的模块同样开窗，窗内给出配置表单）——
///     「无界面模块」不是例外：模块的运行/停用/隐藏/移除都在**它自己的窗口**上操作；</item>
///   <item><b>没有 JSON 编辑</b>：配置一律走 **Schema 生成表单**（与模块窗口内同一份定义）；
///     界面不出现配置类型完整名等代码化标识。</item>
/// </list>
/// </remarks>
public sealed partial class ModuleCatalogViewModel : ViewModelBase
{
    private readonly IRuntimeQuery _query;
    private readonly IRuntimeCommand _command;
    private int _instanceCounter;

    /// <summary>创建模块清单视图模型。</summary>
    /// <param name="query">查询面。</param>
    /// <param name="command">命令面。</param>
    public ModuleCatalogViewModel(IRuntimeQuery query, IRuntimeCommand command)
    {
        ArgumentNullException.ThrowIfNull(query);
        ArgumentNullException.ThrowIfNull(command);

        _query = query;
        _command = command;
    }

    /// <summary>可用模块目录（**已按分类 + 搜索过滤**；标题行与模块行是独立列表项）。</summary>
    public ObservableCollection<ModuleCatalogListItem> Catalog { get; } = [];

    /// <summary>分类按钮（横向；图标 + 悬停说明）。</summary>
    public ObservableCollection<ModuleCategoryViewModel> Categories { get; } = [];

    /// <summary>未过滤的全量模块（分类与搜索在内存里过滤，避免每帧扫状态）。</summary>
    private readonly List<ModuleCatalogRowViewModel> _allModules = [];

    /// <summary>上一次消费的目录/实例快照：引用都没变 ⇒ 这一帧没有任何目录工作。</summary>
    private IReadOnlyList<ModuleCatalogEntryView>? _lastCatalog;
    private IReadOnlyList<ModuleInstanceView>? _lastInstances;

    /// <summary>搜索文本（按名称/描述/标签匹配；空 = 不过滤）。</summary>
    [ObservableProperty]
    public partial string? SearchText { get; set; }

    /// <summary>当前分类键。</summary>
    [ObservableProperty]
    public partial string SelectedCategory { get; set; } = ModuleCategories.All;

    partial void OnSearchTextChanged(string? value) => ApplyFilter();

    partial void OnSelectedCategoryChanged(string value) => ApplyFilter();

    /// <summary>切换分类（界面按钮调用）。</summary>
    /// <param name="category">分类按钮。</param>
    [RelayCommand]
    private void SelectCategory(ModuleCategoryViewModel? category)
    {
        if (category is not null)
        {
            SelectedCategory = category.Key;
        }
    }

    /// <summary>按"分类 + 搜索"过滤可见模块（列表用增量同步，避免闪烁）。</summary>
    private void ApplyFilter()
    {
        var category = SelectedCategory;
        var search = SearchText?.Trim();
        var selectedModuleId = SelectedCatalogEntry?.ModuleId;

        var filtered = _allModules
            .Where(row =>
                (string.Equals(category, ModuleCategories.All, StringComparison.Ordinal)
                    || string.Equals(row.CategoryKey, category, StringComparison.Ordinal))
                && (string.IsNullOrEmpty(search)
                    || row.DisplayName.Contains(search, StringComparison.OrdinalIgnoreCase)
                    || row.Description.Contains(search, StringComparison.OrdinalIgnoreCase)))
            .OrderBy(row => ModuleCategories.Rank(row.CategoryKey))
            .ThenBy(row => row.DisplayName, StringComparer.CurrentCultureIgnoreCase)
            .ToList();

        var items = new List<ModuleCatalogListItem>(filtered.Count + ModuleCategories.Definitions.Count);
        var previousCategory = string.Empty;
        foreach (var row in filtered)
        {
            if (string.Equals(category, ModuleCategories.All, StringComparison.Ordinal)
                && !string.Equals(row.CategoryKey, previousCategory, StringComparison.Ordinal))
            {
                items.Add(new ModuleCatalogListItem(
                    $"header:{row.CategoryKey}",
                    Header: ModuleCategories.Title(row.CategoryKey)));
            }

            items.Add(new ModuleCatalogListItem($"module:{row.ModuleId}", Entry: row));
            previousCategory = row.CategoryKey;
        }

        _ = CollectionSync.Sync(Catalog, items);

        foreach (var item in Categories)
        {
            item.IsSelected = string.Equals(item.Key, SelectedCategory, StringComparison.Ordinal);
        }

        // **选中项按模块标识重绑**：列表增删/插入分类标题会让容器索引变化，不能只信旧引用。
        // 过滤后不再包含原选中项 ⇒ 清掉选择（避免"看不见却已选中"）。
        SelectedCatalogItem = selectedModuleId is { Length: > 0 }
            ? Catalog.FirstOrDefault(item =>
                string.Equals(item.Entry?.ModuleId, selectedModuleId, StringComparison.Ordinal))
            : null;
    }

    /// <summary>重建分类按钮与其计数。</summary>
    private void RebuildCategories()
    {
        foreach (var (key, iconKey, title) in ModuleCategories.Definitions)
        {
            var count = string.Equals(key, ModuleCategories.All, StringComparison.Ordinal)
                ? _allModules.Count
                : _allModules.Count(m => string.Equals(m.CategoryKey, key, StringComparison.Ordinal));

            if (Categories.FirstOrDefault(c => string.Equals(c.Key, key, StringComparison.Ordinal)) is { } existing)
            {
                existing.Count = count;
                continue;
            }

            Categories.Add(new ModuleCategoryViewModel(key, iconKey, title, count));
        }
    }

    /// <summary>占位实例（可指定替代模块）。</summary>
    public ObservableCollection<PlaceholderRowViewModel> Placeholders { get; } = [];

    private ModuleCatalogListItem? _selectedCatalogItem;

    /// <summary>选中的列表项（模块行；标题行不可选）。</summary>
    /// <remarks>按引用判断：列表增量同步会重建等值项，只比较值会让 ListBox 继续指向旧对象。</remarks>
    public ModuleCatalogListItem? SelectedCatalogItem
    {
        get => _selectedCatalogItem;
        set
        {
            if (ReferenceEquals(_selectedCatalogItem, value))
            {
                return;
            }

            _selectedCatalogItem = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(SelectedCatalogEntry));
        }
    }

    /// <summary>选中的可用模块（标题行 / 未选择时为 <see langword="null"/>）。</summary>
    public ModuleCatalogRowViewModel? SelectedCatalogEntry => _selectedCatalogItem?.Entry;

    /// <summary>选中的占位实例。</summary>
    [ObservableProperty]
    public partial PlaceholderRowViewModel? SelectedPlaceholder { get; set; }

    /// <summary>状态提示（单行短句）。</summary>
    [ObservableProperty]
    public partial string? StatusMessage { get; set; }

    /// <summary>状态详情（ToolTip；完整路径/逐条结果）。</summary>
    [ObservableProperty]
    public partial string? StatusDetail { get; set; }

    /// <summary>上次操作是否成功。</summary>
    [ObservableProperty]
    public partial bool StatusIsOk { get; set; } = true;

    /// <summary>状态是否为错误（XAML 着色用）。</summary>
    public bool StatusIsError => !StatusIsOk;

    partial void OnStatusIsOkChanged(bool value) => OnPropertyChanged(nameof(StatusIsError));

    /// <summary>请求**保存现场并重启应用**（重扫发现需重启的包时）；由外壳/主窗口执行。</summary>
    public event EventHandler? RestartRequested;

    /// <summary>本次扫描发现的“需重启”的包数。</summary>
    [ObservableProperty]
    public partial int NeedsRestartCount { get; set; }

    /// <summary>需重启提示（空 = 没有）。</summary>
    public string RestartHint => NeedsRestartCount > 0
        ? CatalogText.Get("catalog.restartHint", NeedsRestartCount)
        : string.Empty;

    /// <summary>是否有包需要重启。</summary>
    public bool HasNeedsRestart => NeedsRestartCount > 0;

    partial void OnNeedsRestartCountChanged(int value)
    {
        OnPropertyChanged(nameof(RestartHint));
        OnPropertyChanged(nameof(HasNeedsRestart));
    }

    /// <summary>
    /// 把选中的模块加入现场（**按钮与"双击列表项"共用这一条路径**）。
    /// </summary>
    /// <returns>是否成功加入。</returns>
    /// <remarks>
    /// 加入现场有两个入口：单击选中 → 再点「加入现场」，以及双击列表项直接加入（列表项的常见惯例）。
    /// 故把动作抽成公开方法，两个入口共用同一实现（不复制逻辑）。
    /// </remarks>
    public bool AddSelected()
    {
        if (SelectedCatalogEntry is not { } entry)
        {
            SetStatus(false, CatalogText.Get("catalog.status.selectModule"));
            return false;
        }

        var instanceId = NextInstanceId(entry.ModuleId);
        var result = _command.AddModule(instanceId, entry.ModuleId);
        SetStatus(
            result.Succeeded,
            result.Succeeded
                ? CatalogText.Get("catalog.status.added", entry.DisplayName)
                : CatalogText.Get("catalog.status.failed", string.Join("；", result.Failures)));
        Refresh();
        return result.Succeeded;
    }

    /// <summary>把选中的模块加入现场（按钮）。</summary>
    [RelayCommand]
    private void Add() => AddSelected();

    /// <summary>为占位实例指定替代模块（用可用模块中选中的那一项）。</summary>
    [RelayCommand]
    private void Resolve()
    {
        if (SelectedPlaceholder is not { } placeholder || SelectedCatalogEntry is not { } entry)
        {
            SetStatus(false, CatalogText.Get("catalog.status.selectPlaceholder"));
            return;
        }

        var result = _command.ResolvePlaceholder(placeholder.InstanceId, entry.ModuleId);
        SetStatus(
            result.Succeeded,
            result.Succeeded
                ? CatalogText.Get("catalog.status.replaced", entry.DisplayName)
                : CatalogText.Get("catalog.status.failed", string.Join("；", result.Failures)));
        Refresh();
    }

    /// <summary>请求选择外部模块目录（由视图打开文件夹选择器）。</summary>
    public event EventHandler? ImportModulesRequested;

    /// <summary>打开外部模块目录选择器。</summary>
    [RelayCommand]
    private void ImportModules() => ImportModulesRequested?.Invoke(this, EventArgs.Empty);

    /// <summary>导入外部模块目录（视图选好目录后调用）。</summary>
    /// <param name="directory">外部包目录或包含多个包的上级目录。</param>
    public void ImportDirectory(string directory)
    {
        if (string.IsNullOrWhiteSpace(directory))
        {
            return;
        }

        var result = _command.ImportModuleDirectory(directory);
        SetStatus(
            result.Succeeded,
            result.Succeeded
                ? CatalogText.Get("catalog.status.imported")
                : CatalogText.Get("catalog.status.importFailed"),
            string.Join(Environment.NewLine, result.Succeeded ? result.Notices : result.Failures));
        Refresh();
    }

    /// <summary>把拖入无法识别内容的原因写进页面操作状态（不走应用底部提示）。</summary>
    /// <param name="message">完整原因。</param>
    public void ReportImportError(string message) => SetStatus(false, message, message);

    /// <summary>
    /// 导入一个外层文件夹：向下最多 3 层找模块包与组合包，逐个导入并汇总到页面状态。
    /// </summary>
    /// <param name="directory">外层文件夹。</param>
    public void ImportFolder(string directory)
    {
        if (string.IsNullOrWhiteSpace(directory))
        {
            return;
        }

        var result = _command.ImportFolder(directory);
        var detail = string.Join(Environment.NewLine, result.Failures.Concat(result.Notices));

        if (result.ImportedCount == 0 && !result.HasFailures)
        {
            SetStatus(false, CatalogText.Get("catalog.status.importFolderNone"));
        }
        else if (result.ImportedCount == 0)
        {
            SetStatus(false, CatalogText.Get("catalog.status.importFailed"), detail);
        }
        else if (result.HasFailures)
        {
            SetStatus(
                false,
                CatalogText.Get(
                    "catalog.status.importFolderPartial",
                    result.ImportedCount,
                    result.Failures.Count),
                detail);
        }
        else
        {
            SetStatus(
                true,
                CatalogText.Get(
                    "catalog.status.importFolder",
                    result.ModuleCount,
                    result.CompositeCount),
                string.Join(Environment.NewLine, result.Notices));
        }

        Refresh();
    }

    /// <summary>
    /// **立刻重扫**默认模块目录：装入新增的模块包（改过代码的包需重启应用才生效）。
    /// </summary>
    [RelayCommand]
    private void Rescan()
    {
        var result = _command.RescanModuleSources();

        Refresh();
        SetStatus(
            result.Succeeded,
            result.Succeeded
                ? CatalogText.Get("catalog.status.rescanDone")
                : CatalogText.Get("catalog.status.rescanFailed"),
            result.Succeeded
                ? string.Join(Environment.NewLine, result.Notices)
                : string.Join(Environment.NewLine, result.Failures));

        // 扫描发现"本次启动后重新构建过"的包：只弹一次重启确认（由外壳复用现有重启对话框）。
        if (result.Succeeded && HasNeedsRestart)
        {
            RestartRequested?.Invoke(this, EventArgs.Empty);
        }
    }

    /// <summary>
    /// 按刷新节拍同步目录与清单（保持选中项）。
    /// </summary>
    /// <remarks>
    /// **必须用增量同步**：面板按刷新率（默认 60 Hz）刷新，若每帧 <c>Clear()</c> + 重加，
    /// 列表项容器会被整批重建 —— 表现为"鼠标悬停时所有项乱闪"（瞬时视觉状态每帧被重置）。
    /// 见 <see cref="CollectionSync"/>。
    /// </remarks>
    public void Refresh()
    {
        var catalogEntries = _query.GetModuleCatalog();
        var instances = _query.GetInstances();

        if (ReferenceEquals(catalogEntries, _lastCatalog) && ReferenceEquals(instances, _lastInstances))
        {
            return;
        }

        _lastCatalog = catalogEntries;
        _lastInstances = instances;

        var catalog = catalogEntries
            .Select(entry => new ModuleCatalogRowViewModel(
                entry.ModuleId,
                entry.DisplayName,
                entry.Description,
                entry.Capabilities,
                entry.SourceDirectory,
                entry.NeedsRestart,
                ModuleCategories.FromTags(entry.Tags),
                entry.IsExternal,
                PreviewImageStore.Load(entry.PreviewImagePath),
                CreateTypeRing(entry.TypeKeys)))
            .ToList();

        // 全量保存 + 分类计数 + 应用"分类 + 搜索"过滤（列表用增量同步，避免每帧重建 → 闪烁）
        _allModules.Clear();
        _allModules.AddRange(catalog);
        NeedsRestartCount = _allModules.Count(row => row.NeedsRestart);
        RebuildCategories();
        ApplyFilter();

        // 选中项由 ApplyFilter 按模块标识重绑；没有选择时不自动选中（避免「扫一眼列表就被选中」的错觉）。
        var selectedPlaceholderId = SelectedPlaceholder?.InstanceId;

        var placeholders = instances
            .Where(instance => instance.IsPlaceholder)
            .Select(instance => new PlaceholderRowViewModel(instance.InstanceId, instance.ModuleId))
            .ToList();

        // 非占位实例**一律不出现在本弹窗**：每个实例都有自己的窗口（含未声明视图的模块），
        // 运行/停用/隐藏/移除/配置都在它的窗口上完成。
        _ = CollectionSync.Sync(Placeholders, placeholders);

        if (selectedPlaceholderId is { Length: > 0 })
        {
            SelectedPlaceholder = Placeholders.FirstOrDefault(p => string.Equals(p.InstanceId, selectedPlaceholderId, StringComparison.Ordinal));
        }
    }

    /// <summary>
    /// 造一行的**类型色外圈**；同一组类型标识复用**同一个**环实例。
    /// </summary>
    /// <param name="typeKeys">模块声明端口承载的类型标识（空 ⇒ 无环）。</param>
    /// <returns>环；没有类型时为 <see langword="null"/>。</returns>
    /// <remarks>
    /// 列表按刷新节拍重建行模型，而 <see cref="CollectionSync"/> 靠**值相等**判断"要不要换实例"
    /// ⇒ 环相等必须由身份（签名）决定，见 <see cref="TypeRingViewModel"/>;
    /// 这里再做一层"同签名复用实例"，使得用户改色时只需对**一个**环发通知，
    /// 而不是等下一次刷新各自重算（也能避免每次刷新都分配新环）。
    /// </remarks>
    private TypeRingViewModel? CreateTypeRing(IReadOnlyList<string>? typeKeys)
    {
        if (typeKeys is not { Count: > 0 })
        {
            return null;
        }

        var signature = string.Join(',', typeKeys);
        if (_typeRings.TryGetValue(signature, out var existing))
        {
            return existing;
        }

        var ring = new TypeRingViewModel(typeKeys);
        _typeRings[signature] = ring;
        return ring;
    }

    /// <summary>
    /// **用户改了某个类型的颜色** ⇒ 让对应的环重新取色（清单页可见时才需要）。
    /// </summary>
    /// <remarks>
    /// 色板是静态单例，改色不经过视图模型 ⇒ 不主动通知的话，环会一直显示旧色，
    /// 直到一次"重新扫描"把行模型整批重建（用户会觉得"改了没生效"）。
    /// </remarks>
    public void RefreshTypeRings()
    {
        foreach (var ring in _typeRings.Values)
        {
            ring.RefreshBrush();
        }
    }

    /// <summary>已造过的环（按类型签名索引）：复用实例 + 改色时统一刷新。</summary>
    private readonly Dictionary<string, TypeRingViewModel> _typeRings = new(StringComparer.Ordinal);

    private string NextInstanceId(string moduleIdLiteral)
    {
        var parts = moduleIdLiteral.Split('/');
        var name = parts.Length >= 2 ? parts[1] : "module";

        while (true)
        {
            var candidate = $"{name}-{++_instanceCounter}";
            if (_query.GetInstances().All(i => !string.Equals(i.InstanceId, candidate, StringComparison.Ordinal)))
            {
                return candidate;
            }
        }
    }

    private void SetStatus(bool ok, string message, string? detail = null)
    {
        StatusIsOk = ok;
        StatusMessage = message;
        StatusDetail = string.IsNullOrWhiteSpace(detail) ? message : detail;
    }
}
