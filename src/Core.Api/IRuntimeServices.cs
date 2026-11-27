using HYWDebugAssistant.Sdk.Ports;
using HYWDebugAssistant.Sdk.Values;

namespace HYWDebugAssistant.Core.Api;

/// <summary>口引用（命令参数用）。</summary>
/// <param name="InstanceId">实例 ID。</param>
/// <param name="PortName">口名。</param>
public readonly record struct PortRef(string InstanceId, string PortName);

/// <summary>命令结果。</summary>
/// <param name="Succeeded">是否成功（false = 整体拒绝、零副作用）。</param>
/// <param name="Failures">拒绝原因。</param>
/// <param name="Notices">提示（如自动建网的歧义提示）。</param>
/// <param name="SkippedCount">因用户选择「跳过」而没有落地的项数（导入命令用；其余命令恒 0）。</param>
public readonly record struct CommandResult(
    bool Succeeded,
    IReadOnlyList<string> Failures,
    IReadOnlyList<string> Notices,
    int SkippedCount = 0)
{
    /// <summary>成功。</summary>
    public static CommandResult Ok(IReadOnlyList<string>? notices = null) => new(true, [], notices ?? []);

    /// <summary>失败。</summary>
    public static CommandResult Fail(IReadOnlyList<string> failures) => new(false, failures, []);
}

/// <summary>一个外部路径可能被导入成的形态。</summary>
/// <remarks>
/// 判定的深浅口径只有一处：批量扫描器的深度（<c>FolderImportScanner.MaxDepth</c>）。
/// 分类、冲突查询与执行都读同一份扫描结果，因此"能列出"与"能导入"永远指同一批包。
/// </remarks>
public enum ImportPathKind
{
    /// <summary>不认识（不导入）。</summary>
    None = 0,

    /// <summary>单个模块包（目录自身或直接子目录含 <c>module.json</c>）。</summary>
    ModulePackage = 1,

    /// <summary>单个组合包（目录含 <c>composite.json</c>，或路径就是该文件）。</summary>
    CompositePackage = 2,

    /// <summary>
    /// **外层文件夹**：自身不是单个包，但扫描深度内能找到包（模块包或组合包，可混合）。
    /// </summary>
    Folder = 3,
}

/// <summary>
/// **本次导入要处理的包类别**：让"问哪些冲突"与"导哪些包"用同一个范围。
/// </summary>
/// <remarks>
/// 组合页只导组合包、模块清单页只导模块包、拖入外层文件夹两者都导；
/// 若冲突查询不跟着分范围，用户就会被问到本次根本不会导入的项。
/// </remarks>
public enum ImportScope
{
    /// <summary>模块包与组合包（拖入外层文件夹 / 整文件夹导入）。</summary>
    All = 0,

    /// <summary>只有模块包。</summary>
    Modules = 1,

    /// <summary>只有组合包。</summary>
    Composites = 2,
}

/// <summary>
/// **同名冲突的处置**（对应资源管理器撞名时的「替换 / 跳过」）。
/// </summary>
/// <remarks>
/// 决定表里**没有这个键**才是"没人回答"（该冲突按失败处理），不是 <see cref="Skip"/>：
/// 静默跳过会让用户以为导入成功。执行侧据此区分"用户选了跳过"与"没人回答"。
/// </remarks>
public enum ImportConflictChoice
{
    /// <summary>跳过这一项（默认目录里的旧内容保持原样）。</summary>
    Skip = 0,

    /// <summary>替换（旧内容删除，写入新内容；不保留副本）。</summary>
    Replace = 1,
}

/// <summary>
/// 查询面：UI 读取框架状态的**唯一通道**（Q1-4/C4-4）。
/// </summary>
/// <remarks>实现位于 L4；UI 只引用本程序集（<c>Core.Api</c>）。</remarks>
public interface IRuntimeQuery
{
    /// <summary>当前全部模块实例。</summary>
    IReadOnlyList<ModuleInstanceView> GetInstances();

    /// <summary>取单个实例视图。</summary>
    bool TryGetInstance(string instanceId, out ModuleInstanceView? view);

    /// <summary>当前全部网络（含匿名与停流的）。</summary>
    IReadOnlyList<NetworkView> GetNetworks();

    /// <summary>
    /// 绑定诊断（L4）：计数 + **三类丢弃** + **四档速率**（框架口径；UI 不得自行差分估算）。
    /// </summary>
    IReadOnlyList<BindingDiagnosticView> GetDiagnostics();

    /// <summary>
    /// **每 (实例, 口) 队列**的深度与吞吐（水位 + 按宿计的搬运字节速率）。
    /// </summary>
    IReadOnlyList<PortQueueDepthView> GetQueueDepths();

