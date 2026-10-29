using HYWDebugAssistant.Sdk.Access;
using HYWDebugAssistant.Sdk.Context;
using HYWDebugAssistant.Sdk.Contracts;
using HYWDebugAssistant.Sdk.Lifecycle;
using HYWDebugAssistant.Sdk.Ports;

namespace HYWDebugAssistant.Sdk.Testing;

/// <summary>契约探针结果：声明面 + 问题清单 + 建议清单。</summary>
/// <param name="EntryType">模块入口类型。</param>
/// <param name="Ports">已声明的口。</param>
/// <param name="Problems">契约问题（空 = 合法）；面向作者的**具体、可定位**描述。</param>
/// <param name="Flows">已声明的**内部处理顺序**（角色 → 角色）。</param>
/// <param name="Roles">已声明的**角色**（模块的功能分块）。</param>
/// <param name="Advice">
/// **建议**（不是错误）：与 <paramref name="Problems"/> **分通道**——
/// `Problems` 的语义是「空 = 合法」（`IsValid`/`CheckAll`/门禁用例都按它写），
/// 建议放进去会把「能跑但有话说」变成「不合法」。运行期**不产生**这类提示（不能在自家正确模块的 Issue 中心里报警）。
/// </param>
/// <remarks>
/// **界面契约也在代码里**：显示源 / 输入源 / 命令由模块的 <c>OnRegistered</c> 声明；
/// 视图（XAML）只按这些名字取数、提交。本结果当前汇总数据面；界面键与声明的对账由
/// `VisualSnapshots` 的视图契约用例覆盖（真造控件，再按控件上的键对声明）。
/// </remarks>
public sealed record ContractProbeResult(
    Type EntryType,
    IReadOnlyList<PortDeclaration> Ports,
    IReadOnlyList<DataTypeDeclaration> DataTypes,
    IReadOnlyList<string> Problems,
    IReadOnlyList<FlowDeclaration>? Flows = null,
    IReadOnlyList<RoleDeclaration>? Roles = null,
    IReadOnlyList<string>? Advice = null,
    IReadOnlyList<ExternalAccessDeclaration>? ExternalAccesses = null)
{
    /// <summary>契约是否合法。</summary>
    public bool IsValid => Problems.Count == 0;

    /// <summary>已声明的**内部处理顺序**（`角色 → 角色`）。</summary>
    public IReadOnlyList<FlowDeclaration> DeclaredFlows => Flows ?? [];

    /// <summary>已声明的**角色**（功能分块；叠加层画出来的方块就是它们）。</summary>
    public IReadOnlyList<RoleDeclaration> DeclaredRoles => Roles ?? [];

    /// <summary>**建议**（空 = 没有可说的）；**不影响 <see cref="IsValid"/>**。</summary>
    public IReadOnlyList<string> Advisories => Advice ?? [];

    /// <summary>已声明的**外部接入**（按声明顺序）。</summary>
    public IReadOnlyList<ExternalAccessDeclaration> DeclaredExternalAccesses => ExternalAccesses ?? [];
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
    /// <param name="catalog">
    /// 可选的外部标准目录：给出时校验标准标识可解析、参数类型可登记；
    /// 省略时只做结构校验（外部接入不接线也能自检）。
    /// </param>
    public static ContractProbeResult Probe(
        Type entryType,
        TestModuleContext? context = null,
        IExternalAccessCatalog? catalog = null)
    {
        ArgumentNullException.ThrowIfNull(entryType);

        var problems = new List<string>();
        var advice = new List<string>();

        if (!typeof(IModule).IsAssignableFrom(entryType))
        {
            return new ContractProbeResult(entryType, [], [], [$"入口类型未实现 IModule：{entryType.FullName}"]);
        }

        if (entryType.GetConstructor(Type.EmptyTypes) is null)
        {
            problems.Add("入口类型缺少公共无参构造函数（框架用 Activator 实例化）");
        }

        if (entryType.IsAbstract || entryType.IsInterface)
        {
            return new ContractProbeResult(entryType, [], [], [$"入口类型不可实例化：{entryType.FullName}"]);
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
            return new ContractProbeResult(entryType, [], [], problems);
        }

        CheckPorts(probeContext.DeclaredPorts, problems);
        CheckDataTypes(probeContext.DeclaredDataTypes, probeContext.DeclaredPorts, problems);
        CheckRoles(probeContext.DeclaredRoles, probeContext.DeclaredPorts, probeContext.DeclaredDataTypes, problems, advice);
        CheckFlows(probeContext.DeclaredFlows, probeContext.DeclaredRoles, problems);
        CheckExternalAccesses(probeContext.DeclaredExternalAccesses, catalog, problems);
        CheckInteractionModes(entryType, advice);
        CheckConfiguration(entryType, problems);

        return new ContractProbeResult(
            entryType,
            probeContext.DeclaredPorts,
            probeContext.DeclaredDataTypes,
            problems,
            probeContext.DeclaredFlows,
            probeContext.DeclaredRoles,
            advice,
            probeContext.DeclaredExternalAccesses);
    }

    /// <summary>多个模块类型一次探测（返回全部**问题**）。</summary>
    /// <param name="entryTypes">模块入口类型集合。</param>
    /// <remarks>
    /// **只汇总问题、不含建议**：调用方（门禁用例、模块作者的"声明面应无问题"断言）按"空 = 合法"使用它；
    /// 建议经 <see cref="Probe"/> 的 <see cref="ContractProbeResult.Advisories"/> 单独取用。
    /// </remarks>
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
    /// 校验**数据类型声明**：标识/版本合法、同（标识,版本）不重复、引用都能在本模块解析、
    /// 计量块自洽（否则兼容判定会退化成结构比较，作者应当**现在**就知道）。
    /// </summary>
    private static void CheckDataTypes(
        IReadOnlyList<DataTypeDeclaration> types,
        IReadOnlyList<PortDeclaration> ports,
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

            // **有效长度字段声明**必须真的能读（速率只统计有效数据）：
            // 名字要能在本类型的**顶层字段**里找到，且必须是**整型原语**（小端读出来当字节数用）。
            // 只声明不校验 ⇒ 框架运行期读到 0 ⇒ 「有效速率恒为 0」这种**静默归零**（比误报危险得多）。
            if (type.Metering.PayloadLengthField is { Length: > 0 } lengthField)
            {
                var layout = ContractLayout.FromShape(type.Shape);

                if (!layout.TryGetField(lengthField, out var field))
                {
                    problems.Add(
                        $"数据类型 {type.Reference} 的计量块声明了有效长度字段 '{lengthField}'，"
                        + $"但契约形状里没有这个顶层字段（现有：{string.Join("、", layout.Fields.Select(f => f.Name))}）");
                }
                else if (field.Shape is not PrimitiveShape primitive
                    || primitive.Primitive is not (ContractPrimitive.I8 or ContractPrimitive.U8
                        or ContractPrimitive.I16 or ContractPrimitive.U16
                        or ContractPrimitive.I32 or ContractPrimitive.U32
                        or ContractPrimitive.I64 or ContractPrimitive.U64))
                {
                    problems.Add(
                        $"数据类型 {type.Reference} 的有效长度字段 '{lengthField}' 必须是**整型**原语"
                        + "（字节数是从它读出来的一个整数；浮点/字节块/结构都不行）");
                }
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
    }

    /// <summary>
    /// 校验**角色声明**（功能分块）：Id 唯一 · 类型可在本模块解析 · 口存在且方向相符 ·
    /// **一个口最多属于一个角色** · **输入口与输出口同型（α）** · 至少一个口；
    /// **口数 &gt; 2 时给建议**（进 <paramref name="advice"/>，**不是问题**）。
    /// </summary>
    /// <remarks>
    /// 角色**只用于呈现**（叠加层画功能块与 `m/n`），不参与建网/兼容/投递 —— 因此这里的校验只回答
    /// "画得出来吗、说得清吗"；**校验不了语义**（作者可以声明"融合"而代码里走别的路径：声明 ≠ 证明）。
    /// **口数不设上限**（2 入 1 出的融合块是合法且最常见的多口形态）；该建议只提醒作者"多数分块只有一入一出"，
    /// 并明说"多入一出/一入多出不必拆"，避免把一件功能硬拆成两块。
    /// <para>
    /// <b>两侧同型</b>：角色的输入口与输出口必须承载**同一种**数据类型（= 角色的
    /// <c>Type</c>）—— 一个角色只有一种数据类型，本条约束的是**两侧是否同型**。
    /// ⇒ 类型转换 = **两个数据角色** + <c>DeclareFlow</c>；**口未声明类型时不校验**（可声明 ≠ 必须声明的模块形态）。
    /// </para>
    /// </remarks>
    private static void CheckRoles(
        IReadOnlyList<RoleDeclaration> roles,
        IReadOnlyList<PortDeclaration> ports,
        IReadOnlyList<DataTypeDeclaration> types,
        List<string> problems,
        List<string> advice)
    {
        var knownTypes = new HashSet<DataTypeRef>(types.Select(t => t.Reference));
        var ids = new HashSet<string>(StringComparer.Ordinal);
        var claimed = new Dictionary<string, string>(StringComparer.Ordinal);
        var withRoles = new HashSet<string>(StringComparer.Ordinal);
        var validRoleCount = 0;

        foreach (var role in roles)
        {
            if (string.IsNullOrWhiteSpace(role.Id))
            {
                problems.Add("角色的标识为空（标识是角色的身份，模块内唯一）");
                continue;
            }

            if (!ids.Add(role.Id))
            {
                problems.Add($"角色标识重复：'{role.Id}'（模块内唯一）");
                continue;
            }

            if (!knownTypes.Contains(role.Type))
            {
                problems.Add($"角色 '{role.Id}' 引用了本模块未声明的数据类型 {role.Type}（先把类型声明出来）");
                continue;
            }

            if (role.Inputs.Count == 0 && role.Outputs.Count == 0)
            {
                problems.Add($"角色 '{role.Id}' 既没有输入口也没有输出口（一个分块至少要有一个口）");
                continue;
            }

            // **口数不设上限**（2 入 1 出的融合块是合法形态）⇒ 只给**建议**，且明说"多入一出/一入多出不必拆"
            if (role.PortCount > 2)
            {
                advice.Add(
                    $"建议：角色 '{role.Id}' 有 {role.PortCount} 个口（{role.Inputs.Count} 入 / {role.Outputs.Count} 出）——" +
                    "多数分块只有 1 入 1 出；若这其实是两件事，可拆成两个角色（若是一件事，例如多路合成一路，则不必拆）");
            }

            foreach (var (name, expected) in role.Inputs.Select(n => (n, PortDirection.Receive))
                         .Concat(role.Outputs.Select(n => (n, PortDirection.Send))))
            {
                var port = ports.FirstOrDefault(p => string.Equals(p.Name, name, StringComparison.Ordinal));

                if (port is null)
                {
                    problems.Add($"角色 '{role.Id}' 引用了未声明的口 '{name}'");
                    continue;
                }

                if (port.Direction != expected)
                {
                    problems.Add(
                        $"角色 '{role.Id}' 的{(expected == PortDirection.Receive ? "输入" : "输出")}口 '{name}' " +
                        $"方向应为 {expected}（口自己声明的是 {port.Direction}）");
                    continue;
                }

                // 角色的**输入口与输出口必须承载同一种数据类型**（= 角色的 Type）：
                // 一个角色只有一种数据类型，本条约束的是**两侧是否同型**。
                // ⇒ **类型转换 = 两个数据角色**（「收 A」+「发 B」）+ DeclareFlow；叠加层上表现为两个方块。
                // 口**未声明类型**（可声明 ≠ 必须声明的模块形态）⇒ 无可比对象，**跳过不报**。
                if (port.Type is { } portType && portType != role.Type)
                {
                    problems.Add(
                        $"角色 '{role.Id}' 的{(expected == PortDirection.Receive ? "输入" : "输出")}口 '{name}' 的类型是 {portType}，" +
                        $"与角色的类型 {role.Type} 不一致（一个角色的输入口与输出口必须同一种数据类型；" +
                        "**类型转换请拆成两个角色** + DeclareFlow）");
                    continue;
                }

                if (claimed.TryGetValue(name, out var owner))
                {
                    problems.Add($"口 '{name}' 同时属于角色 '{owner}' 与 '{role.Id}'（一个口最多属于一个角色）");
                    continue;
                }

                claimed[name] = role.Id;
                _ = withRoles.Add(name);
            }

            validRoleCount++;
        }

        // 「口 → 角色归属」的完整性**只给建议、不进 Problems、运行期不报**：
        // 与「口数 > 2 只给建议」同一口径 —— 在自家正确模块的 Issue 中心报警，
        // 用户会以为模块写错了；且该要求是「**应当**」而非「必须」，故不得升级为错误。
        // 两条规则分别对应：① 有数据口却零（有效）角色 ⇒ 叠加层不画任何功能块；
        //                   ② 有角色，但某些口没有归属 ⇒ 那些口在叠加层上没有方块。
        if (ports.Count > 0 && validRoleCount == 0)
        {
            advice.Add(
                $"建议：本模块声明了 {ports.Count} 个数据口，但**没有任何有效的角色声明** —— " +
                "叠加层不会画功能块，用户看不到\"这个模块内部有哪几块功能\"；请为每块处理功能声明一个角色（DeclareRole）");
        }
        else if (validRoleCount > 0)
        {
            var orphans = ports
                .Where(p => !withRoles.Contains(p.Name))
                .Select(p => $"'{p.Name}'")
                .ToList();

            if (orphans.Count > 0)
            {
                advice.Add(
                    $"建议：口 {string.Join("、", orphans)} **不属于任何角色** —— 这些口在叠加层上没有归属方块；" +
                    "若它们与某块功能是一件事，请把口名加进该角色的输入/输出口");
            }
        }
    }

    /// <summary>
    /// 校验**内部处理顺序声明**（`角色 → 角色`）：两端角色必须已声明、不得自环、同（起点, 终点）不重复。
    /// </summary>
    private static void CheckFlows(
        IReadOnlyList<FlowDeclaration> flows,
        IReadOnlyList<RoleDeclaration> roles,
        List<string> problems)
    {
        var known = new HashSet<string>(roles.Select(r => r.Id), StringComparer.Ordinal);
        var seen = new HashSet<(string From, string To)>();

        foreach (var flow in flows)
        {
            if (string.IsNullOrWhiteSpace(flow.FromRoleId) || string.IsNullOrWhiteSpace(flow.ToRoleId))
            {
                problems.Add($"内部处理顺序的角色标识不能为空：{flow}");
                continue;
            }

            if (string.Equals(flow.FromRoleId, flow.ToRoleId, StringComparison.Ordinal))
            {
                problems.Add($"内部处理顺序不得自环：{flow}");
                continue;
            }

            if (!known.Contains(flow.FromRoleId) || !known.Contains(flow.ToRoleId))
            {
                problems.Add($"内部处理顺序引用了未声明的角色：{flow}");
                continue;
            }

            if (!seen.Add((flow.FromRoleId, flow.ToRoleId)))
            {
                problems.Add($"内部处理顺序重复声明：{flow}");
            }
        }
    }

    /// <summary>
    /// 校验**外部接入声明**：名字非空且唯一、标准标识非空且可解析、参数类型可登记、单元上限合法。
    /// </summary>
    /// <remarks>
    /// 适配器与参数类型归框架所有：给出 <paramref name="catalog"/> 时按标准标识解析 descriptor，
    /// 并校验其参数类型能按 <c>class + 无参构造</c> 登记；省略 catalog 时只做结构校验，
    /// 不扫描模块程序集。
    /// </remarks>
    private static void CheckExternalAccesses(
        IReadOnlyList<ExternalAccessDeclaration> accesses,
        IExternalAccessCatalog? catalog,
        List<string> problems)
    {
        var names = new HashSet<string>(StringComparer.Ordinal);

        foreach (var access in accesses)
        {
            if (string.IsNullOrWhiteSpace(access.Name))
            {
                problems.Add("外部接入的名字不能为空（名字是模块内唯一的接入身份）");
                continue;
            }

            if (!names.Add(access.Name))
            {
                problems.Add($"外部接入名重复：'{access.Name}'（模块内唯一）");
            }

            if (access.MaxUnitBytes <= 0)
            {
                problems.Add($"外部接入 '{access.Name}' 的单元上限必须大于 0（当前 {access.MaxUnitBytes}）");
            }

            if (string.IsNullOrWhiteSpace(access.StandardId))
            {
                problems.Add(
                    $"外部接入 '{access.Name}' 的标准标识为空（框架按标准标识解析适配器与参数类型）");
                continue;
            }

            if (catalog is null)
            {
                continue;
            }

            if (!catalog.TryResolve(access.StandardId, out var descriptor) || descriptor is null)
            {
                problems.Add(
                    $"外部接入 '{access.Name}' 的标准标识未登记：'{access.StandardId}'（框架无法解析适配器与参数类型）");
                continue;
            }

            var parametersType = descriptor.ParametersType;
            if (parametersType is null)
            {
                problems.Add($"标准 '{access.StandardId}' 的参数类型为空");
            }
            else if (!parametersType.IsClass || parametersType.IsAbstract || !parametersType.IsPublic)
            {
                problems.Add(
                    $"标准 '{access.StandardId}' 的参数类型 {parametersType.Name} 必须是公开、非抽象的类");
            }
            else if (parametersType.GetConstructor(Type.EmptyTypes) is null)
            {
                problems.Add(
                    $"标准 '{access.StandardId}' 的参数类型 {parametersType.Name} 缺少公共无参构造函数（框架按 class + new() 登记配置）");
            }

            if (descriptor.CreateAdapter is null)
            {
                problems.Add($"标准 '{access.StandardId}' 缺少适配件工厂");
            }
        }
    }

    /// <summary>
    /// 检查两种消费方式是否同时存在：活动体优先、交互回调不会投递。
    /// </summary>
    /// <remarks>
    /// 这是**建议**不是问题：框架能跑（活动体赢），但模块作者应当知道另一半代码永远不会被调用。
    /// </remarks>
    private static void CheckInteractionModes(Type entryType, List<string> advice)
    {
        if (typeof(IActivitySource).IsAssignableFrom(entryType)
            && typeof(IModuleInteractionSink).IsAssignableFrom(entryType))
        {
            advice.Add(
                "模块同时实现 IActivitySource 与 IModuleInteractionSink：运行时活动体优先，交互回调不会投递；请只选一种消费方式");
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
