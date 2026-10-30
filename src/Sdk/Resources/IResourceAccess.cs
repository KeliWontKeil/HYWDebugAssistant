namespace HYWDebugAssistant.Sdk.Resources;

/// <summary>
/// **模块自有资源包的只读访问面**：按"kind + 相对路径"读模块自己 `resources/` 下的文件。
/// </summary>
/// <remarks>
/// <list type="bullet">
///   <item><b>只读、只读自己的包</b>：实现由宿主给，根固定为**该模块目录下的 `resources/`**；
///     模块拿不到宿主的 `resources/`，也拿不到别的模块的；</item>
///   <item><b>相对路径、不得越界</b>：`kind` 是单段目录名，`relativePath` 是相对该 kind 的路径；
///     绝对路径 / `..` / 盘符一律拒绝（返回 <see langword="false"/>，不抛）；</item>
///   <item><b>只管自定义 kind</b>：标准文本表与图标表仍由框架的 `TextCatalog` / `IconCatalog` 读，
///     模块不需要、也不应该用本接口去读它们；</item>
///   <item><b>失败不抛</b>：文件不存在、越界、IO 出错都返回 <see langword="false"/>——
///     "资源没随包"不该让模块崩。</item>
/// </list>
/// </remarks>
public interface IResourceAccess
{
    /// <summary>
    /// 打开模块自有资源包里的一个文件（只读流，调用方负责释放）。
    /// </summary>
    /// <param name="kind">资源类别（`resources/` 下的第一级目录名，如 <c>templates</c>）。</param>
    /// <param name="relativePath">相对该类别目录的文件路径（`/` 或 `\` 分隔）。</param>
    /// <param name="stream">打开的只读流（失败时为 <see langword="null"/>）。</param>
    /// <returns>文件存在且路径合法时为 <see langword="true"/>。</returns>
    bool TryOpen(string kind, string relativePath, out Stream? stream);
}

/// <summary>资源访问面的公共实例与便捷读法。</summary>
public static class ResourceAccess
{
    /// <summary>**读不到任何东西**的访问面（未接线时的回落：不发包、拿不到模块目录都适用）。</summary>
    public static IResourceAccess Empty { get; } = new EmptyResourceAccess();

    /// <summary>把资源读成字节（小文件；失败 ⇒ <see langword="null"/>）。</summary>
    /// <param name="access">访问面。</param>
    /// <param name="kind">资源类别。</param>
    /// <param name="relativePath">相对路径。</param>
    public static byte[]? ReadAllBytes(this IResourceAccess access, string kind, string relativePath)
    {
        ArgumentNullException.ThrowIfNull(access);

        if (!access.TryOpen(kind, relativePath, out var stream) || stream is null)
        {
            return null;
        }

        using (stream)
        {
            using var buffer = new MemoryStream();
            stream.CopyTo(buffer);
            return buffer.ToArray();
        }
    }

    private sealed class EmptyResourceAccess : IResourceAccess
    {
        public bool TryOpen(string kind, string relativePath, out Stream? stream)
        {
            stream = null;
            return false;
        }
    }
}
