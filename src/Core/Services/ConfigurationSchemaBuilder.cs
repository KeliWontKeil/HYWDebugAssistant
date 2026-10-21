using System.Globalization;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Nodes;
using HYWDebugAssistant.Core.Api;
using HYWDebugAssistant.Core.Persistence;
using HYWDebugAssistant.Sdk.Configuration;

namespace HYWDebugAssistant.Core.Services;

/// <summary>
/// 配置 **Schema 生成与按字段写回**（Q5-5：配置 = C# 类型 → 表单由框架生成）。
/// </summary>
/// <remarks>
/// <list type="bullet">
///   <item><b>反射只发生在配置类型上</b>：协议面（口/视图/网络）零反射；配置类型由模块定义、
///     框架编译期不可知，v1 明确取舍（与 <c>ConfigurationJson</c> 同一取舍）；</item>
///   <item><b>Schema 不含代码化标识</b>：字段标题缺省由属性名生成（<c>PeriodMs</c> → "Period Ms"），
///     类型完整名、<c>ModuleId</c> 字面量不出现在界面（主文档 §10.4）；</item>
///   <item><b>写回走 JSON 中转</b>：先把当前配置序列化成 <see cref="JsonNode"/>，改字段后再反序列化成
///     **新实例** —— 绝不就地修改运行中的配置对象（变更入口要求 Prepare 零副作用）；</item>
///   <item>写回经 L4 变更入口（<c>UpdateConfigurationsOperation</c>）→ 原子应用或整体拒绝，并触发一次
///     配置变更通知。</item>
/// </list>
/// </remarks>
internal static class ConfigurationSchemaBuilder
{
    /// <summary>按实例的配置快照生成 Schema。</summary>
    /// <param name="instanceId">实例 ID。</param>
    /// <param name="snapshot">配置快照（键 = 配置类型完整名）。</param>
    public static ConfigurationSchemaView Build(string instanceId, IReadOnlyDictionary<string, object> snapshot)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(instanceId);
        ArgumentNullException.ThrowIfNull(snapshot);

        if (snapshot.Count == 0)
        {
            // 无配置类型的模块是**合法**的：表单为空并说明原因，不假装有字段
            return new ConfigurationSchemaView(instanceId, [], "该模块没有可编辑的配置项");
        }

        var fields = new List<ConfigurationFieldView>();
        foreach (var (_, configuration) in snapshot.OrderBy(pair => pair.Key, StringComparer.Ordinal))
        {
            foreach (var property in configuration.GetType().GetProperties(BindingFlags.Public | BindingFlags.Instance))
            {
                if (!property.CanRead || !property.CanWrite || property.GetIndexParameters().Length > 0)
                {
                    continue;
                }

                if (TryDescribe(property, configuration, out var field))
                {
                    fields.Add(field);
                }
            }
        }

