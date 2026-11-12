using System.Buffers;
using System.Buffers.Binary;
using HYWDebugAssistant.Sdk;
using HYWDebugAssistant.Sdk.Contracts;
using HYWDebugAssistant.Sdk.Context;
using HYWDebugAssistant.Sdk.Lifecycle;
using HYWDebugAssistant.Sdk.Ports;
using HYWDebugAssistant.Sdk.Reporting;
using HYWDebugAssistant.Sdk.Text;
using HYWDebugAssistant.Sdk.Timing;
using HYWDebugAssistant.Sdk.Views;

namespace HYWDebugAssistant.Modules.TextSender;

/// <summary>
/// **文本发送器**：发送一串文本；单次点击 + 连续发送（开关 + 间隔）。
/// </summary>
/// <remarks>
/// <list type="bullet">
///   <item><b>模块页</b>：`docs/modules/hyw-text-sender.md`；</item>
///   <item><b>字节口径</b>（与 C 里面的约定对齐）：`len` = **字节数**（`strlen` 同口径）·
///     文本编码 **UTF-8**（ASCII 部分单字节兼容）· **`0x00` 是合法数据、不是终止符** · 协议层**不碰字符**；</item>
///   <item><b>多页消息 + 回读</b>：输入区是 `TabPages`（各页一个输入框，源名
///     `text1…text5`），**只有一个「发送」按钮** —— 点它时模块**回读**「当前选中页 + 该页的当前文本」
///     （含用户**尚未提交**的编辑）⇒ **所见即所发**，且 UI 上不必每页重复一套控件；</item>
///   <item><b>结构约定</b>：本类按**固定四段**写 —— ① 标识与常量 ② 声明块
///     ③ 钩子块 ④ 接线与业务。</item>
/// </list>
/// </remarks>
[ModuleDefinition(
    "hyw",
    "text-sender",
    DisplayNameKey = TextSenderText.Title,
    DescriptionKey = TextSenderText.Description,
    Tags = ["text", "sender"],
    ParticipatesInDataFlow = true,
    ViewFactory = typeof(Views.TextSenderViewFactory))]
public sealed class TextSenderModule : IModule, IActivitySource
{
    /// <summary>取一条本模块文本表里的文案（拥有者 = 本模块程序集；表随包走）。</summary>
    /// <param name="key">文本表键。</param>
    private static string Tr(string key) => TextCatalog.Get(typeof(TextSenderModule).Assembly, key);

    /// <summary>取一条带占位符的本模块文案。</summary>
    /// <param name="key">文本表键。</param>
    /// <param name="args">格式化参数。</param>
    private static string Tr(string key, params object?[] args) =>
        TextCatalog.Get(typeof(TextSenderModule).Assembly, key, args);

    // ---------------------------------------------------------------- ① 标识与常量

    /// <summary>输出端口名（唯一的数据端点）。</summary>
    public const string OutPort = "out";

    /// <summary>数据角色 Id（**一种数据类型 = 一个角色**；本模块是纯源 ⇒ 只有一个输出角色）。</summary>
    public const string RoleId = "text-send";

    /// <summary>命令名：发送一次（视图按钮用它关联）。</summary>
    public const string SendCommand = "send";

    /// <summary>标签容器的**状态键**：模块靠它回读"当前选中页"（`ViewElement.StateKey`）。</summary>
    public const string PagesStateKey = "text-pages";

    /// <summary>
    /// 消息页数（每页一个输入框）。
    /// </summary>
    /// <remarks>
    /// 页数取 5（`tabpages` 的标签数量为 5 个）；视图元素数量不设上限 ⇒ 页数可自由取。
    /// </remarks>
    public const int PageCount = 5;

    /// <summary>第 1 页输入源名。</summary>
    public const string Text1 = "text1";

    /// <summary>第 2 页输入源名。</summary>
    public const string Text2 = "text2";

    /// <summary>第 3 页输入源名。</summary>
    public const string Text3 = "text3";

    /// <summary>第 4 页输入源名。</summary>
    public const string Text4 = "text4";

    /// <summary>第 5 页输入源名。</summary>
    public const string Text5 = "text5";

    /// <summary>第 n 页输入源名（<c>text1</c> … <c>text5</c>）。</summary>
    /// <param name="page">页序号（0 基）。</param>
    /// <returns>输入源名（越界时回落到第 1 页 —— 表格是声明的一部分，不该因越界崩）。</returns>
    public static string TextSourceOf(int page) => Math.Clamp(page, 0, PageCount - 1) switch
    {
        0 => Text1,
        1 => Text2,
        2 => Text3,
        3 => Text4,
        _ => Text5,
    };

    /// <summary>输入源 / 数据源：连续发送开关。</summary>
    public const string ContinuousSource = "continuous";

    /// <summary>输入源 / 数据源：发送间隔（毫秒，数值输入 + 框外单位标签）。</summary>
    public const string IntervalSource = "interval";

    /// <summary>数据源：已发送计数。</summary>
    public const string SentSource = "sent";

    /// <summary>
    /// 显示源：**消息**（"为什么发不出去"）。
    /// </summary>
    /// <remarks>没有界面实体：模块按自己的逻辑写，界面上一块 `label`（或只读文本面）按声明吃它。</remarks>
    public const string StatusSource = "status";

