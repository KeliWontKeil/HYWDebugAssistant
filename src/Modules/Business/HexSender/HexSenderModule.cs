using System.Diagnostics;
using HYWDebugAssistant.Sdk;
using HYWDebugAssistant.Sdk.Contracts;
using HYWDebugAssistant.Sdk.Context;
using HYWDebugAssistant.Sdk.Lifecycle;
using HYWDebugAssistant.Sdk.Ports;
using HYWDebugAssistant.Sdk.Reporting;
using HYWDebugAssistant.Sdk.Views;

namespace HYWDebugAssistant.Modules.HexSender;

/// <summary>
/// **HEX 发送器**：把十六进制文本解析成字节发出去。
/// </summary>
/// <remarks>
/// <list type="bullet">
///   <item><b>模块页</b>：`docs/modules/hyw-hex-sender.md`；</item>
///   <item><b>与 `hyw/text-sender` 是「同形态的两兄弟」</b>：视图结构、活动形态、拆包口径完全一致，
///     **唯一差别 = 输入解析**（那边按 UTF-8 编码文本，这边按「每字节两位十六进制」解析字节）；
///     **两边声明同一个类型标识 `RawBytes`@v1** ⇒ 与 `hyw/data-monitor` **开箱即连**；</item>
///   <item><b>多页消息 + 回读</b>：`TabPages` 各页一个输入框（源名 `hex1…hex3`），
///     **只有一个「发送」按钮** —— 点它时模块**回读**当前选中页与那一页的当前文本；</item>
///   <item><b>结构约定</b>：按 ①标识与常量 ②声明块 ③钩子块 ④接线与业务 的固定四段写。</item>
/// </list>
/// </remarks>
[ModuleDefinition(
    "hyw",
    "hex-sender",
    "1.0.0",
    DisplayName = "HEX 发送器",
    Description = "把一串十六进制文本解析成字节发出去；单次点击 + 连续发送（开关 + 间隔）",
    Tags = ["hex", "sender"],
    ParticipatesInDataFlow = true)]
public sealed class HexSenderModule : IModule, IActivitySource
{
    // ---------------------------------------------------------------- ① 标识与常量

    /// <summary>输出端口名（唯一的数据端点）。</summary>
    public const string OutPort = "out";

    /// <summary>数据角色 Id（纯源 ⇒ 只有一个输出角色）。</summary>
    public const string RoleId = "hex-send";

    /// <summary>命令名：发送一次（视图按钮用它关联）。</summary>
    public const string SendCommand = "send";

    /// <summary>标签容器的**状态键**（模块靠它回读"当前选中页"）。</summary>
    public const string PagesStateKey = "hex-pages";

    /// <summary>
    /// 消息页数（每页一个输入框）。
    /// </summary>
    /// <remarks>页数取 5（`tabpages` 的标签数量为 5 个）；视图元素数量不设上限，页数可自由取。</remarks>
    public const int PageCount = 5;

    /// <summary>第 n 页输入源名（<c>hex1</c> … <c>hex5</c>）。</summary>
    /// <param name="page">页序号（0 基）。</param>
    /// <returns>输入源名（越界回落到第 1 页）。</returns>
    public static string HexSourceOf(int page) =>
        $"hex{Math.Clamp(page, 0, PageCount - 1) + 1}";

    /// <summary>输入源 / 数据源：连续发送开关。</summary>
    public const string ContinuousSource = "continuous";

    /// <summary>输入源 / 数据源：发送间隔（毫秒）。</summary>
    public const string IntervalSource = "interval";

    /// <summary>数据源：已发送计数。</summary>
    public const string SentSource = "sent";

    /// <summary>
    /// 显示源：**消息**（"为什么发不出去 / HEX 哪里不对"）。
    /// </summary>
    /// <remarks>没有界面实体：模块按自己的逻辑写，界面上一块 `label`（或只读文本面）按声明吃它。</remarks>
    public const string StatusSource = "status";

    /// <summary>
    /// 模块自己挑的**提示角色**（**相对色**）—— 框架只提供"设边框色"这个能力，不认识"红 = 失败"。
    /// </summary>
    /// <remarks>用角色词而不是色值：设置里改那一支色 / 换深浅主题时，这一圈跟着变。</remarks>
    public const string AlertRole = ViewInkRoles.Danger;

