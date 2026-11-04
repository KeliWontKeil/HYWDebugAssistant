namespace HYWDebugAssistant.Sdk.Views;

/// <summary>
/// **显示文本的读取游标**：版本 + 已封口行的绝对位置 + 当前行长度/世代。
/// </summary>
/// <remarks>
/// 版本只在**整块替换/清空**时变；封口行用绝对索引（裁剪只前移起点，不重发旧行）；
/// 当前行用"世代"区分"接后缀"与"整行换掉"。
/// </remarks>
/// <param name="Version">内容版本（清空/替换时变）。</param>
/// <param name="Dropped">已丢弃的旧行累计数（绝对坐标）。</param>
/// <param name="SealedCount">已封口行的绝对条数（含已丢弃的）。</param>
/// <param name="OpenLength">当前行已读到的字符数。</param>
/// <param name="OpenVersion">当前行世代（封口/替换/头部裁剪时变）。</param>
public readonly record struct TextCursor(
    long Version,
    int Dropped,
    int SealedCount,
    int OpenLength,
    long OpenVersion)
{
    /// <summary>空游标（从缓冲开头开始读）。</summary>
    public static readonly TextCursor Empty = new(0, 0, 0, 0, 0);
}

/// <summary>
/// **一次窗口读取**：新增的已封口行 + 当前行的"后缀"或"整行"。
/// </summary>
/// <param name="Version">读完之后的内容版本。</param>
/// <param name="Dropped">读完之后已丢弃的旧行累计数。</param>
/// <param name="SealedCount">读完之后已封口行的绝对条数。</param>
/// <param name="OpenLength">读完之后当前行的字符数。</param>
/// <param name="OpenVersion">读完之后当前行的世代。</param>
/// <param name="Reset">
/// 游标已失效（整块替换 / 游标跑到缓冲之前）⇒ 调用方用本窗口的 <c>SealedLines</c> 与
/// <see cref="OpenText"/> 全量重建（不需要另取快照）。
/// </param>
/// <param name="OpenSuffix">当前行的新增后缀（世代未变时有效；空串 = 无新增）。</param>
/// <param name="OpenText">当前行的整行文本（世代变了 / 重置时有效）。</param>
/// <param name="Revision">缓冲变更号（同一个号 ⇒ 内容没变，可以什么都不做）。</param>
/// <param name="Overflow">本拍新增内容超过读取预算（未复制行/后缀；调用方改用尾读）。</param>
public readonly record struct TextWindow(
    long Version,
    int Dropped,
    int SealedCount,
    int OpenLength,
    long OpenVersion,
    bool Reset,
    string? OpenSuffix,
    string? OpenText,
    long Revision,
    bool Overflow = false)
{
    /// <summary>重置窗口（游标失效）：调用方用随附的行表与 <see cref="OpenText"/> 重建。</summary>
    /// <param name="cursor">新游标。</param>
    /// <param name="revision">缓冲变更号。</param>
    public static TextWindow ResetRequired(TextCursor cursor, long revision) =>
        new(cursor.Version, cursor.Dropped, cursor.SealedCount, cursor.OpenLength,
            cursor.OpenVersion, Reset: true, OpenSuffix: null, OpenText: string.Empty, revision);
}