    /// <summary>
    /// 模块自己挑的**提示角色**（**相对色**）—— 框架只提供"设边框色"这个能力，不认识"红 = 失败"。
    /// </summary>
    /// <remarks>用角色词而不是色值：设置里改那一支色 / 换深浅主题时，这一圈跟着变。</remarks>
    public const string AlertRole = ViewInkRoles.Danger;

    /// <summary>
    /// **发一条消息给界面**（写状态源；要标出"是哪一格不对"就同时给它上提示色）。
    /// </summary>
    /// <param name="context">模块上下文。</param>
    /// <param name="message">消息文本；<see langword="null"/> ⇒ 清掉消息。</param>
    /// <param name="subject">这一步说的是哪一格（源名）。</param>
    /// <param name="alert">要不要把 <paramref name="subject"/> 标成提示色（false ⇒ 清掉标色）。</param>
    /// <remarks>
    /// 两件事分开表达：**消息**是文本（给人读的话），**颜色**是外观（模块自己的选择）。
    /// 之所以不合成一个"成功/失败"参数：那会让框架替模块规定"失败必须长什么样"。
    /// </remarks>
    private static void Report(IModuleContext context, string? message, string subject, bool alert)
    {
        context.Display.SetText(StatusSource, message ?? string.Empty);
        context.Appearance.SetOutlineRole(subject, alert ? AlertRole : null);
    }

    /// <summary>
    /// 数据类型标识 —— **PascalCase 短名**（不带作者前缀、不带版本）。
    /// </summary>
    /// <remarks>
    /// 模块 ID 仍是 `hyw/*` 形态；**模块 ID 与类型标识是两个命名空间**，不要互相套用。
    /// <para>
    /// 这个类型的本质是**一段带长度的字节区**（0–1472 字节）——**文本（UTF-8）与 HEX 字节都只是它的用法**。
    /// 文本发送器、HEX 发送器与数据监视器**共用同一标识**才能**开箱即连**
    /// （兼容规则：**不同标识默认不兼容**，要显式强连）。
    /// </para>
    /// </remarks>
    public const string RawBytesTypeId = "RawBytes";

    /// <summary>
    /// **单条文本的字节上限**（= `data` 字节区的长度；**一个完整 UDP 载荷**）。
    /// </summary>
    /// <remarks>
    /// 契约**只定长**（`03` §2）⇒ 每个数据单元**永远搬 `2 + 1472 = 1474` 字节**（就算只发 1 个字符）。
    /// 超过 1472 的文本由本模块**内部拆成多个数据包**发送（契约不上调；接收侧因此表现为多条）。
    /// </remarks>
    public const int TextCapacity = 1472;

    /// <summary>
    /// **`RawBytes` 的契约形状**：`{ len: u16, data: bytes[1472] }`。
    /// </summary>
    /// <remarks>
    /// <list type="bullet">
    ///   <item><b>`len` = 有效字节数</b>（0–1472）——**唯一的"到哪结束"判据**；
    ///     `data` 尾部**补零**只是定长填充，**`0x00` 是合法数据**（不是终止符）；</item>
    ///   <item>**文本**类的用法：按 **UTF-8** 编码进 `data`，拆包**按字符边界**切；
    ///     **HEX/二进制**类的用法：字节原样进 `data`；**形状不区分二者** —— 那是用法的事；</item>
    ///   <item>消费者：下游模块（如 `hyw/data-monitor`）按 `len` 切片后解码 —— 形状本身**不解释语义**。</item>
    /// </list>
    /// </remarks>
    public static ContractShape RawBytesShape { get; } = ContractShape.OfStruct(
        ("len", ContractShape.OfPrimitive(ContractPrimitive.U16)),
        ("data", ContractShape.OfBytes(TextCapacity)));

    // ---------------------------------------------------------------- ② 声明块

    /// <inheritdoc />
    public void OnRegistered(IModuleContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        // ① 数据类型（形状即契约）→ ② 口 → ③ 角色 → ④ 视图（顺序即 `06` §C.2 的声明块顺序）
        context.Declaration.DeclareDataType(new DataTypeDeclaration(
            RawBytesTypeId,
            RawBytesShape)
        {
            // 「速率只要有效数据」：`len` 就是**有效字节数**（0–1472，尾部补零不算）
            // ⇒ 声明它 ⇒ 框架逐单元读这个字段累计有效字节，第 3 档速率才是**真实**有效速率
            //（未声明时框架只能按"契约尺寸"算，把补零也算成有效数据）。
            Metering = new MeteringBlock { PayloadLengthField = "len" },
        });

        // 端口声明在 Views/TextSenderView.axaml 的 ModuleView.Ports 里。

        // 纯源角色：**只有输出口** ⇒ α（两侧同型）天然满足；单角色 ⇒ 不声明 DeclareFlow
        context.Declaration.DeclareRole(RoleDeclaration.Output(
            RoleId,
            RawBytesTypeId,
            OutPort,
            Tr(TextSenderText.RoleSend)));

        DeclareUi(context);

        _context = context;
        _timer = context.Timers.Create();
        _timer.Tick += OnTimerTick;
    }

