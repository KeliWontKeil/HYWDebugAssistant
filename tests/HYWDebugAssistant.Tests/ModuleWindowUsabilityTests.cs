using HYWDebugAssistant.Core.Api;
using HYWDebugAssistant.Core.Composition;
using HYWDebugAssistant.Core.Runtime;
using HYWDebugAssistant.Core.Services;
using HYWDebugAssistant.Ui.ViewModels;
using Shouldly;
using Xunit;

namespace HYWDebugAssistant.Tests;

/// <summary>
/// 批次 1 可用性修复的回归测试（用户第四轮反馈）：
/// 控件类型互斥（"重影"）· 窗口按内容定尺寸 · 端口标签提交的**反馈闭环** · 标记信息（方向/状态/投递量）。
/// </summary>
public class ModuleWindowUsabilityTests
{
    [Fact]
    public void 配置字段_枚举不得同时显示下拉与文本框()
    {
        using var harness = new Harness();
        harness.Register("enum-config", typeof(EnumConfigModule));
        harness.Services.AddModule("c1", "test/enum-config/1.0.0").Succeeded.ShouldBeTrue();

        harness.Windows.Refresh();
        var editor = harness.Windows.Modules.ShouldHaveSingleItem()
            .Elements.OfType<ConfigurationEditorElementViewModel>().Single();

        var mode = editor.Fields.Single(f => f.Name == "Mode");
        mode.Kind.ShouldBe(ConfigurationFieldKind.Enum);
        mode.IsEnum.ShouldBeTrue();
        mode.IsText.ShouldBeFalse("曾是 !IsBoolean → 下拉与文本框同时可见并叠在同一单元格（界面表现为输入框重影）");
        mode.IsBoolean.ShouldBeFalse();

        var count = editor.Fields.Single(f => f.Name == "Count");
        count.Kind.ShouldBe(ConfigurationFieldKind.WholeNumber);
        count.IsText.ShouldBeTrue();
        count.IsEnum.ShouldBeFalse();
    }

    [Fact]
    public void 模块窗口_首次摆放应按内容估算尺寸而不是固定最小高()
    {
        using var harness = new Harness();
        harness.Register("enum-config", typeof(EnumConfigModule));
        harness.Services.AddModule("c1", "test/enum-config/1.0.0").Succeeded.ShouldBeTrue();

        harness.Windows.Refresh();
        var module = harness.Windows.Modules.ShouldHaveSingleItem();

        // 配置表单（3 个字段）+ 身份行 + 预留带 → 必须明显高于旧的固定 140 DIP 默认值
        module.Height.ShouldBeGreaterThan(ModuleViewViewModel.DefaultHeight, "否则用户必须先手动拉大窗口才能看全内容");
        module.Width.ShouldBeGreaterThanOrEqualTo(ModuleViewViewModel.DefaultWidth, "表单需要更宽的窗口");
    }

    [Fact]
    public void 端口标签_提交后必须给出反馈()
    {
        using var harness = new Harness();
        harness.Register("sender", typeof(SenderModule));
        harness.Register("receiver", typeof(ReceiverModule));
        harness.Services.AddModule("s1", "test/sender/1.0.0").Succeeded.ShouldBeTrue();
        harness.Services.AddModule("r1", "test/receiver/1.0.0").Succeeded.ShouldBeTrue();
        harness.Windows.Refresh();

        var notices = new List<(string Message, bool IsError)>();
        harness.Windows.NoticeRaised += (_, notice) => notices.Add(notice);

        var source = harness.Windows.Modules.Single(m => m.InstanceId == "s1");
        var sink = harness.Windows.Modules.Single(m => m.InstanceId == "r1");

        // ① 只有发送端命名 → 成功但提示"尚无接收端"（运行时给出 notice）
        var sourceMarker = source.Ports.ShouldHaveSingleItem();
        sourceMarker.EditText = "bus-1";
        sourceMarker.IsEditing = true;
        var first = harness.Windows.CommitPortLabel(source, sourceMarker);

        first.ShouldContain("bus-1");
        notices.ShouldNotBeEmpty("提交结果必须反馈（此前调用方把返回值丢掉了 → 用户完全看不到结果）");
        notices[^1].IsError.ShouldBeFalse();
        sourceMarker.IsEditing.ShouldBeFalse("提交后必须关闭编辑框");

        // ② 接收端命名同名 → 网络建立，反馈里能看出已连上
        var sinkMarker = sink.Ports.ShouldHaveSingleItem();
        sinkMarker.EditText = "bus-1";
        sinkMarker.IsEditing = true;
        _ = harness.Windows.CommitPortLabel(sink, sinkMarker);

        harness.Services.GetNetworks().ShouldHaveSingleItem().Name.ShouldBe("bus-1");
        notices[^1].Message.ShouldContain("bus-1");
    }

