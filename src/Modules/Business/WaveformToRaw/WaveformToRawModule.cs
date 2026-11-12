using System.Buffers.Binary;
using System.Text;
using HYWDebugAssistant.Modules.WaveformToRaw.Codec;
using HYWDebugAssistant.Sdk;
using HYWDebugAssistant.Sdk.Contracts;
using HYWDebugAssistant.Sdk.Context;
using HYWDebugAssistant.Sdk.Data;
using HYWDebugAssistant.Sdk.Lifecycle;
using HYWDebugAssistant.Sdk.Ports;
using HYWDebugAssistant.Sdk.Reporting;
using HYWDebugAssistant.Sdk.Timing;
using HYWDebugAssistant.Sdk.Views;

namespace HYWDebugAssistant.Modules.WaveformToRaw;

/// <summary>
/// 波形帧 → 原始数据：把 <c>WaveformFrame</c> 按所选协议编码成 <c>RawBytes</c> 字节流。
/// </summary>
[ModuleDefinition(
    "hyw",
    "waveform-to-raw",
    DisplayNameKey = WaveformToRawText.Title,
    DescriptionKey = WaveformToRawText.Description,
    Tags = ["waveform", "protocol"],
    ParticipatesInDataFlow = true,
    ViewFactory = typeof(Views.WaveformToRawViewFactory))]
public sealed class WaveformToRawModule : IModule, IActivitySource
{
    /// <summary>接收口。</summary>
    public const string InPort = "wave";

    /// <summary>发送口。</summary>
    public const string OutPort = "raw";

    /// <summary>接收角色。</summary>
    public const string RoleIngest = "ingest";

    /// <summary>编码角色。</summary>
    public const string RoleEmit = "emit";

    /// <summary>显示源：最近一帧的通道数。</summary>
    public const string ChannelCountSource = "channelCount";

    /// <summary>显示源：最近一帧的通道组编号。</summary>
    public const string GroupIdSource = "groupId";

    /// <summary>显示源：已转换帧数。</summary>
    public const string FramesSource = "frames";

    /// <summary>显示源：丢弃单元数。</summary>
    public const string DroppedSource = "dropped";

    /// <summary>显示源：状态。</summary>
    public const string StatusSource = "status";

    /// <summary>命令：清空统计。</summary>
    public const string ResetCommand = "reset";

    /// <summary>配置字段名：协议。</summary>
    public const string ProtocolField = nameof(WaveformToRawConfig.Protocol);

    /// <summary>配置字段名：帧头。</summary>
    public const string HeaderField = nameof(WaveformToRawConfig.TextHeader);

    /// <summary>配置字段名：帧尾。</summary>
    public const string FooterField = nameof(WaveformToRawConfig.TextFooter);

    /// <summary>配置字段名：FireWater 前缀。</summary>
    public const string PrefixField = nameof(WaveformToRawConfig.FireWaterPrefix);

    /// <summary>配置字段名：通道组编号。</summary>
    public const string GroupIdField = nameof(WaveformToRawConfig.GroupId);

    /// <summary>配置字段名：仅转换该组。</summary>
    public const string OnlyGroupField = nameof(WaveformToRawConfig.OnlyThisGroup);

    /// <summary>配置字段名：小数位数。</summary>
    public const string DecimalsField = nameof(WaveformToRawConfig.Decimals);

    private IModuleContext? _context;
    private WaveformToRawConfig _config = new();
    private ulong _frames;
    private ulong _dropped;
    private ushort _lastChannelCount;
    private uint _lastGroupId;
    private bool _badConfigReported;
    private string _lastStatus = string.Empty;

    // 编码路径的复用缓冲：稳态下不按帧分配字符数组与字节数组。
    private readonly char[] _textChars = new char[TextFrameCodec.MaxChars];
    private byte[] _encodeBuffer = new byte[TextFrameCodec.MaxChars];

    /// <inheritdoc />
    public ReceiveMode Receive => ReceiveMode.Push;

