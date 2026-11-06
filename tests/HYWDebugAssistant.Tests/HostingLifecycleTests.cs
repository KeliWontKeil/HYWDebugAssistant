using HYWDebugAssistant.Hosting.Diagnostics;
using HYWDebugAssistant.Hosting.Installation;
using HYWDebugAssistant.Hosting.Instances;
using HYWDebugAssistant.Hosting.Lifecycle;
using HYWDebugAssistant.Hosting.Packages;
using HYWDebugAssistant.Hosting.Registry;
using HYWDebugAssistant.Sdk;
using HYWDebugAssistant.Sdk.Context;
using HYWDebugAssistant.Sdk.Lifecycle;
using HYWDebugAssistant.Sdk.Ports;
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

        // 界面是模块自带的视图：框架只知道"有没有视图工厂"，不持有任何界面结构。
        instance.ViewType.ShouldNotBeNull("文本发送器自带 XAML 视图");
        instance.ActivitySource.ShouldNotBeNull();
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
        sink.Issues[0].Issue.Message.ShouldNotBeNullOrWhiteSpace();
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
        sink.Issues[0].Issue.Message.ShouldNotBeNullOrWhiteSpace();
    }

    // ------------------------------------------------------------ 安装与校验

    [Fact]
    public void 安装器_重复安装同一包应返回已载入而不是错误()
    {
        var sink = new CollectingIssueSink();
        var registry = new ModuleRegistry();
        var installer = new ModuleInstaller(registry, sink);

        var first = installer.Install(TextSenderPackage);
        first.Outcome.ShouldBe(ModuleInstallOutcome.Installed);
        first.Definition!.Id.ToString().ShouldBe("hyw/text-sender");
        registry.Definitions.Count.ShouldBe(1);

        var second = installer.Install(TextSenderPackage);
        second.Outcome.ShouldBe(ModuleInstallOutcome.AlreadyLoaded);
        second.Definition!.Id.ToString().ShouldBe("hyw/text-sender");
        sink.Issues.ShouldBeEmpty("同一包重复安装是正常状态，不是 Error");
    }

    [Fact]
    public void 安装器_缺少依赖应拒绝安装并报Issue()
    {
        using var temp = new TempPackage(TextSenderPackage, dependsOn: "hyw/not-installed");
        var sink = new CollectingIssueSink();
        var installer = new ModuleInstaller(new ModuleRegistry(), sink);

        installer.TryInstall(temp.Directory, out _).ShouldBeFalse();

        sink.Issues.Count.ShouldBe(1);
        sink.Issues[0].Issue.Message.ShouldNotBeNullOrWhiteSpace();
    }

    // ------------------------------------------------------------ 测试辅助

    private static ModuleDefinition RealDefinition()
    {
        var result = ModulePackageLoader.Load(TextSenderPackage);
        result.Succeeded.ShouldBeTrue(result.FailureReason);
        return ModuleDefinition.FromPackage(result.Package!);
    }

    private static ModuleDefinition TestDefinition(string name, Type entryType) =>
        new(ModuleId.Create("test", name),
            name, string.Empty, [], ModuleCapability.None, [], true,
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
    [ModuleDefinition("test", "tracing")]
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

    /// <summary>OnRegistered 抛异常的测试模块。</summary>
    [ModuleDefinition("test", "throwing")]
    public sealed class ThrowingModule : IModule
    {
        public void OnRegistered(IModuleContext context) =>
            throw new InvalidOperationException("契约：故意在 OnRegistered 抛出");
    }

    /// <summary>临时包：复制真实包输出并篡改清单字段，用于验证拒绝路径。</summary>
    private sealed class TempPackage : IDisposable
    {
        public TempPackage(string sourceDirectory, string? dependsOn = null)
        {
            // **确定性临时目录**：跨进程复用同一个名字；启动时先尝试删掉上一次的副本
            // （上一进程的锁已释放），本进程内因 LoadFrom 仍锁定而不删 ⇒ 不会无限堆积。
            Directory = Path.Combine(Path.GetTempPath(), "hyw-test-package-lifecycle-text-sender");
            TryDeleteTempDirectory(Directory);
            _ = System.IO.Directory.CreateDirectory(Directory);

            // 真实包含子目录（如随包 `i18n/`）：临时副本必须同样带上，否则"换了目录就丢文本"。
            foreach (var file in System.IO.Directory.EnumerateFiles(sourceDirectory, "*", SearchOption.AllDirectories))
            {
                if (Path.GetDirectoryName(file) == sourceDirectory
                    && file.EndsWith(".json", StringComparison.OrdinalIgnoreCase))
                {
                    continue;   // 顶层 module.json 由下面的篡改版替代
                }

                var relative = Path.GetRelativePath(sourceDirectory, file);
                var target = Path.Combine(Directory, relative);
                _ = System.IO.Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                File.Copy(file, target, overwrite: true);
            }

            var depends = dependsOn is null ? "[]" : $"[\"{dependsOn}\"]";

            var manifest = $$"""
            {
              "id": "hyw/text-sender",
              "entryAssembly": "HYWDebugAssistant.Modules.TextSender.dll",
              "entryType": "HYWDebugAssistant.Modules.TextSender.TextSenderModule",
              "dependsOn": {{depends}}
            }
            """;

            File.WriteAllText(Path.Combine(Directory, "module.json"), manifest);
        }

        public string Directory { get; }

        /// <summary>删掉上一次运行留下的副本（失败 = 本进程仍锁着，直接复用/覆盖）。</summary>
        private static void TryDeleteTempDirectory(string directory)
        {
            try
            {
                if (System.IO.Directory.Exists(directory))
                {
                    System.IO.Directory.Delete(directory, recursive: true);
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // 上一个进程的残留删不掉就保留：下面的文件复制会覆盖它。
            }
        }

        public void Dispose()
        {
            // **不删临时目录**：`Assembly.LoadFrom` 会锁定程序集；递归删除会先删掉未锁的
            // `i18n/**` 再在 DLL 上失败 ⇒ 后续用例复用已加载程序集时"随包文本凭空消失"。
            // 目录在系统 temp 下，由操作系统回收。
        }
    }
}
