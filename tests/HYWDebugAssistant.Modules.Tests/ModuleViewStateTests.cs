using HYWDebugAssistant.Sdk.Context;
using HYWDebugAssistant.Sdk.Testing;
using Shouldly;
using Xunit;

namespace HYWDebugAssistant.Modules.Tests;

/// <summary>
/// **模块作者视角的视图状态回读**。
/// </summary>
/// <remarks>
/// 模块在自己的测试里用 <see cref="TestModuleContext.ViewState"/> 预置"用户此刻在看/在编辑什么"，
/// 然后断言模块**读到的是同一份**（框架里这些值由 UI 写；测试里由用例写 —— 语义同一份）。
/// </remarks>
public class ModuleViewStateTests
{
    [Fact]
    public void 模块应能回读预置的选中页_输入文本与显示行()
    {
        var context = new TestModuleContext();
        IModuleContext moduleView = context;

        context.ViewState.SetSelectedPage("pages", 2);
        var typed = "用户此刻看到的内容";
        var logLines = new[] { "第一行", "第二行" };
        context.ViewState.SetInputText("text1", typed);
        context.ViewState.SetDisplayLines("log", logLines);

        moduleView.ViewState.GetSelectedPage("pages").ShouldBe(2);
        moduleView.ViewState.GetInputText("text1").ShouldBe(typed);
        moduleView.ViewState.GetDisplayLines("log").ShouldBe(logLines);

        // **未知一律 null / 空**（不抛异常、不猜 —— 与框架侧同一纪律）
        moduleView.ViewState.GetSelectedPage("nope").ShouldBeNull();
        moduleView.ViewState.GetInputText("nope").ShouldBeNull();
        moduleView.ViewState.GetDisplayLines("nope").ShouldBeEmpty();
    }

    [Fact]
    public void 未预置时_视图状态应为空而不是异常()
    {
        var context = new TestModuleContext();
        IModuleContext moduleView = context;

        moduleView.ViewState.GetSelectedPage("pages").ShouldBeNull();
        moduleView.ViewState.GetInputText("text1").ShouldBeNull();
        moduleView.ViewState.GetDisplayLines("log").ShouldBeEmpty();
    }
}
