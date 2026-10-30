using System.Reflection;
using HYWDebugAssistant.Access;
using HYWDebugAssistant.Modules.Serial;
using HYWDebugAssistant.Modules.TcpClient;
using HYWDebugAssistant.Modules.TcpServer;
using HYWDebugAssistant.Modules.Udp;
using HYWDebugAssistant.Sdk.Access;
using HYWDebugAssistant.Sdk.Configuration;
using HYWDebugAssistant.Sdk.Text;
using Shouldly;
using Xunit;

namespace HYWDebugAssistant.Modules.Tests;

/// <summary>
/// **配置文本的属主 = 声明该接入的模块**：参数类型留在框架侧，但标题 / 说明 / 分组 / 枚举选项键
/// 必须住在模块自己的表里，且不得再出现在参数类型所在程序集（Access）的表里。
/// </summary>
public sealed class ExternalAccessConfigTextOwnershipTests
{
    private static readonly BuiltInExternalAccessCatalog Catalog = new();

    /// <summary>模块 ↔ 它声明的外部标准。</summary>
    public static TheoryData<Type, string> Cases => new()
    {
        { typeof(SerialModule), ExternalAccessStandardIds.Serial },
        { typeof(TcpClientModule), ExternalAccessStandardIds.TcpClient },
        { typeof(TcpServerModule), ExternalAccessStandardIds.TcpServer },
        { typeof(UdpModule), ExternalAccessStandardIds.Udp },
    };

    [Theory]
    [MemberData(nameof(Cases))]
    public void 配置文本键_应由声明接入的模块解析(Type moduleType, string standardId)
    {
        Catalog.TryResolve(standardId, out var descriptor).ShouldBeTrue($"标准应已登记：{standardId}");

        var configType = descriptor.ParametersType;
        var owner = moduleType.Assembly;
        var moduleName = owner.GetName().Name;
        var typeOwnerName = configType.Assembly.GetName().Name;
        var checkedKeys = 0;

        foreach (var property in configType.GetProperties(BindingFlags.Public | BindingFlags.Instance))
        {
            if (property.GetCustomAttribute<ConfigurationFieldAttribute>() is not { } field)
            {
                continue;
            }

            foreach (var key in new[] { field.TitleKey, field.DescriptionKey, field.GroupKey })
            {
                if (string.IsNullOrWhiteSpace(key))
                {
                    continue;
                }

                TextCatalog.TryGet(owner, key, out var text).ShouldBeTrue(
                    $"{moduleType.Name} 的配置文本键「{key}」不在模块表 {moduleName} 里");
                text.ShouldNotBeNullOrWhiteSpace();

                TextCatalog.TryGet(configType.Assembly, key, out _).ShouldBeFalse(
                    $"配置文本键「{key}」不得再住在参数类型所在程序集 {typeOwnerName}");
                checkedKeys++;
            }

            var underlying = Nullable.GetUnderlyingType(property.PropertyType) ?? property.PropertyType;

            if (!underlying.IsEnum)
            {
                continue;
            }

            foreach (var name in Enum.GetNames(underlying))
            {
                if (underlying.GetField(name)?.GetCustomAttribute<ConfigurationChoiceAttribute>()
                    is not { TitleKey: { Length: > 0 } choiceKey })
                {
                    continue;
                }

                TextCatalog.TryGet(owner, choiceKey, out _).ShouldBeTrue(
                    $"{moduleType.Name} 的枚举选项文本键「{choiceKey}」不在模块表 {moduleName} 里");

                TextCatalog.TryGet(configType.Assembly, choiceKey, out _).ShouldBeFalse(
                    $"枚举选项文本键「{choiceKey}」不得再住在参数类型所在程序集 {typeOwnerName}");
                checkedKeys++;
            }
        }

        checkedKeys.ShouldBeGreaterThan(0, $"{moduleType.Name} 至少要有一条配置文本键");
    }
}
