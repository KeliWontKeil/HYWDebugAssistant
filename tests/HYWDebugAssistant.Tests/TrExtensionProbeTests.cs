using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using HYWDebugAssistant.Sdk.Text;
using HYWDebugAssistant.Sdk.Ui.Controls;
using HYWDebugAssistant.Tests.Probes;
using Shouldly;
using Xunit;

namespace HYWDebugAssistant.Tests;

/// <summary>
/// **`{loc:Tr key}` 的探针**：编译期 XAML 里 <c>ProvideValue</c> 到底能不能定位到"表的属主程序集"。
/// </summary>
/// <remarks>
/// 钉住的行为：目标对象一般是**框架控件**（它所属的程序集里没有表）⇒ 必须回退到
/// <c>IRootObjectProvider</c> 的根对象；两条路径都不通时返回**可见缺失标记**，不返回空白。
/// 表来自 <see cref="TextCatalogTestRoot"/> 造的临时根（与产品同一套目录布局）。
/// </remarks>
[Collection(nameof(TextCatalogTests))]
public class TrExtensionProbeTests : IDisposable
{
    private readonly TextCatalogTestRoot _tables = TextCatalogTestRoot.CreateWithProbeTables();

    /// <inheritdoc />
    public void Dispose()
    {
        _tables.Dispose();
        GC.SuppressFinalize(this);
    }

    [Fact]
    public void 编译XAML_应能解析到属主程序集的表()
    {
        TextCatalog.Apply(TextLanguages.ZhHans);

        var view = new TrExtensionProbeView();

        view.ResolvedText.Text.ShouldBe("探针基础表");
        view.MissingText.Text.ShouldBe("⟦probe.absent.message⟧", "缺键必须露出键名，不能空白");
    }

    [Fact]
    public void 目标对象程序集有表时_应优先命中()
    {
        var services = new FakeXamlServices
        {
            TargetObject = new ProbeOwner(),   // 本程序集的对象 ⇒ `TargetObject` 这条路就能解析
            RootObject = new TextBlock(),      // 根对象没有表：命中只可能来自 TargetObject
        };

        new TrExtension("probe.base.label").ProvideValue(services).ShouldBe("探针基础表");
    }

    [Fact]
    public void 目标对象是框架控件时_应回退到根对象程序集()
    {
        var services = new FakeXamlServices
        {
            TargetObject = new TextBlock(),    // Avalonia.Controls 里没有表
            RootObject = new ProbeOwner(),     // 真正的属主程序集（这份 XAML 的根）
        };

        new TrExtension("probe.base.label").ProvideValue(services).ShouldBe("探针基础表");
    }

    [Fact]
    public void 两条路径都取不到时_应返回可见缺失标记()
    {
        var services = new FakeXamlServices
        {
            TargetObject = new TextBlock(),
            RootObject = new TextBlock(),
        };

        new TrExtension("probe.base.label")
            .ProvideValue(services)
            .ShouldBe("⟦probe.base.label⟧");
    }

    /// <summary>探针用的"本程序集对象"（它的程序集就是探针表的属主）。</summary>
    private sealed class ProbeOwner
    {
    }

    /// <summary>只实现 `{loc:Tr}` 用到的两个 XAML 服务（成员按接口的**非空**契约实现）。</summary>
    private sealed class FakeXamlServices : IServiceProvider, IProvideValueTarget, IRootObjectProvider
    {
        public object TargetObject { get; init; } = new ProbeOwner();

        public object TargetProperty { get; init; } = new object();

        public object RootObject { get; init; } = new ProbeOwner();

        public object IntermediateRootObject => RootObject;

        public object? GetService(Type serviceType) =>
            serviceType == typeof(IProvideValueTarget) ? this
            : serviceType == typeof(IRootObjectProvider) ? this
            : null;
    }
}
