using System.Reflection;
using HYWDebugAssistant.Modules.Serial;
using HYWDebugAssistant.Modules.TcpClient;
using HYWDebugAssistant.Modules.TcpServer;
using HYWDebugAssistant.Modules.Udp;
using HYWDebugAssistant.Sdk;
using HYWDebugAssistant.Sdk.Lifecycle;
using HYWDebugAssistant.Sdk.Testing;
using HYWDebugAssistant.Sdk.Text;
using Shouldly;
using Xunit;

namespace HYWDebugAssistant.Modules.Tests;

/// <summary>
/// **模块文本的归属**：外部接入四模块的命令标题与身份文本必须来自**模块自己的表**。
/// </summary>
/// <remarks>
/// 外部接入骨架（`Sdk.AccessKit`）只搬数据、不写文案；按钮/命令标题、模块标题与说明由模块提供。
/// 视图里的同一文案走 `{loc:Tr}`，命令标题走模块常量解析——两条路都指向同一张表，这里钉住 C# 这一路。
/// </remarks>
public class ModuleTextOwnershipTests
{
    /// <summary>外部接入四模块。</summary>
    public static TheoryData<Type> AccessModules => new()
    {
        typeof(SerialModule),
        typeof(TcpClientModule),
        typeof(TcpServerModule),
        typeof(UdpModule),
    };

    [Theory]
    [MemberData(nameof(AccessModules))]
    public void 外部接入模块_命令与身份文本来自自己的表(Type moduleType)
    {
        var owner = moduleType.Assembly;

        var context = new TestModuleContext();
        var module = (IModule)Activator.CreateInstance(moduleType)!;
        module.OnRegistered(context);

        TextCatalog.TryGet(owner, "view.open", out var openTitle).ShouldBeTrue(
            $"{moduleType.Name} 的「打开」文案不在自己的表里");
        TextCatalog.TryGet(owner, "view.close", out var closeTitle).ShouldBeTrue(
            $"{moduleType.Name} 的「关闭」文案不在自己的表里");

        context.DeclaredCommands.ShouldContain(
            command => command.Name == "open" && command.Title == openTitle,
            $"{moduleType.Name} 的 open 命令标题必须取自己的表（不是硬编码）");
        context.DeclaredCommands.ShouldContain(
            command => command.Name == "close" && command.Title == closeTitle,
            $"{moduleType.Name} 的 close 命令标题必须取自己的表（不是硬编码）");

        TextCatalog.TryGet(owner, "view.clearCounters", out var clearCountersTitle).ShouldBeTrue(
            $"{moduleType.Name} 的「清空计数」文案不在自己的表里");

        var clearCountersCommand = moduleType
            .GetField("ClearCountersCommand", BindingFlags.Public | BindingFlags.Static)
            ?.GetValue(null) as string;

        clearCountersCommand.ShouldNotBeNullOrWhiteSpace(
            $"{moduleType.Name} 必须提供清空计数命令名");

        context.DeclaredCommands.ShouldContain(
            command => command.Name == clearCountersCommand && command.Title == clearCountersTitle,
            $"{moduleType.Name} 的清空计数命令标题必须取自己的表（不是硬编码）");

        var definition = moduleType.GetCustomAttribute<ModuleDefinitionAttribute>();
        definition.ShouldNotBeNull();

        TextCatalog.TryGet(owner, definition!.DisplayNameKey, out var displayName).ShouldBeTrue(
            $"{moduleType.Name} 的模块标题不在自己的表里");
        displayName.ShouldNotBeNullOrWhiteSpace();

        TextCatalog.TryGet(owner, definition.DescriptionKey, out _).ShouldBeTrue(
            $"{moduleType.Name} 的模块说明不在自己的表里");
    }
}
