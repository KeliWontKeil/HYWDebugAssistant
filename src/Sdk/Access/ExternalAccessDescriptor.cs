namespace HYWDebugAssistant.Sdk.Access;

/// <summary>
/// **一个外部标准的框架侧描述**：标识、呈现名、参数类型与适配件工厂。
/// </summary>
/// <remarks>
/// <para>
/// descriptor 由框架的 catalog 持有；模块只声明标准标识，运行期由框架按 descriptor 创建适配器、
/// 登记参数类型并持有会话。
/// </para>
/// <para>
/// <see cref="CreateAdapter"/> **每次调用都返回新实例**：同一标准的多个接入/多实例会话各自独立。
/// </para>
/// </remarks>
/// <param name="StandardId">标准标识（见 <see cref="ExternalAccessStandardIds"/>）。</param>
/// <param name="DisplayName">呈现名（诊断 / 界面用）。</param>
/// <param name="ParametersType">参数类型（配置表单与持久化复用）。</param>
/// <param name="CreateAdapter">适配件工厂（每次新建）。</param>
public sealed record ExternalAccessDescriptor(
    string StandardId,
    string DisplayName,
    Type ParametersType,
    Func<IExternalAccessAdapter> CreateAdapter);