    /// <summary>
    /// **按端口取指标**：一个端口的指标的**唯一来源**。
    /// </summary>
    /// <remarks>
    /// <list type="bullet">
    ///   <item><b>为什么要有它</b>：按「两次字典查找 + 猜键」取数会出错（先查网络身份键、再拿口字面量查逐源表），
    ///     而逐源表的键是**源**键 ⇒ **宿口永远匹配不上** ⇒ 宿口标记的投递/丢弃/速率**静默为 0**。
    ///     本查询把"一个端口该读哪些流"这件事**放回框架**（它才知道网络成员关系），界面只格式化；</item>
    ///   <item><b>口径</b>：发送口 = 以它为源的全部绑定之和；接收口 = 以它为宿的全部绑定之和；
    ///     **不在任何网络上 ⇒ 全 0**。完整定义与"三类丢弃不混算"见 <see cref="PortMetricsView"/>；</item>
    ///   <item>速率与角色方块**同一口径**（同一批框架速率计），故同一根线两侧不会再显示不同的数。</item>
    /// </list>
    /// </remarks>
    /// <param name="instanceId">实例 ID。</param>
    /// <param name="portName">口名。</param>
    /// <returns>该端口的指标（**永不返回 null**；未入网 / 实例或口不存在 ⇒ 全 0）。</returns>
    PortMetricsView GetPortMetrics(string instanceId, string portName);

    /// <summary>
    /// **一个接收口的队列流动状态**（深度 / 峰值 / 丢弃账本 / 饱和）——诊断与模块状态行读它。
    /// </summary>
    /// <param name="instanceId">实例 ID。</param>
    /// <param name="portName">接收口名。</param>
    /// <returns>
    /// 该实例的该口有队列时给出真实状态；未知实例 / 未知口 / 发送口 ⇒ <see cref="PortFlowState.None"/>（**不抛**）。
    /// </returns>
    PortFlowState GetPortFlowState(string instanceId, string portName);

    /// <summary>
    /// **全部磁盘主题**（`settings\appearance\themes\&lt;id&gt;\theme.json`；内置 `light` / `dark` 不在这里）。
    /// </summary>
    /// <remarks>
    /// <list type="bullet">
    ///   <item><b>顺序稳定</b>（按 id 序），设置页与主题目录拿到的是同一份；</item>
    ///   <item><b>坏清单不返回</b>（版本不认识 / 字段非法 / 壁纸名越界）：框架记一条 Warning，该主题不进目录；</item>
    ///   <item><b>只是元数据、不含图片字节</b>：壁纸按需经 <see cref="TryGetThemeWallpaper"/> 读取。</item>
    /// </list>
    /// </remarks>
    IReadOnlyList<ThemeFileView> GetThemeFiles();

    /// <summary>
    /// 读取某个磁盘主题的壁纸（与清单同目录；文件名由清单的 `wallpaper` 字段给出）。
    /// </summary>
    /// <remarks>内置主题没有壁纸 ⇒ false；无壁纸 / 文件缺失 / 超大小上限 ⇒ false（主题仍可用）。</remarks>
    /// <param name="themeId">主题 id（**不是**文件路径或资源引用）。</param>
    /// <param name="png">PNG 字节（失败为 null）。</param>
    bool TryGetThemeWallpaper(string themeId, out byte[]? png);

    /// <summary>某模块实例的壁纸（无 ⇒ null）。</summary>
    /// <param name="instanceId">实例 ID。</param>
    ModuleWallpaperView? GetModuleWallpaper(string instanceId);

    /// <summary>某模块控件的持久化图片（无 ⇒ null）。</summary>
    /// <param name="instanceId">实例 ID。</param>
    ModuleViewImageAssetView? GetModuleViewImage(string instanceId);

    /// <summary>某模块控件图片的版本（无图 ⇒ 0；不复制字节）。</summary>
    /// <param name="instanceId">实例 ID。</param>
    long GetModuleViewImageRevision(string instanceId);

    /// <summary>本次启动从工作区恢复的外壳绝对矩形；没有工作区 ⇒ null。</summary>
    WorkspacePlacementView? GetWorkspacePlacement();

    /// <summary>最近的问题（最新在前）。</summary>
    IReadOnlyList<IssueView> GetIssues(int maxCount);

    /// <summary>Issue 全量汇总（总数 + 按严重度计数；不受面板显示上限影响）。</summary>
    IssueSummaryView GetIssueSummary();

    /// <summary>模块目录（已注册的可用模块）。</summary>
    IReadOnlyList<ModuleCatalogEntryView> GetModuleCatalog();

