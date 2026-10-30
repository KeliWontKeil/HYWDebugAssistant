using System.Globalization;
using System.Reflection;
using Avalonia.Controls;
using HYWDebugAssistant.Sdk.Configuration;
using HYWDebugAssistant.Sdk.Text;

namespace HYWDebugAssistant.Sdk.Ui.Controls;

/// <summary>
/// **设计时模块上下文**：从模块的配置类型反射出字段，让预览器也能画出配置表单。
/// </summary>
/// <remarks>
/// <para>
/// 运行时的 <see cref="IModuleViewContext"/> 由宿主提供，字段来自组合件里的配置 Schema；
/// 预览器不启动运行时，拿不到这个上下文。于是 <see cref="ModuleView"/> 在设计环境
/// （<see cref="Design.IsDesignMode"/> 或 <see cref="ModuleView.IsPreviewHost"/>）挂一个本类实例：
/// 字段类型与可选项从配置类型自己的标注读；标题 / 说明 / 分组 / 枚举选项的文本从
/// **声明该接入的模块**的表读——和运行时是同一份元数据与同一张表，只是值用默认实例。
/// </para>
/// <para>
/// 它**不是运行时数据源**，也不参与写回：提交/命令全部返回成功但什么也不做。
/// </para>
/// </remarks>
public sealed class DesignModuleViewContext : IModuleViewContext
{
    /// <summary>设计时的样例数字（让读数/占位在预览里有墨；不参与任何业务）。</summary>
    private const double SampleNumber = 1234;

    private readonly Type _configType;
    private readonly Assembly _textOwner;
    private readonly object? _config;

    /// <summary>用配置类型构造设计时上下文。</summary>
    /// <param name="configType">带 <see cref="ConfigurationFieldAttribute"/> 的配置类型。</param>
    /// <param name="textOwner">配置文本的属主程序集（声明该接入的模块）。</param>
    public DesignModuleViewContext(Type configType, Assembly textOwner)
    {
        ArgumentNullException.ThrowIfNull(configType);
        ArgumentNullException.ThrowIfNull(textOwner);

        _configType = configType;
        _textOwner = textOwner;

        try
        {
            _config = Activator.CreateInstance(configType);
        }
        catch (Exception)
        {
            // 设计时不应该因为某个配置类型需要构造参数就整页空白；字段元数据仍可读。
            _config = null;
        }
    }

    /// <inheritdoc />
    public string InstanceId => "design";

    /// <inheritdoc />
    public string MultilineSubmitKey => "CtrlEnter";

    /// <inheritdoc />
    public double? ReadLatest(string source) => SampleNumber;

    /// <inheritdoc />
    public IReadOnlyList<double> ReadSeries(string source) => [SampleNumber];

    /// <inheritdoc />
    public TextBufferSnapshot ReadText(string source) => new([SampleText], 0, 20000, 0);

    /// <inheritdoc />
    public bool SubmitInput(string source, string text) => true;

    /// <inheritdoc />
    public CommandOutcome InvokeCommand(string command) => CommandOutcome.Ok;

    /// <inheritdoc />
    public CommandOutcome UpdateField(string fieldName, string value) => CommandOutcome.Ok;

    /// <inheritdoc />
    public ViewAppearance Appearance(string source) => ViewAppearance.None;

    /// <inheritdoc />
    public string? ReadInputText(string source) => null;

    /// <inheritdoc />
    public void WriteInputText(string source, string text)
    {
        // 设计时没有视图状态；输入仅用于看排版。
    }

    /// <inheritdoc />
    public int? SelectedPage(string stateKey) => null;

    /// <inheritdoc />
    public void SetSelectedPage(string stateKey, int page)
    {
        // 设计时没有页签状态。
    }

