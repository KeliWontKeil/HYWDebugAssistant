using HYWDebugAssistant.Core.Api;
using HYWDebugAssistant.Core.Composition;
using HYWDebugAssistant.Core.Runtime;
using HYWDebugAssistant.Core.Services;
using HYWDebugAssistant.Core.Settings;
using HYWDebugAssistant.FrameworkUi.ViewModels;
using HYWDebugAssistant.Hosting.Registry;
using HYWDebugAssistant.Sdk;
using HYWDebugAssistant.Sdk.Context;
using HYWDebugAssistant.Sdk.Contracts;
using HYWDebugAssistant.Sdk.Lifecycle;
using HYWDebugAssistant.Sdk.Ports;
using HYWDebugAssistant.Sdk.Reporting;
using HYWDebugAssistant.Sdk.Views;
using HYWDebugAssistant.Ui.ViewModels;
using Shouldly;
using Xunit;

namespace HYWDebugAssistant.Tests;

/// <summary>
/// M1-15：**框架外壳与弹窗**（设置 / 模块清单 / Issue / 拓扑诊断）的端到端验证。
/// </summary>
/// <remarks>
/// 只断言视图模型行为（不创建窗口）。连接管理已被删除（ADR-0034 R-4）：
/// 建网唯一机制 = 模块窗口里口上的网络标签，故此处不再有"建立/断开网络"的界面测试。
/// </remarks>
public class FrameworkUiTests
{
    private static readonly string DataGeneratorPackage = Path.Combine(
        FindRepoRoot(), "src", "Modules", "Reference", "DataGenerator", "bin",
        BuildConfiguration(), "net10.0");

    // ------------------------------------------------------------ 设置弹窗

    [Fact]
    public void 设置面板_应用后应即时生效并可回读()
    {
        using var harness = new ShellHarness();
        var settings = harness.Shell.Settings;

        settings.StopTimeoutSeconds = 3;
        settings.UiRefreshRateHz = 30;
        settings.ApplyCommand.Execute(null);

        settings.StatusIsOk.ShouldBeTrue(settings.StatusMessage);

        var current = harness.Services.GetSettings();
        current.StopTimeoutSeconds.ShouldBe(3);
        current.UiRefreshRateHz.ShouldBe(30);
    }

    [Fact]
    public void 设置面板_非法值应被拒绝并显示原因且不改变生效值()
    {
        using var harness = new ShellHarness();
        var settings = harness.Shell.Settings;

        settings.StopTimeoutSeconds = 0;   // 非法（必须为正）
        settings.ApplyCommand.Execute(null);

        settings.StatusIsOk.ShouldBeFalse();
        settings.StatusMessage.ShouldNotBeNull().ShouldContain("未生效");
        harness.Services.GetSettings().StopTimeoutSeconds.ShouldBe(5);
    }

    [Fact]
    public void 设置面板_还原应回到当前生效值()
    {
        using var harness = new ShellHarness();
        var settings = harness.Shell.Settings;

        settings.UiRefreshRateHz = 42;
        settings.ReloadCommand.Execute(null);

        settings.UiRefreshRateHz.ShouldBe(60);
    }

    // ------------------------------------------------------------ 模块清单弹窗

    [Fact]
    public void 模块清单_应加载模块包并加入现场()
    {
        using var harness = new ShellHarness();
        var catalog = harness.Shell.Catalog;

        catalog.PackageDirectory = DataGeneratorPackage;
        catalog.InstallCommand.Execute(null);

        catalog.StatusIsOk.ShouldBeTrue(catalog.StatusMessage);
        catalog.Catalog.ShouldContain(c => c.ModuleId == "hyw/reference-data-generator/1.0.0");

        catalog.SelectedCatalogEntry = catalog.Catalog.Single(c => c.ModuleId == "hyw/reference-data-generator/1.0.0");
        catalog.AddCommand.Execute(null);

        catalog.StatusIsOk.ShouldBeTrue(catalog.StatusMessage);
        harness.Services.GetInstances().ShouldHaveSingleItem().DisplayName.ShouldBe("参考数据发生器");

        // 实例**不出现在清单里**：每个实例都有自己的窗口（含未声明视图的模块）——"无界面模块"概念已撤销
        catalog.Refresh();
        catalog.Placeholders.ShouldBeEmpty();
    }