    /// <inheritdoc />
    public void OnRegistered(IModuleContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        _context = context;
        _config = context.Configuration.Read<WaveformToRawConfig>();

        context.Declaration.DeclareDataType(new DataTypeDeclaration(
            WaveformContract.TypeId,
            WaveformContract.Shape,
            WaveformContract.Semantics)
        {
            Metering = WaveformContract.Metering,
        });

        context.Declaration.DeclareDataType(new DataTypeDeclaration(
            RawBytesContract.TypeId,
            RawBytesContract.Shape,
            TransferSemantics.Stream)
        {
            Metering = RawBytesContract.Metering,
        });

        context.Declaration.DeclarePort(new PortDeclaration(
            InPort,
            PortDirection.Receive,
            WaveformContract.Shape,
            WaveformContract.Semantics)
        {
            TypeId = WaveformContract.TypeId,
            Edge = PortEdge.Top,
        });

        context.Declaration.DeclarePort(new PortDeclaration(
            OutPort,
            PortDirection.Send,
            RawBytesContract.Shape,
            TransferSemantics.Stream)
        {
            TypeId = RawBytesContract.TypeId,
            Edge = PortEdge.Bottom,
        });

        context.Declaration.DeclareRole(RoleDeclaration.Input(
            RoleIngest,
            WaveformContract.TypeId,
            InPort,
            WaveformToRawText.Get(WaveformToRawText.RoleIngest)));

        context.Declaration.DeclareRole(RoleDeclaration.Output(
            RoleEmit,
            RawBytesContract.TypeId,
            OutPort,
            WaveformToRawText.Get(WaveformToRawText.RoleEmit)));

        context.Declaration.DeclareFlow(new FlowDeclaration(RoleIngest, RoleEmit));

        context.Declaration.DeclareDisplaySource(new DisplaySourceDeclaration(
            ChannelCountSource,
            ViewDataSourceKind.Value,
            ContractPrimitive.U16));

        context.Declaration.DeclareDisplaySource(new DisplaySourceDeclaration(
            GroupIdSource,
            ViewDataSourceKind.Value,
            ContractPrimitive.U32));

        context.Declaration.DeclareDisplaySource(new DisplaySourceDeclaration(
            FramesSource,
            ViewDataSourceKind.Value,
            ContractPrimitive.U64));

        context.Declaration.DeclareDisplaySource(new DisplaySourceDeclaration(
            DroppedSource,
            ViewDataSourceKind.Value,
            ContractPrimitive.U64));

        context.Declaration.DeclareDisplaySource(new DisplaySourceDeclaration(
            StatusSource,
            ViewDataSourceKind.Text));

        context.Declaration.DeclareCommand(ResetCommand, WaveformToRawText.Get(WaveformToRawText.Reset));

        context.Ports.SetReceiveHandler(InPort, OnWave);
    }

    /// <inheritdoc />
    public void OnActivated()
    {
        if (_context is not { } context)
        {
            return;
        }

        PublishCounters(context);
        SetStatus(context, HasValidTextConfig()
            ? WaveformToRawText.Get(WaveformToRawText.Ready)
            : WaveformToRawText.Get(WaveformToRawText.BadConfig));
    }

    /// <inheritdoc />
    public void OnConfigurationChanged()
    {
        if (_context is not { } context)
        {
            return;
        }

        _config = context.Configuration.Read<WaveformToRawConfig>();
        _badConfigReported = false;
        SetStatus(context, HasValidTextConfig()
            ? WaveformToRawText.Get(WaveformToRawText.Ready)
            : WaveformToRawText.Get(WaveformToRawText.BadConfig));
    }

    /// <inheritdoc />
    public void OnStep(IModuleContext context, StepReason reason)
    {
        ArgumentNullException.ThrowIfNull(context);

        while (context.Inbox.TryDequeueCommand(out var command))
        {
            if (!string.Equals(command, ResetCommand, StringComparison.Ordinal))
            {
                continue;
            }

            _frames = 0;
            _dropped = 0;
            PublishCounters(context);
            SetStatus(context, WaveformToRawText.Get(WaveformToRawText.ResetDone));
        }

        while (context.Inbox.TryDequeueInput(out _, out _))
        {
            // 本模块没有输入源。
        }
    }

    /// <inheritdoc />
    public void OnRemoved() => _context = null;

    private bool HasValidTextConfig() =>
        _config.Protocol is not WaveformToRawProtocol.TextFrame
        || (!string.IsNullOrEmpty(_config.TextHeader) && !string.IsNullOrEmpty(_config.TextFooter));

    private void OnWave(ReadOnlySpan<byte> payload)
    {
        if (_context is not { } context)
        {
            return;
        }

        if (!PayloadReader.TryCreate(payload, WaveformContract.Layout, out var reader))
        {
            _dropped++;
            PublishCounters(context);
            return;
        }

        var channelCount = reader.ReadU16(WaveformContract.ChannelCountField);
        var groupId = reader.ReadU32(WaveformContract.GroupIdField);
        if (channelCount is < 1 or > WaveformContract.MaxChannels)
        {
            _dropped++;
            PublishCounters(context);
            return;
        }

        if (_config.OnlyThisGroup && groupId != (_config.GroupId > 0 ? unchecked((uint)_config.GroupId) : 1u))
        {
            SetStatus(context, WaveformToRawText.Get(WaveformToRawText.GroupFiltered));
            return;
        }

        var samples = reader.ReadBytes(WaveformContract.SamplesField);
        if (samples.Length < channelCount * sizeof(float))
        {
            _dropped++;
            PublishCounters(context);
            return;
        }

        Span<float> values = stackalloc float[channelCount];
        for (var i = 0; i < channelCount; i++)
        {
            values[i] = BinaryPrimitives.ReadSingleLittleEndian(samples.Slice(i * sizeof(float), sizeof(float)));
        }

        var bytes = Encode(context, values);
        if (bytes.IsEmpty)
        {
            _dropped++;
            PublishCounters(context);
            return;
        }

        _lastChannelCount = channelCount;
        _lastGroupId = groupId;
        Send(context, bytes);
        SetStatus(context, WaveformToRawText.Get(WaveformToRawText.Converting));
    }

