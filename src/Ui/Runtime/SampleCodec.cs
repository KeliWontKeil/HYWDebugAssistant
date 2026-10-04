using HYWDebugAssistant.Sdk.Contracts;
using SampleDecoderReference = HYWDebugAssistant.Sdk.Contracts.PrimitiveSampleDecoder;

namespace HYWDebugAssistant.Ui.Runtime;

/// <summary>
/// **样本解码的参考实现（UI 侧）**——原语 → 数值。
/// </summary>
/// <remarks>
/// <list type="bullet">
///   <item>**呈现期解码应由模块提供**（<c>ViewDataSource.Type</c> → 类型注册的解码器），
///     框架/UI **只回调**；本类因此**降级为参考实现**：仅用于 **未声明类型的旧模块** 的过渡回退
///     （与"结构回退"同一纪律：只为兼容，不作为机制）；</item>
///   <item>新模块**应**用 <c>Sdk.Contracts.PrimitiveSampleDecoder</c>（或自己的实现）注册解码器 ——
///     那是给模块用的公开实现，本类只是 UI 侧保留的回退副本；</item>
///   <item>**不猜类型**：模块未声明原语时解码失败，UI 显示"未声明类型 + 字节数"，而不是按长度推断；</item>
///   <item>字节序与数据面一致（小端，见 `PayloadWriter`）。</item>
/// </list>
/// </remarks>
internal static class SampleCodec
{
    /// <summary>解码一个样本（参考实现）。</summary>
    /// <param name="primitive">声明的契约原语（null = 未声明 → 解码失败）。</param>
    /// <param name="sample">样本字节。</param>
    /// <param name="value">解出的数值。</param>
    public static bool TryDecode(ContractPrimitive? primitive, ReadOnlySpan<byte> sample, out double value)
    {
        value = 0;

        // 参考实现直接复用 SDK 的公开实现（同一套字节序与失败语义，避免两份逻辑漂移）
        return primitive is { } type && SampleDecoderReference.TryDecode(type, sample, out value);
    }

    /// <summary>按声明原语的字节数（未声明时为 0）。</summary>
    public static int SizeOf(ContractPrimitive? primitive) =>
        primitive is { } type ? SampleDecoderReference.SizeOf(type) : 0;
}
