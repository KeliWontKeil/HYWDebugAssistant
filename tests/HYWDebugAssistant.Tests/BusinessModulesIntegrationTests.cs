using HYWDebugAssistant.Core.Api;
using HYWDebugAssistant.Core.Runtime;
using HYWDebugAssistant.Core.Services;
using Shouldly;
using Xunit;

namespace HYWDebugAssistant.Tests;

/// <summary>
/// **「整体测试单元」的端到端验收**：四个模块组成最基础的**整体测试单元**。
/// </summary>
/// <remarks>
/// <para>
/// 与模块级测试（`HYWDebugAssistant.Modules.Tests`）的分工：那里用测试上下文驱动模块的**单步入口**；
/// 这里**装入真实的模块包**（`artifacts/modules` 里构建产出的四个包）→ 走真实加载路径
/// （`Assembly.LoadFrom` + 清单校验）→ 建实例 → **建网** → 用**回读**模拟界面操作 → 断言**数据真的到了显示区**。
/// </para>
/// <para>
/// 这一条是整个第一批的"能不能用"判据：它同时覆盖 ① 四个包能被框架装入 ② 契约互通（同标识同形状）
/// ③ 建网成功 ④ 活动体真的在跑 ⑤ 显示缓冲里能看到内容（回读面）。
/// </para>
/// </remarks>
public class BusinessModulesIntegrationTests
{
    private const string TextSenderId = "hyw/text-sender/1.0.0";
    private const string HexSenderId = "hyw/hex-sender/1.0.0";
    private const string MonitorId = "hyw/data-monitor/1.0.0";
    private const string ConverterId = "hyw/hex-string-converter/1.0.0";

    /// <summary>四个真实模块包：装入 → 建网 → 发一条文本 → 显示区里能看到它。</summary>
    [Fact]
    public void 整体测试单元_四个真实模块包应能装入_连上_并端到端投递到显示区()
    {
        var staged = Path.Combine(FindRepoRoot(), "artifacts", "modules");
        Directory.Exists(staged).ShouldBeTrue($"模块包未归拢：{staged}（请先运行 ./build.ps1）");

        var viewState = new ViewStateStore();
        using var runtime = new ModuleRuntime(viewState: viewState);
        var services = new RuntimeServices(runtime);

        services.ScanModuleSources([staged]).ShouldBeGreaterThanOrEqualTo(
            4, "归拢目录下的包应被装入（四个基础模块：文本发送器 / HEX 发送器 / 数据显示器 / 转换器）");

        var catalog = services.GetModuleCatalog();
        catalog.Select(c => c.ModuleId).ShouldContain(TextSenderId);
        catalog.Select(c => c.ModuleId).ShouldContain(HexSenderId);
        catalog.Select(c => c.ModuleId).ShouldContain(MonitorId);
        catalog.Select(c => c.ModuleId).ShouldContain(ConverterId);

        // ① 四个实例（含**零口零角色**的转换器 —— 它不进拓扑，但必须能装入并活动）
        services.AddModule("t1", TextSenderId).Succeeded.ShouldBeTrue();
        services.AddModule("h1", HexSenderId).Succeeded.ShouldBeTrue();
        services.AddModule("m1", MonitorId).Succeeded.ShouldBeTrue();
        services.AddModule("c1", ConverterId).Succeeded.ShouldBeTrue();

        // ② 建网：给三个口**同一个标签** ⇒ 框架建出**一条多源网络**（多源是合法的一等组合）
        // —— 这也正是界面上的做法（改端口标签），比逐条 Connect 更贴近真实用法。
        SetLabel(runtime, "t1", "out", "bus");
        SetLabel(runtime, "h1", "out", "bus");
        SetLabel(runtime, "m1", "in", "bus");

        var network = services.GetNetworks().ShouldHaveSingleItem();
        network.Name.ShouldBe("bus");
        network.Sources.Count.ShouldBe(2, "两个发送器同属一条多源网络 ⇒ 显示器两条都收");

        // ③ 模拟界面操作：把"当前页 + 该页文本"写进视图状态（UI 就是这么写的），再点「发送」
        viewState.SetSelectedPage("t1", "text-pages", 0);
        viewState.SetInputText("t1", "text1", "Hello 整体测试单元");
        services.SendCommand("t1", "send").Succeeded.ShouldBeTrue();

        // ④ 数据真的到了显示器的**显示区**：通道只增 `AppendText`/`AppendLine`。
        // 读法与 UI 完全相同：显示缓冲由框架持有，模块经 `IViewDisplayAccess` 追加、UI 在刷新节拍里落地，
        // 而"回读"这条路径（模块保存用的就是它）会把**还没被 UI 取走的追加**一起叠进来 ⇒ 无 UI 也能读到。
        var shown = WaitForDisplayLines(viewState, "m1", "log", text => text.Contains("Hello", StringComparison.Ordinal));
        shown.ShouldContain(l => l.Contains("Hello", StringComparison.Ordinal),
            "文本发送器 → 数据显示器：**端到端**真的到了显示区（同一个 `RawBytes` ⇒ 开箱即连）");

        // ⑤ 十六进制发送器同样通（同一个标识 ⇒ 共用一条多源网络）
        viewState.SetInputText("h1", "hex1", "41 42 43");
        services.SendCommand("h1", "send").Succeeded.ShouldBeTrue();

        var hexShown = WaitForDisplayLines(viewState, "m1", "log", text => text.Contains("ABC", StringComparison.Ordinal));
        hexShown.ShouldContain(l => l.Contains("ABC", StringComparison.Ordinal));

        // ⑥ 转换器（零口）：**装进来了、活动体在跑、声明面齐** —— 它不进拓扑，所以"能不能用"
        //    在这一层能验的就是这些；**它的转发行为**（输入一变就转、失败标红）由模块级测试钉住。
        //    刻意**不在这里断言"结果里出现了 41 42"**：本用例读的是归拢目录里的包，
        //    而那个包可能是上一次构建的 —— 拿它判"模块内部怎么写的"会得到一个与源码无关的红。
        services.TryGetInstance("c1", out var converter).ShouldBeTrue();
        converter!.View.ShouldNotBeNull("转换器的界面声明必须随实例带出来（否则界面无从读它的结果区）");
        converter.View!.Sources.Select(s => s.Name).ShouldContain("hexResult");
    }


