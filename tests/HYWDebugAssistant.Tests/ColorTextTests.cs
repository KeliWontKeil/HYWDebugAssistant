using Avalonia.Media;
using HYWDebugAssistant.Sdk.Ui;
using Shouldly;
using Xunit;

namespace HYWDebugAssistant.Tests;

/// <summary>颜色文本与 ARGB 打包保持一致。</summary>
public class ColorTextTests
{
    [Fact]
    public void 颜色转ARGB与解析互逆()
    {
        const uint argb = 0xFF12AB34;
        ColorText.TryParseArgb(ColorText.FormatArgb(argb), out var parsed).ShouldBeTrue();
        parsed.ShouldBe(argb);

        ColorText.ToArgb(Color.FromUInt32(argb)).ShouldBe(argb);
    }
}
