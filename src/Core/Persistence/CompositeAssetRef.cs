using System.Security.Cryptography;

namespace HYWDebugAssistant.Core.Persistence;

/// <summary>
/// **组合件资产的唯一命名规则**：<c>assets/&lt;sha256&gt;.&lt;ext&gt;</c>。
/// </summary>
/// <remarks>
/// <para>
/// 引用即文件名：JSON 里存的就是这个相对路径，<c>Composite.Assets</c> 也以它为键。
/// 因此壁纸与控件图片共用同一个 <c>assets/</c> 目录和同一条命名规则，
/// 不再按用途强制拆分扩展名（旧实现的 <c>.img</c> 与 hash→文件名映射已删除）。
/// </para>
/// <para>
/// 同一份字节 + 同一扩展名 ⇒ 同一个引用 ⇒ 同一个文件；壁纸和图片可以共享同一个资产。
/// </para>
/// </remarks>
internal static class CompositeAssetRef
{
    /// <summary>包内资产目录名。</summary>
    public const string DirectoryName = "assets";

    /// <summary>包内相对引用前缀（<c>assets/</c>）。</summary>
    public const string Prefix = DirectoryName + "/";

    /// <summary>单个资产文件的字节上限（32 MB，与图片选择/显示端一致）。</summary>
    public const long MaxAssetBytes = 32L * 1024 * 1024;

    private const int HashLength = 64;
    private const int MaxExtensionLength = 5;

    /// <summary>把引用归一为包内相对路径（<c>\</c> → <c>/</c>，去空白）；非 <c>assets/</c> ⇒ null。</summary>
    /// <param name="reference">JSON 中的资产引用。</param>
    public static string? Normalize(string? reference)
    {
        if (string.IsNullOrWhiteSpace(reference))
        {
            return null;
        }

        var value = reference.Replace('\\', '/').Trim();
        return value.StartsWith(Prefix, StringComparison.OrdinalIgnoreCase) ? value : null;
    }

    /// <summary>严格解析一个资产引用：只接受 <c>assets/&lt;64 hex&gt;[.ext]</c>，不允许子目录。</summary>
    /// <param name="reference">JSON 中的资产引用。</param>
    /// <param name="normalized">归一后的相对引用（成功时）。</param>
    /// <param name="fileName">纯文件名（成功时）。</param>
    public static bool TryParse(string? reference, out string normalized, out string fileName)
    {
        normalized = string.Empty;
        fileName = string.Empty;

        var value = Normalize(reference);
        if (value is null)
        {
            return false;
        }

        var name = value[Prefix.Length..];
        if (name.Length == 0
            || name.Contains('/')
            || !IsValidFileName(name))
        {
            return false;
        }

        normalized = Prefix + name;
        fileName = name;
        return true;
    }

    /// <summary>文件名是否为内容寻址资产名：64 位十六进制 + 可选 1–5 位字母数字扩展名。</summary>
    /// <param name="fileName">纯文件名（不含目录）。</param>
    public static bool IsValidFileName(string fileName)
    {
        if (fileName.Length == 0 || fileName.Contains('/') || fileName.Contains('\\'))
        {
            return false;
        }

        var dot = fileName.IndexOf('.');
        var hash = dot >= 0 ? fileName[..dot] : fileName;
        if (hash.Length != HashLength || !hash.All(Uri.IsHexDigit))
        {
            return false;
        }

        if (dot < 0)
        {
            return true;
        }

        var extension = fileName[(dot + 1)..];
        return extension.Length is > 0 and <= MaxExtensionLength
               && extension.All(char.IsAsciiLetterOrDigit);
    }

    /// <summary>按字节内容与真实扩展名生成引用；空字节/超限/未知扩展名 ⇒ false。</summary>
    /// <param name="bytes">资产字节。</param>
    /// <param name="extension">扩展名（不含点；<c>jpeg</c> 归一为 <c>jpg</c>）。</param>
    /// <param name="reference">生成的无前导目录引用（<c>assets/&lt;hash&gt;.&lt;ext&gt;</c>）。</param>
    /// <param name="fileName">生成的文件名（<c>&lt;hash&gt;.&lt;ext&gt;</c>）。</param>
    public static bool TryCreate(
        byte[]? bytes,
        string? extension,
        out string reference,
        out string fileName)
    {
        reference = string.Empty;
        fileName = string.Empty;

        if (bytes is not { Length: > 0 } || bytes.Length > MaxAssetBytes)
        {
            return false;
        }

        if (NormalizeExtension(extension) is not { } ext)
        {
            return false;
        }

        var hash = Convert.ToHexStringLower(SHA256.HashData(bytes));
        fileName = $"{hash}.{ext}";
        reference = Prefix + fileName;
        return true;
    }

    private static string? NormalizeExtension(string? extension)
    {
        if (string.IsNullOrWhiteSpace(extension))
        {
            return null;
        }

        var ext = extension.Trim().TrimStart('.').ToLowerInvariant();
        return ext switch
        {
            "png" or "jpg" or "gif" or "webp" or "bmp" => ext,
            "jpeg" => "jpg",
            _ => null,
        };
    }
}
