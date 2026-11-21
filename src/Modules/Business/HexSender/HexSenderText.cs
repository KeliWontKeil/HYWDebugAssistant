namespace HYWDebugAssistant.Modules.HexSender;

/// <summary>**HEX 发送器的文本表键**（表随模块包走）。</summary>
public static class HexSenderText
{
    public const string Title = "module.title";
    public const string Description = "module.description";
    public const string RoleSend = "view.roleSend";
    public const string Send = "view.send";
    public const string InputLabel = "view.inputLabel";
    public const string Continuous = "view.continuous";
    public const string SentTitle = "view.sentTitle";
    public const string SentUnit = "view.sentUnit";
    public const string StatusIntervalInvalid = "status.intervalInvalid";
    public const string StatusIntervalZeroHint = "status.intervalZeroHint";
    public const string StatusIntervalInvalidContinuous = "status.intervalInvalidContinuous";
    public const string StatusIntervalEmptyContinuous = "status.intervalEmptyContinuous";
    public const string StatusPageEmpty = "status.pageEmpty";
    public const string StatusPageEmptyContinuous = "status.pageEmptyContinuous";
    public const string StatusConvertError = "status.convertError";
    public const string StatusHexSegmentLength = "status.hex.segmentLength";
    public const string StatusHexNonHex = "status.hex.nonHex";
    public const string StatusUnknownCommand = "status.unknownCommand";
    public const string StatusSplitPackets = "status.splitPackets";

    /// <summary>发送按钮菜单：回车发送。</summary>
    public const string SubmitKeyEnter = "view.submitKey.enter";

    /// <summary>发送按钮菜单：Ctrl+Enter 发送。</summary>
    public const string SubmitKeyCtrlEnter = "view.submitKey.ctrlEnter";
}
