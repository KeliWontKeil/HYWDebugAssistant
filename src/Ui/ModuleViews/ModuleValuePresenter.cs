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
    private readonly Dictionary<string, ValueValidity> _validities = new(StringComparer.Ordinal);

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
                ValueEditorMapping.ToEditor(field.Kind),
                field.Value,
                field.Value,
                field.Choices?.Select(choice => new ValueChoice(choice.Value, choice.Title)).ToArray(),
                appearance.ReadOnly is true,
                appearance.Enabled is not false,
                EffectiveValidity(name),
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
            EffectiveValidity(name),
            0,
            IsKnown: true);
    }

    /// <summary>把正在编辑的文本交给值层：配置字段只投递输入事件，声明值写视图状态。</summary>
    /// <remarks>
    /// 配置字段的“当前文本”归控件所有；模块在 <c>OnInput(source, text)</c> 里接收它，
    /// 不通过值面读取，也不在键入过程中写配置。声明值仍以视图状态为存放处。
    /// </remarks>
    internal void PreviewValue(string name, string text)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return;
        }

        // 配置字段不是声明值：它们要靠输入事件知道“这一格正在被编辑”。
        if (_readField(name) is not null)
        {
            _ = _command.SubmitInput(_instanceId, name, text);
            return;
        }

        _viewState?.SetInputText(_instanceId, name, text);
    }

    /// <summary>把一个编辑结果交给后端；失败结论只影响该值。</summary>
    internal void CommitValue(string name, string text)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            SetValidity(name, ValueValidity.Error(
                TextCatalog.Get(typeof(ModuleValuePresenter).Assembly, "ui.moduleView.valueNameMissing")));
            return;
        }

        if (_readField(name) is { } field)
        {
            // 模块已把当前文本判为无效（红框/悬停原因）时不写配置：
            // 非法输入必须保留上一份有效值，不能因为失焦/回车把它提交进去。
            if (_query.TryGetValueValidity(_instanceId, name, out var moduleValidity)
                && moduleValidity.IsInvalid)
            {
                return;
            }

            var result = _command.UpdateConfigurationField(_instanceId, name, text);

            SetValidity(
                name,
                result.Succeeded
                    ? ValueValidity.Valid
                    : ValueValidity.Error(string.Join("；", result.Failures)));

            // 文本字段已在 PreviewValue 逐键投递；离散配置字段（下拉/开关）在提交时补一次，
            // 让网络模块的地址/端口选择也能走同一事件驱动校验。
            if (result.Succeeded && field.Kind is not ConfigFieldKind.Text)
            {
                _ = _command.SubmitInput(_instanceId, name, text);
            }

            return;
        }

        if (!_query.TryGetValueDeclaration(_instanceId, name, out var declaration) || declaration is null)
        {
            SetValidity(name, ValueValidity.Error(
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

        SetValidity(name, ValueValidity.Valid);
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

    private ValueValidity EffectiveValidity(string name) =>
        _validities.TryGetValue(name, out var local)
            ? local
            : _query.TryGetValueValidity(_instanceId, name, out var validity)
                ? validity
                : ValueValidity.Valid;

    private void SetValidity(string name, ValueValidity validity)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return;
        }

        if (validity == ValueValidity.Valid)
        {
            _ = _validities.Remove(name);
        }
        else
        {
            _validities[name] = validity;
        }
    }
}