        return fields.Count == 0
            ? new ConfigurationSchemaView(instanceId, [], "该模块的配置项均不可编辑")
            : new ConfigurationSchemaView(instanceId, fields);
    }

    /// <summary>
    /// 按字段名生成**新的配置对象**（不修改传入对象）。
    /// </summary>
    /// <param name="snapshot">当前配置快照。</param>
    /// <param name="fieldName">字段名。</param>
    /// <param name="text">新值的文本形态。</param>
    /// <param name="updated">生成的新配置对象（<see langword="false"/> 时为空）。</param>
    /// <param name="failure">失败原因。</param>
    public static bool TryCreateUpdated(
        IReadOnlyDictionary<string, object> snapshot,
        string fieldName,
        string text,
        out object? updated,
        out string? failure)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        ArgumentException.ThrowIfNullOrWhiteSpace(fieldName);

        updated = null;
        failure = null;

        // 先定位字段：0 个 = 未知字段；多于 1 个 = 歧义（拒绝，不猜改哪一份配置）
        var matches = new List<(Type Type, object Configuration, PropertyInfo Property)>();
        foreach (var (_, configuration) in snapshot.OrderBy(pair => pair.Key, StringComparer.Ordinal))
        {
            var type = configuration.GetType();
            var property = type.GetProperty(fieldName, BindingFlags.Public | BindingFlags.Instance);
            if (property is { CanWrite: true })
            {
                matches.Add((type, configuration, property));
            }
        }

        if (matches.Count == 0)
        {
            failure = $"配置中没有可写字段：{fieldName}";
            return false;
        }

        if (matches.Count > 1)
        {
            failure = $"字段名在多份配置中重复：{fieldName}（无法判定改哪一份）";
            return false;
        }

        var (targetType, current, targetProperty) = matches[0];

        // JSON 中转：序列化当前值 → 覆盖一个属性 → 反序列化为**新实例**
        if (JsonSerializer.SerializeToNode(current, targetType, ConfigurationJson.Options) is not JsonObject node)
        {
            failure = "当前配置无法序列化为对象（无法按字段写回）";
            return false;
        }

        if (!TryConvert(targetProperty.PropertyType, text, out var jsonValue, out var reason))
        {
            failure = $"{DisplayName(targetProperty)}：{reason}";
            return false;
        }

        node[targetProperty.Name] = jsonValue;

        try
        {
            updated = node.Deserialize(targetType, ConfigurationJson.Options);
        }
        catch (Exception ex) when (ex is JsonException or NotSupportedException)
        {
            failure = $"写回后配置无法解析：{ex.Message}";
            return false;
        }

        if (updated is null)
        {
            failure = "写回后配置为空（已拒绝）";
            return false;
        }

        return true;
    }

    // ---------------------------------------------------------------- 字段描述

    private static bool TryDescribe(PropertyInfo property, object configuration, out ConfigurationFieldView field)
    {
        field = null!;

        var type = property.PropertyType;
        var metadata = property.GetCustomAttribute<ConfigurationFieldAttribute>();
        var title = string.IsNullOrWhiteSpace(metadata?.Title) ? DisplayName(property) : metadata!.Title!;

        var (kind, choices) = Classify(type);
        if (kind is null)
        {
            return false;   // 非可编辑类型（复杂对象等）：不生成字段
        }

        var current = Format(property.GetValue(configuration), type);

        // **动态可选项**：配置对象自己知道"现在有哪些"（如本机当前可用的外部设备）⇒ 该字段改为**下拉**呈现。
        // 返回 `null` 才表示"这个字段不是动态字段"；**空表表示"是动态字段但现在没有可选项"**（两件事不能混同）。
        // 当前值必须留在选项里：否则表单会把已存的值显示成列表里的第一项 —— 看着像被改过。
        if (configuration is IConfigurationChoices provider && provider.GetChoices(property.Name) is { } dynamic)
        {
            var list = new List<ConfigurationFieldChoice>();

            if (current.Length > 0 && !dynamic.Contains(current, StringComparer.Ordinal))
            {
                list.Add(new ConfigurationFieldChoice(current, current));
            }

            foreach (var value in dynamic)
            {
                list.Add(new ConfigurationFieldChoice(value, value));
            }

            kind = ConfigurationFieldKind.Enum;
            choices = list;
        }

        field = new ConfigurationFieldView(
            property.Name,
            title,
            string.IsNullOrWhiteSpace(metadata?.Group) ? null : metadata!.Group,
            kind.Value,
            current,
            OrNull(metadata?.Minimum),
            OrNull(metadata?.Maximum),
            OrNull(metadata?.Step),
            string.IsNullOrWhiteSpace(metadata?.Unit) ? null : metadata!.Unit,
            string.IsNullOrWhiteSpace(metadata?.Description) ? null : metadata!.Description,
            choices);

        return true;
    }

    /// <summary>标注里的 <see cref="double.NaN"/> = 未设置（特性参数只能是常量，故用 NaN 表达"不限制"）。</summary>
    private static double? OrNull(double? value) =>
        value is { } number && !double.IsNaN(number) ? number : null;

    private static (ConfigurationFieldKind? Kind, IReadOnlyList<ConfigurationFieldChoice>? Choices) Classify(Type type)
    {
        var underlying = Nullable.GetUnderlyingType(type) ?? type;

        if (underlying == typeof(bool))
        {
            return (ConfigurationFieldKind.Boolean, null);
        }

        if (underlying.IsEnum)
        {
            var choices = new List<ConfigurationFieldChoice>();
            foreach (var name in Enum.GetNames(underlying))
            {
                // **写回用成员名，显示可用标注**（`[ConfigurationChoice]`）：两者合一会让界面里
                // 只能看到标识符（"Baud115200"），而人该看到的是"115200"。
                var title = underlying.GetField(name)?.GetCustomAttribute<ConfigurationChoiceAttribute>()?.Title;

                choices.Add(new ConfigurationFieldChoice(
                    name,
                    string.IsNullOrWhiteSpace(title) ? name : title!));
            }

            return (ConfigurationFieldKind.Enum, choices);
        }

        if (underlying == typeof(string))
        {
            return (ConfigurationFieldKind.Text, null);
        }

        if (underlying == typeof(byte) || underlying == typeof(sbyte)
            || underlying == typeof(short) || underlying == typeof(ushort)
            || underlying == typeof(int) || underlying == typeof(uint)
            || underlying == typeof(long) || underlying == typeof(ulong))
        {
            return (ConfigurationFieldKind.WholeNumber, null);
        }

        if (underlying == typeof(float) || underlying == typeof(double) || underlying == typeof(decimal))
        {
            return (ConfigurationFieldKind.Number, null);
        }

        return (null, null);
    }

    /// <summary>把文本解析为 JSON 值（按目标类型；失败即拒绝，不猜测）。</summary>
    private static bool TryConvert(Type type, string text, out JsonNode? node, out string? failure)
    {
        node = null;
        failure = null;

        var underlying = Nullable.GetUnderlyingType(type) ?? type;
        var trimmed = text.Trim();

        if (underlying == typeof(string))
        {
            node = JsonValue.Create(text);
            return true;
        }

        if (underlying == typeof(bool))
        {
            if (bool.TryParse(trimmed, out var boolean))
            {
                node = JsonValue.Create(boolean);
                return true;
            }

            failure = "布尔值必须是 true 或 false";
            return false;
        }

        if (underlying.IsEnum)
        {
            if (Enum.TryParse(underlying, trimmed, ignoreCase: true, out var parsed) && parsed is not null)
            {
                node = JsonValue.Create(parsed.ToString());
                return true;
            }

            failure = $"可选项必须是：{string.Join(" / ", Enum.GetNames(underlying))}";
            return false;
        }

        if (underlying == typeof(float) || underlying == typeof(double) || underlying == typeof(decimal))
        {
            if (double.TryParse(trimmed, NumberStyles.Float, CultureInfo.InvariantCulture, out var number))
            {
                node = JsonValue.Create(number);
                return true;
            }

            failure = "数值格式不正确（示例：3.14）";
            return false;
        }

        if (underlying == typeof(byte) || underlying == typeof(sbyte)
            || underlying == typeof(short) || underlying == typeof(ushort)
            || underlying == typeof(int) || underlying == typeof(uint)
            || underlying == typeof(long) || underlying == typeof(ulong))
        {
            if (long.TryParse(trimmed, NumberStyles.Integer, CultureInfo.InvariantCulture, out var integer))
            {
                node = JsonValue.Create(integer);
                return true;
            }

            failure = "整数格式不正确（示例：100）";
            return false;
        }

        failure = $"不支持的字段类型：{underlying.Name}";
        return false;
    }

    /// <summary>把当前值格式化为**可编辑文本**（数值用不受区域设置影响的写法，便于原样写回）。</summary>
    private static string Format(object? value, Type type)
    {
        if (value is null)
        {
            return string.Empty;
        }

        return value switch
        {
            bool boolean => boolean ? "true" : "false",
            double number => number.ToString("R", CultureInfo.InvariantCulture),
            float number => number.ToString("R", CultureInfo.InvariantCulture),
            decimal number => number.ToString(CultureInfo.InvariantCulture),
            IFormattable formattable => formattable.ToString(null, CultureInfo.InvariantCulture),
            _ => value.ToString() ?? string.Empty,
        };
    }

    /// <summary>由字段名生成显示标题（<c>PeriodMs</c> → "Period Ms"；有标注时以标注为准）。</summary>
    private static string DisplayName(PropertyInfo property)
    {
        var name = property.Name;
        var builder = new System.Text.StringBuilder(name.Length + 4);

        for (var i = 0; i < name.Length; i++)
        {
            var current = name[i];
            if (i > 0 && char.IsUpper(current) && !char.IsUpper(name[i - 1]))
            {
                _ = builder.Append(' ');
            }

            _ = builder.Append(current);
        }

        return builder.ToString();
    }
}
