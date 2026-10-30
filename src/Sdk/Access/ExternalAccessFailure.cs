namespace HYWDebugAssistant.Sdk.Access;

/// <summary>
/// 一次外部接入失败的**结构化事实**：种类 + 端点 + 原始参数值 + 底层细节。
/// </summary>
/// <remarks>
/// <para>
/// 三个可选字段都只**搬运**、不格式化：端点给"哪里"，值给"填的是什么"，细节给"底层为什么"
/// （异常类型 / 消息 / 期望类型名）。任何面向用户的中文句子都不在这里。
/// </para>
/// <para>
/// 只读快照：模块按 <see cref="Kind"/> 从自己的表选句子，需要时把其余字段当占位参数。
/// </para>
/// </remarks>
/// <param name="Kind">失败种类（模块据此选句子）。</param>
/// <param name="Endpoint">相关端点（语义明确时给；如连接失败的目标、端口占用的本地端点）。</param>
/// <param name="Value">原始参数值（地址 / 端口文本）；只搬运，不格式化。</param>
/// <param name="Detail">底层原因（异常类型与消息、期望类型名）；只搬运，不翻译。</param>
public sealed record ExternalAccessFailure(
    ExternalAccessFailureKind Kind,
    ExternalAccessEndpoint? Endpoint = null,
    string? Value = null,
    string? Detail = null);
