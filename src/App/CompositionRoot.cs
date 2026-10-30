using System.Collections.Concurrent;
using HYWDebugAssistant.Access;
using HYWDebugAssistant.Core.Api;
using HYWDebugAssistant.Core.Composition;
using HYWDebugAssistant.Core.Runtime;
using HYWDebugAssistant.Core.Services;
using HYWDebugAssistant.Core.Settings;
using HYWDebugAssistant.FrameworkUi.ViewModels;
using HYWDebugAssistant.Infrastructure.Storage;
using HYWDebugAssistant.Sdk.Access;
using HYWDebugAssistant.Sdk.Reporting;
using HYWDebugAssistant.Sdk.Resources;
using HYWDebugAssistant.Sdk.Text;
using Microsoft.Extensions.DependencyInjection;

namespace HYWDebugAssistant.App;

/// <summary>
/// **组合根**（AR-11 唯一白名单）：装配序 + 依赖注入 + 启动动作。
/// </summary>
/// <remarks>
/// <list type="bullet">
///   <item><b>装配链</b>：Application 初始化 → 创建 Core 构成（含 Registry）
///     → 载入应用级设置（**失败不阻断启动**）→ 扫描模块来源目录 → 创建外壳视图模型（只经 `Core.Api`）
///     → 显示外壳窗口；模块窗口由 `FrameworkUi` 侧创建（模块集控制器在 `Ui`）；</item>
///   <item><b>只做装配</b>：不放业务/机制逻辑（一切机制在 L4 内）；</item>
///   <item><b>依赖方向</b>：`FrameworkUi` → `Ui` → `Core.Api`；本类是唯一同时可见 L4 与 L5 的位置。</item>
/// </list>
/// </remarks>
internal static class CompositionRoot
{
    /// <summary>装配全部构成并返回外壳视图模型与服务提供者。</summary>
    /// <returns>外壳视图模型 + 服务提供者（随应用退出释放）。</returns>
    internal static (ShellViewModel Shell, ServiceProvider Provider) Build()
    {
        var services = new ServiceCollection();

        // ---- ① Core 构成（L2/L3/L4）----
        // **视图状态存取面**：唯一实例，三方共用 ——
        //   ① 模块（经 `IModuleContext.ViewState` 只读回读：当前选中页 / 输入框当前文本 / 显示区文本）
        //   ② UI（写：切页 / 文本变化 / 追加行；读：元素表重建时恢复未提交的输入）
        //   ③ L4（创建实例时按实例绑只读面；移除实例时清理）。
        //   它是**视图状态**（不进组合件、不持久化），故不经 L4 的串行变更入口（输入文本逐键变化）。
        _ = services.AddSingleton<IViewStateAccess>(static _ => new ViewStateStore());

        // 外部标准目录：**唯一装配点**（模块只声明标准标识；适配器与参数类型归框架侧）。
        _ = services.AddSingleton<IExternalAccessCatalog>(static _ => new BuiltInExternalAccessCatalog());

        _ = services.AddSingleton(static provider => new ModuleRuntime(
            issues: null,
            carrierOptions: null,
            viewState: provider.GetRequiredService<IViewStateAccess>(),
            catalog: provider.GetRequiredService<IExternalAccessCatalog>()));

        _ = services.AddSingleton(static provider => new SettingsService(
            provider.GetRequiredService<ModuleRuntime>(),
            StoragePaths.SettingsFile));

        _ = services.AddSingleton(static provider => new RuntimeServices(
            provider.GetRequiredService<ModuleRuntime>(),
            provider.GetRequiredService<SettingsService>()));

        _ = services.AddSingleton<IRuntimeQuery>(static provider => provider.GetRequiredService<RuntimeServices>());
        _ = services.AddSingleton<IRuntimeCommand>(static provider => provider.GetRequiredService<RuntimeServices>());

        // ---- ② 外壳视图模型（L5；内含 `Ui` 的模块窗口集控制器）----
        _ = services.AddSingleton(static provider => new ShellViewModel(
            provider.GetRequiredService<IRuntimeQuery>(),
            provider.GetRequiredService<IRuntimeCommand>(),
            provider.GetRequiredService<IViewStateAccess>()));

        var provider = services.BuildServiceProvider();

        // ---- ③ 启动动作（顺序即语义）----
        var runtime = provider.GetRequiredService<RuntimeServices>();

        // **根的唯一来源是 StoragePaths**：Sdk 与 Infrastructure 零依赖，所以由组合根把路径推给
        // `TextCatalog`（默认解析只服务开发/测试/预览，不参与生产）。设根必须在接线诊断之前/之前均可，
        // 但两者都必须在 `settings.Load()` 之前 —— 设置载入就会切 UI 语言、首次触发文本表解析。
        TextCatalog.UseRoot(StoragePaths.TextPacksRoot);

        // **文本表诊断必须先接线**：坏表 / 缺表目录的问题不能等到界面起来后才被看见。
        // 同类消息去重（同一次加载会重复触发相同原因）。
        var reportedTextTableIssues = new ConcurrentDictionary<string, bool>(StringComparer.Ordinal);
        TextCatalog.Diagnostics = message =>
        {
            if (!reportedTextTableIssues.TryAdd(message, true))
            {
                return;
            }

            runtime.Runtime.Issues.Report("resources:i18n", new IssueReport(
                IssueCategory.PresentationError,
                IssueSeverity.Warning,
                // i18n-exempt: 文本表自身诊断前缀——表不可用时不能再读表，否则递归
                "文本表加载问题：" + message));
        };

        // **图标表诊断走同一条通道**：`ResourceLayout.Diagnostics` 是资源层的共享告警
        // （`IconCatalog.Diagnostics` 只是它的别名）。同样按消息去重：一次坏表只报一次。
        var reportedIconTableIssues = new ConcurrentDictionary<string, bool>(StringComparer.Ordinal);
        ResourceLayout.Diagnostics = message =>
        {
            if (!reportedIconTableIssues.TryAdd(message, true))
            {
                return;
            }

            runtime.Runtime.Issues.Report("resources:icons", new IssueReport(
                IssueCategory.PresentationError,
                IssueSeverity.Warning,
                // i18n-exempt: 图标表自身诊断前缀——表不可用时不能再读表，否则递归
                "图标表加载问题：" + message));
        };

        var settings = provider.GetRequiredService<SettingsService>();
        _ = settings.Load();   // 损坏 → 默认值 + Issue，**不阻断启动**

        // 模块来源根：**内置包目录排首位**（不可移除、不出现在「来源目录」可编辑列表里），
        // 其后是用户配置的根列表。
        // 启动与「重新加载」**共用同一条解析**（`ResolveScanRoots`）：
        // 若把内置目录只拼进局部的 `roots` 扫描，而「重扫」只读设置里的列表（内置目录**刻意不在设置里**），
        // 则用户没配来源根时重扫的根列表为空 ⇒ 列表被清空、什么也没重扫。
        runtime.BuiltInModuleDirectory = StoragePaths.ModulesRoot;
        runtime.ExternalModuleDirectory = StoragePaths.ExternalModulesRoot;

        var roots = runtime.ResolveScanRoots();
        var installed = runtime.ScanModuleSources(roots);
        var failed = runtime.GetModuleSources().Sum(s => s.FailedPackages);

        // 工作区快照：模块包先扫描（定义齐全），再恢复现场；不存在/损坏都不阻断启动。
        _ = runtime.LoadWorkspaceIfAny();

        runtime.Runtime.Issues.Report("app:startup", new IssueReport(
            IssueCategory.CompositionError,
            failed > 0 ? IssueSeverity.Warning : IssueSeverity.Info,
            AppText.Get(
                "app.startup.moduleRootsScanned",
                roots.Count,
                installed,
                failed,
                string.Join("；", roots))));

        return (provider.GetRequiredService<ShellViewModel>(), provider);
    }
}
