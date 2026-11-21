namespace HYWDebugAssistant.Modules.CommandSender.Views;

/// <summary>视图键（XAML 只经 <c>{x:Static}</c> 引用）。</summary>
public static class CommandSenderViewKeys
{
    /// <summary>活动槽位顺序值。</summary>
    public const string Order = CommandSenderModule.OrderValue;

    /// <summary>勾选顺序值。</summary>
    public const string Selection = CommandSenderModule.SelectionValue;

    /// <summary>搜索值。</summary>
    public const string Search = CommandSenderModule.SearchValue;

    /// <summary>间隔值。</summary>
    public const string Interval = CommandSenderModule.IntervalValue;

    /// <summary>连续开关值。</summary>
    public const string Continuous = CommandSenderModule.ContinuousValue;

    /// <summary>已发送数据包数。</summary>
    public const string Sent = CommandSenderModule.SentSource;

    /// <summary>状态句。</summary>
    public const string Status = CommandSenderModule.StatusSource;

    /// <summary>新建命令。</summary>
    public const string Add = CommandSenderModule.AddCommand;

    /// <summary>全部发送命令。</summary>
    public const string SendAll = CommandSenderModule.SendAllCommand;

    /// <summary>导入命令。</summary>
    public const string Import = CommandSenderModule.ImportCommand;

    /// <summary>另存为命令。</summary>
    public const string SaveAs = CommandSenderModule.SaveAsCommand;

    /// <summary>新建按钮的加号图标。</summary>
    public const string IconAdd = "icon.command.add";

    /// <summary>行内删除按钮的垃圾桶图标。</summary>
    public const string IconTrash = "icon.command.trash";
}
