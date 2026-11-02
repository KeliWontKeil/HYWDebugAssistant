using System.Text.Json;

namespace HYWDebugAssistant.Hosting.Packages;

/// <summary>
/// 模块包清单（manifest）：**包 = manifest + 实现绑定 + 可选资源**。
/// </summary>
/// <remarks>
/// <para>manifest 是**包级**元数据；模块内还有代码级声明（<c>[ModuleDefinition]</c> 特性）。
/// 加载器会校验两者一致（<c>Id</c> 必须相同），不一致即拒绝加载并报 Issue。</para>
/// <para>M1 的物理形式（临时值，C8-2 裁决）：**目录 + <c>module.json</c> + 实现程序集**；
/// 最终形式（目录/压缩包）属 R 项。</para>
/// </remarks>
public sealed record ModuleManifest
{
    /// <summary>模块标识字面量（<c>作者/模块名</c>，如 <c>hyw/text-sender</c>）。</summary>
    public string Id { get; init; } = string.Empty;

    /// <summary>实现程序集文件名（相对包目录）。</summary>
    public string EntryAssembly { get; init; } = string.Empty;

    /// <summary>实现类型全名（模块入口类型）。</summary>
    public string EntryType { get; init; } = string.Empty;

    /// <summary>依赖的其他模块标识字面量；缺失依赖 → Issue，**不静默降级**。</summary>
    public string[] DependsOn { get; init; } = [];

    /// <summary>可选资源（相对包目录的路径）。</summary>
    public string[] Resources { get; init; } = [];
}

/// <summary>manifest 读取器（<c>module.json</c>）。</summary>
public static class ModuleManifestReader
{
    /// <summary>清单文件名（包目录下）。</summary>
    public const string FileName = "module.json";

    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
    };

    /// <summary>读取并反序列化清单；失败时给出可读原因（**不抛异常**，由调用方转 Issue）。</summary>
    public static bool TryRead(string packageDirectory, out ModuleManifest? manifest, out string? error)
    {
        manifest = null;
        error = null;

        if (string.IsNullOrWhiteSpace(packageDirectory) || !Directory.Exists(packageDirectory))
        {
            error = HostingText.Get("hosting.manifest.packageDirMissing", packageDirectory);
            return false;
        }

        var path = Path.Combine(packageDirectory, FileName);
        if (!File.Exists(path))
        {
            error = HostingText.Get("hosting.manifest.fileMissing", FileName, path);
            return false;
        }

        try
        {
            manifest = JsonSerializer.Deserialize<ModuleManifest>(File.ReadAllText(path), Options);
        }
        catch (JsonException ex)
        {
            error = HostingText.Get("hosting.manifest.parseFailed", FileName, ex.Message);
            return false;
        }

        if (manifest is null)
        {
            error = HostingText.Get("hosting.manifest.empty", FileName);
            return false;
        }

        return true;
    }
}