    /// <summary>
    /// **声明在容器里的输入也要能被提交**（框架缺陷回归网）。
    /// </summary>
    /// <remarks>
    /// 背景：`SubmitInput` 的校验**只看顶层元素**就会漏掉宣言在 `TabPages` 页内
    /// （四个基础模块正是这种形态：多页输入框）的输入一律被判"**模块未声明该输入**"而拒绝
    /// —— 现象 = 在页内输入框里回车**没有任何反应**。修法 = 校验前**展平**元素树。
    /// 本用例用一个"输入在容器里"的最小模块把这个坑钉住（与 G4 的 `FileSelector` 漏判同类）。
    /// </remarks>
    [Fact]
    public void 声明在容器里的输入_也应能被提交()
    {
        var runtime = new HYWDebugAssistant.Core.Runtime.ModuleRuntime();
        using var _ = runtime;
        var services = new RuntimeServices(runtime);

        var definition = new HYWDebugAssistant.Hosting.Registry.ModuleDefinition(
            HYWDebugAssistant.Sdk.ModuleId.Create("test", "nested-input", "1.0.0"),
            typeof(NestedInputModule).Name,
            string.Empty,
            [],
            HYWDebugAssistant.Sdk.ModuleCapability.None,
            [],
            null,
            true,
            typeof(NestedInputModule),
            NestedInputPackage,
            string.Empty);

        services.Registry.TryRegister(definition, out var failure).ShouldBeTrue(failure);
        services.AddModule("n1", "test/nested-input/1.0.0").Succeeded.ShouldBeTrue();

        // 输入声明在 `TabPages` 的页内 ⇒ 提交必须被接受（修复前这里返回"模块未声明该输入"）
        services.SubmitInput("n1", NestedInputModule.NestedSource, "回车提交")
            .Succeeded.ShouldBeTrue("页内输入与顶层输入一样，都是模块声明过的输入");

        // 未声明的名字仍然拒绝（修复不能把校验一起放开）
        services.SubmitInput("n1", "不存在", "x").Succeeded.ShouldBeFalse();
    }