    [Fact]
    public void 模块清单_未选择模块时加入现场应失败并提示()
    {
        using var harness = new ShellHarness();
        var catalog = harness.Shell.Catalog;

        catalog.Refresh();
        catalog.SelectedCatalogEntry = null;
        catalog.AddCommand.Execute(null);

        catalog.StatusIsOk.ShouldBeFalse();
        catalog.StatusMessage.ShouldNotBeNull().ShouldContain("可用模块");
    }

    [Fact]
    public void 未声明视图的模块_同样有窗口并给出配置表单()
    {
        using var harness = new ShellHarness();
        harness.Register("headless", typeof(HeadlessModule));

        harness.Services.AddModule("h1", "test/headless/1.0.0").Succeeded.ShouldBeTrue();
        harness.Shell.Windows.Refresh();

        // **每个模块实例都有窗口**（用户裁决 M1-15）：未声明视图者只显示模块名行 + 配置表单
        var module = harness.Shell.Windows.Modules.ShouldHaveSingleItem();
        module.InstanceId.ShouldBe("h1");
        module.Ports.ShouldNotBeEmpty("口仍在，可用于建网");

        var editor = module.Elements.OfType<ConfigurationEditorElementViewModel>().Single();
        editor.HasFields.ShouldBeFalse("该模块没有配置类型 → 表单为空并说明原因（不假装有字段）");
        editor.Notice.ShouldNotBeNull().ShouldContain("没有可编辑的配置项");

        // 运行/停用、移除都在它自己的窗口上（不需要任何"实例清单"）
        module.IsEnabled.ShouldBeTrue();
        harness.Shell.Windows.ToggleEnabled("h1");
        harness.Services.GetInstances().ShouldHaveSingleItem().State.ShouldBe("Disabled");

        harness.Shell.Windows.Refresh();
        harness.Shell.Windows.Modules.ShouldHaveSingleItem().IsEnabled.ShouldBeFalse();

        harness.Shell.Windows.CloseModule("h1");
        harness.Services.GetInstances().ShouldBeEmpty();
    }

    [Fact]
    public void 未声明配置编辑元素的模块_窗口内应自动补上配置表单()
    {
        using var harness = new ShellHarness();
        harness.Register("configurable", typeof(ConfigurableModule));

        harness.Services.AddModule("c1", "test/configurable/1.0.0").Succeeded.ShouldBeTrue();
        harness.Shell.Windows.Refresh();

        var editor = harness.Shell.Windows.Modules.ShouldHaveSingleItem()
            .Elements.OfType<ConfigurationEditorElementViewModel>().Single();

        editor.HasFields.ShouldBeTrue("模块没声明 B1 元素时由框架补一个（C5-4：配置编辑在模块视图内）");
        editor.Fields.Select(f => f.Name).ShouldBe(["PeriodMs", "Gain"], ignoreOrder: true);

        var period = editor.Fields.Single(f => f.Name == "PeriodMs");
        period.Kind.ShouldBe(ConfigurationFieldKind.WholeNumber);
        period.Value.ShouldBe("100");

        period.Value = "250";
        editor.ApplyCommand.Execute(null);

        editor.StatusIsOk.ShouldBeTrue(editor.StatusMessage);
        period.IsDirty.ShouldBeFalse();

        var instance = harness.Services.GetInstances().ShouldHaveSingleItem();
        harness.Services.TryGetConfigurationSchema(instance.InstanceId, out var schema).ShouldBeTrue();
        schema!.Fields.Single(f => f.Name == "PeriodMs").Value.ShouldBe("250");
    }

