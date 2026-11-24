using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using HYWDebugAssistant.Access;
using HYWDebugAssistant.Core.Changes;
using HYWDebugAssistant.Core.Runtime;
using HYWDebugAssistant.Core.Services;
using HYWDebugAssistant.DataPlane.Carriers;
using HYWDebugAssistant.Hosting.Packages;
using HYWDebugAssistant.Hosting.Registry;
using HYWDebugAssistant.Sdk;
using HYWDebugAssistant.Sdk.Access;
using Shouldly;
using Xunit;

namespace HYWDebugAssistant.Tests;

/// <summary>
/// T3 的一部分（M1-4）：模块包加载（显式加载 + 校验）与真实包上的外部接入接线。
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

    private static string CommandSenderPackage => Path.Combine(
        RepoRoot, "src", "Modules", "Business", "CommandSender", "bin", Configuration, "net10.0");

    private static string ImageFramePackage => Path.Combine(
        RepoRoot, "src", "Modules", "Business", "ImageFrame", "bin", Configuration, "net10.0");

    private static int EndpointPort(ExternalAccessSnapshot snapshot)
    {
        var endpoint = snapshot.Endpoint;
        endpoint.ShouldNotBeNull("打开后的快照必须带端点事实");
        return endpoint!.Value.Port!.Value;
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
    public void 加载指令发送器包_应成功并解析出目录名与入口类型()
    {
        var result = ModulePackageLoader.Load(CommandSenderPackage);

        result.Succeeded.ShouldBeTrue(result.FailureReason);
        var package = result.Package!;
        package.Id.ToString().ShouldBe("hyw/command-sender");
        package.EntryType.Name.ShouldBe("CommandSenderModule");
        package.DefinitionAttribute.DisplayNameKey.ShouldBe("module.title");
        ModuleDefinition.FromPackage(package).DisplayName.ShouldBe("指令发送器"); // i18n-test-data: 断言随包文本解析出的模块标题

    }

    [Fact]
    public void 加载图片框包_应成功并解析出目录名与入口类型()
    {
        var result = ModulePackageLoader.Load(ImageFramePackage);

        result.Succeeded.ShouldBeTrue(result.FailureReason);
        var package = result.Package!;
        package.Id.ToString().ShouldBe("hyw/image-frame");
        package.EntryType.Name.ShouldBe("ImageFrameModule");
        package.DefinitionAttribute.DisplayNameKey.ShouldBe("module.title");
        ModuleDefinition.FromPackage(package).DisplayName.ShouldBe("图片框"); // i18n-test-data: 断言随包文本解析出的模块标题
    }

    [Fact]
    public void 加载业务模块包_应成功并解析出标识与入口类型()
    {
        var result = ModulePackageLoader.Load(TextSenderPackage);

        result.Succeeded.ShouldBeTrue(result.FailureReason);
        var package = result.Package!;
        package.Id.ToString().ShouldBe("hyw/text-sender");
        package.EntryType.Name.ShouldBe("TextSenderModule");
        package.DefinitionAttribute.DisplayName.ShouldBeEmpty("字面量已进文本表（DisplayNameKey）");
        package.DefinitionAttribute.DisplayNameKey.ShouldBe("module.title");

        var tablePath = Path.Combine(
            Path.GetDirectoryName(package.EntryType.Assembly.Location)!,
            "resources",
            "i18n",
            package.EntryType.Assembly.GetName().Name!,
            "zh-Hans.json");
        File.Exists(tablePath).ShouldBeTrue($"随包文本应存在：{tablePath}");

        ModuleDefinition.FromPackage(package).DisplayName.ShouldNotBeNullOrWhiteSpace(
            $"随包文本应解析出模块标题（程序集：{package.EntryType.Assembly.Location}）");
        File.Exists(package.AssemblyPath).ShouldBeTrue();
    }

    [Fact]
    public void 值初值_真实包应在激活前播种()
    {
        var packageDirectory = Path.Combine(
            RepoRoot, "src", "Modules", "Business", "RawToWaveform", "bin", Configuration, "net10.0");

        var load = ModulePackageLoader.Load(packageDirectory);
        load.Succeeded.ShouldBeTrue(load.FailureReason);

        var viewState = new ViewStateStore();
        using var runtime = new ModuleRuntime(viewState: viewState);
        var added = runtime.Apply(ChangeSet.OfOne(new AddModuleOperation(
            "raw-xaml-defaults",
            ModuleDefinition.FromPackage(load.Package!))));
        added.Applied.ShouldBeTrue(string.Join("；", added.Failures));

        runtime.Content.TryGetInstance("raw-xaml-defaults", out var instance).ShouldBeTrue();
        instance!.Context.Values.ReadText("headerHex").ShouldBe("61 62");
        instance.Context.Values.ReadText("footerHex").ShouldBe("63 64");
        instance.Context.Values.ReadText("groupId").ShouldBe("1");
        instance.Context.Values.ReadText("rateHz").ShouldBe("1000");
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
            WatchdogInterval = TimeSpan.FromMilliseconds(20),
        }, catalog: new BuiltInExternalAccessCatalog());

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

        instance.Context.Access.GetSnapshot("serial").Failure!.Kind
            .ShouldBe(ExternalAccessFailureKind.OpenFailed);

        runtime.Issues.Count.ShouldBeGreaterThan(0);
        runtime.Issues.Snapshot().Any(entry =>
                !string.IsNullOrWhiteSpace(entry.Issue.Message))
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
            WatchdogInterval = TimeSpan.FromMilliseconds(20),
        }, catalog: new BuiltInExternalAccessCatalog());

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

        var port = EndpointPort(instance!.Context.Access.GetSnapshot("tcp-server"));

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
            WatchdogInterval = TimeSpan.FromMilliseconds(20),
        }, catalog: new BuiltInExternalAccessCatalog());

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

        var localPort = EndpointPort(instance!.Context.Access.GetSnapshot("udp"));
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
    public void 旧包引用了模块侧Access_应拒绝并提示重编()
    {
        var directory = Path.Combine(Path.GetTempPath(), "hyw-legacy-" + Guid.NewGuid().ToString("N"));
        _ = Directory.CreateDirectory(directory);

        try
        {
            var assemblyPath = typeof(HostingPackageTests).Assembly.Location;
            var assemblyName = Path.GetFileName(assemblyPath);
            File.Copy(assemblyPath, Path.Combine(directory, assemblyName), overwrite: true);
            File.WriteAllText(
                Path.Combine(directory, "module.json"),
                $$"""
                {
                  "id": "test/nested-input",
                  "entryAssembly": "{{assemblyName}}",
                  "entryType": "HYWDebugAssistant.Tests.BusinessModulesIntegrationTests+NestedInputModule"
                }
                """);

            var result = ModulePackageLoader.Load(directory);

            result.Succeeded.ShouldBeFalse();
            result.FailureReason.ShouldNotBeNull();
            result.FailureReason.ShouldNotBeNullOrWhiteSpace();
        }
        finally
        {
            try
            {
                Directory.Delete(directory, recursive: true);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // 已加载程序集会锁定文件；清理失败不影响测试结论。
            }
        }
    }

    [Fact]
    public void 加载不存在的目录_应失败并给出可读原因()
    {
        var result = ModulePackageLoader.Load(Path.Combine(Path.GetTempPath(), "hyw-nonexistent-" + Guid.NewGuid().ToString("N")));

        result.Succeeded.ShouldBeFalse();
        result.FailureReason.ShouldNotBeNullOrWhiteSpace();
    }

    [Fact]
    public void 清单标识非法_应失败()
    {
        using var temp = new TempPackage(TextSenderPackage, manifestId: "NotAValidId");

        var result = ModulePackageLoader.Load(temp.Directory);

        result.Succeeded.ShouldBeFalse();
        result.FailureReason.ShouldNotBeNullOrWhiteSpace();
    }

    [Fact]
    public void 清单与代码声明不一致_应拒绝加载()
    {
        // 清单写的是另一个合法 ID，而代码声明是 hyw/text-sender → 拒绝（避免同名歧义）
        using var temp = new TempPackage(TextSenderPackage, manifestId: "hyw/other-sender");

        var result = ModulePackageLoader.Load(temp.Directory);

        result.Succeeded.ShouldBeFalse();
        result.FailureReason.ShouldNotBeNullOrWhiteSpace();
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
            // **确定性临时目录**：跨进程复用同一个名字；启动时先尝试删掉上一次的副本
            // （上一进程的锁已释放），本进程内因 LoadFrom 仍锁定而不删 ⇒ 不会无限堆积。
            Directory = Path.Combine(Path.GetTempPath(), "hyw-test-package-text-sender");
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
