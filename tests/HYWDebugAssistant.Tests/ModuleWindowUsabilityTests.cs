using HYWDebugAssistant.Core.Api;
using HYWDebugAssistant.Core.Composition;
using HYWDebugAssistant.Core.Runtime;
using HYWDebugAssistant.Core.Services;
using HYWDebugAssistant.Ui.ViewModels;
using HYWDebugAssistant.Ui.Views;
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
    public void 模块窗口_尺寸不由内容决定_内容排不下时由滚动条处理()
    {
        using var harness = new Harness();
        harness.Register("enum-config", typeof(EnumConfigModule));
        harness.Services.AddModule("c1", "test/enum-config/1.0.0").Succeeded.ShouldBeTrue();

        harness.Windows.Refresh();
        var module = harness.Windows.Modules.ShouldHaveSingleItem();

        // 【用户第九轮反馈 · UI 开发方式】外层容器尺寸**固定**：
        // 尺寸 = XAML 默认值（与常量同步），**不随元素多少变化**；内容排不下由内容区 ScrollViewer 滚动。
        module.Width.ShouldBe(ModuleViewViewModel.DefaultWidth, "尺寸默认值 = ModuleWindow.axaml（不由内容估算）");
        module.Height.ShouldBe(ModuleViewViewModel.DefaultHeight, "同上：不再按元素类型累计高度");
        module.Elements.OfType<ConfigurationEditorElementViewModel>().ShouldHaveSingleItem();
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
        marker.NetworkText.ShouldBe("bus-x", "标签已生效就必须看得见——显示\"无网络\"会让用户以为改标签没生效");
        marker.Tooltip.ShouldContain("bus-x");
    }

    [Fact]
    public void 端口标记_显示文本不得再出现连接状态或数据包类型()
    {
        // 【用户第十三轮反馈】标记上的显示文本**只有网络名**：
        //   · 连接状态由**指示灯**表达（不再追"（未连）"后缀，"没连上时灯会灭，文字多余"）；
        //   · 数据包类型由**窄色条**表达（类型文本只在悬停提示里）；标签名通常很长，塞不下。
        using var harness = new Harness();
        harness.Register("sender", typeof(SenderModule));
        harness.Services.AddModule("s1", "test/sender/1.0.0").Succeeded.ShouldBeTrue();
        harness.Windows.Refresh();

        var marker = harness.Windows.Modules.ShouldHaveSingleItem().Ports.ShouldHaveSingleItem();

        // ① 未连、无标签
        marker.NetworkText.ShouldBe("无网络");
        marker.NetworkText.ShouldNotContain("未连");
        marker.NetworkText.ShouldNotContain("已连接");

        // ② 有标签但未连（此前会追"（未连）"）
        marker.EditText = "bus-x";
        marker.IsEditing = true;
        _ = harness.Windows.CommitPortLabel(harness.Windows.Modules.Single(), marker);

        marker.IsConnected.ShouldBeFalse("只有发送端命名时还不成网");
        marker.NetworkText.ShouldBe("bus-x");
        marker.NetworkText.ShouldNotContain("未连");
        marker.NetworkText.ShouldNotContain("已连接");

        // ③ 类型文本不再出现在标记显示文本里（改由窄色条 + 悬停提示承担）
        marker.TypeText.ShouldNotBeNullOrEmpty("类型文本本身仍然存在（悬停里要显示）");
        marker.NetworkText.ShouldNotContain(marker.TypeText);

        // ④ 窄色条：颜色来自**唯一入口** PacketTypePalette，且按**类型身份**取值【ADR-0049 D6/Q4】
        //    （不再是"一律占位色"：自动配色保证同一类型恒同色 ⇒ 端口标记与角色方块颜色一致）
        marker.TypeBrush.ShouldBeSameAs(
            PacketTypePalette.For(marker.TypeKey),
            "色条颜色必须经 PacketTypePalette（改映射只改那一处）");
        PacketTypePalette.For(marker.TypeKey).ShouldBeSameAs(
            PacketTypePalette.For(marker.TypeKey),
            "同一类型身份 ⇒ 恒取同一画刷（哈希配色是纯函数）");
    }

    [Fact]
    public void 端口标记_悬停提示应给出类型但不携带上次操作()
    {
        // 【用户第十三轮反馈】① 类型**允许**在悬停里显式给出；② 悬停**不得**显示"上次操作"，该字段也不再保存。
        using var harness = new Harness();
        harness.Register("sender", typeof(SenderModule));
        harness.Services.AddModule("s1", "test/sender/1.0.0").Succeeded.ShouldBeTrue();
        harness.Windows.Refresh();

        var marker = harness.Windows.Modules.ShouldHaveSingleItem().Ports.ShouldHaveSingleItem();
        marker.EditText = "bus-x";
        marker.IsEditing = true;
        _ = harness.Windows.CommitPortLabel(harness.Windows.Modules.Single(), marker);

        marker.Tooltip.ShouldContain("类型：");
        marker.Tooltip.Contains(marker.TypeText, StringComparison.Ordinal)
            .ShouldBeTrue("类型只在悬停里给出 → 悬停必须真的带上它");
        marker.Tooltip.ShouldContain("bus-x");
        marker.Tooltip.Contains("上次", StringComparison.Ordinal)
            .ShouldBeFalse("悬停提示不得携带\"上次操作\"（且该数据不再保存）");

        // 该数据已从视图模型上删除：标记上不应再有承载"上次操作"的成员
        typeof(PortMarkerViewModel).GetProperty("EditResult")
            .ShouldBeNull("「上次操作」字段必须整体删除（模板/绑定随之失效）");
    }

    [Fact]
    public void 两条独立自动建网链路_刷新不得崩溃且各显示各自的投递量()
    {
        // 【P40 回归 · 用户实测崩溃】第二条自动建网链路建立瞬间，`RefreshMarkerStatus` 抛
        // `ArgumentException: An item with the same key has already been added. Key: `（**空键**）。
        // 根因：诊断字典**按网络名索引**，而匿名网络名为空串 ⇒ 两条匿名网络撞键。
        using var harness = new Harness();
        harness.Register("sender", typeof(SenderModule));
        harness.Register("receiver", typeof(ReceiverModule));

        foreach (var id in new[] { "s1", "r1", "s2", "r2" })
        {
            harness.Services.AddModule(id, id.StartsWith('s') ? "test/sender/1.0.0" : "test/receiver/1.0.0")
                .Succeeded.ShouldBeTrue();
        }

        // 两组互不相邻的**接触对**（各自自动建网：右边缘贴左边缘 + 端口标记都在 0.5）
        Place(harness, "s1", 0, 0);
        Place(harness, "r1", 300, 0);
        Place(harness, "s2", 0, 400);
        Place(harness, "r2", 300, 400);

        harness.Services.GetNetworks().Count.ShouldBe(2, "两条独立链路");

        // 刷新（旧实现在这里抛异常）；顺带把"每条链路显示自己的数字"钉住
        harness.SendOneUnit();
        var ok = Harness.WaitUntil(
            () =>
            {
                harness.Windows.Refresh();
                return harness.Windows.Modules.Single(m => m.InstanceId == "s1").Ports.Single().Delivered > 0;
            },
            TimeSpan.FromSeconds(5));

        ok.ShouldBeTrue("刷新技术上必须不抛异常并且能读到投递量");

        var first = harness.Windows.Modules.Single(m => m.InstanceId == "s1").Ports.Single();
        var second = harness.Windows.Modules.Single(m => m.InstanceId == "s2").Ports.Single();

        first.Delivered.ShouldBeGreaterThan(0, "第一条链路有数据");
        second.Delivered.ShouldBe(0, "第二条链路**不得**显示第一条链路的数字（旧实现按名撞键会张冠李戴）");
        first.NetworkName.ShouldBeNull("匿名网络 ⇒ 网络名为 null（界面上显示「（直连）」）");
        second.NetworkName.ShouldBeNull();
    }

    /// <summary>摆放模块（对齐建网需要几何：接触 + 端口标记都对在边缘中点）。</summary>
    private static void Place(Harness harness, string instanceId, double x, double y) =>
        harness.Services.UpdateModuleLayout(new ModuleLayoutView(instanceId, x, y, 300, 200))
            .Succeeded.ShouldBeTrue();

    [Fact]
    public void 还原_选择尺寸和配置时应同时还原配置到默认值()
    {
        using var harness = new Harness();
        harness.Register("enum-config", typeof(EnumConfigModule));
        harness.Services.AddModule("c1", "test/enum-config/1.0.0").Succeeded.ShouldBeTrue();
        harness.Windows.Refresh();

        // 先把 Count 改成 42（默认 10）
        harness.Services.UpdateConfigurationField("c1", nameof(EnumConfig.Count), "42").Succeeded.ShouldBeTrue();
        ConfigValue(harness, "c1", nameof(EnumConfig.Count)).ShouldBe("42");

        var module = harness.Windows.Modules.ShouldHaveSingleItem();
        harness.Windows.ApplyReset(module, ModuleResetScope.SizeAndConfiguration);

        ConfigValue(harness, "c1", nameof(EnumConfig.Count))
            .ShouldBe("10", "「尺寸和配置都还原」应把配置回到模块默认值（丢弃已保存的配置对象）");
    }

    [Fact]
    public void 还原_只还原尺寸时不应动配置()
    {
        using var harness = new Harness();
        harness.Register("enum-config", typeof(EnumConfigModule));
        harness.Services.AddModule("c1", "test/enum-config/1.0.0").Succeeded.ShouldBeTrue();
        harness.Windows.Refresh();

        harness.Services.UpdateConfigurationField("c1", nameof(EnumConfig.Count), "42").Succeeded.ShouldBeTrue();

        harness.Windows.ApplyReset(harness.Windows.Modules.ShouldHaveSingleItem(), ModuleResetScope.SizeOnly);

        ConfigValue(harness, "c1", nameof(EnumConfig.Count))
            .ShouldBe("42", "只还原尺寸时配置必须保持不变");
    }

    /// <summary>读某实例某配置字段的当前值（断言配置是否被还原用）。</summary>
    private static string ConfigValue(Harness harness, string instanceId, string fieldName)
    {
        harness.Services.TryGetConfigurationSchema(instanceId, out var schema).ShouldBeTrue();
        schema.ShouldNotBeNull();
        return schema!.Fields.Single(f => f.Name == fieldName).Value;
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

            // 【2026-09 用户裁定：删除整个配置框】框架不再自动补 ⇒ 本用例显式声明 B1 以继续覆盖"枚举字段"渲染
            context.Declaration.DeclareView(new HYWDebugAssistant.Sdk.Views.ViewDeclaration(
                [new HYWDebugAssistant.Sdk.Views.ViewElement(
                    HYWDebugAssistant.Sdk.Views.ViewElementKind.ConfigurationEditor,
                    title: "参数")],
                []));
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
