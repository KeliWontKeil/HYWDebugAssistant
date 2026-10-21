namespace HYWDebugAssistant.Ui.ModuleViews;

/// <summary>
/// **显示缓冲的一次读取结果**（文本日志 / 十六进制视图共用）。
/// </summary>
/// <param name="Lines">当前保留的全部行（只读快照，可安全跨帧持有）。</param>
/// <param name="Dropped">因超出容量被丢弃的旧行累计数。</param>
/// <param name="Capacity">这块缓冲的行容量。</param>
/// <param name="Sequence">世代号（每次清空递增；读到不同的号说明手里的副本过期）。</param>
/// <param name="Revision">缓冲变更号（追加/替换/清空都会递增；同一个号 ⇒ 内容没变，可以跳过重排）。</param>
internal readonly record struct BufferSnapshot(
    IReadOnlyList<string> Lines,
    int Dropped,
    int Capacity,
    long Sequence,
    long Revision = 0)
{
    /// <summary>空缓冲。</summary>
    public static BufferSnapshot Empty { get; } = new([], 0, 0, 0, 0);
}
