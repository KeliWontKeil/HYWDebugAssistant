using HYWDebugAssistant.Sdk.Text;

namespace HYWDebugAssistant.Modules.RawToWaveform;

/// <summary>文本键；文案在 <c>resources/i18n/HYWDebugAssistant.Modules.RawToWaveform/zh-Hans.json</c>。</summary>
internal static class RawToWaveformText
{
    public const string Title = "module.title";
    public const string Description = "module.description";
    public const string RoleDecode = "role.decode";
    public const string RoleEmit = "role.emit";
    public const string ViewTitle = "view.title";
    public const string Header = "view.header";
    public const string Footer = "view.footer";
    public const string Channels = "view.channels";
    public const string Frames = "view.frames";
    public const string Dropped = "view.dropped";
    public const string Reset = "view.reset";
    public const string Ready = "state.ready";
    public const string Converting = "state.converting";
    public const string ResetDone = "state.reset";
    public const string BadConfig = "state.bad-config";
    public const string ParseError = "state.parse-error";

    /// <summary>配置字段标题：协议。</summary>
    public const string FieldProtocol = "field.protocol";

    /// <summary>配置字段标题：帧头。</summary>
    public const string FieldHeader = "field.header";

    /// <summary>配置字段标题：帧尾。</summary>
    public const string FieldFooter = "field.footer";

    /// <summary>配置字段标题：通道组编号。</summary>
    public const string FieldGroup = "field.group";

    /// <summary>配置字段标题：采样率。</summary>
    public const string FieldRate = "field.rate";

    /// <summary>协选项：自定义文本帧。</summary>
    public const string ChoiceTextFrame = "choice.textFrame";

    /// <summary>协选项：VOFA+ FireWater。</summary>
    public const string ChoiceFireWater = "choice.fireWater";

    /// <summary>协选项：VOFA+ JustFloat。</summary>
    public const string ChoiceJustFloat = "choice.justFloat";

    public static string Get(string key) => TextCatalog.Get(typeof(RawToWaveformText).Assembly, key);
}
