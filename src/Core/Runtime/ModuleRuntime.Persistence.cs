using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;
using HYWDebugAssistant.Core.Adapters;
using HYWDebugAssistant.Core.Api;
using HYWDebugAssistant.Core.Binding;
using HYWDebugAssistant.Core.Composition;
using HYWDebugAssistant.Core.Persistence;
using HYWDebugAssistant.Core.Views;
using HYWDebugAssistant.Core.Workspace;
using HYWDebugAssistant.Hosting.Instances;
using HYWDebugAssistant.Hosting.Registry;
using HYWDebugAssistant.Sdk.Ports;
using HYWDebugAssistant.Sdk.Reporting;

namespace HYWDebugAssistant.Core.Runtime;

/// <summary>
/// 运行时的**持久化面**：现场 ⇄ 组合件（捕获 / 降级式加载 / 重锚 / 占位解析）。
/// </summary>
/// <remarks>
/// <list type="bullet">
///   <item><b>捕获</b>（保存）：按选中范围物化实例 + 网络 + 布局 + 配置 + 端口几何；**不写运行态与绑定**（§9.2）；</item>
///   <item><b>加载</b>：**降级式两阶段**——解析分类（可锚定 / 缺失 / 悬空）→ 提交（无失败点）；</item>
///   <item><b>重锚</b>：按 <c>实例ID.口名</c> 重建成员；口消失 → 悬空成员（保留声明 + Issue）；</item>
///   <item><b>并发</b>：本文件的一切操作都在**变更闸门**内（与 <see cref="Apply"/> 同一把锁），
///     不在现场被并发修改的中途捕获半成品。</item>
/// </list>
/// </remarks>
public sealed partial class ModuleRuntime
{
    /// <summary>模块配置序列化参数（**反射式**：配置类型由模块定义、框架编译期不可知）。</summary>
    /// <remarks>
    /// 组合件**信封**不依赖反射（手工 JsonNode 映射）；只有模块自有的配置对象走反射式序列化。
    /// 这是 v1 的明确取舍：SDK 未要求模块提供源生成上下文（记入 M1-8 实现细节）。
    /// </remarks>
    private static JsonSerializerOptions ConfigurationJsonOptions => ConfigurationJson.Options;

    /// <summary>配置原文片段（键 = 实例 ID）：保存时把**当前配置值合并进它** → 未识别配置类型/字段不丢失（C3-3）。</summary>
    private readonly Dictionary<string, JsonObject> _configurationFragments = new(StringComparer.Ordinal);

    /// <summary>布局坐标/尺寸的绝对值上限（护栏：拒绝畸形数值，不做静默截断）。</summary>
    private const double MaxCoordinate = 1_000_000d;

    /// <summary>最近一次加载的组合件（片段来源：实例/网络记录层的未知字段靠它带回）。</summary>
    private Composite? _loadedSource;

    // ---------------------------------------------------------------- 捕获（保存）