    /// <summary>活动周期（`implementation/06` §E.2：20 ms）。</summary>
    public static readonly TimeSpan ActivityInterval = TimeSpan.FromMilliseconds(1);

    /// <summary>数据类型标识（**与两个兄弟模块逐字相同**）。</summary>
    public const string RawBytesTypeId = "RawBytes";

    /// <summary>数据类型版本。</summary>
    public const int RawBytesVersion = 1;

    /// <summary>单条字节上限（= `data` 字节区长度；一个完整 UDP 载荷）。</summary>
    public const int TextCapacity = 1472;

    /// <summary>**`RawBytes`@v1 的契约形状**：`{ len: u16, data: bytes[1472] }`（与两兄弟完全一致）。</summary>
    public static ContractShape RawBytesShape { get; } = ContractShape.OfStruct(
        ("len", ContractShape.OfPrimitive(ContractPrimitive.U16)),
        ("data", ContractShape.OfBytes(TextCapacity)));

    // ---------------------------------------------------------------- ② 声明块

    /// <inheritdoc />
    public void OnRegistered(IModuleContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        context.Declaration.DeclareDataType(new DataTypeDeclaration(
            RawBytesTypeId,
            RawBytesVersion,
            RawBytesShape)
        {
            // 「速率只要有效数据」同文本发送器：`len` = 有效字节数 ⇒ 声明它，
            // 框架逐单元读它累计有效字节（否则补零会被算进"有效速率"）。
            Metering = new MeteringBlock { PayloadLengthField = "len" },
        });

        context.Declaration.DeclarePort(new PortDeclaration(OutPort, PortDirection.Send, RawBytesShape)
        {
            Type = new DataTypeRef(RawBytesTypeId, RawBytesVersion),
        });

        context.Declaration.DeclareRole(RoleDeclaration.Output(
            RoleId,
            new DataTypeRef(RawBytesTypeId, RawBytesVersion),
            OutPort,
            "发送"));

        // ---------------------------------------------------------------- 视图
        // 界面形态**不在代码里声明**：控件树、数据源、命令、窗口尺寸与端口默认边都在
        // 包内 `view.json`（唯一事实源），由框架读取并按 schema 补齐默认值。
        // 本模块的数据源名（`hex1…hex5` / `continuous` / `interval` / `sent`）与命令名（`send`）
        // 必须与 `view.json` 一致；「只放行 16 进制写法」由声明文件的 `inputKind` 表达。
    }

    // ---------------------------------------------------------------- ③ 钩子块

    /// <inheritdoc />
    public Sdk.Lifecycle.ActivityKind Kind => Sdk.Lifecycle.ActivityKind.Periodic;

    /// <inheritdoc />
    public TimeSpan Interval => ActivityInterval;

    // 其余钩子：无配置（不实现 OnConfigurationChanged）· 能力 None（不实现 OnRemoved）·
    // 停/启用由框架暂停活动体（不实现 OnDisabled/OnEnabled）。

    /// <inheritdoc />
    public void RunActivity(IModuleContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        var schedule = Stopwatch.StartNew();
        var nextDue = TimeSpan.Zero;

        _sent = 0;
        context.Ports.Publish(SentSource, CountPayload(0));

        while (!context.StopSignal.IsStopRequested)
        {
            _ = Tick(context, shouldSend: false);

            if (!_continuous || _intervalMs is not { } scheduled)
            {
                nextDue = TimeSpan.Zero;
                _ = context.StopSignal.Wait(ActivityInterval);
                continue;
            }

            if (nextDue <= TimeSpan.Zero)
            {
                nextDue = schedule.Elapsed;
            }

            var due = schedule.Elapsed >= nextDue;

            if (due)
            {
                nextDue = schedule.Elapsed + TimeSpan.FromMilliseconds(scheduled);
            }

            _ = Tick(context, shouldSend: due);
        }
    }

    // ---------------------------------------------------------------- ④ 接线与业务

    private ulong _sent;
    private bool _continuous;