    /// <summary>
    /// 桩模块的包目录：页内输入的声明**只在包内 `view.json`**（代码侧不再声明视图）。
    /// </summary>
    private static readonly string NestedInputPackage = TempViewPackage.Create(
        "business-nested-input",
        """
        {
          "version": 1,
          "sources": [ { "name": "nested1", "kind": "Text" } ],
          "views": [
            {
              "root": {
                "kind": "tabs",
                "children": [
                  {
                    "kind": "group",
                    "title": "第 1 页",
                    "children": [ { "kind": "textBox", "fontSize": 0, "textColor": "", "titleFontSize": 0, "titleColor": "", "title": "内容", "input": "nested1", "width": { "fill": true }, "height": { "pixels": 92 } } ]
                  }
                ]
              }
            }
          ]
        }
        """);

    /// <summary>测试夹具：输入声明在 `TabPages` 页内的最小模块。</summary>
    [HYWDebugAssistant.Sdk.ModuleDefinition("test", "nested-input", "1.0.0")]
    public sealed class NestedInputModule : HYWDebugAssistant.Sdk.Lifecycle.IModule
    {
        /// <summary>页内输入源名。</summary>
        public const string NestedSource = "nested1";

        /// <inheritdoc />
        public void OnRegistered(HYWDebugAssistant.Sdk.Context.IModuleContext context) =>
            ArgumentNullException.ThrowIfNull(context);
    }

    /// <summary>
    /// 显示器**清空**：模块请求 ⇒ 框架执行（模块不留副本 ⇒ 只能请求）。
    /// </summary>
    [Fact]
    public void 整体测试单元_显示器的清空应真的把显示缓冲清掉()
    {
        var staged = Path.Combine(FindRepoRoot(), "artifacts", "modules");

        var viewState = new ViewStateStore();
        using var runtime = new ModuleRuntime(viewState: viewState);
        var services = new RuntimeServices(runtime);

        _ = services.ScanModuleSources([staged]);
        services.AddModule("m1", MonitorId).Succeeded.ShouldBeTrue();

        // 直接往显示缓冲里写两行（模拟"已经显示过内容"）
        var buffer = viewState.Display("m1", "log");
        buffer.Append("第一行");
        buffer.Append("第二行");
        buffer.Count.ShouldBe(2);

        // 点「清空」⇒ 模块请求框架
        services.SendCommand("m1", "clear").Succeeded.ShouldBeTrue();

        // **UI 侧**在刷新节拍里取走并执行（这里用与 UI 相同的调用：TryTakeClear + Clear）
        var deadline = DateTime.UtcNow.AddSeconds(3);

        while (DateTime.UtcNow < deadline)
        {
            if (viewState.TryTakeClear("m1", out var source))
            {
                buffer = viewState.Display("m1", source!);
                buffer.Clear();
                break;
            }

            Thread.Sleep(20);
        }

        buffer.Count.ShouldBe(0, "清空是唯一会丢显示内容的入口");
        viewState.ReaderFor("m1").GetDisplayLines("log").ShouldBeEmpty();
    }

    /// <summary>改端口标签（与界面上的操作同一变更入口）。</summary>
    private static void SetLabel(ModuleRuntime runtime, string instanceId, string portName, string? label) =>
        runtime.Apply(HYWDebugAssistant.Core.Changes.ChangeSet.OfOne(
            new HYWDebugAssistant.Core.Changes.UpdatePortNetworkOperation(instanceId, portName, label))).Applied.ShouldBeTrue();

    /// <summary>
    /// 轮询等**显示区**里出现满足条件的文本（活动体在自己的线程上跑，有节拍延迟）。
    /// </summary>
    /// <remarks>
    /// 读法 = **模块保存时用的那一条**（`IViewStateReader.GetDisplayLines`）：框架侧的显示缓冲 + 还没被 UI
    /// 取走的追加，一起给快照 ⇒ 无 UI 的进程里也能读到"数据真的到了显示区"。
    /// </remarks>
    private static IReadOnlyList<string> WaitForDisplayLines(
        ViewStateStore viewState,
        string instanceId,
        string source,
        Func<string, bool> predicate)
    {
        var deadline = DateTime.UtcNow.AddSeconds(5);
        var reader = viewState.ReaderFor(instanceId);
        IReadOnlyList<string> lines = [];

        while (DateTime.UtcNow < deadline)
        {
            lines = reader.GetDisplayLines(source);

            if (lines.Any(predicate))
            {
                return lines;
            }

            Thread.Sleep(20);
        }

        return lines;
    }

