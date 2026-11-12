using HYWDebugAssistant.Sdk.Ports;
using Shouldly;
using Xunit;

namespace HYWDebugAssistant.Tests;

/// <summary>
/// **视图 XAML 端口声明读取**：字面量、`x:Static`、非法值、重复名。
/// </summary>
/// <remarks>
/// 这是格式/契约面（编译期看不见），不是呈现面；不写像素/布局断言。
/// </remarks>
public class PortSpecReaderTests
{
    [Fact]
    public void 应读取字面量与静态常量的端口声明()
    {
        using var workspace = new TempWorkspace();
        var file = workspace.Write("Views/Ports.axaml", """
            <ui:ModuleView xmlns="https://github.com/avaloniaui"
                           xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
                           xmlns:ui="using:HYWDebugAssistant.Sdk.Ui.Controls"
                           xmlns:local="using:HYWDebugAssistant.Tests">
              <ui:ModuleView.Ports>
                <ui:ModulePort Name="{x:Static local:PortSpecFixture.Rx}"
                               Direction="Send"
                               TypeId="{x:Static local:PortSpecFixture.TypeId}"
                               Edge="Bottom"
                               Offset="0.25" />
                <ui:ModulePort Name="tx"
                               Direction="Receive"
                               TypeId="RawBytes"
                               Edge="Top" />
              </ui:ModuleView.Ports>
            </ui:ModuleView>
            """);

        var result = PortSpecReader.ReadFile(file, typeof(PortSpecReaderTests).Assembly);

        result.Errors.ShouldBeEmpty();
        result.Specs.Count.ShouldBe(2);

        var rx = result.Specs[0];
        rx.Name.ShouldBe("rx");
        rx.Direction.ShouldBe(PortDirection.Send);
        rx.TypeId.ShouldBe("RawBytes");
        rx.Edge.ShouldBe(PortEdge.Bottom);
        rx.AnchorOffset.ShouldBe(0.25);

        var tx = result.Specs[1];
        tx.Name.ShouldBe("tx");
        tx.Direction.ShouldBe(PortDirection.Receive);
        tx.Edge.ShouldBe(PortEdge.Top);
    }

    [Fact]
    public void 非法偏移应报错而不是猜()
    {
        using var workspace = new TempWorkspace();
        var file = workspace.Write("Views/Ports.axaml", """
            <ui:ModuleView xmlns="https://github.com/avaloniaui"
                           xmlns:ui="using:HYWDebugAssistant.Sdk.Ui.Controls">
              <ui:ModuleView.Ports>
                <ui:ModulePort Name="rx" Direction="Send" TypeId="RawBytes" Offset="1.5" />
              </ui:ModuleView.Ports>
            </ui:ModuleView>
            """);

        var result = PortSpecReader.ReadFile(file, typeof(PortSpecReaderTests).Assembly);

        result.Specs.ShouldBeEmpty();
        result.Errors.ShouldHaveSingleItem().Code.ShouldBe(PortSpecErrorCode.OffsetInvalid);
    }

    [Fact]
    public void 缺类型标识应报错()
    {
        using var workspace = new TempWorkspace();
        var file = workspace.Write("Views/Ports.axaml", """
            <ui:ModuleView xmlns="https://github.com/avaloniaui"
                           xmlns:ui="using:HYWDebugAssistant.Sdk.Ui.Controls">
              <ui:ModuleView.Ports>
                <ui:ModulePort Name="rx" Direction="Send" />
              </ui:ModuleView.Ports>
            </ui:ModuleView>
            """);

        var result = PortSpecReader.ReadFile(file, typeof(PortSpecReaderTests).Assembly);

        result.Errors.ShouldHaveSingleItem().Code.ShouldBe(PortSpecErrorCode.TypeIdMissing);
    }

    [Fact]
    public void 跨文件重名应报错()
    {
        using var workspace = new TempWorkspace();
        workspace.Write("Views/A.axaml", PortFile("rx"));
        workspace.Write("Views/B.axaml", PortFile("rx"));

        var result = PortSpecReader.ReadDirectory(
            Path.Combine(workspace.Root, "Views"),
            typeof(PortSpecReaderTests).Assembly);

        result.Specs.ShouldHaveSingleItem();
        result.Errors.ShouldHaveSingleItem().Code.ShouldBe(PortSpecErrorCode.DuplicatePort);
    }

    private static string PortFile(string name) => $"""
        <ui:ModuleView xmlns="https://github.com/avaloniaui"
                       xmlns:ui="using:HYWDebugAssistant.Sdk.Ui.Controls">
          <ui:ModuleView.Ports>
            <ui:ModulePort Name="{name}" Direction="Send" TypeId="RawBytes" />
          </ui:ModuleView.Ports>
        </ui:ModuleView>
        """;

    /// <summary>临时目录夹具（测试结束即删）。</summary>
    private sealed class TempWorkspace : IDisposable
    {
        public TempWorkspace()
        {
            Root = Path.Combine(Path.GetTempPath(), "hyw-port-spec-" + Guid.NewGuid().ToString("N"));
            _ = Directory.CreateDirectory(Root);
        }

        public string Root { get; }

        public string Write(string relativePath, string content)
        {
            var path = Path.Combine(Root, relativePath);
            _ = Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, content);
            return path;
        }

        public void Dispose()
        {
            try
            {
                Directory.Delete(Root, recursive: true);
            }
            catch (IOException)
            {
            }
        }
    }
}

/// <summary>供 `x:Static` 解析的静态常量（测试程序集内）。</summary>
public static class PortSpecFixture
{
    public const string Rx = "rx";

    public const string TypeId = "RawBytes";
}
