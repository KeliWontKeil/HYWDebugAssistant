namespace HYWDebugAssistant.Sdk.Access;

/// <summary>
/// 模块在注册钩子里声明的一条**外部接入**：模块内唯一的名字 + 标准标识。
/// </summary>
/// <remarks>
/// <para>
/// 与数据类型 / 口 / 角色同级：**声明期一次性**、运行期不重读。框架按 <see cref="StandardId"/>
/// 从 catalog 解析适配器与参数类型并创建会话，模块只通过 <see cref="IExternalAccess"/> 发起打开/关闭与读写。
/// </para>
/// <para>
/// <b>模块不接触适配器</b>：适配器、参数类型与第三方依赖都在框架侧；模块只声明"用哪个标准"。
/// </para>
/// </remarks>
/// <param name="Name">接入名（模块内唯一；界面与诊断用它）。</param>
/// <param name="StandardId">外部标准标识（见 <see cref="ExternalAccessStandardIds"/>）；框架据此解析适配器与参数类型。</param>
public sealed record ExternalAccessDeclaration(string Name, string StandardId)
{
    /// <summary>默认单会话单元上限（与既有 <c>RawBytes</c> 契约的字节区一致）。</summary>
    public const int DefaultUnitBytes = 1472;

    /// <summary>单会话单元上限（字节；超过即丢弃并只报一次）。</summary>
    public int MaxUnitBytes { get; init; } = DefaultUnitBytes;
}