    /// <summary>
    /// **连续发送的间隔**（毫秒）。<see langword="null"/> = 用户**还没设过** —— 与"设成 0"是两件事：
    /// 前者要**显式报错、绝不硬发**，后者是"每个活动周期都发"这条明确的选择。
    /// </summary>
    /// <remarks>
    /// 越界/非法值时**保留上一次的合法值**（"接受范围 = 存储范围 = 文案里的范围"），
    /// 另由 <see cref="_intervalState"/> 记下"这一份能不能用"。
    /// </remarks>
    private int? _intervalMs;

    /// <summary>间隔那一格**现在填的这份**处于什么状态（空 / 填错了 / 可用）。</summary>
    /// <remarks>
    /// <b>与 <see cref="_intervalMs"/> 必须分开</b>：后者是"**当前生效的值**"（越界时按取向**保留旧值**），
    /// 前者是"**用户此刻填的这一份能不能用**"。合成一个就有两种坏法：丢掉"越界不改动当前值"，
    /// 或者把"旧值还在"当成"这一格可用" —— 于是输入框已经空了、开关却还开得起来。
    /// </remarks>
    private IntervalState _intervalState;

    /// <summary>上一次看到的间隔文本（"没变就不重解析"的判据）。</summary>
    private string? _intervalSeen;

    /// <summary>**按页**记"这一页被发送拦下过"（0 基页号）—— 红因此有了按页、粘住的依据。</summary>
    private readonly bool[] _pageBlockedBySend = new bool[PageCount];

    /// <summary>**界面上现在红着的那一格**（源名）—— 唯一记录"红"的地方。</summary>
    private string? _shownAlertSource;

    /// <summary>**「间隔」那一格现在是不是被我锁着**（只读）—— 幂等写法，不需要记"锁了哪一格"。</summary>
    private bool _intervalLocked;

    /// <summary>
    /// **用户拨过"开"、而这件事还没解决** —— 间隔格的红据此**粘住**。
    /// </summary>
    /// <remarks>
    /// 它与"开关那一格的值"是两件事：那一格 = **现在跑不跑**（界面对它照画）；
    /// 这个字段 = **他刚拨过、还没解决**（红要一直挂着，直到他补上或自己关掉）。
    /// 不记它的话红就只活一拍（写回把那格变 false 后，"有人在要连续发"就查不到了）。
    /// 它**只在用户拨动时改**，不由模块写回的值反推。
    /// </remarks>
    private bool _userWantsContinuous;



    /// <summary>**当前页的输入源名**（换页判据）。</summary>
    private string? _currentPageSource;



    /// <summary>
    /// **连续发送的间隔上限**（24 小时）——"定时发送"的全部现实用法都在它之内。
    /// </summary>
    public const int MaxIntervalMs = 86_400_000;


    /// <summary>连续发送是否开着（测试可读）。</summary>
    /// <remarks>
    /// **它就是"界面那一格的值"**：每拍由判定算出并写回那一格，所以三者同源。
    /// 间隔不可用 ⇒ 它是 <see langword="false"/>（开关开不起来）；这一页空**不**影响它。
    /// </remarks>
    public bool IsContinuous => _continuous;

    /// <summary>
    /// **一拍一次判定**（活动体的单步；模块测试直接驱动它，无需起线程）。
    /// </summary>
    /// <param name="context">模块上下文。</param>
    /// <returns>这一拍是否发生了一次"按发送"（发送本身在拍内完成）。</returns>
    /// <remarks>
    /// 与文本发送器**同一套形状**（两个模块共用一份逻辑，必须同判同形）：
    /// 读状态 → 判定（纯函数）→ 调和开关 → 落地 → 该发就发。
    /// </remarks>
    public bool PumpOnce(IModuleContext context) => Tick(context, shouldSend: true);

