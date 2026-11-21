using HYWDebugAssistant.Core.Api;
using HYWDebugAssistant.Core.Runtime;
using HYWDebugAssistant.Core.Services;
using HYWDebugAssistant.Hosting.Registry;
using HYWDebugAssistant.Sdk;
using HYWDebugAssistant.Sdk.Lifecycle;
using HYWDebugAssistant.Sdk.Ui;
using HYWDebugAssistant.Sdk.Values;
using HYWDebugAssistant.Ui.ModuleViews;
using NSubstitute;
using Shouldly;
using Xunit;

namespace HYWDebugAssistant.Tests;

/// <summary>
/// 控件侧的值契约：XAML 初值播种、提交后可见、还原回到 XAML 初值。
/// </summary>
public class ValueContextTests
{
    [Fact]
    public void XAML值初值_应读取文本开关与编辑器初值且跳过显示面()
    {
        var path = Path.Combine(Path.GetTempPath(), "hyw-values-" + Guid.NewGuid().ToString("N") + ".axaml");

        try
        {
            File.WriteAllText(path, """
                <ui:ModuleView xmlns:ui="using:HYWDebugAssistant.Sdk.Ui.Controls"
                               xmlns:local="using:HYWDebugAssistant.Tests">
                  <ui:ModuleTextSurface Value="{x:Static local:ValueSpecTestConstants.Text}" Text="ab" />
                  <ui:ModuleToggle Value="{x:Static local:ValueSpecTestConstants.Flag}" IsChecked="True" />
                  <ui:ModuleCheckBox Value="{x:Static local:ValueSpecTestConstants.Check}" IsChecked="False" />
                  <ui:ModuleTextSurface Value="{x:Static local:ValueSpecTestConstants.Interval}" Text="500" />
                  <ui:ModuleTextSurface Mode="Display" Source="{x:Static local:ValueSpecTestConstants.Display}" />
                </ui:ModuleView>
                """);

            var result = ValueSpecReader.ReadFile(path, typeof(ValueContextTests).Assembly);

            result.Errors.ShouldBeEmpty();
            result.Specs.Select(spec => (spec.Name, spec.Value)).ShouldBe(
            [
                ("text", "ab"),
                ("flag", "true"),
                ("check", "false"),
                ("interval", "500"),
            ]);
        }
        finally
        {
            try
            {
                File.Delete(path);
            }
            catch (IOException)
            {
                // 清理失败不影响结论
            }
        }
    }

    [Fact]
    public void 值控件_外观寻址键应跟随值名()
    {
        var surface = new HYWDebugAssistant.Sdk.Ui.Controls.ModuleTextSurface { Value = "header" };

        ((HYWDebugAssistant.Sdk.Ui.Controls.IModuleElementInternal)surface).Source.ShouldBe("header");
    }

    [Fact]
    public void 枚举下拉_只读外观禁用且可恢复()
    {
        var select = new HYWDebugAssistant.Sdk.Ui.Controls.ModuleSelect { Value = "mode" };
        var element = (HYWDebugAssistant.Sdk.Ui.Controls.IModuleElementInternal)select;

        element.ApplyModuleAppearance(new HYWDebugAssistant.Sdk.Ui.ViewAppearance { ReadOnly = true });
        select.IsEnabled.ShouldBeFalse();

        element.ApplyModuleAppearance(HYWDebugAssistant.Sdk.Ui.ViewAppearance.None);
        select.IsEnabled.ShouldBeTrue();
    }

    [Fact]
    public void 配置字段_作为值可读可提交()
    {
        var store = new ViewStateStore();
        using var runtime = new ModuleRuntime(viewState: store);
        var services = new RuntimeServices(runtime);

        var definition = new ModuleDefinition(
            ModuleId.Create("test", "config-value"),
            nameof(ConfigValueModule),
            string.Empty,
            [],
            ModuleCapability.None,
            [],
            true,
            typeof(ConfigValueModule),
            packageDirectory: string.Empty,
            assemblyPath: string.Empty);

        services.Registry.TryRegister(definition, out var failure).ShouldBeTrue(failure);
        services.AddModule("c1", "test/config-value").Succeeded.ShouldBeTrue();

        var context = new ModuleViewContext("c1", services, services, store, () => "CtrlEnter");

        var initial = context.ReadValue(ConfigValueModule.Field);
        initial.IsKnown.ShouldBeTrue();
        initial.Value.ShouldBe("500");

        context.CommitValue(ConfigValueModule.Field, "750");
        context.ReadValue(ConfigValueModule.Field).Value.ShouldBe("750");
    }