    /// <summary>
    /// **界面契约**：显示源 / 输入源 / 命令。
    /// </summary>
    /// <remarks>
    /// <para>
    /// 这些名字只在这里写一次：界面按名字读、用户按名字提交、按钮按名字触发 ——
    /// 三处若各写一份，改名时必然漏一处。
    /// </para>
    /// <para>
    /// 「间隔」同时是**显示源**（界面要显示它的当前值）与**输入源**（用户能改它），所以两条都要声明。
    /// </para>
    /// </remarks>
    private static void DeclareUi(IModuleContext context)
    {
        foreach (var name in new[] { Text1, Text2, Text3, Text4, Text5 })
        {
            context.Declaration.DeclareDisplaySource(new DisplaySourceDeclaration(name, ViewDataSourceKind.Text));
            context.Declaration.DeclareInput(new InputDeclaration(name) { Persistence = InputPersistence.Persistent });
        }

        context.Declaration.DeclareDisplaySource(new DisplaySourceDeclaration(ContinuousSource, ViewDataSourceKind.Value, ContractPrimitive.Bool));
        context.Declaration.DeclareDisplaySource(new DisplaySourceDeclaration(IntervalSource, ViewDataSourceKind.Text));
        context.Declaration.DeclareDisplaySource(new DisplaySourceDeclaration(SentSource, ViewDataSourceKind.Value, ContractPrimitive.U64));
        context.Declaration.DeclareDisplaySource(new DisplaySourceDeclaration(StatusSource, ViewDataSourceKind.Text));

        // 连续发送是“启动就会持续发”的动作，不恢复；间隔是用户设置，恢复。
        context.Declaration.DeclareInput(ContinuousSource);
        context.Declaration.DeclareInput(new InputDeclaration(IntervalSource) { Persistence = InputPersistence.Persistent });

        context.Declaration.DeclareCommand(SendCommand, Tr(TextSenderText.Send));
    }

    // ---------------------------------------------------------------- ③ 钩子块

    /// <inheritdoc />
    public ReceiveMode Receive => ReceiveMode.Push;

    /// <inheritdoc />
    public void OnStep(IModuleContext context, StepReason reason)
    {
        ArgumentNullException.ThrowIfNull(context);
        _context = context;

        if (reason.HasFlag(StepReason.Start))
        {
            // 初始状态：把「已发送」置 0（`OnActivated()` 没有上下文 ⇒ 在这里做这件"需要上下文"的事）
            _sent = 0;
            context.Ports.Publish(SentSource, CountPayload(0));
        }

        _ = Tick(context, shouldSend: false);
        SyncTimer(context);
    }

    /// <summary>定时器 Tick：只在实例线程触发，和 OnStep 串行。</summary>
    private void OnTimerTick(object? sender, EventArgs e)
    {
        if (_context is not { } context)
        {
            return;
        }

        _ = Tick(context, shouldSend: true);
        SyncTimer(context);
    }

    /// <summary>把“连续发送 + 可用间隔”落到唯一一个定时器上。</summary>
    private void SyncTimer(IModuleContext context)
    {
        _ = context;

        if (_timer is null)
        {
            return;
        }

        if (_continuous && _intervalMs is { } interval && interval > 0)
        {
            var desired = TimeSpan.FromMilliseconds(interval);

            if (_timer.Interval != desired)
            {
                _timer.Interval = desired;
            }

            if (!_timer.IsEnabled)
            {
                _timer.Start();
            }

            return;
        }

        if (_timer.IsEnabled)
        {
            _timer.Stop();
        }
    }

    // ---------------------------------------------------------------- ④ 接线与业务

    private IModuleTimer? _timer;
    private IModuleContext? _context;

    /// <summary>移除前释放定时器（框架也会兜底释放；这里保持模块自己的资源纪律）。</summary>
    public void OnRemoved()
    {
        _timer?.Dispose();
        _timer = null;
    }

    private ulong _sent;

    /// <summary>
    /// **连续发送现在是不是开着** —— 它是**判定结果**，不是用户意图，也不是独立状态。
    /// </summary>
    /// <remarks>
    /// 每拍由 <see cref="ContinuousSource"/> 那一格的值与"间隔可用吗"算出（见 <c>Tick</c>），
    /// 并把结果**写回那一格**。于是"界面位置 = 那一格的值 = 这个字段"三者同源，只有一个事实。
    /// </remarks>
    private volatile bool _continuous;

    /// <summary>**「间隔」那一格现在是不是被我锁着**（只读）—— 幂等写法，不需要记"锁了哪一格"。</summary>
    private bool _intervalLocked;



    /// <summary>
    /// **连续发送的间隔**（毫秒）。<see langword="null"/> = 用户**还没设过**；
    /// 0 可以被输入并保留，但**不能作为连续发送周期**——连续发送要求大于 0。
    /// </summary>
    /// <remarks>
    /// 越界/非法值时**保留上一次的合法值**（"接受范围 = 存储范围 = 文案里的范围"：
    /// 越界不该悄悄改变正在生效的行为），另由 <see cref="_intervalState"/> 记下"这一份能不能用"。
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
    /// <remarks>
    /// "这一格现在不对"要一直看着（补上内容才消失）；而"用户按过发送"是**一次动作** ——
    /// 两者粘性不同、必须分开记：合成一支就会在"提示一闪就没"与"没动作也冒红"之间二选一。
    /// </remarks>
    private readonly bool[] _pageBlockedBySend = new bool[PageCount];