    // ------------------------------------------------------------ Issue 弹窗

    [Fact]
    public void Issue面板_应按归属_类别_来源_严重度过滤()
    {
        using var harness = new ShellHarness();
        var runtime = harness.Runtime;

        runtime.Issues.Report("module:a", new IssueReport(IssueCategory.CompositionError, IssueSeverity.Error, "框架判定的问题"));
        runtime.Issues.Report("module:b", new IssueReport(IssueCategory.InterfaceError, IssueSeverity.Warning, "接口问题"));
        runtime.Issues.Report("module:b/out", new IssueReport(IssueCategory.ModuleError, IssueSeverity.Error, "模块报告的问题", Source: IssueSource.Module));

        var issues = harness.Shell.Issues;
        issues.Refresh();

        issues.TotalCount.ShouldBe(3);
        issues.ErrorCount.ShouldBe(2);
        issues.Issues.Count.ShouldBe(3);
        issues.Summary.ShouldContain("3");

        issues.ObjectFilter = "module:b";
        issues.Refresh();
        issues.Issues.Count.ShouldBe(2);

        issues.ObjectFilter = string.Empty;
        issues.SourceFilter = nameof(IssueSource.Module);
        issues.Refresh();
        issues.Issues.ShouldHaveSingleItem().Message.ShouldBe("模块报告的问题");

        issues.SourceFilter = IssuesPanelViewModel.All;
        issues.CategoryFilter = nameof(IssueCategory.InterfaceError);
        issues.Refresh();
        issues.Issues.ShouldHaveSingleItem().ObjectPath.ShouldBe("module:b");

        issues.CategoryFilter = IssuesPanelViewModel.All;
        issues.SeverityFilter = nameof(IssueSeverity.Error);
        issues.Refresh();
        issues.Issues.Count.ShouldBe(2);

        issues.ClearFilterCommand.Execute(null);
        issues.Refresh();
        issues.Issues.Count.ShouldBe(3);
    }

    [Fact]
    public void Issue面板_模块经上下文上报应标记来源为模块()
    {
        using var harness = new ShellHarness();
        harness.Register("reporter", typeof(ReportingModule));
        harness.Services.AddModule("rp1", "test/reporter/1.0.0").Succeeded.ShouldBeTrue();

        harness.Shell.Issues.Refresh();

        var issue = harness.Shell.Issues.Issues.ShouldHaveSingleItem();
        issue.Source.ShouldBe(nameof(IssueSource.Module), "来源由框架在模块上报路径上补齐");
        issue.ObjectPath.ShouldBe("module:rp1/out", "归属对象 = 实例 + 口");
        issue.Category.ShouldBe(nameof(IssueCategory.InterfaceError));
    }

    // ------------------------------------------------------------ 拓扑诊断弹窗（只读）

    [Fact]
    public void 拓扑诊断_应列出网络_成员_方向与状态()
    {
        using var harness = new ShellHarness();
        harness.Register("connect-sender", typeof(ConnectSenderModule));
        harness.Register("connect-receiver", typeof(ConnectReceiverModule));

        harness.Services.AddModule("s1", "test/connect-sender/1.0.0").Succeeded.ShouldBeTrue();
        harness.Services.AddModule("r1", "test/connect-receiver/1.0.0").Succeeded.ShouldBeTrue();

        // 建网唯一机制：口上的网络标签同名即连（此处直接走冻结的命令面）
        harness.Services.UpdatePortNetwork("s1", "out", "bus-1").Succeeded.ShouldBeTrue();
        harness.Services.UpdatePortNetwork("r1", "in", "bus-1").Succeeded.ShouldBeTrue();

        var topology = harness.Shell.Topology;
        topology.Refresh();

        topology.Summary.ShouldContain("网络 1");
        topology.Rows.Count.ShouldBe(2);

        var source = topology.Rows.Single(r => string.Equals(r.Direction, "源", StringComparison.Ordinal));
        source.NetworkName.ShouldBe("bus-1");
        source.Kind.ShouldBe("命名");
        source.Member.ShouldBe("s1.out");
        source.State.ShouldBe("正常");

        var target = topology.Rows.Single(r => string.Equals(r.Direction, "宿", StringComparison.Ordinal));
        target.Member.ShouldBe("r1.in");
    }

