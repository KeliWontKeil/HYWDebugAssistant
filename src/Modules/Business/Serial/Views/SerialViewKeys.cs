namespace HYWDebugAssistant.Modules.Serial.Views;

/// <summary>
/// **串口视图的键**：XAML 只经 <c>{x:Static}</c> 引用它们，不写字符串字面量。
/// </summary>
/// <remarks>
/// 显示源 / 命令来自模块常量；配置字段用 <c>nameof</c> 锁到配置类型上 ——
/// 改名时先断编译，而不是等运行期界面空白或提交被拒。
/// </remarks>
public static class SerialViewKeys
{
    public const string Link = SerialModule.LinkSource;
    public const string State = SerialModule.StateSource;
    public const string RxCount = SerialModule.RxCountSource;
    public const string TxCount = SerialModule.TxCountSource;
    public const string DropCount = SerialModule.DropCountSource;
    public const string Open = SerialModule.OpenCommand;
    public const string Close = SerialModule.CloseCommand;

    public const string PortName = nameof(SerialConfig.PortName);
    public const string BaudRate = nameof(SerialConfig.BaudRate);
    public const string DataBits = nameof(SerialConfig.DataBits);
    public const string StopBits = nameof(SerialConfig.StopBits);
    public const string Parity = nameof(SerialConfig.Parity);
}
