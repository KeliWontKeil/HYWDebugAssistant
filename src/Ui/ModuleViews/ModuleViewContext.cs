using System.Globalization;
using Avalonia.Controls;
using HYWDebugAssistant.Core.Api;
using HYWDebugAssistant.Sdk.Ui;
using HYWDebugAssistant.Sdk.Views;


using HYWDebugAssistant.Sdk.Text;

namespace HYWDebugAssistant.Ui.ModuleViews;

/// <summary>
/// **模块视图的上下文实现**：把视图要的东西翻译成框架的三件服务（取数 / 命令 / 视图状态）。
/// </summary>
/// <remarks>
/// <para>
/// 它是"领域 ↔ 模块界面"之间唯一的一层：模块视图只说"我要 <c>rxCount</c> 的值"、
/// "把这段文本交给 <c>text1</c>"、"触发 <c>open</c>"，翻译全在这里。
/// </para>
/// <para>
/// <b>取数的解码口径只有一处</b>（<see cref="SurfaceSourceReader"/>）：同一个源在哪个控件里
/// 都必须算出同一个数，否则"界面显示的值"取决于它长在哪种控件上。
/// </para>
/// <para>
/// 配置字段的呈现**每次现取**（不缓存）：配置变更随时可能发生，而一次字段读取是内存里的对象查找；
/// 缓存它只会造出"界面显示旧值"的窗口。
/// </para>
/// </remarks>
internal sealed class ModuleViewContext : IModuleViewContext
{
    private readonly IRuntimeQuery _query;
    private readonly IRuntimeCommand _command;
    private readonly IViewStateAccess? _viewState;
    private readonly Func<string>? _multilineSubmitKey;
    private readonly SurfaceSourceReader _reader;
    private readonly Dictionary<string, ViewAppearance> _appearances = new(StringComparer.Ordinal);
    private long _appearanceRevision = -1;

    /// <summary>造一个视图上下文。</summary>
    /// <param name="instanceId">实例 ID。</param>
    /// <param name="query">运行期查询面。</param>
    /// <param name="command">运行期命令面。</param>
    /// <param name="viewState">视图状态（无 ⇒ 未提交文本与页面状态只记在本地）。</param>
    /// <param name="multilineSubmitKey">多行提交键档位提供者（设置随时会变，故传取值函数）。</param>
    internal ModuleViewContext(
        string instanceId,
        IRuntimeQuery query,
        IRuntimeCommand command,
        IViewStateAccess? viewState,
        Func<string>? multilineSubmitKey)
    {
        ArgumentException.ThrowIfNullOrEmpty(instanceId);
        ArgumentNullException.ThrowIfNull(query);
        ArgumentNullException.ThrowIfNull(command);

        InstanceId = instanceId;
        _query = query;
        _command = command;
        _viewState = viewState;
        _multilineSubmitKey = multilineSubmitKey;
        _reader = new SurfaceSourceReader(query, viewState);
    }

    /// <inheritdoc />
    public string InstanceId { get; }

    /// <inheritdoc />
    public string MultilineSubmitKey => _multilineSubmitKey?.Invoke() ?? "CtrlEnter";

    /// <summary>
    /// 每帧开头刷新一次数据源快照（数据源视图对象是每帧重建的，不能跨帧持有），
    /// 并把本实例的**外观**取一次（元素逐个查表，而不是逐个元素各取一次全量快照）。
    /// </summary>
    /// <param name="sources">本帧的订阅源快照（由窗口集统一取一次；<see langword="null"/> = 自己取）。</param>
    internal void BeginFrame(IReadOnlyCollection<SubscriptionSourceView>? sources = null)
    {
        _reader.RefreshSources(sources);
        BuildAppearances();
    }

    /// <inheritdoc />
    public double? ReadLatest(string source) => _reader.ReadLatest(InstanceId, source);

    /// <inheritdoc />
    public IReadOnlyList<double> ReadSeries(string source) => _reader.ReadSeries(InstanceId, source);

    /// <summary>把外观快照转成按源名的查表（每帧一次）。</summary>
    private void BuildAppearances()
    {
        var revision = _query.GetAppearanceRevision(InstanceId);

        if (revision == _appearanceRevision)
        {
            return;
        }

        _appearanceRevision = revision;
        _appearances.Clear();

        foreach (var appearance in _query.GetAppearance(InstanceId))
        {
            if (appearance.SourceName is not { Length: > 0 } source)
            {
                continue;
            }

            // **角色词 → token 的解析只在宿主这一处**：控件库只认 token，不维护第二张映射表。
            _ = HYWDebugAssistant.Ui.Theming.PaletteManifest.TryRoleKey(appearance.OutlineRole, out var outlineToken);
            _ = HYWDebugAssistant.Ui.Theming.PaletteManifest.TryRoleKey(appearance.TextRole, out var textToken);

            _appearances[source] = new ViewAppearance
            {
                Outline = appearance.Outline,
                OutlineToken = outlineToken.Length > 0 ? outlineToken : null,
                Text = appearance.Text,
                TextToken = textToken.Length > 0 ? textToken : null,
                TextSize = appearance.TextSize,
                ReadOnly = appearance.ReadOnly,
                Enabled = appearance.Enabled,
            };
        }
    }

