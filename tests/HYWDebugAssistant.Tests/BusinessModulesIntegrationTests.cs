using HYWDebugAssistant.Access;
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
/// 这里**装入真实的模块包**（按构建配置归拢的包；本用例用四个基础模块）→ 走真实加载路径
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

    private static readonly string Configuration =
        AppContext.BaseDirectory.Contains(
            $"{Path.DirectorySeparatorChar}Release{Path.DirectorySeparatorChar}",
            StringComparison.OrdinalIgnoreCase)
            ? "Release"
            : "Debug";

    /// <summary>模块包归拢目录（按构建配置）。</summary>
    private static string StagedModules => Path.Combine(FindRepoRoot(), "artifacts", "modules", Configuration);

    /// <summary>四个真实模块包：装入 → 建网 → 发一条文本 → 显示区里能看到它。</summary>
    [Fact]
    public void 整体测试单元_四个真实模块包应能装入_连上_并端到端投递到显示区()
    {
        var staged = StagedModules;
        Directory.Exists(staged).ShouldBeTrue($"模块包未归拢：{staged}（请先运行 ./build.ps1）");

        var viewState = new ViewStateStore();
        using var runtime = new ModuleRuntime(viewState: viewState, catalog: new BuiltInExternalAccessCatalog());
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

        // 界面是模块自带的视图：它的结果区绑哪条显示源，是**代码声明**的事（这里验声明本身）。
        converter!.HasView.ShouldBeTrue("转换器自带 XAML 视图");
    }

    /// <summary>
    /// **1 ms 连续发送应走全局计时服务**：TextSender 不再自建 Timer，实际发送量应与 1 kHz 档位相符，
    /// 而不是 Windows 通用 Timer 的约 10–15 ms。
    /// </summary>
    [Fact]
    public void 文本发送器_1毫秒连续发送_应达到全局计时服务的档位()
    {
        var staged = StagedModules;
        Directory.Exists(staged).ShouldBeTrue($"模块包未归拢：{staged}（请先运行 ./build.ps1）");

        var viewState = new ViewStateStore();
        using var runtime = new ModuleRuntime(viewState: viewState, catalog: new BuiltInExternalAccessCatalog());
        var services = new RuntimeServices(runtime);
        services.ScanModuleSources([staged]).ShouldBeGreaterThan(0);

        services.AddModule("t1", TextSenderId).Succeeded.ShouldBeTrue();
        services.AddModule("m1", MonitorId).Succeeded.ShouldBeTrue();
        SetLabel(runtime, "t1", "out", "bus1");
        SetLabel(runtime, "m1", "in", "bus1");

        viewState.SetSelectedPage("t1", "text-pages", 0);
        viewState.SetInputText("t1", "text1", "tick");
        viewState.SetInputText("t1", "interval", "1");
        viewState.SetInputText("t1", "continuous", "true");

        Thread.Sleep(400);

        var sent = services.GetPortMetrics("t1", "out").Delivered;
        sent.ShouldBeGreaterThan(
            100,
            $"1 ms 连续发送在 400 ms 窗口内应远超 10 ms 定时器的约 40 条（实测 {sent}）");

        viewState.SetInputText("t1", "continuous", "false");
        Thread.Sleep(80);
        var stopped = services.GetPortMetrics("t1", "out").Delivered;
        Thread.Sleep(120);
        services.GetPortMetrics("t1", "out").Delivered.ShouldBe(stopped, "停止后不得再有幽灵发送");
    }


    /// <summary>
    /// **串口模块（第二批样板）的参数应进配置表单**：读即登记 ⇒ Schema 有字段；枚举的显示名与写回名各司其职。
    /// </summary>
    /// <remarks>
    /// 这条同时钉三件事：① 模块在 <c>OnRegistered</c> 里读一次配置就足以让参数出现在表单（无 UI 也能读到）；
    /// ② <c>[ConfigurationChoice]</c> 只改**显示名**，写回用的始终是**成员名**；
    /// ③ 写回走框架的变更入口（<c>UpdateConfigurationField</c>），而不是模块自己改。
    /// </remarks>
    [Fact]
    public void 串口模块_参数应进配置表单_且枚举的显示名与写回名各司其职()
    {
        var staged = StagedModules;
        Directory.Exists(staged).ShouldBeTrue($"模块包未归拢：{staged}（请先运行 ./build.ps1）");

        var viewState = new ViewStateStore();
        using var runtime = new ModuleRuntime(viewState: viewState, catalog: new BuiltInExternalAccessCatalog());
        var services = new RuntimeServices(runtime);
        services.ScanModuleSources([staged]).ShouldBeGreaterThan(0);

        services.AddModule("s1", "hyw/serial/1.0.0").Succeeded.ShouldBeTrue();

        services.TryGetConfigurationSchema("s1", out var schema).ShouldBeTrue();
        schema.ShouldNotBeNull();

        // ① 五个串口参数都在 —— 判据是"读一次配置就登记"，不是"表单里恰好有这几行"
        schema!.Fields.Select(field => field.Name).ShouldBe(
            ["PortName", "BaudRate", "DataBits", "StopBits", "Parity"],
            ignoreOrder: true);

        // ② 枚举的显示名来自标注，写回名是成员名（两者混同会让界面里只能看到标识符）
        var baud = schema.Fields.Single(field => field.Name == "BaudRate");
        baud.Kind.ShouldBe(ConfigurationFieldKind.Enum);
        baud.Choices.ShouldNotBeNull();
        baud.Choices!.ShouldContain(
            choice => choice.Value == "Baud115200" && choice.Title == "115200",
            "写回用成员名、给人看的是标注里的字");

        // ③ 端口号是**动态下拉**：与这台机器上有没有插串口无关（空表也是下拉）
        schema.Fields.Single(field => field.Name == "PortName").Kind
            .ShouldBe(ConfigurationFieldKind.Enum, "端口号的可选值随环境变 ⇒ 它是下拉，不是文本框");

        // ④ System.IO.Ports 由**宿主**提供（模块包不再自带）：解析到的必须是宿主输出里的那一份
        var ports = AppDomain.CurrentDomain.GetAssemblies()
            .FirstOrDefault(assembly => assembly.GetName().Name == "System.IO.Ports");
        ports.ShouldNotBeNull("宿主必须提供 System.IO.Ports（模块包已不再携带）");
        ports!.Location.StartsWith(AppContext.BaseDirectory, StringComparison.OrdinalIgnoreCase)
            .ShouldBeTrue($"System.IO.Ports 必须来自宿主输出（实际：{ports.Location}）");

        // ⑤ 写回走框架入口，读回来的是新值
        services.UpdateConfigurationField("s1", "BaudRate", "Baud9600").Succeeded.ShouldBeTrue();
        services.TryGetConfigurationSchema("s1", out var updated).ShouldBeTrue();
        updated!.Fields.Single(field => field.Name == "BaudRate").Value.ShouldBe("Baud9600");
    }

    /// <summary>
    /// **网络类模块的地址与端口应按各自性质呈现**：本机地址有"可用值"来源 ⇒ 动态下拉；
    /// 对端地址没有 ⇒ 文本；端口有明确定义域 ⇒ 整数档（带上下限）。
    /// </summary>
    /// <param name="moduleId">模块 Id。</param>
    /// <param name="instanceId">实例 Id。</param>
    /// <param name="localField">本机地址字段名（没有传空串）。</param>
    /// <param name="remoteField">对端地址字段名（没有传空串）。</param>
    /// <param name="portField">端口字段名。</param>
    /// <remarks>
    /// 判据与机器无关：本机地址那一档**永远**至少有 <c>0.0.0.0</c>（"听/绑全部网卡"与本机有几个地址无关），
    /// 所以"它是不是下拉"在任何机器上都可判。
    /// </remarks>
    [Theory]
    [InlineData("hyw/tcp-client/1.0.0", "c1", "", "RemoteHost", "RemotePort")]
    [InlineData("hyw/tcp-server/1.0.0", "s1", "LocalAddress", "", "LocalPort")]
    [InlineData("hyw/udp/1.0.0", "u1", "LocalAddress", "RemoteAddress", "LocalPort")]
    public void 网络类模块的地址与端口应按各自性质呈现(
        string moduleId,
        string instanceId,
        string localField,
        string remoteField,
        string portField)
    {
        var staged = StagedModules;
        Directory.Exists(staged).ShouldBeTrue($"模块包未归拢：{staged}（请先运行 ./build.ps1）");

        var viewState = new ViewStateStore();
        using var runtime = new ModuleRuntime(viewState: viewState, catalog: new BuiltInExternalAccessCatalog());
        var services = new RuntimeServices(runtime);
        services.ScanModuleSources([staged]).ShouldBeGreaterThan(0);
        services.AddModule(instanceId, moduleId).Succeeded.ShouldBeTrue();

        services.TryGetConfigurationSchema(instanceId, out var schema).ShouldBeTrue();
        schema.ShouldNotBeNull();

        if (localField.Length > 0)
        {
            var local = schema!.Fields.Single(field => field.Name == localField);
            local.Kind.ShouldBe(ConfigurationFieldKind.Enum, "本机地址能枚举 ⇒ 动态下拉");
            local.Choices.ShouldNotBeNull();
            local.Choices!.ShouldContain(
                choice => choice.Value == "0.0.0.0",
                "「听/绑全部网卡」这一项与本机有几个地址无关 ⇒ 任何机器上都有得选");
        }

        if (remoteField.Length > 0)
        {
            schema!.Fields.Single(field => field.Name == remoteField).Kind
                .ShouldBe(ConfigurationFieldKind.Text, "对端地址没有可用值来源 ⇒ 文本框");
        }

        var port = schema!.Fields.Single(field => field.Name == portField);
        port.Kind.ShouldBe(ConfigurationFieldKind.WholeNumber, "端口是整数且有定义域");
        port.Minimum.ShouldNotBeNull();
        port.Maximum.ShouldNotBeNull();
    }

    /// <summary>
    /// **输入源由模块代码声明**：声明过的可提交，未声明的仍被拒绝。
    /// </summary>
    /// <remarks>
    /// <para>
    /// 输入源的名字**只有代码声明一个来源**（不再从界面数据文件里翻找）：框架据此判定"这个名字模块登记过吗"。
    /// 未登记的名字拒收并报 Issue —— 那是"漏声明"的可见形态，而不是让一次提交悄悄消失。
    /// </para>
    /// <para>
    /// 本用例用一条最小模块把这个闸门钉住：登记的通过、没登记的拒绝（**校验不能被放宽成"什么都收"**）。
    /// </para>
    /// </remarks>
    [Fact]
    public void 输入源由代码声明_登记过的可提交_未声明的仍拒绝()
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
            TestViewPackages.For(typeof(NestedInputModule)),
            string.Empty);

        services.Registry.TryRegister(definition, out var failure).ShouldBeTrue(failure);
        services.AddModule("n1", "test/nested-input/1.0.0").Succeeded.ShouldBeTrue();

        // 输入由模块代码声明 ⇒ 提交必须被接受
        services.SubmitInput("n1", NestedInputModule.NestedSource, "回车提交")
            .Succeeded.ShouldBeTrue("声明的输入源与它长在界面的哪一层无关，提交都应被接受");

        // 未声明的名字仍然拒绝（修复不能把校验一起放开）
        services.SubmitInput("n1", "不存在", "x").Succeeded.ShouldBeFalse();
    }

    /// <summary>测试夹具：输入声明在 `TabPages` 页内的最小模块。</summary>
    [HYWDebugAssistant.Sdk.ModuleDefinition("test", "nested-input", "1.0.0")]
    public sealed class NestedInputModule : HYWDebugAssistant.Sdk.Lifecycle.IModule
    {
        /// <summary>页内输入源名。</summary>
        public const string NestedSource = "nested1";

        /// <inheritdoc />
        public void OnRegistered(HYWDebugAssistant.Sdk.Context.IModuleContext context)
        {
            ArgumentNullException.ThrowIfNull(context);

            // 界面契约在**代码**里声明：显示源 + 输入源（闸门据此判定）
            context.Declaration.DeclareDisplaySource(
                new HYWDebugAssistant.Sdk.Views.DisplaySourceDeclaration(
                    NestedSource,
                    HYWDebugAssistant.Sdk.Views.ViewDataSourceKind.Text));
            context.Declaration.DeclareInput(NestedSource);
        }
    }

    /// <summary>
    /// 显示器**清空**：模块请求 ⇒ 框架执行（模块不留副本 ⇒ 只能请求）。
    /// </summary>
    [Fact]
    public void 整体测试单元_显示器的清空应真的把显示缓冲清掉()
    {
        var staged = StagedModules;

        var viewState = new ViewStateStore();
        using var runtime = new ModuleRuntime(viewState: viewState, catalog: new BuiltInExternalAccessCatalog());
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
        var staged = StagedModules;
        Directory.Exists(staged).ShouldBeTrue($"模块包未归拢：{staged}（请先运行 ./build.ps1）");

        var viewState = new ViewStateStore();
        using var runtime = new ModuleRuntime(viewState: viewState, catalog: new BuiltInExternalAccessCatalog());
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