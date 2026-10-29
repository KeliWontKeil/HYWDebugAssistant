using System.Buffers.Binary;
using System.Reflection;
using HYWDebugAssistant.Sdk.Access;
using HYWDebugAssistant.Sdk.Configuration;
using HYWDebugAssistant.Sdk.Context;
using HYWDebugAssistant.Sdk.Contracts;
using HYWDebugAssistant.Sdk.Lifecycle;
using HYWDebugAssistant.Sdk.Ports;
using HYWDebugAssistant.Sdk.Views;

namespace HYWDebugAssistant.Sdk.AccessKit;

/// <summary>
/// **外部接入模块的共用骨架**：声明标准 <c>RawBytes</c> 契约、两个口、角色、显示源、命令、接入；
/// 负责双向数据搬运、状态灯、状态文字与计数发布。
/// </summary>
/// <remarks>
/// <para>
/// 它是**模块侧帮助类，不是框架件**：只使用 <see cref="IModuleContext"/> 的公开面；
/// 四个模块继承它，只提供 <see cref="Spec"/> 与 <see cref="Describe"/>。
/// </para>
/// <para>
/// <b>驱动方式</b>：模块不实现 <see cref="IActivitySource"/>，由实例循环把命令 / 数据 / 状态推给
/// <see cref="IModuleInteractionSink"/>；四个模块因此不轮询、不建 IO 线程。
/// </para>
/// <para>
/// <b>数据路径</b>：外部原始字节 → 组 <c>{len,data}</c> → <c>rx</c> 口；
/// <c>tx</c> 口 → 按 <c>len</c> 取有效字节 → 写回外部。
/// </para>
/// </remarks>
public abstract class ExternalAccessModule : IModule, IModuleInteractionSink
{
    private IModuleContext? _context;
    private byte[] _frame = [];
    private string? _lastStateText;
    private string? _lastLinkRole;
    private long _portDroppedBytes;
    private string[] _configurationFields = [];
    private bool _configurationFieldsResolved;
    private bool? _lastCanOpen;
    private bool? _lastCanClose;
    private bool? _lastCanEdit;

    /// <summary>本模块的共用骨架参数（接入名 / 标准标识 / 口 / 显示源 / 命令 / 容量）。</summary>
    protected abstract AccessModuleSpec Spec { get; }

    /// <summary>把会话快照翻译成一句人类可读的状态（模块自己的文案）。</summary>
    /// <param name="snapshot">该接入的当前快照。</param>
    protected abstract string Describe(ExternalAccessSnapshot snapshot);

    /// <summary>
    /// 状态灯的外观角色（相对色；默认按会话状态映射）。
    /// </summary>
    /// <param name="snapshot">该接入的当前快照。</param>
    protected virtual string? LinkRole(ExternalAccessSnapshot snapshot)
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
    /// 非标准命令的落点（四个模块只有打开/关闭；需要更多命令时覆盖）。
    /// </summary>
    /// <param name="context">模块上下文。</param>
    /// <param name="commandName">命令名。</param>
    protected virtual void OnUnknownCommand(IModuleContext context, string commandName)
    {
        _ = context;
        _ = commandName;
    }

    /// <inheritdoc />
    public void OnRegistered(IModuleContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        _context = context;
        var spec = Spec;

        if (spec.Capacity is < 1 or > BytesShape.MaxLength)
        {
            throw new InvalidOperationException(
                $"RawBytes 字节区容量必须在 1–{BytesShape.MaxLength} 之间（当前 {spec.Capacity}）");
        }

        _frame = new byte[RawBytesFraming.FrameSize(spec.Capacity)];

        var shape = RawBytesFraming.Shape(spec.Capacity);
        var type = new DataTypeRef(RawBytesFraming.TypeId, RawBytesFraming.Version);

        context.Declaration.DeclareDataType(new DataTypeDeclaration(
            RawBytesFraming.TypeId,
            RawBytesFraming.Version,
            shape)
        {
            Metering = new MeteringBlock { PayloadLengthField = "len" },
        });

        context.Declaration.DeclarePort(new PortDeclaration(spec.RxPort, PortDirection.Send, shape)
        {
            Type = type,
            Edge = PortEdge.Bottom,
        });

        context.Declaration.DeclarePort(new PortDeclaration(spec.TxPort, PortDirection.Receive, shape)
        {
            Type = type,
            Edge = PortEdge.Top,
        });

        context.Declaration.DeclareRole(RoleDeclaration.Both(
            spec.RoleId,
            type,
            spec.TxPort,
            spec.RxPort,
            spec.RoleDisplayName));

        context.Declaration.DeclareDisplaySource(new DisplaySourceDeclaration(spec.LinkSource, ViewDataSourceKind.Text));
        context.Declaration.DeclareDisplaySource(new DisplaySourceDeclaration(spec.StateSource, ViewDataSourceKind.Text));
        context.Declaration.DeclareDisplaySource(new DisplaySourceDeclaration(spec.RxCountSource, ViewDataSourceKind.Value, ContractPrimitive.U64));
        context.Declaration.DeclareDisplaySource(new DisplaySourceDeclaration(spec.TxCountSource, ViewDataSourceKind.Value, ContractPrimitive.U64));
        context.Declaration.DeclareDisplaySource(new DisplaySourceDeclaration(spec.DropCountSource, ViewDataSourceKind.Value, ContractPrimitive.U64));

        if (spec.ConnCountSource is { Length: > 0 } conn)
        {
            context.Declaration.DeclareDisplaySource(new DisplaySourceDeclaration(conn, ViewDataSourceKind.Value, ContractPrimitive.U32));
        }

        context.Declaration.DeclareCommand(spec.OpenCommand, spec.OpenTitle);
        context.Declaration.DeclareCommand(spec.CloseCommand, spec.CloseTitle);

        // 模块只声明"接入名 + 标准标识"；适配器与参数类型都由框架按标准创建/登记。
        context.Declaration.DeclareExternalAccess(new ExternalAccessDeclaration(
            spec.AccessName,
            spec.StandardId)
        {
            MaxUnitBytes = spec.Capacity,
        });

        // tx 口的接收处理在注册期登记（占位投递面会把它存进同一个处理器表）。
        context.Ports.SetReceiveHandler(spec.TxPort, OnPortData);
    }