    /// <summary>
    /// **预览建网**：按「接触且对齐」规则算出**将会建立**的连接（不产生任何副作用）。
    /// </summary>
    /// <remarks>
    /// 供 UI 在**拖动过程中**高亮"即将自动建网"的端口标记——判定规则与真正建网**同一份实现**（L4），
    /// 因此"预览会连上"与"松手后真的连上"永远一致（吸附是 UI 行为，不参与判定）。
    /// </remarks>
    /// <param name="provisionalLayouts">拖动中的临时布局（为空 = 用当前现场布局）。</param>
    IReadOnlyList<ConnectionCandidateView> PreviewConnections(IReadOnlyList<ModuleLayoutView>? provisionalLayouts = null);

    /// <summary>
    /// **同网络名但契约不兼容的端口对**（只读查询，不产生副作用）。
    /// </summary>
    /// <remarks>
    /// <list type="bullet">
    ///   <item><b>只回答“为什么同名却没连上”</b>：两端都打了同一个标签、方向为发送↔接收，
    ///     但 <c>ContractCompatibility.Check</c> 判为不兼容；</item>
    ///   <item><b>不参与建网</b>：它只是呈现面的事实，实际网络仍由 <see cref="GetNetworks"/> 给出；</item>
    ///   <item><b>只读、可缓存</b>：按现场与布局修订号缓存；改标签/改布局后自动失效。</item>
    /// </list>
    /// </remarks>
    IReadOnlyList<PortContractConflictView> GetPortContractConflicts();

    /// <summary>
    /// **拖动网络标签时的「连接对齐」建议**：把某个口拖到指定**边 + 偏移**时，
    /// 若它与某个**面对面**的口已满足"接触且对齐"的建网条件（只差对齐），返回**应对齐到的偏移**。
    /// </summary>
    /// <remarks>
    /// <list type="bullet">
    ///   <item>判定复用 <c>DockRule</c>（与真正建网**同一份实现**）⇒ **"能吸上" ⟺ "松手真的连上"**；</item>
    ///   <item>返回 <see langword="null"/> = 没有这样的对端口（不吸附、不高亮）；</item>
    ///   <item><b>不改现场、不落盘、不触发重算</b>：纯查询（端口几何用传入的预览值，不进布局）。</item>
    /// </list>
    /// </remarks>
    /// <param name="instanceId">本口所属实例。</param>
    /// <param name="portName">本口名。</param>
    /// <param name="edge">本口**当前**所在边（拖动中可能已换边）。</param>
    /// <param name="offset">本口**当前**的归一化偏移（指针算出来的预览值）。</param>
    /// <returns>建议（含对端口与应对齐到的偏移）；无则 <see langword="null"/>。</returns>
    AnchorDockHintView? SuggestAnchorDock(string instanceId, string portName, Sdk.Ports.PortEdge edge, double offset);

    /// <summary>
    /// **模块来源根的扫描结果**：上次扫描（启动或「立即重扫」）时每个根的
    /// 路径 · 是否存在 · 扫到/跳过/失败的包数 · 逐包结果。
    /// </summary>
    IReadOnlyList<ModuleSourceView> GetModuleSources();

    /// <summary>读取应用级设置的当前值（返回副本，修改它不影响生效值）。</summary>
    ApplicationSettings GetSettings();

    /// <summary>
    /// **存储位置**（只读）：组合文件默认目录、框架设置/重启快照/日志位置。
    /// </summary>
    /// <remarks>UI 只用于文件选择器的初始目录与设置页的只读展示；路径本身仍由路径解析器唯一给出。</remarks>
    StorageLocationsView GetStorageLocations();

    /// <summary>默认组合目录中的文件（供“加载组合”列表；按修改时间倒序）。</summary>
    IReadOnlyList<CompositeFileView> GetCompositeFiles();

    /// <summary>把一个外部路径分类为可导入的包类型（只读，不修改任何文件）。</summary>
    /// <param name="path">外部文件或文件夹路径。</param>
    ImportPathKind ClassifyImportPath(string path);

