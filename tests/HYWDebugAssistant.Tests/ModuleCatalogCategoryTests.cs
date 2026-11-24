using Avalonia.Controls;
using HYWDebugAssistant.FrameworkUi.ViewModels;
using HYWDebugAssistant.FrameworkUi.Views;
using Shouldly;
using Xunit;

namespace HYWDebugAssistant.Tests;

/// <summary>模块清单的五类分类与「全部模块」排序顺序。</summary>
public class ModuleCatalogCategoryTests
{
    [Fact]
    public void 模块清单_分类顺序固定为全部加五类()
    {
        ModuleCategories.Definitions.Select(item => item.Key).ShouldBe(
            new[]
            {
                ModuleCategories.All,
                ModuleCategories.Port,
                ModuleCategories.Source,
                ModuleCategories.Terminal,
                ModuleCategories.Converter,
                ModuleCategories.Other,
            });
    }

    [Fact]
    public void 模块清单_规范标签与旧标签都归入对应分类()
    {
        ModuleCategories.FromTags(["port", "serial", "io"]).ShouldBe(ModuleCategories.Port);
        ModuleCategories.FromTags(["serial", "io"]).ShouldBe(ModuleCategories.Port);

        ModuleCategories.FromTags(["source", "text", "sender"]).ShouldBe(ModuleCategories.Source);
        ModuleCategories.FromTags(["waveform", "generator"]).ShouldBe(ModuleCategories.Source);

        ModuleCategories.FromTags(["terminal", "monitor", "display"]).ShouldBe(ModuleCategories.Terminal);
        ModuleCategories.FromTags(["waveform", "display"]).ShouldBe(ModuleCategories.Terminal);

        ModuleCategories.FromTags(["converter", "waveform", "protocol"]).ShouldBe(ModuleCategories.Converter);
        ModuleCategories.FromTags(["waveform", "protocol"]).ShouldBe(ModuleCategories.Converter);

        // HexStringConverter 是离线工具，不操作数据流：即使有 hex/tool 标签也归“其他”。
        ModuleCategories.FromTags(["other", "hex", "tool"]).ShouldBe(ModuleCategories.Other);
        ModuleCategories.FromTags(["hex", "tool"]).ShouldBe(ModuleCategories.Other);
        ModuleCategories.FromTags(null).ShouldBe(ModuleCategories.Other);
    }

}
