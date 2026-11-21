namespace HYWDebugAssistant.Modules.CommandSender;

/// <summary>本模块文本表键（只写一次，代码与文本表共用）。</summary>
public static class CommandSenderText
{
    /// <summary>模块标题键。</summary>
    public const string Title = "module.title";

    /// <summary>模块描述键。</summary>
    public const string Description = "module.description";

    /// <summary>角色名键。</summary>
    public const string RoleSend = "view.roleSend";

    /// <summary>新建按钮。</summary>
    public const string Add = "view.add";

    /// <summary>全部发送按钮。</summary>
    public const string SendAll = "view.sendAll";

    /// <summary>连续发送开关。</summary>
    public const string Continuous = "view.continuous";

    /// <summary>间隔单位。</summary>
    public const string IntervalUnit = "view.intervalUnit";

    /// <summary>导入按钮。</summary>
    public const string Import = "view.import";

    /// <summary>另存为按钮。</summary>
    public const string SaveAs = "view.saveAs";

    /// <summary>行内发送按钮。</summary>
    public const string Send = "view.send";

    /// <summary>已发送标题。</summary>
    public const string SentTitle = "view.sentTitle";

    /// <summary>已发送单位（包）。</summary>
    public const string SentUnit = "view.sentUnit";

    /// <summary>状态预览值。</summary>
    public const string StatusPreview = "view.statusPreview";

    /// <summary>默认标签。</summary>
    public const string DefaultLabel = "item.defaultLabel";

    /// <summary>文件过滤串。</summary>
    public const string FileFilter = "file.filter";

    /// <summary>就绪。</summary>
    public const string StatusReady = "status.ready";

    /// <summary>已发送（数据包数 / 逻辑条数）。</summary>
    public const string StatusSent = "status.sent";

    /// <summary>未勾选。</summary>
    public const string StatusNoSelection = "status.noSelection";

    /// <summary>非法条目。</summary>
    public const string StatusInvalidItem = "status.invalidItem";

    /// <summary>发送失败包数。</summary>
    public const string StatusSendFailed = "status.sendFailed";

    /// <summary>发送完成但部分包未送达。</summary>
    public const string StatusSentFailed = "status.sentFailed";

    /// <summary>HEX 转文本失败（解析）。</summary>
    public const string StatusConvertInvalidHex = "status.convertInvalidHex";

    /// <summary>HEX 转文本失败（非 UTF-8）。</summary>
    public const string StatusConvertNotUtf8 = "status.convertNotUtf8";

    /// <summary>间隔非法。</summary>
    public const string StatusIntervalInvalid = "status.intervalInvalid";

    /// <summary>连续发送时间隔非法。</summary>
    public const string StatusIntervalInvalidContinuous = "status.intervalInvalidContinuous";

    /// <summary>间隔为空。</summary>
    public const string StatusIntervalEmpty = "status.intervalEmpty";

    /// <summary>间隔为 0。</summary>
    public const string StatusIntervalZero = "status.intervalZero";

    /// <summary>已导入。</summary>
    public const string StatusImported = "status.imported";

    /// <summary>已导出。</summary>
    public const string StatusExported = "status.exported";

    /// <summary>导入失败。</summary>
    public const string StatusImportFailed = "status.importFailed";

    /// <summary>导入格式不匹配。</summary>
    public const string StatusImportFormat = "status.importFormat";

    /// <summary>导入版本不支持。</summary>
    public const string StatusImportVersion = "status.importVersion";

    /// <summary>导入条数超限。</summary>
    public const string StatusImportTooMany = "status.importTooMany";

    /// <summary>导入解析失败。</summary>
    public const string StatusImportParse = "status.importParse";

    /// <summary>导出失败。</summary>
    public const string StatusExportFailed = "status.exportFailed";

    /// <summary>达到槽位上限。</summary>
    public const string StatusMaxSlots = "status.maxSlots";

    /// <summary>未知命令。</summary>
    public const string StatusUnknownCommand = "status.unknownCommand";

    /// <summary>未知列表动作。</summary>
    public const string StatusUnknownAction = "status.unknownAction";

    /// <summary>连续发送已开始。</summary>
    public const string StatusContinuousStarted = "status.continuousStarted";

    /// <summary>连续发送已停止。</summary>
    public const string StatusContinuousStopped = "status.continuousStopped";

    /// <summary>HEX 段长度错误。</summary>
    public const string StatusHexSegmentLength = "status.hexSegmentLength";

    /// <summary>HEX 段含非十六进制字符。</summary>
    public const string StatusHexNonHex = "status.hexNonHex";
}