    /// <summary>
    /// **「保存」这条路在真实运行时上必须走得通**：点按钮 → 模块请求对话框 → 选定路径经输入通道回来 → 落盘。
    /// </summary>
    /// <remarks>
    /// 这条走的是**真实装配顺序**（先挂文件请求面，再加模块），与外壳启动同序；
    /// 用来钉住"模块拿到的请求面是不是真的那一个" —— 拿成空实现的表现是"点了什么都没发生、也没有文件"，
    /// 而那**不会有任何报错**。
    /// </remarks>
    [Fact]
    public void 数据显示器的保存_在真实运行时上应当请求对话框并落盘()
    {
        var staged = Path.Combine(FindRepoRoot(), "artifacts", "modules");
        Directory.Exists(staged).ShouldBeTrue($"模块包未归拢：{staged}（请先运行 ./build.ps1）");

        var viewState = new ViewStateStore();
        using var runtime = new ModuleRuntime(viewState: viewState);
        var services = new RuntimeServices(runtime);

        // **与外壳同序**：请求面先挂上（外壳是在组合根建好之后挂的），再加模块。
        var recorder = new PickRecorder();
        runtime.FileAccessFor = _ => recorder;

        services.ScanModuleSources([staged]).ShouldBeGreaterThanOrEqualTo(4);
        services.AddModule("m1", MonitorId).Succeeded.ShouldBeTrue();

        // 先往显示区写点东西（保存读的是显示缓冲 ⇒ 落盘内容应当与屏幕一致）
        viewState.Display("m1", "log").AppendText("第一行");
        runtime.FileAccessFor!("m1").ShouldBeSameAs(recorder, "同一个实例问两次必须拿到同一个请求面");

        // 点「保存」：模块必须请求一次保存对话框，并说明它要哪条输出源、预填什么文件名
        services.SendCommand("m1", "save").Succeeded.ShouldBeTrue();

        var request = recorder.WaitForRequest();
        request.ShouldNotBeNull("点「保存」必须请求一次对话框；没有请求 = 模块拿到的请求面是个空实现");
        request!.Mode.ShouldBe(Sdk.Context.FilePickMode.Save);
        request.SourceName.ShouldBe("savePath");
        request.SuggestedFileName.ShouldBe("savetext1.txt");

        // 用户选定路径 ⇒ 框架把它经**输入通道**投回模块 ⇒ 模块当场写盘
        var path = Path.Combine(Path.GetTempPath(), $"hyw-monitor-e2e-{Guid.NewGuid():N}.txt");

        try
        {
            services.SubmitInput("m1", request.SourceName, path).Succeeded.ShouldBeTrue();

            var deadline = DateTime.UtcNow.AddSeconds(5);
            while (!File.Exists(path) && DateTime.UtcNow < deadline)
            {
                Thread.Sleep(20);
            }

            File.Exists(path).ShouldBeTrue("选完位置就该当场写盘 —— 屏幕上看到的与落盘的必须是同一份");
            File.ReadAllText(path).ShouldContain("第一行");
        }
        finally
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
    }

    /// <summary>记下模块发来的文件选择请求（真实运行时里的替身）。</summary>
    private sealed class PickRecorder : Sdk.Context.IFileAccess, IDisposable
    {
        private readonly ManualResetEventSlim _arrived = new(false);
        private Sdk.Context.FilePickRequest? _request;

        /// <inheritdoc />
        public void Request(Sdk.Context.FilePickRequest request)
        {
            _request = request;
            _arrived.Set();
        }

        /// <summary>等第一条请求（超时 ⇒ <see langword="null"/>）。</summary>
        /// <returns>请求；没等到 ⇒ <see langword="null"/>。</returns>
        public Sdk.Context.FilePickRequest? WaitForRequest()
        {
            _ = _arrived.Wait(TimeSpan.FromSeconds(5));
            return _request;
        }

        /// <inheritdoc />
        public void Dispose() => _arrived.Dispose();
    }

    /// <summary>仓库根（模块包归拢目录在它下面）。</summary>
    private static string FindRepoRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);

        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "HYWDebugAssistant.slnx")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new InvalidOperationException("找不到仓库根（HYWDebugAssistant.slnx）");
    }
}
