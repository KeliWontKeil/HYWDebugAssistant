using HYWDebugAssistant.Core.Api;
using HYWDebugAssistant.Core.Runtime;
using HYWDebugAssistant.Core.Services;
using HYWDebugAssistant.Hosting.Registry;
using HYWDebugAssistant.Sdk;
using HYWDebugAssistant.Sdk.Configuration;
using HYWDebugAssistant.Sdk.Context;
using HYWDebugAssistant.Sdk.Lifecycle;
using Shouldly;
using Xunit;

namespace HYWDebugAssistant.Tests;

/// <summary>
/// **配置项的动态可选项**（<see cref="IConfigurationChoices"/>）：值随环境变的字段（如可用串口）
/// 应当变成下拉，且**当前值必须留在选项里**。
/// </summary>
/// <remarks>
/// 这一条是**机制级判据**：没有它，"可用端口下拉"只能靠"这台机器上恰好插了串口"来验证 ——
/// 而那样在 CI / 空机器上等于没验。所以这里用一个自带选项表的桩模块把规则钉住。
/// </remarks>
public class ConfigurationChoicesTests
{
    [Fact]
    public void 动态可选项应变成下拉_且当前值留在选项里()
    {
        using var runtime = new ModuleRuntime();
        var services = new RuntimeServices(runtime);

        Register(services, "configurable", typeof(ConfigurableModule));
        services.AddModule("c1", "test/configurable/1.0.0").Succeeded.ShouldBeTrue();

        services.TryGetConfigurationSchema("c1", out var schema).ShouldBeTrue();
        schema.ShouldNotBeNull();

        var probe = schema!.Fields.Single(field => field.Name == nameof(ConfigurableConfig.Probe));

        // ① 字符串字段缺省是文本框；给了动态选项 ⇒ 变成下拉
        probe.Kind.ShouldBe(ConfigurationFieldKind.Enum);
        probe.Choices.ShouldNotBeNull();

        // ② 当前值（"COM9"）不在"现在可用"的表里 ⇒ 排在最前，不能显示成列表里的第一项
        probe.Choices!.Select(choice => choice.Value).ShouldBe(["COM9", "COM3", "COM7"]);
        probe.Value.ShouldBe("COM9");

        // ③ 写回用的就是字面量（动态选项没有"成员名"这一层）
        services.UpdateConfigurationField("c1", nameof(ConfigurableConfig.Probe), "COM7").Succeeded.ShouldBeTrue();
        services.TryGetConfigurationSchema("c1", out var updated).ShouldBeTrue();
        updated!.Fields.Single(field => field.Name == nameof(ConfigurableConfig.Probe)).Value.ShouldBe("COM7");

        // ④ 没有动态选项的字段不受影响：还是文本框（机制只对"给得出表"的字段生效）
        updated.Fields.Single(field => field.Name == nameof(ConfigurableConfig.Plain)).Kind
            .ShouldBe(ConfigurationFieldKind.Text);

        // ⑤ **空表 ≠ 不是动态字段**：设备一个都没插上时它仍是下拉，只是选项里只剩已存的那个值
        var empty = updated.Fields.Single(field => field.Name == nameof(ConfigurableConfig.Empty));
        empty.Kind.ShouldBe(ConfigurationFieldKind.Enum);
        empty.Choices!.Select(choice => choice.Value).ShouldBe(["old"]);
    }

    private static void Register(RuntimeServices services, string name, Type entryType)
    {
        var definition = new ModuleDefinition(
            ModuleId.Create("test", name, "1.0.0"),
            entryType.Name,
            string.Empty,
            [],
            ModuleCapability.None,
            [],
            null,
            true,
            entryType,
            TestViewPackages.For(entryType),
            string.Empty);

        services.Registry.TryRegister(definition, out var failure).ShouldBeTrue(failure);
    }

    /// <summary>只有一个配置类型的桩：读一次即登记（与真实模块同一条路径）。</summary>
    [ModuleDefinition("test", "configurable", "1.0.0")]
    public sealed class ConfigurableModule : IModule
    {
        /// <inheritdoc />
        public void OnRegistered(IModuleContext context) => _ = context.Configuration.Read<ConfigurableConfig>();
    }

    /// <summary>桩配置：一个字段给动态选项，另一个不给。</summary>
    public sealed class ConfigurableConfig : IConfigurationChoices
    {
        /// <summary>动态选项字段（初值故意不在"可用"表里）。</summary>
        public string Probe { get; set; } = "COM9";

        /// <summary>普通字段（应当仍是文本框）。</summary>
        public string Plain { get; set; } = "x";

        /// <summary>动态字段，但"现在一个都没有"（应当仍是下拉）。</summary>
        public string Empty { get; set; } = "old";

        /// <inheritdoc />
        public IReadOnlyList<string>? GetChoices(string fieldName) => fieldName switch
        {
            nameof(Probe) => ["COM3", "COM7"],
            nameof(Empty) => [],
            _ => null,
        };
    }
}
