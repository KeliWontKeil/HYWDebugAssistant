using HYWDebugAssistant.Hosting.Packages;
using HYWDebugAssistant.Sdk.Views;
using Shouldly;

namespace HYWDebugAssistant.Tests;

/// <summary>
/// 测试里的**声明面装配**：一段 <c>view.json</c> ⇒ <see cref="ViewDocument"/> / <see cref="ViewNode"/>。
/// </summary>
/// <remarks>
/// <para>
/// **测试里不手写 `ViewNode`**：它有一堆必填成员，手写必然与 schema 漂移。这里一律走产线的
/// <see cref="ViewDeclarationReader"/>，于是"缺省值来自 schema"这条口径在测试里也自动成立。
/// </para>
/// <para>
/// 读不出来 = **夹具本身写错**（不是被测行为）⇒ 直接抛 / 断言失败，绝不返回空文档：
/// 否则用例会以"控件树是空的"这种与真因无关的方式变红。
/// </para>
/// </remarks>
internal static class TestView
{
    private static readonly Lazy<ViewSchema> Schema = new(() =>
    {
        ViewSchema.TryLoad(out var schema, out var error).ShouldBeTrue(error);
        return schema!;
    });

    private static readonly Dictionary<string, ViewNode> Nodes = new(StringComparer.Ordinal);

    /// <summary>把一份**完整声明**（顶层对象）读成文档。</summary>
    /// <param name="viewJson">声明文件内容。</param>
    /// <returns>读到的文档（必非空；有硬伤或警告直接失败）。</returns>
    internal static ViewDocument Read(string viewJson)
    {
        var directory = TempViewPackage.Create($"view-{Guid.NewGuid():N}", viewJson);
        ViewDeclarationReader.TryRead(directory, Schema.Value, out var document, out var warnings, out var error)
            .ShouldBeTrue(error);
        warnings.ShouldBeEmpty();
        document.ShouldNotBeNull();
        return document;
    }

    /// <summary>只要一个节点时的简写：<paramref name="rootJson"/> 是唯一的视图根。</summary>
    /// <param name="rootJson">根节点的声明片段。</param>
    /// <param name="sourcesJson">数据源声明片段（缺省为空表）。</param>
    /// <returns>读到的根节点。</returns>
    internal static ViewNode Node(string rootJson, string sourcesJson = "[]")
    {
        var json = $$"""
        {
          "version": 1,
          "sources": {{sourcesJson}},
          "views": [ { "root": {{rootJson}} } ]
        }
        """;

        lock (Nodes)
        {
            if (!Nodes.TryGetValue(json, out var node))
            {
                node = Read(json).Views.ShouldHaveSingleItem().Root;
                Nodes[json] = node;
            }

            return node;
        }
    }

    /// <summary>只读文本面节点（schema 缺省：容量 20 000 / 按宽度折行 / 画空行标记）。</summary>
    /// <param name="title">标题（缺省「日志」）。</param>
    /// <returns>节点。</returns>
    internal static ViewNode TextLog(string title = "日志") =>
        Node(
            $$"""{ "kind": "textBox", "fontSize": 0, "textColor": "", "titleFontSize": 0, "titleColor": "", "readonly": true, "title": "{{title}}", "data": "log" }""",
            """[ { "name": "log", "kind": "Text" } ]""");

    /// <summary>
    /// <see cref="Node"/> 的同伴：把**同一份声明里的数据源表**也带回来。
    /// </summary>
    /// <param name="rootJson">根节点的声明片段。</param>
    /// <param name="sourcesJson">数据源声明片段。</param>
    /// <returns>根节点 + 数据源表（顺序 = 声明顺序）。</returns>
    /// <remarks>
    /// 视图模型的 <c>dataSource</c> 参数**只为取「框外单位标签」**（`ViewSourceDeclaration.Unit`）——
    /// 单位写在与控件分开的 `sources[]` 里，所以这里要能同时拿到两半。
    /// </remarks>
    internal static (ViewNode Node, IReadOnlyList<ViewSourceDeclaration> Sources) NodeWithSources(
        string rootJson,
        string sourcesJson)
    {
        var document = Read(
            $$"""
            {
              "version": 1,
              "sources": {{sourcesJson}},
              "views": [ { "root": {{rootJson}} } ]
            }
            """);

        return (document.Views.ShouldHaveSingleItem().Root, document.Sources);
    }
}
