using System.Reflection;
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
/// 钉住的行为：属主解析顺序是 ① <c>IUriContext.BaseUri</c>（`DataTemplate` 里唯一能拿到属主的路径）
/// → ② 目标对象 → ③ 根对象 → ④ 兜底；框架程序集（`Avalonia.*`）**不查表**（查了只会得到一条假"目录缺失"），
/// 但四处都找不到时仍返回**可见缺失标记**，不返回空白。
/// 表来自 <see cref="TextCatalogTestRoot"/> 造的临时根（与产品同一套目录布局）。
/// </remarks>
[Collection(nameof(TextCatalogTests))]
public class TrExtensionProbeTests : IDisposable
{
    /// <summary>本测试程序集的 URI host（`avares://&lt;程序集简名&gt;/...` 的 host 就是程序集简名）。</summary>
    private static readonly string ProbeAssemblyName = typeof(TrExtensionProbeTests).Assembly.GetName().Name!;

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

        view.ResolvedText.Text.ShouldBe(_tables.Entries["probe.base.label"]);
        view.MissingText.Text.ShouldBe(TextCatalog.MissingValue("probe.absent.message"), "缺键必须露出键名，不能空白");
    }

    [Fact]
    public void 目标对象程序集有表时_应优先命中()
    {
        var services = new FakeXamlServices
        {
            TargetObject = new ProbeOwner(),   // 本程序集的对象 ⇒ `TargetObject` 这条路就能解析
            RootObject = new TextBlock(),      // 根对象没有表：命中只可能来自 TargetObject
        };

        new TrExtension("probe.base.label").ProvideValue(services).ShouldBe(_tables.Entries["probe.base.label"]);
    }

    [Fact]
    public void DataTemplate场景_目标与根都是框架控件时_应靠URI命中测试表()
    {
        // 复现启动期的事故形态：`DataTemplate` 里的目标是 `Button`，根对象也不是这份 XAML 的根，
        // 只有编译期给的 BaseUri 指向真正的属主。
        var services = new FakeXamlServices
        {
            TargetObject = new Button(),
            RootObject = new TextBlock(),
            BaseUri = new Uri($"avares://{ProbeAssemblyName}/Probes/TrExtensionProbeView.axaml"),
        };

        new TrExtension("probe.base.label").ProvideValue(services).ShouldBe(_tables.Entries["probe.base.label"]);
    }

    [Fact]
    public void 无URI且两处都取不到时_应返回可见缺失标记且不报框架表目录缺失()
    {
        var diagnostics = new List<string>();
        var previous = TextCatalog.Diagnostics;
        TextCatalog.Diagnostics = diagnostics.Add;

        try
        {
            var services = new FakeXamlServices
            {
                TargetObject = new TextBlock(),
                RootObject = new Button(),
            };

            new TrExtension("probe.base.label")
                .ProvideValue(services)
                .ShouldBe(TextCatalog.MissingValue("probe.base.label"));
        }
        finally
        {
            TextCatalog.Diagnostics = previous;
        }

        diagnostics.ShouldNotContain(
            message => message.Contains("Avalonia.Controls", StringComparison.Ordinal),
            "两处都取不到时只该给可见缺失标记，不该为框架程序集报假诊断");
    }

    /// <summary>探针用的"本程序集对象"（它的程序集就是探针表的属主）。</summary>
    private sealed class ProbeOwner
    {
    }

    /// <summary>只实现 `{loc:Tr}` 用到的三个 XAML 服务（成员按接口的**非空**契约实现）。</summary>
    private sealed class FakeXamlServices : IServiceProvider, IProvideValueTarget, IRootObjectProvider, IUriContext
    {
        public object TargetObject { get; init; } = new ProbeOwner();

        public object TargetProperty { get; init; } = new object();

        public object RootObject { get; init; } = new ProbeOwner();

        public object IntermediateRootObject => RootObject;

        /// <summary>编译期给出的 BaseUri；**没设**时连 `IUriContext` 服务都不提供（模拟没有 URI 的旧路径）。</summary>
        public Uri BaseUri { get; set; } = null!;

        public object? GetService(Type serviceType) =>
            serviceType == typeof(IProvideValueTarget) ? this
            : serviceType == typeof(IRootObjectProvider) ? this
            : serviceType == typeof(IUriContext) && BaseUri is not null ? this
            : null;
    }
}
