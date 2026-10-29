namespace HYWDebugAssistant.Sdk.Access;

/// <summary>
/// **外部标准目录**：按标准标识解析框架侧 descriptor。
/// </summary>
/// <remarks>
/// 只有组合根（<c>App</c>）构造并注入本目录；模块不接触它。未知标识 ⇒ <see cref="TryResolve"/> 返回
/// <see langword="false"/>，框架据此给出 Error 并忽略该接入（模块以未接线状态继续运行）。
/// </remarks>
public interface IExternalAccessCatalog
{
    /// <summary>全部已登记的标准（按登记顺序）。</summary>
    IReadOnlyList<ExternalAccessDescriptor> Standards { get; }

    /// <summary>按标准标识解析 descriptor。</summary>
    /// <param name="standardId">标准标识。</param>
    /// <param name="descriptor">解析结果（未登记时为空）。</param>
    /// <returns>是否已登记该标准。</returns>
    bool TryResolve(string standardId, out ExternalAccessDescriptor descriptor);
}