    /// <summary>
    /// **列出这个外部路径导入时会撞名的项**（只读，不修改任何文件）。
    /// </summary>
    /// <remarks>
    /// <list type="bullet">
    ///   <item>顺序 = 实际导入顺序（对话框逐条询问时按它走）；</item>
    ///   <item>撞名**只按名字判定**，不比内容：名字已占用就是冲突，与内容是否相同无关；</item>
    ///   <item>模块包的同名目录若属于**已加载**的包，也不算冲突 —— 那是只能失败的情形，
    ///     导入时返回 <c>core.services.import.moduleDirectoryOccupied</c>；</item>
    ///   <item>外层文件夹按批量导入同一套扫描规则取冲突（含只装组合包的文件夹）；</item>
    ///   <item>**只列本次真会导入的类别**（见 <paramref name="scope"/>）：问的与做的必须是同一批包，
    ///     否则用户会被问到根本不会导入的项；</item>
    ///   <item>不认识或没有包 ⇒ 空列表（不报错，交给导入本身给原因）。</item>
    /// </list>
    /// </remarks>
    /// <param name="sourcePath">外部包目录、外层文件夹，或组合文件路径。</param>
    /// <param name="scope">本次导入要处理的包类别（缺省 = 模块包与组合包都算）。</param>
    IReadOnlyList<ImportConflictView> GetImportConflicts(
        string sourcePath,
        ImportScope scope = ImportScope.All);

    /// <summary>模块视图布局（位置/尺寸/视图开关）；**未摆放过的实例**不在结果中（UI 自行取默认位）。</summary>
    IReadOnlyList<ModuleLayoutView> GetLayout();

    /// <summary>订阅数据源清单（UI 交接面：按声明数据源读缓冲，§5.8）。</summary>
    IReadOnlyList<SubscriptionSourceView> GetSubscriptionSources();

    /// <summary>
    /// 某实例的**角色**（功能分块）清单：一种数据类型 + 其输入口/输出口 + **`m/n`**（连了几个角色）。
    /// </summary>
    /// <remarks>
    /// <list type="bullet">
    ///   <item><b>只读、只用于呈现</b>：不参与建网、兼容、停流、投递与计量；</item>
    ///   <item>顺序 = 模块**声明顺序**（稳定，便于 UI 逐块摆放）；</item>
    ///   <item>**未声明角色 ⇒ 空列表**（叠加层不画方块；**运行期不报**，由 SDK 自检给一条建议 —— B5 裁定）；</item>
    ///   <item>`m/n` 由框架从网络成员关系派生（**不是模块自报**）：无该方向的口 ⇒ null，有口未连 ⇒ 0。</item>
    /// </list>
    /// </remarks>
    /// <param name="instanceId">实例 ID（不存在 / 占位实例 ⇒ 空列表）。</param>
    IReadOnlyList<RoleView> GetRoles(string instanceId);

    /// <summary>
    /// 某实例各元素**运行时被设定的外观**（颜色）。
    /// </summary>
    /// <remarks>
    /// <list type="bullet">
    ///   <item><b>它回答什么</b>：模块经 <c>IModuleContext.Appearance</c> 设的边框色 / 文字色 ——
    ///     用于把某元素画成模块要的样子；</item>
    ///   <item><b>它不含语义</b>：只有颜色，**没有**"成功 / 失败"这种判断。颜色与它何时变都是模块的 C# 逻辑
    ///     （框架不认识"红 = 失败"），因为把"失败必须长什么样"写进框架就是把功能限定死；</item>
    ///   <item><b>为什么需要它</b>：模块不持 UI，而 `SubmitInput` 的 `CommandResult` 只反映"是否投递入队"，
    ///     **不反映模块处理结果** ⇒ 没有这条通道，"这一格现在不对"在界面上无处可见；</item>
    ///   <item><b>生命周期 = 模块自己设、自己清</b>：设 <see langword="null"/> 即回到声明 / 主题；
    ///     没设过就没有条目（返回空表）；</item>
    ///   <item><b>只读、只用于呈现</b>：不参与建网/兼容/停流/投递/计量，**不进组合件**。</item>
    /// </list>
    /// </remarks>
    /// <param name="instanceId">实例 ID（不存在 / 占位实例 ⇒ 空列表）。</param>
    IReadOnlyList<AppearanceView> GetAppearance(string instanceId);

    /// <summary>某实例外观表的变更号（UI 据此跳过未变化的整表读取/角色解析）。</summary>
    /// <param name="instanceId">实例 ID。</param>
    /// <returns>变更号；实例不存在 ⇒ 0。</returns>
    long GetAppearanceRevision(string instanceId) => 0;

    /// <summary>
    /// 某实例参与的**角色间数据流**清单：已按（本端角色, 对端实例, 对端角色）**聚合**并给出口对数。
    /// </summary>
    /// <remarks>只读、只用于呈现；线两端锚点（输出侧/输入侧）与几何由 UI 决定。</remarks>
    /// <param name="instanceId">实例 ID（不存在 / 占位实例 ⇒ 空列表）。</param>
    IReadOnlyList<RoleFlowView> GetRoleFlows(string instanceId);

