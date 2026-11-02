using HYWDebugAssistant.Sdk;
using HYWDebugAssistant.Sdk.Access;
using HYWDebugAssistant.Sdk.AccessKit;

namespace HYWDebugAssistant.Samples.ExternalModule;

/// <summary>
/// **独立编译示例**：只声明一个 <c>serial</c> 标准接入，适配器与参数类型都由宿主按标准解析。
/// </summary>
/// <remarks>
/// 本模块不提供视图；它证明一个外部作者只靠三个 SDK NuGet 包即可声明接入并被打包、加载。
/// </remarks>
[ModuleDefinition(
    "sample",
    "serial-access",
    DisplayName = "示例串口接入",
    Description = "只声明 serial 标准接入的最小外部模块（独立编译示例）",
    Tags = ["sample", "serial"],
    Capabilities = ModuleCapability.SerialPort,
    ParticipatesInDataFlow = true,
    WindowWidth = 400,
    WindowHeight = 190,
    MinWidth = 400,
    MinHeight = 190)]
public sealed class SampleSerialModule : ExternalAccessModule
{
    /// <summary>接入名（模块内唯一）。</summary>
    public const string AccessName = "sample-serial";

    /// <summary>发送口（设备 → 框架）。</summary>
    public const string RxPort = "rx";

    /// <summary>接收口（框架 → 设备）。</summary>
    public const string TxPort = "tx";

    /// <summary>角色标识。</summary>
    public const string RoleId = "sample-serial";

    /// <summary>显示源：连接状态。</summary>
    public const string LinkSource = "link";

    /// <summary>显示源：一句话状态。</summary>
    public const string StateSource = "state";

    /// <summary>显示源：收字节数。</summary>
    public const string RxCountSource = "rxCount";

    /// <summary>显示源：发字节数。</summary>
    public const string TxCountSource = "txCount";

    /// <summary>显示源：丢弃字节数。</summary>
    public const string DropCountSource = "dropCount";

    /// <summary>命令：打开。</summary>
    public const string OpenCommand = "open";

    /// <summary>命令：关闭。</summary>
    public const string CloseCommand = "close";

    /// <summary>命令：清空计数。</summary>
    public const string ClearCountersCommand = "clear-counters";

    /// <inheritdoc />
    protected override AccessModuleSpec Spec { get; } = new(
        AccessName,
        ExternalAccessStandardIds.Serial,
        RxPort,
        TxPort,
        RoleId,
        "示例串口",
        LinkSource,
        StateSource,
        RxCountSource,
        TxCountSource,
        DropCountSource,
        OpenCommand,
        "打开",
        CloseCommand,
        "关闭",
        ClearCountersCommand,
        "清空计数");

    /// <inheritdoc />
    protected override string Describe(ExternalAccessSnapshot snapshot) => snapshot.State switch
    {
        ExternalAccessState.Open => "已打开",
        ExternalAccessState.Opening => "正在打开…",
        ExternalAccessState.Closing => "正在关闭…",
        ExternalAccessState.Faulted => snapshot.Failure is { } failure
            ? $"打开失败（{failure.Kind}）"
            : "打开失败",
        _ => snapshot.Suspended ? "实例已停用" : "未打开",
    };
}
