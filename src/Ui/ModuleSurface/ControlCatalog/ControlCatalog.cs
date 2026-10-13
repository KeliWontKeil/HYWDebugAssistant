namespace HYWDebugAssistant.Ui.ModuleSurface.ControlCatalog;

/// <summary>
/// **控件目录** —— 控件词汇表在**渲染侧**的唯一载体（一个 kind 一条描述，每条一个文件）。
/// </summary>
/// <remarks>
/// <para>
/// 目录之外不存在"另一种控件"：schema 的 kind 集合与 <c>ViewElementKind</c> 枚举是另外两处
/// **同一份词汇表的投影**（写法：包声明 / 模块 SDK），本目录是渲染侧那一处。
/// 三者的**基数相等**由架构检查守住（不靠运行时互相引用 —— 三者分属三个程序集，方向禁令不允许互相读）。
/// </para>
/// <para>
/// 本类只回答"有哪些控件、各自要什么"；**怎么看、怎么排、怎么长**分属
/// <c>ModuleSurface/Hosting</c>、<c>ModuleSurface/Layout</c> 与 <c>ModuleSurface/Theme</c>。
/// </para>
/// </remarks>
internal static partial class ControlCatalog
{
    /// <remarks>
    /// 表**惰性建**（<see cref="Lazy{T}"/>）：建表要读三个描述类的静态清单，
    /// 而"本类的静态初始化"与"它们的静态初始化"之间的先后由运行时决定。
    /// 惰性建表把"读清单"推到类型初始化**之后**，顺序就不再是问题。
    /// </remarks>
    private static readonly Lazy<Dictionary<string, ControlDescriptor>> ByKindLazy =
        new(Build, LazyThreadSafetyMode.ExecutionAndPublication);

    /// <summary>目录条数。</summary>
    internal static int Count => ByKindLazy.Value.Count;

    /// <summary>目录里的全部声明键名。</summary>
    internal static IReadOnlyList<string> Kinds => [.. ByKindLazy.Value.Keys.Order(StringComparer.Ordinal)];

    /// <summary>声明键名 → 描述（未登记 ⇒ <see langword="null"/>）。</summary>
    /// <param name="kind">小驼峰声明键名。</param>
    internal static ControlDescriptor? Find(string? kind) =>
        kind is { Length: > 0 } name && ByKindLazy.Value.TryGetValue(name, out var descriptor) ? descriptor : null;

    private static Dictionary<string, ControlDescriptor> Build()
    {
        // **序数比较、不用忽略大小写**：kind 名是 JSON 键，大小写不同是**两个不同的 kind**
        //（schema 与读取器一律按序数比），这里若宽松就会把 `textLog` 与 `textlog` 悄悄合成一条。
        var map = new Dictionary<string, ControlDescriptor>(StringComparer.Ordinal);
        var duplicates = new List<string>();

        foreach (var descriptor in Declared())
        {
            if (!map.TryAdd(descriptor.Kind, descriptor))
            {
                duplicates.Add(descriptor.Kind);
            }
        }

        // 「一个 kind 一条描述」是目录的立身之本：两条同 kind 描述会让"派发到哪个控件"变成
        // 反射顺序的偶然结果 ⇒ 这里宁可炸掉，也不静默取其一。
        if (duplicates.Count > 0)
        {
            throw new InvalidOperationException(
                $"控件目录里出现重复的 kind：{string.Join(" / ", duplicates)}（一个 kind 只允许一条描述）。");
        }

        return map;
    }

    /// <summary>
    /// 本目录登记的全部描述 —— 由三个描述类各自的 <see cref="DataPresentationDescriptors.All"/>
    /// 清单**显式拼成**。
    /// </summary>
    /// <remarks>
    /// <para>
    /// **为什么不能"按类型反射扫自己的静态字段"**：描述字段一旦挂在本类上（偏类 <c>partial</c> 跨文件），
    /// 偏类的静态字段跨文件初始化顺序就是未定义的 —— 本类的静态初始化先跑时另外两个文件里的字段还是
    /// <see langword="null"/>，反射枚举会**跳过且不报错**。后果全是静默的：19 条描述只进 4 条
    /// （<c>group</c>/<c>tabs</c>/<c>collapsible</c>/<c>container</c>），于是 <c>textLog</c> 的块轴下限 88
    /// 不生效、<c>numeric</c> 被当成未知 kind —— 读数与排布全错，却没有一条报错、没有一处异常。
    /// </para>
    /// <para>
    /// 所以三个描述文件是三个**独立类型**，本方法显式读它们的 <c>All</c> ⇒ 顺序由类型依赖写死。
    /// 加一条描述要同时加进对应类的 <c>All</c>，而条数由 <c>SurfaceCatalogTests</c> 与 schema 基数交叉守住。
    /// </para>
    /// </remarks>
    private static ControlDescriptor[] Declared() =>
        [.. DataPresentationDescriptors.All, .. InteractionDescriptors.All, .. ContainerDescriptors.All];
}