    /// <summary>一拍一次判定。</summary>
    /// <param name="context">模块上下文。</param>
    /// <param name="shouldSend">调度上"到点了"（连续发送由活动体给；单次由用户命令给）。</param>
    /// <returns>这一拍是否发生了一次"按发送"。</returns>
    /// <remarks>"到点了"与"发得出去"是两件事：前者是调度，后者是判据；都为真才真的发。</remarks>
    private bool Tick(IModuleContext context, bool shouldSend)
    {
        ArgumentNullException.ThrowIfNull(context);

        // ① 读状态：间隔那一格（逐键）与"是不是换了一页"
        SyncInterval(context);
        SyncPageSwitch(context);

        var commandArrived = false;
        var pending = false;

        // **用户按"发送"时指的那一页**（`null` = 本拍没有"按发送"这件事）。
        // 它与"连续发送要发哪一页"是两回事：红是"你刚才那一下没发出去"的记账，属于**那一次动作**。
        int? sendPage = null;

        while (context.Inbox.TryDequeueCommand(out var command))
        {
            if (string.Equals(command, SendCommand, StringComparison.Ordinal))
            {
                // **"空内容不是没事发生"**：用户明明点了发送，那就得说清楚"发不出去、因为这一格是空的"。
                // 这里只记下"他按了、按的是哪一页"；"该发哪一份、该说什么、哪一格该红"由判据统一给。
                commandArrived = true;
                pending = true;
                sendPage = context.ViewState.GetSelectedPage(PagesStateKey) ?? 0;
                continue;
            }

            context.Issues.Report(new IssueReport(
                IssueCategory.ModuleError,
                IssueSeverity.Warning,
                $"未知命令：{command}（模块未实现）"));
        }

        // 本拍**刚提交进来**的那一份（`SubmitInput` 本身不写视图状态：真实链路里是 UI 先写状态再提交，
        // 但命令与提交可能**同拍**到达，此时只有这一份是新的）。
        int? submittedPage = null;
        string? submittedText = null;

        // 本拍**用户拨了开关**（`null` = 没拨）。就地判定，别留到下一拍。
        bool? toggleAsked = null;
        var toggleStands = false;

        while (context.Inbox.TryDequeueInput(out var source, out var text))
        {
            if (string.Equals(source, ContinuousSource, StringComparison.Ordinal))
            {
                // **就在这一刻判定**（唯一写者、当场闭环），结果由下面统一的落地写回。
                // 不"记下意图、等下一拍再作废"：那一拍的空隙里用户可能刚好把间隔填好，
                // 于是被拒的请求又生效了 —— 表现就是"被拒之后补上间隔，开关自己开了"。
                var want = string.Equals(text, "true", StringComparison.OrdinalIgnoreCase);

                toggleAsked = want;
                toggleStands = want && ToggleStands(context);

                // **"他拨过、还没解决"由这一刻决定**（不是由写回后的值反推）：
                //   拨"开" ⇒ 记上（红了要一直挂着）；拨"关" ⇒ 清掉（他自己撤了这次请求）。
                _userWantsContinuous = want;
                continue;
            }

            if (string.Equals(source, IntervalSource, StringComparison.Ordinal))
            {
                continue;   // 间隔由视图状态逐键驱动（见 `SyncInterval`）
            }

            // 文本输入的**显式提交**：等价于"发这一页"（"哪一页"由**源名**解析，不是当前页）
            if (IsHexSource(source))
            {
                submittedPage = PageOf(source);
                submittedText = text ?? string.Empty;
                pending = true;
            }
        }

        // ② **这一拍要发哪一页、哪一份内容**（唯一取内容的地方）。
        var selectedPage = context.ViewState.GetSelectedPage(PagesStateKey) ?? 0;
        var senderPage = submittedPage ?? sendPage ?? selectedPage;
        var outgoing = submittedPage is not null
            ? submittedText ?? string.Empty
            : context.ViewState.GetInputText(HexSourceOf(senderPage)) ?? string.Empty;

        // **"按了发送而这一页是空的"⇒ 记下"这一页被拦下过"**（红按页粘住，补上内容才消失）。
        // 只认"按发送"，不认"连续发送当前页空" —— 后者每拍都成立，会把一整列格子永久染红。
        if (sendPage is not null && outgoing.Length == 0)
        {
            _pageBlockedBySend[Math.Clamp(senderPage, 0, PageCount - 1)] = true;
        }

        // ③ **开关该是几**。
        // · 本拍**刚拨过** ⇒ 就用那一刻判定的结果（当场闭环，请求不会留到下一拍）；
        // · 本拍没拨 ⇒ 维持现状，但若**间隔变得不可用**则立即收起来（不留半开状态）。
        var asked = string.Equals(
            context.ViewState.GetInputText(ContinuousSource), "true", StringComparison.OrdinalIgnoreCase);

        if (toggleAsked is { } want2)
        {
            _continuous = toggleStands;
        }
        else if (asked && !ToggleStands(context))
        {
            _continuous = false;
        }
        else
        {
            _continuous = asked;
        }

        // **被拒**：他拨了"开"而没被答应 ⇒ 这一拍要把话说清楚（红由上面那支粘性字段撑着）
        var askedAndRefused = toggleAsked == true && !_continuous;

        // **真的跑起来了** ⇒ 这次请求已解决，撤掉粘性标记（否则补上间隔后红会一直挂着）
        if (_continuous && _userWantsContinuous)
        {
            _userWantsContinuous = false;
        }

        // ④ 判定（纯函数）：只看"这一拍要发的那一份内容"与"刚被拒过没有"
        var verdict = Evaluate(context, senderPage, outgoing, askedAndRefused);

        // ⑤ **把开关那一格写成应然值**（唯一写者）。位置与应然一致时**不写**（幂等）。
        // 界面每帧读这一格来画，所以**不需要**任何单独的复位信号。
        if (!string.Equals(
            context.ViewState.GetInputText(ContinuousSource),
            _continuous ? "true" : "false",
            StringComparison.OrdinalIgnoreCase))
        {
            context.ViewState.SetInputValue(ContinuousSource, _continuous ? "true" : "false");
        }

        // ⑥ 落地（唯一写界面的地方）
        ApplyVerdict(context, verdict);

        // ⑦ 该发就发（判定说不发就不发）
        if ((pending || shouldSend) && verdict.CanSend && outgoing.Length > 0)
        {
            var source = HexSourceOf(senderPage);
            _ = Send(context, outgoing, source, out _);

            // 发出去 ⇒ 这一页"被拦下过"这一笔可以销掉（内容非空时它就不该红）
            _pageBlockedBySend[Math.Clamp(senderPage, 0, PageCount - 1)] = false;

            ApplyVerdict(context, Evaluate(context, senderPage, outgoing, askedAndRefused: false));
        }

        // **连续发送依赖的那一格要锁住**（真的在按它发时才锁）
        SyncIntervalReadOnly(context);

        return commandArrived;
    }