    /// <summary>
    /// 某实例**声明的内部处理顺序**（`角色 → 角色`）：**零声明 ⇒ 空**（框架不猜）。
    /// </summary>
    /// <remarks>只读、只用于呈现；不参与任何判定，也不进组合件。</remarks>
    /// <param name="instanceId">实例 ID（不存在 / 占位实例 ⇒ 空列表）。</param>
    IReadOnlyList<RoleOrderView> GetRoleOrder(string instanceId);

    /// <summary>
    /// 读**最新样本**（A1 数值 / A2 状态灯 / A7 仪表）。
    /// </summary>
    /// <param name="instanceId">实例 ID。</param>
    /// <param name="dataSourceName">数据源名。</param>
    /// <param name="destination">目标缓冲（需 ≥ 样本字节数）。</param>
    /// <param name="length">读到的字节数。</param>
    /// <returns>是否读到（无数据 / 写入抖动过大时为 false，**不等待**）。</returns>
    bool TryReadLatest(string instanceId, string dataSourceName, Span<byte> destination, out int length);

    /// <summary>
    /// 读**序列快照**（A3 曲线等；按时间升序，最多 <paramref name="maxSamples"/> 个）。
    /// </summary>
    /// <param name="instanceId">实例 ID。</param>
    /// <param name="dataSourceName">数据源名。</param>
    /// <param name="destination">目标缓冲（需 ≥ maxSamples × 样本字节数）。</param>
    /// <param name="maxSamples">最多读取的样本数（**抽样/降采样在 UI 侧**，§4.3）。</param>
    /// <returns>实际读到的样本数。</returns>
    int ReadSeries(string instanceId, string dataSourceName, Span<byte> destination, int maxSamples);

    /// <summary>
    /// 读实例**配置**的 JSON 文本（键 = 配置类型完整名 → 配置对象）——
    /// 供 `FrameworkUi` 的模块管理视图做**无 UI 模块的配置兜底**（§10.4/§5.6）。
    /// </summary>
    /// <param name="instanceId">实例 ID。</param>
    /// <param name="json">配置 JSON（无配置时为 <c>{}</c>）。</param>
    bool TryGetConfigurationJson(string instanceId, out string? json);

    /// <summary>
    /// 读实例配置的 **Schema + 当前值**（Q5-5）——模块视图内的配置编辑（B1 元素）与
    /// 无 UI 模块的管理兜底**共用同一份字段定义**（`11` §4.2）。
    /// </summary>
    /// <remarks>字段的**显示标题**已由模块标注或字段名生成，界面**不得**呈现类型完整名等代码化标识。</remarks>
    /// <param name="instanceId">实例 ID。</param>
    /// <param name="schema">Schema（含 <c>Notice</c>：不可编辑时的原因）。</param>
    /// <returns>实例存在时为 true（占位实例返回空字段 + 原因）。</returns>
    bool TryGetConfigurationSchema(string instanceId, out ConfigurationSchemaView? schema);

    /// <summary>取某个值的声明（未声明 ⇒ false）。</summary>
    /// <param name="instanceId">实例 ID。</param>
    /// <param name="name">值名。</param>
    /// <param name="declaration">声明。</param>
    bool TryGetValueDeclaration(string instanceId, string name, out ValueDeclaration? declaration)
    {
        declaration = null;
        return false;
    }

    /// <summary>取某个值当前的有效性结论（模块/框架最近一次报告；没有 ⇒ false）。</summary>
    /// <param name="instanceId">实例 ID。</param>
    /// <param name="name">值名。</param>
    /// <param name="validity">结论。</param>
    bool TryGetValueValidity(string instanceId, string name, out ValueValidity validity)
    {
        validity = ValueValidity.Valid;
        return false;
    }
}

/// <summary>
/// 命令面：UI 触发变更的**唯一通道**（一切变更经变更入口，Q1-4/C4-4）。
/// </summary>
/// <remarks>实现位于 L4；每个方法对应一次 <c>ChangeSet</c>（原子应用或整体拒绝）。</remarks>
public interface IRuntimeCommand
{
    /// <summary>加载并注册一个模块包（显式加载）。</summary>
    CommandResult InstallModulePackage(string packageDirectory);

    /// <summary>导入外部模块目录（扫描目录自身或 2–3 层内的模块包）。</summary>
    /// <param name="sourceDirectory">外部包目录或包含多个包目录的上级目录。</param>
    /// <param name="conflicts">
    /// 同名冲突的处置（键 = <see cref="ImportConflictView.SourcePath"/>）；缺省 = 没有冲突。
    /// 冲突项**没有决定**时该包按失败处理，绝不静默改名或覆盖。
    /// </param>
    CommandResult ImportModuleDirectory(
        string sourceDirectory,
        IReadOnlyDictionary<string, ImportConflictChoice>? conflicts = null);

