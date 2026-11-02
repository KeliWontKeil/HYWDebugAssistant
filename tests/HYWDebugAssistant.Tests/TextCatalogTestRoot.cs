using System.Text;
using System.Text.Json;
using HYWDebugAssistant.Sdk.Text;

namespace HYWDebugAssistant.Tests;

/// <summary>
/// **文本表测试根**：临时目录 + <see cref="TextCatalog.UseRoot"/>；用完删目录、解除显式根。
/// </summary>
/// <remarks>
/// 表已经不住在程序集里（不再有内嵌资源），测试必须自己造一个"资源根"：
/// 布局与出厂一致 —— `<根>/<程序集简名>/<culture>[.<area>].json`，
/// 这样读表走的是与产品**完全相同**的代码路径。
/// </remarks>
internal sealed class TextCatalogTestRoot : IDisposable
{
    private TextCatalogTestRoot(string root)
    {
        Root = root;
        OwnerDirectory = Path.Combine(root, OwnerName);
    }

    /// <summary>本测试程序集的简名（= 拥有者目录名）。</summary>
    public static string OwnerName => typeof(TextCatalogTestRoot).Assembly.GetName().Name!;

    /// <summary>临时"资源根"。</summary>
    public string Root { get; }

    /// <summary>本程序集的表目录（`<根>/<程序集简名>/`）。</summary>
    public string OwnerDirectory { get; }

    /// <summary>本夹具写过的表项（键 → 值；同一键先者胜，与运行期"先者胜"一致）。</summary>
    public Dictionary<string, string> Entries { get; } = new(StringComparer.Ordinal);

    /// <summary>
    /// 造一份**探针表**根：基础表 + 一份分片 + 语言中立表（分片与基表**没有**重复键，
    /// 重复键的场景由用例自己往这个根里再加一份分片）。
    /// </summary>
    public static TextCatalogTestRoot CreateWithProbeTables()
    {
        var tables = new TextCatalogTestRoot(NewDirectory());

        tables.WriteTable("zh-Hans.json", """
            {
              "language": "zh-Hans",
              "entries": {
                "probe.base.label": "探针基础表",
                "probe.duplicate.label": "基础表优先",
                "probe.format.message": "值 {0} 无效"
              }
            }
            """);

        tables.WriteTable("zh-Hans.settings.json", """
            {
              "language": "zh-Hans",
              "entries": {
                "probe.shard.label": "分片表"
              }
            }
            """);

        tables.WriteTable("zh.json", """
            {
              "language": "zh",
              "entries": {
                "probe.neutral.label": "语言中立表"
              }
            }
            """);

        tables.Use();
        return tables;
    }

    /// <summary>造一个**空**资源根（没有任何拥有者目录；用例自己写表）。</summary>
    public static TextCatalogTestRoot CreateEmpty() => new(NewDirectory());

    /// <summary>写一份表到本程序集的表目录（目录不存在时就地创建；文件名即表身份）。</summary>
    /// <param name="fileName">表文件名（如 <c>zh-Hans.json</c>）。</param>
    /// <param name="content">表内容。</param>
    public void WriteTable(string fileName, string content)
    {
        _ = Directory.CreateDirectory(OwnerDirectory);
        File.WriteAllText(
            Path.Combine(OwnerDirectory, fileName),
            content,
            new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));

        MergeEntries(content);
    }

    /// <summary>把一份表里的 entries 收进 <see cref="Entries"/>（坏文件 / 坏 JSON 直接跳过）。</summary>
    /// <param name="content">表内容。</param>
    private void MergeEntries(string content)
    {
        try
        {
            using var document = JsonDocument.Parse(content);

            if (!document.RootElement.TryGetProperty("entries", out var entries)
                || entries.ValueKind != JsonValueKind.Object)
            {
                return;
            }

            foreach (var property in entries.EnumerateObject())
            {
                _ = Entries.TryAdd(property.Name, property.Value.GetString() ?? string.Empty);
            }
        }
        catch (JsonException)
        {
            // 坏表由用例自己造出来（专门验证跳过坏文件），不是本夹具的错误。
        }
    }

    /// <summary>把文本表根切到本临时根（并清空表缓存 / 语言扫描缓存）。</summary>
    public void Use() => TextCatalog.UseRoot(Root);

    /// <inheritdoc />
    public void Dispose()
    {
        TextCatalog.UseRoot(null);   // 回到默认根（仓库 resources/i18n），不把临时根留给后面的用例

        try
        {
            Directory.Delete(Root, recursive: true);
        }
        catch (IOException)
        {
            // 临时目录删不掉不影响用例结论（操作系统会自己清 temp）
        }
        catch (UnauthorizedAccessException)
        {
        }
    }

    private static string NewDirectory() =>
        Path.Combine(Path.GetTempPath(), "hyw-text-tables-" + Guid.NewGuid().ToString("N"));
}
