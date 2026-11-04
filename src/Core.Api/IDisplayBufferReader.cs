using HYWDebugAssistant.Sdk.Views;

namespace HYWDebugAssistant.Core.Api;

/// <summary>
/// **显示文本的只读视图**：UI 只能取快照/窗口/尾部，不能写入。
/// </summary>
/// <remarks>
/// 写者唯一：模块实例线程经 <c>IViewDisplayAccess</c> 写 <c>DisplayBuffer</c>；
/// 本接口只暴露读方法，UI 程序集不再拿到可变的 <c>DisplayBuffer</c>。
/// </remarks>
public interface IDisplayBufferReader
{
    /// <summary>缓冲变更号（追加/替换/清空都会递增）。</summary>
    long Revision { get; }

    /// <summary>行容量。</summary>
    int Capacity { get; }

    /// <summary>因容量丢弃的旧行累计数。</summary>
    int DroppedCount { get; }

    /// <summary>自上次清空/替换以来是否丢过内容。</summary>
    bool HasTrimmed { get; }

    /// <summary>当前保留的全部行（副本；保存/标签用）。</summary>
    IReadOnlyList<string> Snapshot();

    /// <summary>从游标之后取窗口；超过 <paramref name="maxChars"/> 返回 Overflow，不复制行/后缀。</summary>
    /// <param name="cursor">上一拍游标。</param>
    /// <param name="sealedLines">接收新增封口行（调用方持有）。</param>
    /// <param name="maxChars">字符预算（&lt;=0 ⇒ 不限）。</param>
    TextWindow ReadWindow(TextCursor cursor, List<string> sealedLines, int maxChars = 0);

    /// <summary>读尾部窗口（呈现超预算时跳读）。</summary>
    /// <param name="maxLines">最多行数。</param>
    /// <param name="maxChars">最多字符数。</param>
    /// <param name="cursor">尾部游标。</param>
    IReadOnlyList<string> ReadTail(int maxLines, int maxChars, out TextCursor cursor);
}
