using System.Globalization;
using System.Text.Json.Nodes;
using HYWDebugAssistant.Core.Api;
using HYWDebugAssistant.Hosting.Diagnostics;
using HYWDebugAssistant.Infrastructure.Json;
using HYWDebugAssistant.Sdk.Reporting;

namespace HYWDebugAssistant.Core.Themes;

/// <summary>
/// **主题文件夹存储**：`&lt;ThemesRoot&gt;\&lt;id&gt;\theme.json` + 同目录壁纸（读 / 写 / 删）。
/// </summary>
/// <remarks>
/// <list type="bullet">
///   <item><b>主题是设置内部的一个子项</b>：每个主题一个文件夹，清单与壁纸同住 ⇒ 复制文件夹即可搬走一套主题；</item>
///   <item><b>种子与用户主题同格式</b>：投放只是把同一形状的文件夹写进来，运行期没有特殊分支；</item>
///   <item><b>id 的权威是文件夹名</b>：清单里的 `id` 是冗余说明；读时一律用文件夹名（复制后改名也能用），
///     写时用请求里的 id 建文件夹并写进清单；id 过 <see cref="ThemeIds.Normalize"/> 收敛并拒绝保留名；</item>
///   <item><b>坏清单跳过 + 原因</b>：版本不认识 / 字段非法 / 壁纸名越界 ⇒ 该主题不进目录，记一条 Warning
///     （同一份坏清单只报一次，避免每次查询都刷同一批 Issue）；</item>
///   <item><b>原子写</b>：清单走 `JsonFileStore`（同目录临时文件 → 替换）；壁纸先写临时文件再替换；
///     先落壁纸再落清单 ⇒ 清单不会指向一个还没写好的文件；</item>
///   <item><b>色板键只校验格式</b>：完整校验（键必须是色板契约里登记过的 token）需要 `<c>Ui</c>` 的
///     <c>PaletteManifest</c>，而 `<c>Core</c>` 不能引用 `<c>Ui</c>`（依赖方向）⇒ 这里只校验
///     「字母开头，字母 / 数字 / 下划线」；完整校验由阶段 E 的审计脚本负责。</item>
/// </list>
/// </remarks>
public sealed class ThemeStore
{
    /// <summary>主题清单文件名（每个主题文件夹里一个）。</summary>
    public const string ManifestFileName = "theme.json";

    /// <summary>保存壁纸时使用的文件名（读时以清单的 `wallpaper` 字段为准，可以是别的名字）。</summary>
    public const string WallpaperFileName = "wallpaper.png";

    /// <summary>主题清单版本（本应用能读的最高版本）。</summary>
    public const int CurrentSchemaVersion = 1;

    /// <summary>单个壁纸文件的大小上限（32 MB）——防止把超大图片读进内存。</summary>
    public const long MaxWallpaperBytes = 32L * 1024 * 1024;

    private static readonly StringComparer IdComparer = StringComparer.OrdinalIgnoreCase;

    /// <summary>已报告过的坏清单（键 = 清单路径；同一份只报一次）。</summary>
    private readonly HashSet<string> _reportedBadManifests = new(StringComparer.OrdinalIgnoreCase);

    private readonly IIssueSink _issues;

    /// <summary>创建存储。</summary>
    /// <param name="themesRoot">主题根（生产 = `StoragePaths.ThemesRoot`；测试可注入临时目录）。</param>
    /// <param name="issues">Issue 上报口（缺省丢弃；生产 = 运行时 Issue 中心）。</param>
    public ThemeStore(string themesRoot, IIssueSink? issues = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(themesRoot);

        ThemesRoot = Path.GetFullPath(themesRoot);
        _issues = issues ?? NullIssueSink.Instance;
    }

    /// <summary>主题根目录（`&lt;设置根&gt;\appearance\themes`）。</summary>
    public string ThemesRoot { get; }