    /// <summary>
    /// **导入一个外层文件夹**：向下最多 3 层扫描模块包与组合包，逐个导入并汇总结果。
    /// </summary>
    /// <remarks>
    /// 包边界优先组合包（含 <c>composite.json</c> 即不再下钻），其次模块包（含 <c>module.json</c>）。
    /// 单个包失败不中断其余；隐藏/系统/重解析点目录跳过，超过上限即停止并报告。
    /// 本入口两类包都导（拖入外层文件夹就是"把这里面的东西都导进来"）；
    /// 只导一类的入口是 <see cref="ImportModuleDirectory"/> 与 <see cref="ImportComposite"/>。
    /// </remarks>
    /// <param name="sourceDirectory">外层文件夹。</param>
    /// <param name="conflicts">
    /// 同名冲突的处置（键 = <see cref="ImportConflictView.SourcePath"/>）；缺省 = 没有冲突。
    /// </param>
    FolderImportResult ImportFolder(
        string sourceDirectory,
        IReadOnlyDictionary<string, ImportConflictChoice>? conflicts = null);

    /// <summary>
    /// **重扫模块来源根**：按设置里的根列表（内置目录在首位）重新扫描，
    /// 装入**新增**的模块包并刷新诊断结果。
    /// </summary>
    /// <remarks>
    /// 只发现**新增**的包：改过代码的包因程序集已载入且不可卸载，**必须重启应用**才生效
    /// （框架不实现热重载；对"本次启动后重新构建过"的包，目录信息里会给出需重启提示）。
    /// </remarks>
    CommandResult RescanModuleSources();

    /// <summary>加入模块实例（按模块标识字面量解析定义）。</summary>
    CommandResult AddModule(string instanceId, string moduleIdLiteral);

    /// <summary>移除模块实例（级联清理其连接与绑定）。</summary>
    CommandResult RemoveModule(string instanceId);

    /// <summary>启用 / 禁用实例（保留实例、拓扑与配置，只停流）。</summary>
    CommandResult SetModuleEnabled(string instanceId, bool enabled);

    /// <summary>更新实例配置。</summary>
    CommandResult UpdateConfiguration(string instanceId, object configuration);

    /// <summary>更新应用级设置（校验 → **即时生效** → 原子写盘）。</summary>
    CommandResult UpdateSettings(ApplicationSettings settings);

    /// <summary>
    /// **写一个主题文件夹**（`settings\appearance\themes\&lt;id&gt;\`）：清单原子写 + 可选壁纸字节。
    /// </summary>
    /// <remarks>
    /// <list type="bullet">
    ///   <item>id 过规范名收敛并拒绝保留名（`system` / `light` / `dark`）；</item>
    ///   <item>清单里只有**稀疏**色板（用户改过的 token）；壁纸与清单同目录；</item>
    ///   <item>不修改设置文件：主题选择仍走 <see cref="UpdateSettings"/>（三键语义不变）。</item>
    /// </list>
    /// </remarks>
    /// <param name="request">清单字段 + 可选壁纸字节。</param>
    CommandResult SaveTheme(ThemeSaveRequest request);

    /// <summary>**删除一个主题文件夹**（含壁纸；不存在视为已删除）。</summary>
    /// <param name="themeId">主题 id。</param>
    CommandResult DeleteTheme(string themeId);

    /// <summary>设置/清除某模块实例的壁纸（null = 清除）；只影响该模块。</summary>
    CommandResult SetModuleWallpaper(string instanceId, byte[]? png);

    /// <summary>设置/清除某模块控件的持久化图片（null = 清除）；不影响模块壁纸。</summary>
    /// <param name="instanceId">实例 ID。</param>
    /// <param name="bytes">图片字节；null/空 = 清除。</param>
    /// <param name="crop">归一化裁切。</param>
    CommandResult SetModuleViewImage(string instanceId, byte[]? bytes, ViewImageCrop crop);

    /// <summary>严格持久化关闭行为（写盘失败即回滚并返回失败）。</summary>
    /// <param name="behavior"><c>Ask</c> / <c>Save</c> / <c>Discard</c>。</param>
    CommandResult SetWorkspaceExitBehavior(string behavior);

    /// <summary>
    /// **移除指定的 Issue**（按 <see cref="IssueView.Sequence"/>；用户删除单条）。
    /// </summary>
    /// <remarks>只影响 Issue 列表与计数，不影响模块、网络或配置；不存在的序号忽略。</remarks>
    /// <param name="sequences">要移除的序号集合。</param>
    CommandResult RemoveIssues(IReadOnlyCollection<long> sequences);

