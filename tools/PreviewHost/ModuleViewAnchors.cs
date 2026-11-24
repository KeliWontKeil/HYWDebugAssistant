using HYWDebugAssistant.Modules.AttitudeDisplay.Views;
using HYWDebugAssistant.Modules.CommandSender.Views;
using HYWDebugAssistant.Modules.DataMonitor.Views;
using HYWDebugAssistant.Modules.GaugeDisplay.Views;
using HYWDebugAssistant.Modules.HexSender.Views;
using HYWDebugAssistant.Modules.HexStringConverter.Views;
using HYWDebugAssistant.Modules.ImageFrame.Views;
using HYWDebugAssistant.Modules.ScaleBarHorizontal.Views;
using HYWDebugAssistant.Modules.ScaleBarVertical.Views;
using HYWDebugAssistant.Modules.Serial.Views;
using HYWDebugAssistant.Modules.TcpClient.Views;
using HYWDebugAssistant.Modules.TcpServer.Views;
using HYWDebugAssistant.Modules.TextSender.Views;
using HYWDebugAssistant.Modules.Udp.Views;
using HYWDebugAssistant.Modules.WaveformDisplay.Views;
using HYWDebugAssistant.Modules.WaveformGenerator.Views;
using HYWDebugAssistant.Modules.RawToWaveform.Views;
using HYWDebugAssistant.Modules.WaveformToRaw.Views;

namespace HYWDebugAssistant.PreviewHost;

/// <summary>
/// **模块视图的加载锚点**。
/// </summary>
/// <remarks>
/// PreviewHost 的全部模块引用都是“项目引用但代码不直接使用”。Avalonia 预览器在自己的
/// 设计时程序集上下文里解析 <c>x:Class</c> 时，不保证会主动加载这些仅被引用的程序集；
/// 逐个摸一下视图类型，把它们变成真正的运行时依赖，避免
/// “Unable to resolve type HYWDebugAssistant.Modules.X.Views.XView”。
/// </remarks>
internal static class ModuleViewAnchors
{
    /// <summary>全部模块视图类型（保持引用，禁止裁剪/优化掉）。</summary>
    public static readonly Type[] All =
    [
        typeof(AttitudeDisplayView),
        typeof(CommandSenderView),
        typeof(DataMonitorView),
        typeof(GaugeDisplayView),
        typeof(HexSenderView),
        typeof(HexStringConverterView),
        typeof(ImageFrameView),
        typeof(ScaleBarHorizontalView),
        typeof(ScaleBarVerticalView),
        typeof(SerialView),
        typeof(TcpClientView),
        typeof(TcpServerView),
        typeof(TextSenderView),
        typeof(UdpView),
        typeof(WaveformDisplayView),
        typeof(WaveformGeneratorView),
        typeof(RawToWaveformView),
        typeof(WaveformToRawView),
    ];

    /// <summary>触发静态字段初始化（从而加载全部模块程序集）。</summary>
    public static void Touch() => GC.KeepAlive(All);
}
