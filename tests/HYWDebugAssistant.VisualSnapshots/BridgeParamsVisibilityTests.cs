using System.Threading;
using Avalonia.Controls;
using HYWDebugAssistant.Modules.RawToWaveform;
using HYWDebugAssistant.Modules.RawToWaveform.Views;
using HYWDebugAssistant.Modules.WaveformToRaw;
using HYWDebugAssistant.Modules.WaveformToRaw.Views;
using Xunit;

namespace HYWDebugAssistant.VisualSnapshots;

/// <summary>
/// **参数行可见性回归**：帧信息行只在与协议对应时出现。
/// </summary>
/// <remarks>
/// 历史缺陷：`原始 → 波形` 的视图只切换了帧头/帧尾那一行，前缀行从未被收起，
/// 于是"自定义文本帧"下也摆着一个不起作用的前缀框。这里直接断言两个容器的可见性，
/// 三个协议 + 认不出协议四种输入都覆盖。
/// </remarks>
[Collection("visual-snapshots")]
public class BridgeParamsVisibilityTests
{
    [Fact]
    public async Task 原始到波形_参数行只显示对应协议的那一行()
    {
        await SnapshotSession.Shared.Dispatch(
            () =>
            {
                var view = new RawToWaveformView();
                var textParams = view.FindControl<Control>("TextParams")!;
                var fireWaterParams = view.FindControl<Control>("FireWaterParams")!;

                view.ApplyProtocol(RawToWaveformProtocol.TextFrame);
                Assert.True(textParams.IsVisible);
                Assert.False(fireWaterParams.IsVisible);

                view.ApplyProtocol(RawToWaveformProtocol.FireWater);
                Assert.False(textParams.IsVisible);
                Assert.True(fireWaterParams.IsVisible);

                view.ApplyProtocol(RawToWaveformProtocol.JustFloat);
                Assert.False(textParams.IsVisible);
                Assert.False(fireWaterParams.IsVisible);

                view.ApplyProtocol(null);
                Assert.False(textParams.IsVisible);
                Assert.False(fireWaterParams.IsVisible);
            },
            CancellationToken.None);
    }

    [Fact]
    public async Task 波形到原始_参数行只显示对应协议的那一行()
    {
        await SnapshotSession.Shared.Dispatch(
            () =>
            {
                var view = new WaveformToRawView();
                var textParams = view.FindControl<Control>("TextParams")!;
                var fireWaterParams = view.FindControl<Control>("FireWaterParams")!;

                view.ApplyProtocol(WaveformToRawProtocol.TextFrame);
                Assert.True(textParams.IsVisible);
                Assert.False(fireWaterParams.IsVisible);

                view.ApplyProtocol(WaveformToRawProtocol.FireWater);
                Assert.False(textParams.IsVisible);
                Assert.True(fireWaterParams.IsVisible);

                view.ApplyProtocol(WaveformToRawProtocol.JustFloat);
                Assert.False(textParams.IsVisible);
                Assert.False(fireWaterParams.IsVisible);

                view.ApplyProtocol(null);
                Assert.False(textParams.IsVisible);
                Assert.False(fireWaterParams.IsVisible);
            },
            CancellationToken.None);
    }
}
