using System.Buffers.Binary;
using System.Globalization;
using System.Text.Json;
using HYWDebugAssistant.Sdk;
using HYWDebugAssistant.Sdk.Contracts;
using HYWDebugAssistant.Sdk.Context;
using HYWDebugAssistant.Sdk.Lifecycle;
using HYWDebugAssistant.Sdk.Ports;
using HYWDebugAssistant.Sdk.Reporting;
using HYWDebugAssistant.Sdk.Text;
using HYWDebugAssistant.Sdk.Timing;
using HYWDebugAssistant.Sdk.Values;
using HYWDebugAssistant.Sdk.Views;

namespace HYWDebugAssistant.Modules.CommandSender;

/// <summary>槽位里的六个值。</summary>
public enum SlotField
{
    /// <summary>标签。</summary>
    Label = 0,

    /// <summary>内容。</summary>
    Content = 1,

    /// <summary>HEX 开关。</summary>
    Hex = 2,

    /// <summary>Enter 写 CR。</summary>
    Cr = 3,

    /// <summary>Enter 写 LF。</summary>
    Lf = 4,

    /// <summary>派生的换行档。</summary>
    Newline = 5,
}

/// <summary>
/// **指令发送器（多组字符串发送器）**：一个列表、每条一组文本/HEX、按勾选顺序单发/批量/连续发送。
/// </summary>
/// <remarks>
/// <para>
/// **行数据全部是声明值**：固定 256 个槽位 × 6 个值；`order` 记录活动槽位，`selection` 记录勾选顺序，
/// `search`/`interval`/`continuous` 是普通值。没有模块配置，也没有新的数据通道。
/// </para>
/// <para>
/// 行内动作（单发、删除）走已有输入 JSON（<c>action</c>）；持久化、红框、实时校验、按钮禁用
/// 全部由值机制与 <see cref="InputValidity"/> 吸收。
/// </para>
/// </remarks>
[ModuleDefinition(
    "hyw",
    "command-sender",
    DisplayNameKey = CommandSenderText.Title,
    DescriptionKey = CommandSenderText.Description,
    Tags = ["source", "command", "sender"],
    Capabilities = ModuleCapability.File,
    ViewFactory = typeof(Views.CommandSenderViewFactory))]
public sealed class CommandSenderModule : IModule, IActivitySource
{
    /// <summary>行槽位上限（值名注册期固定；只有活动槽位写视图状态）。</summary>
    public const int MaxSlots = 256;

    /// <summary>输出端口名。</summary>
    public const string OutPort = "out";

    /// <summary>角色标识。</summary>
    public const string RoleId = "command-send";

    /// <summary>数据类型标识（与两个发送器/监视器同型）。</summary>
    public const string RawBytesTypeId = "RawBytes";

    /// <summary>单包有效字节上限。</summary>
    public const int TextCapacity = 1472;

    /// <summary>连续发送间隔上限。</summary>
    public const int MaxIntervalMs = 86_400_000;

    /// <summary>活动槽位顺序值名。</summary>
    public const string OrderValue = "order";

    /// <summary>勾选顺序值名。</summary>
    public const string SelectionValue = "selection";

    /// <summary>搜索值名。</summary>
    public const string SearchValue = "search";

    /// <summary>间隔值名。</summary>
    public const string IntervalValue = "interval";

    /// <summary>连续开关值名。</summary>
    public const string ContinuousValue = "continuous";

    /// <summary>合并发送开关值名。</summary>
    public const string MergeValue = "merge";

    /// <summary>发送进行中闸门值名（行内发送按钮总闸）。</summary>
    public const string SendingValue = "sending";

    /// <summary>已发送数据包数显示源。</summary>
    public const string SentSource = "sent";

    /// <summary>状态句显示源。</summary>
    public const string StatusSource = "status";

    /// <summary>行内动作输入源。</summary>
    public const string ActionSource = "action";

    /// <summary>导入路径输入源。</summary>
    public const string ImportPathSource = "importPath";

    /// <summary>导出路径输入源。</summary>
    public const string ExportPathSource = "exportPath";

    /// <summary>新建命令。</summary>
    public const string AddCommand = "add";

    /// <summary>全部发送命令。</summary>
    public const string SendAllCommand = "sendAll";

    /// <summary>导入命令。</summary>
    public const string ImportCommand = "import";

    /// <summary>另存为命令。</summary>
    public const string SaveAsCommand = "saveAs";

    /// <summary>发送动作键。</summary>
    public const string ActionSend = "send";

    /// <summary>删除动作键。</summary>
    public const string ActionDelete = "delete";

