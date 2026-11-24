using System.Buffers.Binary;
using System.Text.Json;
using HYWDebugAssistant.Modules.AttitudeDisplay;
using HYWDebugAssistant.Modules.AttitudeDisplay.Views;
using HYWDebugAssistant.Modules.GaugeDisplay;
using HYWDebugAssistant.Modules.ScaleBarHorizontal;
using HYWDebugAssistant.Modules.ScaleBarVertical;
using HYWDebugAssistant.Modules.WaveformGenerator;
using HYWDebugAssistant.Sdk.Contracts;
using HYWDebugAssistant.Sdk.Ports;
using HYWDebugAssistant.Sdk.Resources;
using HYWDebugAssistant.Sdk.Testing;
using HYWDebugAssistant.Sdk.Values;
using HYWDebugAssistant.Sdk.Views;
using Shouldly;
using Xunit;
using GaugeFeed = HYWDebugAssistant.Modules.GaugeDisplay.ChannelFrameFeed;
using GaugeFrameResult = HYWDebugAssistant.Modules.GaugeDisplay.FrameApplyResult;

namespace HYWDebugAssistant.Modules.Tests;

/// <summary>
/// **四个终端显示模块**（指针仪表盘 / 横向进度条 / 纵向进度条 / 三轴姿态仪）的共用机制：
/// 契约与波形帧生产方一致、值初值无重复、图标可解析、组/通道路由与目标值规则、三轴单位与反相。
/// </summary>
/// <remarks>
/// 只保留看不见的机制不变量；表盘/进度条/三维姿态与浮层设置的观感由截图 + 人眼复核。
/// 四个模块共用一套帧路由代码，因此机制用例只写一份（不是按模块各写一套）。
/// </remarks>
public class ChannelDisplayModulesTests
{
    private const string PortName = "in";

    /// <summary>四个模块与它们的源码目录名（端口声明与值初值都从源码 XAML 读）。</summary>
    public static TheoryData<Type, string> Modules => new()
    {
        { typeof(GaugeDisplayModule), "GaugeDisplay" },
        { typeof(ScaleBarHorizontalModule), "ScaleBarHorizontal" },
        { typeof(ScaleBarVerticalModule), "ScaleBarVertical" },
        { typeof(AttitudeDisplayModule), "AttitudeDisplay" },
    };

    [Theory]
    [MemberData(nameof(Modules))]
    public void 终端显示模块_与波形帧生产方同类型同形状(Type moduleType, string folder)
    {
        var producer = ModuleContract.Probe(
            typeof(WaveformGeneratorModule),
            portSpecs: ModulePortSpecs.Read("WaveformGenerator", typeof(WaveformGeneratorModule).Assembly));
        producer.Problems.ShouldBeEmpty();

        var probe = ModuleContract.Probe(moduleType, portSpecs: ModulePortSpecs.Read(folder, moduleType.Assembly));
        probe.Problems.ShouldBeEmpty();
        probe.Advisories.ShouldBeEmpty();

        var producerType = producer.DataTypes.ShouldHaveSingleItem();
        var type = probe.DataTypes.ShouldHaveSingleItem();
        type.TypeId.ShouldBe("WaveformFrame");
        ContractShape.AreSame(type.Shape, producerType.Shape).ShouldBeTrue();

        var port = probe.Ports.ShouldHaveSingleItem();
        port.Direction.ShouldBe(PortDirection.Receive);
        port.TypeId.ShouldBe(type.TypeId);
        port.Semantics.ShouldBe(TransferSemantics.Stream);
    }

    /// <summary>值初值只能由一个编辑器声明：同名值出现两次会让整包装载失败。</summary>
    [Theory]
    [MemberData(nameof(Modules))]
    public void 值初值读取_四个终端显示模块无重复且可解析(Type moduleType, string folder)
    {
        var directory = Path.Combine(FindRepoRoot(), "src", "Modules", "Business", folder, "Views");
        var result = ValueSpecReader.ReadDirectory(directory, moduleType.Assembly);

        result.Errors.ShouldBeEmpty(
            string.Join(Environment.NewLine, result.Errors.Select(error => $"{error.Code}: {error.Detail}")));
        result.Specs.ShouldNotBeEmpty($"{folder} 的视图没有声明任何值初值");
    }