    /// <inheritdoc />
    public TextBufferSnapshot ReadText(string source)
    {
        var buffer = _reader.ReadBuffer(InstanceId, source);

        return new TextBufferSnapshot(buffer.Lines, buffer.Dropped, buffer.Capacity, buffer.Revision);
    }

    /// <inheritdoc />
    public long ReadTextRevision(string source) =>
        source.Length == 0 ? 0 : _reader.ReadRevision(InstanceId, source);

    /// <inheritdoc />
    public bool TryReadTextWindow(
        string source,
        in TextCursor cursor,
        List<string> sealedLines,
        int maxChars,
        out TextWindow window)
    {
        ArgumentNullException.ThrowIfNull(sealedLines);

        if (source.Length == 0)
        {
            window = default;
            return false;
        }

        window = _reader.ReadWindow(InstanceId, source, cursor, sealedLines, maxChars);
        return true;
    }

    /// <inheritdoc />
    public bool TryReadTextTail(string source, int maxLines, int maxChars, out TextTailSnapshot tail)
    {
        if (source.Length == 0)
        {
            tail = default;
            return false;
        }

        var buffer = _reader.ReadTail(InstanceId, source, maxLines, maxChars, out var cursor);

        tail = new TextTailSnapshot(
            new TextBufferSnapshot(buffer.Lines, buffer.Dropped, buffer.Capacity, buffer.Revision),
            cursor);

        return true;
    }

    /// <inheritdoc />
    public bool SubmitInput(string source, string text)
    {
        if (source.Length == 0)
        {
            return false;
        }

        var result = _command.SubmitInput(InstanceId, source, text);

        // **不清输入框状态**：它保存的是"用户此刻编辑的内容"，不是"待提交的队列项"。
        // 提交只是把这份内容另抄一份交给模块；清掉会让视图重建后丢失用户刚打的字。
        return result.Succeeded;
    }

    /// <inheritdoc />
    public CommandOutcome InvokeCommand(string command)
    {
        if (command.Length == 0)
        {
            return CommandOutcome.Fail(TextCatalog.Get(
                typeof(ModuleViewContext).Assembly,
                "ui.moduleView.commandNameMissing"));
        }

        var result = _command.SendCommand(InstanceId, command);

        return result.Succeeded
            ? new CommandOutcome(false, string.Join("；", result.Notices))
            : new CommandOutcome(true, string.Join("；", result.Failures));
    }

    /// <inheritdoc />
    public ConfigFieldView? ReadField(string fieldName)
    {
        if (fieldName.Length == 0 || !_query.TryGetConfigurationSchema(InstanceId, out var schema) || schema is null)
        {
            return null;
        }

        foreach (var field in schema.Fields)
        {
            if (!string.Equals(field.Name, fieldName, StringComparison.Ordinal))
            {
                continue;
            }

            return new ConfigFieldView(
                field.Name,
                field.Title,
                Translate(field.Kind),
                field.Value,
                field.Choices?.Select(choice => new ConfigChoice(choice.Value, choice.Title)).ToArray(),
                field.Unit,
                field.Description,
                field.Group);
        }

        return null;
    }

    /// <inheritdoc />
    public CommandOutcome UpdateField(string fieldName, string value)
    {
        var result = _command.UpdateConfigurationField(InstanceId, fieldName, value);

        return result.Succeeded
            ? new CommandOutcome(false, string.Join("；", result.Notices))
            : new CommandOutcome(true, string.Join("；", result.Failures));
    }

    /// <inheritdoc />
    public ViewAppearance Appearance(string source)
    {
        if (source.Length == 0)
        {
            return ViewAppearance.None;
        }

        return _appearances.TryGetValue(source, out var appearance) ? appearance : ViewAppearance.None;
    }

    /// <inheritdoc />
    public string? ReadInputText(string source) =>
        source.Length == 0 ? null : _viewState?.GetInputText(InstanceId, source);

    /// <inheritdoc />
    public void WriteInputText(string source, string text)
    {
        if (source.Length > 0)
        {
            _viewState?.SetInputText(InstanceId, source, text);
        }
    }

    /// <inheritdoc />
    public int? SelectedPage(string stateKey) =>
        stateKey.Length == 0 ? null : _viewState?.GetSelectedPage(InstanceId, stateKey);

    /// <inheritdoc />
    public void SetSelectedPage(string stateKey, int page)
    {
        if (stateKey.Length > 0)
        {
            _viewState?.SetSelectedPage(InstanceId, stateKey, page);
        }
    }

    /// <summary>框架的字段种类 → 界面契约的种类。</summary>
    /// <param name="kind">框架侧种类。</param>
    private static ConfigFieldKind Translate(ConfigurationFieldKind kind) => kind switch
    {
        ConfigurationFieldKind.Boolean => ConfigFieldKind.Boolean,
        ConfigurationFieldKind.WholeNumber => ConfigFieldKind.WholeNumber,
        ConfigurationFieldKind.Number => ConfigFieldKind.Number,
        ConfigurationFieldKind.Enum => ConfigFieldKind.Enum,
        _ => ConfigFieldKind.Text,
    };
}