    [Fact]
    public void 动态选项_不保留当前值时不应把旧值伪装成选项()
    {
        var store = new ViewStateStore();
        using var runtime = new ModuleRuntime(viewState: store);
        var services = new RuntimeServices(runtime);

        var definition = new ModuleDefinition(
            ModuleId.Create("test", "dynamic-choices"),
            nameof(DynamicChoiceModule),
            string.Empty,
            [],
            ModuleCapability.None,
            [],
            false,
            typeof(DynamicChoiceModule),
            packageDirectory: string.Empty,
            assemblyPath: string.Empty);

        services.Registry.TryRegister(definition, out var failure).ShouldBeTrue(failure);
        services.AddModule("d1", "test/dynamic-choices").Succeeded.ShouldBeTrue();

        services.TryGetConfigurationSchema("d1", out var schema).ShouldBeTrue();
        var field = schema!.Fields.ShouldHaveSingleItem();

        field.Value.ShouldBe("COM9");
        field.Choices.ShouldNotBeNull();
        field.Choices.ShouldNotContain(choice => choice.Value == "COM9");
    }

    [Fact]
    public void 值状态_XAML初值播种_提交后可见_还原回到XAML初值()
    {
        var store = new ViewStateStore();
        using var runtime = new ModuleRuntime(viewState: store);
        var services = new RuntimeServices(runtime);

        var definition = new ModuleDefinition(
            ModuleId.Create("test", "value-context"),
            nameof(CompositePersistenceTests.ValueModule),
            string.Empty,
            [],
            ModuleCapability.None,
            [],
            true,
            typeof(CompositePersistenceTests.ValueModule),
            packageDirectory: string.Empty,
            assemblyPath: string.Empty,
            valueInitials: [new ValueInitialSpec(CompositePersistenceTests.ValueModule.TextValue, "xaml-default")]);

        services.Registry.TryRegister(definition, out var failure).ShouldBeTrue(failure);
        services.AddModule("v1", "test/value-context").Succeeded.ShouldBeTrue();

        var context = new ModuleViewContext("v1", services, services, store, () => "CtrlEnter");

        // 新实例由 XAML 初值播种，不依赖 ValueDeclaration 的另一个默认值。
        context.ReadValue(CompositePersistenceTests.ValueModule.TextValue).Value.ShouldBe("xaml-default");

        context.RegisterViewDefault(CompositePersistenceTests.ValueModule.TextValue, "ignored-xaml-default");
        context.ReadValue(CompositePersistenceTests.ValueModule.TextValue).Value.ShouldBe("xaml-default");

        context.CommitValue(CompositePersistenceTests.ValueModule.TextValue, "typed");
        context.ReadValue(CompositePersistenceTests.ValueModule.TextValue).Value.ShouldBe("typed");
        store.GetInputText("v1", CompositePersistenceTests.ValueModule.TextValue).ShouldBe("typed");

        runtime.Content.TryGetInstance("v1", out var instance).ShouldBeTrue();
        instance!.Context.Values.ReportValidity(
            CompositePersistenceTests.ValueModule.TextValue,
            ValueValidity.Warning("bad"));
        context.ReadValue(CompositePersistenceTests.ValueModule.TextValue).Notice!.Message.ShouldBe("bad");
        context.ReadValue(CompositePersistenceTests.ValueModule.TextValue).IsInvalid.ShouldBeFalse();
        instance.Context.Values.ReadValidity(CompositePersistenceTests.ValueModule.TextValue)
            .Notice!.Message.ShouldBe("bad");

        services.ResetModuleConfiguration("v1").Succeeded.ShouldBeTrue();
        context.ReadValue(CompositePersistenceTests.ValueModule.TextValue).Value.ShouldBe("xaml-default");
        context.ReadValue(CompositePersistenceTests.ValueModule.TextValue).Notice.ShouldBeNull();
    }