    /// <summary>图标必须真的随模块输出：键算不出来时按钮上什么都不画（编译期完全合法）。</summary>
    [Theory]
    [MemberData(nameof(Modules))]
    public void 模块图标_设置与关闭键应随输出可解析(Type moduleType, string folder)
    {
        _ = folder;

        foreach (var key in new[] { "icon.settings", "icon.close", "icon.refresh" })
        {
            IconCatalog.TryGet(moduleType.Assembly, key, out var asset)
                .ShouldBeTrue($"{moduleType.Name} 缺少图标 {key}");
            asset.ShouldNotBeNull();
            asset!.PathData.ShouldNotBeEmpty($"{moduleType.Name} 的 {key} 没有路径");
        }
    }

    [Fact]
    public void 目标值解析_新鲜给样本_过期按自动回零给零或保持()
    {
        var feed = new GaugeFeed();

        // 组还没出现：给 0。
        feed.Resolve(5, 0, 0, autoZero: false, delayMs: 1000).ShouldBe(0);

        feed.ApplyFrame(Frame(5, 2, 0b11, [3f, 7f]), 1000);

        // 新鲜：样本值。
        feed.Resolve(5, 0, 1500, autoZero: true, delayMs: 1000).ShouldBe(3.0);
        feed.Resolve(5, 1, 1500, autoZero: true, delayMs: 1000).ShouldBe(7.0);

        // 过期且自动回零关闭：保持最后样本。
        feed.Resolve(5, 0, 3000, autoZero: false, delayMs: 1000).ShouldBe(3.0);

        // 过期且自动回零开启：0。
        feed.Resolve(5, 0, 3000, autoZero: true, delayMs: 1000).ShouldBe(0);

        // 通道越界 / 组不存在：都按"没有样本"给 0，不做钳制。
        feed.Resolve(5, 9, 1500, autoZero: false, delayMs: 1000).ShouldBe(0);
        feed.Resolve(6, 0, 1500, autoZero: false, delayMs: 1000).ShouldBe(0);
    }

    [Fact]
    public void 组发现_通道掩码按帧生效()
    {
        var feed = new GaugeFeed();

        feed.ApplyFrame(Frame(7, 2, 0b01, [1f, 2f]), 0)
            .HasFlag(GaugeFrameResult.StructureChanged)
            .ShouldBeTrue();
        feed.GroupIds.ShouldBe([7u]);
        feed.HasChannel(7, 1).ShouldBeTrue();
        feed.HasChannel(7, 2).ShouldBeFalse();
        feed.ChannelValid(7, 0).ShouldBeTrue();
        feed.ChannelValid(7, 1).ShouldBeFalse();

        feed.ApplyFrame(Frame(9, 1, 0b1, [5f]), 1);
        feed.GroupIds.ShouldBe([7u, 9u]);
    }

    [Fact]
    public void 无效掩码_保持最后有效样本且不刷新有效时刻()
    {
        var feed = new GaugeFeed();
        feed.ApplyFrame(Frame(1, 2, 0b01, [4f, 9f]), 100);
        feed.Resolve(1, 0, 100, autoZero: true, delayMs: 50).ShouldBe(4.0);

        // 通道 0 本次无效：目标保持 4，过期判定仍以 100 为最后有效时刻。
        var result = feed.ApplyFrame(Frame(1, 2, 0b10, [11f, 9f]), 200);
        result.HasFlag(GaugeFrameResult.InvalidFrame).ShouldBeFalse();
        feed.Resolve(1, 0, 200, autoZero: false, delayMs: 1000).ShouldBe(4.0);
        feed.Resolve(1, 0, 260, autoZero: true, delayMs: 100).ShouldBe(0);
        feed.ChannelValid(1, 0).ShouldBeFalse();
    }

    [Fact]
    public void 仪表盘_按输入的通道号发布显示值()
    {
        var context = new TestModuleContext();
        var module = new GaugeDisplayModule();
        module.OnRegistered(context);

        // 组号/通道号都是 1 基；显式组 3、通道 2 → 帧槽位 1。
        context.ViewState.SetInputText(GaugeDisplayModule.GroupSource, "3");
        context.ViewState.SetInputText(GaugeDisplayModule.ChannelSource, "2");

        context.Deliver(PortName, Frame(3, 2, 0b11, [2.5f, 8f]));
        module.PumpOnce(context);

        Published(context, GaugeDisplayModule.ValueSource).ShouldBe(8f, 1e-6);

        // 改成通道 1 后下一拍取第一个槽位。
        context.ViewState.SetInputText(GaugeDisplayModule.ChannelSource, "1");
        module.PumpOnce(context);
        Published(context, GaugeDisplayModule.ValueSource).ShouldBe(2.5f, 1e-6);
    }