    /// <summary>
    /// 捕获当前现场为组合件。
    /// </summary>
    /// <param name="name">组合件名（空 = 由调用方用文件名补齐）。</param>
    /// <param name="instanceIds">选中范围（<see langword="null"/> = **当前全部**，即"保存全部内容"）。</param>
    /// <param name="notices">捕获过程中的提示（选中项不存在、网络被排除等）。</param>
    public Composite CaptureComposite(string name, IReadOnlyList<string>? instanceIds, out IReadOnlyList<string> notices)
    {
        lock (_changeGate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);

            var noteList = new List<string>();
            var selection = BuildSelection(instanceIds, noteList);

            var instances = new List<CompositeInstanceRecord>();
            var layout = new List<ModuleLayoutView>();

            foreach (var instance in Content.Instances)
            {
                if (!selection.Contains(instance.InstanceId))
                {
                    continue;
                }

                instances.Add(CaptureInstance(instance));
                AddLayoutIfPresent(instance.InstanceId, layout);
            }

            // 占位实例也要保存：否则"加载缺失 + 再保存"会吃掉用户的配置与声明
            foreach (var placeholder in Content.Placeholders)
            {
                if (!selection.Contains(placeholder.InstanceId))
                {
                    continue;
                }

                instances.Add(new CompositeInstanceRecord
                {
                    InstanceId = placeholder.InstanceId,
                    ModuleId = placeholder.ModuleIdLiteral,
                    Enabled = placeholder.Enabled,
                    Configuration = placeholder.Configuration is null
                        ? null
                        : (JsonObject)placeholder.Configuration.DeepClone(),
                    Ports = Content.Layout.GetPorts(placeholder.InstanceId),
                    Fragment = FindLoadedInstance(placeholder.InstanceId)?.Fragment,
                });

                AddLayoutIfPresent(placeholder.InstanceId, layout);
            }

            var networks = new List<CompositeNetworkRecord>();
            foreach (var network in Content.Networks)
            {
                // **推导出来的匿名网络不落盘**：接触建网的事实是几何，加载后由几何重新推导（这才是它的来源）。
                // 落盘会让"移开后就该断的直连"变成文件里的僵尸连接。
                // 例外：**旧组合件里读进来的匿名声明**（IsFromFile）必须原样带走，否则一存一读就丢用户数据。
                if (!network.IsNamed && !network.IsFromFile)
                {
                    continue;
                }

                // 保存单位 = "一组实例 + 连接"：只保存**成员全在选中范围内**的网络（不做静默截断）
                if (!network.Members.All(m => selection.Contains(m.InstanceId)))
                {
                    noteList.Add(CoreText.Get("core.runtime.networkNotIncluded", Describe(network)));
                    continue;
                }

                networks.Add(CaptureNetwork(network));
            }

            notices = noteList;

            // 壁纸按实例收集：JSON 只写包内相对引用，字节放 Assets 由包存储落到 assets/。
            var assets = new Dictionary<string, byte[]>(StringComparer.Ordinal);
            var wallpapers = CaptureWallpapers();
            foreach (var record in instances)
            {
                if (wallpapers.TryGetValue(record.InstanceId, out var wallpaper))
                {
                    var hash = NormalizeRef(wallpaper.Ref);
                    record.Wallpaper = $"assets/{hash}.png";
                    assets[hash] = wallpaper.Png;
                }
            }

            return new Composite
            {

                Name = name,
                SavedAt = DateTimeOffset.Now,
                Instances = instances,
                Networks = networks,
                Layout = layout,
                Assets = assets,

                // 以上次加载的根片段为底稿 → 根层未识别字段原样带回
                RootFragment = _loadedSource?.RootFragment,
            };
        }
    }

    // ---------------------------------------------------------------- 加载（降级式）

    /// <summary>
    /// 把组合件**实例化进当前工作区**（追加，不清空现场）。
    /// </summary>
    /// <param name="composite">已解析的组合件。</param>
    /// <param name="resolveDefinition">按标识字面量解析模块定义（由 L4 服务注入注册表；失败时给出**面向用户的原因**）。</param>
    /// <remarks>
    /// <para>
    /// **加载 = 加载一批模块**：与单独加入模块同构。实例 ID 若与当前现场冲突（或文件内重复），
    /// **只重映射冲突项**，并同步改写网络成员、布局键；端口标签、网络名、配置与位置**原样保留**。
    /// 因此同一文件可以加载多次，同名标签仍按命名网络语义并入同一张网（与单独加模块一致）。
    /// </para>
    /// <para>
    /// **降级式**：单条记录失败不放弃整次加载（§9.3"损坏 → 尽量部分恢复"）——
    /// 缺失定义 → 占位；口消失 → 悬空成员；两者都记 Issue 且**不阻断其余部分运行**。
    /// **网络**：先按文件重锚（声明来自文件），再由**标签**重算命名网络（标签是建网事实源）；
    /// 文件里没有标签的声明网络保留，不会被推导删掉。
    /// </para>
    /// </remarks>
    internal CompositeLoadResult LoadComposite(Composite composite, ModuleDefinitionResolver resolveDefinition)
    {
        ArgumentNullException.ThrowIfNull(composite);
        ArgumentNullException.ThrowIfNull(resolveDefinition);

        lock (_changeGate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);

            var notices = new List<string>();
            composite = RemapConflictingInstanceIds(composite, notices);
            _loadedSource = composite;

            var instancesAdded = 0;
            var placeholdersCreated = 0;

            // ① 实例：解析 → 实例化 → **恢复配置** → 激活 → 活动面
            foreach (var record in composite.Instances)
            {
                if (Content.IsInstanceIdTaken(record.InstanceId))
                {
                    var conflict = CoreText.Get("core.runtime.load.instanceIdConflict", record.InstanceId);
                    notices.Add(conflict);
                    Issues.Report($"composite:{DescribeName(composite)}", new IssueReport(
                        IssueCategory.CompositionError, IssueSeverity.Warning, conflict));
                    continue;
                }

                if (!resolveDefinition(record.ModuleId, out var definition, out var reason) || definition is null)
                {
                    AddPlaceholder(record, reason ?? CoreText.Get("core.runtime.load.definitionMissing"));
                    placeholdersCreated++;
                    continue;
                }

                if (CreateLoadedInstance(record, definition, notices))
                {
                    instancesAdded++;
                }
                else
                {
                    placeholdersCreated++;
                }
            }

            // ② 网络：按 `实例ID.口名` 重锚（此时实例与口都已在场）
            var networksRestored = 0;
            var unresolvedMembers = 0;
            foreach (var record in composite.Networks)
            {
                var anchoring = AnchorNetwork(
                    record.Name,
                    record.Sources,
                    record.Targets,
                    record.Unresolved,
                    isFromFile: true,
                    notices);

                unresolvedMembers += anchoring.DanglingCount;
                AddOrMergeLoadedNetwork(anchoring.Network, notices);

                // 加载 = **首见**：源侧与宿侧问题全报
                ReportNetworkAnchoring(anchoring, notices, reportSources: true, reportDangling: true);
                networksRestored++;
            }

            // ③ 布局（**加载可忽略**：缺失/非法不影响运行，§9.2）
            ApplyLoadedLayout(composite);

            // ③′ 标签是事实源：按件中端口标签**重算命名网络**（文件中缺网络记录、或记录与标签不一致时，
            //     以标签为准；一致时本次重算是幂等的）
            ReconcileNetworks(notices, AlignAutoNetwork);

            // ④ 活动面收尾：禁用实例的绑定要置停流（网络在 ② 才建立，此处才能生效）
            foreach (var instance in Content.Instances)
            {
                if (instance.State == ModuleInstanceState.Disabled)
                {
                    SetSuspended(instance.InstanceId, suspended: true);
                }
            }

            return new CompositeLoadResult(instancesAdded, placeholdersCreated, networksRestored, unresolvedMembers, notices);
        }
    }

    /// <summary>
    /// **加载时的网络落位**：同名命名网络**合并成员**（同名 = 同一张网，与标签语义一致），
    /// 匿名网络按对象独立加入。合并后重建该网络的绑定。
    /// </summary>
    private void AddOrMergeLoadedNetwork(Network incoming, List<string> notices)
    {
        if (!incoming.IsNamed)
        {
            AddNetworkCore(incoming);
            return;
        }

        var existing = Content.Networks.FirstOrDefault(n =>
            n.IsNamed && string.Equals(n.Name, incoming.Name, StringComparison.Ordinal));

        if (existing is null)
        {
            AddNetworkCore(incoming);
            return;
        }

        var sources = existing.Sources.Concat(incoming.Sources).Distinct().ToList();
        var unresolvedSources = existing.UnresolvedSources.Concat(incoming.UnresolvedSources).Distinct().ToList();
        var targets = existing.Targets.Concat(incoming.Targets).Distinct().ToList();
        var unresolved = existing.UnresolvedMembers.Concat(incoming.UnresolvedMembers).Distinct().ToList();

        if (sources.Count == 0 && unresolvedSources.Count == 0)
        {
            return;
        }

        var merged = new Network(
            incoming.Name,
            sources,
            targets,
            unresolved,
            unresolvedSources,
            existing.IsFromFile || incoming.IsFromFile);

        RemoveNetworkCore(existing);
        AddNetworkCore(merged);
        notices.Add(CoreText.Get("core.runtime.load.networkMerged", incoming.Name));
    }

    /// <summary>
    /// 为占位实例指定替代模块（"可手动指定替代版本"，§9.3）：实例化 + 沿用配置 + **重锚悬空成员**。
    /// </summary>
    /// <param name="instanceId">占位实例 ID。</param>
    /// <param name="definition">替代模块定义。</param>
    /// <param name="notices">提示（重锚结果等）。</param>
    /// <param name="failure">失败原因（成功时为 <see langword="null"/>）。</param>
    public bool TryResolvePlaceholder(
        string instanceId,
        ModuleDefinition definition,
        out IReadOnlyList<string> notices,
        out string? failure)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(instanceId);
        ArgumentNullException.ThrowIfNull(definition);

        lock (_changeGate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);

            if (!Content.TryGetPlaceholder(instanceId, out var placeholder) || placeholder is null)
            {
                notices = [];
                failure = CoreText.Get("core.runtime.placeholderMissing", instanceId);
                return false;
            }

            if (!TryInstantiate(instanceId, definition, out var created, out failure))
            {
                notices = [];
                return false;
            }

            var noteList = new List<string>();

            // 沿用占位保留的配置（在 OnActivated 之前写入）
            RestoreConfiguration(created!, new CompositeInstanceRecord
            {
                InstanceId = instanceId,
                ModuleId = definition.Id.ToString(),
                Configuration = placeholder.Configuration,
            }, noteList);

            _ = Content.RemovePlaceholder(instanceId);
            ActivateInstance(created!, enabled: placeholder.Enabled);

            // 占位期间**没有口**，随件保存的口几何（位置/锁定/**网络标签**）无处安放 —— 此刻补上
            RestoreLoadedPortGeometry(instanceId, created!);

            ReanchorNetworks(noteList);
            ReconcileNetworks(noteList, AlignAutoNetwork);

            // 占位原为禁用 → 解析后仍是禁用：新建立的绑定同样要停流
            if (!placeholder.Enabled)
            {
                SetSuspended(instanceId, suspended: true);
            }

            notices = noteList;
            failure = null;
            return true;
        }
    }

    // ---------------------------------------------------------------- 实例化与配置

    private bool CreateLoadedInstance(CompositeInstanceRecord record, ModuleDefinition definition, List<string> notices)
    {
        if (!TryInstantiate(record.InstanceId, definition, out var instance, out var failure))
        {
            // 实例化失败也**降级为占位**（部分恢复：配置与网络成员不丢）
            AddPlaceholder(record, CoreText.Get("core.runtime.load.instantiateFailed", failure));
            return false;
        }

        RestoreViewState(record);
        RestoreWallpaper(record);
        RestoreConfiguration(instance!, record, notices);
        ActivateInstance(instance!, record.Enabled);
        return true;
    }

    /// <summary>把实例记录里的 viewState 恢复到共享视图状态存储（必须在激活之前）。</summary>
    private void RestoreViewState(CompositeInstanceRecord record)
    {
        if (record.ViewState is { IsEmpty: false } snapshot && ViewState is Services.ViewStateStore store)
        {
            store.Restore(record.InstanceId, snapshot);
        }
    }

    /// <summary>把实例记录里的壁纸引用恢复成内存资产（包内 assets/ 字节由 Composite.Assets 提供）。</summary>
    private void RestoreWallpaper(CompositeInstanceRecord record)
    {
        if (record.Wallpaper is not { Length: > 0 } reference)
        {
            return;
        }

        var hash = NormalizeRef(reference);
        if (hash.EndsWith(".png", StringComparison.OrdinalIgnoreCase))
        {
            hash = hash[..^4];
        }

        if (_loadedSource?.Assets.TryGetValue(hash, out var png) == true)
        {
            RestoreWallpaper(record.InstanceId, hash, png);
        }
    }

    private bool TryInstantiate(string instanceId, ModuleDefinition definition, out ModuleInstance? instance, out string? failure)
    {
        try
        {
            // 与 `AddModuleOperation` 同口径：视图状态**按实例绑定**交给模块（只读），文件选择面同理。
            instance = ModuleInstanceFactory.Create(
                instanceId,
                definition,
                Issues,
                ViewState?.ReaderFor(instanceId),
                (ViewState as Services.ViewStateStore)?.DisplayFor(instanceId),
                FileAccessFor?.Invoke(instanceId),
                catalog: _catalog);
            failure = null;
            return true;
        }
        catch (Exception ex) when (ex is MissingMethodException
                                      or MemberAccessException
                                      or System.Reflection.TargetInvocationException
                                      or TypeInitializationException
                                      or InvalidOperationException
                                      or ArgumentException
                                      or NotSupportedException)
        {
            instance = null;
            failure = $"{ex.GetType().Name} — {ex.Message}";
            return false;
        }
    }

    /// <summary>加入现场 + 启动承载 + 生命周期激活 + 应用活动面（**加载与占位解析共用**）。</summary>
    private void ActivateInstance(ModuleInstance instance, bool enabled)
    {
        var adapter = new ModuleCarrierAdapter(instance, instance.Context, Issues);
        Content.AddInstance(instance);
        _dataTypeLedger.Reconcile(Content.Instances, Issues);
        _adapters[instance.InstanceId] = adapter;
        _subscriptions[instance.InstanceId] = new SubscriptionSet(
            instance.InstanceId, instance.DisplaySources);
        instance.Context.AttachDataPlane(new RuntimePortAccess(this, instance.InstanceId, instance.Context));

        // 外部接入必须在承载启动前挂上（加载路径与"加入现场"同口径）。
        var runtimeAccess = AttachExternalAccess(instance);
        if (runtimeAccess is not null)
        {
            _accesses[instance.InstanceId] = runtimeAccess;
        }

        if (!Carriers.TryActivate(adapter, out _, out var failure))
        {
            Issues.Report($"module:{instance.InstanceId}", new IssueReport(
                IssueCategory.RuntimeError,
                IssueSeverity.Error,
                CoreText.Get("core.runtime.carrierStartFailed", failure)));
            return;
        }

        _lifecycle.Activate(instance);

        // 活动面：文件记录"禁用"时，加入后立即置为 Disabled（保留实例/拓扑/配置，只停流）
        if (!enabled)
        {
            _lifecycle.Disable(instance);

            if (_accesses.TryGetValue(instance.InstanceId, out var access))
            {
                access.SetSuspended(true);
            }
        }
    }

    private void RestoreConfiguration(ModuleInstance instance, CompositeInstanceRecord record, List<string> notices)
    {
        // 片段留底：保存时把当前配置值合并进它（未识别配置类型/字段不丢失）
        _configurationFragments[instance.InstanceId] =
            record.Configuration is null ? new JsonObject() : (JsonObject)record.Configuration.DeepClone();

        if (record.Configuration is null)
        {
            return;
        }

        var objectPath = $"module:{instance.InstanceId}";

        foreach (var (typeName, node) in record.Configuration)
        {
            if (node is null)
            {
                continue;
            }

            var type = ResolveConfigurationType(instance, typeName);
            if (type is null)
            {
                var reason = CoreText.Get("core.runtime.configTypeUnresolved", typeName);
                notices.Add($"{instance.InstanceId}：{reason}");
                Issues.Report(objectPath, new IssueReport(IssueCategory.ConfigurationError, IssueSeverity.Warning, reason));
                continue;
            }

            try
            {
                if (node.Deserialize(type, ConfigurationJsonOptions) is { } value)
                {
                    instance.Context.RestoreConfiguration(typeName, value);
                }
            }
            catch (Exception ex) when (ex is JsonException or NotSupportedException)
            {
                var reason = CoreText.Get("core.runtime.configItemUnresolved", typeName, ex.Message);
                notices.Add($"{instance.InstanceId}：{reason}");
                Issues.Report(objectPath, new IssueReport(IssueCategory.ConfigurationError, IssueSeverity.Warning, reason));
            }
        }
    }

    private Type? ResolveConfigurationType(ModuleInstance instance, string typeName)
    {
        // 外部标准参数类型不在模块程序集：先按 catalog 解析出的参数类型逐字匹配。
        var fromAccess = instance.ExternalAccesses
            .Select(access => ResolveAccessParametersType(access.StandardId))
            .FirstOrDefault(type => type is not null
                && string.Equals(type.FullName, typeName, StringComparison.Ordinal));

        return fromAccess
            ?? instance.Definition.EntryType.Assembly.GetType(typeName, throwOnError: false)
            ?? Type.GetType(typeName, throwOnError: false);
    }

    private void AddPlaceholder(CompositeInstanceRecord record, string reason)
    {
        var placeholder = new PlaceholderInstance(
            record.InstanceId,
            record.ModuleId,
            reason,
            record.Enabled,
            record.Configuration is null ? null : (JsonObject)record.Configuration.DeepClone());

        _ = Content.AddPlaceholder(placeholder);

        Issues.Report(placeholder.ObjectPath, new IssueReport(
            IssueCategory.ModuleError,
            IssueSeverity.Warning,
            CoreText.Get("core.runtime.placeholderRetained", reason, record.ModuleId)));
    }

    // ---------------------------------------------------------------- 网络重锚

    /// <summary>
    /// **锚定结果**：重建的网络 + 逐源在场明细 + 悬空成员（含原因）——供调用方决定**报什么**。
    /// </summary>
    /// <remarks>
    /// 把"算"与"报"分开：<see cref="AnchorNetwork"/> 只算，Issue 由调用方按**状态是否变化**决定是否报，
    /// 从而既不在加载时漏报、也不在反复重锚时刷重复 Issue。
    /// </remarks>
    /// <param name="Network">重建后的网络（**不可变**）。</param>
    /// <param name="DeclaredSources">**声明的源**及其在场口（<see langword="null"/> = 该源未锚定）。</param>
    /// <param name="UnanchoredSources">未锚定的源（声明保留在网络的 <c>UnresolvedSources</c> 里）。</param>
    /// <param name="DanglingMembers">仍无法锚定的**宿**成员及其原因。</param>
    private readonly record struct NetworkAnchoring(
        Network Network,
        List<(NetworkMember Member, PortInstance? Port)> DeclaredSources,
        List<NetworkMember> UnanchoredSources,
        List<(NetworkMember Member, string Reason)> DanglingMembers)
    {
        /// <summary>悬空成员数（源 + 宿）——加载摘要用。</summary>
        public int DanglingCount => DanglingMembers.Count + UnanchoredSources.Count;
    }

    /// <summary>
    /// **从成员字面量重建一条网络**：组合件加载与"占位补齐后的重锚"**共用同一套规则**
    /// ——两处行为**不可能再分叉**（踩过的坑：单源重锚曾把多源网络悄悄打回单源）。
    /// </summary>
    /// <param name="name">网络名（空 = 匿名）。</param>
    /// <param name="sourceLiterals">**声明的源成员字面量**（≥1；顺序 = 声明顺序）。</param>
    /// <param name="targetLiterals">宿成员字面量。</param>
    /// <param name="unresolvedLiterals">上次遗留的**悬空宿**字面量（本次已具备条件就接回）。</param>
    /// <param name="isFromFile">是否来自组合件文件（加载 = true；占位补齐后的重锚沿用原值）。</param>
    /// <param name="notices">提示收集（**只收好消息**：本次接回了哪些成员）。</param>
    /// <returns>锚定结果（**算而报**：Issue 与问题提示由 <see cref="ReportNetworkAnchoring"/> 报）。</returns>
    private NetworkAnchoring AnchorNetwork(
        string name,
        IReadOnlyList<string> sourceLiterals,
        IReadOnlyList<string> targetLiterals,
        IReadOnlyList<string> unresolvedLiterals,
        bool isFromFile,
        List<string> notices)
    {
        var description = Describe(name);
        var sources = new List<NetworkMember>(sourceLiterals.Count);
        var unanchoredSources = new List<NetworkMember>();
        var declared = new List<(NetworkMember Member, PortInstance? Port)>(sourceLiterals.Count);

        foreach (var literal in sourceLiterals)
        {
            var member = ParseMemberAs(literal, PortDirection.Send);
            if (TryAnchorPort(member, PortDirection.Send, out var anchored, out var port))
            {
                sources.Add(anchored);
                declared.Add((member, port));
            }
            else
            {
                // **声明保留**：未锚定的源进 UnresolvedSources（带方向 = 发送），补齐模块或口后自动重锚
                unanchoredSources.Add(member);
                declared.Add((member, null));
            }
        }

        var targets = new List<NetworkMember>();
        var dangling = new List<(NetworkMember Member, string Reason)>();

        foreach (var literal in targetLiterals)
        {
            if (TryAnchorTarget(literal, declared, out var anchored, out var reason))
            {
                targets.Add(anchored);
                continue;
            }

            dangling.Add((ParseMemberAs(literal, PortDirection.Receive), reason));
        }

        // 上次遗留的悬空宿：本次若已具备条件则自动重新锚定
        foreach (var literal in unresolvedLiterals)
        {
            if (TryAnchorTarget(literal, declared, out var anchored, out _))
            {
                targets.Add(anchored);
                notices.Add(CoreText.Get("core.runtime.reanchored", description, literal));
                continue;
            }

            dangling.Add((ParseMemberAs(literal, PortDirection.Receive), CoreText.Get("core.runtime.reason.portMissingOrIncompatible")));
        }

        // **停流 ⇔ 全部声明的源都未锚定**（由 Sources 为空表达，不另存标志）
        var network = new Network(
            name, sources, targets,
            [.. dangling.Select(entry => entry.Member)],
            unanchoredSources,
            isFromFile);

        return new NetworkAnchoring(network, declared, unanchoredSources, dangling);
    }

    /// <summary>
    /// 报一条网络的**锚定问题**：源侧（停流 / 逐源未锚定）与宿侧（逐成员悬空）。
    /// </summary>
    /// <param name="anchoring">锚定结果。</param>
    /// <param name="notices">提示收集。</param>
    /// <param name="reportSources">是否报源侧问题（**源在场状态变化时报**）。</param>
    /// <param name="reportDangling">是否报宿悬空问题（**只在加载时报**；重锚只可能变少，重报 = 噪声）。</param>
    private void ReportNetworkAnchoring(
        NetworkAnchoring anchoring,
        List<string> notices,
        bool reportSources,
        bool reportDangling)
    {
        var description = Describe(anchoring.Network.Name);

        if (reportSources)
        {
            ReportSourceAnchoring(anchoring.DeclaredSources, anchoring.UnanchoredSources, notices, description);
        }

        if (!reportDangling)
        {
            return;
        }

        foreach (var (member, reason) in anchoring.DanglingMembers)
        {
            notices.Add(CoreText.Get("core.runtime.danglingNotice", description, member, reason));
            Issues.Report($"network:{description}", new IssueReport(
                IssueCategory.InterfaceError,
                IssueSeverity.Warning,
                CoreText.Get("core.runtime.danglingIssue", reason, member),
                member.PortName));
        }
    }

    /// <summary>
    /// 报**源锚定**问题：停流（全部源未锚定）与**逐源**未锚定两种口径分开说清。
    /// </summary>
    /// <param name="declaredSources">**声明的源**及其在场口（<see langword="null"/> = 未锚定）。</param>
    /// <param name="unanchoredSources">未锚定的源（声明保留）。</param>
    /// <param name="notices">提示收集。</param>
    /// <param name="description">网络描述（面向用户）。</param>
    private void ReportSourceAnchoring(
        List<(NetworkMember Member, PortInstance? Port)> declaredSources,
        List<NetworkMember> unanchoredSources,
        List<string> notices,
        string description)
    {
        if (unanchoredSources.Count == 0)
        {
            return;
        }

        if (unanchoredSources.Count == declaredSources.Count)
        {
            var all = string.Join("、", unanchoredSources);
            notices.Add(CoreText.Get("core.runtime.stalledNotice", description, all));
            Issues.Report($"network:{description}", new IssueReport(
                IssueCategory.InterfaceError,
                IssueSeverity.Warning,
                CoreText.Get("core.runtime.stalledIssue", all)));
            return;
        }

        foreach (var (member, port) in declaredSources)
        {
            if (port is not null)
            {
                continue;
            }

            // **部分源不可用 ≠ 停流**：说清"这一个源不走数据"，而不是让用户以为整条网络坏了
            notices.Add(CoreText.Get("core.runtime.sourceUnanchoredNotice", description, member));
            Issues.Report($"network:{description}", new IssueReport(
                IssueCategory.InterfaceError,
                IssueSeverity.Warning,
                CoreText.Get("core.runtime.sourceUnanchoredIssue", member),
                member.PortName));
        }
    }

    /// <summary>
    /// 锚定**一个宿成员**：口必须在场且方向为接收，且**与任一在场源契约兼容**（逐对判定）。
    /// </summary>
    /// <param name="literal">宿成员字面量。</param>
    /// <param name="declaredSources">声明的源及其在场口（<see langword="null"/> = 该源未锚定）。</param>
    /// <param name="anchored">锚定后的成员（成功时口名已按现场校正）。</param>
    /// <param name="reason">失败原因（面向用户）。</param>
    /// <returns>是否锚定成功。</returns>
    private bool TryAnchorTarget(
        string literal,
        List<(NetworkMember Member, PortInstance? Port)> declaredSources,
        out NetworkMember anchored,
        out string reason)
    {
        anchored = ParseMemberAs(literal, PortDirection.Receive);
        var anySourceAnchored = declaredSources.Exists(source => source.Port is not null);

        if (!TryAnchorPort(anchored, PortDirection.Receive, out var resolved, out var target))
        {
            reason = anySourceAnchored
                ? CoreText.Get("core.runtime.reason.portMissingOrDirection")
                : CoreText.Get("core.runtime.reason.sourceUnanchored");
            return false;
        }

        foreach (var (member, port) in declaredSources)
        {
            if (port is not null && IsCompatible(
                    member.InstanceId, port, resolved.InstanceId, target!, out _))
            {
                anchored = resolved;
                reason = string.Empty;
                return true;
            }
        }

        reason = anySourceAnchored
            ? CoreText.Get("core.runtime.reason.contractIncompatible")
            : CoreText.Get("core.runtime.reason.sourceUnanchored");
        return false;
    }

    /// <summary>
    /// 占位解析/补齐后**重新锚定**：把已具备条件的成员接回网络。
    /// </summary>
    /// <remarks>
    /// **整条网络按 <see cref="AnchorNetwork"/> 的同一套规则重算**（不做单源特例）：多源网络的
    /// 全部源一起参与锚定与逐对兼容判定 ⇒ 重锚**不可能**把多源网络打回单源。
    /// 成员关系无变化时**什么都不做**（不换对象、不动绑定）。
    /// </remarks>
    private void ReanchorNetworks(List<string> notices)
    {
        foreach (var network in Content.Networks.ToArray())
        {
            var anchoring = AnchorNetwork(
                network.Name,
                [.. network.DeclaredSources.Select(source => source.ToString())],
                [.. network.Targets.Select(target => target.ToString())],
                [.. network.UnresolvedMembers.Select(member => member.ToString())],
                network.IsFromFile,
                notices);

            var replacement = anchoring.Network;
            if (SameMembership(network, replacement))
            {
                continue;
            }

            // 源**在场状态**变了吗？（成员字面量相同也可能从"悬空"变"在场"，故单独比这两个集合）
            var sourcesChanged = !network.Sources.SequenceEqual(replacement.Sources)
                || !network.UnresolvedSources.SequenceEqual(replacement.UnresolvedSources);

            _ = Content.ReplaceNetwork(network, replacement);
            DropBinding(network);

            // 源侧问题**只在源状态变化时报**（反复重锚不刷重复 Issue）；宿悬空只在加载时报
            ReportNetworkAnchoring(anchoring, notices, reportSources: sourcesChanged, reportDangling: false);

            // 逐源重建绑定（部分源不可用 ⇒ 其余照常；全部不可用 ⇒ 停流早返回）
            RebuildBindingsCore(replacement);
        }
    }

    /// <summary>成员关系（在场源 / 悬空源 / 宿 / 悬空宿）是否完全一致——判断重锚是否**真的**改变了什么。</summary>
    private static bool SameMembership(Network left, Network right) =>
        left.Sources.SequenceEqual(right.Sources)
        && left.UnresolvedSources.SequenceEqual(right.UnresolvedSources)
        && left.Targets.SequenceEqual(right.Targets)
        && left.UnresolvedMembers.SequenceEqual(right.UnresolvedMembers);

    /// <summary>把成员解析到现场的实例口上（方向必须匹配）。</summary>
    private bool TryAnchorPort(NetworkMember member, PortDirection required, out NetworkMember anchored, out PortInstance? port)
    {
        anchored = default;
        port = null;

        if (string.IsNullOrEmpty(member.InstanceId) || string.IsNullOrEmpty(member.PortName))
        {
            return false;
        }

        if (!Content.TryGetInstance(member.InstanceId, out var instance) || instance is null)
        {
            return false;
        }

        var found = instance.FindPort(member.PortName);
        if (found is null || found.Direction != required)
        {
            return false;
        }

        anchored = new NetworkMember(member.InstanceId, member.PortName, required);
        port = found;
        return true;
    }

    /// <summary>
    /// 逐 **(源, 宿)** 对判定契约兼容（**三层判据 + 结构回退**，与运行期同一入口 —— 见 `CheckCompatibility`）。
    /// </summary>
    /// <param name="sourceInstanceId">源的实例 ID（解析其声明的数据类型）。</param>
    /// <param name="source">源口。</param>
    /// <param name="targetInstanceId">宿的实例 ID。</param>
    /// <param name="target">宿口。</param>
    /// <param name="incompatibility">不兼容原因（兼容时为 <see langword="null"/>）。</param>
    private bool IsCompatible(
        string sourceInstanceId,
        PortInstance source,
        string targetInstanceId,
        PortInstance target,
        out string? incompatibility)
    {
        var compatibility = CheckCompatibility(
            sourceInstanceId, source, targetInstanceId, target);

        incompatibility = compatibility.IsCompatible ? null : compatibility.Reason;
        return compatibility.IsCompatible;
    }

    private void DropBinding(Network network)
    {
        BindingSet current, updated;
        do
        {
            current = Volatile.Read(ref _bindings);
            updated = current.Without(network);
        }
        while (!ReferenceEquals(Interlocked.CompareExchange(ref _bindings, updated, current), current));
    }

    // ---------------------------------------------------------------- 布局

    private void ApplyLoadedLayout(Composite composite)
    {
        foreach (var item in composite.Layout)
        {
            // 布局对**不存在**的实例无意义（占位保留布局：占位也在场，故一并应用）
            if (Content.IsInstanceIdTaken(item.InstanceId))
            {
                Content.Layout.SetModule(item);
            }
        }

        foreach (var record in composite.Instances)
        {
            if (!Content.TryGetInstance(record.InstanceId, out var instance) || instance is null)
            {
                continue;
            }

            // 只恢复**该实例当前真实存在**的口几何（口消失 → 几何无意义）
            foreach (var geometry in record.Ports)
            {
                if (instance.FindPort(geometry.PortName) is not null)
                {
                    Content.Layout.SetPort(record.InstanceId, geometry);
                }
            }
        }
    }

    // ---------------------------------------------------------------- 捕获辅助

    /// <summary>
    /// 为**刚解析出的实例**补上随件保存的口几何（口消失 → 该口几何无意义，跳过）。
    /// </summary>
    /// <remarks>占位实例没有口，加载时这些几何被暂时搁置；补上后标签重新参与命名网络派生。</remarks>
    private void RestoreLoadedPortGeometry(string instanceId, ModuleInstance instance)
    {
        var record = FindLoadedInstance(instanceId);
        if (record is null)
        {
            return;
        }

        foreach (var geometry in record.Ports)
        {
            if (instance.FindPort(geometry.PortName) is not null)
            {
                Content.Layout.SetPort(instanceId, geometry);
            }
        }
    }

    private static List<string> ValidateLayout(ModuleLayoutView layout)
    {
        ArgumentNullException.ThrowIfNull(layout);
        var failures = new List<string>();

        if (string.IsNullOrWhiteSpace(layout.InstanceId))
        {
            failures.Add(CoreText.Get("core.runtime.layout.missingInstanceId"));
        }

        if (!IsFinite(layout.X) || !IsFinite(layout.Y) || Math.Abs(layout.X) > MaxCoordinate || Math.Abs(layout.Y) > MaxCoordinate)
        {
            failures.Add(CoreText.Get("core.runtime.layout.invalidCoordinates", layout.X, layout.Y));
        }

        if (!IsFinite(layout.Width) || !IsFinite(layout.Height)
            || layout.Width <= 0 || layout.Height <= 0
            || layout.Width > MaxCoordinate || layout.Height > MaxCoordinate)
        {
            failures.Add(CoreText.Get("core.runtime.layout.invalidSize", layout.Width, layout.Height));
        }

        return failures;
    }

    private static List<string> ValidatePortGeometry(string instanceId, PortGeometryView geometry)
    {
        ArgumentNullException.ThrowIfNull(geometry);
        var failures = new List<string>();

        if (string.IsNullOrWhiteSpace(instanceId))
        {
            failures.Add(CoreText.Get("core.runtime.portGeometry.missingInstanceId"));
        }

        if (string.IsNullOrWhiteSpace(geometry.PortName))
        {
            failures.Add(CoreText.Get("core.runtime.portGeometry.missingPortName"));
        }

        if (!IsFinite(geometry.Offset) || geometry.Offset is < 0 or > 1)
        {
            failures.Add(CoreText.Get("core.runtime.portGeometry.offsetOutOfRange", geometry.Offset));
        }

        return failures;
    }

    private static bool IsFinite(double value) => !double.IsNaN(value) && !double.IsInfinity(value);

    private HashSet<string> BuildSelection(IReadOnlyList<string>? instanceIds, List<string> notices)
    {
        var selection = new HashSet<string>(StringComparer.Ordinal);

        if (instanceIds is null)
        {
            foreach (var instance in Content.Instances)
            {
                _ = selection.Add(instance.InstanceId);
            }

            foreach (var placeholder in Content.Placeholders)
            {
                _ = selection.Add(placeholder.InstanceId);
            }

            return selection;
        }

        foreach (var instanceId in instanceIds)
        {
            if (Content.IsInstanceIdTaken(instanceId))
            {
                _ = selection.Add(instanceId);
            }
            else
            {
                notices.Add(CoreText.Get("core.runtime.selection.instanceMissing", instanceId));
            }
        }

        return selection;
    }

    private CompositeInstanceRecord CaptureInstance(ModuleInstance instance)
    {
        var configuration = _configurationFragments.TryGetValue(instance.InstanceId, out var preserved)
            ? (JsonObject)preserved.DeepClone()
            : new JsonObject();

        foreach (var (typeName, value) in instance.Context.ConfigurationSnapshot)
        {
            if (JsonSerializer.SerializeToNode(value, value.GetType(), ConfigurationJsonOptions) is not JsonObject current)
            {
                continue;
            }

            if (configuration[typeName] is JsonObject preservedType)
            {
                // 合并**已知属性** → 该配置对象内未识别的字段同样带回（C3-3 逐层保留）
                foreach (var (propertyName, propertyValue) in current)
                {
                    preservedType[propertyName] = propertyValue?.DeepClone();
                }
            }
            else
            {
                configuration[typeName] = current;
            }
        }

        var ports = new List<PortGeometryView>();
        foreach (var port in instance.Ports)
        {
            var geometry = Content.Layout.GetPort(instance.InstanceId, port.Name);

            // 默认值不入文件；但**网络标签**是建网的事实源，必须随件保存（否则重载后网络消失）；
            // 【G5】**边覆盖**同理（非 Auto 才写）—— 有覆盖就必须进文件，否则重载后口会跳回模块声明的默认边；
            // 【G5】**固定态**也是三态：非 null 就写（**false 同样要写** —— 那是用户"解锁"了模块声明为固定的口，
            // 不写就等于把用户的解锁丢掉）。
            if (Math.Abs(geometry.Offset - 0.5) > double.Epsilon
                || geometry.IsAnchorFixed is not null
                || geometry.Edge != HYWDebugAssistant.Sdk.Ports.PortEdge.Auto
                || !string.IsNullOrWhiteSpace(geometry.Label))
            {
                ports.Add(geometry);
            }
        }

        var viewState = ViewState is Services.ViewStateStore store
            ? store.Capture(instance.InstanceId, instance.PersistentInputs)
            : null;

        return new CompositeInstanceRecord
        {
            InstanceId = instance.InstanceId,
            ModuleId = instance.Definition.Id.ToString(),
            Enabled = instance.State != ModuleInstanceState.Disabled,
            Configuration = configuration.Count == 0 ? null : configuration,
            ViewState = viewState is { IsEmpty: false } ? viewState : null,
            Ports = ports,
            Fragment = FindLoadedInstance(instance.InstanceId)?.Fragment,
        };
    }

    private CompositeNetworkRecord CaptureNetwork(Network network)
    {
        var loaded = FindLoadedNetwork(network);

        return new CompositeNetworkRecord
        {
            Name = network.Name,
            // **写全部声明的源**（在场 + 悬空）：文件存"声明"，在场与否是加载期重新推导的运行时事实
            Sources = [.. network.DeclaredSources.Select(source => source.ToString())],
            Targets = [.. network.Targets.Select(t => t.ToString())],
            Unresolved = [.. network.UnresolvedMembers.Select(m => m.ToString())],
            Fragment = loaded?.Fragment,
        };
    }

    private void AddLayoutIfPresent(string instanceId, List<ModuleLayoutView> layout)
    {
        if (Content.Layout.TryGetModule(instanceId, out var item))
        {
            layout.Add(item);
        }
    }

    /// <summary>
    /// **只重映射冲突的实例 ID**：新加入的模块保留文件里的 ID；已被当前现场占用的 ID 生成唯一后缀，
    /// 并同步重写网络成员与布局键。标签/网络名/配置/位置不动。
    /// </summary>
    /// <remarks>
    /// 这是"加载一批模块"的单条规则，**不判断现场是否为空**：空现场没有冲突 ⇒ 原样还原；
    /// 非空现场只有真正冲突的那几个 ID 变，因此同一文件可以重复加载。
    /// </remarks>
    private Composite RemapConflictingInstanceIds(Composite composite, List<string> notices)
    {
        var reserved = new HashSet<string>(StringComparer.Ordinal);
        var idMap = new Dictionary<string, string>(StringComparer.Ordinal);
        var instances = new List<CompositeInstanceRecord>(composite.Instances.Count);
        var remapped = 0;

        foreach (var record in composite.Instances)
        {
            var newId = record.InstanceId;
            if (Content.IsInstanceIdTaken(newId) || !reserved.Add(newId))
            {
                newId = GenerateUniqueInstanceId(record.InstanceId, reserved);
                _ = idMap.TryAdd(record.InstanceId, newId);
                remapped++;
            }

            instances.Add(newId == record.InstanceId
                ? record
                : new CompositeInstanceRecord
                {
                    InstanceId = newId,
                    ModuleId = record.ModuleId,
                    Enabled = record.Enabled,
                    Configuration = record.Configuration is null
                        ? null
                        : (JsonObject)record.Configuration.DeepClone(),
                    ViewState = record.ViewState,
                    Wallpaper = record.Wallpaper,
                    Ports = [.. record.Ports],
                    Fragment = record.Fragment is null
                        ? null
                        : (JsonObject)record.Fragment.DeepClone(),
                });
        }

        if (remapped == 0)
        {
            return composite;
        }

        var networks = new List<CompositeNetworkRecord>(composite.Networks.Count);
        foreach (var network in composite.Networks)
        {
            networks.Add(new CompositeNetworkRecord
            {
                Name = network.Name,
                Sources = [.. network.Sources.Select(member => RemapMemberLiteral(member, idMap))],
                Targets = [.. network.Targets.Select(member => RemapMemberLiteral(member, idMap))],
                Unresolved = [.. network.Unresolved.Select(member => RemapMemberLiteral(member, idMap))],
                Fragment = network.Fragment is null
                    ? null
                    : (JsonObject)network.Fragment.DeepClone(),
            });
        }

        var layout = composite.Layout
            .Select(item => idMap.TryGetValue(item.InstanceId, out var newId)
                ? item with { InstanceId = newId }
                : item)
            .ToList();

        notices.Add(CoreText.Get("core.runtime.instanceIdRemapped", remapped));

        return new Composite
        {

            Name = composite.Name,
            SavedAt = composite.SavedAt,
            Instances = instances,
            Networks = networks,
            Layout = layout,
            Assets = composite.Assets,
            WorkspacePlacement = composite.WorkspacePlacement,
            RootFragment = composite.RootFragment is null
                ? null
                : (JsonObject)composite.RootFragment.DeepClone(),
        };
    }

    /// <summary>为一个冲突 ID 生成当前现场与本次批次都未占用的新 ID。</summary>
    private string GenerateUniqueInstanceId(string baseId, HashSet<string> reserved)
    {
        var prefix = string.IsNullOrWhiteSpace(baseId) ? "instance" : baseId;
        var index = 2;

        while (true)
        {
            var candidate = $"{prefix}-{index++}";
            if (!Content.IsInstanceIdTaken(candidate) && reserved.Add(candidate))
            {
                return candidate;
            }
        }
    }

    /// <summary>把 <c>实例ID.口名</c> 的实例前缀按 ID 映射改写；未知前缀原样保留。</summary>
    private static string RemapMemberLiteral(string literal, Dictionary<string, string> idMap)
    {
        var separator = literal.LastIndexOf('.');
        if (separator <= 0)
        {
            return literal;
        }

        var instanceId = literal[..separator];
        return idMap.TryGetValue(instanceId, out var newId)
            ? $"{newId}{literal[separator..]}"
            : literal;
    }

    private CompositeInstanceRecord? FindLoadedInstance(string instanceId) =>
        _loadedSource?.Instances.FirstOrDefault(r => string.Equals(r.InstanceId, instanceId, StringComparison.Ordinal));

    private CompositeNetworkRecord? FindLoadedNetwork(Network network)
    {
        if (_loadedSource is null)
        {
            return null;
        }

        if (network.Name.Length > 0)
        {
            return _loadedSource.Networks.FirstOrDefault(r => string.Equals(r.Name, network.Name, StringComparison.Ordinal));
        }

        // 匿名网络没有唯一名 → 用**源集合 + 宿集合**定位（与保存端同一匹配规则；多源亦适用）
        var declared = network.DeclaredSources.Select(s => s.ToString()).OrderBy(s => s, StringComparer.Ordinal).ToList();

        return _loadedSource.Networks.FirstOrDefault(r =>
            r.Name.Length == 0
            && r.Sources.OrderBy(s => s, StringComparer.Ordinal).SequenceEqual(declared)
            && r.Targets.OrderBy(t => t, StringComparer.Ordinal)
                .SequenceEqual(network.Targets.Select(t => t.ToString()).OrderBy(t => t, StringComparer.Ordinal)));
    }

    private static NetworkMember ParseMemberAs(string literal, PortDirection direction) =>
        CompositeSerializer.TryParseMember(literal, out var member)
            ? member with { Direction = direction }
            : new NetworkMember(literal, string.Empty, direction);

    private static string Describe(Network network) => network.IsNamed
        ? network.Name
        : CoreText.Get(
            "core.runtime.networkAnonymousDescription",
            string.Join(CoreText.Get("core.common.listSeparator"), network.DeclaredSources));

    private static string Describe(string name) =>
        name.Length > 0 ? name : CoreText.Get("core.composite.anonymous");

    private static string DescribeName(Composite composite) =>
        composite.Name.Length > 0 ? composite.Name : CoreText.Get("core.composite.unnamed");
}

/// <summary>
/// 模块定义解析器（由 L4 服务注入注册表与框架兼容判定）：失败时给出**面向用户的原因**。
/// </summary>
/// <param name="moduleIdLiteral">模块标识字面量（作者/模块名/版本）。</param>
/// <param name="definition">解析到的定义（失败时为 <see langword="null"/>）。</param>
/// <param name="reason">失败原因（面向用户；成功时为 <see langword="null"/>）。</param>
internal delegate bool ModuleDefinitionResolver(string moduleIdLiteral, out ModuleDefinition? definition, out string? reason);

/// <summary>加载结果统计（面向调用方的摘要；逐条问题见 Issue 中心）。</summary>
/// <param name="InstancesAdded">成功实例化的实例数。</param>
/// <param name="PlaceholdersCreated">降级为占位实例的数量。</param>
/// <param name="NetworksRestored">恢复的网络数（含停流的）。</param>
/// <param name="UnresolvedMembers">悬空成员数（保留声明、不参与绑定）。</param>
/// <param name="Notices">面向用户的提示。</param>
internal sealed record CompositeLoadResult(
    int InstancesAdded,
    int PlaceholdersCreated,
    int NetworksRestored,
    int UnresolvedMembers,
    IReadOnlyList<string> Notices);
