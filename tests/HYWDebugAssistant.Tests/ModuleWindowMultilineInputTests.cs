using HYWDebugAssistant.Core.Api;
using HYWDebugAssistant.Sdk.Views;
using HYWDebugAssistant.Ui.ViewModels;
using Shouldly;
using Xunit;

namespace HYWDebugAssistant.Tests;

/// <summary>
/// 【G2 · 2026-09】多行文本输入：`ViewElement.Multiline` + **应用级**提交键档位。
/// </summary>
/// <remarks>
/// <list type="bullet">
///   <item><b>用户裁决</b>："换行是一个极其常见的行为，且**不同用户习惯不一样**" ⇒ 提交键做成
///     **框架级可配置项**（设置键 `multilineSubmitKey`），模块只声明"我要多行"；</item>
///   <item><b>三档</b>：`CtrlEnter`（默认：回车换行 / Ctrl+Enter 提交 / **失焦不提交**）· `Enter`（回车提交 / Shift+Enter 换行）·
///     `None`（键盘都不提交）；</item>
///   <item><b>单行不变</b>：仍是"回车 / 失焦一次提交"（ADR-0040 D-5）——三档设置**只管多行**。</item>
/// </list>
/// </remarks>
public class ModuleWindowMultilineInputTests
{
    [Fact]
    public void 多行输入_默认档位应为回车换行加Ctrl回车提交_且失焦不提交()
    {
        var input = Create(multiline: true);

        input.IsMultiline.ShouldBeTrue();
        input.SubmitOnEnter.ShouldBeFalse("回车应当**换行**");
        input.SubmitOnCtrlEnter.ShouldBeTrue("Ctrl+Enter 才是提交");
        input.SubmitOnLostFocus.ShouldBeFalse("失焦**不**提交 —— 否则点模块按钮会先失焦提交一次、按钮再发一次 ⇒ 双发");
        input.EditorMinHeight.ShouldBe(TextInputElementViewModel.MultilineEditorMinHeight);
    }

    [Theory]
    [InlineData("Enter", true, false, true)]
    [InlineData("None", false, false, false)]
    [InlineData("CtrlEnter", false, true, false)]
    [InlineData("不认识的档位", false, true, false)]     // 非法值 ⇒ 回落 CtrlEnter（不能让"两种键都不提交"）
    [InlineData("", false, true, false)]
    public void 多行输入_各档位的提交策略(string key, bool submitOnEnter, bool submitOnCtrlEnter, bool submitOnLostFocus)
    {
        var input = Create(multiline: true, submitKey: key);

        input.SubmitOnEnter.ShouldBe(submitOnEnter, $"{key}：回车是否提交");
        input.SubmitOnCtrlEnter.ShouldBe(submitOnCtrlEnter, $"{key}：Ctrl+回车是否提交");
        input.SubmitOnLostFocus.ShouldBe(submitOnLostFocus, $"{key}：失焦是否提交");
    }

    [Fact]
    public void 单行输入_不受多行档位影响_仍是回车与失焦一次提交()
    {
        foreach (var key in new[] { "CtrlEnter", "Enter", "None" })
        {
            var input = Create(multiline: false, submitKey: key);

            input.IsMultiline.ShouldBeFalse();
            input.SubmitOnEnter.ShouldBeTrue($"{key}：单行回车必须仍是提交（ADR-0040 D-5）");
            input.SubmitOnLostFocus.ShouldBeTrue($"{key}：单行失焦必须仍是提交");
            input.EditorMinHeight.ShouldBe(0, "单行不设最小高度");
        }
    }

    [Fact]
    public void 多行提交键变更_应触发模块视图重建输入元素()
    {
        // 档位在**构造元素时定型** ⇒ 设置改了必须重建元素表才能生效（否则"改了没生效"）
        var vm = CreateModuleViewModel();
        var before = vm.Elements.OfType<TextInputElementViewModel>().ShouldHaveSingleItem();

        vm.MultilineSubmitKey = "Enter";

        var after = vm.Elements.OfType<TextInputElementViewModel>().ShouldHaveSingleItem();
        after.ShouldNotBeSameAs(before, "提交键变了 ⇒ 输入元素必须重建（旧对象仍持旧策略）");
        after.SubmitOnEnter.ShouldBeTrue("新档位 `Enter` ⇒ 回车提交");
    }

    // ---------------------------------------------------------------- 输入类型 / 对齐 / 单位（2026-09）

    [Fact]
    public void 数值输入_按声明解析出输入类型与对齐_单位来自数据源()
    {
        var element = new ViewElement(ViewElementKind.TextInput, "interval", "间隔")
        {
            InputKind = TextInputKind.Number,
            Alignment = ViewAlignment.Right,
        };

        var input = new TextInputElementViewModel(element, dataSource: new ViewDataSource("interval", unit: "ms"));

        input.IsNumeric.ShouldBeTrue("声明为数值输入 ⇒ 视图据此只放行数字写法");
        input.IsAlignRight.ShouldBeTrue("对齐由**模块声明**（不归框架限死）");
        input.IsAlignLeft.ShouldBeFalse();
        input.IsAlignCenter.ShouldBeFalse();
        input.Unit.ShouldBe("ms", "单位取**数据源声明**（框外标签用）—— 不是元素字段、也不做框内后缀");
    }

    [Fact]
    public void 普通文本输入_默认为文本且无对齐无单位()
    {
        var input = new TextInputElementViewModel(
            new ViewElement(ViewElementKind.TextInput, "text", "值"),
            dataSource: new ViewDataSource("text"));

        input.IsNumeric.ShouldBeFalse("默认 Text ⇒ 不限制字符（老模块行为不变）");
        input.IsAlignLeft.ShouldBeFalse("Default ⇒ 三类全 false ⇒ 落框架默认（左）");
        input.IsAlignRight.ShouldBeFalse();
        input.IsAlignCenter.ShouldBeFalse();
        input.Unit.ShouldBeNull("数据源未声明单位 ⇒ 整块标签不显示");
    }

    private static TextInputElementViewModel Create(bool multiline, string? submitKey = null) =>
        new(
            new ViewElement(ViewElementKind.TextInput, "text", "输入") { Multiline = multiline },
            submit: null,
            multilineSubmitKey: submitKey);
    /// <summary>造一个"只声明一个多行输入"的模块视图。</summary>
    private static ModuleViewViewModel CreateModuleViewModel()
    {
        var declaration = new ViewDeclaration(
            [new ViewElement(ViewElementKind.TextInput, "text", "输入") { Multiline = true }],
            [new ViewDataSource("text")]);

        var instance = new ModuleInstanceView(
            "multiline-test",
            "test/multiline/1.0.0",
            "多行输入测试模块",
            "Active",
            HasView: true,
            Ports: [],
            IsPlaceholder: false,
            View: declaration);

        return new ModuleViewViewModel(
            instance,
            layout: null,
            HYWDebugAssistant.Ui.Design.EmptyRuntime.Instance,
            HYWDebugAssistant.Ui.Design.EmptyRuntime.Instance,
            scale: 1.0,
            fallbackX: 0,
            fallbackY: 0);
    }
}