    /// <summary>读**全部**主题（按 id 序；坏清单跳过并记 Warning）。</summary>
    /// <returns>主题只读视图（不含图片字节）；目录不存在 ⇒ 空表。</returns>
    public IReadOnlyList<ThemeFileView> LoadAll()
    {
        if (!Directory.Exists(ThemesRoot))
        {
            return [];
        }

        List<string> directories;
        try
        {
            directories = [.. Directory.EnumerateDirectories(ThemesRoot).OrderBy(path => path, StringComparer.Ordinal)];
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            ReportOnce("enumerate:" + ThemesRoot, CoreText.Get("core.theme.enumerateFailed", ThemesRoot, ex.Message));
            return [];
        }

        var themes = new List<ThemeFileView>(directories.Count);

        foreach (var directory in directories)
        {
            // **id 的权威是文件夹名**（清单里的 id 只是冗余说明）：复制文件夹后改名仍能用。
            var id = Path.GetFileName(directory);
            if (!TryNormalizeId(id, out var normalized))
            {
                ReportOnce(directory, CoreText.Get("core.theme.folderNameInvalid", directory));
                continue;
            }

            var manifestPath = Path.Combine(directory, ManifestFileName);
            if (!File.Exists(manifestPath))
            {
                ReportOnce(manifestPath, CoreText.Get("core.theme.manifestMissing", ManifestFileName, directory));
                continue;
            }

            if (!TryReadManifest(manifestPath, normalized, out var view, out var reason) || view is null)
            {
                ReportOnce(manifestPath, CoreText.Get(
                    "core.theme.manifestUnavailable",
                    reason ?? CoreText.Get("core.theme.reasonUnknown"),
                    manifestPath));
                continue;
            }

            themes.Add(view);
        }

        return themes;
    }

    /// <summary>读一个主题（不存在 / 清单坏 ⇒ <see langword="null"/>；查询路径**不**报 Issue）。</summary>
    /// <param name="themeId">主题 id。</param>
    public ThemeFileView? Read(string themeId)
    {
        if (!TryNormalizeId(themeId, out var id))
        {
            return null;
        }

        var manifestPath = Path.Combine(ThemesRoot, id, ManifestFileName);
        if (!File.Exists(manifestPath))
        {
            return null;
        }

        return TryReadManifest(manifestPath, id, out var view, out _) ? view : null;
    }

    /// <summary>该 id 是否已经有一个主题文件夹（= 清单存在）。</summary>
    /// <param name="themeId">主题 id。</param>
    public bool Exists(string themeId) =>
        TryNormalizeId(themeId, out var id) && File.Exists(Path.Combine(ThemesRoot, id, ManifestFileName));

