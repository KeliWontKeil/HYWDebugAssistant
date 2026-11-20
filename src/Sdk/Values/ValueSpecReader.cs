using System.Globalization;
using System.Reflection;
using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace HYWDebugAssistant.Sdk.Values;

/// <summary>一个值在视图 XAML 中声明的初值。</summary>
/// <param name="Name">值名。</param>
/// <param name="Value">用户实例化新模块时的值。</param>
public sealed record ValueInitialSpec(string Name, string Value);

/// <summary>值初值读取的错误类别。</summary>
public enum ValueSpecErrorCode
{
    /// <summary>声明的 XAML 文件不存在/不可读。</summary>
    FileMissing,

    /// <summary>XML 语法错误。</summary>
    XmlInvalid,

    /// <summary>值名缺失或无法解析。</summary>
    ValueMissing,

    /// <summary>`{x:Static}` 指向的类型/成员无法解析。</summary>
    StaticUnresolved,

    /// <summary>同一个值名在多个控件/文件里重复声明。</summary>
    DuplicateValue,
}

/// <summary>一条值初值读取错误。</summary>
/// <param name="Code">错误类别。</param>
/// <param name="FilePath">出错的 XAML 文件（未知为 null）。</param>
/// <param name="Detail">可读细节。</param>
public sealed record ValueSpecError(ValueSpecErrorCode Code, string? FilePath, string Detail);

/// <summary>值初值读取结果。</summary>
/// <param name="Specs">成功解析的值初值。</param>
/// <param name="Errors">错误（非空 ⇒ 宿主应拒绝该包并报 Issue）。</param>
public sealed record ValueSpecReadResult(
    IReadOnlyList<ValueInitialSpec> Specs,
    IReadOnlyList<ValueSpecError> Errors)
{
    /// <summary>是否没有错误。</summary>
    public bool Succeeded => Errors.Count == 0;
}

/// <summary>
/// **从模块视图 XAML 读取值初值**（只依赖 BCL，不需要 Avalonia）。
/// </summary>
/// <remarks>
/// <para>
/// 视图可见值的默认值只有一个来源：XAML。这个读取器把每个输入控件的
/// <c>Value</c> 与它的初值属性（<c>Text</c> / <c>IsChecked</c> / <c>SelectedKey</c> /
/// <c>InitialValue</c>）读成 <see cref="ValueInitialSpec"/>，由运行时在激活前播种。
/// </para>
/// <para>
/// 只识别本库的输入控件；<c>ModuleTextSurface Mode="Display"</c> 与没有 <c>Value</c> 的元素跳过。
/// 解析错误不抛异常，由包加载器转成拒绝原因。
/// </para>
/// </remarks>
public static class ValueSpecReader
{
    private static readonly Regex StaticExpression = new(
        @"^\{\s*x:Static\s+(?<prefix>[A-Za-z_][A-Za-z0-9_.-]*):(?<type>[A-Za-z_][A-Za-z0-9_.]*)\s*\.\s*(?<member>[A-Za-z_][A-Za-z0-9_]*)\s*\}$",
        RegexOptions.CultureInvariant | RegexOptions.Compiled);

    private static readonly HashSet<string> InputControlNames = new(StringComparer.Ordinal)
    {
        "ModuleTextSurface",
        "ModuleToggle",
        "ModuleCheckBox",
        "ModuleSelect",
        "ModuleSegmentedControl",
    };

    /// <summary>读取一个目录下的全部 `*.axaml`（递归；目录不存在 ⇒ 空结果）。</summary>
    /// <param name="directory">包内的 `views/` 目录。</param>
    /// <param name="entryAssembly">模块入口程序集（用于解析 `x:Static`）。</param>
    public static ValueSpecReadResult ReadDirectory(string directory, Assembly entryAssembly)
    {
        ArgumentNullException.ThrowIfNull(entryAssembly);

        if (string.IsNullOrWhiteSpace(directory) || !Directory.Exists(directory))
        {
            return new ValueSpecReadResult([], []);
        }

        var files = Directory.EnumerateFiles(directory, "*.axaml", SearchOption.AllDirectories)
            .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
            .ToList();

        return ReadFiles(files, entryAssembly);
    }

    /// <summary>读取单个 XAML 文件。</summary>
    /// <param name="path">XAML 路径。</param>
    /// <param name="entryAssembly">模块入口程序集。</param>
    public static ValueSpecReadResult ReadFile(string path, Assembly entryAssembly)
    {
        ArgumentNullException.ThrowIfNull(entryAssembly);
        return ReadFiles([path], entryAssembly);
    }

