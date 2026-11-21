using HYWDebugAssistant.Sdk.Text;

namespace HYWDebugAssistant.Modules.RawToWaveform;

/// <summary>文本键；文案在 <c>resources/i18n/HYWDebugAssistant.Modules.RawToWaveform/zh-Hans.json</c>。</summary>
internal static class RawToWaveformText
{
    public const string Title = "module.title";
    public const string Description = "module.description";
    public const string RoleDecode = "role.decode";
    public const string RoleEmit = "role.emit";
    public const string Channels = "view.channels";
    public const string GroupShort = "view.groupShort";
    public const string Frames = "view.frames";
    public const string Dropped = "view.dropped";
    public const string Reset = "view.reset";
    public const string Ready = "state.ready";
    public const string Converting = "state.converting";
    public const string ResetDone = "state.reset";

    /// <summary>帧头/帧尾没有可用的十六进制值。</summary>
    public const string BadHex = "state.bad-hex";

    /// <summary>帧头/帧尾格式无效，但仍在用上一次有效值。</summary>
    public const string BadHexKept = "state.bad-hex-kept";

    /// <summary>组号必须是 1 基正整数。</summary>
    public const string BadGroup = "state.bad-group";
    public const string InvalidRate = "state.invalid-rate";

    /// <summary>帧头/帧尾输入框的悬停提示。</summary>
    public const string HexHint = "view.hexHint";

    /// <summary>配置字段标题：协议。</summary>
    public const string FieldProtocol = "field.protocol";

    /// <summary>协选项：自定义文本帧。</summary>
    public const string ChoiceTextFrame = "choice.textFrame";

    /// <summary>协选项：VOFA+ FireWater。</summary>
    public const string ChoiceFireWater = "choice.fireWater";

    /// <summary>协选项：VOFA+ JustFloat。</summary>
    public const string ChoiceJustFloat = "choice.justFloat";

    /// <summary>协议说明：文本帧定义。</summary>
    public const string ProtoTextFrameDefinition = "proto.textFrame.def";

    /// <summary>协议说明：文本帧示例。</summary>
    public const string ProtoTextFrameSample = "proto.textFrame.sample";

    /// <summary>协议说明：FireWater 定义。</summary>
    public const string ProtoFireWaterDefinition = "proto.fireWater.def";

    /// <summary>协议说明：FireWater 示例。</summary>
    public const string ProtoFireWaterSample = "proto.fireWater.sample";

    /// <summary>协议说明：JustFloat 定义。</summary>
    public const string ProtoJustFloatDefinition = "proto.justFloat.def";

    /// <summary>协议说明：JustFloat 示例。</summary>
    public const string ProtoJustFloatSample = "proto.justFloat.sample";

    /// <summary>单位：Hz。</summary>
    public const string UnitHz = "unit.hz";

    public static string Get(string key) => TextCatalog.Get(typeof(RawToWaveformText).Assembly, key);

    /// <summary>当前协议的"定义 + 示例"两行说明。</summary>
    public static (string Definition, string Sample) ProtocolHelp(RawToWaveformProtocol protocol) =>
        protocol switch
        {
            RawToWaveformProtocol.FireWater => (Get(ProtoFireWaterDefinition), Get(ProtoFireWaterSample)),
            RawToWaveformProtocol.JustFloat => (Get(ProtoJustFloatDefinition), Get(ProtoJustFloatSample)),
            _ => (Get(ProtoTextFrameDefinition), Get(ProtoTextFrameSample)),
        };
}
