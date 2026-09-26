using HYWDebugAssistant.Core.Composition;
using HYWDebugAssistant.Core.Runtime;
using HYWDebugAssistant.Core.Services;
using HYWDebugAssistant.Modules.Reference.ManualSender;
using HYWDebugAssistant.Modules.Reference.Monitor;
using HYWDebugAssistant.Sdk.Contracts;
using Shouldly;
using Xunit;

namespace HYWDebugAssistant.Tests.Reference;

/// <summary>
/// M1-15/M1-16：**手工验收用的两个测试模块**的自动化覆盖（`11` §5 的"避免只服务手工验收、不受自动化保护"）。
/// </summary>
/// <remarks>
/// 覆盖"人来戳"的完整链路：手的动作 = **文本输入（B5）+ 命令按钮（B2）+ 开关（A10）**
/// → 投递到模块线程的收件箱 → 模块活动体消费 → 网络 → 信息显示模块 → 最后值/计数/最值。
/// **【M1-16 / ADR-0040】不再用"写配置即发送"那条权宜通道**（配置只承载非交互参数）。
/// </remarks>
public class ManualChainTests
{
    [Fact]
    public void 手动发送器_输入值加发送命令应产生一个数据单元()
    {
        using var composition = new Composition();
        composition.InstallReferencePackages();

        composition.Services.AddModule("sender", ManualSenderModuleId).Succeeded.ShouldBeTrue();
        composition.Services.AddModule("monitor", MonitorModuleId).Succeeded.ShouldBeTrue();

        // 建网唯一机制：两侧写同名网络标签
        composition.Services.UpdatePortNetwork("sender", ManualSenderModule.OutputPort, "bus").Succeeded.ShouldBeTrue();
        composition.Services.UpdatePortNetwork("monitor", MonitorModule.InputPort, "bus").Succeeded.ShouldBeTrue();

        composition.Services.GetNetworks().ShouldHaveSingleItem().Name.ShouldBe("bus");

        // 用户动作：在输入框里填 3.5（回车提交）→ 点"发送"
        composition.Submit("sender", ManualSenderModule.ValueInput, "3.5").Succeeded.ShouldBeTrue();
        composition.Services.SendCommand("sender", ManualSenderModule.SendCommand).Succeeded.ShouldBeTrue();

        composition.WaitForCount("monitor", MonitorModule.CountSource, atLeast: 1)
            .ShouldBeTrue(Diagnose(composition, "monitor", MonitorModule.CountSource));

        composition.WaitUntilLatest("monitor", MonitorModule.LastSource, 3.5)
            .ShouldBeTrue("输入的值应到达接收端");

        // 再填一次值 + 再点一次 → 又发一个（"点一下发一次"）
        var baseline = composition.Written("monitor", MonitorModule.CountSource);

        composition.Submit("sender", ManualSenderModule.ValueInput, "1.25").Succeeded.ShouldBeTrue();
        composition.Services.SendCommand("sender", ManualSenderModule.SendCommand).Succeeded.ShouldBeTrue();

        composition.WaitForCount("monitor", MonitorModule.CountSource, atLeast: baseline + 1)
            .ShouldBeTrue(Diagnose(composition, "monitor", MonitorModule.CountSource));

        composition.Latest("monitor", MonitorModule.LastSource).ShouldBe(1.25, 0.0001);
        composition.Latest("monitor", MonitorModule.MaxSource).ShouldBeGreaterThanOrEqualTo(3.5);
    }