    /// <inheritdoc />
    public void OnActivated()
    {
        if (_context is null)
        {
            return;
        }

        // 此时外部接入面已由 L4 挂上：登记回调，数据由实例循环投递。
        _context.Access.SetReceiveHandler(Spec.AccessName, OnAccessData);
        PublishStatus();
    }

    /// <inheritdoc />
    public void OnCommand(string commandName)
    {
        if (_context is null)
        {
            return;
        }

        var spec = Spec;
        var snapshot = _context.Access.GetSnapshot(spec.AccessName);

        if (string.Equals(commandName, spec.OpenCommand, StringComparison.Ordinal))
        {
            if (!snapshot.Suspended
                && snapshot.State is ExternalAccessState.Closed or ExternalAccessState.Faulted)
            {
                _context.Access.RequestOpen(spec.AccessName);
            }

            return;
        }

        if (string.Equals(commandName, spec.CloseCommand, StringComparison.Ordinal))
        {
            if (!snapshot.Suspended
                && snapshot.State is ExternalAccessState.Open or ExternalAccessState.Opening)
            {
                _context.Access.RequestClose(spec.AccessName);
            }

            return;
        }

        OnUnknownCommand(_context, commandName);
    }

    /// <inheritdoc />
    public void OnInput(string sourceName, string text)
    {
        _ = sourceName;
        _ = text;
    }

    /// <inheritdoc />
    public void OnWork() => PublishStatus();

    /// <summary>外部数据到达（实例线程）：组帧并从 <c>rx</c> 口发出。</summary>
    /// <param name="payload">原始字节（≤ 容量）。</param>
    private void OnAccessData(ReadOnlySpan<byte> payload)
    {
        if (_context is null)
        {
            return;
        }

        var spec = Spec;
        if (payload.Length > spec.Capacity)
        {
            _portDroppedBytes += payload.Length;
            return;
        }

        var frame = _frame;
        if (frame.Length != RawBytesFraming.FrameSize(spec.Capacity))
        {
            return;
        }

        RawBytesFraming.WriteFrame(payload, frame, spec.Capacity);

        if (!_context.Ports.TrySend(spec.RxPort, frame))
        {
            _portDroppedBytes += payload.Length;
        }
    }

    /// <summary>口数据到达（实例线程）：按 <c>len</c> 取有效字节并写回外部。</summary>
    /// <param name="payload">定长载荷。</param>
    private void OnPortData(ReadOnlySpan<byte> payload)
    {
        if (_context is null)
        {
            return;
        }

        var spec = Spec;
        if (!RawBytesFraming.TryReadContent(payload, spec.Capacity, out var content))
        {
            _portDroppedBytes += payload.Length;
            return;
        }

        if (content.Length == 0)
        {
            return;
        }

        _ = _context.Access.Write(spec.AccessName, content);
    }

    private void PublishStatus()
    {
        if (_context is null)
        {
            return;
        }

        var spec = Spec;
        var snapshot = _context.Access.GetSnapshot(spec.AccessName);

        var canOpen = !snapshot.Suspended
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

        var text = Describe(snapshot);
        if (!string.Equals(text, _lastStateText, StringComparison.Ordinal))
        {
            _context.Display.SetText(spec.StateSource, text);
            _lastStateText = text;
        }

        var role = LinkRole(snapshot);
        if (!string.Equals(role, _lastLinkRole, StringComparison.Ordinal))
        {
            _context.Appearance.SetOutlineRole(spec.LinkSource, role);
            _lastLinkRole = role;
        }

        var dropped = Math.Max(0, snapshot.DroppedBytes + _portDroppedBytes);

        PublishU64(_context, spec.RxCountSource, ToUInt64(snapshot.ReceivedBytes));
        PublishU64(_context, spec.TxCountSource, ToUInt64(snapshot.SentBytes));
        PublishU64(_context, spec.DropCountSource, ToUInt64(dropped));

        if (spec.ConnCountSource is { Length: > 0 } conn)
        {
            PublishU32(_context, conn, (uint)Math.Max(0, snapshot.ActiveConnections));
        }
    }

    /// <summary>
    /// 从框架登记的**参数类型**反射配置字段名（用于会话打开/故障时禁用编辑）。
    /// </summary>
    /// <remarks>
    /// 参数类型在模块注册时可能还没挂上（外部接入面尚未接线）⇒ 返回空并保持未解析，
    /// 下一次状态发布继续尝试；这不是错误。
    /// </remarks>
    private string[] ResolveConfigurationFields()
    {
        if (_configurationFieldsResolved || _context is null)
        {
            return _configurationFields;
        }

        if (_context.Access.GetParametersType(Spec.AccessName) is not { } parametersType)
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
        _context?.Appearance.SetEnabled(source, enabled);
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