    /// <summary>
    /// **一次判定的结论**：这一页能不能发出去、该说哪句话、哪一格该红。
    /// </summary>
    /// <param name="CanSend">这一页现在发得出去（内容非空且写法合法）。</param>
    /// <param name="Message">状态行要说的话；<see langword="null"/> = 不说。</param>
    /// <param name="AlertSource">该红的格子（源名）；<see langword="null"/> = 没有该红的。</param>
    /// <remarks>
    /// <b>`CanSend` 只描述"这一页能不能发"，不是"开关成不成立"</b> —— 后者只看 `interval` 那一格
    /// （见 <see cref="ToggleStands"/>）。混在一起就会出现"空页把开关也拨回去"这种错。
    /// </remarks>
    private readonly record struct Verdict(bool CanSend, string? Message, string? AlertSource);

    /// <summary>间隔那一格**现在填的这份**的状态。</summary>
    private enum IntervalState
    {
        /// <summary>没填（空白）：还没设过 —— 要显式说出来，绝不硬发。</summary>
        Empty,

        /// <summary>填了但不可用（越界 / 非数字 / 有杂质）：要说清楚，且**开关因此开不起来**。</summary>
        Invalid,

        /// <summary>可用：收下这个值（<c>_intervalMs</c> 随之更新）。</summary>
        Usable,
    }

    /// <summary>
    /// **「连续发送」这个开关现在成不成立** —— 只看 `interval` 那一格。
    /// </summary>
    /// <param name="context">模块上下文。</param>
    /// <returns>间隔可用为 <see langword="true"/>。</returns>
    /// <remarks>
    /// 只跟"这一格**当前填的这一份能不能用**"绑定（<see cref="_intervalState"/>），
    /// **不看 <see cref="_intervalMs"/>**：那是可能比当前文本更旧的生效值（越界时按取向保留旧值），
    /// 拿它当判据就会出现"输入框已经空了、开关却还开得起来"。
    /// </remarks>
    private bool ToggleStands(IModuleContext context)
    {
        _ = context;
        return _intervalState == IntervalState.Usable;
    }

