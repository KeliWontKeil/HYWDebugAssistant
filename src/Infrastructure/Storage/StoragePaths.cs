namespace HYWDebugAssistant.Infrastructure.Storage;

/// <summary>
/// **按功能分根的唯一路径来源**（AR-17）：设置 / 模块 / 组合 / 工作区 / 日志 各自一个根，
/// 出厂种子单独一处；任何要落盘的地方**只从这里取路径**。
/// </summary>
/// <remarks>
/// <list type="bullet">
///   <item><b>便携语义</b>：所有路径都在程序目录下（不引入 `%AppData%`）；</item>
///   <item><b>功能根</b>：`settings\` = 设置与外观主题；`modules\` = 模块包（内置 + 外部导入在 `modules\external\`）；
///     `composites\` = 用户组合文件；`workspace\` = 工作区快照与一次性重启恢复快照；`logs\` = 日志；</item>
///   <item><b>出厂内容只读</b>：`resources\seeds\` 与目标目录同构，只给投放器读；</item>
///   <item><b>不预先建目录</b>：写盘方按需建目录 ⇒ 这里只给路径，不产生副作用；</item>
///   <item><b>不做迁移</b>：旧位置的文件不搬家、不提示（开发期手工移动即可）。</item>
/// </list>
/// </remarks>
public static class StoragePaths
{
    /// <summary>模块包目录名（= `<程序目录>\modules`，内置包 + 外部导入包）。</summary>
    public const string ModulesDirectoryName = "modules";

    /// <summary>默认模块目录下的**外部导入包**子目录名（`modules\external`）。</summary>
    public const string ExternalModulesDirectoryName = "external";

    /// <summary>用户组合文件默认目录名（= `<程序目录>\composites`；对话框初值，**允许选别处**）。</summary>
    public const string CompositesDirectoryName = "composites";

    /// <summary>一个组合件包内的文档文件名。</summary>
    public const string CompositeDocumentFileName = "composite.json";

    /// <summary>一个组合件包内的封面文件名。</summary>
    public const string CompositeCoverFileName = "cover.png";

    /// <summary>一个组合件包内的资产子目录名（模块壁纸、控件图片等）。</summary>
    public const string CompositeAssetsDirectoryName = "assets";

    /// <summary>设置目录名（= `<程序目录>\settings`；设置文件与外观主题都在其下）。</summary>
    public const string SettingsDirectoryName = "settings";

    /// <summary>应用级设置文件名（`settings\settings.json`）。</summary>
    public const string SettingsFileName = "settings.json";

    /// <summary>外观目录名（`settings\appearance`）。</summary>
    public const string AppearanceDirectoryName = "appearance";

    /// <summary>主题目录名（`settings\appearance\themes`；每个主题一个文件夹，清单 + 壁纸同住）。</summary>
    public const string ThemesDirectoryName = "themes";

    /// <summary>工作区快照目录名（= `<程序目录>\workspace`；工作区快照与重启恢复快照都在其下）。</summary>
    public const string WorkspaceDirectoryName = "workspace";

    /// <summary>工作区快照文档名（`workspace\workspace.json`）。</summary>
    public const string WorkspaceFileName = "workspace.json";

    /// <summary>工作区资产子目录名（`workspace\assets`）。</summary>
    public const string WorkspaceAssetsDirectoryName = "assets";

    /// <summary>重启恢复快照文件名（`workspace\restart.json`；一次性文件）。</summary>
    public const string RestartFileName = "restart.json";

    /// <summary>日志目录名（= `<程序目录>\logs`；日志能力未落地，路径先在此登记）。</summary>
    public const string LogsDirectoryName = "logs";

    /// <summary>出厂资源目录名（= `<程序目录>\resources`；只读，只有投放器读）。</summary>
    public const string ResourcesDirectoryName = "resources";

    /// <summary>出厂种子子目录名（`resources\seeds`；与目标目录同构）。</summary>
    public const string SeedsDirectoryName = "seeds";

    /// <summary>文本包子目录名（`resources\i18n`；**就地读、只读**，每个拥有者一个子目录）。</summary>
    public const string TextPacksDirectoryName = "i18n";

    /// <summary>程序目录（= `AppContext.BaseDirectory`，exe 所在处）。</summary>
    public static string AppRoot => AppContext.BaseDirectory;

    /// <summary>模块包默认目录（内置包直接位于此；外部导入包在 <see cref="ExternalModulesRoot"/>）。</summary>
    public static string ModulesRoot => Path.Combine(AppRoot, ModulesDirectoryName);

    /// <summary>外部导入模块包目录（`modules\external`；来源标记 = 此路径）。</summary>
    public static string ExternalModulesRoot => Path.Combine(ModulesRoot, ExternalModulesDirectoryName);

    /// <summary>组合文件默认目录（`<程序目录>\composites`，与 `modules` 同级）。</summary>
    public static string CompositesRoot => Path.Combine(AppRoot, CompositesDirectoryName);

    /// <summary>设置根（`<程序目录>\settings`，与 `modules` / `composites` 同级）。</summary>
    public static string SettingsRoot => Path.Combine(AppRoot, SettingsDirectoryName);

    /// <summary>应用级设置文件（`settings\settings.json`）。</summary>
    public static string SettingsFile => Path.Combine(SettingsRoot, SettingsFileName);

    /// <summary>自定义主题根（`settings\appearance\themes`；每个主题一个文件夹）。</summary>
    public static string ThemesRoot => Path.Combine(SettingsRoot, AppearanceDirectoryName, ThemesDirectoryName);

    /// <summary>工作区快照根（`<程序目录>\workspace`，与 `modules` / `composites` 同级）。</summary>
    public static string WorkspaceRoot => Path.Combine(AppRoot, WorkspaceDirectoryName);

    /// <summary>工作区快照文档（`workspace\workspace.json`）。</summary>
    public static string WorkspaceFile => Path.Combine(WorkspaceRoot, WorkspaceFileName);

    /// <summary>工作区资产根（`workspace\assets`）。</summary>
    public static string WorkspaceAssetsRoot => Path.Combine(WorkspaceRoot, WorkspaceAssetsDirectoryName);

    /// <summary>重启恢复快照（`workspace\restart.json`；一次性文件）。</summary>
    public static string RestartFile => Path.Combine(WorkspaceRoot, RestartFileName);

    /// <summary>日志目录（`<程序目录>\logs`）。</summary>
    public static string LogsRoot => Path.Combine(AppRoot, LogsDirectoryName);

    /// <summary>出厂资源根（`<程序目录>\resources`）。</summary>
    public static string ResourcesRoot => Path.Combine(AppRoot, ResourcesDirectoryName);

    /// <summary>出厂种子根（`resources\seeds`；与目标目录同构，只读）。</summary>
    public static string SeedsRoot => Path.Combine(ResourcesRoot, SeedsDirectoryName);

    /// <summary>文本包根（`resources\i18n`；就地读，是宿主编排 `TextCatalog.UseRoot` 的唯一来源）。</summary>
    public static string TextPacksRoot => Path.Combine(ResourcesRoot, TextPacksDirectoryName);
}