    [Fact]
    public void 仪表盘_组号和通道号零非法()
    {
        var context = new TestModuleContext();
        var module = new GaugeDisplayModule();
        module.OnRegistered(context);

        context.ViewState.SetInputText(GaugeDisplayModule.GroupSource, "0");
        context.ViewState.SetInputText(GaugeDisplayModule.ChannelSource, "0");
        module.PumpOnce(context);

        // 0 不生效：保留默认组 1 / 通道 1，不自动跟随，也不改写成别的编号。
        ReadMetaInt(context, GaugeDisplayModule.MetaSource, "group").ShouldBe(1);
        ReadMetaInt(context, GaugeDisplayModule.MetaSource, "channel").ShouldBe(1);

        context.ViewState.SetInputText(GaugeDisplayModule.GroupSource, "2");
        context.ViewState.SetInputText(GaugeDisplayModule.ChannelSource, "2");
        module.PumpOnce(context);

        ReadMetaInt(context, GaugeDisplayModule.MetaSource, "group").ShouldBe(2);
        ReadMetaInt(context, GaugeDisplayModule.MetaSource, "channel").ShouldBe(2);
    }

    [Fact]
    public void 三轴姿态_通道重复时三格都标为无效_修正后清除()
    {
        var context = new TestModuleContext();
        var module = new AttitudeDisplayModule();
        module.OnRegistered(context);

        // 真实视图会同时有这三个框；只设一个会让冲突判定因“另一格不可解析”而走不到。
        context.ViewState.SetInputText(AttitudeDisplayModule.RollChannelSource, "1");
        context.ViewState.SetInputText(AttitudeDisplayModule.PitchChannelSource, "2");
        context.ViewState.SetInputText(AttitudeDisplayModule.YawChannelSource, "3");
        module.PumpOnce(context);

        context.ViewState.SetInputText(AttitudeDisplayModule.RollChannelSource, "2");
        module.PumpOnce(context);

        context.ValueValidities[AttitudeDisplayModule.RollChannelSource].IsInvalid.ShouldBeTrue();
        context.ValueValidities[AttitudeDisplayModule.PitchChannelSource].IsInvalid.ShouldBeTrue();
        context.ValueValidities[AttitudeDisplayModule.YawChannelSource].IsInvalid.ShouldBeTrue();

        // 再走一拍：红必须还在（冲突按当前文本判定，不因配置回退而消失）。
        module.PumpOnce(context);
        context.ValueValidities[AttitudeDisplayModule.RollChannelSource].IsInvalid.ShouldBeTrue();
        context.ValueValidities[AttitudeDisplayModule.PitchChannelSource].IsInvalid.ShouldBeTrue();
        context.ValueValidities[AttitudeDisplayModule.YawChannelSource].IsInvalid.ShouldBeTrue();

        context.ViewState.SetInputText(AttitudeDisplayModule.RollChannelSource, "1");
        module.PumpOnce(context);
        context.ValueValidities.ContainsKey(AttitudeDisplayModule.RollChannelSource).ShouldBeFalse();
        context.ValueValidities.ContainsKey(AttitudeDisplayModule.PitchChannelSource).ShouldBeFalse();
        context.ValueValidities.ContainsKey(AttitudeDisplayModule.YawChannelSource).ShouldBeFalse();
    }

    [Fact]
    public void 三轴姿态_按单位换算并支持反相()
    {
        var context = new TestModuleContext();
        var module = new AttitudeDisplayModule();
        module.OnRegistered(context);

        context.ViewState.SetInputText(AttitudeDisplayModule.UnitSource, "rad");
        context.ViewState.SetInputText(AttitudeDisplayModule.InvertRollSource, "true");

        context.Deliver(PortName, Frame(1, 3, 0b111, [MathF.PI / 2, 0f, 0f]));
        module.PumpOnce(context);

        Published(context, AttitudeDisplayModule.RollSource).ShouldBe(-90f, 0.01f);
        Published(context, AttitudeDisplayModule.PitchSource).ShouldBe(0f, 0.01f);
        Published(context, AttitudeDisplayModule.YawSource).ShouldBe(0f, 0.01f);
    }

    [Fact]
    public void 进度条颜色_十六进制写入元数据_空与非法回默认()
    {
        var context = new TestModuleContext();
        var module = new ScaleBarHorizontalModule();
        module.OnRegistered(context);

        context.ViewState.SetInputText(ScaleBarHorizontalModule.ColorSource, "FF112233");
        module.PumpOnce(context);
        ReadMetaString(context, ScaleBarHorizontalModule.MetaSource, "color").ShouldBe("FF112233");

        context.ViewState.SetInputText(ScaleBarHorizontalModule.ColorSource, string.Empty);
        module.PumpOnce(context);
        ReadMetaString(context, ScaleBarHorizontalModule.MetaSource, "color").ShouldBe(string.Empty);

        context.ViewState.SetInputText(ScaleBarHorizontalModule.ColorSource, "not-a-color");
        module.PumpOnce(context);
        ReadMetaString(context, ScaleBarHorizontalModule.MetaSource, "color").ShouldBe(string.Empty);
    }

