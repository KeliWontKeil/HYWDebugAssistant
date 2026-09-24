using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using HYWDebugAssistant.Core.Api;
using HYWDebugAssistant.Ui.ViewModels;

namespace HYWDebugAssistant.FrameworkUi.ViewModels;

/// <summary>可用模块行（**界面不显示模块标识字面量**，只在内部用于加入现场 —— 意见 3）。</summary>
/// <param name="ModuleId">模块标识字面量（加入现场用，不显示）。</param>
/// <param name="DisplayName">展示名。</param>
/// <param name="Description">描述。</param>
/// <param name="Capabilities">能力声明。</param>
/// <param name="SourceDirectory">装入该模块的来源根（诊断用）。</param>
/// <param name="NeedsRestart">是否"本次启动后被重新构建过"（需重启应用才生效；框架不实现热重载）。</param>
public sealed record ModuleCatalogRowViewModel(
    string ModuleId,
    string DisplayName,
    string Description,
    string Capabilities,
    string? SourceDirectory = null,
    bool NeedsRestart = false)
{
    /// <summary>需重启提示（空 = 不需要）。</summary>
    public string RestartNotice => NeedsRestart ? "该包在本次启动后重新构建 —— 重启应用才生效" : string.Empty;
}

/// <summary>占位实例行（模块定义缺失/版本不兼容 → 可指定替代模块，§9.3）。</summary>
/// <param name="InstanceId">实例 ID。</param>
/// <param name="ModuleId">原模块标识字面量（占位信息，诊断用）。</param>
public sealed record PlaceholderRowViewModel(string InstanceId, string ModuleId);

