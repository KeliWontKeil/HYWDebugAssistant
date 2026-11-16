using System.Buffers.Binary;
using System.Globalization;
using System.Text;
using HYWDebugAssistant.Modules.RawToWaveform.Codec;
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

namespace HYWDebugAssistant.Modules.RawToWaveform;

/// <summary>
/// 原始数据 → 波形帧：按所选协议把 <c>RawBytes</c> 里的字节流解析成 <c>WaveformFrame</c>。
/// </summary>
/// <remarks>
/// <b>值变更 → 应用只有一条链</b>：视图按 <c>CommitMode</c> 交值，模块在每一拍用
/// <see cref="ApplySettings"/> 读一次值、解析、校验，变了就重建解码器。持久化由值声明决定。
/// </remarks>
[ModuleDefinition(
    "hyw",
    "raw-to-waveform",
    DisplayNameKey = RawToWaveformText.Title,
    DescriptionKey = RawToWaveformText.Description,
    Tags = ["converter", "waveform", "protocol"],
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

    /// <summary>输入：通道组编号。</summary>
    public const string GroupIdInput = "groupId";

    /// <summary>输入：采样率（Hz）。</summary>
    public const string RateInput = "rateHz";

    /// <summary>输入：文本帧帧头（十六进制字节串，如 <c>61 62</c>）。</summary>
    public const string HeaderHexInput = "headerHex";

    /// <summary>输入：文本帧帧尾（十六进制字节串，如 <c>63 64</c>）。</summary>
    public const string FooterHexInput = "footerHex";

    /// <summary>文本帧帧头的迁移源：只在新值为空时读取一次，不绑定界面。</summary>
    public const string HeaderInput = "header";

    /// <summary>文本帧帧尾的迁移源：只在新值为空时读取一次，不绑定界面。</summary>
    public const string FooterInput = "footer";

    /// <summary>输入：FireWater 前缀过滤（空 = 任意前缀）。</summary>
    public const string PrefixInput = "prefix";

    private const int DefaultGroupId = 1;
    private const double DefaultRateHz = 1000;

    /// <summary>帧头的 XAML 初值（迁移时用它区分"用户值"与"播种值"）。</summary>
    public const string DefaultHeaderHex = "61 62";

    /// <summary>帧尾的 XAML 初值。</summary>
    public const string DefaultFooterHex = "63 64";


    private IModuleContext? _context;
    private Settings _settings = new(
        RawToWaveformProtocol.TextFrame,
        [],
        [],
        string.Empty,
        DefaultGroupId,
        DefaultRateHz);
    private TextFrameDecoder? _text;
    private FireWaterDecoder? _fireWater;
    private JustFloatDecoder? _justFloat;
    private ulong _frames;
    private ulong _dropped;
    private uint _sequence;
    private ushort _lastChannelCount;
    private string _lastStatus = string.Empty;

    /// <summary>旧文本帧头/帧尾是否已经迁移过（一次性；迁移后清空旧值）。</summary>
    private bool _legacyInputsMigrated;

    /// <summary>已标红的输入（避免每拍重复写外观）。</summary>
    private readonly HashSet<string> _alertedInputs = new(StringComparer.Ordinal);

    /// <summary>本拍帧头/帧尾是否非法（保留上一次有效值）。</summary>
    private bool _headerInvalid;
    private bool _footerInvalid;

    /// <inheritdoc />
    public ReceiveMode Receive => ReceiveMode.Push;

    /// <inheritdoc />
    public void OnRegistered(IModuleContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        _context = context;

        // 配置类型是协议下拉的 Schema 来源；读一次即登记（值本身由 context.Values 读取）。
        _ = context.Configuration.Read<RawToWaveformConfig>();

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

        DeclareValue(context, GroupIdInput, ValueEditor.Number);
        DeclareValue(context, RateInput, ValueEditor.Number);
        DeclareValue(context, HeaderHexInput, ValueEditor.Text);
        DeclareValue(context, FooterHexInput, ValueEditor.Text);
        DeclareValue(context, PrefixInput, ValueEditor.Text);

        // 旧文本帧头/帧尾：只作一次性迁移源（不绑定界面、不再声明显示源）。
        context.Declaration.DeclareValue(new ValueDeclaration(HeaderInput)
        {
            Editor = ValueEditor.Text,
            Persistent = true,
            ResetWithModule = false,
        });
        context.Declaration.DeclareValue(new ValueDeclaration(FooterInput)
        {
            Editor = ValueEditor.Text,
            Persistent = true,
            ResetWithModule = false,
        });

        context.Declaration.DeclareCommand(ResetCommand, RawToWaveformText.Get(RawToWaveformText.Reset));

        context.Ports.SetReceiveHandler(InPort, OnRaw);
        _settings = ReadSettings(context, reportAlerts: false);
    }

    /// <inheritdoc />
    public void OnActivated()
    {
        if (_context is not { } context)
        {
            return;
        }

        // 激活发生在 XAML 初值与组合件值恢复之后：先做旧文本帧头的一次性迁移，
        // 再按当前值建立解码器/状态句。
        MigrateLegacyInputs(context);
        _settings = ReadSettings(context);
        BuildDecoder(context);
        SetStatus(context, StatusForSettings());
        PublishCounters(context);
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

        MigrateLegacyInputs(context);
        ApplySettings(context);

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
            _text?.Reset();
            _fireWater?.Reset();
            _justFloat?.Reset();
            PublishCounters(context);
            SetStatus(context, RawToWaveformText.Get(RawToWaveformText.ResetDone));
        }

        while (context.Inbox.TryDequeueInput(out _, out _))
        {
            // 值在 ApplySettings 里按值名统一读取；这里只消费事件，避免积压。
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
    private Settings ReadSettings(IModuleContext context, bool reportAlerts = true)
    {
        var protocol = Enum.TryParse<RawToWaveformProtocol>(
                context.Values.ReadText(ProtocolField),
                ignoreCase: true,
                out var parsed)
            && Enum.IsDefined(parsed)
                ? parsed
                : _settings.Protocol;

        // 帧头/帧尾是**十六进制字节串**；空/非法 ⇒ 保持上一次有效字节并标红。
        var header = ReadHex(context, HeaderHexInput, _settings.Header, out var headerInvalid);
        var footer = ReadHex(context, FooterHexInput, _settings.Footer, out var footerInvalid);
        _headerInvalid = headerInvalid;
        _footerInvalid = footerInvalid;

        if (reportAlerts)
        {
            SetInputAlert(context, HeaderHexInput, headerInvalid);
            SetInputAlert(context, FooterHexInput, footerInvalid);
        }

        var prefix = context.Values.ReadText(PrefixInput) ?? string.Empty;
        var groupId = ReadInt(context, GroupIdInput, _settings.GroupId, 1, int.MaxValue);
        var rateHz = ReadRate(context, RateInput, _settings.RateHz);

        return new Settings(protocol, header, footer, prefix, groupId, rateHz);
    }

    private static byte[] ReadHex(IModuleContext context, string name, byte[] fallback, out bool invalid)
    {
        if (!HexInput.TryParse(context.Values.ReadText(name), out var bytes))
        {
            invalid = true;
            return fallback;
        }

        invalid = false;
        return bytes;
    }

    /// <summary>把某一格的"格式无效"落到外观（只在该格状态变化时写一次）。</summary>
    private void SetInputAlert(IModuleContext context, string source, bool alert)
    {
        if (alert)
        {
            if (_alertedInputs.Add(source))
            {
                context.Appearance.SetOutlineRole(source, ViewInkRoles.Danger);
            }

            return;
        }

        if (_alertedInputs.Remove(source))
        {
            context.Appearance.SetOutlineRole(source, null);
        }
    }

    /// <summary>旧文本帧头/帧尾 → 十六进制串（一次性；迁移后清空旧值，避免清空新值后旧值复活）。</summary>
    private void MigrateLegacyInputs(IModuleContext context)
    {
        if (_legacyInputsMigrated)
        {
            return;
        }

        _legacyInputsMigrated = true;
        Migrate(HeaderInput, HeaderHexInput, DefaultHeaderHex);
        Migrate(FooterInput, FooterHexInput, DefaultFooterHex);

        void Migrate(string legacyName, string hexName, string initial)
        {
            if (context.Values.ReadText(legacyName) is not { Length: > 0 } legacy)
            {
                return;
            }

            var migrated = ToHex(Encoding.UTF8.GetBytes(legacy));
            var current = context.Values.ReadText(hexName);

            // 升级时新值还没有事实，XAML 初值会在激活前先播种成 initial；
            // 只有"空 / 仍是初值 / 已是同一个迁移结果"三种情况可以写，避免覆盖用户后来改的新值。
            if (!string.IsNullOrEmpty(current)
                && !string.Equals(current, initial, StringComparison.Ordinal)
                && !string.Equals(current, migrated, StringComparison.Ordinal))
            {
                return;
            }

            context.Values.AcceptText(hexName, migrated);
            context.Values.AcceptText(legacyName, string.Empty);
        }
    }

    /// <summary>字节 → 大写两位、空格分隔的十六进制串。</summary>
    private static string ToHex(ReadOnlySpan<byte> bytes)
    {
        if (bytes.IsEmpty)
        {
            return string.Empty;
        }

        var chars = new char[(bytes.Length * 3) - 1];
        var position = 0;

        for (var index = 0; index < bytes.Length; index++)
        {
            if (index > 0)
            {
                chars[position++] = ' ';
            }

            var value = bytes[index];
            chars[position++] = ToHexChar(value >> 4);
            chars[position++] = ToHexChar(value & 0xF);
        }

        return new string(chars);
    }

    private static char ToHexChar(int value) => (char)(value < 10 ? '0' + value : 'A' + value - 10);

    /// <summary>值没变就什么都不做；变了才重建解码器并刷新状态句。</summary>
    private void ApplySettings(IModuleContext context)
    {
        var next = ReadSettings(context);
        if (SameSettings(next, _settings))
        {
            return;
        }

        var decoderChanged =
            next.Protocol != _settings.Protocol
            || !next.Header.AsSpan().SequenceEqual(_settings.Header)
            || !next.Footer.AsSpan().SequenceEqual(_settings.Footer)
            || !string.Equals(next.Prefix, _settings.Prefix, StringComparison.Ordinal);

        _settings = next;

        if (decoderChanged)
        {
            BuildDecoder(context);
        }

        SetStatus(context, StatusForSettings());
    }

    private static bool SameSettings(Settings left, Settings right) =>
        left.Protocol == right.Protocol
        && left.Header.AsSpan().SequenceEqual(right.Header)
        && left.Footer.AsSpan().SequenceEqual(right.Footer)
        && string.Equals(left.Prefix, right.Prefix, StringComparison.Ordinal)
        && left.GroupId == right.GroupId
        && left.RateHz.Equals(right.RateHz);

    /// <summary>当前配置对应的状态句（无有效帧头/帧尾 → 配置错误；有但非法 → 仍用上次有效值）。</summary>
    private string StatusForSettings()
    {
        if (_settings.Protocol is not RawToWaveformProtocol.TextFrame)
        {
            return RawToWaveformText.Get(RawToWaveformText.Ready);
        }

        if (_settings.Header.Length == 0 || _settings.Footer.Length == 0)
        {
            return RawToWaveformText.Get(RawToWaveformText.BadHex);
        }

        return _headerInvalid || _footerInvalid
            ? RawToWaveformText.Get(RawToWaveformText.BadHexKept)
            : RawToWaveformText.Get(RawToWaveformText.Ready);
    }

    private static int ReadInt(IModuleContext context, string name, int fallback, int minimum, int maximum)
    {
        var text = context.Values.ReadText(name);
        return int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var value)
            ? Math.Clamp(value, minimum, maximum)
            : fallback;
    }

    private static double ReadRate(IModuleContext context, string name, double fallback)
    {
        var text = context.Values.ReadText(name);
        return double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var value)
            && double.IsFinite(value)
            && value > 0
                ? value
                : fallback;
    }

    private void BuildDecoder(IModuleContext context)
    {
        _text = null;
        _fireWater = null;
        _justFloat = null;

        switch (_settings.Protocol)
        {
            case RawToWaveformProtocol.TextFrame:
                if (_settings.Header.Length == 0 || _settings.Footer.Length == 0)
                {
                    context.Issues.Report(new IssueReport(
                        IssueCategory.ConfigurationError,
                        IssueSeverity.Warning,
                        RawToWaveformText.Get(RawToWaveformText.BadHex),
                        HeaderHexInput));
                    return;
                }

                _text = new TextFrameDecoder(_settings.Header, _settings.Footer);
                return;

            case RawToWaveformProtocol.FireWater:
                _fireWater = new FireWaterDecoder(_settings.Prefix);
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


    }

    private void Emit(IModuleContext context, ReadOnlySpan<float> values, int count)
    {
        Span<byte> unit = stackalloc byte[WaveformContract.SizeInBytes];
        var writer = new PayloadWriter(unit, WaveformContract.Layout);
        writer.Clear();
        writer.WriteU16(WaveformContract.ChannelCountField, (ushort)count);
        writer.WriteU16(WaveformContract.PayloadBytesField, (ushort)(count * sizeof(float)));
        // 内置波形显示器把 groupId == 0 判为格式不正确并丢弃整帧，这里兜底为 1。
        writer.WriteU32(WaveformContract.GroupIdField, _settings.GroupId > 0 ? unchecked((uint)_settings.GroupId) : 1u);
        writer.WriteU32(WaveformContract.SequenceField, unchecked(_sequence++));
        writer.WriteF32(WaveformContract.RateHzField, (float)_settings.RateHz);
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

    /// <summary>此刻生效的一组值（协议 + 运行期参数）。</summary>
    private sealed record Settings(
        RawToWaveformProtocol Protocol,
        byte[] Header,
        byte[] Footer,
        string Prefix,
        int GroupId,
        double RateHz);
}