    /// <summary>
    /// **当前这一页"现在应该是什么样"的唯一判据**（纯函数：只看给定的事实，不存东西、不写界面）。
    /// </summary>
    /// <param name="context">模块上下文。</param>
    /// <param name="page">这一拍要发的那一页（0 基）。</param>
    /// <param name="outgoing">这一拍要发的那一份内容。</param>
    /// <param name="askedAndRefused">用户这一拍拨了"开"、但没被答应（写回已把它变回关）。</param>
    /// <returns>结论。</returns>
    /// <remarks>
    /// <para>
    /// 优先级（同一拍只说一句）：① 间隔填错 → ② **HEX 写法不合法** → ③ 间隔没设且要连续发 →
    /// ④ 这一页空着被拦下 → ⑤ 一切正常。**红按格独立**。
    /// </para>
    /// <para>
    /// 那些"要连续发"的判据依据**他拨过、还没解决**（<see cref="_userWantsContinuous"/> 粘住）
    /// 或**这一拍刚被拒**，<b>不看开关那一格的值</b> —— 它会因写回而变化，拿它当依据红就只活一拍。
    /// </para>
    /// </remarks>
    private Verdict Evaluate(IModuleContext context, int page, string outgoing, bool askedAndRefused)
    {
        _ = context;

        var pageSource = HexSourceOf(page);
        var pageEmpty = outgoing.Length == 0;
        var blocked = _pageBlockedBySend[Math.Clamp(page, 0, PageCount - 1)];
        var wantsContinuous = _userWantsContinuous || askedAndRefused;

        // ① 间隔那一格填错了。**只挡"连续发送"**：用户没想连续发时，那一格填了什么与"这一页发一次"无关。
        if (_intervalState == IntervalState.Invalid)
        {
            var message = wantsContinuous
                ? $"间隔必须是 0 – {MaxIntervalMs} 的整数（毫秒）：连续发送开着也发不出东西"
                : $"间隔必须是 0 – {MaxIntervalMs} 的整数（毫秒）";

            return new Verdict(!wantsContinuous, message, IntervalSource);
        }

        // ② **HEX 写法不合法** ⇒ 说它、标它（判据自己校验，不等 `Send` 失败才记：
        //    "这格写法不对"应在用户填着的时候就说出来，与间隔那一格同一条取向）
        if (!pageEmpty && !HexTextCodec.TryParse(outgoing, out _, out var parseFailure))
        {
            return new Verdict(false, $"HEX 转换错误：{parseFailure}", pageSource);
        }

        // ③ 想连续发送，而那一格是空的 ⇒ 红粘在那一格上（原因还在就一直挂着）
        if (_intervalState == IntervalState.Empty && wantsContinuous)
        {
            return new Verdict(
                false,
                "还没有设置间隔（毫秒）：连续发送开着也发不出东西（先补上这一项）",
                IntervalSource);
        }

        // ④ 这一页空着，而它**被发送拦下过**（或连续发送正开着）
        if (pageEmpty && (blocked || _continuous))
        {
            var message = _continuous
                ? "当前页没有内容：连续发送开着也发不出东西（先补上这一项）"
                : "当前页没有内容：先写点东西再发送";

            // **空页只让"这一拍发不出去"，不动开关**（开关的成立条件是间隔那一格）
            return new Verdict(false, message, pageSource);
        }

        // ⑤ 一切正常 ⇒ 发得出去、不说话、不标红
        return new Verdict(!pageEmpty, null, null);
    }