    /// <summary>
    /// **用户拨过"开"、而这件事还没解决** —— 间隔格的红据此**粘住**。
    /// </summary>
    /// <remarks>
    /// <para>
    /// 它与"开关那一格的值"是两件事，必须分开：
    /// <list type="bullet">
    ///   <item>那一格 = **现在跑不跑**（写回后即判定结果，界面照它画）；</item>
    ///   <item>这个字段 = **他刚拨过、还没解决**（红要一直挂着，直到他补上或自己关掉）。</item>
    /// </list>
    /// </para>
    /// <para>
    /// 不记它的话红就只活一拍：写回把那格变成 false 之后，"有人在要连续发"这件事就查不到了，
    /// 下一拍判据顺手把红清掉 —— 用户看到的是"红一闪就没"，等于没告诉他为什么拨不动。
    /// </para>
    /// <para>
    /// 它**只在用户拨动时改**（关掉 ⇒ 清；开起来 ⇒ 清），不由模块自己写回的值反推 ——
    /// 否则又回到"拿被写回的值当意图"那个自相矛盾上。
    /// </para>
    /// </remarks>
    private bool _userWantsContinuous;

    /// <summary>**界面上现在红着的那一格**（源名）—— 唯一记录"红"的地方。</summary>
    /// <remarks>
    /// 每次落地都是"**先撤下这一格、再挂上新的那一格**"：只记一支的话，后来的判定会把它顶掉，
    /// 先前那一格的红就**再也消不掉**了（这正是用户报的那条）。
    /// </remarks>
    private string? _shownAlertSource;

    /// <summary>**上一次写进状态源的那句话**（值没变就不重复写 —— 状态缓冲的版本号不该每拍变一次）。</summary>
    private string? _shownStatus;



    /// <summary>**当前页的输入源名**（换页判据）。</summary>
    private string? _currentPageSource;



    /// <summary>
    /// **连续发送的间隔上限**（24 小时）——"定时发送"的全部现实用法都在它之内。
    /// </summary>
    /// <remarks>有上限是刻意的：**接受范围 = 存储范围 = 报错文案里的范围**，三者必须同一个值。</remarks>
    public const int MaxIntervalMs = 86_400_000;


    /// <summary>
    /// **一拍一次判定**（step 的单步；模块测试直接驱动它，无需起线程）。
    /// </summary>
    /// <param name="context">模块上下文。</param>
    /// <returns>这一拍是否发生了一次"按发送"（供测试断言；发送本身在拍内完成）。</returns>
    /// <remarks>
    /// <para>
    /// 一拍只做四件事：<b>读状态 → 判定（纯函数）→ 落地（唯一写界面的地方）→ 该发就发</b>。
    /// 顺序上"先发送、再落地"是刻意的：发送的结果要在**同一拍**的落地里体现出来。
    /// </para>
    /// <para>
    /// <b>「间隔」不看收件箱，看视图状态</b>：用户在那一格里打字时，值**逐键**就写进视图状态了
    /// （与"页内文本"同一条路），所以模块每拍看一眼、变了才解析 —— **不需要**任何"提交"动作。
    /// 让编辑去触发提交（回车 / 失焦）等于把"提交"变成用户可感知的行为，而它只是实现机制；
    /// 更要命的是：发送器的页面文本也在同一条通道上，编辑即提交会变成"打完字点一下别处就发一条"。
    /// </para>
    /// <para>
    /// <b>判据是纯函数、落地只有一处</b>：界面恒等于"现在应该是什么样"，
    /// 于是不存在"某一格的红没人认领"或"提示晚一拍"这类残留（旧写法四处各自写红，必然互相顶替）。
    /// </para>
    /// </remarks>
    public bool PumpOnce(IModuleContext context, bool shouldSend = true) => Tick(context, shouldSend);

