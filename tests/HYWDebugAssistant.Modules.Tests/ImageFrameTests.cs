using HYWDebugAssistant.Modules.ImageFrame;
using HYWDebugAssistant.Sdk.Lifecycle;
using HYWDebugAssistant.Sdk.Testing;
using Shouldly;
using Xunit;

namespace HYWDebugAssistant.Modules.Tests;

/// <summary>图片显示器只声明视图，不持有模块值/输入/端口。</summary>
public class ImageFrameTests
{
    [Fact]
    public void 注册_应零值零输入零端口()
    {
        var context = new TestModuleContext();
        var module = new ImageFrameModule();

        ((IModule)module).OnRegistered(context);

        context.DeclaredPorts.ShouldBeEmpty();
        context.DeclaredRoles.ShouldBeEmpty();
        context.DeclaredDataTypes.ShouldBeEmpty();
        context.DeclaredCommands.ShouldBeEmpty();
        context.DeclaredValues.ShouldBeEmpty();
        context.DeclaredInputs.ShouldBeEmpty();
    }
}
