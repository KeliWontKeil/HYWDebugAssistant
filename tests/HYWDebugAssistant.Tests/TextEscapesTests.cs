using HYWDebugAssistant.Sdk.Text;
using Shouldly;
using Xunit;

namespace HYWDebugAssistant.Tests;

/// <summary>共享 C 字符串转义：发送器、转换器与输入面高亮只有这一套识别器。</summary>
public class TextEscapesTests
{
    [Theory]
    [InlineData(@"\x41\101\v\?\\", "AA\v?\\")]
    [InlineData(@"\x4G", "\u0004G")]
    [InlineData(@"\x41B", "AB")]
    [InlineData(@"\x3022343", "022343")]
    [InlineData(@"\x100", "\u00100")]
    [InlineData(@"\x0FF", "\u000FF")]
    [InlineData(@"\1011", "A1")]
    [InlineData(@"\377", "\u00FF")]
    [InlineData(@"\08", "\0" + "8")]
    [InlineData(@"\u0041", "A")]
    [InlineData(@"\U0001F600", "😀")]
    [InlineData(@"\\x41", @"\x41")]
    [InlineData(@"\a\b\f\n\r\t\v", "\a\b\f\n\r\t\v")]
    public void 合法转义应解码(string text, string expected)
    {
        TextEscapes.Normalize(text).ShouldBe(expected);
    }

    [Theory]
    [InlineData(@"\x")]
    [InlineData(@"\q")]
    [InlineData(@"\400")]
    [InlineData(@"\777")]
    [InlineData(@"\u041")]
    [InlineData(@"\uD800")]
    [InlineData(@"\U00110000")]
    public void 非法或不完整转义保持字面(string text)
    {
        TextEscapes.Normalize(text).ShouldBe(text);
    }

    [Fact]
    public void 兼容重载关闭时保持字面()
    {
        TextEscapes.Normalize(@"\x41", escape: false).ShouldBe(@"\x41");
        TextEscapes.Normalize(@"\x41", escape: true).ShouldBe("A");
    }

    [Fact]
    public void 高亮区间应与实际解析一致()
    {
        var text = @"A\x41 B\101\u0041\q";
        var spans = TextEscapes.FindEscapes(text);

        spans.Count.ShouldBe(3);
        text.Substring(spans[0].Start, spans[0].Length).ShouldBe(@"\x41");
        text.Substring(spans[1].Start, spans[1].Length).ShouldBe(@"\101");
        text.Substring(spans[2].Start, spans[2].Length).ShouldBe(@"\u0041");

        // `\x` 最多吃两位：`\x41B` 是 `A` + `B`，`\x3022343` 只把 `\x30` 画蓝。
        var afterEscape = TextEscapes.FindEscapes(@"\x41B");
        afterEscape.Count.ShouldBe(1);
        afterEscape[0].ShouldBe(new TextEscapeSpan(0, 4));

        var longDigits = TextEscapes.FindEscapes(@"\x3022343");
        longDigits.Count.ShouldBe(1);
        longDigits[0].Length.ShouldBe(4);

        // `\\` 是合法转义，后面的 `x41` 只是普通正文。
        var literal = TextEscapes.FindEscapes(@"\\x41");
        literal.Count.ShouldBe(1);
        literal[0].ShouldBe(new TextEscapeSpan(0, 2));
    }
}