    [Fact]
    public void 拓扑诊断_契约不兼容时成员不入网并在Issue中说明()
    {
        using var harness = new ShellHarness();
        harness.Register("connect-sender", typeof(ConnectSenderModule));
        harness.Register("wide-only", typeof(WideReceiverModule));

        harness.Services.AddModule("s1", "test/connect-sender/1.0.0").Succeeded.ShouldBeTrue();
        harness.Services.AddModule("w1", "test/wide-only/1.0.0").Succeeded.ShouldBeTrue();

        harness.Services.UpdatePortNetwork("s1", "out", "bus-2").Succeeded.ShouldBeTrue();
        harness.Services.UpdatePortNetwork("w1", "in", "bus-2").Succeeded.ShouldBeTrue();

        harness.Services.GetNetworks().ShouldBeEmpty("契约不兼容的成员被排除，且无兼容宿 → 不成网");

        harness.Shell.Issues.Refresh();
        harness.Shell.Issues.Issues.ShouldContain(i => i.Message.Contains("契约不兼容", StringComparison.Ordinal));
    }

    // ------------------------------------------------------------ 外壳（开关分组 + 状态栏）

    [Fact]
    public void 外壳_开关应按显示_交互_窗口三组工作()
    {
        using var harness = new ShellHarness();
        var shell = harness.Shell;

        // 显示组：**叠加信息层**默认开（端口标记本身常显，不再有开关 —— ADR-0039 D5）
        shell.ShowInfoOverlay.ShouldBeTrue();
        shell.ToggleMarkersCommand.Execute(null);
        shell.ShowInfoOverlay.ShouldBeFalse();
        shell.Windows.ShowInfoOverlay.ShouldBeFalse("开关的真实状态由模块窗口集持有");

        // 交互组：吸附默认开，可关
        shell.SnapEnabled.ShouldBeTrue();
        shell.ToggleSnapCommand.Execute(null);
        shell.SnapEnabled.ShouldBeFalse();

        // 窗口组：全部锁定 / 解锁
        harness.Register("connect-sender", typeof(ConnectSenderModule));
        harness.Services.AddModule("s1", "test/connect-sender/1.0.0").Succeeded.ShouldBeTrue();
        shell.Refresh();
        shell.LockAllCommand.Execute(null);
        shell.AllLocked.ShouldBeTrue();
        shell.Windows.Modules.ShouldAllBe(m => m.IsLocked);

        shell.UnlockAllCommand.Execute(null);
        shell.Windows.Modules.ShouldAllBe(m => !m.IsLocked);

        shell.Refresh();
        shell.StatusText.ShouldContain("模块 1");
        shell.StatusText.ShouldContain("刷新");
        shell.StatusText.ShouldContain("Issue");
    }

    // ------------------------------------------------------------ 组合根（M1-11）

