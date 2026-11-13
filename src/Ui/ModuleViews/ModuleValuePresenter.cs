using HYWDebugAssistant.Core.Api;
using HYWDebugAssistant.Sdk.Text;
using HYWDebugAssistant.Sdk.Ui;
using HYWDebugAssistant.Sdk.Values;

namespace HYWDebugAssistant.Ui.ModuleViews;

/// <summary>
/// **值呈现层**：把值名翻译成控制器能画的状态，把控制器的提交落到配置/视图状态。
/// </summary>
/// <remarks>
/// 这一层是控件与存储之间的唯一适配器：控件只看到 <see cref="ValueState"/>，
/// 不需要知道值最终在配置对象里还是视图状态里。`ModuleViewContext` 只做转发。
/// </remarks>
internal sealed class ModuleValuePresenter
{
    private readonly string _instanceId;
    private readonly Func<string, ConfigFieldView?> _readField;
    private readonly Func<string, ViewAppearance> _appearance;
    private readonly IRuntimeQuery _query;
    private readonly IRuntimeCommand _command;
    private readonly IViewStateAccess? _viewState;
    private readonly Dictionary<string, ValueNotice> _notices = new(StringComparer.Ordinal);

    internal ModuleValuePresenter(
        string instanceId,
        Func<string, ConfigFieldView?> readField,
        Func<string, ViewAppearance> appearance,
        IRuntimeQuery query,
        IRuntimeCommand command,
        IViewStateAccess? viewState)
    {
        ArgumentException.ThrowIfNullOrEmpty(instanceId);
        ArgumentNullException.ThrowIfNull(readField);
        ArgumentNullException.ThrowIfNull(appearance);
        ArgumentNullException.ThrowIfNull(query);
        ArgumentNullException.ThrowIfNull(command);

        _instanceId = instanceId;
        _readField = readField;
        _appearance = appearance;
        _query = query;
        _command = command;
        _viewState = viewState;
    }

    /// <summary>读一个值的当前呈现状态。</summary>
    internal ValueState ReadValue(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return ValueState.Empty(string.Empty);
        }

        var field = _readField(name);
        if (field is not null)
        {
            var appearance = _appearance(name);
            return new ValueState(
                name,
                Translate(field.Kind),
                field.Value,
                field.Value,
                field.Choices?.Select(choice => new ValueChoice(choice.Value, choice.Title)).ToArray(),
                appearance.ReadOnly is true,
                appearance.Enabled is not false,
                EffectiveNotice(name),
                0,
                IsKnown: true);
        }

        if (!_query.TryGetValueDeclaration(_instanceId, name, out var declaration) || declaration is null)
        {
            return ValueState.Empty(name);
        }

        var valueAppearance = _appearance(name);
        var value = _viewState?.GetInputText(_instanceId, name) ?? string.Empty;

        return new ValueState(
            name,
            declaration.Editor,
            value,
            string.Empty,
            declaration.Choices,
            valueAppearance.ReadOnly is true,
            valueAppearance.Enabled is not false,
            EffectiveNotice(name),
            0,
            IsKnown: true);
    }

    /// <summary>把一个编辑结果交给后端；失败结论只影响该值。</summary>
    internal void CommitValue(string name, string text)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            SetNotice(name, new ValueNotice(
                ValueNoticeSeverity.Error,
                TextCatalog.Get(typeof(ModuleValuePresenter).Assembly, "ui.moduleView.valueNameMissing")));
            return;
        }

        if (_readField(name) is not null)
        {
            var result = _command.UpdateConfigurationField(_instanceId, name, text);

            SetNotice(
                name,
                result.Succeeded
                    ? null
                    : new ValueNotice(ValueNoticeSeverity.Error, string.Join("；", result.Failures)));
            return;
        }

        if (!_query.TryGetValueDeclaration(_instanceId, name, out var declaration) || declaration is null)
        {
            SetNotice(name, new ValueNotice(
                ValueNoticeSeverity.Error,
                TextCatalog.Get(typeof(ModuleValuePresenter).Assembly, "ui.moduleView.valueUnknown", name)));
            return;
        }

        _viewState?.SetInputText(_instanceId, name, text);

        // 离散值（布尔/枚举）同时投一次输入事件：模块可以像过去一样在收件箱里看到“拨到了哪”，
        // 也可以只读值。文本不逐键投递，避免把有界收件箱淹掉。
        if (declaration.Editor is ValueEditor.Boolean or ValueEditor.Enum)
        {
            _ = _command.SubmitInput(_instanceId, name, text);
        }

        SetNotice(name, null);
    }

    /// <summary>登记 XAML 初值（仅当当前没有值）；正常由运行时在激活前播种。</summary>
    internal void RegisterViewDefault(string name, string value)
    {
        if (string.IsNullOrWhiteSpace(name)
            || !_query.TryGetValueDeclaration(_instanceId, name, out var declaration)
            || declaration is null)
        {
            return;
        }

        if (_viewState?.GetInputText(_instanceId, name) is null)
        {
            _viewState?.SetInputText(_instanceId, name, value);
        }
    }

    private ValueNotice? EffectiveNotice(string name) =>
        _notices.TryGetValue(name, out var local)
            ? local
            : _query.TryGetValueNotice(_instanceId, name, out var notice) ? notice : null;

    private void SetNotice(string name, ValueNotice? notice)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return;
        }

        if (notice is null)
        {
            _ = _notices.Remove(name);
        }
        else
        {
            _notices[name] = notice;
        }
    }

    private static ValueEditor Translate(ConfigFieldKind kind) => kind switch
    {
        ConfigFieldKind.Boolean => ValueEditor.Boolean,
        ConfigFieldKind.WholeNumber or ConfigFieldKind.Number => ValueEditor.Number,
        ConfigFieldKind.Enum => ValueEditor.Enum,
        _ => ValueEditor.Text,
    };
}