    [Fact]
    public void 端口标记_应显示方向_连接状态与投递量()
    {
        using var harness = new Harness();
        harness.Register("sender", typeof(SenderModule));
        harness.Register("receiver", typeof(ReceiverModule));
        harness.Services.AddModule("s1", "test/sender/1.0.0").Succeeded.ShouldBeTrue();
        harness.Services.AddModule("r1", "test/receiver/1.0.0").Succeeded.ShouldBeTrue();
        harness.Services.UpdatePortNetwork("s1", "out", "bus").Succeeded.ShouldBeTrue();
        harness.Services.UpdatePortNetwork("r1", "in", "bus").Succeeded.ShouldBeTrue();

        harness.Windows.Refresh();
        var source = harness.Windows.Modules.Single(m => m.InstanceId == "s1");
        var sink = harness.Windows.Modules.Single(m => m.InstanceId == "r1");

        var sourceMarker = source.Ports.ShouldHaveSingleItem();
        sourceMarker.IsSend.ShouldBeTrue();
        sourceMarker.DirectionText.ShouldBe("发送");
        sourceMarker.Display.ShouldBe("out·bus");

        var sinkMarker = sink.Ports.ShouldHaveSingleItem();
        sinkMarker.IsSend.ShouldBeFalse();
        sinkMarker.DirectionText.ShouldBe("接收");

        // 推一个单元走过去 → 标记上的投递量/字节数应体现（网络粒度估算）
        harness.SendOneUnit();

        var ok = Harness.WaitUntil(() =>
        {
            harness.Windows.Refresh();
            return sourceMarker.IsConnected && sourceMarker.Delivered > 0;
        }, TimeSpan.FromSeconds(5));

        ok.ShouldBeTrue("标记应显示连接状态与投递量（用户要求：标签至少说明方向/状态/通过数据量）");
        sourceMarker.RateText.ShouldNotBeNullOrEmpty("标记应显示数据速率（无流量为 ---）");
        sourceMarker.BytesText.ShouldNotBeNullOrEmpty("字节数 = 投递单元数 × 契约尺寸（估算，工具提示已注明网络粒度）");
        sourceMarker.Tooltip.ShouldContain("投递");
    }

    [Fact]
    public void 端口标签_非法名提交应保持编辑框并就地给出原因()
    {
        using var harness = new Harness();
        harness.Register("sender", typeof(SenderModule));
        harness.Services.AddModule("s1", "test/sender/1.0.0").Succeeded.ShouldBeTrue();
        harness.Windows.Refresh();

        var notices = new List<(string Message, bool IsError)>();
        harness.Windows.NoticeRaised += (_, notice) => notices.Add(notice);

        var marker = harness.Windows.Modules.ShouldHaveSingleItem().Ports.ShouldHaveSingleItem();
        marker.EditText = "含 空格 的名字";       // 非法：含空白（NetworkNaming 拒绝）
        marker.IsEditing = true;

        _ = harness.Windows.CommitPortLabel(harness.Windows.Modules.Single(), marker);

        marker.IsEditing.ShouldBeTrue("失败时必须保持编辑框打开（此前一律先关框 → 用户以为\"改标签没反应\"）");
        marker.HasEditError.ShouldBeTrue();
        marker.EditError.ShouldNotBeNullOrEmpty();
        marker.Label.ShouldBeNull("被拒绝的标签不得落到本地状态上");
        marker.EditText.ShouldBe("含 空格 的名字", "失败时不得清空用户输入");
        notices[^1].IsError.ShouldBeTrue("失败必须报错到状态栏");
    }

    [Fact]
    public void 端口标签_打上标签后即使尚未连上也应显示标签本身()
    {
        using var harness = new Harness();
        harness.Register("sender", typeof(SenderModule));
        harness.Services.AddModule("s1", "test/sender/1.0.0").Succeeded.ShouldBeTrue();
        harness.Windows.Refresh();

        var marker = harness.Windows.Modules.ShouldHaveSingleItem().Ports.ShouldHaveSingleItem();

        // 未命名、未连：无网络
        marker.NetworkText.ShouldBe("无网络");

        marker.EditText = "bus-x";
        marker.IsEditing = true;
        _ = harness.Windows.CommitPortLabel(harness.Windows.Modules.Single(), marker);

        harness.Services.GetNetworks().ShouldBeEmpty("只有发送端命名时还不成网");
        marker.Label.ShouldBe("bus-x");
        marker.NetworkText.ShouldBe("bus-x（未连）", "标签已生效就必须看得见——显示\"无网络\"会让用户以为改标签没生效");
        marker.Tooltip.ShouldContain("bus-x");
    }