    [Fact]
    public void 组合根_应默认装入应用目录下的内置模块包()
    {
        // 模拟发布形态：应用目录下 modules\（由 build.ps1 的归拢步骤产出）
        var staged = Path.Combine(FindRepoRoot(), "artifacts", "modules");
        Directory.Exists(staged).ShouldBeTrue($"模块包未归拢：{staged}（请先运行 ./build.ps1）");

        var builtIn = Path.Combine(AppContext.BaseDirectory, "modules");
        if (Directory.Exists(builtIn))
        {
            try
            {
                Directory.Delete(builtIn, recursive: true);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
            }
        }

        CopyDirectory(staged, builtIn);

        try
        {
            var (shell, provider) = HYWDebugAssistant.App.CompositionRoot.Build();

            try
            {
                shell.Catalog.Refresh();

                // **内置模块开箱可用**：启动即扫描内置包目录，用户无需去找路径
                shell.Catalog.Catalog.Count.ShouldBe(5);
                shell.Catalog.Catalog.Select(c => c.ModuleId).ShouldContain("hyw/reference-manual-sender/1.0.0");
                shell.Catalog.Catalog.Select(c => c.ModuleId).ShouldContain("hyw/reference-monitor/1.0.0");

                // 来源根诊断：内置根在首位且可见（"到底扫了哪里"必须可查）
                var source = shell.Catalog.Sources.First();
                source.Directory.ShouldBe(builtIn);
                source.Summary.ShouldContain("包 5 个");
            }
            finally
            {
                provider.Dispose();
            }
        }
        finally
        {
            // 已加载的模块程序集会锁住文件 → 清理失败可忽略（目录在测试输出下，不影响结论）
            try
            {
                Directory.Delete(builtIn, recursive: true);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
            }
        }
    }

    [Fact]
    public void 组合根_应装配完整构成并给出状态摘要()
    {
        var (shell, provider) = HYWDebugAssistant.App.CompositionRoot.Build();

        try
        {
            shell.Windows.ShouldNotBeNull("模块窗口集控制器由外壳持有（Ui 提供，FrameworkUi 调用）");
            shell.Catalog.ShouldNotBeNull();
            shell.Issues.ShouldNotBeNull();
            shell.Settings.ShouldNotBeNull();
            shell.Topology.ShouldNotBeNull();

            shell.Refresh();

            shell.StatusText.ShouldContain("模块");
            shell.StatusText.ShouldContain("刷新");
            shell.StatusText.ShouldContain("Issue");
        }
        finally
        {
            provider.Dispose();
        }
    }

    // ------------------------------------------------------------ 测试辅助

    private static string BuildConfiguration() =>
#if DEBUG
        "Debug";
#else
        "Release";
#endif

    /// <summary>递归复制目录（内置模块包目录的模拟发布用）。</summary>
    private static void CopyDirectory(string source, string destination)
    {
        _ = Directory.CreateDirectory(destination);

        foreach (var file in Directory.EnumerateFiles(source))
        {
            File.Copy(file, Path.Combine(destination, Path.GetFileName(file)), overwrite: true);
        }

        foreach (var directory in Directory.EnumerateDirectories(source))
        {
            CopyDirectory(directory, Path.Combine(destination, Path.GetFileName(directory)));
        }
    }

    private static string FindRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            if (dir.EnumerateFiles("*.slnx").Any() || dir.EnumerateFiles("*.sln").Any())
            {
                return dir.FullName;
            }

