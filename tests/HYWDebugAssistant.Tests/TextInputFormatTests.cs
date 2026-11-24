using HYWDebugAssistant.Sdk.Ui;
using HYWDebugAssistant.Sdk.Values;
using Shouldly;
using Xunit;

namespace HYWDebugAssistant.Tests;

/// <summary>文本准入格式：过滤只丢非法字符，不判结构/范围；合法值字符不被拒。</summary>
public class TextInputFormatTests
{
    [Theory]
    [InlineData(ValueTextFormat.Hex, "0a 0B\t\r\nFF", "0a 0B\t\r\nFF")]
    [InlineData(ValueTextFormat.Hex, "0x41", "041")]
    [InlineData(ValueTextFormat.Hex, "AB-CD", "ABCD")]
    [InlineData(ValueTextFormat.Hex, "中 A", " A")]
    [InlineData(ValueTextFormat.SignedWhole, "-12", "-12")]
    [InlineData(ValueTextFormat.SignedWhole, "1.5", "15")]
    [InlineData(ValueTextFormat.NonNegativeWhole, "-12", "12")]
    [InlineData(ValueTextFormat.PositiveWhole, "+5", "+5")]
    [InlineData(ValueTextFormat.PositiveWhole, "0", "0")]
    [InlineData(ValueTextFormat.Real, "-1.5e+3", "-1.5e+3")]
    [InlineData(ValueTextFormat.Real, "1,5", "15")]
    [InlineData(ValueTextFormat.Real, "NaN", "")]
    [InlineData(ValueTextFormat.IpAddress, "192.168.1.1", "192.168.1.1")]
    [InlineData(ValueTextFormat.IpAddress, "fe80::1%12", "fe80::1%12")]
    [InlineData(ValueTextFormat.IpAddress, "[::1]", "[::1]")]
    [InlineData(ValueTextFormat.IpAddress, " 192.168.1.1 ", "192.168.1.1")]
    [InlineData(ValueTextFormat.IpAddress, "localhost", "localhost")]
    [InlineData(ValueTextFormat.Text, "\\x0C 中\nA", "\\x0C 中\nA")]
    public void 过滤只保留格式允许的字符(ValueTextFormat format, string input, string expected) =>
        ValueTextFormats.Filter(format, input).ShouldBe(expected);

    [Fact]
    public void 纯文本不过滤且返回原引用()
    {
        const string text = "\\x0C\r\n中🙂";

        ValueTextFormats.Filter(ValueTextFormat.Text, text).ShouldBeSameAs(text);
        ValueTextFormats.AllowsAll(ValueTextFormat.Text, text).ShouldBeTrue();
    }

    [Fact]
    public void 合法解析文本的每个字符都应被对应格式允许()
    {
        ValueTextFormats.AllowsAll(ValueTextFormat.Hex, "0a 0B\t\r\nFF").ShouldBeTrue();
        ValueTextFormats.AllowsAll(ValueTextFormat.SignedWhole, " -12 ").ShouldBeTrue();
        ValueTextFormats.AllowsAll(ValueTextFormat.NonNegativeWhole, "+42").ShouldBeTrue();
        ValueTextFormats.AllowsAll(ValueTextFormat.Real, " -1.5e+3 ").ShouldBeTrue();
        ValueTextFormats.AllowsAll(ValueTextFormat.IpAddress, "fe80::1%12").ShouldBeTrue();
    }

}
