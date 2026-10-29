namespace HYWDebugAssistant.Core.Api;

/// <summary>
/// **一个磁盘主题的只读视图**（`settings\appearance\themes\&lt;id&gt;\theme.json` 的元数据）。
/// </summary>
/// <remarks>
/// <list type="bullet">
///   <item><b>它只是元数据、不含字节</b>：壁纸打开时按需从同一文件夹取（
///     <see cref="IRuntimeQuery.TryGetThemeWallpaper"/>），设置列表与主题目录不必把所有图片读进内存；</item>
///   <item><b>主题自包含</b>：清单 + 壁纸在同一个文件夹里 ⇒ "导出一个主题" = 复制一个文件夹；</item>
///   <item><b>种子与用户主题同格式</b>：出厂主题投放 = 把文件夹写进 `themes\`，运行期没有特殊分支；</item>
///   <item><b>`palette` 稀疏</b>：只写用户改过的 token，没写的跟随该主题基类型的内置值；</item>
///   <item><b>内置 `light` / `dark` 不在这张表里</b>：它们是代码里的色板，永远只读、不落盘。</item>
/// </list>
/// </remarks>
/// <param name="Id">稳定 id（规范名，小写；= 文件夹名）。</param>
/// <param name="Title">显示名（用户可改；只改清单，不搬文件夹）。</param>
/// <param name="Base">基类型（决定继承浅色 / 深色内置色板）。</param>
/// <param name="Palette">稀疏色板覆盖：token → `#RRGGBB(AA)`。</param>
/// <param name="WallpaperFileName">同目录壁纸文件名（空 = 无壁纸；只有文件名，不含目录）。</param>
/// <param name="WallpaperVeilOpacity">壁纸遮罩浓度（0–0.9）。</param>
/// <param name="SurfaceOpacity">控件表面不透明度（0–1）。</param>
public sealed record ThemeFileView(
    string Id,
    string Title,
    ThemeBaseKind Base,
    IReadOnlyDictionary<string, string> Palette,
    string? WallpaperFileName,
    double WallpaperVeilOpacity,
    double SurfaceOpacity);

/// <summary>
/// **保存一个主题**的请求：清单字段 + 可选壁纸字节（`settings` 页「应用 / 确认」时提交）。
/// </summary>
/// <remarks>
/// <list type="bullet">
///   <item><b>壁纸随主题写</b>：不再有"导入图片得到资产引用 + 设置里存引用"的两段式 ——
///     图片字节与清单写进同一个文件夹，删主题 = 删文件夹；</item>
///   <item><b>`WallpaperPng` 与 `ClearWallpaper` 二选一</b>：给了新图就写图（并覆盖旧图），
///     只说清除就删旧图；两者都空 = 保留现有壁纸不动；</item>
///   <item><b>id 稳定</b>：改名只改 <see cref="Title"/>，不搬文件夹（覆盖表不需要跟着改键）。</item>
/// </list>
/// </remarks>
/// <param name="Id">稳定 id（非法 / 保留名 ⇒ 拒绝）。</param>
/// <param name="Title">显示名（空 ⇒ “自定义主题”）。</param>
/// <param name="Base">基类型。</param>
/// <param name="Palette">稀疏色板覆盖。</param>
/// <param name="WallpaperPng">新壁纸 PNG 字节（空 = 不换图）。</param>
/// <param name="ClearWallpaper">是否清除现有壁纸（<paramref name="WallpaperPng"/> 非空时以新图为准）。</param>
/// <param name="WallpaperVeilOpacity">壁纸遮罩浓度（0–0.9；越界按边界收敛）。</param>
/// <param name="SurfaceOpacity">控件表面不透明度（0–1；越界按边界收敛）。</param>
public sealed record ThemeSaveRequest(
    string Id,
    string Title,
    ThemeBaseKind Base,
    IReadOnlyDictionary<string, string> Palette,
    byte[]? WallpaperPng,
    bool ClearWallpaper,
    double WallpaperVeilOpacity,
    double SurfaceOpacity);
