using System.Buffers.Binary;
using System.Reflection;
using HYWDebugAssistant.Sdk.Access;
using HYWDebugAssistant.Sdk.Configuration;
using HYWDebugAssistant.Sdk.Context;
using HYWDebugAssistant.Sdk.Views;

namespace HYWDebugAssistant.Sdk.AccessKit;

/// <summary>
/// **外部接入通道**：一个模块与框架外部接入面之间的唯一接缝。
/// </summary>
/// <remarks>
/// <list type="bullet">
///   <item><b>只搬字节</b>：本类型不认识数据类型、数据包与组帧 —— 那些属于模块间通信，由模块自己做；</item>
///   <item><b>只发请求</b>：打开 / 关闭由框架在实例循环里执行，通道不直接操作连接；</item>
///   <item><b>状态只有快照</b>：<see cref="Snapshot"/> 是唯一事实源，没有事件面；</item>
///   <item><b>呈现由模块给</b>：状态句与压力句的措辞归模块（<see cref="ExternalAccessStatus"/>），
///     通道只负责会话侧的优先级、灯色、计数与按钮闸门。</item>
/// </list>
/// <para>
/// <b>与口的边界</b>：通道**读写原始字节**，不碰任何端口；把字节变成数据单元（组帧）再投口，
/// 是模块自己的事。通道也**不读**口的流动状态 —— 口侧压力由模块在自己的状态句里表达。
/// </para>
/// </remarks>
public sealed class ExternalAccessChannel
{
    /// <summary>丢失提示的时间窗：最近一次丢弃超过它就不再顶掉连接句（累计数字仍保留）。</summary>
    public static readonly TimeSpan LossHintWindow = TimeSpan.FromSeconds(10);

    private readonly IModuleContext _context;
    private readonly ExternalAccessChannelSpec _spec;
    private readonly Func<ExternalAccessSnapshot, ExternalAccessStatus> _describe;
    private readonly Func<ExternalAccessPressure, ExternalAccessSnapshot, string?>? _pressure;

    private string? _lastStateText;
    private string? _lastLinkRole;
    private string? _lastStateRole;
    private string? _lastDropRole;
    private string[] _configurationFields = [];
    private bool _configurationFieldsResolved;
    private bool? _lastCanOpen;
    private bool? _lastCanClose;
    private bool? _lastCanEdit;
    private bool _openReady = true;
    private string? _openReadyReason;

    /// <summary>创建一条接入通道（模块在 <c>OnRegistered</c> 里建一次）。</summary>
    /// <param name="context">模块上下文。</param>
    /// <param name="spec">声明面（接入名 / 显示源名 / 命令名）。</param>
    /// <param name="describe">状态句（模块自己的文案；可带自己的严重度）。</param>
    /// <param name="pressure">会话侧压力句（返回 <see langword="null"/> = 不说，通道继续回落）。</param>
    public ExternalAccessChannel(
        IModuleContext context,
        ExternalAccessChannelSpec spec,
        Func<ExternalAccessSnapshot, ExternalAccessStatus> describe,
        Func<ExternalAccessPressure, ExternalAccessSnapshot, string?>? pressure = null)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(spec);
        ArgumentNullException.ThrowIfNull(describe);