    [Fact]
    public void 手动发送器_自动发送开关应按周期发送()
    {
        using var composition = new Composition();
        composition.InstallReferencePackages();

        composition.Services.AddModule("sender", ManualSenderModuleId).Succeeded.ShouldBeTrue();
        composition.Services.AddModule("monitor", MonitorModuleId).Succeeded.ShouldBeTrue();

        composition.Services.UpdatePortNetwork("sender", ManualSenderModule.OutputPort, "bus").Succeeded.ShouldBeTrue();
        composition.Services.UpdatePortNetwork("monitor", MonitorModule.InputPort, "bus").Succeeded.ShouldBeTrue();

        // 用户动作：周期填 5 ms → 打开"自动发送"开关
        composition.Submit("sender", ManualSenderModule.PeriodInput, "5").Succeeded.ShouldBeTrue();
        composition.Submit("sender", ManualSenderModule.AutoInput, "true").Succeeded.ShouldBeTrue();

        composition.WaitForCount("monitor", MonitorModule.CountSource, atLeast: 5)
            .ShouldBeTrue(Diagnose(composition, "monitor", MonitorModule.CountSource));

        // 关掉开关 → 停发（语义可预期：没人操作就不产生数据）
        composition.Submit("sender", ManualSenderModule.AutoInput, "false").Succeeded.ShouldBeTrue();
        Thread.Sleep(120);

        var settled = composition.Written("monitor", MonitorModule.CountSource);
        Thread.Sleep(120);
        composition.Written("monitor", MonitorModule.CountSource)
            .ShouldBe(settled, "关掉自动发送后不应再产生数据");
    }

    [Fact]
    public void 手动发送器_非法输入应上报Issue而不是静默()
    {
        using var composition = new Composition();
        composition.InstallReferencePackages();

        composition.Services.AddModule("sender", ManualSenderModuleId).Succeeded.ShouldBeTrue();

        composition.Submit("sender", ManualSenderModule.ValueInput, "not-a-number").Succeeded.ShouldBeTrue();

        Composition.WaitUntil(
            () => composition.Services.GetIssues(20).Any(i => i.Message.Contains("无法解析为数值", StringComparison.Ordinal)),
            TimeSpan.FromSeconds(3)).ShouldBeTrue("非法输入必须被模块识别并上报（不静默丢值）");    }

    [Fact]
    public void 命令与输入_未声明的名字应被拒绝()
    {
        using var composition = new Composition();
        composition.InstallReferencePackages();

        composition.Services.AddModule("sender", ManualSenderModuleId).Succeeded.ShouldBeTrue();

        var unknownCommand = composition.Services.SendCommand("sender", "no-such-command");
        unknownCommand.Succeeded.ShouldBeFalse("未声明的命令必须被拒绝：否则用户点了按钮却毫无反应（不静默）");
        unknownCommand.Failures.ShouldContain(f => f.Contains("未声明命令", StringComparison.Ordinal));

        var unknownInput = composition.Submit("sender", "no-such-input", "1");
        unknownInput.Succeeded.ShouldBeFalse("未声明的输入源同样必须被拒绝");
    }

    [Fact]
    public void 信息显示模块_应发布最后值_计数_最值与日志()
    {
        using var composition = new Composition();
        composition.InstallReferencePackages();

        composition.Services.AddModule("sender", ManualSenderModuleId).Succeeded.ShouldBeTrue();
        composition.Services.AddModule("monitor", MonitorModuleId).Succeeded.ShouldBeTrue();

        composition.Services.UpdatePortNetwork("sender", ManualSenderModule.OutputPort, "bus").Succeeded.ShouldBeTrue();
        composition.Services.UpdatePortNetwork("monitor", MonitorModule.InputPort, "bus").Succeeded.ShouldBeTrue();

        composition.Submit("sender", ManualSenderModule.ValueInput, "-2.25").Succeeded.ShouldBeTrue();
        composition.Services.SendCommand("sender", ManualSenderModule.SendCommand).Succeeded.ShouldBeTrue();

        composition.WaitUntilLatest("monitor", MonitorModule.LastSource, -2.25)
            .ShouldBeTrue(Diagnose(composition, "monitor", MonitorModule.LastSource));

        composition.Latest("monitor", MonitorModule.MinSource).ShouldBeLessThanOrEqualTo(-2.25);
        composition.Latest("monitor", MonitorModule.CountSource).ShouldBeGreaterThanOrEqualTo(1);

        // 日志：定长文本样本 → 至少写入过一条（可被 UI 的 A5 元素解码）
        composition.Written("monitor", MonitorModule.LogSource).ShouldBeGreaterThan(0);
        composition.SubscriptionSampleSize("monitor", MonitorModule.LogSource).ShouldBe(48, "日志行按定长发布（框架要求样本长度一致）");
    }