    private static ValueSpecReadResult ReadFiles(IReadOnlyList<string> files, Assembly entryAssembly)
    {
        var specs = new List<ValueInitialSpec>();
        var errors = new List<ValueSpecError>();
        var names = new HashSet<string>(StringComparer.Ordinal);

        foreach (var file in files)
        {
            if (!File.Exists(file))
            {
                errors.Add(new ValueSpecError(ValueSpecErrorCode.FileMissing, file, file));
                continue;
            }

            XDocument document;
            try
            {
                document = XDocument.Load(file, LoadOptions.SetLineInfo);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Xml.XmlException)
            {
                errors.Add(new ValueSpecError(ValueSpecErrorCode.XmlInvalid, file, ex.Message));
                continue;
            }

            foreach (var element in document.Descendants()
                         .Where(element => InputControlNames.Contains(element.Name.LocalName)))
            {
                var spec = ReadElement(element, file, entryAssembly, errors);
                if (spec is null)
                {
                    continue;
                }

                if (!names.Add(spec.Name))
                {
                    errors.Add(new ValueSpecError(ValueSpecErrorCode.DuplicateValue, file, spec.Name));
                    continue;
                }

                specs.Add(spec);
            }
        }

        return new ValueSpecReadResult(specs, errors);
    }

    private static ValueInitialSpec? ReadElement(
        XElement element,
        string file,
        Assembly entryAssembly,
        List<ValueSpecError> errors)
    {
        var controlName = element.Name.LocalName;

        // 显示面的 Source 不是值；没有 Value 的控件也不产生值初值。
        if (string.Equals(controlName, "ModuleTextSurface", StringComparison.Ordinal)
            && ReadLiteral(element, "Mode") is { Length: > 0 } mode
            && !string.Equals(mode, "Input", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        var name = ReadAttribute(element, "Value", entryAssembly, file, errors);
        if (string.IsNullOrWhiteSpace(name))
        {
            return null;
        }

        var (attributeName, fallback) = controlName switch
        {
            "ModuleTextSurface" => ("Text", string.Empty),
            "ModuleToggle" or "ModuleCheckBox" => ("IsChecked", "false"),
            "ModuleSelect" or "ModuleSegmentedControl" => ("SelectedKey", string.Empty),
            _ => (string.Empty, string.Empty),
        };

        var value = attributeName.Length == 0
            ? fallback
            : ReadAttribute(element, attributeName, entryAssembly, file, errors) ?? fallback;

        if (string.Equals(attributeName, "IsChecked", StringComparison.Ordinal)
            && bool.TryParse(value, out var boolean))
        {
            value = boolean ? "true" : "false";
        }

        return new ValueInitialSpec(name.Trim(), value);
    }

    private static string? ReadAttribute(
        XElement element,
        string attributeName,
        Assembly entryAssembly,
        string file,
        List<ValueSpecError> errors)
    {
        var attribute = element.Attributes()
            .FirstOrDefault(candidate => string.Equals(candidate.Name.LocalName, attributeName, StringComparison.Ordinal));

        if (attribute is null)
        {
            return null;
        }

        var value = attribute.Value.Trim();
        if (value.Length == 0 || !value.StartsWith("{x:Static", StringComparison.Ordinal))
        {
            return value;
        }

        var match = StaticExpression.Match(value);
        if (!match.Success)
        {
            errors.Add(new ValueSpecError(ValueSpecErrorCode.StaticUnresolved, file, value));
            return null;
        }

        var namespaceName = ResolveNamespace(element, match.Groups["prefix"].Value);
        if (namespaceName is null)
        {
            errors.Add(new ValueSpecError(ValueSpecErrorCode.StaticUnresolved, file, value));
            return null;
        }

        var type = entryAssembly.GetType($"{namespaceName}.{match.Groups["type"].Value}", throwOnError: false);
        if (type is null)
        {
            errors.Add(new ValueSpecError(ValueSpecErrorCode.StaticUnresolved, file, value));
            return null;
        }

        const BindingFlags flags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static;
        object? resolved = type.GetField(match.Groups["member"].Value, flags)?.GetValue(null)
            ?? type.GetProperty(match.Groups["member"].Value, flags)?.GetValue(null);

        if (resolved is null)
        {
            errors.Add(new ValueSpecError(ValueSpecErrorCode.StaticUnresolved, file, value));
            return null;
        }

        return resolved is string text ? text : Convert.ToString(resolved, CultureInfo.InvariantCulture);
    }

    private static string? ReadLiteral(XElement element, string attributeName) =>
        element.Attributes()
            .FirstOrDefault(candidate => string.Equals(candidate.Name.LocalName, attributeName, StringComparison.Ordinal))
            ?.Value
            .Trim();

    private static string? ResolveNamespace(XElement element, string prefix)
    {
        for (var current = element; current is not null; current = current.Parent)
        {
            foreach (var attribute in current.Attributes())
            {
                if (!attribute.IsNamespaceDeclaration
                    || !string.Equals(attribute.Name.LocalName, prefix, StringComparison.Ordinal))
                {
                    continue;
                }

                var value = attribute.Value;
                if (value.StartsWith("using:", StringComparison.Ordinal))
                {
                    return value["using:".Length..].Trim();
                }

                if (value.StartsWith("clr-namespace:", StringComparison.Ordinal))
                {
                    var body = value["clr-namespace:".Length..];
                    var separator = body.IndexOf(';');
                    return (separator >= 0 ? body[..separator] : body).Trim();
                }
            }
        }

        return null;
    }
}
