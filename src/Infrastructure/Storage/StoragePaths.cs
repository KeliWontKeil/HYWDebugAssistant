namespace HYWDebugAssistant.Infrastructure.Storage;

/// <summary>
/// **分层保存的唯一路径来源**：
/// 程序层（只读，构建产物）与应用数据层（用户数据）**分成两处**，任何要落盘的地方**只从这里取路径**。
/// </summary>
/// <remarks>
/// <list type="bullet">
///   <item><b>便携语义</b>：用户数据**只放程序目录**（不引入 `%AppData%`），**所有需要持久化的数据全部进 `data`**；</item>
///   <item><b>两层的界线</b>：`modules\` 是**构建产物**（随包分发，勿手工改）；**其它一切需要持久化的东西**都进 `data\`；</item>
///   <item><b>为什么要收敛到一处</b>：路径散落在各调用点（`CompositionRoot` / `RuntimeServices` / `SettingsService` /
///     5 个读取点…）⇒ 「用户数据在哪」没有单一答案，包根会出现互相矛盾的说法；
///   <item><b>不预先建目录</b>：写盘方（`JsonFileStore`）会按需建目录 ⇒ 这里**只给路径**，不产生副作用
///     （避免"装起来就多一堆空目录"）；</item>
///   <item><b>不管临时文件</b>：`%TEMP%` 下的临时产物属临时层，**不在这里**。</item>
/// </list>
/// <para>
/// **本类不做任何版本/迁移**：未设计兼容性的改动一律不考虑兼容性 ⇒
/// 旧的 `exe\settings.json` **不搬家、不提示**（重设即可）。
/// </para>
/// </remarks>
public static class StoragePaths
{
    /// <summary>程序层：内置模块包目录名（= `<程序目录>\modules`，**构建产物**）。</summary>
    public const string ModulesDirectoryName = "modules";

    /// <summary>应用数据层目录名（= `<程序目录>\data`，**全部用户数据**）。</summary>
    public const string DataDirectoryName = "data";

    /// <summary>组合件默认目录名（`data\composites`；对话框初值，**允许选别处**）。</summary>
    public const string CompositesDirectoryName = "composites";

    /// <summary>日志目录名（`data\logs`；日志能力未落地，路径先在此登记）。</summary>
    public const string LogsDirectoryName = "logs";

    /// <summary>应用级设置文件名（`data\settings.json`）。</summary>
    public const string SettingsFileName = "settings.json";

    /// <summary>程序目录（= `AppContext.BaseDirectory`，exe 所在处）。</summary>
    public static string AppRoot => AppContext.BaseDirectory;

    /// <summary>内置模块包目录（程序层，只读：**不要手工往里放东西**）。</summary>
    public static string ModulesRoot => Path.Combine(AppRoot, ModulesDirectoryName);

    /// <summary>应用数据根（用户数据全在这里；备份/搬走 = 拷这一个目录）。</summary>
    public static string DataRoot => Path.Combine(AppRoot, DataDirectoryName);

    /// <summary>应用级设置文件（`data\settings.json`）。</summary>
    public static string SettingsFile => Path.Combine(DataRoot, SettingsFileName);

    /// <summary>组合件默认目录（`data\composites`）。</summary>
    public static string CompositesRoot => Path.Combine(DataRoot, CompositesDirectoryName);

    /// <summary>日志目录（`data\logs`）。</summary>
    public static string LogsRoot => Path.Combine(DataRoot, LogsDirectoryName);
}
