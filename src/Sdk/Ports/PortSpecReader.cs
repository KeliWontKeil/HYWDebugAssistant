using System.Globalization;
using System.Reflection;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using HYWDebugAssistant.Sdk.Contracts;

namespace HYWDebugAssistant.Sdk.Ports;

/// <summary>端口声明读取的错误类别（由宿主翻译成面向用户的文案）。</summary>
public enum PortSpecErrorCode
{
    /// <summary>声明的 XAML 文件不存在/不可读。</summary>
    FileMissing,

    /// <summary>XML 语法错误。</summary>
    XmlInvalid,

    /// <summary>`Name` 缺失或无法解析。</summary>
    NameMissing,

    /// <summary>`Direction` 缺失或不是 Send/Receive。</summary>
    DirectionInvalid,

    /// <summary>`TypeId` 缺失或无法解析。</summary>
    TypeIdMissing,

    /// <summary>`Edge` 不是已知的边。</summary>
    EdgeInvalid,

    /// <summary>`Offset` 不是 0–1 的数。</summary>
    OffsetInvalid,

    /// <summary>`{x:Static}` 指向的类型/成员无法解析。</summary>
    StaticUnresolved,

    /// <summary>端口名在多个视图文件里重复。</summary>
    DuplicatePort,
}

/// <summary>一条端口声明读取错误。</summary>
/// <param name="Code">错误类别。</param>
/// <param name="FilePath">出错的 XAML 文件（未知为 null）。</param>
/// <param name="Detail">可读细节（类型/成员/原始值等；由宿主决定是否展示）。</param>
public sealed record PortSpecError(PortSpecErrorCode Code, string? FilePath, string Detail);

/// <summary>端口声明读取结果。</summary>
/// <param name="Specs">成功解析的端口（按文件路径、文件内顺序）。</param>
/// <param name="Errors">错误（非空 ⇒ 宿主应拒绝该包并报 Issue）。</param>
public sealed record PortSpecReadResult(
    IReadOnlyList<PortDeclarationSpec> Specs,
    IReadOnlyList<PortSpecError> Errors)
{
    /// <summary>是否没有错误。</summary>
    public bool Succeeded => Errors.Count == 0;
}

/// <summary>
/// **从模块视图 XAML 读取端口声明**（只依赖 BCL，不需要 Avalonia）。
/// </summary>
/// <remarks>
/// <list type="bullet">
///   <item>扫描 `&lt;ModulePort&gt;` 元素（`using:` 命名空间），字段名与
///     `HYWDebugAssistant.Sdk.Ui.Controls.ModulePort` 一致；</item>
///   <item>属性值支持字面量与 `{x:Static 前缀:类型.成员}`；后者按元素上的
///     `xmlns:前缀="using:命名空间"` 在**入口程序集**里解析静态字段/属性；</item>
///   <item>形状/语义不在这里解析：只产出 <see cref="PortDeclarationSpec"/>，等
///     `OnRegistered` 声明完数据类型后再按 `TypeId` 解析；</item>
///   <item>错误**不抛异常**，由调用方转成包加载失败或契约自检问题。</item>
/// </list>
/// </remarks>
public static class PortSpecReader
{
    private static readonly Regex StaticExpression = new(
        @"^\{\s*x:Static\s+(?<prefix>[A-Za-z_][A-Za-z0-9_.-]*):(?<type>[A-Za-z_][A-Za-z0-9_.]*)\s*\.\s*(?<member>[A-Za-z_][A-Za-z0-9_]*)\s*\}$",
        RegexOptions.CultureInvariant | RegexOptions.Compiled);

    /// <summary>读取一个目录下的全部 `*.axaml`（递归；目录不存在 ⇒ 空结果）。</summary>
    /// <param name="directory">包内的 `views/` 目录。</param>
    /// <param name="entryAssembly">模块入口程序集（用于解析 `x:Static`）。</param>
    public static PortSpecReadResult ReadDirectory(string directory, Assembly entryAssembly)
    {
        ArgumentNullException.ThrowIfNull(entryAssembly);

        if (string.IsNullOrWhiteSpace(directory) || !Directory.Exists(directory))
        {
            return new PortSpecReadResult([], []);
        }

        var files = Directory.EnumerateFiles(directory, "*.axaml", SearchOption.AllDirectories)
            .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
            .ToList();

        return ReadFiles(files, entryAssembly);
    }

    /// <summary>读取单个 XAML 文件。</summary>
    /// <param name="path">XAML 路径。</param>
    /// <param name="entryAssembly">模块入口程序集。</param>
    public static PortSpecReadResult ReadFile(string path, Assembly entryAssembly)
    {
        ArgumentNullException.ThrowIfNull(entryAssembly);
        return ReadFiles([path], entryAssembly);
    }

