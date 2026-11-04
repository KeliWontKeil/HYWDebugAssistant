namespace HYWDebugAssistant.Sdk.Views;

/// <summary>
/// **显示文本的增量读取游标**：视图记住"我已经读到哪里"，下一拍只取之后新增的部分。
/// </summary>
/// <remarks>
/// 一段显示文本由**已封口的行**与**尚未封口的当前行**组成：当前行会随追加不断变长，
/// 把它整行搬给视图等于每拍复制全文 ⇒ 游标把"行数 / 当前行世代 / 当前行长度"分开记，
/// 视图据此只取新增后缀。世代号在"当前行被替换或封口"时变化，用来区分
/// "接在后面"与"整行换掉"这两件不同的事。
/// </remarks>
/// <param name="SealedCount">已封口行的**绝对**条数（含已被丢弃的，用来判断游标是否落后于裁剪）。</param>
/// <param name="OpenGeneration">当前行世代号（封口 / 替换 / 头部裁剪都会变）。</param>
/// <param name="OpenLength">当前行已读到的字符数。</param>
/// <param name="Dropped">已丢弃的旧行累计数（变大 ⇒ 视图要同步删头）。</param>
/// <param name="OpenTrimGeneration">
/// 当前行**被头部裁剪**的世代号（只有"真的截掉了当前行的开头"才变）：
/// 封口也会让 <paramref name="OpenGeneration"/> 变，但那时视图手里的文本仍然是对的——
/// 靠这个号把"封口"与"内容被截"分开，避免每次封口都整行重排。
/// </param>
public readonly record struct DisplayCursor(
    int SealedCount,
    long OpenGeneration,
    int OpenLength,
    int Dropped,
    long OpenTrimGeneration = 0)
{
    /// <summary>空游标（从缓冲开头开始读）。</summary>
    public static readonly DisplayCursor Empty = new(0, 0, 0, 0);
}

/// <summary>
/// **一次增量读取的结果**：新增的已封口行 + 当前行的"后缀"或"整行"。
/// </summary>
/// <param name="SealedCount">读完之后已封口行的绝对条数。</param>
/// <param name="Dropped">读完之后已丢弃的旧行累计数。</param>
/// <param name="OpenGeneration">当前行世代号。</param>
/// <param name="OpenLength">当前行字符数。</param>
/// <param name="OpenSuffix">
/// 世代号未变 ⇒ 当前行的**新增后缀**（空串 = 没有新增）；世代号变了 ⇒ <see langword="null"/>
/// （此时看 <paramref name="OpenText"/>）。
/// </param>
/// <param name="OpenText">世代号变了 ⇒ 当前行的**整行文本**（可能为空串 = 当前行被清空）。</param>
/// <param name="Capacity">这块缓冲的行容量。</param>
/// <param name="Revision">缓冲变更号（同一个号 ⇒ 内容没变，可以什么都不做）。</param>
/// <param name="Reset">游标已失效（被整块替换 / 游标跑到缓冲之前）⇒ 调用方改用全量快照重建。</param>
/// <param name="Cursor">读完之后的新游标。</param>
/// <param name="OpenTrimGeneration">
/// 当前行被头部裁剪的世代号（见 <see cref="DisplayCursor.OpenTrimGeneration"/>）：
/// 与游标里的号相同 ⇒ 当前行只是封口/替换，视图手里的文本仍然有效，不必整行重排。
/// </param>
/// <param name="SealedFromStart">
/// **封口行从头重来**：视图游标落后于裁剪点（它手里的封口行全被丢掉了）。
/// 此时 <paramref name="SealedCount"/> 与 <c>sealedLines</c> 表示的是"当前全部封口行"，
/// 视图应整段替换而不是追加。它不是"内容坏了"：不需要退回全量快照
/// （那会在 LOH 上反复造整行字符串，长时间运行会把内存推高）。
/// </param>
public readonly record struct DisplayDelta(
    int SealedCount,
    int Dropped,
    long OpenGeneration,
    int OpenLength,
    string? OpenSuffix,
    string? OpenText,
    int Capacity,
    long Revision,
    bool Reset,
    DisplayCursor Cursor,
    long OpenTrimGeneration = 0,
    bool SealedFromStart = false)
{
    /// <summary>需要全量重建的增量（游标失效、没有可增量应用的内容）。</summary>
    public static DisplayDelta ResetRequired(DisplayCursor cursor, int capacity, long revision) =>
        new(cursor.SealedCount, cursor.Dropped, cursor.OpenGeneration, cursor.OpenLength,
            OpenSuffix: null, OpenText: null, capacity, revision, Reset: true, cursor,
            cursor.OpenTrimGeneration);
}