/// <summary>
/// **模块清单弹窗**（R-3 / `11` §4）：可用模块 · 加入现场 · 显式加载 · 占位实例补救。
/// </summary>
/// <remarks>
/// <list type="bullet">
///   <item><b>没有"连接管理"</b>（R-4 删除）：建网唯一机制 = 模块窗口里口上的网络标签；</item>
///   <item><b>没有实例清单</b>：**每个模块实例都有自己的窗口**（未声明视图的模块同样开窗，窗内给出配置表单）——
///     "无界面模块"这一例外概念已按用户裁决撤销；模块的运行/停用/隐藏/移除都在**它自己的窗口**上操作；</item>
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

    /// <summary>可用模块目录。</summary>
    public ObservableCollection<ModuleCatalogRowViewModel> Catalog { get; } = [];

    /// <summary>占位实例（可指定替代模块）。</summary>
    public ObservableCollection<PlaceholderRowViewModel> Placeholders { get; } = [];

    /// <summary>模块包目录（显式加载入口）。</summary>
    [ObservableProperty]
    public partial string? PackageDirectory { get; set; }

    /// <summary>选中的可用模块。</summary>
    [ObservableProperty]
    public partial ModuleCatalogRowViewModel? SelectedCatalogEntry { get; set; }

    /// <summary>选中的占位实例。</summary>
    [ObservableProperty]
    public partial PlaceholderRowViewModel? SelectedPlaceholder { get; set; }

    /// <summary>选中的来源根（打开目录用）。</summary>
    [ObservableProperty]
    public partial string? SelectedSource { get; set; }

    /// <summary>模块包目录提示（内置模块包位置与"该选哪个目录"）。</summary>
    public string PackageHint { get; } =
        $@"内置模块包已自动装入（应用目录下 {Path.Combine(AppContext.BaseDirectory, "modules")}）。"
        + @"显式加载第三方模块包时，请选择**构建产物目录**（含 module.json 与实现程序集的目录），不要选源码目录。";

    /// <summary>状态提示。</summary>
    [ObservableProperty]
    public partial string? StatusMessage { get; set; }

    /// <summary>上次操作是否成功。</summary>
    [ObservableProperty]
    public partial bool StatusIsOk { get; set; } = true;

    /// <summary>请求选择模块包目录（由视图打开文件夹选择器）。</summary>
    public event EventHandler? PickPackageDirectoryRequested;

    /// <summary>加载模块包并注册（显式加载）。</summary>
    [RelayCommand]
    private void Install()
    {
        if (string.IsNullOrWhiteSpace(PackageDirectory))
        {
            SetStatus(false, "请先选择模块包目录");
            return;
        }

        var result = _command.InstallModulePackage(PackageDirectory);
        SetStatus(result.Succeeded, result.Succeeded ? "模块包已加载" : $"失败：{string.Join("；", result.Failures)}");
        Refresh();
    }

    /// <summary>请求打开文件夹选择器。</summary>
    [RelayCommand]
    private void PickPackageDirectory() => PickPackageDirectoryRequested?.Invoke(this, EventArgs.Empty);

    /// <summary>把选中的模块加入现场。</summary>
    [RelayCommand]
    private void Add()
    {
        if (SelectedCatalogEntry is not { } entry)
        {
            SetStatus(false, "请先在可用模块中选择一个模块");
            return;
        }

        var instanceId = NextInstanceId(entry.ModuleId);
        var result = _command.AddModule(instanceId, entry.ModuleId);
        SetStatus(result.Succeeded, result.Succeeded ? $"已加入：{entry.DisplayName}" : $"失败：{string.Join("；", result.Failures)}");
        Refresh();
    }

    /// <summary>为占位实例指定替代模块（用可用模块中选中的那一项）。</summary>
    [RelayCommand]
    private void Resolve()
    {
        if (SelectedPlaceholder is not { } placeholder || SelectedCatalogEntry is not { } entry)
        {
            SetStatus(false, "请选中一个占位实例，并在可用模块中选中替代模块");
            return;
        }

        var result = _command.ResolvePlaceholder(placeholder.InstanceId, entry.ModuleId);
        SetStatus(result.Succeeded, result.Succeeded ? $"已指定替代模块：{entry.DisplayName}" : $"失败：{string.Join("；", result.Failures)}");
        Refresh();
    }

    /// <summary>来源根扫描诊断（ADR-0036 D1：让用户看到"到底扫了哪些目录、每个目录发生了什么"）。</summary>
    public ObservableCollection<ModuleSourceRowViewModel> Sources { get; } = [];

    /// <summary>请求在文件管理器里打开某个来源根（由视图实现）。</summary>
    public event EventHandler<string>? OpenSourceDirectoryRequested;

    /// <summary>打开选中的来源根。</summary>
    [RelayCommand]
    private void OpenSourceDirectory()
    {
        if (SelectedSource is { Length: > 0 } directory)
        {
            OpenSourceDirectoryRequested?.Invoke(this, directory);
        }
    }

    /// <summary>
    /// **立刻重扫**全部来源根：装入新增的模块包并刷新诊断（改过代码的包需重启应用才生效）。
    /// </summary>
    [RelayCommand]
    private void Rescan()
    {
        var result = _command.RescanModuleSources();

        Refresh();
        SetStatus(
            result.Succeeded,
            result.Succeeded
                ? result.Notices.Count > 0 ? result.Notices[0] : "重扫完成"
                : $"重扫失败：{string.Join("；", result.Failures)}");
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
        var selectedModuleId = SelectedCatalogEntry?.ModuleId;

        var catalog = _query.GetModuleCatalog()
            .Select(entry => new ModuleCatalogRowViewModel(
                entry.ModuleId,
                entry.DisplayName,
                entry.Description,
                entry.Capabilities,
                entry.SourceDirectory,
                entry.NeedsRestart))
            .ToList();

        _ = CollectionSync.Sync(Catalog, catalog);

        // 选中项：保持原选择；原先没有选择时不自动选中（避免"扫一眼列表就被选中"的错觉）
        if (selectedModuleId is { Length: > 0 })
        {
            SelectedCatalogEntry = Catalog.FirstOrDefault(c => string.Equals(c.ModuleId, selectedModuleId, StringComparison.Ordinal));
        }

        var selectedPlaceholderId = SelectedPlaceholder?.InstanceId;

        var placeholders = _query.GetInstances()
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

        RefreshSources();
    }

    /// <summary>刷新来源根诊断（每根的路径 · 包数 · 失败数与逐包结果）。</summary>
    public void RefreshSources()
    {
        var sources = _query.GetModuleSources().Select(source => new ModuleSourceRowViewModel(source)).ToList();

        if (CollectionSync.Sync(Sources, sources) && SelectedSource is null)
        {
            SelectedSource = Sources.FirstOrDefault()?.Directory;
        }
    }

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

    private void SetStatus(bool ok, string message)
    {
        StatusIsOk = ok;
        StatusMessage = message;
    }
}