    /// <summary>**把一次判定落到界面上**（唯一写"红与消息"的地方）。</summary>
    /// <param name="context">模块上下文。</param>
    /// <param name="verdict">判定结论。</param>
    /// <remarks>
    /// **先撤下上一次算出来的红、再挂上这一次的**：于是任何一格都不会失去认领。
    /// 消息也在这里落：**同一拍只有一句**，且与红同源。
    /// </remarks>
    private void ApplyVerdict(IModuleContext context, Verdict verdict)
    {
        if (!string.Equals(_shownAlertSource, verdict.AlertSource, StringComparison.Ordinal))
        {
            if (_shownAlertSource is { Length: > 0 } shown)
            {
                context.Appearance.SetOutlineColor(shown, null);
            }

            _shownAlertSource = verdict.AlertSource;

            if (verdict.AlertSource is { Length: > 0 } next)
            {
                context.Appearance.SetOutlineRole(next, AlertRole);
            }
        }

        context.Display.SetText(StatusSource, verdict.Message ?? string.Empty);
    }

    /// <summary>
    /// **连续发送进行中把「间隔」设成只读**（关掉即恢复）。
    /// </summary>
    /// <param name="context">模块上下文。</param>
    /// <remarks>
    /// 只跟"真的在按这个间隔发"绑定：判据不成立时**不锁**（那时用户正需要改那一格）。
    /// 锁与值同源：不需要另记"锁了哪一格"，每拍算一次、幂等地写下去即可。
    /// </remarks>
    private void SyncIntervalReadOnly(IModuleContext context)
    {
        var want = _continuous && _intervalMs is not null;

        if (want == _intervalLocked)
        {
            return;
        }

        _intervalLocked = want;
        context.Appearance.SetReadOnly(IntervalSource, want);
    }

    /// <summary>
    /// **换页结算**：切走之后"这一页被发送拦下过"这件事不再成立（那是上一次操作的残影）。
    /// </summary>
    /// <param name="context">模块上下文。</param>
    private void SyncPageSwitch(IModuleContext context)
    {
        var page = context.ViewState.GetSelectedPage(PagesStateKey) ?? 0;
        var pageSource = HexSourceOf(page);

        if (string.Equals(_currentPageSource, pageSource, StringComparison.Ordinal))
        {
            return;
        }

        if (_currentPageSource is { Length: > 0 })
        {
            for (var i = 0; i < _pageBlockedBySend.Length; i++)
            {
                _pageBlockedBySend[i] = false;
            }
        }

        _currentPageSource = pageSource;
    }

    /// <summary>
    /// **把「间隔」那一格的值同步进来**（每拍看一眼视图状态；与上次不同才解析）。
    /// </summary>
    /// <param name="context">模块上下文。</param>
    /// <remarks>
    /// 与文本发送器同口径：**读视图状态，不等提交**；**只改状态，不写界面**
    /// （合法 ⇒ 收下；非法 ⇒ 记下"填错了"但**不动当前值**；空 ⇒ 回到"还没设过"）。
    /// </remarks>
    private void SyncInterval(IModuleContext context)
    {
        var text = context.ViewState.GetInputText(IntervalSource);

        if (string.Equals(text, _intervalSeen, StringComparison.Ordinal))
        {
            return;
        }

        _intervalSeen = text;

        if (TryParseInterval(text, out var milliseconds))
        {
            _intervalMs = (int)milliseconds;
            _intervalState = IntervalState.Usable;
            return;
        }

        if (string.IsNullOrWhiteSpace(text))
        {
            _intervalMs = null;
            _intervalState = IntervalState.Empty;
            return;
        }

        // 填了但非法：**保留 `_intervalMs` 当前的合法值**，但这一格此刻**不可用**
        _intervalState = IntervalState.Invalid;
    }

    /// <summary>某个页内输入源对应的页号（显式提交用）。</summary>
    /// <param name="source">源名。</param>
    /// <returns>页号（对不上时为 -1）。</returns>
    private static int PageOf(string source)
    {
        for (var i = 0; i < PageCount; i++)
        {
            if (string.Equals(source, HexSourceOf(i), StringComparison.Ordinal))
            {
                return i;
            }
        }

        return -1;
    }

    /// <summary>当前选中页的十六进制文本（模块**回读**视图状态；空 = 没内容）。</summary>
    /// <param name="context">模块上下文。</param>
    /// <returns>当前页文本（可能为空串）。</returns>
    public static string CurrentPageText(IModuleContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        var page = context.ViewState.GetSelectedPage(PagesStateKey) ?? 0;
        return context.ViewState.GetInputText(HexSourceOf(page)) ?? string.Empty;
    }