    private static readonly JsonSerializerOptions DocumentJson = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
        WriteIndented = true,
    };

    private readonly List<int> _order = [];
    private readonly List<int> _selection = [];
    private readonly HashSet<int> _active = [];
    private readonly Dictionary<int, SlotState> _slots = [];
    private readonly InputValidity _validity = new();
    private readonly List<int> _pendingSends = [];

    private IModuleContext? _context;
    private IModuleTimer? _timer;
    private string? _lastOrderText;
    private string? _lastSelectionText;
    private string? _lastGateSignature;
    private string? _shownStatus;
    private string? _intervalText;
    private int? _intervalMs;
    private IntervalState _intervalState;
    private bool _continuous;
    private bool? _continuousRequest;
    private bool _merge;
    private bool _sendAllRequested;
    private SendSequence? _sequence;
    private int _cycleIndex;
    private bool _started;
    private bool _actionStatusShown;
    private ulong _sent;
    private bool? _lastSendAllEnabled;
    private bool? _lastContinuousEnabled;
    private bool? _lastAddEnabled;

    /// <summary>槽位值名（模块声明与视图建行共用）。</summary>
    /// <param name="slot">槽位号。</param>
    /// <param name="field">字段。</param>
    /// <returns>值名。</returns>
    public static string SlotValue(int slot, SlotField field) =>
        "cmd" + slot.ToString(CultureInfo.InvariantCulture) + field;

    /// <summary>当前活动槽位顺序（只读快照，测试用）。</summary>
    public IReadOnlyList<int> Order => _order;

    /// <summary>当前勾选顺序（只读快照，测试用）。</summary>
    public IReadOnlyList<int> Selection => _selection;

    /// <summary>当前是否连续发送。</summary>
    public bool IsContinuous => _continuous;

    /// <summary>当前是否勾选“合并发送”。</summary>
    public bool IsMerged => _merge;

    /// <summary>当前是否处于自动发送（逐条序列或连续发送）。</summary>
    public bool IsSending => _sequence is not null || _continuous;

    /// <summary>当前生效的间隔（毫秒）。</summary>
    public int? IntervalMilliseconds => _intervalMs;

    /// <inheritdoc />
    public ReceiveMode Receive => ReceiveMode.Push;

    /// <inheritdoc />
    public void OnRegistered(IModuleContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        _context = context;

        context.Declaration.DeclareDataType(new DataTypeDeclaration(RawBytesTypeId, Shape)
        {
            Metering = new MeteringBlock { PayloadLengthField = "len" },
        });

        context.Declaration.DeclareRole(RoleDeclaration.Output(
            RoleId,
            RawBytesTypeId,
            OutPort,
            Tr(CommandSenderText.RoleSend)));

        DeclareUi(context);

        _timer = context.Timers.Create();
        _timer.Tick += OnTimerTick;
    }

    /// <inheritdoc />
    public void OnStep(IModuleContext context, StepReason reason)
    {
        ArgumentNullException.ThrowIfNull(context);
        _context = context;

        var start = (reason & StepReason.Start) != 0;

        if (start)
        {
            _sent = 0;
            _shownStatus = null;
            PublishSent(context);
            EnsureInitialState(context);
        }

        _sendAllRequested = false;
        _actionStatusShown = false;
        DrainInputs(context);
        DrainCommands(context);

        SyncOrder(context);
        SyncSelection(context);
        SyncInterval(context);
        SyncMerge(context);
        SyncSlots(context, start);
        ApplyContinuousRequest(context);
        SyncGates(context);

        if (_sendAllRequested)
        {
            StartSendAll(context);
        }

        foreach (var slot in _pendingSends)
        {
            if (_sequence is null && !_continuous)
            {
                _ = SendSlot(context, slot, out _);
            }
        }

        _pendingSends.Clear();

        if (start && _shownStatus is null)
        {
            PublishStatus(context, Tr(CommandSenderText.StatusReady));
        }
    }

    /// <summary>发送一条槽位（公开给模块级测试；非法/非活动返回 false）。</summary>
    /// <param name="context">上下文。</param>
    /// <param name="slot">槽位号。</param>
    /// <param name="packets">实际发出的数据包数。</param>
    /// <returns>是否至少发出一个包。</returns>
    public bool SendSlot(IModuleContext context, int slot, out int packets)
    {
        ArgumentNullException.ThrowIfNull(context);
        packets = 0;

        if (!_order.Contains(slot))
        {
            return false;
        }

        if (FindInvalid([slot]) is { } invalid)
        {
            PublishStatus(context, InvalidItemMessage(context, invalid));
            return false;
        }

        return SendSlots(context, [slot], publishStatus: true, out packets, out _);
    }

    /// <summary>点“全部发送”：合并模式一次发出；逐条模式启动序列。</summary>
    private void StartSendAll(IModuleContext context)
    {
        if (_sequence is not null || _continuous)
        {
            return;
        }

        var selected = ActiveSelection();
        if (selected.Count == 0)
        {
            PublishStatus(context, Tr(CommandSenderText.StatusNoSelection));
            return;
        }

        if (FindInvalid(selected) is { } invalid)
        {
            PublishStatus(context, InvalidItemMessage(context, invalid));
            return;
        }

        if (_merge)
        {
            _ = SendMerged(context, selected, publishStatus: true, out _, out _);
            return;
        }

        if (selected.Count == 1)
        {
            _ = SendSlots(context, selected, publishStatus: true, out _, out _);
            return;
        }

        if (_intervalState != IntervalState.Usable || _intervalMs is not > 0)
        {
            PublishStatus(context, Tr(CommandSenderText.StatusSequenceIntervalRequired, MaxIntervalMs));
            return;
        }

        _sequence = new SendSequence(selected, _intervalMs.Value);
        PublishStatus(context, Tr(
            CommandSenderText.StatusSequenceStarted,
            selected.Count,
            _intervalMs.Value));
        SyncSending(context);
        AdvanceSequence(context);
        SyncGates(context);
    }

    /// <summary>发送序列推进一条；完成时收尾，结构变化/非法时取消。</summary>
    private void AdvanceSequence(IModuleContext context)
    {
        if (_sequence is not { } sequence)
        {
            return;
        }

        if (sequence.Index >= sequence.Slots.Count)
        {
            FinishSequence(context);
            return;
        }

        var slot = sequence.Slots[sequence.Index++];

        if (!_active.Contains(slot) || !_order.Contains(slot))
        {
            CancelSequence(context, Tr(CommandSenderText.StatusSequenceCanceled));
            return;
        }

        if (FindInvalid([slot]) is { } invalid)
        {
            CancelSequence(context, InvalidItemMessage(context, invalid));
            return;
        }

        _ = SendSlots(context, [slot], publishStatus: false, out var packets, out var failed);
        sequence.Packets += packets;
        sequence.Failed += failed;

        if (sequence.Index >= sequence.Slots.Count)
        {
            FinishSequence(context);
            return;
        }

        SyncTimer(context);
    }

    /// <summary>逐条序列完成：恢复按钮，发布汇总。</summary>
    private void FinishSequence(IModuleContext context)
    {
        if (_sequence is not { } sequence)
        {
            return;
        }

        _sequence = null;
        SyncSending(context);
        SyncTimer(context);
        _lastGateSignature = null;
        SyncGates(context);

        if (sequence.Failed > 0)
        {
            PublishStatus(context, Tr(CommandSenderText.StatusSentFailed, sequence.Packets, sequence.Failed));
        }
        else if (sequence.Packets > 0)
        {
            PublishStatus(context, Tr(CommandSenderText.StatusSent, sequence.Packets, sequence.Slots.Count));
        }
        else
        {
            PublishStatus(context, Tr(CommandSenderText.StatusReady));
        }
    }

    /// <summary>逐条序列取消：恢复按钮并说明原因。</summary>
    private void CancelSequence(IModuleContext context, string reason)
    {
        if (_sequence is null)
        {
            return;
        }

        _sequence = null;
        SyncSending(context);
        SyncTimer(context);
        _lastGateSignature = null;
        SyncGates(context);
        PublishStatus(context, reason);
    }

    /// <summary>把全部勾选项合并成一次逻辑发送；返回是否至少发出一个包。</summary>
    private bool SendMerged(
        IModuleContext context,
        List<int> slots,
        bool publishStatus,
        out int packets,
        out int failed)
    {
        packets = 0;
        failed = 0;
        var bytes = new List<byte>();

        foreach (var slot in slots)
        {
            var content = Read(context, SlotValue(slot, SlotField.Content));
            var hex = ReadBool(context, SlotValue(slot, SlotField.Hex), fallback: false);

            if (!CommandSenderCodec.TryEncodeContent(content, hex, out var encoded, out var failure))
            {
                PublishStatus(context, Tr(
                    CommandSenderText.StatusInvalidItem,
                    SlotOrdinal(slot),
                    SlotLabel(context, slot),
                    failure ?? string.Empty));
                return false;
            }

            bytes.AddRange(encoded);
        }

        if (bytes.Count == 0)
        {
            return false;
        }

        var payloads = CommandSenderCodec.BuildMergedPackets(bytes.ToArray(), TextCapacity);

        foreach (var payload in payloads)
        {
            if (context.Ports.TrySend(OutPort, payload))
            {
                packets++;
            }
            else
            {
                failed++;
            }
        }

        if (packets > 0)
        {
            _sent += (ulong)packets;
            PublishSent(context);
        }

        if (publishStatus)
        {
            if (failed > 0)
            {
                PublishStatus(context, Tr(CommandSenderText.StatusSentFailed, packets, failed));
            }
            else if (packets > 0)
            {
                PublishStatus(context, Tr(CommandSenderText.StatusSent, packets, slots.Count));
            }
        }

        return packets > 0;
    }

    /// <summary>导入指令集 JSON（公开给模块级测试）；失败不改当前列表。</summary>
    /// <param name="context">上下文。</param>
    /// <param name="json">JSON 文本。</param>
    /// <param name="error">失败原因。</param>
    /// <returns>是否成功。</returns>
    public bool ImportJson(IModuleContext context, string json, out string? error)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(json);
        error = null;

        CommandSetDocument? document;

        try
        {
            document = JsonSerializer.Deserialize<CommandSetDocument>(json, DocumentJson);
        }
        catch (JsonException exception)
        {
            error = Tr(CommandSenderText.StatusImportParse, exception.Message);
            return false;
        }
        catch (NotSupportedException exception)
        {
            error = Tr(CommandSenderText.StatusImportParse, exception.Message);
            return false;
        }

        if (document is null)
        {
            error = Tr(CommandSenderText.StatusImportParse, string.Empty);
            return false;
        }

        if (!string.Equals(document.Format, "hyw/command-sender", StringComparison.Ordinal))
        {
            error = Tr(CommandSenderText.StatusImportFormat);
            return false;
        }

        if (document.Version != 1)
        {
            error = Tr(CommandSenderText.StatusImportVersion, document.Version);
            return false;
        }

        if (document.Commands is null)
        {
            error = Tr(CommandSenderText.StatusImportParse, string.Empty);
            return false;
        }

        var commands = document.Commands;

        if (commands.Count > MaxSlots)
        {
            error = Tr(CommandSenderText.StatusImportTooMany, MaxSlots);
            return false;
        }

        CancelSequence(context, Tr(CommandSenderText.StatusSequenceCanceled));

        foreach (var slot in _order.ToArray())
        {
            ClearSlot(context, slot);
        }

        _order.Clear();
        _active.Clear();
        _slots.Clear();
        _selection.Clear();
        _pendingSends.Clear();
        _validity.ClearAll(context);
        _intervalText = null;

        for (var index = 0; index < commands.Count; index++)
        {
            var command = commands[index];
            var slot = index;
            var hex = command.Hex ?? false;
            var cr = command.Cr ?? true;
            var lf = command.Lf ?? true;

            WriteValue(context, SlotValue(slot, SlotField.Label), command.Label ?? string.Empty);
            WriteValue(context, SlotValue(slot, SlotField.Content), command.Content ?? string.Empty);
            WriteBool(context, SlotValue(slot, SlotField.Hex), hex);
            WriteBool(context, SlotValue(slot, SlotField.Cr), cr);
            WriteBool(context, SlotValue(slot, SlotField.Lf), lf);
            WriteValue(
                context,
                SlotValue(slot, SlotField.Newline),
                TextNewlines.ChoiceOf(TextNewlines.FromCheckboxes(cr, lf)));

            _order.Add(slot);
            _active.Add(slot);
        }

        WriteOrder(context);
        WriteSelection(context, []);
        _cycleIndex = 0;
        _lastGateSignature = null;

        if (document.IntervalMs is { } interval && interval is >= 1 and <= MaxIntervalMs)
        {
            WriteValue(context, IntervalValue, interval.ToString(CultureInfo.InvariantCulture));
        }

        PublishActionStatus(context, Tr(CommandSenderText.StatusImported, commands.Count));
        return true;
    }

    /// <summary>导出当前指令集 JSON。</summary>
    /// <returns>JSON 文本。</returns>
    public string ExportJson()
    {
        var context = _context ?? throw new InvalidOperationException("module is not registered");

        var document = new CommandSetDocument
        {
            Format = "hyw/command-sender",
            Version = 1,
            IntervalMs = _intervalState == IntervalState.Usable ? _intervalMs : null,
            Commands = [.. _order.Select(slot => new CommandDocument
            {
                Label = Read(context, SlotValue(slot, SlotField.Label)),
                Content = Read(context, SlotValue(slot, SlotField.Content)),
                Hex = ReadBool(context, SlotValue(slot, SlotField.Hex), fallback: false),
                Cr = ReadBool(context, SlotValue(slot, SlotField.Cr), fallback: true),
                Lf = ReadBool(context, SlotValue(slot, SlotField.Lf), fallback: true),
            })],
        };

        return JsonSerializer.Serialize(document, DocumentJson);
    }

    /// <inheritdoc />
    public void OnRemoved()
    {
        _timer?.Dispose();
        _timer = null;
    }

    // ---------------------------------------------------------------- 声明

    private static ContractShape Shape { get; } = ContractShape.OfStruct(
        ("len", ContractShape.OfPrimitive(ContractPrimitive.U16)),
        ("data", ContractShape.OfBytes(TextCapacity)));

    private static void DeclareUi(IModuleContext context)
    {
        for (var slot = 0; slot < MaxSlots; slot++)
        {
            DeclareText(context, SlotValue(slot, SlotField.Label), persistent: true);
            DeclareText(context, SlotValue(slot, SlotField.Content), persistent: true);
            DeclareBool(context, SlotValue(slot, SlotField.Hex));
            DeclareBool(context, SlotValue(slot, SlotField.Cr));
            DeclareBool(context, SlotValue(slot, SlotField.Lf));
            DeclareText(context, SlotValue(slot, SlotField.Newline), persistent: false);
        }

        DeclareText(context, OrderValue, persistent: true);
        DeclareText(context, SelectionValue, persistent: true);
        DeclareText(context, SearchValue, persistent: false);

        context.Declaration.DeclareValue(new ValueDeclaration(IntervalValue)
        {
            Editor = ValueEditor.Number,
            Persistent = true,
            ResetWithModule = true,
        });

        context.Declaration.DeclareValue(new ValueDeclaration(ContinuousValue)
        {
            Editor = ValueEditor.Boolean,
            Persistent = false,
            ResetWithModule = true,
        });

        context.Declaration.DeclareValue(new ValueDeclaration(MergeValue)
        {
            Editor = ValueEditor.Boolean,
            Persistent = false,
            ResetWithModule = true,
        });

        context.Declaration.DeclareValue(new ValueDeclaration(SendingValue)
        {
            Editor = ValueEditor.Boolean,
            Persistent = false,
            ResetWithModule = true,
        });

        context.Declaration.DeclareDisplaySource(
            new DisplaySourceDeclaration(SentSource, ViewDataSourceKind.Value, ContractPrimitive.U64));
        context.Declaration.DeclareDisplaySource(
            new DisplaySourceDeclaration(StatusSource, ViewDataSourceKind.Text));

        context.Declaration.DeclareCommand(AddCommand, Tr(CommandSenderText.Add));
        context.Declaration.DeclareCommand(SendAllCommand, Tr(CommandSenderText.SendAll));
        context.Declaration.DeclareCommand(ImportCommand, Tr(CommandSenderText.Import));
        context.Declaration.DeclareCommand(SaveAsCommand, Tr(CommandSenderText.SaveAs));

        context.Declaration.DeclareInput(ActionSource);
        context.Declaration.DeclareInput(ImportPathSource);
        context.Declaration.DeclareInput(ExportPathSource);
    }

    private static void DeclareText(IModuleContext context, string name, bool persistent) =>
        context.Declaration.DeclareValue(new ValueDeclaration(name)
        {
            Editor = ValueEditor.Text,
            Persistent = persistent,
            ResetWithModule = false,
        });

    private static void DeclareBool(IModuleContext context, string name) =>
        context.Declaration.DeclareValue(new ValueDeclaration(name)
        {
            Editor = ValueEditor.Boolean,
            Persistent = true,
            ResetWithModule = false,
        });

    // ---------------------------------------------------------------- 收件箱

    private void DrainInputs(IModuleContext context)
    {
        while (context.Inbox.TryDequeueInput(out var source, out var text))
        {
            switch (source)
            {
                case ActionSource:
                    HandleAction(context, text);
                    break;
                case ContinuousValue:
                    _continuousRequest = string.Equals(text, "true", StringComparison.OrdinalIgnoreCase);
                    break;
                case ImportPathSource:
                    ImportFromPath(context, text);
                    break;
                case ExportPathSource:
                    ExportToPath(context, text);
                    break;
                default:
                    if (TryParseSlotField(source, out var slot, out var field))
                    {
                        if (field == SlotField.Hex)
                        {
                            HandleHexToggle(
                                context,
                                slot,
                                string.Equals(text, "true", StringComparison.OrdinalIgnoreCase));
                        }
                        else if (field is SlotField.Cr or SlotField.Lf)
                        {
                            SyncNewline(context, slot);
                        }
                    }

                    break;
            }
        }
    }

    private void DrainCommands(IModuleContext context)
    {
        while (context.Inbox.TryDequeueCommand(out var command))
        {
            switch (command)
            {
                case AddCommand:
                    AddSlot(context);
                    break;
                case SendAllCommand:
                    _sendAllRequested = true;
                    break;
                case ImportCommand:
                    RequestImport(context);
                    break;
                case SaveAsCommand:
                    RequestExport(context);
                    break;
                default:
                    PublishStatus(context, Tr(CommandSenderText.StatusUnknownCommand, command));
                    break;
            }
        }
    }

    // ---------------------------------------------------------------- 初始与同步

    private void EnsureInitialState(IModuleContext context)
    {
        if (_started)
        {
            return;
        }

        _started = true;

        var orderText = Read(context, OrderValue);
        var order = ParseSlots(orderText);

        if (order.Count == 0)
        {
            SetSlotDefaults(context, 0, 1);
            _order.Add(0);
            _active.Add(0);
            WriteOrder(context);
        }
        else
        {
            _order.AddRange(order);
            _active.Clear();

            foreach (var slot in _order)
            {
                _active.Add(slot);
            }

            var normalized = SerializeSlots(_order);
            if (!string.Equals(normalized, orderText, StringComparison.Ordinal))
            {
                WriteOrder(context);
            }
            else
            {
                _lastOrderText = orderText;
            }
        }

        var selectionText = Read(context, SelectionValue);
        _selection.AddRange(ParseSlots(selectionText).Where(_active.Contains));
        _lastSelectionText = selectionText;
    }

    private void SyncOrder(IModuleContext context)
    {
        var text = Read(context, OrderValue);

        if (!string.Equals(text, _lastOrderText, StringComparison.Ordinal))
        {
            _lastOrderText = text;
            var order = ParseSlots(text);

            if (!order.SequenceEqual(_order))
            {
                CancelSequence(context, Tr(CommandSenderText.StatusSequenceCanceled));
                _order.Clear();
                _order.AddRange(order);
                _active.Clear();

                foreach (var slot in _order)
                {
                    _active.Add(slot);
                }

                foreach (var slot in _slots.Keys.Where(slot => !_active.Contains(slot)).ToArray())
                {
                    _ = _slots.Remove(slot);
                }

                _cycleIndex = 0;
                _lastGateSignature = null;
            }

            var normalized = SerializeSlots(_order);
            if (!string.Equals(normalized, text, StringComparison.Ordinal))
            {
                WriteOrder(context);
            }
        }
    }

    private void SyncSelection(IModuleContext context)
    {
        var text = Read(context, SelectionValue);

        if (string.Equals(text, _lastSelectionText, StringComparison.Ordinal))
        {
            return;
        }

        _lastSelectionText = text;

        if (_sequence is not null)
        {
            CancelSequence(context, Tr(CommandSenderText.StatusSequenceCanceled));
        }

        _selection.Clear();
        _selection.AddRange(ParseSlots(text).Where(_active.Contains));
        _lastGateSignature = null;
    }

    private void SyncInterval(IModuleContext context)
    {
        var text = Read(context, IntervalValue);

        if (string.Equals(text, _intervalText, StringComparison.Ordinal))
        {
            return;
        }

        _intervalText = text;

        if (string.IsNullOrWhiteSpace(text))
        {
            _intervalState = IntervalState.Empty;
            _validity.Set(context, IntervalValue, false);
            _lastGateSignature = null;
            return;
        }

        if (uint.TryParse(
                text.Trim(),
                NumberStyles.None,
                CultureInfo.InvariantCulture,
                out var milliseconds)
            && milliseconds <= MaxIntervalMs)
        {
            _intervalState = IntervalState.Usable;
            _intervalMs = (int)milliseconds;
            _validity.Set(context, IntervalValue, false);
        }
        else
        {
            _intervalState = IntervalState.Invalid;
            _validity.Set(
                context,
                IntervalValue,
                true,
                Tr(CommandSenderText.StatusIntervalInvalid, MaxIntervalMs),
                ValueValidityOrigin.Format);
        }

        _lastGateSignature = null;
    }

    private void SyncMerge(IModuleContext context)
    {
        var merge = ReadBool(context, MergeValue, fallback: false);

        if (merge == _merge)
        {
            return;
        }

        _merge = merge;
        _cycleIndex = 0;
        _lastGateSignature = null;
    }

    private void SyncSlots(IModuleContext context, bool start)
    {
        foreach (var slot in _order)
        {
            var contentName = SlotValue(slot, SlotField.Content);
            var hexName = SlotValue(slot, SlotField.Hex);
            var crName = SlotValue(slot, SlotField.Cr);
            var lfName = SlotValue(slot, SlotField.Lf);
            var newlineName = SlotValue(slot, SlotField.Newline);

            var content = Read(context, contentName);
            var hex = ReadBool(context, hexName, fallback: false);
            var cr = ReadBool(context, crName, fallback: true);
            var lf = ReadBool(context, lfName, fallback: true);
            var newline = TextNewlines.ChoiceOf(TextNewlines.FromCheckboxes(cr, lf));

            WriteValue(context, newlineName, newline);

            if (start
                || !_slots.TryGetValue(slot, out var previous)
                || previous.Hex != hex)
            {
                context.Appearance.SetEnabled(crName, !hex);
                context.Appearance.SetEnabled(lfName, !hex);
            }

            var error = hex && !CommandSenderCodec.TryParseHex(content, out _, out var failure)
                ? failure
                : null;

            _slots[slot] = new SlotState { Hex = hex, Error = error };

            if (error is null)
            {
                _validity.Set(context, contentName, false);
            }
            else
            {
                _validity.Set(context, contentName, true, error, ValueValidityOrigin.Format);
            }
        }
    }

    private void ApplyContinuousRequest(IModuleContext context)
    {
        if (_continuousRequest is not { } want)
        {
            return;
        }

        _continuousRequest = null;

        if (!want)
        {
            _continuous = false;
            SyncSending(context);
            SyncTimer(context);
            PublishStatus(context, Tr(CommandSenderText.StatusContinuousStopped));
            return;
        }

        if (_sequence is not null)
        {
            _continuous = false;
            WriteBool(context, ContinuousValue, false);
            return;
        }

        if (!CanContinuous(context, out _, out var reason))
        {
            _continuous = false;
            WriteBool(context, ContinuousValue, false);
            SyncSending(context);
            SyncTimer(context);
            PublishStatus(context, reason);
            return;
        }

        _continuous = true;
        _cycleIndex = 0;
        SyncSending(context);
        SyncTimer(context);
        PublishStatus(context, _merge
            ? Tr(CommandSenderText.StatusContinuousStartedMerged, _intervalMs ?? 0)
            : Tr(CommandSenderText.StatusContinuousStarted, _intervalMs ?? 0));
    }

    private void SyncGates(IModuleContext context)
    {
        var selected = ActiveSelection();
        var invalid = FindInvalid(selected);
        var requiresInterval = !_merge && selected.Count > 1;
        var intervalReady = !requiresInterval
            || (_intervalState == IntervalState.Usable && _intervalMs is > 0);
        var sendable = selected.Count > 0 && invalid is null;
        var canContinuous = sendable
            && _intervalState == IntervalState.Usable
            && _intervalMs is > 0
            && _sequence is null;

        if (_continuous && !canContinuous)
        {
            _continuous = false;
            WriteBool(context, ContinuousValue, false);

            if (!_actionStatusShown)
            {
                PublishStatus(context, StopReason(context));
            }
        }

        var canSendAll = sendable
            && intervalReady
            && !_continuous
            && _sequence is null;

        SetEnabled(context, SendAllCommand, canSendAll, ref _lastSendAllEnabled);
        SetEnabled(context, ContinuousValue, canContinuous, ref _lastContinuousEnabled);
        SetEnabled(context, AddCommand, _order.Count < MaxSlots, ref _lastAddEnabled);

        SyncSending(context);
        SyncTimer(context);

        var signature = string.Join(',', selected)
            + "|" + (invalid?.ToString(CultureInfo.InvariantCulture) ?? string.Empty)
            + "|" + _intervalState
            + "|" + (_intervalMs?.ToString(CultureInfo.InvariantCulture) ?? string.Empty)
            + "|" + (_merge ? "merge" : "single");

        if (string.Equals(signature, _lastGateSignature, StringComparison.Ordinal))
        {
            return;
        }

        _lastGateSignature = signature;

        if (_actionStatusShown || _sequence is not null)
        {
            return;
        }

        if (selected.Count == 0)
        {
            PublishStatus(context, Tr(CommandSenderText.StatusNoSelection));
        }
        else if (invalid is { } bad)
        {
            PublishStatus(context, InvalidItemMessage(context, bad));
        }
        else if (!intervalReady)
        {
            PublishStatus(context, Tr(CommandSenderText.StatusSequenceIntervalRequired, MaxIntervalMs));
        }
        else
        {
            PublishStatus(context, Tr(CommandSenderText.StatusReady));
        }
    }

    private bool CanContinuous(IModuleContext context, out int? invalidSlot, out string reason)
    {
        invalidSlot = null;
        var selected = ActiveSelection();

        if (selected.Count == 0)
        {
            reason = Tr(CommandSenderText.StatusNoSelection);
            return false;
        }

        invalidSlot = FindInvalid(selected);

        if (invalidSlot is { } bad)
        {
            reason = InvalidItemMessage(context, bad);
            return false;
        }

        switch (_intervalState)
        {
            case IntervalState.Empty:
                reason = Tr(CommandSenderText.StatusIntervalEmpty);
                return false;
            case IntervalState.Invalid:
                reason = Tr(CommandSenderText.StatusIntervalInvalidContinuous, MaxIntervalMs);
                return false;
            default:
                if (_intervalMs is not > 0)
                {
                    reason = Tr(CommandSenderText.StatusIntervalZero);
                    return false;
                }

                reason = string.Empty;
                return true;
        }
    }

    private string StopReason(IModuleContext context)
    {
        var selected = ActiveSelection();
        if (selected.Count == 0)
        {
            return Tr(CommandSenderText.StatusNoSelection);
        }

        if (FindInvalid(selected) is { } invalid)
        {
            return InvalidItemMessage(context, invalid);
        }

        return _intervalState switch
        {
            IntervalState.Empty => Tr(CommandSenderText.StatusIntervalEmpty),
            IntervalState.Invalid => Tr(CommandSenderText.StatusIntervalInvalidContinuous, MaxIntervalMs),
            _ => Tr(CommandSenderText.StatusIntervalZero),
        };
    }

    // ---------------------------------------------------------------- 连续

    private void OnTimerTick(object? sender, EventArgs e)
    {
        if (_context is not { } context)
        {
            return;
        }

        if (_sequence is not null)
        {
            AdvanceSequence(context);
            return;
        }

        if (!_continuous)
        {
            return;
        }

        SendContinuous(context);
    }

    private void SendContinuous(IModuleContext context)
    {
        var selected = ActiveSelection();

        if (selected.Count == 0)
        {
            StopContinuous(context, Tr(CommandSenderText.StatusNoSelection));
            return;
        }

        if (_merge)
        {
            if (FindInvalid(selected) is { } mergedInvalid)
            {
                StopContinuous(context, InvalidItemMessage(context, mergedInvalid));
                return;
            }

            _ = SendMerged(context, selected, publishStatus: false, out _, out var mergedFailed);

            if (mergedFailed > 0)
            {
                PublishStatus(context, Tr(CommandSenderText.StatusSendFailed, mergedFailed));
            }

            return;
        }

        if (_cycleIndex >= selected.Count)
        {
            _cycleIndex = 0;
        }

        var slot = selected[_cycleIndex];
        _cycleIndex = (_cycleIndex + 1) % selected.Count;

        if (FindInvalid([slot]) is { } invalid)
        {
            StopContinuous(context, InvalidItemMessage(context, invalid));
            return;
        }

        _ = SendSlots(context, [slot], publishStatus: false, out _, out var failed);

        if (failed > 0)
        {
            PublishStatus(context, Tr(CommandSenderText.StatusSendFailed, failed));
        }
    }

    private void StopContinuous(IModuleContext context, string reason)
    {
        _continuous = false;
        WriteBool(context, ContinuousValue, false);
        SyncSending(context);
        SyncTimer(context);
        SyncGates(context);
        PublishStatus(context, reason);
    }

    private void SyncTimer(IModuleContext context)
    {
        _ = context;

        if (_timer is null)
        {
            return;
        }

        if (_sequence is { } sequence)
        {
            var desired = TimeSpan.FromMilliseconds(sequence.IntervalMs);
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

        if (_continuous && _intervalState == IntervalState.Usable && _intervalMs is > 0)
        {
            var desired = TimeSpan.FromMilliseconds(_intervalMs.Value);
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

    // ---------------------------------------------------------------- 发送

    private bool SendSlots(
        IModuleContext context,
        List<int> slots,
        bool publishStatus,
        out int packets,
        out int failed)
    {
        packets = 0;
        failed = 0;

        foreach (var slot in slots)
        {
            var content = Read(context, SlotValue(slot, SlotField.Content));
            var hex = ReadBool(context, SlotValue(slot, SlotField.Hex), fallback: false);

            if (!CommandSenderCodec.TryBuildPackets(
                    content,
                    hex,
                    TextCapacity,
                    out var payloads,
                    out var failure))
            {
                PublishStatus(context, Tr(
                    CommandSenderText.StatusInvalidItem,
                    SlotOrdinal(slot),
                    SlotLabel(context, slot),
                    failure ?? string.Empty));
                return false;
            }

            foreach (var payload in payloads)
            {
                if (context.Ports.TrySend(OutPort, payload))
                {
                    packets++;
                }
                else
                {
                    failed++;
                }
            }
        }

        if (packets > 0)
        {
            _sent += (ulong)packets;
            PublishSent(context);
        }

        if (publishStatus)
        {
            if (failed > 0)
            {
                PublishStatus(context, Tr(CommandSenderText.StatusSentFailed, packets, failed));
            }
            else if (packets > 0)
            {
                PublishStatus(context, Tr(CommandSenderText.StatusSent, packets, slots.Count));
            }
        }

        return packets > 0;
    }

    private void PublishSent(IModuleContext context)
    {
        Span<byte> payload = stackalloc byte[sizeof(ulong)];
        BinaryPrimitives.WriteUInt64LittleEndian(payload, _sent);
        context.Ports.Publish(SentSource, payload);
    }

    // ---------------------------------------------------------------- 新建 / 删除

    private void AddSlot(IModuleContext context)
    {
        CancelSequence(context, Tr(CommandSenderText.StatusSequenceCanceled));

        var firstFree = FirstFreeSlot();

        if (firstFree < 0)
        {
            PublishStatus(context, Tr(CommandSenderText.StatusMaxSlots, MaxSlots));
            return;
        }

        SetSlotDefaults(context, firstFree, _order.Count + 1);
        _order.Add(firstFree);
        _active.Add(firstFree);
        _ = _slots.Remove(firstFree);
        WriteOrder(context);
        _lastGateSignature = null;
    }

    private void DeleteSlot(IModuleContext context, int slot)
    {
        CancelSequence(context, Tr(CommandSenderText.StatusSequenceCanceled));

        if (!_order.Remove(slot))
        {
            return;
        }

        _active.Remove(slot);
        _ = _slots.Remove(slot);
        _ = _selection.Remove(slot);
        _pendingSends.RemoveAll(candidate => candidate == slot);

        WriteOrder(context);
        WriteSelection(context, _selection);
        ClearSlot(context, slot);
        _lastGateSignature = null;
    }

    private int FirstFreeSlot()
    {
        for (var slot = 0; slot < MaxSlots; slot++)
        {
            if (!_order.Contains(slot))
            {
                return slot;
            }
        }

        return -1;
    }

    private static void SetSlotDefaults(IModuleContext context, int slot, int number)
    {
        WriteValue(context, SlotValue(slot, SlotField.Label), Tr(CommandSenderText.DefaultLabel, number));
        WriteValue(context, SlotValue(slot, SlotField.Content), string.Empty);
        WriteBool(context, SlotValue(slot, SlotField.Hex), false);
        WriteBool(context, SlotValue(slot, SlotField.Cr), true);
        WriteBool(context, SlotValue(slot, SlotField.Lf), true);
        WriteValue(context, SlotValue(slot, SlotField.Newline), "crlf");
    }

    private void ClearSlot(IModuleContext context, int slot)
    {
        foreach (var field in Enum.GetValues<SlotField>())
        {
            WriteValue(context, SlotValue(slot, field), null);
        }

        _validity.Set(context, SlotValue(slot, SlotField.Content), false);
    }

    // ---------------------------------------------------------------- HEX 切换

    private void HandleHexToggle(IModuleContext context, int slot, bool hex)
    {
        if (!_active.Contains(slot))
        {
            return;
        }

        var contentName = SlotValue(slot, SlotField.Content);
        var hexName = SlotValue(slot, SlotField.Hex);

        if (hex)
        {
            WriteValue(context, contentName, CommandSenderCodec.ToHexText(Read(context, contentName)));
        }
        else
        {
            var status = CommandSenderCodec.TryFromHexText(
                Read(context, contentName),
                out var text,
                out var failure);

            if (status == HexToTextStatus.InvalidHex)
            {
                WriteBool(context, hexName, true);
                PublishStatus(context, Tr(CommandSenderText.StatusConvertInvalidHex, failure ?? string.Empty));
                SyncSlotUi(context, slot);
                return;
            }

            if (status == HexToTextStatus.NotUtf8)
            {
                WriteBool(context, hexName, true);
                PublishStatus(context, Tr(CommandSenderText.StatusConvertNotUtf8));
                SyncSlotUi(context, slot);
                return;
            }

            WriteValue(context, contentName, text);
        }

        SyncSlotUi(context, slot);
        _lastGateSignature = null;
    }

    private static void SyncSlotUi(IModuleContext context, int slot)
    {
        var hex = ReadBool(context, SlotValue(slot, SlotField.Hex), fallback: false);
        context.Appearance.SetEnabled(SlotValue(slot, SlotField.Cr), !hex);
        context.Appearance.SetEnabled(SlotValue(slot, SlotField.Lf), !hex);
    }

    private static void SyncNewline(IModuleContext context, int slot)
    {
        var cr = ReadBool(context, SlotValue(slot, SlotField.Cr), fallback: true);
        var lf = ReadBool(context, SlotValue(slot, SlotField.Lf), fallback: true);
        WriteValue(
            context,
            SlotValue(slot, SlotField.Newline),
            TextNewlines.ChoiceOf(TextNewlines.FromCheckboxes(cr, lf)));
    }

    // ---------------------------------------------------------------- 动作与文件

    private void HandleAction(IModuleContext context, string text)
    {
        try
        {
            using var document = JsonDocument.Parse(text);
            var root = document.RootElement;
            var op = root.GetProperty("op").GetString();
            var slot = root.GetProperty("slot").GetInt32();

            switch (op)
            {
                case ActionSend:
                    if (_sequence is null && !_continuous && _active.Contains(slot))
                    {
                        _pendingSends.Add(slot);
                    }

                    break;
                case ActionDelete:
                    DeleteSlot(context, slot);
                    break;
                default:
                    PublishStatus(context, Tr(CommandSenderText.StatusUnknownAction, op ?? string.Empty));
                    break;
            }
        }
        catch (Exception exception) when (exception is JsonException or KeyNotFoundException or InvalidOperationException or FormatException)
        {
            PublishStatus(context, Tr(CommandSenderText.StatusUnknownAction, text));
        }
    }

    private static void RequestImport(IModuleContext context) =>
        context.Files.Request(new FilePickRequest(
            ImportPathSource,
            FilePickMode.Open,
            Tr(CommandSenderText.FileFilter),
            "commands.json"));

    private static void RequestExport(IModuleContext context) =>
        context.Files.Request(new FilePickRequest(
            ExportPathSource,
            FilePickMode.Save,
            Tr(CommandSenderText.FileFilter),
            "commands.json"));

    private void ImportFromPath(IModuleContext context, string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return;
        }

        try
        {
            var json = File.ReadAllText(path);
            if (!ImportJson(context, json, out var error))
            {
                PublishActionStatus(context, Tr(CommandSenderText.StatusImportFailed, error ?? string.Empty));
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or NotSupportedException or ArgumentException)
        {
            PublishActionStatus(context, Tr(CommandSenderText.StatusImportFailed, exception.Message));
            ReportFileIssue(context, exception, "import");
        }
    }

    private void ExportToPath(IModuleContext context, string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return;
        }

        try
        {
            File.WriteAllText(path, ExportJson());
            PublishActionStatus(context, Tr(CommandSenderText.StatusExported, Path.GetFileName(path)));
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or NotSupportedException or ArgumentException)
        {
            PublishActionStatus(context, Tr(CommandSenderText.StatusExportFailed, exception.Message));
            ReportFileIssue(context, exception, "export");
        }
    }

    private static void ReportFileIssue(IModuleContext context, Exception exception, string objectPath) =>
        context.Issues.Report(new IssueReport(
            IssueCategory.ModuleError,
            IssueSeverity.Warning,
            exception.Message,
            objectPath));

    // ---------------------------------------------------------------- 工具

    private List<int> ActiveSelection()
    {
        var selected = new List<int>(_selection.Count);

        foreach (var slot in _selection)
        {
            if (_active.Contains(slot))
            {
                selected.Add(slot);
            }
        }

        return selected;
    }

    private int? FindInvalid(List<int> slots)
    {
        foreach (var slot in slots)
        {
            if (_slots.TryGetValue(slot, out var state) && state.Error is not null)
            {
                return slot;
            }
        }

        return null;
    }

    private string InvalidItemMessage(IModuleContext context, int slot) =>
        Tr(
            CommandSenderText.StatusInvalidItem,
            SlotOrdinal(slot),
            SlotLabel(context, slot),
            _slots.TryGetValue(slot, out var state) ? state.Error ?? string.Empty : string.Empty);

    private int SlotOrdinal(int slot)
    {
        var index = _order.IndexOf(slot);
        return index < 0 ? 1 : index + 1;
    }

    private static string SlotLabel(IModuleContext context, int slot)
    {
        var label = Read(context, SlotValue(slot, SlotField.Label));
        return label.Length > 0 ? label : Tr(CommandSenderText.DefaultLabel, slot + 1);
    }

    private void PublishStatus(IModuleContext context, string text)
    {
        if (string.Equals(text, _shownStatus, StringComparison.Ordinal))
        {
            return;
        }

        _shownStatus = text;
        context.Display.SetText(StatusSource, text);
    }

    private void PublishActionStatus(IModuleContext context, string text)
    {
        _actionStatusShown = true;
        PublishStatus(context, text);
    }

    private void WriteOrder(IModuleContext context)
    {
        var json = SerializeSlots(_order);
        WriteValue(context, OrderValue, json);
        _lastOrderText = json;
    }

    private void WriteSelection(IModuleContext context, List<int> selection)
    {
        var json = SerializeSlots(selection);
        WriteValue(context, SelectionValue, json);
        _lastSelectionText = json;
    }

    private static string Read(IModuleContext context, string name) =>
        context.Values.ReadText(name) ?? string.Empty;

    private static bool ReadBool(IModuleContext context, string name, bool fallback) =>
        bool.TryParse(context.Values.ReadText(name), out var value) ? value : fallback;

    private static void WriteValue(IModuleContext context, string name, string? value)
    {
        var current = context.Values.ReadText(name);

        if (!string.Equals(current, value, StringComparison.Ordinal))
        {
            context.Values.AcceptText(name, value);
        }
    }

    private static void WriteBool(IModuleContext context, string name, bool value) =>
        WriteValue(context, name, value ? "true" : "false");

    private static void SetEnabled(
        IModuleContext context,
        string name,
        bool enabled,
        ref bool? last)
    {
        if (last == enabled)
        {
            return;
        }

        last = enabled;
        context.Appearance.SetEnabled(name, enabled);
    }

    /// <summary>行内发送按钮总闸：任何自动发送（逐条序列或连续）进行中都禁用。</summary>
    private void SyncSending(IModuleContext context) =>
        WriteBool(context, SendingValue, _sequence is not null || _continuous);

    private static List<int> ParseSlots(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return [];
        }

        try
        {
            var values = JsonSerializer.Deserialize<int[]>(text);

            return values is null
                ? []
                : [.. values.Where(value => value is >= 0 and < MaxSlots).Distinct()];
        }
        catch (JsonException)
        {
            return [];
        }
    }

    private static string SerializeSlots(List<int> slots) => JsonSerializer.Serialize(slots);

    private static bool TryParseSlotField(string source, out int slot, out SlotField field)
    {
        slot = -1;
        field = default;

        if (!source.StartsWith("cmd", StringComparison.Ordinal))
        {
            return false;
        }

        foreach (var candidate in Enum.GetValues<SlotField>())
        {
            var suffix = candidate.ToString();

            if (!source.EndsWith(suffix, StringComparison.Ordinal)
                || source.Length <= 3 + suffix.Length)
            {
                continue;
            }

            var digits = source.AsSpan(3, source.Length - 3 - suffix.Length);

            if (int.TryParse(digits, NumberStyles.None, CultureInfo.InvariantCulture, out slot)
                && slot is >= 0 and < MaxSlots)
            {
                field = candidate;
                return true;
            }
        }

        return false;
    }

    private static string Tr(string key) =>
        TextCatalog.Get(typeof(CommandSenderModule).Assembly, key);

    private static string Tr(string key, params object?[] args) =>
        TextCatalog.Get(typeof(CommandSenderModule).Assembly, key, args);

    private sealed class SlotState
    {
        public bool Hex { get; init; }

        public string? Error { get; init; }
    }

    /// <summary>一次“逐条发送”的快照：启动时的槽位顺序与间隔不随运行中编辑改变。</summary>
    private sealed class SendSequence
    {
        public SendSequence(List<int> slots, int intervalMs)
        {
            Slots = slots;
            IntervalMs = intervalMs;
        }

        public List<int> Slots { get; }

        public int IntervalMs { get; }

        public int Index { get; set; }

        public int Packets { get; set; }

        public int Failed { get; set; }
    }

    private enum IntervalState
    {
        Empty = 0,
        Usable = 1,
        Invalid = 2,
    }

    private sealed class CommandSetDocument
    {
        public string? Format { get; set; }

        public int Version { get; set; }

        public int? IntervalMs { get; set; }

        public List<CommandDocument>? Commands { get; set; }
    }

    private sealed class CommandDocument
    {
        public string? Label { get; set; }

        public string? Content { get; set; }

        public bool? Hex { get; set; }

        public bool? Cr { get; set; }

        public bool? Lf { get; set; }
    }
}