    private static PortSpecReadResult ReadFiles(IReadOnlyList<string> files, Assembly entryAssembly)
    {
        var specs = new List<PortDeclarationSpec>();
        var errors = new List<PortSpecError>();
        var names = new HashSet<string>(StringComparer.Ordinal);

        foreach (var file in files)
        {
            if (!File.Exists(file))
            {
                errors.Add(new PortSpecError(PortSpecErrorCode.FileMissing, file, file));
                continue;
            }

            XDocument document;
            try
            {
                document = XDocument.Load(file, LoadOptions.SetLineInfo);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Xml.XmlException)
            {
                errors.Add(new PortSpecError(PortSpecErrorCode.XmlInvalid, file, ex.Message));
                continue;
            }

            foreach (var element in document.Descendants()
                         .Where(element => string.Equals(element.Name.LocalName, "ModulePort", StringComparison.Ordinal)))
            {
                if (!element.Name.NamespaceName.StartsWith("using:", StringComparison.Ordinal))
                {
                    continue;
                }

                var spec = ReadElement(element, file, entryAssembly, errors);
                if (spec is null)
                {
                    continue;
                }

                if (!names.Add(spec.Name))
                {
                    errors.Add(new PortSpecError(
                        PortSpecErrorCode.DuplicatePort,
                        file,
                        spec.Name));
                    continue;
                }

                specs.Add(spec);
            }
        }

        return new PortSpecReadResult(specs, errors);
    }

    private static PortDeclarationSpec? ReadElement(
        XElement element,
        string file,
        Assembly entryAssembly,
        List<PortSpecError> errors)
    {
        var name = ReadValue(element, "Name", entryAssembly, file, errors);
        if (string.IsNullOrWhiteSpace(name))
        {
            errors.Add(new PortSpecError(PortSpecErrorCode.NameMissing, file, element.ToString(SaveOptions.DisableFormatting)));
            return null;
        }

        var directionText = ReadValue(element, "Direction", entryAssembly, file, errors);
        if (string.IsNullOrWhiteSpace(directionText)
            || !Enum.TryParse<PortDirection>(directionText, ignoreCase: true, out var direction))
        {
            errors.Add(new PortSpecError(PortSpecErrorCode.DirectionInvalid, file, directionText ?? string.Empty));
            return null;
        }

        var typeId = ReadValue(element, "TypeId", entryAssembly, file, errors);
        if (string.IsNullOrWhiteSpace(typeId))
        {
            errors.Add(new PortSpecError(PortSpecErrorCode.TypeIdMissing, file, name));
            return null;
        }

        var edge = PortEdge.Auto;
        if (ReadValue(element, "Edge", entryAssembly, file, errors) is { Length: > 0 } edgeText
            && !Enum.TryParse(edgeText, ignoreCase: true, out edge))
        {
            errors.Add(new PortSpecError(PortSpecErrorCode.EdgeInvalid, file, edgeText));
            return null;
        }

        var offset = 0.5;
        if (ReadValue(element, "Offset", entryAssembly, file, errors) is { Length: > 0 } offsetText
            && (!double.TryParse(offsetText, NumberStyles.Float, CultureInfo.InvariantCulture, out offset)
                || offset is < 0 or > 1))
        {
            errors.Add(new PortSpecError(PortSpecErrorCode.OffsetInvalid, file, offsetText));
            return null;
        }

        var draggable = false;
        if (ReadValue(element, "AnchorDraggable", entryAssembly, file, errors) is { Length: > 0 } draggableText
            && !bool.TryParse(draggableText, out draggable))
        {
            draggable = false;
        }

        TransferSemantics? semantics = null;
        if (ReadValue(element, "Semantics", entryAssembly, file, errors) is { Length: > 0 } semanticsText
            && Enum.TryParse<TransferSemantics>(semanticsText, ignoreCase: true, out var parsedSemantics))
        {
            semantics = parsedSemantics;
        }

        return new PortDeclarationSpec(name.Trim(), direction, typeId.Trim(), edge, offset, draggable, semantics);
    }

    private static string? ReadValue(
        XElement element,
        string attributeName,
        Assembly entryAssembly,
        string file,
        List<PortSpecError> errors)
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
            errors.Add(new PortSpecError(PortSpecErrorCode.StaticUnresolved, file, value));
            return null;
        }

        var prefix = match.Groups["prefix"].Value;
        var typeName = match.Groups["type"].Value;
        var memberName = match.Groups["member"].Value;
        var namespaceName = ResolveNamespace(element, prefix);
        if (namespaceName is null)
        {
            errors.Add(new PortSpecError(PortSpecErrorCode.StaticUnresolved, file, value));
            return null;
        }

        var type = entryAssembly.GetType($"{namespaceName}.{typeName}", throwOnError: false);
        if (type is null)
        {
            errors.Add(new PortSpecError(PortSpecErrorCode.StaticUnresolved, file, value));
            return null;
        }

        const BindingFlags flags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static;
        object? resolved = type.GetField(memberName, flags)?.GetValue(null)
            ?? type.GetProperty(memberName, flags)?.GetValue(null);

        if (resolved is null)
        {
            errors.Add(new PortSpecError(PortSpecErrorCode.StaticUnresolved, file, value));
            return null;
        }

        return resolved is string text ? text : Convert.ToString(resolved, CultureInfo.InvariantCulture);
    }

    private static string? ResolveNamespace(XElement element, string prefix)
    {
        for (var current = element; current is not null; current = current.Parent)
        {
            foreach (var attribute in current.Attributes())
            {
                if (attribute.IsNamespaceDeclaration
                    && string.Equals(attribute.Name.LocalName, prefix, StringComparison.Ordinal))
                {
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
        }

        return null;
    }
}
