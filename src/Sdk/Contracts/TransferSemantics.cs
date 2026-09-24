namespace HYWDebugAssistant.Sdk.Contracts;

/// <summary>
/// 契约的**传递语义标注**（契约三层能力之①）——ADR-0008 / 主文档 §5.4.1。
/// </summary>
/// <remarks>
/// 跨语义**不判兼容**（需要时由模块内部自行组帧）；该标注参与兼容判定与 UI 呈现。
/// </remarks>
public enum TransferSemantics
{
    /// <summary>按序交付的数据流（最常见的连续数据）。</summary>
    Stream = 0,

    /// <summary>独立成帧的消息（有边界、自成一体）。</summary>
    Message = 1,

    /// <summary>离散事件（发生即通知，通常稀疏）。</summary>
    Event = 2,
}