    /// <inheritdoc />
    public ConfigFieldView? ReadField(string fieldName)
    {
        if (fieldName.Length == 0)
        {
            return null;
        }

        var property = _configType.GetProperty(
            fieldName,
            BindingFlags.Public | BindingFlags.Instance);

        if (property is null
            || !property.CanRead
            || !property.CanWrite
            || property.GetIndexParameters().Length > 0)
        {
            return null;
        }

        var metadata = property.GetCustomAttribute<ConfigurationFieldAttribute>();
        var value = ReadValue(property);
        var (kind, choices) = Classify(property.PropertyType, _textOwner);

        if (kind is null)
        {
            return null;
        }

        // 动态选项（如串口列表）优先：运行时也是"返回表 ⇒ 下拉"。
        if (_config is IConfigurationChoices provider)
        {
            try
            {
                if (provider.GetChoices(fieldName) is { } dynamic)
                {
                    var list = new List<ConfigChoice>();

                    if (value.Length > 0 && !dynamic.Contains(value, StringComparer.Ordinal))
                    {
                        list.Add(new ConfigChoice(value, value));
                    }

                    foreach (var choice in dynamic)
                    {
                        list.Add(new ConfigChoice(choice, choice));
                    }

                    kind = ConfigFieldKind.Enum;
                    choices = list;
                }
            }
            catch (Exception)
            {
                // 设计时枚举设备失败不是视图错误；保留静态字段外观即可。
            }
        }

        var title = ResolveText(_textOwner, metadata?.TitleKey)
            ?? (string.IsNullOrWhiteSpace(metadata?.Title) ? property.Name : metadata!.Title!);
        var description = ResolveText(_textOwner, metadata?.DescriptionKey)
            ?? (string.IsNullOrWhiteSpace(metadata?.Description) ? null : metadata!.Description);
        var group = ResolveText(_textOwner, metadata?.GroupKey)
            ?? (string.IsNullOrWhiteSpace(metadata?.Group) ? null : metadata!.Group);

        return new ConfigFieldView(
            property.Name,
            title,
            kind.Value,
            value,
            choices,
            metadata?.Unit,
            description,
            group);
    }

    /// <summary>设计时的样例文本（预览里让标签有墨；表不在设计环境可用时为空，由占位框兜住）。</summary>
    private static string SampleText =>
        TextCatalog.TryGet(typeof(DesignModuleViewContext).Assembly, "design.sampleText", out var text)
            ? text
            : string.Empty;

    /// <summary>读属性当前值的文本形态（配置实例构造失败 ⇒ 空串）。</summary>
    private string ReadValue(PropertyInfo property)
    {
        if (_config is null)
        {
            return string.Empty;
        }

        try
        {
            return property.GetValue(_config) switch
            {
                null => string.Empty,
                IFormattable formattable => formattable.ToString(null, CultureInfo.InvariantCulture),
                var value => value.ToString() ?? string.Empty,
            };
        }
        catch (Exception)
        {
            return string.Empty;
        }
    }

    /// <summary>按声明里的文本键取值（拥有者 = 配置文本属主）；未声明或表里没有 ⇒ null。</summary>
    /// <param name="textOwner">配置文本的属主程序集。</param>
    /// <param name="key">文本表键（可空）。</param>
    private static string? ResolveText(Assembly textOwner, string? key) =>
        !string.IsNullOrWhiteSpace(key)
        && TextCatalog.TryGet(textOwner, key, out var text)
            ? text
            : null;

    /// <summary>属性类型 → 编辑器种类 + 枚举选项（与运行时 Schema 的规则同构）。</summary>
    private static (ConfigFieldKind? Kind, IReadOnlyList<ConfigChoice>? Choices) Classify(
        Type type,
        Assembly textOwner)
    {
        var underlying = Nullable.GetUnderlyingType(type) ?? type;

        if (underlying == typeof(bool))
        {
            return (ConfigFieldKind.Boolean, null);
        }

        if (underlying.IsEnum)
        {
            var choices = new List<ConfigChoice>();

            foreach (var name in Enum.GetNames(underlying))
            {
                var metadata = underlying.GetField(name)
                    ?.GetCustomAttribute<ConfigurationChoiceAttribute>();
                var title = ResolveText(textOwner, metadata?.TitleKey)
                    ?? (string.IsNullOrWhiteSpace(metadata?.Title) ? null : metadata!.Title);

                choices.Add(new ConfigChoice(
                    name,
                    string.IsNullOrWhiteSpace(title) ? name : title!));
            }

            return (ConfigFieldKind.Enum, choices);
        }

        if (underlying == typeof(string))
        {
            return (ConfigFieldKind.Text, null);
        }

        if (underlying == typeof(byte) || underlying == typeof(sbyte)
            || underlying == typeof(short) || underlying == typeof(ushort)
            || underlying == typeof(int) || underlying == typeof(uint)
            || underlying == typeof(long) || underlying == typeof(ulong))
        {
            return (ConfigFieldKind.WholeNumber, null);
        }

        if (underlying == typeof(float) || underlying == typeof(double) || underlying == typeof(decimal))
        {
            return (ConfigFieldKind.Number, null);
        }

        return (null, null);
    }
}
