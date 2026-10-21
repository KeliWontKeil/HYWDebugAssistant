using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using HYWDebugAssistant.Core.Changes;
using HYWDebugAssistant.Core.Issues;
using HYWDebugAssistant.Core.Runtime;
using HYWDebugAssistant.Core.Services;
using HYWDebugAssistant.DataPlane.Carriers;
using HYWDebugAssistant.Hosting.Instances;
using HYWDebugAssistant.Hosting.Packages;
using HYWDebugAssistant.Hosting.Registry;
using HYWDebugAssistant.Sdk;
using HYWDebugAssistant.Sdk.Access;
using Shouldly;
using Xunit;

namespace HYWDebugAssistant.Tests;

/// <summary>
/// T3 的一部分（M1-4）：模块包加载（显式加载 + 校验）与模块注册表（多版本共存）。
/// </summary>
public class HostingPackageTests
{
    private static readonly string RepoRoot = FindRepoRoot();
    private static readonly string Configuration =
        AppContext.BaseDirectory.Contains($"{Path.DirectorySeparatorChar}Release{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase)
            ? "Release"
            : "Debug";

    /// <summary>业务模块 文本发送器 的构建输出目录（= 它的模块包目录）。</summary>
    /// <remarks>载体说明：用本项目的正式业务模块；参考桩模块 `data-generator` 已不再构建。</remarks>
    private static string TextSenderPackage => Path.Combine(
        RepoRoot, "src", "Modules", "Business", "TextSender", "bin", Configuration, "net10.0");

    private static int ParsePort(string detail)
    {
        // UDP 的 detail 可能是 "地址:本地端口 → 目标:目标端口"：只取第一个地址段的端口。
        var local = detail.Split(' ', StringSplitOptions.RemoveEmptyEntries)[0];
        var index = local.LastIndexOf(':');
        index.ShouldBeGreaterThanOrEqualTo(0, $"状态说明里没有端口：{detail}");
        return int.Parse(local[(index + 1)..], System.Globalization.CultureInfo.InvariantCulture);
    }

    private static void WaitFor(Func<bool> condition, string message, int timeoutMs = 3000)
    {
        var stopwatch = Stopwatch.StartNew();

        while (!condition())
        {
            if (stopwatch.ElapsedMilliseconds > timeoutMs)
            {
                throw new TimeoutException(message);
            }

            Thread.Sleep(10);
        }
    }

    [Fact]
    public void 加载业务模块包_应成功并解析出标识与入口类型()
    {
        var result = ModulePackageLoader.Load(TextSenderPackage);

        result.Succeeded.ShouldBeTrue(result.FailureReason);
        var package = result.Package!;
        package.Id.ToString().ShouldBe("hyw/text-sender/1.0.0");
        package.EntryType.Name.ShouldBe("TextSenderModule");
        package.DefinitionAttribute.DisplayName.ShouldBe("文本发送器");
        File.Exists(package.AssemblyPath).ShouldBeTrue();
    }

    [Fact]
    public void 加载外部接入模块包_应能解析包内Access并实例化()
    {
        // 串口包依赖 Access.dll（随包分发）；这个用例走真实包 + 默认加载上下文，
        // 证明"模块包自带依赖"在外部接入上真的成立。
        var packageDirectory = Path.Combine(
            RepoRoot, "src", "Modules", "Business", "Serial", "bin", Configuration, "net10.0");

        var result = ModulePackageLoader.Load(packageDirectory);

        result.Succeeded.ShouldBeTrue(result.FailureReason);
        var definition = ModuleDefinition.FromPackage(result.Package!);
        var instance = ModuleInstanceFactory.Create("serial-package-1", definition, new IssueCenter());

        instance.ExternalAccesses.ShouldHaveSingleItem().Name.ShouldBe("serial");
        instance.Context.ConfigurationSnapshot.ContainsKey(
            "HYWDebugAssistant.Access.Parameters.SerialConfig").ShouldBeTrue();
    }

    [Fact]
    public void 外部接入模块_经真实包与运行时_配置写回与打开失败应走到会话和Issue()
    {
        var packageDirectory = Path.Combine(
            RepoRoot, "src", "Modules", "Business", "Serial", "bin", Configuration, "net10.0");

        var load = ModulePackageLoader.Load(packageDirectory);
        load.Succeeded.ShouldBeTrue(load.FailureReason);

        using var runtime = new ModuleRuntime(carrierOptions: new CarrierOptions
        {
            QueueCapacity = 16,
            StopTimeout = TimeSpan.FromSeconds(2),
            IdleWait = TimeSpan.FromMilliseconds(1),
        });

        var added = runtime.Apply(ChangeSet.OfOne(new AddModuleOperation(
            "serial-integration",
            ModuleDefinition.FromPackage(load.Package!))));
        added.Applied.ShouldBeTrue(string.Join("；", added.Failures));

        runtime.Content.TryGetInstance("serial-integration", out var instance).ShouldBeTrue();
        instance!.ExternalAccesses.ShouldHaveSingleItem().Name.ShouldBe("serial");

        var services = new RuntimeServices(runtime);
        var updated = services.UpdateConfigurationField("serial-integration", "PortName", "COM_DOES_NOT_EXIST");
        updated.Succeeded.ShouldBeTrue(string.Join("；", updated.Failures));

        services.TryGetConfigurationSchema("serial-integration", out var schema).ShouldBeTrue();
        schema!.Fields.Single(field => field.Name == "PortName").Value.ShouldBe("COM_DOES_NOT_EXIST");

        var opened = runtime.Apply(ChangeSet.OfOne(new SendCommandOperation("serial-integration", "open")));
        opened.Applied.ShouldBeTrue(string.Join("；", opened.Failures));

        WaitFor(
            () => instance.Context.Access.GetSnapshot("serial").State == ExternalAccessState.Faulted,
            "真实串口打开失败应进入 Faulted");

        runtime.Issues.Count.ShouldBeGreaterThan(0);
        runtime.Issues.Snapshot().Any(entry =>
                entry.Issue.Message.Contains("串口", StringComparison.Ordinal))
            .ShouldBeTrue("打开失败应经外部接入转成 Issue");
    }

    [Fact]
    public async Task 外部接入TCP服务端模块_经真实包_应接收外部数据并广播写()
    {
        var load = ModulePackageLoader.Load(Path.Combine(
            RepoRoot, "src", "Modules", "Business", "TcpServer", "bin", Configuration, "net10.0"));
        load.Succeeded.ShouldBeTrue(load.FailureReason);

        using var runtime = new ModuleRuntime(carrierOptions: new CarrierOptions
        {
            QueueCapacity = 16,
            StopTimeout = TimeSpan.FromSeconds(2),
            IdleWait = TimeSpan.FromMilliseconds(1),
        });

        var added = runtime.Apply(ChangeSet.OfOne(new AddModuleOperation(
            "tcp-server-integration",
            ModuleDefinition.FromPackage(load.Package!))));
        added.Applied.ShouldBeTrue(string.Join("；", added.Failures));

        runtime.Content.TryGetInstance("tcp-server-integration", out var instance).ShouldBeTrue();
        var services = new RuntimeServices(runtime);
        services.UpdateConfigurationField("tcp-server-integration", "LocalAddress", "127.0.0.1")
            .Succeeded.ShouldBeTrue();
        services.UpdateConfigurationField("tcp-server-integration", "LocalPort", "0")
            .Succeeded.ShouldBeTrue();

        var opened = runtime.Apply(ChangeSet.OfOne(new SendCommandOperation("tcp-server-integration", "open")));
        opened.Applied.ShouldBeTrue(string.Join("；", opened.Failures));

        WaitFor(
            () => instance!.Context.Access.GetSnapshot("tcp-server").State == ExternalAccessState.Open,
            "服务端应进入 Open");

        var port = ParsePort(instance!.Context.Access.GetSnapshot("tcp-server").Detail);

        using var client = new System.Net.Sockets.TcpClient();
        await client.ConnectAsync(IPAddress.Loopback, port).WaitAsync(TimeSpan.FromSeconds(5));
        await client.GetStream().WriteAsync(new byte[] { 1, 2, 3 });

        WaitFor(
            () => instance.Context.Access.GetSnapshot("tcp-server").ReceivedBytes >= 3,
            "服务端模块应收到外部数据");

        instance.Context.Access.Write("tcp-server", [4, 5]).ShouldBeTrue();

        var received = new byte[2];
        await client.GetStream().ReadExactlyAsync(received).AsTask().WaitAsync(TimeSpan.FromSeconds(5));
        received.ShouldBe(new byte[] { 4, 5 });
    }

    [Fact]
    public async Task 外部接入UDP模块_经真实包_应接收并发送数据报()
    {
        using var peer = new UdpClient(new IPEndPoint(IPAddress.Loopback, 0));
        var peerPort = ((IPEndPoint)peer.Client.LocalEndPoint!).Port;

        var load = ModulePackageLoader.Load(Path.Combine(
            RepoRoot, "src", "Modules", "Business", "Udp", "bin", Configuration, "net10.0"));
        load.Succeeded.ShouldBeTrue(load.FailureReason);

        using var runtime = new ModuleRuntime(carrierOptions: new CarrierOptions
        {
            QueueCapacity = 16,
            StopTimeout = TimeSpan.FromSeconds(2),
            IdleWait = TimeSpan.FromMilliseconds(1),
        });

        var added = runtime.Apply(ChangeSet.OfOne(new AddModuleOperation(
            "udp-integration",
            ModuleDefinition.FromPackage(load.Package!))));
        added.Applied.ShouldBeTrue(string.Join("；", added.Failures));

        runtime.Content.TryGetInstance("udp-integration", out var instance).ShouldBeTrue();
        var services = new RuntimeServices(runtime);
        services.UpdateConfigurationField("udp-integration", "LocalAddress", "127.0.0.1").Succeeded.ShouldBeTrue();
        services.UpdateConfigurationField("udp-integration", "LocalPort", "0").Succeeded.ShouldBeTrue();
        services.UpdateConfigurationField("udp-integration", "RemoteAddress", "127.0.0.1").Succeeded.ShouldBeTrue();
        services.UpdateConfigurationField(
            "udp-integration",
            "RemotePort",
            peerPort.ToString(System.Globalization.CultureInfo.InvariantCulture)).Succeeded.ShouldBeTrue();

        var opened = runtime.Apply(ChangeSet.OfOne(new SendCommandOperation("udp-integration", "open")));
        opened.Applied.ShouldBeTrue(string.Join("；", opened.Failures));

        WaitFor(
            () => instance!.Context.Access.GetSnapshot("udp").State == ExternalAccessState.Open,
            "UDP 模块应进入 Open");

        var localPort = ParsePort(instance!.Context.Access.GetSnapshot("udp").Detail);
        await peer.SendAsync(new byte[] { 1, 2, 3 }, new IPEndPoint(IPAddress.Loopback, localPort));

        WaitFor(
            () => instance.Context.Access.GetSnapshot("udp").ReceivedBytes >= 3,
            "UDP 模块应收到数据报");

        instance.Context.Access.Write("udp", [4, 5]).ShouldBeTrue();

        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var received = await peer.ReceiveAsync(timeout.Token);
        received.Buffer.ShouldBe(new byte[] { 4, 5 });
    }

    [Fact]
    public void 加载不存在的目录_应失败并给出可读原因()
    {
        var result = ModulePackageLoader.Load(Path.Combine(Path.GetTempPath(), "hyw-nonexistent-" + Guid.NewGuid().ToString("N")));

        result.Succeeded.ShouldBeFalse();
        result.FailureReason.ShouldNotBeNullOrWhiteSpace();
        result.FailureReason!.ShouldContain("包目录不存在");
    }

    [Fact]
    public void 清单标识非法_应失败()
    {
        using var temp = new TempPackage(TextSenderPackage, manifestId: "NotAValidId");

        var result = ModulePackageLoader.Load(temp.Directory);

        result.Succeeded.ShouldBeFalse();
        result.FailureReason!.ShouldContain("模块标识不合法");
    }

    [Fact]
    public void 清单与代码声明不一致_应拒绝加载()
    {
        // 清单写 2.0.0，而代码声明是 1.0.0 → 拒绝（避免同名歧义）
        using var temp = new TempPackage(TextSenderPackage, manifestId: "hyw/text-sender/2.0.0");

        var result = ModulePackageLoader.Load(temp.Directory);

        result.Succeeded.ShouldBeFalse();
        result.FailureReason!.ShouldContain("不一致");
    }

    [Fact]
    public void 注册表_重复注册同版本应被拒绝()
    {
        var definition = DefinitionFromRealPackage();
        var registry = new ModuleRegistry();

        registry.TryRegister(definition, out var firstFailure).ShouldBeTrue(firstFailure);
        registry.TryRegister(definition, out var secondFailure).ShouldBeFalse();
        secondFailure!.ShouldContain("已注册");
    }

    [Fact]
    public void 注册表_多版本应可共存且可按名解析()
    {
        var registry = new ModuleRegistry();
        var v1 = DefinitionFromRealPackage();
        var v2 = new ModuleDefinition(
            ModuleId.Create("hyw", "text-sender", "2.0.0"),
            v1.DisplayName, v1.Description, v1.Tags, v1.Capabilities, v1.DependsOn,
            v1.FrameworkCompatibility, v1.ParticipatesInDataFlow, v1.EntryType, v1.PackageDirectory, v1.AssemblyPath);

        registry.TryRegister(v1, out _).ShouldBeTrue();
        registry.TryRegister(v2, out _).ShouldBeTrue();

        registry.Definitions.Count.ShouldBe(2);
        registry.ResolveVersions("hyw", "text-sender").Count.ShouldBe(2);

        registry.TryResolve(ModuleId.Create("hyw", "text-sender", "2.0.0"), out var resolved).ShouldBeTrue();
        resolved!.Id.Version.ShouldBe("2.0.0");
    }

    [Fact]
    public void 注册表_可注销()
    {
        var definition = DefinitionFromRealPackage();
        var registry = new ModuleRegistry();
        registry.TryRegister(definition, out _).ShouldBeTrue();

        registry.TryUnregister(definition.Id).ShouldBeTrue();
        registry.Definitions.ShouldBeEmpty();
        registry.TryResolve(definition.Id, out _).ShouldBeFalse();
    }

    private static ModuleDefinition DefinitionFromRealPackage()
    {
        var result = ModulePackageLoader.Load(TextSenderPackage);
        result.Succeeded.ShouldBeTrue(result.FailureReason);
        return ModuleDefinition.FromPackage(result.Package!);
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

    /// <summary>
    /// 临时包：复制真实包的输出文件到临时目录，并可按需篡改清单内容（用于验证拒绝路径）。
    /// </summary>
    private sealed class TempPackage : IDisposable
    {
        public TempPackage(string sourceDirectory, string manifestId)
        {
            Directory = Path.Combine(Path.GetTempPath(), "hyw-pkg-" + Guid.NewGuid().ToString("N"));
            System.IO.Directory.CreateDirectory(Directory);

            foreach (var file in System.IO.Directory.EnumerateFiles(sourceDirectory)
                         .Where(f => !f.EndsWith(".json", StringComparison.OrdinalIgnoreCase)))
            {
                File.Copy(file, Path.Combine(Directory, Path.GetFileName(file)), overwrite: true);
            }

            var manifest = $$"""
            {
              "id": "{{manifestId}}",
              "entryAssembly": "HYWDebugAssistant.Modules.TextSender.dll",
              "entryType": "HYWDebugAssistant.Modules.TextSender.TextSenderModule"
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
                // 经 LoadFrom 加载的程序集会锁定其文件（进程退出前无法删除）；
                // 清理失败不影响测试结论，临时目录由系统回收。
            }
        }
    }
}