    [Fact]
    public void 设置数值_非法或空保留上一份有效值_框架清空回默认()
    {
        var context = new TestModuleContext();
        var module = new GaugeDisplayModule();
        module.OnRegistered(context);

        context.ViewState.SetInputText(GaugeDisplayModule.GroupSource, "2");
        context.ViewState.SetInputText(GaugeDisplayModule.ZeroDelaySource, "1500");
        module.PumpOnce(context);
        ReadMetaInt(context, GaugeDisplayModule.MetaSource, "group").ShouldBe(2);
        ReadMetaInt(context, GaugeDisplayModule.MetaSource, "zeroDelayMs").ShouldBe(1500);

        // 用户输入非法/空：保留上一份有效值，不回默认。
        context.ViewState.SetInputText(GaugeDisplayModule.GroupSource, "x");
        context.ViewState.SetInputText(GaugeDisplayModule.ZeroDelaySource, "abc");
        module.PumpOnce(context);
        ReadMetaInt(context, GaugeDisplayModule.MetaSource, "group").ShouldBe(2);
        ReadMetaInt(context, GaugeDisplayModule.MetaSource, "zeroDelayMs").ShouldBe(1500);

        context.ViewState.SetInputText(GaugeDisplayModule.ZeroDelaySource, string.Empty);
        module.PumpOnce(context);
        ReadMetaInt(context, GaugeDisplayModule.MetaSource, "zeroDelayMs").ShouldBe(1500);

        // 框架清空（还原/移除值）：回默认。
        context.ViewState.SetInputText(GaugeDisplayModule.ZeroDelaySource, null);
        module.PumpOnce(context);
        ReadMetaInt(context, GaugeDisplayModule.MetaSource, "zeroDelayMs").ShouldBe(800);
    }

    [Fact]
    public void 设置范围_上下界互逆时保留上一份有效范围()
    {
        var context = new TestModuleContext();
        var module = new GaugeDisplayModule();
        module.OnRegistered(context);

        context.ViewState.SetInputText(GaugeDisplayModule.MaximumSource, "50");
        module.PumpOnce(context);
        ReadMetaDouble(context, GaugeDisplayModule.MetaSource, "min").ShouldBe(0);
        ReadMetaDouble(context, GaugeDisplayModule.MetaSource, "max").ShouldBe(50);

        context.ViewState.SetInputText(GaugeDisplayModule.MinimumSource, "100");
        module.PumpOnce(context);
        ReadMetaDouble(context, GaugeDisplayModule.MetaSource, "min").ShouldBe(0);
        ReadMetaDouble(context, GaugeDisplayModule.MetaSource, "max").ShouldBe(50);

        // 上限改到 150 后当前文本 100..150 成为合法范围：未变的 min 文本也要落到 100。
        context.ViewState.SetInputText(GaugeDisplayModule.MaximumSource, "150");
        module.PumpOnce(context);
        ReadMetaDouble(context, GaugeDisplayModule.MetaSource, "min").ShouldBe(100);
        ReadMetaDouble(context, GaugeDisplayModule.MetaSource, "max").ShouldBe(150);
    }

    [Fact]
    public void 设置范围_非法边界下一帧仍标红_修正后清除()
    {
        var context = new TestModuleContext();
        var module = new GaugeDisplayModule();
        module.OnRegistered(context);

        context.ViewState.SetInputText(GaugeDisplayModule.MinimumSource, "abc");
        module.PumpOnce(context);
        context.ValueValidities.ContainsKey(GaugeDisplayModule.MinimumSource).ShouldBeTrue();
        context.AppearanceReports.ShouldContain(
            report => report.SourceName == GaugeDisplayModule.MinimumSource
                      && report.OutlineRole == ViewInkRoles.Danger);

        // 下一帧不能被“范围看起来合法”的分支清掉。
        module.PumpOnce(context);
        context.ValueValidities.ContainsKey(GaugeDisplayModule.MinimumSource).ShouldBeTrue();

        context.ViewState.SetInputText(GaugeDisplayModule.MinimumSource, "0");
        module.PumpOnce(context);
        context.ValueValidities.ContainsKey(GaugeDisplayModule.MinimumSource).ShouldBeFalse();
        context.AppearanceReports.ShouldContain(
            report => report.SourceName == GaugeDisplayModule.MinimumSource
                      && report.OutlineRole == null);
    }