    /// <summary>
    /// 读某个主题的壁纸字节（无壁纸 / 文件缺失 / 超上限 / IO 失败 ⇒ false；**不**报 Issue）。
    /// </summary>
    /// <param name="themeId">主题 id。</param>
    /// <param name="bytes">PNG 字节。</param>
    public bool TryReadWallpaper(string themeId, out byte[]? bytes)
    {
        bytes = null;

        if (!TryNormalizeId(themeId, out var id))
        {
            return false;
        }

        var directory = Path.Combine(ThemesRoot, id);
        var manifestPath = Path.Combine(directory, ManifestFileName);
        if (!File.Exists(manifestPath)
            || !TryReadManifest(manifestPath, id, out var view, out _)
            || view?.WallpaperFileName is not { Length: > 0 } fileName)
        {
            return false;
        }

        try
        {
            var path = Path.Combine(directory, fileName);
            var info = new FileInfo(path);
            if (!info.Exists || info.Length is <= 0 or > MaxWallpaperBytes)
            {
                return false;
            }

            bytes = File.ReadAllBytes(path);
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
        {
            return false;
        }
    }

    /// <summary>
    /// **写一个主题文件夹**：校验 → 建目录 → （可选）写壁纸 → 原子写清单。
    /// </summary>
    /// <param name="request">清单字段 + 可选壁纸字节。</param>
    /// <returns>成功 / 失败原因（失败即**不产生新清单**）。</returns>
    public CommandResult Save(ThemeSaveRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (!TryNormalizeId(request.Id, out var id))
        {
            return CommandResult.Fail([CoreText.Get("core.theme.idInvalidCreate", request.Id)]);
        }

        if (request.Palette is null)
        {
            return CommandResult.Fail([CoreText.Get("core.theme.paletteEmpty")]);
        }

        var palette = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var (token, hex) in request.Palette)
        {
            if (!IsTokenKey(token))
            {
                return CommandResult.Fail([CoreText.Get("core.theme.tokenInvalid", token)]);
            }

            if (!string.IsNullOrWhiteSpace(hex))
            {
                palette[token] = hex.Trim();
            }
        }

        if (request.WallpaperPng is { Length: > 0 } oversized && oversized.Length > MaxWallpaperBytes)
        {
            return CommandResult.Fail([CoreText.Get(
                "core.theme.wallpaperTooLarge",
                oversized.Length,
                MaxWallpaperBytes / 1024 / 1024)]);
        }

        var veil = Math.Clamp(Finite(request.WallpaperVeilOpacity, 0.55), 0, 0.9);
        var surface = Math.Clamp(Finite(request.SurfaceOpacity, 0.70), 0, 1);
        var title = string.IsNullOrWhiteSpace(request.Title)
            ? CoreText.Get("core.theme.defaultTitle")
            : request.Title.Trim();

        var directory = Path.Combine(ThemesRoot, id);
        var manifestPath = Path.Combine(directory, ManifestFileName);

        try
        {
            _ = Directory.CreateDirectory(directory);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
        {
            return CommandResult.Fail([CoreText.Get("core.theme.createDirFailed", directory, ex.Message)]);
        }

        // 现有壁纸名（用于"不换图就保留"和"清除时删对文件"）
        string? previousWallpaper = null;
        if (File.Exists(manifestPath)
            && TryReadManifest(manifestPath, id, out var existing, out _)
            && existing is not null)
        {
            previousWallpaper = existing.WallpaperFileName;
        }

        var notices = new List<string>();
        var wallpaper = previousWallpaper;

        if (request.WallpaperPng is { Length: > 0 } newPng)
        {
            // 新图统一落成 `wallpaper.png`（清单随之指向它），旧名文件不删也无害（已不被引用）。
            if (!TryWriteBytesAtomic(Path.Combine(directory, WallpaperFileName), newPng, out var wallpaperError))
            {
                return CommandResult.Fail([CoreText.Get("core.theme.wallpaperWriteFailed", wallpaperError)]);
            }

            wallpaper = WallpaperFileName;
        }
        else if (request.ClearWallpaper)
        {
            wallpaper = null;

            foreach (var stale in new[] { previousWallpaper, WallpaperFileName }
                .Where(name => name is { Length: > 0 })
                .Distinct(IdComparer))
            {
                var stalePath = Path.Combine(directory, stale!);
                try
                {
                    if (File.Exists(stalePath))
                    {
                        File.Delete(stalePath);
                    }
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    // 残留一张不再被引用的图片不影响正确性；提示一下即可，不阻断保存。
                    notices.Add(CoreText.Get("core.theme.staleWallpaperDeleteFailed", stale, ex.Message));
                }
            }
        }

        var manifest = BuildManifest(id, title, request.Base, palette, wallpaper, veil, surface);
        if (!JsonFileStore.TryWriteObjectAtomic(manifestPath, manifest, out var writeError))
        {
            return CommandResult.Fail([CoreText.Get("core.theme.manifestWriteFailed", writeError)]);
        }

        return CommandResult.Ok(notices);
    }

    /// <summary>删除一个主题文件夹（含壁纸；不存在视为已删除）。</summary>
    /// <param name="themeId">主题 id。</param>
    public CommandResult Delete(string themeId)
    {
        if (!TryNormalizeId(themeId, out var id))
        {
            return CommandResult.Fail([CoreText.Get("core.theme.idInvalidDelete", themeId)]);
        }

        var directory = Path.Combine(ThemesRoot, id);
        if (!Directory.Exists(directory))
        {
            return CommandResult.Ok([CoreText.Get("core.theme.deleteNotFound", id)]);
        }

        try
        {
            // 防御：绝不顺着链接删到别处
            if ((File.GetAttributes(directory) & FileAttributes.ReparsePoint) != 0)
            {
                return CommandResult.Fail([CoreText.Get("core.theme.symlinkRejected", directory)]);
            }

            Directory.Delete(directory, recursive: true);
            return CommandResult.Ok([CoreText.Get("core.theme.deleted", id)]);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
        {
            return CommandResult.Fail([CoreText.Get("core.theme.deleteFailed", id, ex.Message)]);
        }
    }

    /// <summary>
    /// 读一份主题清单（**id 由调用方给**：文件夹名 / 种子清单里的 id）。
    /// </summary>
    /// <param name="manifestPath">清单路径。</param>
    /// <param name="id">已规范化的主题 id。</param>
    /// <param name="view">读出的视图（成功时非 null）。</param>
    /// <param name="reason">失败原因（成功时 null）。</param>
    internal static bool TryReadManifest(
        string manifestPath,
        string id,
        out ThemeFileView? view,
        out string? reason)
    {
        view = null;
        reason = null;

        if (!JsonFileStore.TryReadObject(manifestPath, out var root, out var readError) || root is null)
        {
            reason = readError ?? CoreText.Get("core.theme.unreadable");
            return false;
        }

        return TryParseManifest(root, id, out view, out reason);
    }

    /// <summary>读一份**种子**清单（id 取清单里的 `id`，缺失时回落文件夹名）。</summary>
    /// <param name="manifestPath">清单路径。</param>
    /// <param name="view">读出的视图（成功时非 null）。</param>
    /// <param name="reason">失败原因（成功时 null）。</param>
    internal static bool TryReadSeedManifest(string manifestPath, out ThemeFileView? view, out string? reason)
    {
        view = null;
        reason = null;

        if (!JsonFileStore.TryReadObject(manifestPath, out var root, out var readError) || root is null)
        {
            reason = readError ?? CoreText.Get("core.theme.unreadable");
            return false;
        }

        if (!TryNormalizeId(Text(root, "id"), out var id))
        {
            var folderName = Path.GetFileName(Path.GetDirectoryName(manifestPath));
            if (!TryNormalizeId(folderName, out id))
            {
                reason = CoreText.Get("core.theme.idMissing");
                return false;
            }
        }

        return TryParseManifest(root, id, out view, out reason);
    }

    /// <summary>把规范名收敛成"能当文件夹名的主题 id"（拒绝保留名 / 分隔符 / 以 . 开头）。</summary>
    /// <param name="raw">用户 / 文件里的 id。</param>
    /// <param name="id">规范化后的 id。</param>
    internal static bool TryNormalizeId(string? raw, out string id)
    {
        id = string.Empty;

        if (string.IsNullOrWhiteSpace(raw))
        {
            return false;
        }

        var normalized = ThemeIds.Normalize(raw);
        if (ThemeIds.IsReserved(normalized) || !IsSafeSegment(normalized))
        {
            return false;
        }

        id = normalized;
        return true;
    }

    /// <summary>是不是一个"能当单段文件名"的名字（不含分隔符、不以 . 开头、不是 . / ..）。</summary>
    /// <param name="value">候选名字。</param>
    internal static bool IsSafeSegment(string value)
    {
        if (string.IsNullOrWhiteSpace(value) || value.StartsWith('.'))
        {
            return false;
        }

        if (!string.Equals(Path.GetFileName(value), value, StringComparison.Ordinal))
        {
            return false;
        }

        var invalid = Path.GetInvalidFileNameChars();
        return !value.Any(ch => ch is '/' or '\\' || invalid.Contains(ch));
    }

    /// <summary>色板 token 名的**格式**校验（字母开头，字母 / 数字 / 下划线；不做契约成员校验）。</summary>
    /// <param name="key">token 名。</param>
    internal static bool IsTokenKey(string? key) =>
        !string.IsNullOrWhiteSpace(key)
        && char.IsAsciiLetter(key[0])
        && key.All(ch => char.IsAsciiLetterOrDigit(ch) || ch == '_');

    // ---------------------------------------------------------------- 内部

    private static bool TryParseManifest(
        JsonObject root,
        string id,
        out ThemeFileView? view,
        out string? reason)
    {
        view = null;
        reason = null;

        var version = ReadInt(root, "schemaVersion", CurrentSchemaVersion);
        if (version > CurrentSchemaVersion)
        {
            reason = CoreText.Get("core.theme.versionUnknown", version, CurrentSchemaVersion);
            return false;
        }

        string? wallpaper = null;
        if (Text(root, "wallpaper") is { Length: > 0 } rawWallpaper)
        {
            if (!IsSafeSegment(rawWallpaper))
            {
                reason = CoreText.Get("core.theme.wallpaperNameInvalid", rawWallpaper);
                return false;
            }

            wallpaper = rawWallpaper;
        }

        var palette = new Dictionary<string, string>(StringComparer.Ordinal);
        if (root["palette"] is JsonObject paletteNode)
        {
            foreach (var (token, value) in paletteNode)
            {
                // 坏 token 名跳过（写侧会拒绝；源侧完整校验由阶段 E 的审计脚本负责）
                if (!IsTokenKey(token) || value?.ToString() is not { Length: > 0 } hex)
                {
                    continue;
                }

                palette[token] = hex.Trim();
            }
        }

        var title = Text(root, "title") is { Length: > 0 } rawTitle
            ? rawTitle.Trim()
            : CoreText.Get("core.theme.defaultTitle");
        var baseKind = string.Equals(Text(root, "base"), "dark", StringComparison.OrdinalIgnoreCase)
            ? ThemeBaseKind.Dark
            : ThemeBaseKind.Light;

        view = new ThemeFileView(
            id,
            title,
            baseKind,
            palette,
            wallpaper,
            Math.Clamp(Finite(ReadDouble(root, "wallpaperVeilOpacity", 0.55), 0.55), 0, 0.9),
            Math.Clamp(Finite(ReadDouble(root, "surfaceOpacity", 0.70), 0.70), 0, 1));

        return true;
    }

    private static JsonObject BuildManifest(
        string id,
        string title,
        ThemeBaseKind baseKind,
        IReadOnlyDictionary<string, string> palette,
        string? wallpaper,
        double veil,
        double surface)
    {
        var paletteNode = new JsonObject();
        foreach (var (token, hex) in palette)
        {
            paletteNode[token] = hex;
        }

        var manifest = new JsonObject
        {
            ["schemaVersion"] = CurrentSchemaVersion,
            ["id"] = id,
            ["title"] = title,
            ["base"] = baseKind == ThemeBaseKind.Dark ? "dark" : "light",
            ["palette"] = paletteNode,
            ["wallpaperVeilOpacity"] = Math.Round(veil, 4),
            ["surfaceOpacity"] = Math.Round(surface, 4),
        };

        if (wallpaper is { Length: > 0 })
        {
            manifest["wallpaper"] = wallpaper;
        }

        return manifest;
    }

    private static bool TryWriteBytesAtomic(string path, byte[] bytes, out string? error)
    {
        error = null;
        var temp = path + ".tmp-" + Guid.NewGuid().ToString("N");

        try
        {
            File.WriteAllBytes(temp, bytes);
            File.Move(temp, path, overwrite: true);
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
        {
            error = ex.Message;
            return false;
        }
        finally
        {
            try
            {
                if (File.Exists(temp))
                {
                    File.Delete(temp);
                }
            }
            catch (IOException)
            {
                // 清理失败不影响结论
            }
        }
    }

    private void ReportOnce(string key, string message)
    {
        if (!_reportedBadManifests.Add(key))
        {
            return;
        }

        _issues.Report("theme", new IssueReport(IssueCategory.ConfigurationError, IssueSeverity.Warning, message));
    }

    private static double Finite(double value, double fallback) => double.IsFinite(value) ? value : fallback;

    private static string? Text(JsonObject root, string name) =>
        root.TryGetPropertyValue(name, out var node) ? node?.ToString() : null;

    private static int ReadInt(JsonObject root, string name, int fallback) =>
        root.TryGetPropertyValue(name, out var node) && int.TryParse(node?.ToString(), out var value) ? value : fallback;

    // 清单里的浮点**一律按不变文化解析**：`0.55` 在 `,` 为小数分隔符的机器上会被读成 55
    // （逗号当千位分隔符），再被 clamp 成 0.9 —— 看起来"主题遮罩怎么突然最浓"。
    private static double ReadDouble(JsonObject root, string name, double fallback) =>
        root.TryGetPropertyValue(name, out var node)
        && node is not null
        && double.TryParse(node.ToString(), NumberStyles.Float, CultureInfo.InvariantCulture, out var value)
            ? value
            : fallback;
}
