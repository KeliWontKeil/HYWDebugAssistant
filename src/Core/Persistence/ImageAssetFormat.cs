namespace HYWDebugAssistant.Core.Persistence;

/// <summary>
/// **图片资产格式识别**：按文件头给出真实扩展名，让资产文件名保持原格式（PNG/JPG/GIF/WebP/BMP）。
/// </summary>
/// <remarks>
/// 组合件资产按内容寻址，扩展名只由字节本身决定——同一个文件无论从哪个入口选入，
/// 都落到同一个 <c>assets/&lt;sha256&gt;.&lt;ext&gt;</c>，壁纸与控件图片可以共享同一份资产。
/// 这里只做最少的签名判断；宽高解析仍由 <c>Sdk.Ui.Imaging.ImageHeader</c> 负责（Core 不引用 UI）。
/// </remarks>
internal static class ImageAssetFormat
{
    private static ReadOnlySpan<byte> PngSignature => [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A];

    /// <summary>按文件头返回扩展名（不含点）；无法识别 ⇒ <see langword="null"/>。</summary>
    /// <param name="bytes">完整图片字节（至少包含文件头）。</param>
    public static string? DetectExtension(ReadOnlySpan<byte> bytes)
    {
        if (bytes.Length >= 8 && bytes[..8].SequenceEqual(PngSignature))
        {
            return "png";
        }

        if (bytes.Length >= 3 && bytes[0] == 0xFF && bytes[1] == 0xD8 && bytes[2] == 0xFF)
        {
            return "jpg";
        }

        if (bytes.Length >= 6
            && bytes[0] == (byte)'G'
            && bytes[1] == (byte)'I'
            && bytes[2] == (byte)'F'
            && bytes[3] == (byte)'8'
            && (bytes[4] == (byte)'7' || bytes[4] == (byte)'9')
            && bytes[5] == (byte)'a')
        {
            return "gif";
        }

        if (bytes.Length >= 2 && bytes[0] == (byte)'B' && bytes[1] == (byte)'M')
        {
            return "bmp";
        }

        if (bytes.Length >= 12
            && bytes[0] == (byte)'R'
            && bytes[1] == (byte)'I'
            && bytes[2] == (byte)'F'
            && bytes[3] == (byte)'F'
            && bytes[8] == (byte)'W'
            && bytes[9] == (byte)'E'
            && bytes[10] == (byte)'B'
            && bytes[11] == (byte)'P')
        {
            return "webp";
        }

        return null;
    }
}