            dir = dir.Parent;
        }

        throw new InvalidOperationException("未能定位解决方案根目录（未找到 *.slnx/*.sln）");
    }

    /// <summary>外壳测试现场：真实运行时 + 真实服务面 + 外壳视图模型（含四个弹窗视图模型）。</summary>
    private sealed class ShellHarness : IDisposable
    {
        public ShellHarness()
        {
            var settingsDirectory = Path.Combine(Path.GetTempPath(), "hyw-fwui-" + Guid.NewGuid().ToString("N"));
            _ = Directory.CreateDirectory(settingsDirectory);
            _settingsPath = Path.Combine(settingsDirectory, "settings.json");

            Runtime = new ModuleRuntime();
            var settings = new SettingsService(Runtime, _settingsPath);
            _ = settings.Load();

            Services = new RuntimeServices(Runtime, settings);
            Shell = new ShellViewModel(Services, Services);
            Shell.LoadSettings();
        }

        private readonly string _settingsPath;

        public ModuleRuntime Runtime { get; }

        public RuntimeServices Services { get; }

        public ShellViewModel Shell { get; }

        public void Register(string name, Type entryType)
        {
            var definition = new ModuleDefinition(
                ModuleId.Create("test", name, "1.0.0"),
                entryType.Name,
                string.Empty,
                [],
                ModuleCapability.None,
                [],
                null,
                true,
                entryType,
                string.Empty,
                string.Empty);

            Services.Registry.TryRegister(definition, out var failure).ShouldBeTrue(failure);
        }

        public void Dispose()
        {
            Runtime.Dispose();

            try
            {
                Directory.Delete(Path.GetDirectoryName(_settingsPath)!, recursive: true);
            }
            catch (IOException)
            {
                // 清理失败不影响结论
            }
        }
    }

    // ------------------------------------------------------------ 测试模块

    /// <summary>模块配置（**模块自有类型**：持久化/表单都走反射式映射）。</summary>
    public sealed class ModuleConfig
    {
        public int PeriodMs { get; set; } = 100;

        public double Gain { get; set; } = 1.0;
    }

    /// <summary>无 UI 模块（不声明视图——合法，其配置经模块清单弹窗兜底）。</summary>
    [ModuleDefinition("test", "headless", "1.0.0")]
    public sealed class HeadlessModule : IModule
    {
        public void OnRegistered(IModuleContext context) =>
            context.Declaration.DeclarePort(new PortDeclaration(
                "in",
                PortDirection.Receive,
                ContractShape.OfPrimitive(ContractPrimitive.F32)));
    }

    /// <summary>拓扑诊断测试用的发送模块（f32 发送口）。</summary>
    [ModuleDefinition("test", "connect-sender", "1.0.0")]
    public sealed class ConnectSenderModule : IModule
    {
        public void OnRegistered(IModuleContext context) =>
            context.Declaration.DeclarePort(new PortDeclaration(
                "out",
                PortDirection.Send,
                ContractShape.OfPrimitive(ContractPrimitive.F32)));
    }

    /// <summary>拓扑诊断测试用的接收模块（f32 接收口）。</summary>
    [ModuleDefinition("test", "connect-receiver", "1.0.0")]
    public sealed class ConnectReceiverModule : IModule
    {
        public void OnRegistered(IModuleContext context) =>
            context.Declaration.DeclarePort(new PortDeclaration(
                "in",
                PortDirection.Receive,
                ContractShape.OfPrimitive(ContractPrimitive.F32)));
    }

    /// <summary>契约不兼容验证用（接收 f64，与 f32 发送口不匹配）。</summary>
    [ModuleDefinition("test", "wide-only", "1.0.0")]
    public sealed class WideReceiverModule : IModule
    {
        public void OnRegistered(IModuleContext context) =>
            context.Declaration.DeclarePort(new PortDeclaration(
                "in",
                PortDirection.Receive,
                ContractShape.OfPrimitive(ContractPrimitive.F64)));
    }

    /// <summary>带配置的无界面模块（配置兜底走 Schema 表单）。</summary>
    [ModuleDefinition("test", "configurable", "1.0.0")]
    public sealed class ConfigurableModule : IModule
    {
        public void OnRegistered(IModuleContext context)
        {
            context.Declaration.DeclarePort(new PortDeclaration(
                "out",
                PortDirection.Send,
                ContractShape.OfPrimitive(ContractPrimitive.F32)));

            context.Configuration.Write(new ModuleConfig());
        }
    }

    /// <summary>在注册期上报一个问题的模块（验证"来源 = 模块"与归属对象拼接）。</summary>
    [ModuleDefinition("test", "reporter", "1.0.0")]
    public sealed class ReportingModule : IModule
    {
        public void OnRegistered(IModuleContext context)
        {
            context.Declaration.DeclarePort(new PortDeclaration(
                "out",
                PortDirection.Send,
                ContractShape.OfPrimitive(ContractPrimitive.F32)));

            context.Issues.Report(new IssueReport(
                IssueCategory.InterfaceError,
                IssueSeverity.Warning,
                "模块自述：外部设备未应答",
                ObjectPath: "out"));
        }
    }
}