    [Fact]
    public void 配置枚举_应带显示名并可按成员名提交()
    {
        var store = new ViewStateStore();
        using var runtime = new ModuleRuntime(viewState: store);
        var services = new RuntimeServices(runtime);

        var definition = new ModuleDefinition(
            ModuleId.Create("test", "enum-value"),
            nameof(EnumValueModule),
            string.Empty,
            [],
            ModuleCapability.None,
            [],
            true,
            typeof(EnumValueModule),
            packageDirectory: string.Empty,
            assemblyPath: string.Empty);

        services.Registry.TryRegister(definition, out var failure).ShouldBeTrue(failure);
        services.AddModule("e1", "test/enum-value").Succeeded.ShouldBeTrue();

        var context = new ModuleViewContext("e1", services, services, store, () => "CtrlEnter");
        var state = context.ReadValue(EnumValueModule.Field);

        state.IsKnown.ShouldBeTrue();
        state.Value.ShouldBe("First");
        state.Choices!.Select(choice => choice.Title).ShouldBe(["FirstTitle", "SecondTitle"]);

        context.CommitValue(EnumValueModule.Field, "Second");
        context.ReadValue(EnumValueModule.Field).Value.ShouldBe("Second");
    }

    [Fact]
    public void 配置字段预览_只投递输入_不写视图状态或配置()
    {
        var field = new ConfigFieldView("Host", "Host", ConfigFieldKind.Text, "127.0.0.1");
        var query = Substitute.For<IRuntimeQuery>();
        var command = Substitute.For<IRuntimeCommand>();
        var viewState = Substitute.For<IViewStateAccess>();

        var presenter = new ModuleValuePresenter(
            "p1",
            name => string.Equals(name, "Host", StringComparison.Ordinal) ? field : null,
            _ => ViewAppearance.None,
            query,
            command,
            viewState);

        presenter.PreviewValue("Host", "999.999.999.999");

        command.Received(1).SubmitInput("p1", "Host", "999.999.999.999");
        command.DidNotReceiveWithAnyArgs().UpdateConfigurationField(default!, default!, default!);
        viewState.DidNotReceiveWithAnyArgs().SetInputText(default!, default!, default!);
    }

    /// <summary>测试模块：一个配置字段，OnRegistered 读取以登记 Schema。</summary>
    public sealed class ConfigValueModule : IModule
    {
        public const string Field = nameof(ConfigValueConfig.Interval);

        public void OnRegistered(HYWDebugAssistant.Sdk.Context.IModuleContext context) =>
            _ = context.Configuration.Read<ConfigValueConfig>();
    }

    /// <summary>测试配置：一个数字字段。</summary>
    public sealed class ConfigValueConfig
    {
        [HYWDebugAssistant.Sdk.Configuration.ConfigurationField("Interval")]
        public int Interval { get; set; } = 500;
    }

    /// <summary>测试模块：一个枚举配置字段。</summary>
    public sealed class EnumValueModule : IModule
    {
        public const string Field = nameof(EnumValueConfig.Mode);

        public void OnRegistered(HYWDebugAssistant.Sdk.Context.IModuleContext context) =>
            _ = context.Configuration.Read<EnumValueConfig>();
    }

    /// <summary>测试枚举与配置。</summary>
    public enum TestMode
    {
        [HYWDebugAssistant.Sdk.Configuration.ConfigurationChoice("FirstTitle")]
        First = 0,

        [HYWDebugAssistant.Sdk.Configuration.ConfigurationChoice("SecondTitle")]
        Second = 1,
    }

    /// <summary>测试配置：一个枚举字段。</summary>
    public sealed class EnumValueConfig
    {
        [HYWDebugAssistant.Sdk.Configuration.ConfigurationField("Mode")]
        public TestMode Mode { get; set; } = TestMode.First;
    }

    /// <summary>测试模块：一个不保留当前值的动态选项字段。</summary>
    public sealed class DynamicChoiceModule : IModule
    {
        public void OnRegistered(HYWDebugAssistant.Sdk.Context.IModuleContext context) =>
            _ = context.Configuration.Read<DynamicChoiceConfig>();
    }

    /// <summary>测试配置：当前值不在动态选项里。</summary>
    public sealed class DynamicChoiceConfig : HYWDebugAssistant.Sdk.Configuration.IConfigurationChoices
    {
        [HYWDebugAssistant.Sdk.Configuration.ConfigurationField("Port")]
        public string Port { get; set; } = "COM9";

        public IReadOnlyList<string>? GetChoices(string fieldName) =>
            string.Equals(fieldName, nameof(Port), StringComparison.Ordinal)
                ? ["COM1", "COM2"]
                : null;

        public bool KeepCurrentChoiceWhenUnavailable => false;
    }

}

/// <summary>XAML 值初值测试用的静态常量。</summary>
public static class ValueSpecTestConstants
{
    public const string Text = "text";
    public const string Flag = "flag";
    public const string Check = "check";
    public const string Interval = "interval";
    public const string Display = "display";
}
