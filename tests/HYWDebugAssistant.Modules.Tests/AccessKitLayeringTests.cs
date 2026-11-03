using System.Reflection;
using HYWDebugAssistant.Sdk.AccessKit;
using HYWDebugAssistant.Sdk.Contracts;
using Shouldly;
using Xunit;

namespace HYWDebugAssistant.Modules.Tests;

/// <summary>
/// **分层判据**：外部接入面只搬原始字节，数据契约 / 组帧 / 端口概念一律不得出现在它的公开面上。
/// </summary>
/// <remarks>
/// <para>
/// 这是 AR-25 的机器判据：模块与框架的外部接口是"怎么取数、怎么操作、状态怎么传"，
/// 数据单元的类型、数据包与组帧属于**模块间通信**，只能在模块自己的代码里出现。
/// </para>
/// <para>
/// 判据取"公开签名里是否出现 <c>HYWDebugAssistant.Sdk.Contracts</c> 的类型"：
/// 契约层类型一旦被接入通道引用，说明包级概念漏进了接入面。
/// </para>
/// </remarks>
public sealed class AccessKitLayeringTests
{
    [Fact]
    public void 外部接入通道_公开面不应出现数据契约类型()
    {
        var offenders = new List<string>();

        foreach (var type in typeof(ExternalAccessChannel).Assembly.GetExportedTypes())
        {
            foreach (var member in type.GetMembers(
                BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly))
            {
                foreach (var referenced in ReferencedTypesOf(member))
                {
                    if (IsDataContract(referenced))
                    {
                        offenders.Add($"{type.Name}.{member.Name} → {referenced.Name}");
                    }
                }
            }
        }

        offenders.ShouldBeEmpty(
            "AR-25：外部接入面只搬原始字节，不得出现数据类型 / 组帧 / 端口概念（" + string.Join("；", offenders) + "）");
    }

    /// <summary>是否属于**数据契约**词汇（<c>Sdk.Contracts</c> 命名空间）。</summary>
    /// <remarks>
    /// 按命名空间判定，不按程序集：接入快照与上下文服务面在同一个 <c>Sdk</c> 程序集里，
    /// 但它们是接入面本身的概念，不属于数据契约。
    /// </remarks>
    private static bool IsDataContract(Type type) =>
        type.Namespace is { } name
        && (name == "HYWDebugAssistant.Sdk.Contracts"
            || name.StartsWith("HYWDebugAssistant.Sdk.Contracts.", StringComparison.Ordinal));

    private static IEnumerable<Type> ReferencedTypesOf(MemberInfo member) => member switch
    {
        MethodInfo method => method.GetParameters().Select(parameter => parameter.ParameterType).Append(method.ReturnType),
        ConstructorInfo constructor => constructor.GetParameters().Select(parameter => parameter.ParameterType),
        PropertyInfo property => [property.PropertyType],
        FieldInfo field => [field.FieldType],
        EventInfo @event => [@event.EventHandlerType!],
        _ => [],
    };
}
