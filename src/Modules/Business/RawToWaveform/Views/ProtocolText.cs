namespace HYWDebugAssistant.Modules.RawToWaveform.Views;

/// <summary>把配置字段的值还原成协议；认不出来时返回 <c>null</c>。</summary>
/// <remarks>
/// 只认枚举成员名与数字（配置 Schema 给的就是成员名），**不解析本地化标题**：
/// 标题会随语言/文案变化，拿它当身份是脆的。
/// </remarks>
internal static class ProtocolText
{
    public static RawToWaveformProtocol? Parse(string? text) =>
        Enum.TryParse<RawToWaveformProtocol>(text?.Trim(), ignoreCase: true, out var parsed)
        && Enum.IsDefined(parsed)
            ? parsed
            : null;
}