    /// <summary>一拍一次判定：读状态 → 判定 → 落地 → 该发就发。</summary>
    /// <param name="context">模块上下文。</param>
    /// <param name="shouldSend">调度上"到点了"（连续发送由框架的 Due 给；单次由用户命令给）。</param>
    /// <returns>这一拍是否发生了一次"按发送"。</returns>
    /// <remarks>
    /// "到点了"与"发得出去"是**两件事**：前者是调度，后者是判据。两者都为真才真的发 ——
    /// 合成一个就会出现"每拍都到点 ⇒ 每毫秒一条"（实测 600 ms 发了 47 条）。
    /// </remarks>
    private bool Tick(IModuleContext context, bool shouldSend)
    {
        ArgumentNullException.ThrowIfNull(context);

        // ① 读状态：间隔那一格（逐键）与"是不是换了一页"
        SyncInterval(context);
        SyncPageSwitch(context);

        var wasContinuous = _continuous;
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
                Tr(TextSenderText.StatusUnknownCommand, command)));
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
                // **就在这一刻判定**（唯一写者、当场闭环），把结果记在本拍的局部变量里；
                // "把这个结果落到界面"由下面统一的落地做（不在这里各写一遍）。
                // 为什么不"记下意图、等下一拍再作废"：那一拍的空隙里用户可能刚好把间隔填好，
                // 于是被拒的请求又生效了 —— 表现就是"被拒之后补上间隔，开关自己开了"。
                var want = string.Equals(text, "true", StringComparison.OrdinalIgnoreCase);

                toggleAsked = want;
                toggleStands = want && ToggleStands(context);

                // **"他拨过、还没解决"由这一刻决定**（不是由写回后的值反推）：
                //   拨"开" ⇒ 记上（红了要一直挂着，直到补上间隔 —— 或他拨回"关"）；
                //   拨"关" ⇒ 清掉（他自己撤了这次请求）。
                _userWantsContinuous = want;
                continue;
            }

            if (string.Equals(source, IntervalSource, StringComparison.Ordinal))
            {
                // 间隔**不从这里来**（见 `SyncInterval`）：它由视图状态逐键驱动
                continue;
            }

            // 文本输入的**显式提交**（回车 / 将来的显式入口）：等价于"发这一页"
            if (IsTextSource(source, out var explicitPage))
            {
                submittedPage = explicitPage;
                submittedText = text ?? string.Empty;
                pending = true;
            }
        }

        // ② **这一拍要发哪一页、哪一份内容**（唯一取内容的地方）。
        var selectedPage = context.ViewState.GetSelectedPage(PagesStateKey) ?? 0;
        var senderPage = submittedPage ?? sendPage ?? selectedPage;
        var outgoing = submittedPage is not null
            ? submittedText ?? string.Empty
            : context.ViewState.GetInputText(TextSourceOf(senderPage)) ?? string.Empty;

        // **"按了发送而这一页是空的"⇒ 记下"这一页被拦下过"**（红按页粘住，补上内容才消失）。
        // 只认"按发送"，不认"连续发送当前页空" —— 后者每拍都成立，会把一整列格子永久染红。
        if (sendPage is not null && outgoing.Length == 0)
        {
            _pageBlockedBySend[Math.Clamp(senderPage, 0, PageCount - 1)] = true;
        }

        // ③ **开关该是几**。
        // · 本拍**刚拨过** ⇒ 就用那一刻判定的结果（当场闭环，请求不会留到下一拍）；
        // · 本拍没拨 ⇒ 维持现状（那一格的值就是现状），但若**间隔变得不可用**则立即收起来
        //   （运行中把间隔清空 ⇒ 开关自己关掉，不留半开状态）。
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

        // **被拒**：他拨了"开"而没被答应 ⇒ 这一拍要把话说清楚（红则由上面那支粘性字段撑着）
        var askedAndRefused = toggleAsked == true && !_continuous;

        // **真的跑起来了** ⇒ 这次请求已解决，撤掉粘性标记（否则补上间隔后红会一直挂着）
        if (_continuous && _userWantsContinuous)
        {
            _userWantsContinuous = false;
        }

        // ④ 判定（纯函数）：只看"这一拍要发的那一份内容"与"刚被拒过没有"，红与话据此算出来
        var verdict = Evaluate(context, senderPage, outgoing, askedAndRefused);

        // ⑤ **只在"这次 toggle 被处理"或"被迫关掉"时写回开关那一格**。
        // 每个普通 step 都把模块算出的值写回会覆盖并发到达的用户意图：
        // 用户把那一格拨到 true、而这一拍读到的仍是旧值时，写回 false 会让勾选失效。
        // 写回 true 只发生在刚接受了用户这次 toggle（提交只进收件箱、没写视图状态的路径）；
        // 写回 false 只发生在拒绝或被迫关闭。
        var acceptedToggle = toggleAsked == true && _continuous;
        var forcedOff = !_continuous && wasContinuous;

        if (acceptedToggle || askedAndRefused || forcedOff)
        {
            var desired = acceptedToggle ? "true" : "false";

            if (!string.Equals(
                context.ViewState.GetInputText(ContinuousSource),
                desired,
                StringComparison.OrdinalIgnoreCase))
            {
                context.ViewState.SetInputValue(ContinuousSource, desired);
            }
        }

        // ⑥ 落地（唯一写界面的地方）
        ApplyVerdict(context, verdict);

        // ⑦ 该发就发（判定说不发就不发）
        if ((pending || (shouldSend && _continuous)) && verdict.CanSend && outgoing.Length > 0)
        {
            _ = Send(context, outgoing, out _);

            // 发出去 ⇒ 这一页"被拦下过"这一笔可以销掉（内容非空时它就不该红）
            _pageBlockedBySend[Math.Clamp(senderPage, 0, PageCount - 1)] = false;

            ApplyVerdict(context, Evaluate(context, senderPage, outgoing, askedAndRefused: false));
        }


        // **连续发送所依赖的那一格要锁住**：开着就只读（改不了值 ⇒ 节奏不会中途变形），关掉即恢复。
        SyncIntervalReadOnly(context);


        return commandArrived;
    }

    /// <summary>
    /// **一次判定的结论**：这一拍该说哪句话、哪一格该红、这一页能不能发出去。
    /// </summary>
    /// <param name="CanSend">这一页现在发得出去（内容非空）。</param>
    /// <param name="Message">状态行要说的话；<see langword="null"/> = 不说。</param>
    /// <param name="AlertSource">该红的格子（源名）；<see langword="null"/> = 没有该红的。</param>
    /// <remarks>
    /// <b>`CanSend` 只描述"这一页能不能发"，不是"开关成不成立"</b> —— 后者只看 `interval` 那一格
    /// （见 <see cref="ToggleStands"/>）。两者混在一起就会出现"空页把开关也拨回去"这种错。
    /// </remarks>
    private readonly record struct Verdict(bool CanSend, string? Message, string? AlertSource);

    /// <summary>间隔那一格**现在填的这份**的状态。</summary>
    private enum IntervalState
    {
        /// <summary>没填（空白）：还没设过 —— 要显式说出来，绝不硬发。</summary>
        Empty,

        /// <summary>填了但不可用（越界 / 非数字 / 有杂质）：要说清楚，且**开关因此开不起来**。</summary>
        Invalid,

        /// <summary>语法可用：收下这个值（0 也收下，但连续发送要求大于 0）。</summary>
        Usable,
    }

    /// <summary>
    /// **「连续发送」这个开关现在成不成立** —— 只看 `interval` 那一格。
    /// </summary>
    /// <param name="context">模块上下文。</param>
    /// <returns>间隔可用于连续发送（大于 0 且不超过 <see cref="MaxIntervalMs"/>）为 <see langword="true"/>。</returns>
    /// <remarks>
    /// <para>
    /// <b>它与"这一页有没有内容"无关</b>：间隔是这个开关的**配置**，内容是它的**输入**。
    /// 后者为空只意味着"这一拍发不出去"（标红 + 说一句），**不该把开关也拨回去** ——
    /// 用户明确要求过"切到空页不自动关"。
    /// </para>
    /// <para>
    /// 判据只看"**这一刻填的这一份能不能用**"（<see cref="_intervalState"/>）：
    /// <b>不看 <see cref="_intervalMs"/></b> —— 它是可能比当前文本更旧的**生效值**
    /// （越界时按取向保留旧值），拿它当判据就会出现"输入框已经空了、开关却还开得起来"。
    /// 也不记"上一轮是不是被拦过"：那种记忆会让"补上之后还得先关一次再开"。
    /// </para>
    /// </remarks>
    private bool ToggleStands(IModuleContext context)
    {
        _ = context;

        // **这一格现在填的这份可用吗**；0 是合法输入值，但不是合法的连续发送周期。
        return _intervalState == IntervalState.Usable && _intervalMs is > 0;
    }

    /// <summary>
    /// **当前这一页"现在应该是什么样"的唯一判据**（纯函数：只看视图状态，不存东西、不写界面）。
    /// </summary>
    /// <param name="context">模块上下文。</param>
    /// <param name="page">这一拍要发的那一页（0 基）。</param>
    /// <param name="outgoing">这一拍要发的那一份内容。</param>
    /// <param name="askedAndRefused">用户这一拍拨了"开"、但没被答应（写回已把它变回关）。</param>
    /// <returns>结论。</returns>
    /// <remarks>
    /// <para>
    /// 收成一个纯函数是这套逻辑的骨架：它可反复算、可单测，且**不可能与界面不同步** ——
    /// 界面是它的输出，而不是各自维护的第二份事实。
    /// </para>
    /// <para>
    /// <b>优先级（同一拍只说一句）</b>：① 间隔填错 → ② 间隔为 0 且用户要连续发 →
    /// ③ 间隔没设且用户要连续发 → ④ 这一页空着被拦下 → ⑤ 一切正常（不说话）。
    /// <b>红按格独立</b>：间隔错标间隔格、这一页空着被拦下标这一页。
    /// </para>
    /// <para>
    /// <b>四个"要不要说"的判据各自独立</b>：间隔错（现在填的这份不合法）、间隔缺（用户要连续发但没有可用值）、
    /// 页被拦下（用户按过发送且这一页仍空）、连续发送站着但这一页空（发不出去，得说）。
    /// 它们互不顶替 —— 这正是旧写法（一支 `_blockedBy`）办不到的事。
    /// </para>
    /// </remarks>
    private Verdict Evaluate(IModuleContext context, int page, string outgoing, bool askedAndRefused)
    {
        _ = context;

        var pageSource = TextSourceOf(page);
        var pageEmpty = outgoing.Length == 0;
        var blocked = _pageBlockedBySend[Math.Clamp(page, 0, PageCount - 1)];

        // **同一拍只说一句、按优先级取第一条成立的**。
        // 红是"**这一格现在不对**"的记账：只要原因还在，它就一直挂着（粘住）——
        // 所以标红只看"原因在不在"，不看"这一拍有没有动作"。
        // 话怎么说则分两种口径：用户想要连续发（意图还在）就说"开着也发不出东西"，否则只说"填错了"。

        // **红的依据 = 他拨过"开"而这件事还没解决**（`_userWantsContinuous` 粘住），
        // 或本拍刚被拒（那一下要把话说出来）。**不看那一格的值** —— 它会因为写回而变，
        // 拿它当依据红就只活一拍。
        var wantsContinuous = _userWantsContinuous || askedAndRefused;

        // ① 间隔那一格填错了
        if (_intervalState == IntervalState.Invalid)
        {
            var message = wantsContinuous
                ? Tr(TextSenderText.StatusIntervalInvalidContinuous, MaxIntervalMs)
                : Tr(TextSenderText.StatusIntervalInvalid, MaxIntervalMs);

            // **填错了只挡"连续发送"**：用户没想连续发时，那一格填了什么与"这一页发一次"无关 ——
            // 无条件挡下来会把单次发送（回车提交也算）一起挡死。
            return new Verdict(!pageEmpty, message, IntervalSource);
        }

        // ② 0 可以被输入，但不能作为连续发送周期；只有用户想要连续发送时才报出来。
        if (_intervalState == IntervalState.Usable
            && _intervalMs is <= 0
            && wantsContinuous)
        {
            return new Verdict(
                !pageEmpty,
                Tr(TextSenderText.StatusIntervalInvalidContinuous, MaxIntervalMs),
                IntervalSource);
        }

        // ③ 想连续发送，而那一格是空的 ⇒ 红粘在那一格上（原因还在就一直挂着）
        if (_intervalState == IntervalState.Empty && wantsContinuous)
        {
            return new Verdict(
                !pageEmpty,
                Tr(TextSenderText.StatusIntervalEmptyContinuous),
                IntervalSource);
        }

        // ③ 这一页空着，而它**被发送拦下过**（或连续发送正开着）
        if (pageEmpty && (blocked || _continuous))
        {
            var message = _continuous
                ? Tr(TextSenderText.StatusPageEmptyContinuous)
                : Tr(TextSenderText.StatusPageEmpty);

            // **空页只让"这一拍发不出去"，不动开关**（开关的成立条件是间隔那一格）
            return new Verdict(false, message, pageSource);
        }

        // ④ 一切正常 ⇒ 发得出去、不说话、不标红
        return new Verdict(!pageEmpty, null, null);
    }

    /// <summary>
    /// **把一次判定落到界面上**（唯一写"红与消息"的地方）。
    /// </summary>
    /// <param name="context">模块上下文。</param>
    /// <param name="verdict">判定结论。</param>
    /// <remarks>
    /// <para>
    /// <b>先撤下上一次算出来的红，再挂上这一次的</b>（<see cref="_shownAlertSource"/> 记着上一次那一格）：
    /// 于是**任何一格都不会失去认领** —— 旧写法只记一支"被拦的是谁"，被后面的判定顶掉之后，
    /// 先前那一格的红就再也消不掉了。
    /// </para>
    /// <para>消息也在这里落：<b>同一拍只有一句</b>，且与红同源（谁该红、该说什么，是同一次判定给的）。</para>
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

        var message = verdict.Message ?? string.Empty;

        // **同一句话不重复写**：连续发送时每拍都会走这里，值没变还写会把状态缓冲的版本号
        // 每拍推一次（界面因此每帧重读一遍）；只在真正换话时才写。
        if (!string.Equals(_shownStatus, message, StringComparison.Ordinal))
        {
            _shownStatus = message;
            context.Display.SetText(StatusSource, message);
        }
    }

    /// <summary>
    /// **连续发送进行中把「间隔」设成只读**（关掉即恢复）。
    /// </summary>
    /// <param name="context">模块上下文。</param>
    /// <remarks>
    /// 为什么要锁：连续发送按这个值跑节奏，中途改值会让"这一条按旧间隔、下一条按新间隔"，
    /// 用户看到的是"节奏莫名其妙变了"。锁住之后，要改就先停 —— 这是只读的标准做法
    /// （文本面那边会灰化、不显光标，文字仍可选中）。
    /// </remarks>
    private void SyncIntervalReadOnly(IModuleContext context)
    {
        // **只跟"真的在按这个间隔发"绑定**：开关开着且间隔可用时才锁 ——
        // 那时改值会让发送节奏变形。判据不成立时**不锁**（用户正需要改那一格）。
        // 锁与值同源：不需要另记一份"我锁了哪一格"，每拍算一次、幂等地写下去即可。
        var want = _continuous && _intervalMs is > 0;

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
    /// <remarks>
    /// 红本身仍是**按页、粘住**的：只要还在同一页、那格仍然空着，它就一直看着。
    /// 清掉的是"用户在这一页按过发送"这条事实 —— 用户在别处折腾几轮再切回来时，
    /// 不该莫名其妙看到一片红（那时他什么都没按）。
    /// </remarks>
    private void SyncPageSwitch(IModuleContext context)
    {
        var page = context.ViewState.GetSelectedPage(PagesStateKey) ?? 0;
        var pageSource = TextSourceOf(page);

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
    /// <para>
    /// <b>读视图状态而不是等提交</b>：用户在那一格里打字时值就逐键写进视图状态了，
    /// 所以"填完去点勾选框"这条路径天然成立 —— 不需要回车、也不需要失焦提交。
    /// </para>
    /// <para>
    /// <b>只改状态，不写界面</b>：合法 ⇒ 收下值；非法 ⇒ 记下"填错了"但**不动当前值**
    /// （越界不该悄悄改变正在生效的行为）；空 ⇒ 回到"还没设过"。
    /// "该说什么话、该标哪一格"由 <see cref="Evaluate"/> + <see cref="ApplyVerdict"/> 统一给出。
    /// </para>
    /// </remarks>
    private void SyncInterval(IModuleContext context)
    {
        var text = context.ViewState.GetInputText(IntervalSource);

        if (string.Equals(text, _intervalSeen, StringComparison.Ordinal))
        {
            return;
        }

        _intervalSeen = text;

        if (TextSenderCodec.TryParseInterval(text, out var milliseconds))
        {
            _intervalMs = (int)milliseconds;
            _intervalState = IntervalState.Usable;
            return;
        }

        if (string.IsNullOrWhiteSpace(text))
        {
            _intervalMs = null;          // 空 = 还没设过
            _intervalState = IntervalState.Empty;
            return;
        }

        // 填了但非法：**保留 `_intervalMs` 当前的合法值**（"越界不改动当前值"这条取向），
        // 但这一格此刻**不可用** ⇒ 开关开不起来、红挂在这儿、话说在这儿。
        _intervalState = IntervalState.Invalid;
    }

    /// <summary>当前间隔（毫秒；测试可读）。</summary>
    public int? IntervalMilliseconds => _intervalMs;

    /// <summary>连续发送是否开着（测试可读）。</summary>
    /// <remarks>
    /// **它就是"界面那一格的值"**：每拍由判定算出并写回那一格，所以三者同源。
    /// 间隔不可用 ⇒ 它是 <see langword="false"/>（开关开不起来）；这一页空**不**影响它。
    /// </remarks>
    public bool IsContinuous => _continuous;

    /// <summary>已发送计数。</summary>
    public ulong SentCount => Interlocked.Read(ref _sent);

    /// <summary>
    /// **发送当前选中页的内容**（回读视图状态 ⇒ 含用户**尚未提交**的编辑；空文本 ⇒ 静默不发）。
    /// </summary>
    /// <param name="context">模块上下文。</param>
    /// <param name="packets">实际投递的数据包个数（拆包后可能 &gt; 1）。</param>
    /// <returns>是否真的发了。</returns>
    public bool SendCurrentPage(IModuleContext context, out int packets)
    {
        ArgumentNullException.ThrowIfNull(context);

        var page = context.ViewState.GetSelectedPage(PagesStateKey) ?? 0;
        var text = context.ViewState.GetInputText(TextSourceOf(page)) ?? string.Empty;
        return Send(context, text, out packets);
    }

    /// <summary>当前选中页的文本（模块**回读**视图状态；空 = 没内容）。</summary>
    /// <param name="context">模块上下文。</param>
    /// <returns>当前页文本（可能为空串）。</returns>
    public static string CurrentPageText(IModuleContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        var page = context.ViewState.GetSelectedPage(PagesStateKey) ?? 0;
        return context.ViewState.GetInputText(TextSourceOf(page)) ?? string.Empty;
    }
    /// <summary>发送一段文本（**模块级测试可直接调用**，无需线程）。</summary>
    /// <param name="context">模块上下文。</param>
    /// <param name="text">要发送的文本（原样，不做规范化）。</param>
    /// <param name="packets">实际投递的数据包个数。</param>
    /// <returns>是否真的发了（**空文本 ⇒ false 且什么都不做**）。</returns>
    public bool Send(IModuleContext context, string text, out int packets)
    {
        ArgumentNullException.ThrowIfNull(context);
        packets = 0;

        // **空内容静默不发**（T11："窗口为空这在各个层级的意义都是一致的：没东西，不发即可"）
        if (string.IsNullOrEmpty(text))
        {
            return false;
        }

        // **按字符边界拆包**：先把"这一块最多能放多少字符"量出来（只量长度、不分配），
        // 再一次性编码进**池化**的定长载荷。旧写法逐字符 `ToString()+GetBytes()`，
        // 1 KB 文本每包要造约 40 KB 垃圾 —— 高频发送时这是 GC 的主要来源。
        var payload = ArrayPool<byte>.Shared.Rent(sizeof(ushort) + TextCapacity);

        try
        {
            var start = 0;

            while (start < text.Length)
            {
                var charCount = TextSenderCodec.CountCharsInChunk(text, start, TextCapacity);
                if (charCount == 0)
                {
                    // 单字符都放不下（capacity < 4 的理论情形）：放弃该字符而不是切碎它
                    start += char.IsHighSurrogate(text[start]) && start + 1 < text.Length
                        && char.IsLowSurrogate(text[start + 1])
                        ? 2
                        : 1;
                    continue;
                }

                var bytes = TextSenderCodec.EncodeChunk(
                    text, start, charCount, payload.AsSpan(sizeof(ushort), TextCapacity));

                BinaryPrimitives.WriteUInt16LittleEndian(payload, (ushort)bytes);

                // `data` 尾部补零：保持"定长载荷、零填充"的原有形状（接收方仍按 len 切片）。
                var used = sizeof(ushort) + bytes;
                Array.Clear(payload, used, sizeof(ushort) + TextCapacity - used);

                if (context.Ports.TrySend(OutPort, payload.AsSpan(0, sizeof(ushort) + TextCapacity)))
                {
                    packets++;
                }

                start += charCount;
            }
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(payload);
        }

        if (packets > 0)
        {
            var total = Interlocked.Add(ref _sent, (ulong)packets);
            context.Ports.Publish(SentSource, CountPayload(total));
        }

        // 拆包提示：**状态栏一条**（T14c：拆包是"真的在发"，与"空 = 不发"不同 ⇒ 应知情，但不弹窗、不红框）
        if (packets > 1)
        {
            context.Log.Log(
                ModuleLogLevel.Information,
                Tr(TextSenderText.StatusSplitPackets, packets, TextCapacity));
        }

        return packets > 0;
    }

    private static bool IsTextSource(string source, out int page)
    {
        for (var i = 0; i < PageCount; i++)
        {
            if (string.Equals(source, TextSourceOf(i), StringComparison.Ordinal))
            {
                page = i;
                return true;
            }
        }

        page = -1;
        return false;
    }

    private static byte[] CountPayload(ulong value)
    {
        var payload = new byte[sizeof(ulong)];
        System.Buffers.Binary.BinaryPrimitives.WriteUInt64LittleEndian(payload, value);
        return payload;
    }
}