    /// <summary>
    /// **标记一批 Issue 为已读 / 未读**（"选中即已读"、"标记未读"用）。
    /// </summary>
    /// <remarks>
    /// 已读只影响未读标记与入口角标（角标 = 未读错误级条数），不影响运行；
    /// 不落盘，本次运行有效。不存在的序号忽略。
    /// </remarks>
    /// <param name="sequences">要改的序号集合。</param>
    /// <param name="read"><see langword="true"/> = 已读；<see langword="false"/> = 未读。</param>
    CommandResult MarkIssuesRead(IReadOnlyCollection<long> sequences, bool read);

    /// <summary>**全部标记为已读**（含当前未显示的那些）；角标随之清零。</summary>
    CommandResult MarkAllIssuesRead();

    /// <summary>**清空全部 Issue**（用户确认后的“清空全部”）；不影响运行中的模块。</summary>
    CommandResult ClearIssues();

    /// <summary>更新模块视图布局（位置/尺寸/视图开关；坐标非法即整体拒绝）。</summary>
    CommandResult UpdateModuleLayout(ModuleLayoutView layout);

    /// <summary>
    /// **批量**更新模块视图布局（一次 <c>ChangeSet</c>、原子应用或整体拒绝）——
    /// 拼接组整体拖动属一次用户动作，必须一次性生效（C5-1 约束 1）。
    /// </summary>
    CommandResult UpdateModuleLayouts(IReadOnlyList<ModuleLayoutView> layouts);

    /// <summary>更新端口几何（用户在边缘上重新摆放端口 / 锁定端口位置）。</summary>
    CommandResult UpdatePortGeometry(string instanceId, PortGeometryView geometry);

    /// <summary>
    /// 设置端口的**网络标签**（用户可见的**唯一建网机制**）——
    /// 同名标签的发送口与接收口自动连成一条命名网络；传空即清除标签（退出该网络）。
    /// </summary>
    /// <param name="instanceId">实例 ID。</param>
    /// <param name="portName">口名。</param>
    /// <param name="networkName">网络标签（空 = 清除标签）。</param>
    CommandResult UpdatePortNetwork(string instanceId, string portName, string? networkName);

    /// <summary>
    /// 投递一个**模块命令**（用户点了模块视图里的命令按钮）。
    /// </summary>
    /// <remarks>
    /// 语义：**投递**而非"执行完成"——命令进入该实例的人机操作收件箱，由模块在**自身线程**上消费。
    /// 未声明的命令在变更校验阶段被拒绝（失败原因带回，不静默）。
    /// </remarks>
    /// <param name="instanceId">实例 ID。</param>
    /// <param name="commandName">命令名（视图声明中登记）。</param>
    CommandResult SendCommand(string instanceId, string commandName);

    /// <summary>
    /// 写入一个**文本输入值**（用户在输入元素上回车/失焦提交）。
    /// </summary>
    /// <remarks>与命令同样只**入队**；文本由模块在自己的线程上解析（非法值由模块上报 Issue）。</remarks>
    /// <param name="instanceId">实例 ID。</param>
    /// <param name="source">输入元素绑定的数据源名。</param>
    /// <param name="text">用户输入文本。</param>
    CommandResult SubmitInput(string instanceId, string source, string text);

    /// <summary>
    /// **还原模块配置**到模块默认值（模块窗口的「还原」按钮）。
    /// </summary>
    /// <remarks>
    /// 丢弃该实例已保存的配置对象（模块下次读取时按"读即登记"重新登记默认值），并触发一次配置变更通知，
    /// 使模块在自己的线程上按默认配置继续工作。
    /// </remarks>
    /// <param name="instanceId">实例 ID。</param>
    CommandResult ResetModuleConfiguration(string instanceId);

    /// <summary>
    /// 保存组合件：把选中范围（<paramref name="instanceIds"/> 为空 = **当前全部**，即"保存全部内容"）
    /// 物化为 1 个文件（1 文件 = 1 组合件，§9.2）。
    /// </summary>
    /// <param name="filePath">目标文件路径（原子写）。</param>
    /// <param name="name">组合件名（空 = 取文件名）。</param>
    /// <param name="instanceIds">选中实例（含占位实例）；为空 = 全部。</param>
    CommandResult SaveComposite(string filePath, string? name = null, IReadOnlyList<string>? instanceIds = null);

