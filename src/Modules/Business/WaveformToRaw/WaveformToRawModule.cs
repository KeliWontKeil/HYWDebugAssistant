using System.Buffers.Binary;
using System.Globalization;
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
using HYWDebugAssistant.Sdk.Values;
using HYWDebugAssistant.Sdk.Views;

namespace HYWDebugAssistant.Modules.WaveformToRaw;

/// <summary>
/// 波形帧 → 原始数据：把 <c>WaveformFrame</c> 按所选协议编码成 <c>RawBytes</c> 字节流。
/// </summary>
/// <remarks>
/// <b>值变更 → 应用只有一条链</b>：视图按 <c>CommitMode</c> 交值，模块在每一拍用
/// <see cref="ApplySettings"/> 读一次值、解析、校验，变了就换口径。持久化由值声明决定。
/// </remarks>
[ModuleDefinition(
    "hyw",
    "waveform-to-raw",
    DisplayNameKey = WaveformToRawText.Title,
    DescriptionKey = WaveformToRawText.Description,
    Tags = ["converter", "waveform", "protocol"],
    ParticipatesInDataFlow = true,
    ViewFactory = typeof(Views.WaveformToRawViewFactory))]
public sealed class WaveformToRawModule : IModule, IActivitySource
{
    /// <summary>接收口。</summary>
    public const string InPort = "wave";

    /// <summary>发送口。</summary>
    public const string OutPort = "raw";

    /// <summary>原始字节类型标识（供视图 XAML 的 `{x:Static}` 引用）。</summary>
    public const string RawBytesTypeId = Codec.RawBytesContract.TypeId;

    /// <summary>波形帧类型标识（供视图 XAML 的 `{x:Static}` 引用）。</summary>
    public const string WaveformTypeId = Codec.WaveformContract.TypeId;

    /// <summary>接收角色。</summary>
    public const string RoleIngest = "ingest";

    /// <summary>编码角色。</summary>
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
    public const string ProtocolField = nameof(WaveformToRawConfig.Protocol);

    /// <summary>输入：通道组编号。</summary>
    public const string GroupIdInput = "groupId";

    /// <summary>输入：小数位数。</summary>
    public const string DecimalsInput = "decimals";

    /// <summary>输入：文本帧帧头。</summary>
    public const string HeaderInput = "header";

    /// <summary>输入：文本帧帧尾。</summary>
    public const string FooterInput = "footer";

    /// <summary>输入：FireWater 行前缀。</summary>
    public const string PrefixInput = "prefix";

    private const int DefaultGroupId = 1;
    private const int DefaultDecimals = 0;
    private const int MaxDecimals = 9;

    private IModuleContext? _context;
    private Settings _settings = new(
        WaveformToRawProtocol.TextFrame,
        string.Empty,
        string.Empty,
        string.Empty,
        DefaultDecimals,
        DefaultGroupId);
    private ulong _frames;
    private ulong _dropped;
    private ushort _lastChannelCount;
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

        // 配置类型是协议下拉的 Schema 来源；读一次即登记（值本身由 context.Values 读取）。
        _ = context.Configuration.Read<WaveformToRawConfig>();

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

        // 端口声明在 Views/WaveformToRawView.axaml 的 ModuleView.Ports 里。

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

        DeclareValue(context, GroupIdInput, ValueEditor.Number);
        DeclareValue(context, DecimalsInput, ValueEditor.Number);
        DeclareValue(context, HeaderInput, ValueEditor.Text);
        DeclareValue(context, FooterInput, ValueEditor.Text);
        DeclareValue(context, PrefixInput, ValueEditor.Text);

        context.Declaration.DeclareCommand(ResetCommand, WaveformToRawText.Get(WaveformToRawText.Reset));

