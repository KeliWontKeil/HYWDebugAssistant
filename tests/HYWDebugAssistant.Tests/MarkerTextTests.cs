using HYWDebugAssistant.Sdk.Ui.Controls;
using Shouldly;
using Xunit;

namespace HYWDebugAssistant.Tests;

/// <summary>不可显示字符标记的纯解析：只认成对的 `⟨…⟩`。</summary>
public class MarkerTextTests
{
    [Fact]
    public void 应找出单个与多个标记段()
    {
        MarkerText.FindSpans("A⟨0C⟩B⟨C2 85⟩C").ShouldBe(
        [
            new MarkerSpan(1, 4),
            new MarkerSpan(6, 7),
        ]);
    }

    [Fact]
    public void 未闭合标记不吞后续内容_普通尖括号不算标记()
    {
        MarkerText.FindSpans("A⟨0C").ShouldBeEmpty();
        MarkerText.FindSpans("A<0C>B").ShouldBeEmpty();
        MarkerText.FindSpans("plain").ShouldBeEmpty();
    }
}
