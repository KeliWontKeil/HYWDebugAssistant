namespace HYWDebugAssistant.Core.Api;

/// <summary>口引用（命令参数用）。</summary>
/// <param name="InstanceId">实例 ID。</param>
/// <param name="PortName">口名。</param>
public readonly record struct PortRef(string InstanceId, string PortName);

/// <summary>命令结果。</summary>
/// <param name="Succeeded">是否成功（false = 整体拒绝、零副作用）。</param>
/// <param name="Failures">拒绝原因。</param>
/// <param name="Notices">提示（如自动建网的歧义提示）。</param>
public readonly record struct CommandResult(
    bool Succeeded,
    IReadOnlyList<string> Failures,
    IReadOnlyList<string> Notices)
{
    /// <summary>成功。</summary>
    public static CommandResult Ok(IReadOnlyList<string>? notices = null) => new(true, [], notices ?? []);

    /// <summary>失败。</summary>
    public static CommandResult Fail(IReadOnlyList<string> failures) => new(false, failures, []);
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

    /// <summary>最近的问题（最新在前）。</summary>
    IReadOnlyList<IssueView> GetIssues(int maxCount);

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
}

/// <summary>
/// 命令面：UI 触发变更的**唯一通道**（一切变更经变更入口，Q1-4/C4-4）。
/// </summary>
/// <remarks>实现位于 L4；每个方法对应一次 <c>ChangeSet</c>（原子应用或整体拒绝）。</remarks>
public interface IRuntimeCommand
{
    /// <summary>加载并注册一个模块包（显式加载）。</summary>
    CommandResult InstallModulePackage(string packageDirectory);

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

    /// <summary>建立网络（<paramref name="name"/> 为空 = 匿名直连）。</summary>
    /// <param name="source">源端口。</param>
    /// <param name="targets">宿端口（1:N）。</param>
    /// <param name="name">网络名（空 = 匿名）。</param>
    /// <param name="allowUnverifiedTypeMatch">
    /// 是否已**显式确认**允许「**数据类型标识不同**」的强连（默认 <see langword="false"/> = 拒绝）。
    /// </param>
    CommandResult Connect(
        PortRef source,
        IReadOnlyList<PortRef> targets,
        string? name = null,
        bool allowUnverifiedTypeMatch = false);

    /// <summary>断开命名网络（匿名网络随实例移除级联清理）。</summary>
    CommandResult Disconnect(string networkName);

    /// <summary>更新实例配置。</summary>
    CommandResult UpdateConfiguration(string instanceId, object configuration);

    /// <summary>更新应用级设置（校验 → **即时生效** → 原子写盘）。</summary>
    CommandResult UpdateSettings(ApplicationSettings settings);

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
    /// 加载组合件：实例化进**当前工作区**（追加，不清空现场）。
    /// 异常加载为**降级式**：缺失定义 → 占位 + Issue；口消失 → 悬空成员 + Issue；
    /// 不可解析 / 版本过新 → **拒绝并保持原文件不变**（§9.3）。
    /// </summary>
    CommandResult LoadComposite(string filePath);

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
