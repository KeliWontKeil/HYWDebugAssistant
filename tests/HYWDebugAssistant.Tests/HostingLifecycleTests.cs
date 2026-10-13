using HYWDebugAssistant.Hosting.Context;
using HYWDebugAssistant.Hosting.Diagnostics;
using HYWDebugAssistant.Hosting.Installation;
using HYWDebugAssistant.Hosting.Instances;
using HYWDebugAssistant.Hosting.Lifecycle;
using HYWDebugAssistant.Hosting.Packages;
using HYWDebugAssistant.Hosting.Registry;
using HYWDebugAssistant.Sdk;
using HYWDebugAssistant.Sdk.Context;
using HYWDebugAssistant.Sdk.Contracts;
using HYWDebugAssistant.Sdk.Lifecycle;
using HYWDebugAssistant.Sdk.Ports;
using HYWDebugAssistant.Sdk.Reporting;
using Shouldly;
using Xunit;

namespace HYWDebugAssistant.Tests;

/// <summary>
/// T3（M1-4）：实例化与成员实例表、生命周期状态机与钩子边界、模块安装校验。
/// </summary>
public class HostingLifecycleTests
{
    private static readonly string RepoRoot = FindRepoRoot();
    private static readonly string Configuration =
        AppContext.BaseDirectory.Contains($"{Path.DirectorySeparatorChar}Release{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase)
            ? "Release"
            : "Debug";

    /// <summary>业务模块 文本发送器 的构建输出目录（= 它的模块包目录；原参考桩模块已按裁定删除）。</summary>
    private static string TextSenderPackage => Path.Combine(
        RepoRoot, "src", "Modules", "Business", "TextSender", "bin", Configuration, "net10.0");

    // ------------------------------------------------------------ 实例化与成员实例表

    [Fact]
    public void 工厂_应从真实包创建实例并完成成员实例化()
    {
        var sink = new CollectingIssueSink();
        var instance = ModuleInstanceFactory.Create("gen-1", RealDefinition(), sink);

        sink.Issues.ShouldBeEmpty();
        instance.State.ShouldBe(ModuleInstanceState.Registered);
        instance.InstanceId.ShouldBe("gen-1");
        instance.Ports.Count.ShouldBe(1);
        instance.Ports[0].Name.ShouldBe("out");
        instance.Ports[0].Direction.ShouldBe(PortDirection.Send);
        instance.View.ShouldNotBeNull();
        instance.View!.Views.Count.ShouldBe(1, "整份声明只渲染第 0 个视图");
        instance.View!.Views[0].Root.Children.Count.ShouldBe(
            4,
            "文本发送器声明 4 个顶层元素（内容说明 / 分页输入区 / 消息那一行 / 一行动作 = 发送+连续发送+间隔+已发送）");
        instance.ActivitySource.ShouldNotBeNull();
    }

    [Fact]
    public void 工厂_口名重复应报Issue且仅保留首个声明()
    {
        var sink = new CollectingIssueSink();
        var instance = ModuleInstanceFactory.Create("dup-1", TestDefinition("dup-port", typeof(DuplicatePortModule)), sink);

        instance.Ports.Count.ShouldBe(1);
        sink.Issues.Count.ShouldBe(1);
        sink.Issues[0].Issue.Category.ShouldBe(IssueCategory.InterfaceError);
        sink.Issues[0].Issue.Message.ShouldContain("口名重复");
    }

    [Fact]
    public void 工厂_OnRegistered抛异常应转Issue且框架流程不中断()
    {
        var sink = new CollectingIssueSink();
        var instance = ModuleInstanceFactory.Create("thr-1", TestDefinition("throwing", typeof(ThrowingModule)), sink);

        instance.State.ShouldBe(ModuleInstanceState.Registered);
        instance.Ports.ShouldBeEmpty();
        sink.Issues.Count.ShouldBe(1);
        sink.Issues[0].Issue.Message.ShouldContain("OnRegistered");
    }

    // ------------------------------------------------------------ 生命周期状态机

    [Fact]
    public void 生命周期_应按状态机流转并依次触发六个钩子()
    {
        TracingModule.Trace = [];
        var sink = new CollectingIssueSink();
        var instance = ModuleInstanceFactory.Create("life-1", TestDefinition("tracing", typeof(TracingModule)), sink);
        var controller = new ModuleLifecycleController(sink);

        controller.Activate(instance).ShouldBeTrue();
        instance.State.ShouldBe(ModuleInstanceState.Active);

        controller.Disable(instance).ShouldBeTrue();
        instance.State.ShouldBe(ModuleInstanceState.Disabled);

        controller.Enable(instance).ShouldBeTrue();
        instance.State.ShouldBe(ModuleInstanceState.Active);

        controller.NotifyConfigurationChanged(instance);

        controller.Remove(instance).ShouldBeTrue();
        instance.State.ShouldBe(ModuleInstanceState.Removed);

        sink.Issues.ShouldBeEmpty();
        TracingModule.Trace.ShouldBe(
            ["OnRegistered", "OnActivated", "OnDisabled", "OnEnabled", "OnConfigurationChanged", "OnRemoved"]);
    }

    [Fact]
    public void 生命周期_非法转换应被拒绝并报Issue()
    {
        var sink = new CollectingIssueSink();
        var instance = ModuleInstanceFactory.Create("illegal-1", TestDefinition("tracing", typeof(TracingModule)), sink);
        var controller = new ModuleLifecycleController(sink);

        // Registered → Disabled 非法（必须先 Active）
        controller.Disable(instance).ShouldBeFalse();
        instance.State.ShouldBe(ModuleInstanceState.Registered);
        sink.Issues.Count.ShouldBe(1);
        sink.Issues[0].Issue.Message.ShouldContain("非法状态转换");
    }

    [Fact]
    public void 生命周期_钩子抛异常应转Issue且状态仍推进()
    {
        var sink = new CollectingIssueSink();
        var instance = ModuleInstanceFactory.Create(
            "hook-throw-1", TestDefinition("throwing-activate", typeof(ThrowingOnActivateModule)), sink);
        var controller = new ModuleLifecycleController(sink);

        controller.Activate(instance).ShouldBeTrue();

        instance.State.ShouldBe(ModuleInstanceState.Active);   // 钩子失败不阻断状态推进
        sink.Issues.Count.ShouldBe(1);
        sink.Issues[0].Issue.Message.ShouldContain("OnActivated");
    }

    [Fact]
    public void 生命周期_重复移除应被忽略并报Issue()
    {
        var sink = new CollectingIssueSink();
        var instance = ModuleInstanceFactory.Create("rm-1", TestDefinition("tracing", typeof(TracingModule)), sink);
        var controller = new ModuleLifecycleController(sink);

        controller.Remove(instance).ShouldBeTrue();
        controller.Remove(instance).ShouldBeFalse();

        sink.Issues.Count.ShouldBe(1);
        sink.Issues[0].Issue.Message.ShouldContain("已移除");
    }

    // ------------------------------------------------------------ 安装与校验

    [Fact]
    public void 安装器_应成功安装真实包且重复安装被拒绝()
    {
        var sink = new CollectingIssueSink();
        var registry = new ModuleRegistry();
        var installer = new ModuleInstaller(registry, sink);

        installer.TryInstall(TextSenderPackage, out var definition).ShouldBeTrue();
        definition!.Id.ToString().ShouldBe("hyw/text-sender/1.0.0");
        registry.Definitions.Count.ShouldBe(1);

        installer.TryInstall(TextSenderPackage, out _).ShouldBeFalse();
        sink.Issues.Count.ShouldBe(1);
        sink.Issues[0].Issue.Message.ShouldContain("已注册");
    }

    [Fact]
    public void 安装器_缺少依赖应拒绝安装并报Issue()
    {
        using var temp = new TempPackage(TextSenderPackage, dependsOn: "hyw/not-installed/1.0.0");
        var sink = new CollectingIssueSink();
        var installer = new ModuleInstaller(new ModuleRegistry(), sink);

        installer.TryInstall(temp.Directory, out _).ShouldBeFalse();

        sink.Issues.Count.ShouldBe(1);
        sink.Issues[0].Issue.Message.ShouldContain("缺少依赖模块");
    }

    [Fact]
    public void 安装器_框架兼容范围不满足应拒绝安装()
    {
        using var temp = new TempPackage(TextSenderPackage, frameworkCompatibility: ">=9.0.0");
        var sink = new CollectingIssueSink();
        var installer = new ModuleInstaller(new ModuleRegistry(), sink);

        installer.TryInstall(temp.Directory, out _).ShouldBeFalse();

        sink.Issues.Count.ShouldBe(1);
        sink.Issues[0].Issue.Message.ShouldContain("不满足兼容范围");
    }

    [Theory]
    [InlineData(null, true)]
    [InlineData("", true)]
    [InlineData(">=1.0.0 <2.0.0", true)]
    [InlineData(">=1.0.0", true)]
    [InlineData("=1.0.0", true)]
    [InlineData(">=2.0.0", false)]
    [InlineData("<1.0.0", false)]
    [InlineData(">1.0.0", false)]
    public void 框架兼容范围_判定应正确(string? range, bool expected)
    {
        FrameworkCompatibility
            .IsSatisfied(range, FrameworkCompatibility.CurrentFrameworkVersion, out _)
            .ShouldBe(expected);
    }

    [Fact]
    public void 模块上下文_应收集声明并把问题归属到实例与口()
    {
        var sink = new CollectingIssueSink();
        var context = new ModuleContext("ctx-1", ModuleId.Create("hyw", "ctx", "1.0.0"), sink);

        context.Declaration.DeclarePort(new PortDeclaration(
            "p", PortDirection.Receive, ContractShape.OfPrimitive(ContractPrimitive.U8)));
        context.Issues.Report(new IssueReport(IssueCategory.ModuleError, IssueSeverity.Warning, "样例问题", "p"));

        context.DeclaredPorts.Count.ShouldBe(1);
        context.ReceiveHandlers.ShouldBeEmpty();
        sink.Issues.Count.ShouldBe(1);
        sink.Issues[0].ObjectPath.ShouldBe("module:ctx-1/p");
        sink.Issues[0].Issue.Message.ShouldBe("样例问题");
    }

    // ------------------------------------------------------------ 测试辅助

    private static ModuleDefinition RealDefinition()
    {
        var result = ModulePackageLoader.Load(TextSenderPackage);
        result.Succeeded.ShouldBeTrue(result.FailureReason);
        return ModuleDefinition.FromPackage(result.Package!);
    }

    private static ModuleDefinition TestDefinition(string name, Type entryType) =>
        new(ModuleId.Create("test", name, "1.0.0"),
            name, string.Empty, [], ModuleCapability.None, [], null, true,
            entryType, string.Empty, string.Empty);

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

    /// <summary>
    /// 记录钩子调用顺序的测试模块。
    /// 注：工厂用 <c>Activator</c> 创建实例，故追踪用**静态**集合（xunit 同类内测试串行，安全）。
    /// </summary>
    [ModuleDefinition("test", "tracing", "1.0.0")]
    public sealed class TracingModule : IModule
    {
        public static List<string> Trace { get; set; } = [];

        public void OnRegistered(IModuleContext context) => Trace.Add(nameof(OnRegistered));

        public void OnActivated() => Trace.Add(nameof(OnActivated));

        public void OnDisabled() => Trace.Add(nameof(OnDisabled));

        public void OnEnabled() => Trace.Add(nameof(OnEnabled));

        public void OnConfigurationChanged() => Trace.Add(nameof(OnConfigurationChanged));

        public void OnRemoved() => Trace.Add(nameof(OnRemoved));
    }

    /// <summary>声明两个同名口的测试模块（验证口名唯一性校验）。</summary>
    [ModuleDefinition("test", "dup-port", "1.0.0")]
    public sealed class DuplicatePortModule : IModule
    {
        public void OnRegistered(IModuleContext context)
        {
            var shape = ContractShape.OfPrimitive(ContractPrimitive.U8);
            context.Declaration.DeclarePort(new PortDeclaration("p", PortDirection.Send, shape));
            context.Declaration.DeclarePort(new PortDeclaration("p", PortDirection.Receive, shape));
        }
    }

    /// <summary>OnRegistered 抛异常的测试模块。</summary>
    [ModuleDefinition("test", "throwing", "1.0.0")]
    public sealed class ThrowingModule : IModule
    {
        public void OnRegistered(IModuleContext context) =>
            throw new InvalidOperationException("契约：故意在 OnRegistered 抛出");
    }

    /// <summary>OnActivated 抛异常的测试模块。</summary>
    [ModuleDefinition("test", "throwing-activate", "1.0.0")]
    public sealed class ThrowingOnActivateModule : IModule
    {
        public void OnActivated() => throw new InvalidOperationException("契约：故意在 OnActivated 抛出");
    }

    /// <summary>临时包：复制真实包输出并篡改清单字段，用于验证拒绝路径。</summary>
    private sealed class TempPackage : IDisposable
    {
        public TempPackage(string sourceDirectory, string? dependsOn = null, string? frameworkCompatibility = null)
        {
            Directory = Path.Combine(Path.GetTempPath(), "hyw-lifecycle-" + Guid.NewGuid().ToString("N"));
            System.IO.Directory.CreateDirectory(Directory);

            foreach (var file in System.IO.Directory.EnumerateFiles(sourceDirectory)
                         .Where(f => !f.EndsWith(".json", StringComparison.OrdinalIgnoreCase)))
            {
                File.Copy(file, Path.Combine(Directory, Path.GetFileName(file)), overwrite: true);
            }

            var depends = dependsOn is null ? "[]" : $"[\"{dependsOn}\"]";
            var compatibility = frameworkCompatibility is null
                ? string.Empty
                : $",{Environment.NewLine}  \"frameworkCompatibility\": \"{frameworkCompatibility}\"";
            var manifest = $$"""
            {
              "id": "hyw/text-sender/1.0.0",
              "entryAssembly": "HYWDebugAssistant.Modules.TextSender.dll",
              "entryType": "HYWDebugAssistant.Modules.TextSender.TextSenderModule",
              "dependsOn": {{depends}}{{compatibility}}
            }
            """;

            File.WriteAllText(Path.Combine(Directory, "module.json"), manifest);
        }

        public string Directory { get; }

        public void Dispose()
        {
            try
            {
                System.IO.Directory.Delete(Directory, recursive: true);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // 已加载程序集锁定文件，清理失败不影响测试结论
            }
        }
    }
}
