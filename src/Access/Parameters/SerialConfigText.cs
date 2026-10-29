namespace HYWDebugAssistant.Access.Parameters;

/// <summary>**串口参数文本键**（表属 Access；键集中在配置类型旁，标注只写键）。</summary>
public static class SerialConfigText
{
    public const string PortName = "config.serial.portName";
    public const string PortDescription = "config.serial.port.description";
    public const string BaudRate = "config.serial.baudRate";
    public const string DataBits = "config.serial.dataBits";
    public const string StopBits = "config.serial.stopBits";
    public const string Parity = "config.serial.parity";
    public const string GroupSerial = "config.group.serial";
    public const string ParityNone = "config.parity.none";
    public const string ParityOdd = "config.parity.odd";
    public const string ParityEven = "config.parity.even";
    public const string ParityMark = "config.parity.mark";
    public const string ParitySpace = "config.parity.space";
}