    private sealed class Harness : IDisposable
    {
        public Harness()
        {
            Runtime = new ModuleRuntime();
            Services = new RuntimeServices(Runtime);
            Windows = new ModuleWindowsViewModel(Services, Services);
        }

        public ModuleRuntime Runtime { get; }

        public RuntimeServices Services { get; }

        public ModuleWindowsViewModel Windows { get; }

        public void Register(string name, Type entryType)
        {
            var definition = new HYWDebugAssistant.Hosting.Registry.ModuleDefinition(
                HYWDebugAssistant.Sdk.ModuleId.Create("test", name, "1.0.0"),
                entryType.Name,
                string.Empty,
                [],
                HYWDebugAssistant.Sdk.ModuleCapability.None,
                [],
                null,
                true,
                entryType,
                string.Empty,
                string.Empty);

            Services.Registry.TryRegister(definition, out var failure).ShouldBeTrue(failure);
        }

        /// <summary>向 s1.out 推一个数据单元（走真实投递路径）。</summary>
        public void SendOneUnit()
        {
            var instance = Runtime.Content.Instances.Single(i => i.InstanceId == "s1");
            var unit = new byte[instance.Ports.Single(p => p.Name == "out").Declaration.Shape.SizeInBytes];
            _ = instance.Context.Ports.TrySend("out", unit);
        }

        public static bool WaitUntil(Func<bool> condition, TimeSpan timeout)
        {
            var deadline = DateTime.UtcNow + timeout;
            while (DateTime.UtcNow < deadline)
            {
                if (condition())
                {
                    return true;
                }

                Thread.Sleep(20);
            }

            return condition();
        }

        public void Dispose() => Runtime.Dispose();
    }

    /// <summary>带枚举/整数配置的模块（复现"重影"与尺寸问题）。</summary>
    [HYWDebugAssistant.Sdk.ModuleDefinition("test", "enum-config", "1.0.0")]
    public sealed class EnumConfigModule : HYWDebugAssistant.Sdk.Lifecycle.IModule
    {
        public void OnRegistered(HYWDebugAssistant.Sdk.Context.IModuleContext context)
        {
            _ = context.Configuration.Read<EnumConfig>();
        }
    }

    /// <summary>测试配置：枚举 + 整数 + 布尔。</summary>
    public sealed class EnumConfig
    {
        public TestMode Mode { get; set; } = TestMode.Continuous;

        public int Count { get; set; } = 10;

        public bool Enabled { get; set; } = true;
    }

    /// <summary>测试枚举。</summary>
    public enum TestMode
    {
        Continuous = 0,
        Once = 1,
    }

    /// <summary>测试发送模块。</summary>
    [HYWDebugAssistant.Sdk.ModuleDefinition("test", "sender", "1.0.0")]
    public sealed class SenderModule : HYWDebugAssistant.Sdk.Lifecycle.IModule
    {
        public void OnRegistered(HYWDebugAssistant.Sdk.Context.IModuleContext context) =>
            context.Declaration.DeclarePort(new HYWDebugAssistant.Sdk.Ports.PortDeclaration(
                "out",
                HYWDebugAssistant.Sdk.Ports.PortDirection.Send,
                HYWDebugAssistant.Sdk.Contracts.ContractShape.OfPrimitive(
                    HYWDebugAssistant.Sdk.Contracts.ContractPrimitive.F32)));
    }

    /// <summary>测试接收模块。</summary>
    [HYWDebugAssistant.Sdk.ModuleDefinition("test", "receiver", "1.0.0")]
    public sealed class ReceiverModule : HYWDebugAssistant.Sdk.Lifecycle.IModule
    {
        public void OnRegistered(HYWDebugAssistant.Sdk.Context.IModuleContext context)
        {
            context.Declaration.DeclarePort(new HYWDebugAssistant.Sdk.Ports.PortDeclaration(
                "in",
                HYWDebugAssistant.Sdk.Ports.PortDirection.Receive,
                HYWDebugAssistant.Sdk.Contracts.ContractShape.OfPrimitive(
                    HYWDebugAssistant.Sdk.Contracts.ContractPrimitive.F32)));

            context.Ports.SetReceiveHandler("in", _ => { });
        }
    }
}