    /// <summary>
    /// **导入外部组合**：源路径可以是**组合文件夹**（含 <c>composite.json</c>）或该文件本身；
    /// 检查可读后复制到默认目录。默认目录已有同名项时**一律**按 <paramref name="conflicts"/> 处置
    /// （不比内容：内容相同也要问，否则用户无法与"什么都没做"区分）。
    /// </summary>
    /// <param name="sourcePath">外部组合文件夹或组合文件路径。</param>
    /// <param name="conflicts">
    /// 同名冲突的处置（键 = <see cref="ImportConflictView.SourcePath"/>）；缺省 = 没有冲突。
    /// 冲突项**没有决定**时按失败返回，绝不静默改名、静默覆盖或静默复用。
    /// </param>
    CompositeImportResult ImportComposite(
        string sourcePath,
        IReadOnlyDictionary<string, ImportConflictChoice>? conflicts = null);

    /// <summary>
    /// **删除默认目录中的一个组合文件**（只删文件，不移除当前现场模块）。
    /// </summary>
    /// <param name="filePath">默认目录下的组合文件路径。</param>
    CommandResult DeleteCompositeFile(string filePath);

    /// <summary>
    /// **重命名默认目录中的组合文件**（只改文件名，不覆盖已有文件）。
    /// </summary>
    /// <param name="filePath">原文件路径。</param>
    /// <param name="newName">新文件名（可带/不带 .json；不允许路径分隔符）。</param>
    CompositeRenameResult RenameCompositeFile(string filePath, string newName);

    /// <summary>把外部图片复制为组合封面（旧扁平件为同名 .preview.png，包为 cover.png）。</summary>
    /// <param name="filePath">组合 JSON/文档路径。</param>
    /// <param name="imagePath">外部图片路径。</param>
    CommandResult SetCompositeCover(string filePath, string imagePath);

    /// <summary>用全局图片选择器裁切后的 PNG 字节设置组合封面。</summary>
    /// <param name="filePath">组合 JSON/文档路径。</param>
    /// <param name="png">PNG 字节。</param>
    CommandResult SetCompositeCoverPng(string filePath, byte[] png);

    /// <summary>
    /// 加载组合件：实例化进**当前工作区**（追加，不清空现场）。
    /// **加载 = 加载一批模块**：实例 ID 与现场冲突时只重映射冲突项，标签/网络名/配置/位置原样保留。
    /// 异常加载为**降级式**：缺失定义 → 占位 + Issue；口消失 → 悬空成员 + Issue；
    /// 不可解析 / 版本过新 → **拒绝并保持原文件不变**（§9.3）。
    /// </summary>
    CommandResult LoadComposite(string filePath);

    /// <summary>
    /// 保存**当前工作区快照**（`workspace\workspace.json`）：全部模块、配置、用户输入、
    /// 壁纸、布局与网络；下次启动在空现场恢复。
    /// </summary>
    CommandResult SaveWorkspace(WorkspacePlacementView placement);

    /// <summary>
    /// **保存一次性重启恢复快照**（`workspace\restart.json`）：当前全部模块的关系快照。
    /// </summary>
    /// <remarks>
    /// 旧入口，保留兼容；「重新加载」已改用 <see cref="SaveWorkspace"/>。
    /// </remarks>
    CommandResult SaveRestartSnapshot();

    /// <summary>删除一次性重启恢复快照（重启进程启动失败时回滚，避免下次启动误恢复）。</summary>
    CommandResult DeleteRestartSnapshot();

    /// <summary>
    /// 为占位实例手动指定替代模块（"可手动指定替代版本"，§9.3）：实例化该定义并**沿用占位保存的配置**，
    /// 随后尝试把之前悬空的网络成员重新锚定。
    /// </summary>
    CommandResult ResolvePlaceholder(string instanceId, string moduleIdLiteral);

    /// <summary>
    /// 写回实例**配置**（JSON 文本，键 = 配置类型完整名 → 配置对象）——
    /// 无 UI 模块的配置兜底路径；校验失败即**整体拒绝且零副作用**。
    /// </summary>
    /// <param name="instanceId">实例 ID。</param>
    /// <param name="json">配置 JSON。</param>
    CommandResult UpdateConfigurationJson(string instanceId, string json);

    /// <summary>
    /// 按**字段**写回配置（Q5-5 的 Schema 表单通道）——值按字段类型解析，
    /// 失败即**整体拒绝且零副作用**；经变更入口原子应用并触发一次配置变更通知。
    /// </summary>
    /// <param name="instanceId">实例 ID。</param>
    /// <param name="fieldName">字段名（来自 <see cref="ConfigurationFieldView.Name"/>）。</param>
    /// <param name="value">新值的文本形态（布尔 = true/false；枚举 = 成员名；数值 = 不变文化写法）。</param>
    CommandResult UpdateConfigurationField(string instanceId, string fieldName, string value);
}