    [Fact]
    public void 设置范围_上下界互逆时两个字段都保持标红_修正后都清()
    {
        var context = new TestModuleContext();
        var module = new GaugeDisplayModule();
        module.OnRegistered(context);

        context.ViewState.SetInputText(GaugeDisplayModule.MaximumSource, "50");
        module.PumpOnce(context);
        context.ViewState.SetInputText(GaugeDisplayModule.MinimumSource, "100");
        module.PumpOnce(context);

        context.ValueValidities.ContainsKey(GaugeDisplayModule.MinimumSource).ShouldBeTrue();
        context.ValueValidities.ContainsKey(GaugeDisplayModule.MaximumSource).ShouldBeTrue();
        module.PumpOnce(context);
        context.ValueValidities.ContainsKey(GaugeDisplayModule.MinimumSource).ShouldBeTrue();
        context.ValueValidities.ContainsKey(GaugeDisplayModule.MaximumSource).ShouldBeTrue();

        context.ViewState.SetInputText(GaugeDisplayModule.MaximumSource, "150");
        module.PumpOnce(context);
        context.ValueValidities.ContainsKey(GaugeDisplayModule.MinimumSource).ShouldBeFalse();
        context.ValueValidities.ContainsKey(GaugeDisplayModule.MaximumSource).ShouldBeFalse();
    }

    [Fact]
    public void 设置范围_一侧非法时合法侧不标红()
    {
        var context = new TestModuleContext();
        var module = new GaugeDisplayModule();
        module.OnRegistered(context);

        context.ViewState.SetInputText(GaugeDisplayModule.MaximumSource, "50");
        module.PumpOnce(context);
        context.ViewState.SetInputText(GaugeDisplayModule.MinimumSource, "abc");
        module.PumpOnce(context);

        context.ValueValidities.ContainsKey(GaugeDisplayModule.MinimumSource).ShouldBeTrue();
        context.ValueValidities.ContainsKey(GaugeDisplayModule.MaximumSource).ShouldBeFalse();
    }

    private static string ReadMetaString(TestModuleContext context, string metaSource, string name)
    {
        var text = context.SetTexts.LastOrDefault(item => string.Equals(item.SourceName, metaSource, StringComparison.Ordinal)).Text;
        text.ShouldNotBeNull();
        using var document = JsonDocument.Parse(text);
        return document.RootElement.GetProperty("settings").GetProperty(name).GetString() ?? string.Empty;
    }

    private static int ReadMetaInt(TestModuleContext context, string metaSource, string name)
    {
        var text = context.SetTexts.LastOrDefault(item => string.Equals(item.SourceName, metaSource, StringComparison.Ordinal)).Text;
        text.ShouldNotBeNull();
        using var document = JsonDocument.Parse(text);
        return document.RootElement.GetProperty("settings").GetProperty(name).GetInt32();
    }

    private static double ReadMetaDouble(TestModuleContext context, string metaSource, string name)
    {
        var text = context.SetTexts.LastOrDefault(item => string.Equals(item.SourceName, metaSource, StringComparison.Ordinal)).Text;
        text.ShouldNotBeNull();
        using var document = JsonDocument.Parse(text);
        return document.RootElement.GetProperty("settings").GetProperty(name).GetDouble();
    }

    /// <summary>造一帧 280 B 波形帧。</summary>
    private static byte[] Frame(uint groupId, int channelCount, ulong validMask, float[] samples)
    {
        var frame = new byte[280];
        WaveformFrameWriter.Write(frame, groupId, 1, 1000, channelCount, validMask, samples);
        return frame;
    }

    /// <summary>读某个显示源最近一次发布的浮点值。</summary>
    private static float Published(TestModuleContext context, string source)
    {
        context.Published.TryGetValue(source, out var values).ShouldBeTrue($"显示源 '{source}' 没有发布过值");
        values.ShouldNotBeEmpty();
        return BinaryPrimitives.ReadSingleLittleEndian(values[^1]);
    }

    /// <summary>从测试输出目录往上找仓库根（有 `HYWDebugAssistant.slnx` 的那一层）。</summary>
    private static string FindRepoRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "HYWDebugAssistant.slnx")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new InvalidOperationException("找不到仓库根（HYWDebugAssistant.slnx）");
    }
}