    /// <summary>发送**当前选中页**的内容（回读视图状态；解析失败 ⇒ 回报失败 ⇒ 该页输入框红框）。</summary>
    /// <param name="context">模块上下文。</param>
    /// <param name="packets">实际投递的数据包个数。</param>
    /// <returns>是否真的发了。</returns>
    public bool SendCurrentPage(IModuleContext context, out int packets)
    {
        ArgumentNullException.ThrowIfNull(context);

        var page = context.ViewState.GetSelectedPage(PagesStateKey) ?? 0;
        var source = HexSourceOf(page);
        var text = context.ViewState.GetInputText(source) ?? string.Empty;
        return Send(context, text, source, out packets);
    }

    /// <summary>解析并发送一段十六进制文本（**模块级测试可直接调用**，无需线程）。</summary>
    /// <param name="context">模块上下文。</param>
    /// <param name="text">十六进制文本。</param>
    /// <param name="source">输入源名（失败反馈打到这个输入框上）。</param>
    /// <param name="packets">实际投递的数据包个数。</param>
    /// <returns>是否真的发了。</returns>
    public bool Send(IModuleContext context, string text, string source, out int packets)
    {
        ArgumentNullException.ThrowIfNull(context);
        packets = 0;

        if (!HexTextCodec.TryParse(text, out var bytes, out _))
        {
            // **解析失败**：不在这里写界面 —— 那句话与那一格的红由判据 + 统一落地给出
            // （"这是什么写法"归模块；框架不再替它判字符级格式）
            return false;
        }

        if (bytes.Length == 0)
        {
            // 空（或全空白）⇒ **静默不发**（T11）
            return false;
        }

        for (var offset = 0; offset < bytes.Length; offset += TextCapacity)
        {
            var chunk = bytes.AsSpan(offset, Math.Min(TextCapacity, bytes.Length - offset));

            if (context.Ports.TrySend(OutPort, BuildPayload(chunk, TextCapacity)))
            {
                packets++;
            }
        }

        if (packets > 0)
        {
            var total = Interlocked.Add(ref _sent, (ulong)packets);
            context.Ports.Publish(SentSource, CountPayload(total));
        }

        if (packets > 1)
        {
            context.Log.Log(
                ModuleLogLevel.Information,
                $"字节数超过单包上限，已拆成 {packets} 个数据包发送（接收侧会表现为 {packets} 条）");
        }

        return packets > 0;
    }

    /// <summary>把一块字节写进 `{ len, data }` 形状的定长载荷（`data` 尾部补零）。</summary>
    /// <param name="chunk">有效字节。</param>
    /// <param name="capacity">`data` 区长度。</param>
    /// <returns>载荷。</returns>
    public static byte[] BuildPayload(ReadOnlySpan<byte> chunk, int capacity)
    {
        ArgumentOutOfRangeException.ThrowIfGreaterThan(chunk.Length, capacity);

        var payload = new byte[sizeof(ushort) + capacity];
        System.Buffers.Binary.BinaryPrimitives.WriteUInt16LittleEndian(payload, (ushort)chunk.Length);
        chunk.CopyTo(payload.AsSpan(sizeof(ushort)));
        return payload;
    }

    /// <summary>解析「间隔」（0 – <see cref="MaxIntervalMs"/>；与兄弟模块同口径）。</summary>
    /// <param name="text">输入文本。</param>
    /// <param name="milliseconds">解析结果。</param>
    /// <returns>成功为 <see langword="true"/>。</returns>
    public static bool TryParseInterval(string? text, out uint milliseconds)
    {
        milliseconds = 0;

        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        return uint.TryParse(
            text.Trim(),
            System.Globalization.NumberStyles.None,
            System.Globalization.CultureInfo.InvariantCulture,
            out milliseconds)
            && milliseconds <= MaxIntervalMs;
    }

    private static bool IsHexSource(string source)
    {
        for (var i = 0; i < PageCount; i++)
        {
            if (string.Equals(source, HexSourceOf(i), StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }

    private static byte[] CountPayload(ulong value)
    {
        var payload = new byte[sizeof(ulong)];
        System.Buffers.Binary.BinaryPrimitives.WriteUInt64LittleEndian(payload, value);
        return payload;
    }
}
