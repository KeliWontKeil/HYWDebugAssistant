using HYWDebugAssistant.Sdk.Context;
using HYWDebugAssistant.Sdk.Contracts;
using HYWDebugAssistant.Sdk.Lifecycle;
using HYWDebugAssistant.Sdk.Ports;
using HYWDebugAssistant.Sdk.Views;

namespace HYWDebugAssistant.Sdk.Testing;

/// <summary>契约探针结果：声明面 + 问题清单。</summary>
/// <param name="EntryType">模块入口类型。</param>
/// <param name="Ports">已声明的口。</param>
/// <param name="View">已声明的视图（无 UI 模块为 <see langword="null"/>）。</param>
/// <param name="Problems">契约问题（空 = 合法）；面向作者的**具体、可定位**描述。</param>
public sealed record ContractProbeResult(
    Type EntryType,
    IReadOnlyList<PortDeclaration> Ports,
    IReadOnlyList<DataTypeDeclaration> DataTypes,
    ViewDeclaration? View,
    IReadOnlyList<string> Problems)
{
    /// <summary>契约是否合法。</summary>
    public bool IsValid => Problems.Count == 0;
}

/// <summary>
/// **契约检查**（T6）：不起框架、不装包，仅凭模块类型验证声明是否满足 SDK 规则。
/// </summary>
/// <remarks>
/// 这是"提供且推荐"给模块作者的快速自检：在模块自己的测试项目里一行即可跑出问题清单，
/// 避免把契约错误留到集成期（那时错误表现为"连不上"，定位成本高得多）。
/// </remarks>
public static class ModuleContract
{
    /// <summary>探测一个模块类型的声明面。</summary>
    /// <param name="entryType">模块入口类型（实现 <see cref="IModule"/>）。</param>
    /// <param name="context">可选的测试上下文（默认新建）。</param>
    public static ContractProbeResult Probe(Type entryType, TestModuleContext? context = null)
    {
        ArgumentNullException.ThrowIfNull(entryType);

        var problems = new List<string>();

        if (!typeof(IModule).IsAssignableFrom(entryType))
        {
            return new ContractProbeResult(entryType, [], [], null, [$"入口类型未实现 IModule：{entryType.FullName}"]);
        }

        if (entryType.GetConstructor(Type.EmptyTypes) is null)
        {
            problems.Add("入口类型缺少公共无参构造函数（框架用 Activator 实例化）");
        }

        if (entryType.IsAbstract || entryType.IsInterface)
        {
            return new ContractProbeResult(entryType, [], [], null, [$"入口类型不可实例化：{entryType.FullName}"]);
        }

        TestModuleContext probeContext;
        try
        {
            probeContext = context ?? new TestModuleContext();
            var module = (IModule)Activator.CreateInstance(entryType)!;
            module.OnRegistered(probeContext);
        }
        catch (Exception ex)
        {
            problems.Add($"OnRegistered 抛出异常：{ex.GetType().Name} — {ex.Message}");
            return new ContractProbeResult(entryType, [], [], null, problems);
        }

        CheckPorts(probeContext.DeclaredPorts, problems);
        CheckDataTypes(probeContext.DeclaredDataTypes, probeContext.DeclaredPorts, probeContext.DeclaredView, problems);
        CheckView(probeContext.DeclaredView, problems);
        CheckConfiguration(entryType, problems);

        return new ContractProbeResult(
            entryType,
            probeContext.DeclaredPorts,
            probeContext.DeclaredDataTypes,
            probeContext.DeclaredView,
            problems);
    }

    /// <summary>多个模块类型一次探测（返回全部问题）。</summary>
    /// <param name="entryTypes">模块入口类型集合。</param>
    public static IReadOnlyList<string> CheckAll(params Type[] entryTypes)
    {
        ArgumentNullException.ThrowIfNull(entryTypes);

        var problems = new List<string>();
        foreach (var type in entryTypes)
        {
            foreach (var problem in Probe(type).Problems)
            {
                problems.Add($"{type.Name}：{problem}");
            }
        }

        return problems;
    }

