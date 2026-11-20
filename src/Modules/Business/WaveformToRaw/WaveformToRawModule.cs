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

    /// <summary>输入：文本帧帧头（十六进制字节串，如 <c>61 62</c>）。</summary>
    public const string HeaderHexInput = "headerHex";

    /// <summary>输入：文本帧帧尾（十六进制字节串，如 <c>63 64</c>）。</summary>
    public const string FooterHexInput = "footerHex";

    /// <summary>文本帧帧头的迁移源：只在新值为空时读取一次，不绑定界面。</summary>
    public const string HeaderInput = "header";

    /// <summary>文本帧帧尾的迁移源：只在新值为空时读取一次，不绑定界面。</summary>
    public const string FooterInput = "footer";

    /// <summary>输入：FireWater 行前缀。</summary>
    public const string PrefixInput = "prefix";

    private const int DefaultGroupId = 1;
    private const int DefaultDecimals = 0;
    private const int MaxDecimals = 9;

    /// <summary>帧头的 XAML 初值（迁移时用它区分"用户值"与"播种值"）。</summary>
    public const string DefaultHeaderHex = "61 62";

    /// <summary>帧尾的 XAML 初值。</summary>
    public const string DefaultFooterHex = "63 64";

    private IModuleContext? _context;
    private Settings _settings = new(
        WaveformToRawProtocol.TextFrame,
        [],
        [],
        string.Empty,
        DefaultDecimals,
        DefaultGroupId);
    private ulong _frames;
    private ulong _dropped;
    private ushort _lastChannelCount;
    private bool _badConfigReported;
    private string _lastStatus = string.Empty;

    /// <summary>旧文本帧头/帧尾是否已经迁移过（一次性；迁移后清空旧值）。</summary>
    private bool _legacyInputsMigrated;

    /// <summary>已标红的输入（避免每拍重复写外观）。</summary>
    private readonly InputValidity _validity = new();

    /// <summary>本拍帧头/帧尾是否非法（保留上一次有效值）。</summary>
    private bool _headerInvalid;
    private bool _footerInvalid;
    private bool _groupInvalid;

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

        context.Declaration.DeclareCommand(ResetCommand, WaveformToRawText.Get(WaveformToRawText.Reset));

        context.Ports.SetReceiveHandler(InPort, OnWave);
        _settings = ReadSettings(context, reportAlerts: false);
    }

    /// <inheritdoc />
    public void OnActivated()
    {
        if (_context is not { } context)
        {
            return;
        }

        // 激活发生在 XAML 初值与组合件值恢复之后：先做旧文本帧头的一次性迁移，再应用当前值。
        MigrateLegacyInputs(context);
        ApplySettings(context);
        PublishCounters(context);
        SetStatus(context, StatusForSettings());
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
    private Settings ReadSettings(IModuleContext context, bool reportAlerts = true)
    {
        var protocol = Enum.TryParse<WaveformToRawProtocol>(
                context.Values.ReadText(ProtocolField),
                ignoreCase: true,
                out var parsed)
            && Enum.IsDefined(parsed)
                ? parsed
                : _settings.Protocol;

        // 帧头/帧尾是**十六进制字节串**；空/非法 ⇒ 保持上一次有效字节并标红。
        var header = ReadHex(context, HeaderHexInput, _settings.Header, out var headerInvalid);
        var footer = ReadHex(context, FooterHexInput, _settings.Footer, out var footerInvalid);
        var groupId = ReadGroupId(context, GroupIdInput, _settings.GroupId, out var groupInvalid);
        _headerInvalid = headerInvalid;
        _footerInvalid = footerInvalid;
        _groupInvalid = groupInvalid;

        if (reportAlerts)
        {
            _validity.Set(context, HeaderHexInput, headerInvalid, headerInvalid ? WaveformToRawText.Get(WaveformToRawText.HexHint) : null);
            _validity.Set(context, FooterHexInput, footerInvalid, footerInvalid ? WaveformToRawText.Get(WaveformToRawText.HexHint) : null);
            _validity.Set(context, GroupIdInput, groupInvalid, groupInvalid ? WaveformToRawText.Get(WaveformToRawText.BadGroup) : null);
        }

        var prefix = context.Values.ReadText(PrefixInput) ?? string.Empty;
        var decimals = ReadInt(context, DecimalsInput, _settings.Decimals, 0, MaxDecimals);

        return new Settings(protocol, header, footer, prefix, decimals, groupId);
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

    /// <summary>当前配置对应的状态句（无有效帧头/帧尾 → 配置错误；有但非法 → 仍用上次有效值）。</summary>
    private string StatusForSettings()
    {
        if (_settings.Protocol is not WaveformToRawProtocol.TextFrame)
        {
            return _groupInvalid
                ? WaveformToRawText.Get(WaveformToRawText.BadGroup)
                : WaveformToRawText.Get(WaveformToRawText.Ready);
        }

        if (_settings.Header.Length == 0 || _settings.Footer.Length == 0)
        {
            return WaveformToRawText.Get(WaveformToRawText.BadHex);
        }

        return _headerInvalid || _footerInvalid
            ? WaveformToRawText.Get(WaveformToRawText.BadHexKept)
            : _groupInvalid
                ? WaveformToRawText.Get(WaveformToRawText.BadGroup)
                : WaveformToRawText.Get(WaveformToRawText.Ready);
    }

    /// <summary>值没变就什么都不做；变了才刷状态句（编码口径每帧直接读 <c>_settings</c>）。</summary>
    private void ApplySettings(IModuleContext context)
    {
        var next = ReadSettings(context);
        if (SameSettings(next, _settings))
        {
            return;
        }

        var becameValid = !HasValidTextConfig(_settings) && HasValidTextConfig(next);
        _settings = next;

        if (becameValid)
        {
            _badConfigReported = false;
        }

        SetStatus(context, StatusForSettings());
    }

    private static bool SameSettings(Settings left, Settings right) =>
        left.Protocol == right.Protocol
        && left.Header.AsSpan().SequenceEqual(right.Header)
        && left.Footer.AsSpan().SequenceEqual(right.Footer)
        && string.Equals(left.Prefix, right.Prefix, StringComparison.Ordinal)
        && left.Decimals == right.Decimals
        && left.GroupId == right.GroupId;

    private static bool HasValidTextConfig(Settings settings) =>
        settings.Protocol is not WaveformToRawProtocol.TextFrame
        || (settings.Header.Length > 0 && settings.Footer.Length > 0);

    private static int ReadGroupId(IModuleContext context, string name, int fallback, out bool invalid)
    {
        var text = context.Values.ReadText(name);
        if (string.IsNullOrWhiteSpace(text))
        {
            invalid = false;
            return fallback;
        }

        invalid = !int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var value)
            || value < 1;

        return invalid ? fallback : value;
    }

    private static int ReadInt(IModuleContext context, string name, int fallback, int minimum, int maximum)
    {
        var text = context.Values.ReadText(name);
        return int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var value)
            ? Math.Clamp(value, minimum, maximum)
            : fallback;
    }

    /// <summary>本模块负责的通道组；组号解析保证 >=1，不再兜底 1。</summary>
    private uint ConfiguredGroupId => unchecked((uint)_settings.GroupId);

    private void OnWave(ReadOnlySpan<byte> payload)
    {
        if (_context is not { } context)
        {
            return;
        }

        if (_groupInvalid)
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
        if (groupId == 0)
        {
            _dropped++;
            PublishCounters(context);
            return;
        }

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
                if (_settings.Header.Length == 0 || _settings.Footer.Length == 0)
                {
                    ReportBadConfig(context);
                    return default;
                }

                // 帧头/帧尾是字节：直接写进字节缓冲，数值按 ASCII 追加。
                var requiredBytes = _settings.Header.Length + _settings.Footer.Length + 2 + (values.Length * 32);
                EnsureEncodeBuffer(requiredBytes);

                var byteCount = TextFrameCodec.Write(
                    _encodeBuffer,
                    values,
                    _settings.Header,
                    _settings.Footer,
                    _settings.Decimals);
                return byteCount <= 0 ? default : _encodeBuffer.AsSpan(0, byteCount);

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
            WaveformToRawText.Get(WaveformToRawText.BadHex),
            HeaderHexInput));
        SetStatus(context, WaveformToRawText.Get(WaveformToRawText.BadHex));
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
        byte[] Header,
        byte[] Footer,
        string Prefix,
        int Decimals,
        int GroupId);
}
