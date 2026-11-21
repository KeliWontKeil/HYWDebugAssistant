using HYWDebugAssistant.Access.Parameters;
using HYWDebugAssistant.Core.Services;
using HYWDebugAssistant.Sdk.Configuration;
using Shouldly;
using Xunit;

namespace HYWDebugAssistant.Tests;

/// <summary>配置字段的写回约束：范围不合法时必须拒绝，且不改变旧配置。</summary>
public sealed class ConfigurationValidationTests
{
    [Fact]
    public void 配置数值超出范围时拒绝写回()
    {
        var snapshot = new Dictionary<string, object>
        {
            ["tcp-client"] = new TcpClientConfig { RemotePort = 60721 },
        };

        ConfigurationSchemaBuilder.TryCreateUpdated(
                snapshot,
                nameof(TcpClientConfig.RemotePort),
                "70000",
                out var updated,
                out var failure)
            .ShouldBeFalse();

        updated.ShouldBeNull();
        failure.ShouldNotBeNullOrWhiteSpace();
        ((TcpClientConfig)snapshot["tcp-client"]).RemotePort.ShouldBe(60721);
    }

    [Fact]
    public void 配置数值在范围内时接受写回()
    {
        var snapshot = new Dictionary<string, object>
        {
            ["tcp-client"] = new TcpClientConfig { RemotePort = 60721 },
        };

        ConfigurationSchemaBuilder.TryCreateUpdated(
                snapshot,
                nameof(TcpClientConfig.RemotePort),
                "5000",
                out var updated,
                out var failure)
            .ShouldBeTrue();

        failure.ShouldBeNull();
        ((TcpClientConfig)updated!).RemotePort.ShouldBe(5000);
    }

    [Fact]
    public void 动态选项_当前值不可用时_按provider决定是否保留()
    {
        Check(keepCurrent: false, shouldContainCurrent: false);
        Check(keepCurrent: true, shouldContainCurrent: true);
    }

    private static void Check(bool keepCurrent, bool shouldContainCurrent)
    {
        var snapshot = new Dictionary<string, object>
        {
            ["choice"] = new ChoiceConfig { KeepCurrent = keepCurrent },
        };

        var schema = ConfigurationSchemaBuilder.Build(
            "choice",
            snapshot,
            typeof(ChoiceConfig).Assembly);

        var field = schema.Fields.Single(item => string.Equals(item.Name, nameof(ChoiceConfig.Value), StringComparison.Ordinal));
        var values = field.Choices!.Select(choice => choice.Value).ToArray();

        values.ShouldContain("COM1");
        values.Contains("COM9", StringComparer.Ordinal).ShouldBe(shouldContainCurrent);
    }

    private sealed class ChoiceConfig : IConfigurationChoices
    {
        public bool KeepCurrent { get; init; }

        [ConfigurationField("Value")]
        public string Value { get; set; } = "COM9";

        public IReadOnlyList<string>? GetChoices(string fieldName) => ["COM1"];

        public bool KeepCurrentChoiceWhenUnavailable => KeepCurrent;
    }
}
