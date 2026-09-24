using HYWDebugAssistant.FrameworkUi.ViewModels;
using HYWDebugAssistant.Ui.Design;

namespace HYWDebugAssistant.FrameworkUi.Design;

/// <summary>
/// **外壳与弹窗的设计时样例数据**（XAML 预览器用）——【用户第九轮反馈】：
/// 让 `MainWindow` / 四个弹窗 / 两个侧边页面在 VS 设计器里**有内容可看**，从而能直接调 UI。
/// </summary>
/// <remarks>
/// 用法：视图根上写 <c>d:DataContext="{x:Static design:ShellDesignData.Shell}"</c> 等
/// （`xmlns:design="using:HYWDebugAssistant.FrameworkUi.Design"` + `xmlns:d="…blend/2008"`）。
/// 样例数据来自 <see cref="EmptyRuntime"/>（返回样例行，不是空集合）。`d:` 只在设计器生效。
/// </remarks>
public static class ShellDesignData
{
    /// <summary>外壳（侧边栏）样例。</summary>
    public static ShellViewModel Shell { get; } = BuildShell();

    /// <summary>模块清单页样例。</summary>
    public static ModuleCatalogViewModel Catalog { get; } = BuildCatalog();

    /// <summary>设置弹窗样例。</summary>
    public static SettingsPanelViewModel Settings { get; } = BuildSettings();

    /// <summary>拓扑诊断弹窗样例。</summary>
    public static TopologyViewModel Topology { get; } = BuildTopology();

    /// <summary>Issue 弹窗样例。</summary>
    public static IssuesPanelViewModel Issues { get; } = BuildIssues();

    private static ShellViewModel BuildShell()
    {
        var shell = new ShellViewModel(EmptyRuntime.Instance, EmptyRuntime.Instance);
        shell.LoadSettings();
        shell.Refresh();
        return shell;
    }

    private static ModuleCatalogViewModel BuildCatalog()
    {
        var catalog = new ModuleCatalogViewModel(EmptyRuntime.Instance, EmptyRuntime.Instance);
        catalog.Refresh();
        catalog.RefreshSources();
        return catalog;
    }

    private static SettingsPanelViewModel BuildSettings()
    {
        var settings = new SettingsPanelViewModel(EmptyRuntime.Instance, EmptyRuntime.Instance);
        settings.Load();
        return settings;
    }

    private static TopologyViewModel BuildTopology()
    {
        var topology = new TopologyViewModel(EmptyRuntime.Instance);
        topology.Refresh();
        return topology;
    }

    private static IssuesPanelViewModel BuildIssues()
    {
        var issues = new IssuesPanelViewModel(EmptyRuntime.Instance);
        issues.Refresh();
        return issues;
    }
}
