using System.Buffers.Binary;
using HYWDebugAssistant.Modules.RawToWaveform.Codec;
using HYWDebugAssistant.Sdk;
using HYWDebugAssistant.Sdk.Contracts;
using HYWDebugAssistant.Sdk.Context;
using HYWDebugAssistant.Sdk.Data;
using HYWDebugAssistant.Sdk.Lifecycle;
using HYWDebugAssistant.Sdk.Ports;
using HYWDebugAssistant.Sdk.Reporting;
using HYWDebugAssistant.Sdk.Timing;
using HYWDebugAssistant.Sdk.Views;

namespace HYWDebugAssistant.Modules.RawToWaveform;

/// <summary>
/// 原始数据 → 波形帧：按所选协议把 <c>RawBytes</c> 里的字节流解析成 <c>WaveformFrame</c>。
/// </summary>
[ModuleDefinition(
    "hyw",
    "raw-to-waveform",
    DisplayNameKey = RawToWaveformText.Title,
    DescriptionKey = RawToWaveformText.Description,
    Tags = ["waveform", "protocol"],
    ParticipatesInDataFlow = true,
    ViewFactory = typeof(Views.RawToWaveformViewFactory))]
public sealed class RawToWaveformModule : IModule, IActivitySource
{
    /// <summary>接收口。</summary>
    public const string InPort = "raw";

    /// <summary>发送口。</summary>
    public const string OutPort = "wave";

    /// <summary>原始字节类型标识（供视图 XAML 的 `{x:Static}` 引用）。</summary>
    public const string RawBytesTypeId = Codec.RawBytesContract.TypeId;

    /// <summary>波形帧类型标识（供视图 XAML 的 `{x:Static}` 引用）。</summary>
    public const string WaveformTypeId = Codec.WaveformContract.TypeId;

    /// <summary>解析角色。</summary>
    public const string RoleDecode = "decode";

    /// <summary>输出角色。</summary>
    public const string RoleEmit = "emit";

    /// <summary>显示源：最近一帧的通道数。</summary>
    public const string ChannelCountSource = "channelCount";

    /// <summary>显示源：已转换帧数。</summary>
    public const string FramesSource = "frames";

    /// <summary>显示源：丢弃单元数。</summary>
    public const string DroppedSource = "dropped";

    /// <summary>显示源：状态。</summary>
    public const string StatusSource = "status";

    /// <summary>命令：清空统计。</summary>
    public const string ResetCommand = "reset";

    /// <summary>清空统计按钮的图标键（图标表在模块自有资源里）。</summary>
    public const string ClearStatsIcon = "icon.refresh";

    /// <summary>配置字段名：协议。</summary>
    public const string ProtocolField = nameof(RawToWaveformConfig.Protocol);

    /// <summary>配置字段名：帧头。</summary>
    public const string HeaderField = nameof(RawToWaveformConfig.TextHeader);

    /// <summary>配置字段名：帧尾。</summary>
    public const string FooterField = nameof(RawToWaveformConfig.TextFooter);

    /// <summary>配置字段名：通道组编号。</summary>
    public const string GroupIdField = nameof(RawToWaveformConfig.GroupId);

    /// <summary>配置字段名：采样率。</summary>
    public const string RateField = nameof(RawToWaveformConfig.RateHz);

    /// <summary>解析失败状态句的格式模板（避免每帧重新解析复合格式串）。</summary>
    private static readonly System.Text.CompositeFormat ParseErrorFormat =
        System.Text.CompositeFormat.Parse(RawToWaveformText.Get(RawToWaveformText.ParseError));

    private IModuleContext? _context;
    private RawToWaveformConfig _config = new();
    private TextFrameDecoder? _text;
    private FireWaterDecoder? _fireWater;
    private JustFloatDecoder? _justFloat;
    private ulong _frames;
    private ulong _dropped;
    private uint _sequence;
    private ushort _lastChannelCount;
    private long _lastErrors;
    private string _lastStatus = string.Empty;

