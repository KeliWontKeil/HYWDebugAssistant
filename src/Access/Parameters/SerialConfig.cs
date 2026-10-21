using System.IO.Ports;
using HYWDebugAssistant.Sdk.Configuration;

namespace HYWDebugAssistant.Access.Parameters;

/// <summary>
/// **串口参数**（随组合件保存的实例参数）。
/// </summary>
/// <remarks>
/// <para>
/// 字段标注只影响**配置表单怎么画**（标题 / 分组 / 说明 / 范围），不改任何运行期行为；
/// 枚举成员的显示名用 <see cref="ConfigurationChoiceAttribute"/> 标注 ——
/// **写回用的始终是成员名**，标注只决定给人看的字（<c>115200</c> 而不是 <c>Baud115200</c>）。
/// </para>
/// <para>
/// <b>端口号是动态下拉</b>（见 <see cref="GetChoices"/>）：可选值随插拔变化，写死在枚举里没有意义；
/// 一个设备都没插上时它退回普通文本框（手打端口名仍然可用）。
/// </para>
/// </remarks>
public sealed class SerialConfig : IConfigurationChoices
{
    /// <summary>端口号（如 <c>COM3</c>）。</summary>
    [ConfigurationField("端口号", Group = "串口参数", Description = "本机当前可用的串口；打开失败时也会在消息里列出")]
    public string PortName { get; set; } = string.Empty;

    /// <summary>波特率。</summary>
    [ConfigurationField("波特率", Group = "串口参数")]
    public SerialBaudRate BaudRate { get; set; } = SerialBaudRate.Baud115200;

    /// <summary>数据位。</summary>
    [ConfigurationField("数据位", Group = "串口参数")]
    public SerialDataBits DataBits { get; set; } = SerialDataBits.Eight;

    /// <summary>停止位。</summary>
    [ConfigurationField("停止位", Group = "串口参数")]
    public SerialStopBits StopBits { get; set; } = SerialStopBits.One;

    /// <summary>校验位。</summary>
    [ConfigurationField("校验位", Group = "串口参数")]
    public SerialParity Parity { get; set; } = SerialParity.None;

    /// <inheritdoc />
    /// <remarks>
    /// 只对<b>端口号</b>给动态选项；其余字段是固定枚举，走各自的成员表（返回 <see langword="null"/>）。
    /// 查询廉价、不抛（取不到就当作"现在一个都没有"）—— 它跑在界面线程上。
    /// </remarks>
    public IReadOnlyList<string>? GetChoices(string fieldName)
    {
        if (!string.Equals(fieldName, nameof(PortName), StringComparison.Ordinal))
        {
            return null;
        }

        try
        {
            return SerialPort.GetPortNames();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or PlatformNotSupportedException)
        {
            // 环境取不到就报"现在一个都没有"：**空表**（而不是 null）—— 端口号仍然是动态下拉。
            return [];
        }
    }
}

/// <summary>波特率（值即实际速率）。</summary>
public enum SerialBaudRate
{
    /// <summary>9600。</summary>
    [ConfigurationChoice("9600")]
    Baud9600 = 9600,

    /// <summary>19200。</summary>
    [ConfigurationChoice("19200")]
    Baud19200 = 19200,

    /// <summary>38400。</summary>
    [ConfigurationChoice("38400")]
    Baud38400 = 38400,

    /// <summary>57600。</summary>
    [ConfigurationChoice("57600")]
    Baud57600 = 57600,

    /// <summary>115200。</summary>
    [ConfigurationChoice("115200")]
    Baud115200 = 115200,

    /// <summary>230400。</summary>
    [ConfigurationChoice("230400")]
    Baud230400 = 230400,

    /// <summary>460800。</summary>
    [ConfigurationChoice("460800")]
    Baud460800 = 460800,

    /// <summary>921600。</summary>
    [ConfigurationChoice("921600")]
    Baud921600 = 921600,
}

/// <summary>数据位（值即位数）。</summary>
public enum SerialDataBits
{
    /// <summary>5 位。</summary>
    [ConfigurationChoice("5")]
    Five = 5,

    /// <summary>6 位。</summary>
    [ConfigurationChoice("6")]
    Six = 6,

    /// <summary>7 位。</summary>
    [ConfigurationChoice("7")]
    Seven = 7,

    /// <summary>8 位。</summary>
    [ConfigurationChoice("8")]
    Eight = 8,
}

/// <summary>停止位（值取 <c>System.IO.Ports.StopBits</c> 的口径：1 / 2 / 1.5）。</summary>
public enum SerialStopBits
{
    /// <summary>1 位。</summary>
    [ConfigurationChoice("1")]
    One = 1,

    /// <summary>2 位。</summary>
    [ConfigurationChoice("2")]
    Two = 2,

    /// <summary>1.5 位。</summary>
    [ConfigurationChoice("1.5")]
    OnePointFive = 3,
}

/// <summary>校验位。</summary>
public enum SerialParity
{
    /// <summary>无校验。</summary>
    [ConfigurationChoice("无")]
    None = 0,

    /// <summary>奇校验。</summary>
    [ConfigurationChoice("奇")]
    Odd = 1,

    /// <summary>偶校验。</summary>
    [ConfigurationChoice("偶")]
    Even = 2,

    /// <summary>标记校验。</summary>
    [ConfigurationChoice("标记")]
    Mark = 3,

    /// <summary>空格校验。</summary>
    [ConfigurationChoice("空格")]
    Space = 4,
}