    [Fact]
    public void 两个测试模块_应与参考发生器_接收端同形状可直接互通()
    {
        using var composition = new Composition();
        composition.InstallReferencePackages();

        composition.Services.AddModule("sender", ManualSenderModuleId).Succeeded.ShouldBeTrue();
        composition.Services.AddModule("recorder", "hyw/reference-recorder/1.0.0").Succeeded.ShouldBeTrue();

        // 手动发送器 → 参考记录器（同形状 {seq:u64, value:f32}）
        composition.Services.UpdatePortNetwork("sender", ManualSenderModule.OutputPort, "chain").Succeeded.ShouldBeTrue();
        composition.Services.UpdatePortNetwork("recorder", "in", "chain").Succeeded.ShouldBeTrue();

        // 触发一次发送（输入值 + 发送命令）
        composition.Submit("sender", ManualSenderModule.ValueInput, "7.5").Succeeded.ShouldBeTrue();
        composition.Services.SendCommand("sender", ManualSenderModule.SendCommand).Succeeded.ShouldBeTrue();

        composition.WaitForCount("recorder", "count", atLeast: 1)
            .ShouldBeTrue(Diagnose(composition, "recorder", "count"));

        ManualSenderModule.OutputShape.SizeInBytes
            .ShouldBe(ContractShape.OfStruct(
                ("seq", ContractShape.OfPrimitive(ContractPrimitive.U64)),
                ("value", ContractShape.OfPrimitive(ContractPrimitive.F32))).SizeInBytes);
    }

    private const string ManualSenderModuleId = "hyw/reference-manual-sender/1.0.0";

    private const string MonitorModuleId = "hyw/reference-monitor/1.0.0";

    /// <summary>失败时的现场说明（发送侧计数 / 网络 / Issue），用于定位链路断点。</summary>
    private static string Diagnose(Composition composition, string instanceId, string dataSourceName)
    {
        var networks = string.Join(
            "；",
            composition.Services.GetNetworks().Select(n => $"{n.Name}: {n.Source} → {string.Join(",", n.Targets)}（停流={n.IsStalled}）"));

        var issues = string.Join(
            "；",
            composition.Services.GetIssues(10).Select(i => $"[{i.Severity}] {i.ObjectPath}: {i.Message}"));

        return $"未达到预期样本数：{instanceId}.{dataSourceName} 已写入 {composition.Written(instanceId, dataSourceName)}；"
            + $"发送器已发送 {composition.Written("sender", ManualSenderModule.SentSource)}、丢弃 {composition.Written("sender", ManualSenderModule.DroppedSource)}；"
            + $"网络：{networks}；Issue：{issues}";
    }

    /// <summary>
    /// 测试现场：真实运行时 + 真实服务面 + 参考模块包（经真实包加载路径）。
    /// </summary>
    /// <remarks>`internal` 以便同项目的其它链路测试（如多源链路）复用同一现场，避免两套夹具漂移。</remarks>
    internal sealed class Composition : IDisposable
    {
        public Composition()
        {
            Runtime = new ModuleRuntime();
            Services = new RuntimeServices(Runtime);
        }

        public ModuleRuntime Runtime { get; }

        public RuntimeServices Services { get; }

        /// <summary>装入六个参考桩模块包（走真实包加载路径）。</summary>
        public void InstallReferencePackages()
        {
            foreach (var module in new[] { "DataGenerator", "Recorder", "Asserter", "ManualSender", "Monitor", "Fuser" })
            {
                var directory = PackagePath(module);
                Directory.Exists(directory).ShouldBeTrue($"参考模块未构建：{directory}（请先 build）");
                Services.InstallModulePackage(directory).Succeeded.ShouldBeTrue($"{module} 包应能加载");
            }
        }

        /// <summary>按字段写回实例配置（与 UI 的 Schema 表单同一条命令）。</summary>
        public Core.Api.CommandResult SetConfigurationField(string instanceId, string fieldName, string value) =>
            Services.UpdateConfigurationField(instanceId, fieldName, value);