    private static void CheckPorts(IReadOnlyList<PortDeclaration> ports, List<string> problems)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);

        foreach (var port in ports)
        {
            if (string.IsNullOrWhiteSpace(port.Name))
            {
                problems.Add("存在空口名");
                continue;
            }

            if (!seen.Add(port.Name))
            {
                problems.Add($"口名重复：'{port.Name}'（口名必须在模块内唯一）");
            }

            CheckShape(port.Shape, $"口 '{port.Name}'", problems);
        }
    }

    private static void CheckShape(ContractShape shape, string owner, List<string> problems)    {
        if (shape.Depth > 4)
        {
            problems.Add($"{owner} 的形状嵌套深度 {shape.Depth} 超过上限 4");
        }

        if (shape.SizeInBytes <= 0)
        {
            problems.Add($"{owner} 的形状定长尺寸非法：{shape.SizeInBytes}");
        }

        switch (shape)
        {
            case BytesShape bytes when bytes.Length is < 1 or > BytesShape.MaxLength:
                problems.Add($"{owner} 的字节块长度 {bytes.Length} 超出 1–{BytesShape.MaxLength}");
                break;

            case ArrayShape array:
                if (array.Length is < 1 or > ArrayShape.MaxLength)
                {
                    problems.Add($"{owner} 的定长数组长度 {array.Length} 超出 1–{ArrayShape.MaxLength}");
                }

                break;

            case StructShape structure:
                if (structure.Fields.Count == 0)
                {
                    problems.Add($"{owner} 的结构形状没有任何字段");
                }

                var fieldNames = new HashSet<string>(StringComparer.Ordinal);
                foreach (var (name, fieldShape) in structure.Fields)
                {
                    if (string.IsNullOrWhiteSpace(name))
                    {
                        problems.Add($"{owner} 的结构字段缺少名字");
                    }
                    else if (!fieldNames.Add(name))
                    {
                        problems.Add($"{owner} 的结构字段名重复：'{name}'");
                    }

                    CheckShape(fieldShape, owner, problems);
                }

                break;

            default:
                break;
        }
    }

    /// <summary>
    /// 校验**数据类型声明**【ADR-0046 D2】：标识/版本合法、同（标识,版本）不重复、引用都能在本模块解析、
    /// 计量块自洽（否则兼容判定会退化成结构比较，作者应当**现在**就知道）。
    /// </summary>
    private static void CheckDataTypes(
        IReadOnlyList<DataTypeDeclaration> types,
        IReadOnlyList<PortDeclaration> ports,
        ViewDeclaration? view,
        List<string> problems)
    {
        var known = new HashSet<DataTypeRef>();

        foreach (var type in types)
        {
            if (string.IsNullOrWhiteSpace(type.TypeId))
            {
                problems.Add("数据类型标识为空（标识是类型的身份，跨模块同名即同一类型）");
                continue;
            }

            if (!char.IsLetter(type.TypeId[0]) || type.TypeId.Contains(' ', StringComparison.Ordinal))
            {
                problems.Add($"数据类型标识不规范：'{type.TypeId}'（建议 <作者/名字> 形式，不含空格）");
            }

            if (!known.Add(type.Reference))
            {
                problems.Add($"数据类型重复声明：{type.Reference}（同一（标识, 版本）只能声明一次）");
                continue;
            }

            CheckShape(type.Shape, $"数据类型 {type.Reference}", problems);

            if (type.Metering.SamplesPerUnit <= 0)
            {
                problems.Add($"数据类型 {type.Reference} 的计量块 SamplesPerUnit 必须 > 0（当前 {type.Metering.SamplesPerUnit}）");
            }

            if (type.Metering.ChannelCount <= 0)
            {
                problems.Add($"数据类型 {type.Reference} 的计量块 ChannelCount 必须 > 0（当前 {type.Metering.ChannelCount}）");
            }

            if (type.Metering.PayloadSizeInBytes is { } payload
                && (payload <= 0 || payload > type.SizeInBytes))
            {
                problems.Add(
                    $"数据类型 {type.Reference} 的 PayloadSizeInBytes = {payload} 不合理（应 > 0 且 ≤ 契约尺寸 {type.SizeInBytes}）");
            }
        }

        // 引用必须能在**本模块**解析：解析不了 ⇒ 框架只能回退结构比较（不是错误，但作者必须知道）
        foreach (var port in ports)
        {
            if (port.Type is { } reference && !known.Contains(reference))
            {
                problems.Add($"口 '{port.Name}' 引用了本模块未声明的数据类型 {reference}——该口将回退为**结构**兼容判定");
            }
        }

        foreach (var source in view?.DataSources ?? [])
        {
            if (source.Type is { } reference && !known.Contains(reference))
            {
                problems.Add(
                    $"数据源 '{source.Name}' 引用了本模块未声明的数据类型 {reference}——呈现层将拿不到模块解码器");
            }
        }
    }

    private static void CheckView(ViewDeclaration? view, List<string> problems)
    {
        if (view is null)
        {
            // 无 UI 模块合法（主文档 §10.4）：不是问题
            return;
        }

        if (view.Elements.Count == 0)
        {
            problems.Add("视图声明了但没有根元素（不声明视图请完全省略 DeclareView）");
        }

        var sources = new HashSet<string>(StringComparer.Ordinal);
        foreach (var source in view.DataSources)
        {
            if (!sources.Add(source.Name))
            {
                problems.Add($"数据源名重复：'{source.Name}'（模块内唯一）");
            }

            // 只有**数值类**数据源（值 / 序列）才要求给出原语：文本 / 字节块 / 行集 / 帧本来就按字节呈现
            if (source.Kind is ViewDataSourceKind.Value or ViewDataSourceKind.Sequence && source.ValueType is null)
            {
                problems.Add($"数据源 '{source.Name}' 声明为 {source.Kind} 但未给出 ValueType——UI 无法解码其数值");
            }
        }

        foreach (var element in Flatten(view.Elements))
        {
            // **交互控制类元素**（B 类）绑定的是**命令 / 输入源名**，不是数据源（ADR-0040 D1）：
            // 它们的名字由 ViewCommand / 输入源声明承担，不能拿数据源表去校验
            if (IsInteractive(element.Kind))
            {
                continue;
            }

            if (element.DataSource is { Length: > 0 } name && !sources.Contains(name))
            {
                problems.Add($"元素 {element.Kind} 绑定了未声明的数据源：'{name}'");
            }
        }
    }

    /// <summary>是否为**交互控制类元素**（B 类：绑定命令/输入，而非数据源）。</summary>
    private static bool IsInteractive(ViewElementKind kind) => kind is
        ViewElementKind.ConfigurationEditor
        or ViewElementKind.CommandButton
        or ViewElementKind.ToggleSlider
        or ViewElementKind.EnumSelector
        or ViewElementKind.TextInput
        or ViewElementKind.FileSelector;

    private static IEnumerable<ViewElement> Flatten(IReadOnlyList<ViewElement> elements)
    {
        foreach (var element in elements)
        {
            yield return element;
            foreach (var child in Flatten(element.Children))
            {
                yield return child;
            }
        }
    }

    private static void CheckConfiguration(Type entryType, List<string> problems)
    {
        // 配置类型是**模块自有的**：框架只要求"class + 无参构造"（IConfigurationAccess.Write<T> 的约束）。
        // 按命名约定（*Config / *Configuration）扫描模块程序集，避免误报作者的其他类型。
        Type[] types;
        try
        {
            types = entryType.Assembly.GetTypes();
        }
        catch (System.Reflection.ReflectionTypeLoadException ex)
        {
            problems.Add($"无法枚举程序集类型以检查配置约定：{ex.Message}");
            return;
        }

        foreach (var type in types)
        {
            if (type.IsAbstract || type.IsInterface || !type.IsPublic)
            {
                continue;
            }

            if (!type.Name.EndsWith("Config", StringComparison.Ordinal)
                && !type.Name.EndsWith("Configuration", StringComparison.Ordinal))
            {
                continue;
            }

            if (type.GetConstructor(Type.EmptyTypes) is null)
            {
                problems.Add($"配置类型 {type.Name} 缺少公共无参构造函数（框架按 class + new() 约束读写）");
            }
        }
    }
}