    /// <inheritdoc />
    public ReceiveMode Receive => ReceiveMode.Push;

    /// <inheritdoc />
    public void OnRegistered(IModuleContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        _context = context;
        _config = context.Configuration.Read<RawToWaveformConfig>();

        context.Declaration.DeclareDataType(new DataTypeDeclaration(
            RawBytesContract.TypeId,
            RawBytesContract.Shape,
            TransferSemantics.Stream)
        {
            Metering = RawBytesContract.Metering,
        });

        context.Declaration.DeclareDataType(new DataTypeDeclaration(
            WaveformContract.TypeId,
            WaveformContract.Shape,
            WaveformContract.Semantics)
        {
            Metering = WaveformContract.Metering,
        });

        // 端口声明在 Views/RawToWaveformView.axaml 的 ModuleView.Ports 里。

        context.Declaration.DeclareRole(RoleDeclaration.Input(
            RoleDecode,
            RawBytesContract.TypeId,
            InPort,
            RawToWaveformText.Get(RawToWaveformText.RoleDecode)));

        context.Declaration.DeclareRole(RoleDeclaration.Output(
            RoleEmit,
            WaveformContract.TypeId,
            OutPort,
            RawToWaveformText.Get(RawToWaveformText.RoleEmit)));

        context.Declaration.DeclareFlow(new FlowDeclaration(RoleDecode, RoleEmit));

        context.Declaration.DeclareDisplaySource(new DisplaySourceDeclaration(
            ChannelCountSource,
            ViewDataSourceKind.Value,
            ContractPrimitive.U16));

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

        context.Declaration.DeclareCommand(ResetCommand, RawToWaveformText.Get(RawToWaveformText.Reset));

        context.Ports.SetReceiveHandler(InPort, OnRaw);
        BuildDecoder(context);
    }

    /// <inheritdoc />
    public void OnActivated()
    {
        if (_context is not { } context)
        {
            return;
        }

        PublishCounters(context);
        SetStatus(context, _config.Protocol is RawToWaveformProtocol.TextFrame
            && (string.IsNullOrEmpty(_config.TextHeader) || string.IsNullOrEmpty(_config.TextFooter))
            ? RawToWaveformText.Get(RawToWaveformText.BadConfig)
            : RawToWaveformText.Get(RawToWaveformText.Ready));
    }

    /// <inheritdoc />
    public void OnConfigurationChanged()
    {
        if (_context is not { } context)
        {
            return;
        }

        _config = context.Configuration.Read<RawToWaveformConfig>();
        BuildDecoder(context);
        _lastErrors = 0;
        SetStatus(context, RawToWaveformText.Get(RawToWaveformText.Ready));
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
            _sequence = 0;
            _lastChannelCount = 0;
            _lastErrors = 0;
            _text?.Reset();
            _fireWater?.Reset();
            _justFloat?.Reset();
            PublishCounters(context);
            SetStatus(context, RawToWaveformText.Get(RawToWaveformText.ResetDone));
        }

