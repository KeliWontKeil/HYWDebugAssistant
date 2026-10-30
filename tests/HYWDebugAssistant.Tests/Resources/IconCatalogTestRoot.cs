using System.Text;
using HYWDebugAssistant.Sdk.Resources;

namespace HYWDebugAssistant.Tests.Resources;

/// <summary>
/// **图标清单测试根**：临时目录 + <see cref="IconCatalog.UseRoot"/>；用完删目录、解除显式根。
/// </summary>
/// <remarks>
/// 布局与出厂一致 —— `<根>/<程序集简名>/icons.json` 指向同目录下的 `.svg`，
/// 这样读表走的是与产品**完全相同**的代码路径。
/// </remarks>
internal sealed class IconCatalogTestRoot : IDisposable
{
    /// <summary>探针图标一：20×20 画布。</summary>
    public const string ProbeRefreshSvg =
        """<svg width="20" height="20" viewBox="0 0 20 20" xmlns="http://www.w3.org/2000/svg"><path d="M0 0H10V10H0Z" /></svg>""";

    /// <summary>探针图标二：24×24 画布（与探针一不同，便于验证缓存/替换）。</summary>
    public const string ProbeLowerSvg =
        """<svg width="24" height="24" viewBox="0 0 24 24" xmlns="http://www.w3.org/2000/svg"><path d="M0 0H20V20H0Z" /></svg>""";

    private IconCatalogTestRoot(string root)
    {
        Root = root;
        OwnerDirectory = Path.Combine(root, OwnerName);
    }

    /// <summary>本测试程序集的简名（= 拥有者目录名）。</summary>
    public static string OwnerName => typeof(IconCatalogTestRoot).Assembly.GetName().Name!;

    /// <summary>临时"资源根"。</summary>
    public string Root { get; }

    /// <summary>本程序集的表目录（`<根>/<程序集简名>/`）。</summary>
    public string OwnerDirectory { get; }

    /// <summary>本程序集的清单文件。</summary>
    public string ManifestPath => Path.Combine(OwnerDirectory, "icons.json");

    /// <summary>造一份**探针表**根（两个键：20×20 与 24×24 各一）。</summary>
    public static IconCatalogTestRoot CreateWithProbeAssets()
    {
        var tables = Create();

        tables.WriteAsset("probe/refresh.svg", ProbeRefreshSvg);
        tables.WriteAsset("probe/lower.svg", ProbeLowerSvg);
        tables.WriteManifest("""
            {
              "schemaVersion": 1,
              "entries": {
                "icon.probe.refresh": "probe/refresh.svg",
                "icon.probe.lower": "probe/lower.svg"
              }
            }
            """);

        tables.Use();
        return tables;
    }

    /// <summary>造一个**空**资源根（没有任何拥有者目录；用例自己写表）。</summary>
    public static IconCatalogTestRoot Create() => new(NewDirectory());

    /// <summary>写本程序集的清单（目录不存在时就地创建）。</summary>
    /// <param name="content">清单内容。</param>
    public void WriteManifest(string content)
    {
        _ = Directory.CreateDirectory(OwnerDirectory);
        File.WriteAllText(
            ManifestPath,
            content,
            new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
    }

    /// <summary>写一个 SVG（相对拥有者目录；目录不存在时就地创建）。</summary>
    /// <param name="relativePath">相对路径（`/` 分隔）。</param>
    /// <param name="content">SVG 内容。</param>
    public void WriteAsset(string relativePath, string content)
    {
        var path = Path.Combine(OwnerDirectory, relativePath.Replace('/', Path.DirectorySeparatorChar));
        _ = Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, content, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
    }

    /// <summary>把图标表根切到本临时根（并让缓存代号递增）。</summary>
    public void Use() => IconCatalog.UseRoot(Root);

    /// <inheritdoc />
    public void Dispose()
    {
        IconCatalog.UseRoot(null);   // 回到默认根，不把临时根留给后面的用例

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
        Path.Combine(Path.GetTempPath(), "hyw-icon-tables-" + Guid.NewGuid().ToString("N"));
}
