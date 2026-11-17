namespace HYWDebugAssistant.Modules.TextSender;

/// <summary>**文本发送器的文本表键**（表随模块包走；键字面量集中在这里，声明/视图/C# 共用）。</summary>
public static class TextSenderText
{
    /// <summary>模块标题。</summary>
    public const string Title = "module.title";

    /// <summary>模块描述。</summary>
    public const string Description = "module.description";

    /// <summary>角色显示名（输出口）。</summary>
    public const string RoleSend = "view.roleSend";

    /// <summary>「发送」按钮 / 命令显示名。</summary>
    public const string Send = "view.send";

    /// <summary>间隔填错（未要求连续发送）。</summary>
    public const string StatusIntervalInvalid = "status.intervalInvalid";

    /// <summary>间隔填错（连续发送开着）。</summary>
    public const string StatusIntervalInvalidContinuous = "status.intervalInvalidContinuous";

    /// <summary>间隔为空（连续发送开着）。</summary>
    public const string StatusIntervalEmptyContinuous = "status.intervalEmptyContinuous";

    /// <summary>当前页为空（单次发送）。</summary>
    public const string StatusPageEmpty = "status.pageEmpty";

    /// <summary>当前页为空（连续发送开着）。</summary>
    public const string StatusPageEmptyContinuous = "status.pageEmptyContinuous";
    public const string StatusUnknownCommand = "status.unknownCommand";
    public const string StatusSplitPackets = "status.splitPackets";

    /// <summary>发送按钮菜单：回车发送。</summary>
    public const string SubmitKeyEnter = "view.submitKey.enter";

    /// <summary>发送按钮菜单：Ctrl+Enter 发送。</summary>
    public const string SubmitKeyCtrlEnter = "view.submitKey.ctrlEnter";

    /// <summary>换行开关组标签。</summary>
    public const string Newline = "view.newline";

    /// <summary>CR 开关标签。</summary>
    public const string NewlineCr = "view.newlineCr";

    /// <summary>LF 开关标签。</summary>
    public const string NewlineLf = "view.newlineLf";
}