        while (context.Inbox.TryDequeueInput(out _, out _))
        {
            // 本模块没有输入源。
        }
    }

    /// <inheritdoc />
    public void OnRemoved()
    {
        _text = null;
        _fireWater = null;
        _justFloat = null;
        _context = null;
    }

    private void BuildDecoder(IModuleContext context)
    {
        _text = null;
        _fireWater = null;
        _justFloat = null;

        switch (_config.Protocol)
        {
            case RawToWaveformProtocol.TextFrame:
                if (string.IsNullOrEmpty(_config.TextHeader) || string.IsNullOrEmpty(_config.TextFooter))
                {
                    context.Issues.Report(new IssueReport(
                        IssueCategory.ConfigurationError,
                        IssueSeverity.Warning,
                        RawToWaveformText.Get(RawToWaveformText.BadConfig),
                        nameof(RawToWaveformConfig.TextHeader)));
                    return;
                }

                _text = new TextFrameDecoder(_config.TextHeader, _config.TextFooter);
                return;

            case RawToWaveformProtocol.FireWater:
                _fireWater = new FireWaterDecoder();
                return;

            default:
                _justFloat = new JustFloatDecoder();
                return;
        }
    }

    private void OnRaw(ReadOnlySpan<byte> payload)
    {
        if (_context is not { } context)
        {
            return;
        }

        if (!PayloadReader.TryCreate(payload, RawBytesContract.Layout, out var reader))
        {
            _dropped++;
            PublishCounters(context);
            return;
        }

        var length = reader.ReadU16(RawBytesContract.LengthField);
        if (length > RawBytesContract.MaxPayload || length == 0)
        {
            _dropped++;
            PublishCounters(context);
            return;
        }

        var data = payload.Slice(RawBytesContract.DataOffset, length);
        _text?.Append(data);
        _fireWater?.Append(data);
        _justFloat?.Append(data);

        Drain(context);
        PublishCounters(context);
    }

    private void Drain(IModuleContext context)
    {
        Span<float> values = stackalloc float[WaveformContract.MaxChannels];

        while (true)
        {
            bool decoded;
            int count;
            if (_text is not null)
            {
                decoded = _text.TryReadFrame(values, out count);
            }
            else if (_fireWater is not null)
            {
                decoded = _fireWater.TryReadFrame(values, out count);
            }
            else if (_justFloat is not null)
            {
                decoded = _justFloat.TryReadFrame(values, out count);
            }
            else
            {
                return;
            }

            if (!decoded)
            {
                break;
            }

            Emit(context, values, count);
        }

        var errors = (_text?.Errors ?? 0) + (_fireWater?.Errors ?? 0) + (_justFloat?.Errors ?? 0);
        if (errors != _lastErrors)
        {
            _lastErrors = errors;
            SetStatus(context, string.Format(
                System.Globalization.CultureInfo.InvariantCulture,
                ParseErrorFormat,
                errors));
        }
    }

    private void Emit(IModuleContext context, ReadOnlySpan<float> values, int count)
    {
        Span<byte> unit = stackalloc byte[WaveformContract.SizeInBytes];
        var writer = new PayloadWriter(unit, WaveformContract.Layout);
        writer.Clear();
        writer.WriteU16(WaveformContract.ChannelCountField, (ushort)count);
        writer.WriteU16(WaveformContract.PayloadBytesField, (ushort)(count * sizeof(float)));
        // 内置波形显示器把 groupId == 0 判为格式不正确并丢弃整帧，这里兜底为 1。
        writer.WriteU32(WaveformContract.GroupIdField, _config.GroupId > 0 ? unchecked((uint)_config.GroupId) : 1u);
        writer.WriteU32(WaveformContract.SequenceField, unchecked(_sequence++));
        writer.WriteF32(WaveformContract.RateHzField, (float)_config.RateHz);
        writer.WriteU64(
            WaveformContract.ValidMaskField,
            count >= WaveformContract.MaxChannels ? ulong.MaxValue : (1UL << count) - 1);

        var samples = unit[WaveformContract.SamplesOffset..];
        for (var i = 0; i < count; i++)
        {
            BinaryPrimitives.WriteSingleLittleEndian(
                samples.Slice(i * sizeof(float), sizeof(float)),
                values[i]);
        }

        if (context.Ports.TrySend(OutPort, unit))
        {
            _frames++;
            _lastChannelCount = (ushort)count;
            SetStatus(context, RawToWaveformText.Get(RawToWaveformText.Converting));
        }
        else
        {
            _dropped++;
        }
    }

    private void PublishCounters(IModuleContext context)
    {
        Span<byte> value = stackalloc byte[sizeof(ulong)];

        BinaryPrimitives.WriteUInt16LittleEndian(value, _lastChannelCount);
        context.Ports.Publish(ChannelCountSource, value[..sizeof(ushort)]);

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