    private ReadOnlySpan<byte> Encode(IModuleContext context, ReadOnlySpan<float> values)
    {
        switch (_config.Protocol)
        {
            case WaveformToRawProtocol.TextFrame:
                if (string.IsNullOrEmpty(_config.TextHeader) || string.IsNullOrEmpty(_config.TextFooter))
                {
                    ReportBadConfig(context);
                    return default;
                }

                var textChars = TextFrameCodec.Write(
                    _textChars,
                    values,
                    _config.TextHeader,
                    _config.TextFooter,
                    _config.Decimals);
                return textChars <= 0 ? default : EncodeText(textChars);

            case WaveformToRawProtocol.FireWater:
                var lineChars = FireWaterCodec.Write(
                    _textChars,
                    values,
                    _config.FireWaterPrefix,
                    _config.Decimals);
                return lineChars <= 0 ? default : EncodeText(lineChars);

            default:
                var needed = JustFloatCodec.GetEncodedLength(values.Length);
                EnsureEncodeBuffer(needed);
                JustFloatCodec.Encode(values, _encodeBuffer.AsSpan(0, needed));
                return _encodeBuffer.AsSpan(0, needed);
        }
    }

    /// <summary>把已写入的字符编码成 UTF-8 字节；目标缓冲按需增长，稳态不分配。</summary>
    private ReadOnlySpan<byte> EncodeText(int charCount)
    {
        var chars = _textChars.AsSpan(0, charCount);
        var byteCount = Encoding.UTF8.GetByteCount(chars);
        EnsureEncodeBuffer(byteCount);
        Encoding.UTF8.GetBytes(chars, _encodeBuffer);
        return _encodeBuffer.AsSpan(0, byteCount);
    }

    private void EnsureEncodeBuffer(int length)
    {
        if (_encodeBuffer.Length >= length)
        {
            return;
        }

        var next = _encodeBuffer.Length;
        while (next < length)
        {
            next *= 2;
        }

        _encodeBuffer = new byte[next];
    }

    private void ReportBadConfig(IModuleContext context)
    {
        if (_badConfigReported)
        {
            return;
        }

        _badConfigReported = true;
        context.Issues.Report(new IssueReport(
            IssueCategory.ConfigurationError,
            IssueSeverity.Warning,
            WaveformToRawText.Get(WaveformToRawText.BadConfig),
            nameof(WaveformToRawConfig.TextHeader)));
        SetStatus(context, WaveformToRawText.Get(WaveformToRawText.BadConfig));
    }

    private void Send(IModuleContext context, ReadOnlySpan<byte> bytes)
    {
        Span<byte> unit = stackalloc byte[RawBytesContract.SizeInBytes];
        var offset = 0;
        while (offset < bytes.Length)
        {
            var take = Math.Min(RawBytesContract.MaxPayload, bytes.Length - offset);
            var writer = new PayloadWriter(unit, RawBytesContract.Layout);
            writer.Clear();
            writer.WriteU16(RawBytesContract.LengthField, (ushort)take);
            writer.WriteBytes(RawBytesContract.DataField, bytes.Slice(offset, take));

            if (context.Ports.TrySend(OutPort, unit))
            {
                _frames++;
            }
            else
            {
                _dropped++;
            }

            offset += take;
        }

        PublishCounters(context);
    }

    private void PublishCounters(IModuleContext context)
    {
        Span<byte> value = stackalloc byte[sizeof(ulong)];

        BinaryPrimitives.WriteUInt16LittleEndian(value, _lastChannelCount);
        context.Ports.Publish(ChannelCountSource, value[..sizeof(ushort)]);

        BinaryPrimitives.WriteUInt32LittleEndian(value, _lastGroupId);
        context.Ports.Publish(GroupIdSource, value[..sizeof(uint)]);

        BinaryPrimitives.WriteUInt64LittleEndian(value, _frames);
        context.Ports.Publish(FramesSource, value);

        BinaryPrimitives.WriteUInt64LittleEndian(value, _dropped);
        context.Ports.Publish(DroppedSource, value);
    }

    private void SetStatus(IModuleContext context, string text)
    {
        if (string.Equals(_lastStatus, text, StringComparison.Ordinal))
        {
            return;
        }

        _lastStatus = text;
        context.Display.SetText(StatusSource, text);
        context.State.Update(new BusinessState("convert", "ready", text));
    }
}