        _context = context;
        _spec = spec;
        _describe = describe;
        _pressure = pressure;
    }

    /// <summary>该接入的当前快照（没有这条接入时是 <see cref="ExternalAccessSnapshot.None"/>）。</summary>
    public ExternalAccessSnapshot Snapshot => _context.Access.GetSnapshot(_spec.AccessName);

    /// <summary>
    /// 声明当前配置是否允许打开；未就绪时 Open 置灰，状态句使用给定原因。
    /// </summary>
    /// <param name="ready">配置是否完整且可打开。</param>
    /// <param name="reason">未就绪原因（空 = 不覆盖状态句）。</param>
    public void SetOpenReadiness(bool ready, string? reason = null)
    {
        _openReady = ready;
        _openReadyReason = reason;
    }

    /// <summary>
    /// 进入活动态时挂上原始数据回调并发布首拍状态。
    /// </summary>
    /// <param name="onRawData">原始字节块回调（模块在这里组帧并投口）。</param>
    /// <remarks>
    /// 必须在外部接入面已由框架挂上之后调用（即模块的 <c>OnActivated</c>）。
    /// </remarks>
    public void Attach(ReceiveHandler onRawData)
    {
        ArgumentNullException.ThrowIfNull(onRawData);

        _context.Access.SetReceiveHandler(_spec.AccessName, onRawData);
        ResetPresentationCache();
        PublishStatus();
    }

    /// <summary>主动取一个原始数据块（step Pull 模块用；与回调互斥）。</summary>
    /// <param name="destination">目标缓冲；小于单元长度 ⇒ 丢弃并计数。</param>
    /// <param name="length">实际字节数。</param>
    public bool TryRead(Span<byte> destination, out int length) =>
        _context.Access.TryRead(_spec.AccessName, destination, out length);

    /// <summary>写一个原始数据块（有界等待；<see langword="false"/> = 未发出并已计入丢弃）。</summary>
    /// <param name="payload">有效字节（不含任何组帧）。</param>
    public bool Write(ReadOnlySpan<byte> payload) => _context.Access.Write(_spec.AccessName, payload);

    /// <summary>
    /// 处理一条模块命令：打开 / 关闭 / 清空计数。
    /// </summary>
    /// <param name="commandName">命令名（模块声明的名字）。</param>
    /// <returns>是否属于本接入的标准命令（<see langword="false"/> = 模块自己的命令）。</returns>
    public bool HandleCommand(string commandName)
    {
        var spec = _spec;
        var snapshot = Snapshot;

        if (string.Equals(commandName, spec.OpenCommand, StringComparison.Ordinal))
        {
            // **动作边界闸门**：按钮置灰只是界面；命令/快捷键/程序化请求也必须在这里被拒绝，
            // 否则“非法地址不发起连接”只对鼠标点击成立。
            if (_openReady
                && !snapshot.Suspended
                && snapshot.State is ExternalAccessState.Closed or ExternalAccessState.Faulted)
            {
                _ = _context.Access.RequestOpen(spec.AccessName);
            }

            return true;
        }

        if (string.Equals(commandName, spec.CloseCommand, StringComparison.Ordinal))
        {
            if (!snapshot.Suspended
                && snapshot.State is ExternalAccessState.Open or ExternalAccessState.Opening)
            {
                _ = _context.Access.RequestClose(spec.AccessName);
            }

            return true;
        }

        if (string.Equals(commandName, spec.ClearCountersCommand, StringComparison.Ordinal))
        {
            // 清计数是排障动作：队列数据、连接数与故障态都由会话保留。
            _ = _context.Access.ClearCounters(spec.AccessName);
            PublishStatus();
            return true;
        }

        return false;
    }

    /// <summary>发布一拍状态：状态句、灯色、计数、按钮与配置字段闸门（只在值变化时写）。</summary>
    public void PublishStatus()
    {
        var spec = _spec;
        var snapshot = Snapshot;

        var canOpen = _openReady
            && !snapshot.Suspended
            && snapshot.State is ExternalAccessState.Closed or ExternalAccessState.Faulted;
        var canClose = !snapshot.Suspended
            && snapshot.State is ExternalAccessState.Open or ExternalAccessState.Opening;
        var canEdit = !snapshot.Suspended
            && snapshot.State is ExternalAccessState.Closed or ExternalAccessState.Faulted;

        PublishEnabled(spec.OpenCommand, canOpen, ref _lastCanOpen);
        PublishEnabled(spec.CloseCommand, canClose, ref _lastCanClose);

        if (_lastCanEdit != canEdit)
        {
            _lastCanEdit = canEdit;
            foreach (var field in ResolveConfigurationFields())
            {
                _context.Appearance.SetEnabled(field, canEdit);
            }
        }

        var presentation = BuildPresentation(snapshot);
        if (!_openReady
            && !string.IsNullOrEmpty(_openReadyReason)
            && snapshot.State is ExternalAccessState.Closed or ExternalAccessState.Faulted)
        {
            presentation = new StatusPresentation(_openReadyReason, ViewInkRoles.Warning, presentation.DropRole);
        }

        if (!string.Equals(presentation.Text, _lastStateText, StringComparison.Ordinal))
        {
            _context.Display.SetText(spec.StateSource, presentation.Text);
            _lastStateText = presentation.Text;
        }

        // **灯与状态文字同源**：丢失/故障 ⇒ 红（danger）；积压/饱和 ⇒ 黄（warning）；其余回落到连接状态角色。
        var linkRole = presentation.Severity ?? LinkRole(snapshot);
        if (!string.Equals(linkRole, _lastLinkRole, StringComparison.Ordinal))
        {
            _context.Appearance.SetOutlineRole(spec.LinkSource, linkRole);
            _lastLinkRole = linkRole;
        }

        var textRole = presentation.Severity is ViewInkRoles.Danger or ViewInkRoles.Warning
            ? presentation.Severity
            : null;
        if (!string.Equals(textRole, _lastStateRole, StringComparison.Ordinal))
        {
            _context.Appearance.SetTextRole(spec.StateSource, textRole);
            _lastStateRole = textRole;
        }

        if (!string.Equals(presentation.DropRole, _lastDropRole, StringComparison.Ordinal))
        {
            _context.Appearance.SetTextRole(spec.DropCountSource, presentation.DropRole);
            _lastDropRole = presentation.DropRole;
        }

        // **单一来源**：会话轴的有效字节（模块不再自建"口侧丢弃字节"这第二份账）
        var dropped = Math.Max(0, snapshot.DroppedBytes);

        PublishU64(_context, spec.RxCountSource, ToUInt64(snapshot.ReceivedBytes));
        PublishU64(_context, spec.TxCountSource, ToUInt64(snapshot.SentBytes));
        PublishU64(_context, spec.DropCountSource, ToUInt64(dropped));

        if (spec.ConnCountSource is { Length: > 0 } conn)
        {
            PublishU32(_context, conn, (uint)Math.Max(0, snapshot.ActiveConnections));
        }
    }

    private static string? LinkRole(ExternalAccessSnapshot snapshot)
    {
        if (snapshot.Suspended)
        {
            return ViewInkRoles.Idle;
        }

        return snapshot.State switch
        {
            ExternalAccessState.Open => ViewInkRoles.Success,
            ExternalAccessState.Opening or ExternalAccessState.Closing => ViewInkRoles.Warning,
            ExternalAccessState.Faulted => ViewInkRoles.Danger,
            _ => ViewInkRoles.Idle,
        };
    }

    /// <summary>
    /// **状态呈现的唯一来源**：会话侧优先级 + 模块的措辞，一次算出文本 / 灯色 / 读数色。
    /// </summary>
    /// <param name="snapshot">该接入的当前快照。</param>
    /// <remarks>
    /// 顺序固定：非打开态（故障 / 开合 / 连接句）→ 本次打开以来的接收丢失 → **模块口侧当前丢弃**
    /// → 发送丢失 → 发送积压饱和 → 接收饱和 → **模块口侧当前饱和** → 模块连接句。
    /// 会话侧的“丢失”与“饱和”按最后一次事件谁更新来决定谁说话：丢弃比饱和新就显示丢弃，
    /// 饱和比丢弃新就显示饱和；否则最近丢弃会在窗口内一直压住已经恢复的积压。
    /// </remarks>
    private StatusPresentation BuildPresentation(ExternalAccessSnapshot snapshot)
    {
        var dropRole = snapshot.LastDropAtSinceOpen is not null ? ViewInkRoles.Danger : null;
        var module = _describe(snapshot);

        if (snapshot.State != ExternalAccessState.Open)
        {
            var closedSeverity = module.Severity ?? snapshot.State switch
            {
                ExternalAccessState.Faulted => ViewInkRoles.Danger,
                ExternalAccessState.Opening or ExternalAccessState.Closing => ViewInkRoles.Warning,
                _ => snapshot.RejectedSinceOpen > 0 ? ViewInkRoles.Danger : null,
            };

            return new StatusPresentation(module.Text, closedSeverity, dropRole);
        }

        if (snapshot.ReceiveDropStatusActive
            && Pressure(ExternalAccessPressure.ReceiveLost, snapshot) is { } lostReceive)
        {
            return new StatusPresentation(lostReceive, ViewInkRoles.Danger, dropRole);
        }

        // 口侧丢失是模块状态句里的 Danger；它必须先于会话的积压警告，
        // 否则串口 tx 队列已经开始丢了，状态行仍停留在“发送队列积压”。
        if (module.Severity == ViewInkRoles.Danger)
        {
            return new StatusPresentation(module.Text, module.Severity, dropRole);
        }

        // 当前发送丢弃 episode（写失败 / 积压挤旧 / 过期）优先于积压；
        // 下一次成功写会清掉 SendDropActive 并唤醒实例线程刷新状态。
        if (snapshot.SendDropStatusActive
            && Pressure(ExternalAccessPressure.SendLost, snapshot) is { } lostSend)
        {
            return new StatusPresentation(lostSend, ViewInkRoles.Danger, dropRole);
        }

        // 只要当前还有积压就报积压；深度归零立即回落，不再等历史窗口。
        if (snapshot.SendBacklogStatusActive
            && Pressure(ExternalAccessPressure.SendBacklogged, snapshot) is { } backlogged)
        {
            return new StatusPresentation(backlogged, ViewInkRoles.Warning, dropRole);
        }

        if (snapshot.ReceiveBacklogStatusActive
            && Pressure(ExternalAccessPressure.ReceiveSaturated, snapshot) is { } receive)
        {
            return new StatusPresentation(receive, ViewInkRoles.Warning, dropRole);
        }

        if (module.Severity == ViewInkRoles.Warning)
        {
            return new StatusPresentation(module.Text, module.Severity, dropRole);
        }

        return new StatusPresentation(module.Text, module.Severity, dropRole);
    }

    private string? Pressure(ExternalAccessPressure kind, ExternalAccessSnapshot snapshot) =>
        _pressure?.Invoke(kind, snapshot);

    /// <summary>清空呈现缓存：注册/激活后首拍必写，避免存储重建后沿用旧值。</summary>
    private void ResetPresentationCache()
    {
        _lastStateText = null;
        _lastLinkRole = null;
        _lastStateRole = null;
        _lastDropRole = null;
    }

    private readonly record struct StatusPresentation(string Text, string? Severity, string? DropRole);

    /// <summary>
    /// 从框架登记的**参数类型**反射配置字段名（用于会话打开/故障时禁用编辑）。
    /// </summary>
    /// <remarks>
    /// 参数类型在模块注册时可能还没挂上（外部接入面尚未接线）⇒ 返回空并保持未解析，
    /// 下一次状态发布继续尝试；这不是错误。
    /// </remarks>
    private string[] ResolveConfigurationFields()
    {
        if (_configurationFieldsResolved)
        {
            return _configurationFields;
        }

        if (_context.Access.GetParametersType(_spec.AccessName) is not { } parametersType)
        {
            return _configurationFields;
        }

        _configurationFieldsResolved = true;
        _configurationFields = parametersType
            .GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(property => property.GetCustomAttribute<ConfigurationFieldAttribute>() is not null)
            .Select(property => property.Name)
            .ToArray();

        return _configurationFields;
    }

    private void PublishEnabled(string source, bool enabled, ref bool? previous)
    {
        if (previous == enabled)
        {
            return;
        }

        previous = enabled;
        _context.Appearance.SetEnabled(source, enabled);
    }

    private static void PublishU64(IModuleContext context, string source, ulong value)
    {
        Span<byte> payload = stackalloc byte[sizeof(ulong)];
        BinaryPrimitives.WriteUInt64LittleEndian(payload, value);
        context.Ports.Publish(source, payload);
    }

    private static void PublishU32(IModuleContext context, string source, uint value)
    {
        Span<byte> payload = stackalloc byte[sizeof(uint)];
        BinaryPrimitives.WriteUInt32LittleEndian(payload, value);
        context.Ports.Publish(source, payload);
    }

    private static ulong ToUInt64(long value) => value <= 0 ? 0 : (ulong)value;
}
