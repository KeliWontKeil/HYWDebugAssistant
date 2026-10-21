namespace HYWDebugAssistant.Sdk.Access;

/// <summary>
/// 模块在注册钩子里声明的一条**外部接入**：模块内唯一的名字 + 适配件。
/// </summary>
/// <remarks>
/// <para>
/// 与数据类型 / 口 / 角色同级：**声明期一次性**、运行期不重读。框架据它创建会话并登记参数类型，
/// 模块只通过 <see cref="IExternalAccess"/> 发起打开/关闭与读写。
/// </para>
/// <para>
/// <b>适配件实例由模块在此交出**一行**</b>；之后框架独占驱动（框架不加载第二类件、不按身份解析）。
/// </para>
/// </remarks>
/// <param name="Name">接入名（模块内唯一；界面与诊断用它）。</param>
/// <param name="Adapter">适配件实例（其 <see cref="IExternalAccessAdapter.ParametersType"/> 即参数类型）。</param>
public sealed record ExternalAccessDeclaration(string Name, IExternalAccessAdapter Adapter)
{
    /// <summary>默认单会话单元上限（与既有 <c>RawBytes</c> 契约的字节区一致）。</summary>
    public const int DefaultUnitBytes = 1472;

    /// <summary>参数类型（由适配件给出）。</summary>
    public Type ParametersType => Adapter.ParametersType;

    /// <summary>单会话单元上限（字节；超过即丢弃并只报一次）。</summary>
    public int MaxUnitBytes { get; init; } = DefaultUnitBytes;
}