        context.Ports.SetReceiveHandler(InPort, OnWave);
        _settings = ReadSettings(context);
    }

    /// <inheritdoc />
    public void OnActivated()
    {
        if (_context is not { } context)
        {
            return;
        }

        ApplySettings(context);
        PublishCounters(context);
        SetStatus(context, HasValidTextConfig(_settings)
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

        ApplySettings(context);
    }

    /// <inheritdoc />
    public void OnStep(IModuleContext context, StepReason reason)
    {
        ArgumentNullException.ThrowIfNull(context);

        ApplySettings(context);

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
            // 值在 ApplySettings 里按值名统一读取；这里只消费事件，避免积压。
        }
    }

    /// <inheritdoc />
    public void OnRemoved() => _context = null;

    private static void DeclareValue(IModuleContext context, string name, ValueEditor editor)
    {
        context.Declaration.DeclareDisplaySource(new DisplaySourceDeclaration(name, ViewDataSourceKind.Text));
        context.Declaration.DeclareValue(new ValueDeclaration(name)
        {
            Editor = editor,
            Persistent = true,
        });
    }

    /// <summary>读一次输入与协议，得到"此刻应该生效"的一组值。</summary>
    private Settings ReadSettings(IModuleContext context)
    {
        var protocol = Enum.TryParse<WaveformToRawProtocol>(
                context.Values.ReadText(ProtocolField),
                ignoreCase: true,
                out var parsed)
            && Enum.IsDefined(parsed)
                ? parsed
                : _settings.Protocol;

        // 空串对帧头/帧尾是非法值 ⇒ 保持上一次的有效值；前缀空串是合法值（= 不写前缀）。
        var header = ReadText(context, HeaderInput, _settings.Header);
        var footer = ReadText(context, FooterInput, _settings.Footer);
        var prefix = context.Values.ReadText(PrefixInput) ?? string.Empty;
        var decimals = ReadInt(context, DecimalsInput, _settings.Decimals, 0, MaxDecimals);
        var groupId = ReadInt(context, GroupIdInput, _settings.GroupId, 1, int.MaxValue);

        return new Settings(protocol, header, footer, prefix, decimals, groupId);
    }

    /// <summary>值没变就什么都不做；变了才刷状态句（编码口径每帧直接读 <c>_settings</c>）。</summary>
    private void ApplySettings(IModuleContext context)
    {
        var next = ReadSettings(context);
        if (next == _settings)
        {
            return;
        }

        var becameValid = !HasValidTextConfig(_settings) && HasValidTextConfig(next);
        _settings = next;

        if (becameValid)
        {
            _badConfigReported = false;
        }

        SetStatus(context, HasValidTextConfig(_settings)
            ? WaveformToRawText.Get(WaveformToRawText.Ready)
            : WaveformToRawText.Get(WaveformToRawText.BadConfig));
    }

    private static bool HasValidTextConfig(Settings settings) =>
        settings.Protocol is not WaveformToRawProtocol.TextFrame
        || (!string.IsNullOrEmpty(settings.Header) && !string.IsNullOrEmpty(settings.Footer));

    private static string ReadText(IModuleContext context, string name, string fallback)
    {
        var text = context.Values.ReadText(name);
        return string.IsNullOrEmpty(text) ? fallback : text;
    }

    private static int ReadInt(IModuleContext context, string name, int fallback, int minimum, int maximum)
    {
        var text = context.Values.ReadText(name);
        return int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var value)
            ? Math.Clamp(value, minimum, maximum)
            : fallback;
    }

    /// <summary>本模块负责的通道组；0 是保留值，按 1 处理。</summary>
    private uint ConfiguredGroupId => _settings.GroupId > 0 ? unchecked((uint)_settings.GroupId) : 1u;

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

        if (groupId != ConfiguredGroupId)
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
        Send(context, bytes);
        SetStatus(context, WaveformToRawText.Get(WaveformToRawText.Converting));
    }

    private ReadOnlySpan<byte> Encode(IModuleContext context, ReadOnlySpan<float> values)
    {
        switch (_settings.Protocol)
        {
            case WaveformToRawProtocol.TextFrame:
                if (string.IsNullOrEmpty(_settings.Header) || string.IsNullOrEmpty(_settings.Footer))
                {
                    ReportBadConfig(context);
                    return default;
                }

                var textChars = TextFrameCodec.Write(
                    _textChars,
                    values,
                    _settings.Header,
                    _settings.Footer,
                    _settings.Decimals);
                return textChars <= 0 ? default : EncodeText(textChars);

            case WaveformToRawProtocol.FireWater:
                var lineChars = FireWaterCodec.Write(
                    _textChars,
                    values,
                    _settings.Prefix,
                    _settings.Decimals);
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
            HeaderInput));
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

    /// <summary>此刻生效的一组值（协议 + 运行期参数）。</summary>
    private sealed record Settings(
        WaveformToRawProtocol Protocol,
        string Header,
        string Footer,
        string Prefix,
        int Decimals,
        int GroupId);
}