        /// <summary>写入一个输入值（与 UI 输入框回车提交同一条命令）。</summary>
        public Core.Api.CommandResult Submit(string instanceId, string source, string text) =>
            Services.SubmitInput(instanceId, source, text);

        /// <summary>读某数据源的最新样本（按声明的原语解码）。</summary>
        public double Latest(string instanceId, string dataSourceName)
        {
            var source = Source(instanceId, dataSourceName);
            source.SampleSizeInBytes.ShouldBeGreaterThan(0, $"{instanceId}.{dataSourceName} 尚未写入过样本");
            return ReadLatest(source);
        }

        /// <summary>某数据源已写入的样本数。</summary>
        public long Written(string instanceId, string dataSourceName) => Source(instanceId, dataSourceName).Written;

        /// <summary>某数据源的定长样本字节数。</summary>
        public int SubscriptionSampleSize(string instanceId, string dataSourceName) =>
            Source(instanceId, dataSourceName).SampleSizeInBytes;

        /// <summary>等待某数据源累计写入达到下限。</summary>
        public bool WaitForCount(string instanceId, string dataSourceName, long atLeast) =>
            WaitUntil(() => Written(instanceId, dataSourceName) >= atLeast, TimeSpan.FromSeconds(5));

        /// <summary>等待某数据源的最新样本等于期望值（浮点容差内）。</summary>
        public bool WaitUntilLatest(string instanceId, string dataSourceName, double expected) =>
            WaitUntil(
                () =>
                {
                    var source = Source(instanceId, dataSourceName);
                    return source.SampleSizeInBytes > 0
                        && Math.Abs(ReadLatest(source) - expected) < 0.0001;
                },
                TimeSpan.FromSeconds(5));

        /// <summary>
        /// 读某数据源的最新样本：**优先走模块注册的解码器**【ADR-0046 D1 ③】，未提供时按原语走参考实现。
        /// </summary>
        /// <remarks>这样 T2 顺带覆盖"框架交出解码器句柄 → 呈现层回调"这条链路，而不只是原语回退路径。</remarks>
        private double ReadLatest(Core.Api.SubscriptionSourceView source)
        {
            var buffer = new byte[source.SampleSizeInBytes];
            if (!Services.TryReadLatest(source.InstanceId, source.DataSourceName, buffer, out var length)
                || length != source.SampleSizeInBytes)
            {
                return double.NaN;
            }

            if (source.Decoder is { } decoder)
            {
                return decoder(buffer, out var decoded) ? decoded : double.NaN;
            }

            return source.ValueType switch
            {
                ContractPrimitive.F32 => BitConverter.ToSingle(buffer, 0),
                ContractPrimitive.F64 => BitConverter.ToDouble(buffer, 0),
                ContractPrimitive.U64 => BitConverter.ToUInt64(buffer, 0),
                _ => double.NaN,
            };
        }

        public void Dispose() => Runtime.Dispose();

        private Core.Api.SubscriptionSourceView Source(string instanceId, string dataSourceName) =>
            Services.GetSubscriptionSources().Single(s =>
                string.Equals(s.InstanceId, instanceId, StringComparison.Ordinal)
                && string.Equals(s.DataSourceName, dataSourceName, StringComparison.Ordinal));

        private static string PackagePath(string module)
        {
            var root = FindRepoRoot();
            return Path.Combine(root, "src", "Modules", "Reference", module, "bin", BuildConfiguration(), "net10.0");
        }

        private static string BuildConfiguration() =>
#if DEBUG
            "Debug";
#else
            "Release";
#endif

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

        /// <summary>等待条件成立（测试体可用来断言 Issue 之类的异步效果）。</summary>
        public static bool WaitUntil(Func<bool> condition, TimeSpan timeout)
        {
            var deadline = DateTime.UtcNow + timeout;
            while (DateTime.UtcNow < deadline)
            {
                if (condition())
                {
                    return true;
                }

                Thread.Sleep(10);
            }

            return condition();
        }
    }
}
