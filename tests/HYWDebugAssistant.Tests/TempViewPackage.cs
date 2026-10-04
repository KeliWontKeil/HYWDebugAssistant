namespace HYWDebugAssistant.Tests;

/// <summary>
/// 测试用的**临时模块包**：把一段 `view.json` 写进临时目录，供带 `packageDirectory` 的
/// <c>ModuleDefinition</c> 重载使用。
/// </summary>
/// <remarks>
/// 控件声明的**唯一来源是模块包内的 `view.json`** ⇒ 测试桩模块不能再在
/// 代码里声明视图。同名重复调用**覆盖写**，便于同一桩模块在不同用例里换声明。
/// </remarks>
internal static class TempViewPackage
{
    /// <summary>写入包内 `view.json` 并返回包目录。</summary>
    /// <param name="name">包名（只用于区分临时目录，避免用例之间互相覆盖）。</param>
    /// <param name="viewJson">包内 `view.json` 的全文。</param>
    public static string Create(string name, string viewJson)
    {
        var directory = Path.Combine(Path.GetTempPath(), $"hyw-test-package-{name}");
        _ = Directory.CreateDirectory(directory);
        File.WriteAllText(Path.Combine(directory, "view.json"), viewJson);
        return directory;
    }

    /// <summary>写入一份**只有端口位置与数据源、控件由框架最小生成**的合法声明。</summary>
    /// <param name="name">包名（只用于区分临时目录）。</param>
    /// <param name="ports">数据面端口与它的边（每个数据面口都必须在这里有自己的条目）。</param>
    /// <param name="sources">模块会写的数据源。</param>
    /// <remarks>
    /// 数据面端口**不在这里登记就是硬伤**（端口位置只有这一个来源）；数据源不被任何元素引用会被
    /// 判为「声明了却接不上」并出一条提示 ⇒ 这里替每个数据源补一个最小控件，让声明自洽。
    /// </remarks>
    public static string Create(
        string name,
        IReadOnlyList<(string Port, string Edge)> ports,
        IReadOnlyList<(string Name, string ValueType)> sources)
    {
        var portItems = string.Join(", ", ports.Select(p => $$"""{ "name": "{{p.Port}}", "edge": "{{p.Edge}}" }"""));
        var sourceItems = string.Join(", ", sources.Select(s => $$"""{ "name": "{{s.Name}}", "kind": "Value", "valueType": "{{s.ValueType}}" }"""));
        var viewItems = string.Join(", ", sources.Select(s => $$"""{ "kind": "numeric", "data": "{{s.Name}}" }"""));
        var views = sources.Count == 0
            ? "[]"
            : $$"""[ { "root": { "kind": "container", "children": [ {{viewItems}} ] } } ]""";

        return Create(
            name,
            $$"""
            {
              "version": 1,
              "frame": { "ports": [ {{portItems}} ] },
              "sources": [ {{sourceItems}} ],
              "views": {{views}}
            }
            """);
    }
}
