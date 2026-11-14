using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using CommunityToolkit.Mvvm.ComponentModel;
using HYWDebugAssistant.Core.Api;
using HYWDebugAssistant.Sdk.Ports;
using HYWDebugAssistant.Sdk.Text;
using HYWDebugAssistant.Sdk.Ui;
using HYWDebugAssistant.Sdk.Ui.Formatting;
using HYWDebugAssistant.Ui.Controls;
using HYWDebugAssistant.Ui.Geometry;
using HYWDebugAssistant.Ui.Runtime;
using HYWDebugAssistant.Ui.Views;

namespace HYWDebugAssistant.Ui.ViewModels;

/// <summary>窗口上的缩放手柄（边框的边中点/角点 —— Office 直觉）。</summary>
public enum ResizeHandle
{
    /// <summary>左中。</summary>
    Left = 0,

    /// <summary>右中。</summary>
    Right = 1,

    /// <summary>上中。</summary>
    Top = 2,

    /// <summary>下中。</summary>
    Bottom = 3,

    /// <summary>左上角。</summary>
    TopLeft = 4,

    /// <summary>右上角。</summary>
    TopRight = 5,

    /// <summary>左下角。</summary>
    BottomLeft = 6,

    /// <summary>右下角。</summary>
    BottomRight = 7,
}

/// <summary>
/// **模块窗口集控制器**（拼图式表现形态的"模块区"）：模块 ↔ 无边框窗口的几何与交互逻辑。
/// </summary>
/// <remarks>
/// <list type="bullet">
///   <item><b>没有"工作区"容器</b>：本类不持有画布、不画背景、不做边界钳制——背景就是用户桌面；</item>
///   <item><b>坐标 = 屏幕坐标（物理像素）</b>：拖动/缩放直接改窗口位置与大小；吸附把**屏幕工作区、
///     框架外壳窗口、其他模块窗口**一并作为参照；</item>
///   <item><b>组关系由位置派生</b>：拖动开始时求贴合连通分量并冻结、期间整组位移、结束后重算；
///     按住 Ctrl 或选中态"独立拖动"只动一个；</item>
///   <item><b>本类与窗口技术无关</b>（只操作数值几何）→ 可独立单元测试；真正的开窗/关窗在窗口管理器。</item>
/// </list>
/// </remarks>
public sealed partial class ModuleWindowsViewModel : ViewModelBase
{
    /// <summary>对齐类吸附容差（px）——**默认值**；实际值取设置里的「吸附距离」（见 <see cref="SnapDistance"/>）。</summary>
    public const double SnapTolerance = SnapEngine.DefaultTolerance;

    /// <summary>贴合（接触拼接）容差（px）——**宽**：拼接是主用途，值得给宽容差；同时它也是"是否算一组"的口径。</summary>
    public const double GroupTolerance = SnapEngine.FlushTolerance;

    /// <summary>
    /// **外壳与模块"拼上"的容差**（px）——严格：只有**真的贴上**（吸附产生的 0 间隙，容许 1px 取整误差）才算。
    /// </summary>
    /// <remarks>
    /// 与 <see cref="GroupTolerance"/>（模块之间，8px）**刻意不同**：模块之间"差一点也算拼上"对拼装手感友好；
    /// 但外壳用它会让"停在附近、没吸附"的模块在拖动主窗口时被一并带走。
    /// </remarks>
    public const double ShellJoinTolerance = 1;

    /// <summary>
    /// **当前生效的吸附距离**（px）——设置项 `snapDistance`（默认 4，2–8），
    /// 由 `Refresh` 从设置同步；**对齐类容差 = 本值**，**贴合类容差 = 本值 × 2**。
    /// </summary>
    public double SnapDistance { get; private set; } = SnapEngine.DefaultTolerance;

    /// <summary>新模块默认摆放的起始偏移与步长（物理像素；外壳有效时以**外壳右侧**为起点）。</summary>
    private const double PlaceOrigin = 48;

    /// <summary>新模块与外壳/同列模块之间的间距（物理像素）。</summary>
    private const double PlaceGap = 12;

    private readonly IRuntimeQuery _query;
    private readonly IRuntimeCommand _command;
    private readonly Dictionary<(string InstanceId, string Source), SubscriptionSourceView> _sources = [];

    /// <summary>本帧的订阅源快照（一次取、所有模块视图共用 —— 每模块各取一次等于重复建同样的视图对象）。</summary>
    private readonly List<SubscriptionSourceView> _frameSources = [];

    /// <summary>指标刷新间隔（队列水位 / 速率 / 角色方块；按时间而不是帧数）。</summary>
    private static readonly TimeSpan MetricsInterval = TimeSpan.FromMilliseconds(500);

    /// <summary>上一帧取到的网络视图与实例视图（引用比较：同一个对象 ⇒ 这一帧什么都没变）。</summary>
    private IReadOnlyList<NetworkView>? _lastNetworks;
    private IReadOnlyList<ModuleInstanceView>? _lastInstances;
    private IReadOnlyList<ModuleInstanceView>? _instancesForSignature;

    /// <summary>上一次刷指标的时刻（<see cref="Stopwatch"/> 计时刻度；0 = 还没刷过）。</summary>
    private long _lastMetricsTimestamp;

    /// <summary>当前生效设置的本地快照；只在启动/应用设置时重读，避免每帧深拷贝。</summary>
    private ApplicationSettings? _settings;

    /// <summary>本拍结构签名是否变化（决定对端文本与连接指标是否要重算）。</summary>
    private bool _structureChanged;

    /// <summary>对端文本是否尚未建立/已失效（首次与结构变化后为 true）。</summary>
    private bool _peersDirty = true;

    /// <summary>窗口管理行按实例索引（避免每帧线性查找与 O(n²) 清理）。</summary>
    private readonly Dictionary<string, ModuleOverviewRowViewModel> _overviewById = new(StringComparer.Ordinal);

    /// <summary>本拍已见实例（复用集合，避免每帧新建）。</summary>
    private readonly HashSet<string> _seenInstances = new(StringComparer.Ordinal);

    /// <summary>本拍布局快照（复用字典）。</summary>
    private readonly Dictionary<string, ModuleLayoutView> _layoutById = new(StringComparer.Ordinal);

    /// <summary>本拍新出现的实例（复用列表；只有增删时才需要组级落位判断）。</summary>
    private readonly List<ModuleInstanceView> _incomingInstances = [];

    /// <summary>上次已向界面通知的模块数（只在数量真变时发通知）。</summary>
    private int _lastModuleCount = -1;

    /// <summary>本次拖动**起点**的各成员几何（拖动中按 Ctrl 拆分时用来"放回原位"）。</summary>
    private readonly Dictionary<string, CanvasRect> _dragStartRects = new(StringComparer.Ordinal);

    /// <summary>本次拖动**起点**各成员的整数物理像素矩形（整组位移的唯一基准）。</summary>
    private readonly Dictionary<string, PixelEdges> _dragStartPixels = new(StringComparer.Ordinal);

    /// <summary>本次拖动**起点**的完整布局（连接在拖动中丢失时会提前提交中间布局，取消时要还原）。</summary>
    private readonly Dictionary<string, ModuleLayoutView> _dragStartLayouts = new(StringComparer.Ordinal);

    /// <summary>本次拖动中是否已经为了断开直连提交过中间布局。</summary>
    private bool _dragCommittedLive;

    private string? _draggingId;
    private List<string> _dragGroup = [];

    /// <summary>组内成员在**被拖窗口 DIP 坐标**下的起始矩形（组吸附的输入；顺序同 <see cref="_dragGroup"/>）。</summary>
    private readonly List<CanvasRect> _dragStartDragRects = [];

    /// <summary>组吸附的锚点成员下标（返回值以它为基准换算成整组位移）。</summary>
    private int _dragAnchorIndex;

    /// <summary>组外障碍矩形（同一 DIP 坐标；组吸附不允许把任何成员叠到它们上面）。</summary>
    private IReadOnlyList<CanvasRect> _dragObstacles = [];

    /// <summary>手势按下时的指针位置（**整数物理像素**）——整组位移从它算起，不逐帧累加。</summary>
    private PixelPoint _dragStartPointer;

    /// <summary>上一帧已施加的整组整数物理位移（相同则跳过，兼作死区；null = 本帧还没动过）。</summary>
    private PixelPoint? _lastGroupDelta;

    /// <summary>拖动开始时缓存的参照线/端口锚点（拖动中其它模块不动，不必每帧重建）。</summary>
    private IReadOnlyList<SnapLine> _dragLines = [];
    private IReadOnlyList<PortAnchorPair> _dragAnchors = [];
    private SnapMemory _snapMemory;

    /// <summary>拖动时"即将建网"预览的节流（每帧查询 Core 建网预览代价高）。</summary>
    private long _lastDockPreviewTicks;
    private int _lastDockPreviewCount = -1;

    /// <summary>
    /// 节流窗口内被推迟的建网预览补算（拖动结束/取消时取消）。
    /// </summary>
    /// <remarks>
    /// 没有它时，接触那一帧若恰好落在 50ms 节流窗口内，而此后两轴都被吸附锁住（groupDelta 不变），
    /// 端口高亮就永远不亮 —— 表现为"x 轴相等上下拖动拼接时不加粗，斜着拼就正常"。
    /// </remarks>
    private IDisposable? _dockPreviewRetry;

    /// <summary>整体平移造成的位置变化、**尚未落盘**的实例（合并提交用，见 <see cref="CommitSceneTranslation"/>）。</summary>
    private readonly HashSet<string> _pendingSceneCommits = new(StringComparer.Ordinal);

    /// <summary>本次拖动相对起点的**绝对整数物理像素**位移（不是逐帧增量；取消/拆分时按起点还原）。</summary>
    private int _shellDragPxX;
    private int _shellDragPxY;

    /// <summary>本次拖动开始时外壳的物理原点（`Window.Position`；绝对目标以它为基准）。</summary>
    private PixelPoint? _shellDragOrigin;

    /// <summary>
    /// 本次拖动开始时外壳的**可见外框**（物理像素）。
    /// </summary>
    /// <remarks>
    /// 外壳的 `Window.Position` 是外层窗口左上角，与该窗口的 DWM 可见外框相差系统装饰的不可见边框。
    /// 吸附/成组用的是可见外框 ⇒ 跟随拖动时只能把它整体平移，不能拿窗口位置冒充它。
    /// </remarks>
    private CanvasRect? _shellDragStartFrame;

    /// <summary>
    /// **请求把外壳移动到绝对物理目标**：拖动组包含外壳时（模块单侧吸附在外壳上）触发，
    /// 由 `ModuleWindowManager` 直接落到真实窗口上；不再传 DIP 增量，避免二次缩放与逐帧取整。
    /// </summary>
    /// <remarks>
    /// 主窗口与模块形成一个视觉整体后，移动谁都是整体动 ——
    /// 外壳不是"应用的框架"，而是**拼图里的一个节点**；因此拖模块与拖外壳走的是同一套成组逻辑。
    /// </remarks>
    public event EventHandler<PixelPoint>? ShellMoveRequested;

    private string? _resizingId;
    private ResizeHandle _resizeHandle;
    private CanvasRect _resizeStartRect;

    /// <summary>创建控制器。</summary>
    /// <param name="query">查询面（UI 唯一读通道）。</param>
    /// <param name="command">命令面（UI 唯一写通道）。</param>
    /// <param name="viewState">
    /// **视图状态存取面**：切页选中态 / 输入框未提交文本 /
    /// 显示缓冲都存这里 ⇒ 元素表重建不丢内容、模块可回读。`null` ⇒ 不落存储（设计期 / 未接线的调用点）。
    /// </param>
    public ModuleWindowsViewModel(
        IRuntimeQuery query,
        IRuntimeCommand command,
        IViewStateAccess? viewState = null,
        IModuleColorPicker? colorPicker = null)
    {
        ArgumentNullException.ThrowIfNull(query);
        ArgumentNullException.ThrowIfNull(command);

        _query = query;
        _command = command;
        _viewState = viewState;
        _colorPicker = colorPicker;
    }

    private readonly IViewStateAccess? _viewState;
    private readonly IModuleColorPicker? _colorPicker;

    /// <summary>模块窗口（每个模块一项）。</summary>
    public ObservableCollection<ModuleViewViewModel> Modules { get; } = [];

    /// <summary>交互页「窗口管理」列表的行（按模块增量同步；对象身份稳定）。</summary>
    public ObservableCollection<ModuleOverviewRowViewModel> Overview { get; } = [];

    /// <summary>所有屏幕的工作区（**DIP**；用于判断模块是否落在屏幕外）。</summary>
    public IReadOnlyList<CanvasRect> WorkAreas { get; private set; } = [];

    /// <summary>
    /// 屏幕工作区（**DIP**；外壳窗口关闭时为 null = 不做屏幕吸附与钳制）。
    /// </summary>
    /// <remarks>
    /// 由窗口管理器**换算成 DIP 后**发布（<c>Screens.WorkingArea</c> 本身是物理像素），
    /// 这样它与模块坐标（DIP）同单位，摆放与钳制不必各自换算。
    /// </remarks>
    [ObservableProperty]
    public partial CanvasRect? WorkArea { get; set; }

    /// <summary>
    /// 框架外壳窗口矩形（拼图"起始点"，可被贴靠；**DIP**，同 <see cref="WorkArea"/> 的口径）。
    /// </summary>
    [ObservableProperty]
    public partial CanvasRect? ShellRect { get; set; }

    /// <summary>外壳**可见外框**的物理像素矩形（与 <see cref="ShellRect"/> 同一次探测发布）。</summary>
    public CanvasRect? ShellPhysicalRect { get; private set; }

    /// <summary>
    /// **实例 → 当前叠放序号的来源**（越大越靠上）；由窗口管理器注入，测试/设计期可为空。
    /// </summary>
    /// <remarks>
    /// 层不是全局 z-order 序号，而是由该序号 + 重合关系派生的“叠放带”；见 <see cref="RefreshLayers"/>。
    /// </remarks>
    public Func<string, int>? ZOrderProvider { get; set; }

    /// <summary>外壳窗口的**物理位置**（<c>Window.Position</c>；绝对移动的目标必须用它，不能拿可见外框原点）。</summary>
    public PixelPoint? ShellWindowPosition { get; private set; }

    /// <summary>由窗口管理器原子发布外壳的 DIP 矩形与物理外框（两份事实必须同源）。</summary>
    /// <param name="dip">DIP 矩形（模型/叠加层用）。</param>
    /// <param name="physical">物理像素矩形（成组/吸附/接触判定用）。</param>
    public void SetShellFrame(CanvasRect dip, CanvasRect physical)
    {
        ShellRect = dip;
        ShellPhysicalRect = physical;
    }

    /// <summary>发布外壳窗口的物理位置（与可见外框分开发布）。</summary>
    /// <param name="position">窗口位置（物理像素）。</param>
    public void SetShellWindowPosition(PixelPoint position) => ShellWindowPosition = position;

    /// <summary>
    /// 是否显示**叠加信息层**（端口标记本身常显，本开关只控制叠加层）。
    /// **默认关闭**（它是调试/观测层，不进常驻界面）。
    /// </summary>
    [ObservableProperty]
    public partial bool ShowInfoOverlay { get; set; }

    /// <summary>吸附是否启用（交互组开关；按住 Alt 可临时停用）。</summary>
    [ObservableProperty]
    public partial bool SnapEnabled { get; set; } = true;

    /// <summary>当前主题的壁纸遮罩浓度（由外壳按主题下发）。</summary>
    [ObservableProperty]
    public partial double WallpaperVeilOpacity { get; set; } = 0.55;

    /// <summary>
    /// **多行文本输入的提交键**——应用级设置 `multilineSubmitKey` 的镜像，
    /// 由本类下发到每个模块窗口（与 <see cref="ShowInfoOverlay"/> 同一条路径）。
    /// </summary>
    /// <remarks>
    /// 三档：<c>CtrlEnter</c>（默认：回车换行 / Ctrl+Enter 提交 / 失焦不提交）· <c>Enter</c>（回车提交 / Shift+Enter 换行）·
    /// <c>None</c>（键盘都不提交）。**只影响多行输入**；单行输入仍是"回车 / 失焦一次提交"。
    /// </remarks>
    [ObservableProperty]
    public partial string MultilineSubmitKey { get; set; } = "CtrlEnter";

    /// <summary>当前选中的模块实例（**锚点**；多选时 = 第一个选中的那一个）。</summary>
    [ObservableProperty]
    public partial string? SelectedInstanceId { get; set; }

    /// <summary>当前选择集（有序；包含隐藏/占位实例）。</summary>
    public IReadOnlyList<string> SelectedInstanceIds => [.. _selectedInstanceIds];

    /// <summary>选择集大小。</summary>
    public int SelectionCount => _selectedInstanceIds.Count;

    /// <summary>是否有选中项。</summary>
    public bool HasSelection => _selectedInstanceIds.Count > 0;

    /// <summary>选择集变化（组合页/状态展示订阅）。</summary>
    public event EventHandler? SelectionChanged;

    /// <summary>
    /// **窗口点击**要求把整组抬到应用内最前（组内 primary 最后抬 ⇒ 在最上）。
    /// </summary>
    /// <remarks>
    /// 只在窗口点击路径触发（<see cref="SelectGroup"/>）；<see cref="Select"/> / <see cref="SetSelection"/> /
    /// <see cref="ToggleSelection"/> 不触发 —— 组合页勾选不联动抬窗。
    /// </remarks>
    public event EventHandler<ModuleGroupRaiseRequest>? GroupRaiseRequested;

    private readonly List<string> _selectedInstanceIds = [];

    /// <summary>组合页首次打开时才把“全部模块”作为默认选择；未初始化时不显示选中框。</summary>
    private bool _selectionInitialized;

    /// <summary>
    /// **组合页首次进入**：还没有初始化过选择时，默认全选。
    /// </summary>
    /// <remarks>选择是界面操作状态，不写盘；只在用户第一次打开组合页时初始化，避免启动即满屏选中框。</remarks>
    public void EnsureSelectionInitialized()
    {
        if (_selectionInitialized)
        {
            return;
        }

        SelectAll();
    }

    /// <summary>
    /// **单选**（普通点击模块边框/端口标记）：清空后只选它。
    /// </summary>
    /// <param name="instanceId">实例 ID（null/未知 = 清空选择）。</param>
    /// <remarks>
    /// 锁定模块**可选中**（锁定只禁止拖动/缩放；组合范围仍可包含它）。
    /// </remarks>
    public void Select(string? instanceId)
    {
        if (instanceId is not { Length: > 0 } id || Find(id) is null)
        {
            ClearSelection();
            return;
        }

        _selectedInstanceIds.Clear();
        _selectedInstanceIds.Add(id);
        _selectionInitialized = true;
        SelectedInstanceId = id;
        ApplySelection();
    }

    /// <summary>加入/移出选择集（Shift+点击模块边框，或组合页勾选）；**加选不移动锚点**。</summary>
    /// <param name="instanceId">实例 ID。</param>
    public void ToggleSelection(string instanceId)
    {
        if (string.IsNullOrWhiteSpace(instanceId) || Find(instanceId) is null)
        {
            return;
        }

        _selectionInitialized = true;

        if (_selectedInstanceIds.Remove(instanceId))
        {
            if (string.Equals(SelectedInstanceId, instanceId, StringComparison.Ordinal))
            {
                SelectedInstanceId = _selectedInstanceIds.Count > 0 ? _selectedInstanceIds[0] : null;
            }
        }
        else
        {
            _selectedInstanceIds.Add(instanceId);

            // **锚点 = 第一个选中**：后续 Shift 加选只扩展集合，不抢走锚点。
            if (SelectedInstanceId is not { Length: > 0 } anchor
                || !_selectedInstanceIds.Contains(anchor, StringComparer.Ordinal))
            {
                SelectedInstanceId = instanceId;
            }
        }

        ApplySelection();
    }

    /// <summary>替换整个选择集（组合页全选/反选/批量设置用）。</summary>
    /// <param name="instanceIds">实例 ID 集合（不存在的项被忽略）。</param>
    /// <param name="primary">锚点（须在集合内；缺省 = 保留仍在集合中的锚点，否则取集合第一项）。</param>
    public void SetSelection(IEnumerable<string> instanceIds, string? primary = null)
    {
        ArgumentNullException.ThrowIfNull(instanceIds);

        _selectionInitialized = true;
        var previousAnchor = SelectedInstanceId;
        _selectedInstanceIds.Clear();

        foreach (var id in instanceIds)
        {
            if (Find(id) is not null && !_selectedInstanceIds.Contains(id, StringComparer.Ordinal))
            {
                _selectedInstanceIds.Add(id);
            }
        }

        SelectedInstanceId = primary is { Length: > 0 } candidate && _selectedInstanceIds.Contains(candidate, StringComparer.Ordinal)
            ? candidate
            : previousAnchor is { Length: > 0 } kept && _selectedInstanceIds.Contains(kept, StringComparer.Ordinal)
                ? kept
                : _selectedInstanceIds.Count > 0 ? _selectedInstanceIds[0] : null;

        ApplySelection();
    }

    /// <summary>选中全部模块（含隐藏/占位）。</summary>
    public void SelectAll() =>
        SetSelection(Modules.Select(module => module.InstanceId));

    /// <summary>把锚点移到某个已选中的模块（不改变集合；普通点击/拖动时用）。</summary>
    /// <param name="instanceId">实例 ID。</param>
    public void SetPrimary(string instanceId)
    {
        if (!_selectedInstanceIds.Contains(instanceId, StringComparer.Ordinal))
        {
            Select(instanceId);
            return;
        }

        SelectedInstanceId = instanceId;
        ApplySelection();
    }

    /// <summary>
    /// **普通点击已拼接模块**：选中整组（锚点 = 被点者）并请求把整组抬到最前。
    /// </summary>
    /// <param name="instanceId">被点击的实例 ID。</param>
    /// <remarks>
    /// 组 = 与拖动同一份连通分量（<see cref="GroupOfNode"/> / <see cref="GroupTolerance"/>）；
    /// 过滤掉不存在与不可见（隐藏/占位）的成员；**锁定模块保留**（锁定只禁拖动）。
    /// 被点者不在可见组里（不存在/隐藏）⇒ 退回 <see cref="Select"/> 语义；不存在时不触发事件。
    /// </remarks>
    public void SelectGroup(string instanceId)
    {
        if (Find(instanceId) is null)
        {
            Select(instanceId);
            return;
        }

        var group = GroupOfNode(instanceId)
            .Where(id => Find(id) is { IsViewVisible: true })
            .ToList();

        if (!group.Contains(instanceId, StringComparer.Ordinal))
        {
            Select(instanceId);
            return;
        }

        SetSelection(group, primary: instanceId);
        GroupRaiseRequested?.Invoke(this, new ModuleGroupRaiseRequest(group, instanceId));
    }

    /// <summary>若模块未选中则单选它；已选中则只把锚点移过去（保留多选）。</summary>
    /// <param name="instanceId">实例 ID。</param>
    public void SelectOrKeep(string instanceId)
    {
        if (Find(instanceId) is null)
        {
            return;
        }

        if (_selectedInstanceIds.Contains(instanceId, StringComparer.Ordinal))
        {
            SetPrimary(instanceId);
        }
        else
        {
            Select(instanceId);
        }
    }

    /// <summary>从选择集移除一个实例（关闭模块时用；其余选中保留）。</summary>
    /// <param name="instanceId">实例 ID。</param>
    public void RemoveFromSelection(string instanceId)
    {
        if (!_selectedInstanceIds.Remove(instanceId))
        {
            return;
        }

        if (string.Equals(SelectedInstanceId, instanceId, StringComparison.Ordinal))
        {
            SelectedInstanceId = _selectedInstanceIds.Count > 0 ? _selectedInstanceIds[0] : null;
        }

        ApplySelection();
    }

    /// <summary>
    /// 清空全部选中：点内容区 / 点外壳 / 点本应用其他窗口。
    /// </summary>
    public void ClearSelection()
    {
        if (_selectedInstanceIds.Count == 0 && SelectedInstanceId is null)
        {
            return;
        }

        _selectedInstanceIds.Clear();
        SelectedInstanceId = null;
        ApplySelection();
    }

    /// <summary>
    /// 选择集的**唯一视觉对账点**：所有模块的 `IsSelected/IsPrimary` 一次对齐。
    /// </summary>
    private void ApplySelection()
    {
        // 现场可能刚移除/重建模块：先剪掉不存在的 ID。
        _ = _selectedInstanceIds.RemoveAll(id => Find(id) is null);

        var selected = new HashSet<string>(_selectedInstanceIds, StringComparer.Ordinal);
        var primary = SelectedInstanceId is { Length: > 0 } candidate && selected.Contains(candidate)
            ? candidate
            : _selectedInstanceIds.Count > 0 ? _selectedInstanceIds[0] : null;

        var changed = !string.Equals(SelectedInstanceId, primary, StringComparison.Ordinal);
        if (changed)
        {
            SelectedInstanceId = primary;
        }

        foreach (var module in Modules)
        {
            var isSelected = selected.Contains(module.InstanceId);
            if (module.IsSelected != isSelected)
            {
                module.IsSelected = isSelected;
                changed = true;
            }

            var isPrimary = isSelected && string.Equals(module.InstanceId, primary, StringComparison.Ordinal);
            if (module.IsPrimary != isPrimary)
            {
                module.IsPrimary = isPrimary;
                changed = true;
            }
        }

        if (changed)
        {
            OnPropertyChanged(nameof(SelectedInstanceIds));
            OnPropertyChanged(nameof(SelectionCount));
            OnPropertyChanged(nameof(HasSelection));
            SelectionChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    /// <summary>UI 刷新率（Hz；来自应用级设置）。</summary>
    [ObservableProperty]
    public partial int RefreshRateHz { get; set; } = 60;

    /// <summary>全部窗口是否已锁定（"锁定全部"开关的状态）。</summary>
    [ObservableProperty]
    public partial bool AllLocked { get; set; }

    /// <summary>外壳窗口当前所在屏的缩放（由窗口管理器发布；组内物理位移换算用）。</summary>
    public double ShellScaling { get; set; } = 1;

    /// <summary>是否正在拖动或缩放。</summary>
    public bool IsInteracting => _draggingId is not null || _resizingId is not null;

    /// <summary>由窗口管理器回写某模块窗口的真实缩放（开窗前/跨屏后）。</summary>
    /// <param name="instanceId">实例 ID。</param>
    /// <param name="scaling">缩放；≤0 忽略。</param>
    public void SetModuleScaling(string instanceId, double scaling)
    {
        if (scaling > 0 && Find(instanceId) is { } module)
        {
            module.Scaling = scaling;

            // Core 的建网/接触判定也要读同一份 Scale：缩放变化后立即把布局事实刷新一次。
            _ = _command.UpdateModuleLayout(module.ToLayout());
        }
    }

    /// <summary>模块数（状态栏用）。</summary>
    public int ModuleCount => Modules.Count;

    /// <summary>窗口几何变化（窗口管理器订阅：把数值几何落到真正的窗口上）。</summary>
    public event EventHandler<ModuleWindowGeometryChange>? GeometryChanged;

    /// <summary>窗口可见性变化（窗口管理器订阅：开窗/关窗）。</summary>
    public event EventHandler<ModuleWindowVisibilityChange>? VisibilityChanged;

    /// <summary>请求把某个模块窗口显示并置于应用内最前（窗口管理器订阅）。</summary>
    public event EventHandler<string>? WindowActivationRequested;

    /// <summary>
    /// **面向用户的提示**（建网结果、端口标签提交结果等）——外壳把它显示到状态栏。
    /// </summary>
    /// <remarks>参数：提示文本 + 是否为错误（错误以警示色显示）。</remarks>
    public event EventHandler<(string Message, bool IsError)>? NoticeRaised;

    /// <summary>发出提示（同时保留最后一次提示，便于界面初次绑定）。</summary>
    /// <param name="message">提示文本。</param>
    /// <param name="isError">是否错误。</param>
    public void RaiseNotice(string message, bool isError = false)
    {
        if (string.IsNullOrWhiteSpace(message))
        {
            return;
        }

        LastNotice = message;
        LastNoticeIsError = isError;
        NoticeRaised?.Invoke(this, (message, isError));
    }

    /// <summary>最后一次提示（空 = 无）。</summary>
    [ObservableProperty]
    public partial string? LastNotice { get; set; }

    /// <summary>最后一次提示是否为错误。</summary>
    [ObservableProperty]
    public partial bool LastNoticeIsError { get; set; }

    // ---------------------------------------------------------------- 刷新

    /// <summary>按刷新节拍拉取一次（**UI 线程**）：同步模块、刷新数据、更新叠加信息层。</summary>
    /// <param name="includeOverview">
    /// 是否同时刷新「窗口管理」行；默认 <see langword="true"/>。交互页不可见时外壳可传
    /// <see langword="false"/>，页面重新激活时再调用 <see cref="RefreshOverview"/> ——
    /// 模块数据与可视化本身仍每拍刷新，频率不变。
    /// </param>
    public void Refresh(bool includeOverview = true)
    {
        // 网络快照每拍只取一次：结构签名、对端文本与指标共用同一批事实。
        var networks = _query.GetNetworks();

        SyncModules();
        RefreshSubscriptions(networks);
        RefreshOverlayPeers(networks);

        if (includeOverview)
        {
            RefreshOverview();
        }
    }

    /// <summary>重新读取应用级设置快照（启动、设置应用后调用；不在每帧重复深拷贝）。</summary>
    public void RefreshSettings()
    {
        _settings = _query.GetSettings();
        RefreshRateHz = _settings.UiRefreshRateHz;
        SnapDistance = Math.Clamp(_settings.SnapDistance, 2, 8);
        MultilineSubmitKey = _settings.MultilineSubmitKey;
    }

    /// <summary>
    /// 刷新**叠加层开关**与口标记的**对端文本**（仅服务**悬停提示**）。
    /// </summary>
    /// <remarks>
    /// 【来源唯一】叠加层的**连线**改由**角色对**（`GetRolePairs`）驱动，
    /// 不读标记上的"对端方位/是否贴合"（那是同一事实的第二个来源，故不留那三个字段）。
    /// 本方法现在只做两件事：① 把叠加层开关下发到每个模块；② 给悬停提示填一句"对端是谁"。
    /// </remarks>
    private void RefreshOverlayPeers(IReadOnlyList<NetworkView> networks)
    {
        // 开关是标量，必须每拍下发（值相同时生成属性不会重复通知）；它不依赖拓扑结构。
        foreach (var module in Modules)
        {
            module.ShowInfoOverlay = ShowInfoOverlay;
            module.MultilineSubmitKey = MultilineSubmitKey;
        }

        if (!_structureChanged && !_peersDirty)
        {
            return;
        }

        var peers = new Dictionary<string, string>(StringComparer.Ordinal);

        foreach (var network in networks)
        {
            if (network.IsStalled)
            {
                continue;
            }

            // **多源/多宿**：对端文本必须来自 `Sources` / `Targets` 全集，不能只取主源或第一个宿。
            // 悬空的源不作为“对端”展示（它没有绑定）；在场源 = Sources - UnresolvedSources。
            var presentSources = network.Sources
                .Where(source => !network.UnresolvedSources.Contains(source, StringComparer.Ordinal))
                .ToList();
            var sourceText = DescribePeers(presentSources);
            var targetText = DescribePeers(network.Targets);

            if (sourceText.Length > 0)
            {
                foreach (var target in network.Targets)
                {
                    peers[target] = sourceText;
                }
            }

            if (targetText.Length > 0)
            {
                foreach (var source in presentSources)
                {
                    peers[source] = targetText;
                }
            }
        }

        foreach (var module in Modules)
        {
            foreach (var marker in module.Ports)
            {
                _ = peers.TryGetValue($"{marker.InstanceId}.{marker.PortName}", out var peer);
                marker.PeerText = peer;
            }
        }

        _peersDirty = false;
    }

    /// <summary>
    /// 对端列表的悬停文本：1 个直接显示、2 个用 `、`、更多显示 `首个 等 N 个`（不把长列表塞进提示）。
    /// </summary>
    /// <param name="members">对端口字面量（已按网络成员过滤）。</param>
    /// <returns>提示文本；空列表 ⇒ 空串。</returns>
    private static string DescribePeers(IReadOnlyList<string> members) => members.Count switch
    {
        0 => string.Empty,
        1 => members[0],
        2 => $"{members[0]}、{members[1]}",
        _ => TextCatalog.Get(typeof(ModuleWindowsViewModel).Assembly, "windows.peers.more", members[0], members.Count),
    };

    /// <summary>
    /// **按当前叠放顺序与几何重新派生模块层号**；返回是否有模块的层发生变化。
    /// </summary>
    /// <remarks>
    /// 层号只用于吸附/成组过滤，不落盘；<see cref="ZOrderProvider"/> 为空时（无头测试/设计期）不规划。
    /// 外壳作为最底实体参与重合判断，但不属于任何“同层”模块组。
    /// </remarks>
    public bool RefreshLayers()
    {
        if (ZOrderProvider is not { } zOrder)
        {
            return false;
        }

        var modules = Modules
            .Where(module => module.IsViewVisible)
            .Select(module => (module.InstanceId, module.PhysicalRect))
            .ToList();

        var layers = ModuleLayerPlanner.Plan(modules, ShellPhysicalRect, zOrder);
        var changed = false;

        foreach (var module in Modules)
        {
            var layer = module.IsViewVisible && layers.TryGetValue(module.InstanceId, out var resolved)
                ? resolved
                : 0;

            if (module.Layer == layer)
            {
                continue;
            }

            module.Layer = layer;
            changed = true;
        }

        return changed;
    }

    /// <summary>
    /// **类型颜色改了** ⇒ 让所有端口标记重读类型画刷（数据结构 → 颜色映射表）。
    /// </summary>
    /// <remarks>
    /// 色板（<c>PacketTypePalette</c>）是**绘制期唯一色彩来源**：
    /// <list type="bullet">
    ///   <item>端口标记的窄色条走<b>绑定</b> ⇒ 必须显式通知（<c>RefreshTypeBrush</c>）；</item>
    ///   <item>叠加层的角色方块在 <c>Render</c> 里**当场取色** ⇒ 只要重绘即可；叠加层可见时由共享时钟持续重绘，
    ///     关闭时无需重绘（看不见）。</item>
    /// </list>
    /// </remarks>
    public void RefreshTypeColors()
    {
        foreach (var module in Modules)
        {
            foreach (var marker in module.Ports)
            {
                marker.RefreshTypeBrush();
            }
        }
    }

    /// <summary>切换某窗口的锁定（锁定 = 不可拖动/缩放，仍显示数据；**选中保留**，组合范围可包含它）。</summary>
    /// <param name="instanceId">实例 ID。</param>
    public void ToggleLock(string instanceId)
    {
        if (Find(instanceId) is not { } module)
        {
            return;
        }

        module.IsLocked = !module.IsLocked;

        // 锁定只影响拖动/缩放；选中框与选择集保持。手柄是否显示由视图按 IsLocked 判。
    }

    /// <summary>锁定 / 解锁**全部**窗口（工具栏「窗口」组）。</summary>
    /// <param name="locked">是否锁定。</param>
    public void SetAllLocked(bool locked)
    {
        foreach (var module in Modules)
        {
            module.IsLocked = locked;
        }

        AllLocked = locked;
        // 锁定不改变选择集（组合范围仍可包含锁定模块）。
    }

    /// <summary>切换某模块的**运行 / 停用**（停用 = 保留实例、拓扑与配置，只停流）。</summary>
    /// <param name="instanceId">实例 ID。</param>
    public void ToggleEnabled(string instanceId)
    {
        if (Find(instanceId) is not { } module)
        {
            return;
        }

        _ = _command.SetModuleEnabled(instanceId, !module.IsEnabled);
    }

    /// <summary>隐藏某个模块窗口（模块继续运行，只收起表现层）。</summary>
    /// <param name="instanceId">实例 ID。</param>
    public void HideWindow(string instanceId)
    {
        if (Find(instanceId) is not { } module)
        {
            return;
        }

        module.IsViewVisible = false;
        _ = _command.UpdateModuleLayout(module.ToLayout());
        VisibilityChanged?.Invoke(this, new ModuleWindowVisibilityChange(instanceId, IsVisible: false));
        RefreshOverview();
    }

    /// <summary>显示全部被隐藏的模块窗口（不再逐个激活，避免焦点在窗口间跳）。</summary>
    public void ShowAllWindows()
    {
        foreach (var module in Modules)
        {
            if (module.IsViewVisible)
            {
                continue;
            }

            module.IsViewVisible = true;
            _ = _command.UpdateModuleLayout(module.ToLayout());
            VisibilityChanged?.Invoke(this, new ModuleWindowVisibilityChange(module.InstanceId, IsVisible: true));
        }

        RefreshOverview();
    }

    /// <summary>重新显示某个模块窗口（窗口管理区的"显示"）。</summary>
    /// <param name="instanceId">实例 ID。</param>
    public void ShowWindow(string instanceId)
    {
        if (Find(instanceId) is not { } module)
        {
            return;
        }

        module.IsViewVisible = true;
        _ = _command.UpdateModuleLayout(module.ToLayout());
        VisibilityChanged?.Invoke(this, new ModuleWindowVisibilityChange(instanceId, IsVisible: true));
        RefreshOverview();
        WindowActivationRequested?.Invoke(this, instanceId);
    }

    /// <summary>显示并置于应用内最前（窗口管理区的"置顶"；不是系统级 Topmost）。</summary>
    /// <param name="instanceId">实例 ID。</param>
    public void RaiseModule(string instanceId)
    {
        if (Find(instanceId) is not { } module)
        {
            return;
        }

        if (!module.IsViewVisible)
        {
            ShowWindow(instanceId);
            return;
        }

        WindowActivationRequested?.Invoke(this, instanceId);
    }

    /// <summary>把屏幕外的模块召回最近屏幕的可见区域，然后显示并抬前。</summary>
    /// <param name="instanceId">实例 ID。</param>
    public void RecallModule(string instanceId)
    {
        if (Find(instanceId) is not { } module || !module.Rect.IsValid || WorkAreas.Count == 0)
        {
            ShowWindow(instanceId);
            return;
        }

        var target = WorkAreas
            .OrderBy(area => DistanceSquared(module.Rect, area))
            .First();

        var width = Math.Min(module.Width, target.Width);
        var height = Math.Min(module.Height, target.Height);
        var x = Math.Clamp(module.X, target.Left, Math.Max(target.Left, target.Right - width));
        var y = Math.Clamp(module.Y, target.Top, Math.Max(target.Top, target.Bottom - height));

        module.X = x;
        module.Y = y;
        module.Width = width;
        module.Height = height;
        module.HasLayout = true;
        module.IsOffScreen = false;

        _ = _command.UpdateModuleLayout(module.ToLayout());
        RaiseGeometry(module);
        ShowWindow(instanceId);
    }

    /// <summary>由窗口管理器发布全部屏幕工作区（DIP）。</summary>
    /// <param name="areas">各屏工作区；空 = 未知（不判屏幕外）。</param>
    public void SetWorkAreas(IEnumerable<CanvasRect> areas)
    {
        WorkAreas = [.. areas.Where(area => area.IsValid)];
        RefreshOverview();
    }

    /// <summary>由窗口管理器回写某模块是否有宿主窗口。</summary>
    /// <param name="instanceId">实例 ID。</param>
    /// <param name="hasWindow">是否已有窗口。</param>
    public void SetModuleWindowPresence(string instanceId, bool hasWindow)
    {
        if (Find(instanceId) is { } module)
        {
            module.HasWindow = hasWindow;
        }
    }

    /// <summary>窗口管理列表是否为空。</summary>
    public bool OverviewIsEmpty => Overview.Count == 0;

    /// <summary>模块总览摘要（模块数 / 隐藏数 / 屏幕外数）。</summary>
    public string OverviewSummary
    {
        get
        {
            var hidden = Modules.Count(module => !module.IsViewVisible);
            var offScreen = Modules.Count(module => module.IsOffScreen && module.IsViewVisible);
            return TextCatalog.Get(typeof(ModuleWindowsViewModel).Assembly, "windows.overview.summary", Modules.Count, hidden, offScreen);
        }
    }

    /// <summary>按当前事实增量刷新窗口管理列表（不重建行对象）。</summary>
    public void RefreshOverview()
    {
        var changed = false;

        foreach (var module in Modules)
        {
            var wasOffScreen = module.IsOffScreen;
            module.IsOffScreen = !IsModuleOnScreen(module.Rect);
            changed |= wasOffScreen != module.IsOffScreen;

            if (!_overviewById.TryGetValue(module.InstanceId, out var row))
            {
                row = new ModuleOverviewRowViewModel(
                    module.InstanceId,
                    ShowWindow,
                    HideWindow,
                    RaiseModule,
                    RecallModule,
                    ToggleLock,
                    CloseModule);
                Overview.Add(row);
                _overviewById[module.InstanceId] = row;
                changed = true;
            }

            var rowChanged = !string.Equals(row.Title, module.Title, StringComparison.Ordinal)
                || row.State != module.State
                || row.IsViewVisible != module.IsViewVisible
                || row.IsLocked != module.IsLocked
                || row.IsPlaceholder != module.IsPlaceholder
                || row.IsOffScreen != module.IsOffScreen
                || row.HasWindow != module.HasWindow;

            if (!rowChanged)
            {
                continue;
            }

            row.Update(
                module.Title,
                BuildOverviewStatus(module),
                module.State,
                module.IsViewVisible,
                module.IsLocked,
                module.IsPlaceholder,
                module.IsOffScreen,
                module.HasWindow);
            changed = true;
        }

        // 行数应当与模块数一致；只有不一致（直接调用本方法、尚未经过 SyncModules）才需要集合对账。
        if (Overview.Count != Modules.Count)
        {
            var live = new HashSet<string>(Modules.Select(module => module.InstanceId), StringComparer.Ordinal);
            for (var index = Overview.Count - 1; index >= 0; index--)
            {
                var row = Overview[index];
                if (live.Contains(row.InstanceId))
                {
                    continue;
                }

                _ = _overviewById.Remove(row.InstanceId);
                Overview.RemoveAt(index);
                changed = true;
            }
        }

        if (!changed)
        {
            return;
        }

        OnPropertyChanged(nameof(OverviewSummary));
        OnPropertyChanged(nameof(OverviewIsEmpty));
    }

    private static string BuildOverviewStatus(ModuleViewViewModel module)
    {
        var parts = new List<string>(3);
        if (!module.IsViewVisible)
        {
            parts.Add(TextCatalog.Get(typeof(ModuleWindowsViewModel).Assembly, "windows.overview.hidden"));
        }

        if (module.IsOffScreen)
        {
            parts.Add(TextCatalog.Get(typeof(ModuleWindowsViewModel).Assembly, "windows.overview.offScreen"));
        }

        if (!module.HasWindow && module.IsViewVisible)
        {
            parts.Add(TextCatalog.Get(typeof(ModuleWindowsViewModel).Assembly, "windows.overview.noWindow"));
        }

        parts.Add(module.StateText);
        return string.Join(" · ", parts);
    }

    private bool IsModuleOnScreen(CanvasRect rect)
    {
        if (!rect.IsValid || WorkAreas.Count == 0)
        {
            return true;
        }

        var needWidth = Math.Min(32, rect.Width);
        var needHeight = Math.Min(32, rect.Height);

        foreach (var area in WorkAreas)
        {
            var overlapWidth = Math.Min(area.Right, rect.Right) - Math.Max(area.Left, rect.Left);
            var overlapHeight = Math.Min(area.Bottom, rect.Bottom) - Math.Max(area.Top, rect.Top);
            if (overlapWidth >= needWidth && overlapHeight >= needHeight)
            {
                return true;
            }
        }

        return false;
    }

    private static double DistanceSquared(CanvasRect left, CanvasRect right)
    {
        var dx = (left.Left + (left.Width / 2)) - (right.Left + (right.Width / 2));
        var dy = (left.Top + (left.Height / 2)) - (right.Top + (right.Height / 2));
        return (dx * dx) + (dy * dy);
    }

    /// <summary>关闭某个模块窗口 = **移除该模块实例**（级联清理绑定）；只从选择集移除它本身。</summary>
    /// <param name="instanceId">实例 ID。</param>
    public void CloseModule(string instanceId)
    {
        RemoveFromSelection(instanceId);
        _ = _command.RemoveModule(instanceId);
    }

    // ---------------------------------------------------------------- 拖动

    /// <summary>
    /// 开始拖动（**此刻**求贴合连通分量并冻结）。锁定或不可见时不进入拖动。
    /// </summary>
    /// <param name="instanceId">被拖模块的实例 ID。</param>
    /// <param name="pointerX">指针 X（屏幕坐标）。</param>
    /// <param name="pointerY">指针 Y（屏幕坐标）。</param>
    /// <param name="independent">是否独立拖动（Ctrl 或只动自己）。</param>
    /// <returns>是否进入拖动。</returns>
    public bool BeginDrag(string instanceId, double pointerX, double pointerY, bool independent)
    {
        if (Find(instanceId) is not { IsLocked: false } module)
        {
            return false;
        }

        RefreshLayers();

        _draggingId = instanceId;
        module.IsInteracting = true;

        // **锁定成员不随组移动**：编组时就把锁定的成员排除，
        // 于是拖动整组时它们留在原地（组自然"解开"）—— 这比"拖完再纠正"更直观。
        // **外壳也在这个组里**（单侧吸附完成即同组）：
        // 拖动一个贴在外壳上的模块时，外壳窗口要跟着一起走（"视觉整体"）；见 _shellDragPxX/Y 与 ShellMoveRequested。
        _dragGroup = independent
            ? [instanceId]
            : [.. GroupOfNode(instanceId).Where(id => Find(id) is not { IsLocked: true })];

        // 外壳是跨层锚点：拖一个贴壳组时，外壳上其他层的贴壳组也必须一起走，否则会脱钩。
        if (_dragGroup.Contains(ShellNodeId, StringComparer.Ordinal))
        {
            var expanded = _dragGroup.ToList();

            foreach (var memberId in GroupOfNode(ShellNodeId))
            {
                if (Find(memberId) is not { IsLocked: false }
                    || expanded.Contains(memberId, StringComparer.Ordinal))
                {
                    continue;
                }

                expanded.Add(memberId);
            }

            _dragGroup = expanded;
        }

        // 拖动**起点**的几何（拖动中按 Ctrl 拆分时，把已经被带走的其他成员**放回原位** —— 见 DetachDrag）
        // **整数物理像素矩形是整组位移的唯一基准**：整组每帧从起点 + 同一个整数位移重算，
        // 不逐帧累加小数位移，窗口的独立取整就不会把公共边拉开。
        _dragStartRects.Clear();
        _dragStartPixels.Clear();
        _dragStartLayouts.Clear();
        _dragStartDragRects.Clear();
        _dragAnchorIndex = 0;
        _dragCommittedLive = false;
        _lastGroupDelta = null;

        var dragScale = module.Scaling > 0 ? module.Scaling : 1;
        foreach (var memberId in _dragGroup)
        {
            if (Find(memberId) is { } member)
            {
                _dragStartRects[memberId] = member.Rect;
                _dragStartPixels[memberId] = member.PixelRect;
                _dragStartLayouts[memberId] = member.ToLayout();
                _dragStartDragRects.Add(ToDragDip(member.PhysicalRect, dragScale));

                if (string.Equals(memberId, instanceId, StringComparison.Ordinal))
                {
                    _dragAnchorIndex = _dragStartDragRects.Count - 1;
                }
            }
        }

        _dragObstacles = BuildDragObstacles(dragScale, module.Layer);

        _dragStartPointer = new PixelPoint(ScreenPixels.ToPixel(pointerX), ScreenPixels.ToPixel(pointerY));

        _shellDragPxX = 0;
        _shellDragPxY = 0;
        _shellDragOrigin = ShellWindowPosition
            ?? (ShellPhysicalRect is { IsValid: true } shellPhysical
                ? new PixelPoint(ScreenPixels.ToPixel(shellPhysical.X), ScreenPixels.ToPixel(shellPhysical.Y))
                : null);
        _shellDragStartFrame = ShellPhysicalRect is { IsValid: true } startFrame ? startFrame : null;

        // 组内成员**也由指针驱动**：
        // 否则 `ModuleWindowManager.OnWindowMoved` 会把窗口取整后的位置回写到它们的模型上
        // （被拖的那个模块被跳过、其余成员被逐个取整）⇒ 组内相对位置在拖动中被破坏、落盘带 0.5px 误差。
        foreach (var memberId in _dragGroup)
        {
            if (Find(memberId) is { } member)
            {
                member.IsInteracting = true;
            }
        }
        // 拖动期间其他模块不动 ⇒ 参照线/端口锚点只算一次（不再每次指针事件全量重建）。
        _dragLines = CollectLines(instanceId);
        _dragAnchors = BuildPortAnchors(instanceId);
        _snapMemory = default;
        CancelDockPreviewRetry();
        _lastDockPreviewTicks = 0;
        _lastDockPreviewCount = -1;

        SelectOrKeep(instanceId);
        return true;
    }

    /// <summary>拖动中**按下 Ctrl 立刻拆分**：只拖被按下的那个模块，其余成员回到拖动起点。</summary>
    /// <remarks>
    /// 拖动期间按下 Ctrl 立刻断开更符合常见操作习惯（不必在**拖动前**按住才生效）。
    /// <list type="bullet">
    ///   <item>**语义等价于"从一开始就按住 Ctrl"**：其余成员（含外壳）**回到本次拖动的起点**，被拖模块继续跟着指针；</item>
    ///   <item>**单向**：拆分后即使松开 Ctrl 也不重新合组（组在拖动开始时冻结）；</item>
    ///   <item>幂等：已经独立拖动时不做任何事（拖动中每帧都会调用）。</item>
    /// </list>
    /// </remarks>
    public void DetachDrag()
    {
        if (_draggingId is not { Length: > 0 } draggingId || Find(draggingId) is not { } dragged)
        {
            return;
        }

        if (_dragGroup.Count <= 1 && _dragGroup.Contains(draggingId))
        {
            return;   // 已经是独立拖动
        }

        // ① 外壳已经跟着走了 → 回到**本次拖动的绝对物理起点**（模型 + 真实窗口）
        RestoreShellOrigin();

        // ② 其余成员回到拖动起点（被拖的那个不动：它由指针驱动，下一帧 DragTo 会继续带着它）
        foreach (var memberId in _dragGroup)
        {
            if (string.Equals(memberId, draggingId, StringComparison.Ordinal) || Find(memberId) is not { } member)
            {
                continue;
            }

            member.IsInteracting = false;
            member.Guides = [];

            if (_dragStartRects.TryGetValue(memberId, out var start))
            {
                member.X = start.X;
                member.Y = start.Y;
                RaiseGeometry(member);
            }
        }

        if (_dragCommittedLive)
        {
            // 断线判定已经提交过中间布局：把“被拖成员当前位置 + 被放回成员原位置”再同步一次，
            // 否则拆分后的成员会停在中间布局上。
            var layouts = _dragGroup
                .Select(Find)
                .Where(m => m is not null)
                .Select(m => m!.ToLayout())
                .ToList();

            _ = _command.UpdateModuleLayouts(layouts);
            _dragCommittedLive = false;
        }

        // ③ 组收缩为"只有被拖的那个"。
        // **保留被拖成员的原始起点**（DIP / 整数像素 / 手势起点指针）：下一帧 DragTo 仍从
        // "手势起点 + 指针位移"重算，拆分瞬间不会跳。其余成员的起点随之丢弃。
        var draggedStart = _dragAnchorIndex >= 0 && _dragAnchorIndex < _dragStartDragRects.Count
            ? _dragStartDragRects[_dragAnchorIndex]
            : ToDragDip(dragged.PhysicalRect, dragged.Scaling > 0 ? dragged.Scaling : 1);
        _dragGroup = [draggingId];
        dragged.IsInteracting = true;
        _dragStartDragRects.Clear();
        _dragStartDragRects.Add(draggedStart);
        _dragAnchorIndex = 0;
        foreach (var memberId in _dragStartPixels.Keys
                     .Where(id => !string.Equals(id, draggingId, StringComparison.Ordinal))
                     .ToList())
        {
            _ = _dragStartPixels.Remove(memberId);
        }

        _lastGroupDelta = null;
        _dragObstacles = BuildDragObstacles(dragged.Scaling > 0 ? dragged.Scaling : 1, dragged.Layer);

        // 组变小 ⇒ 被放回原位的成员重新成为参照线/端口锚点。
        _dragLines = CollectLines(draggingId);
        _dragAnchors = BuildPortAnchors(draggingId);
        _snapMemory = default;
    }

    /// <summary>
    /// 把**相对拖动起点的绝对位移**施加到外壳（仅当本次拖动组包含外壳），并请求窗口管理器移动真实窗口。
    /// </summary>
    /// <remarks>
    /// <para>
    /// **不能累加**：调用方传入的是 `手势起点 → 当前目标` 的绝对整数位移；外壳目标 = 起点 + 该位移。
    /// 若写成 `+=`，第二帧就会把绝对位移当增量再加一次，外壳会越拖越快直至飞出屏幕。
    /// </para>
    /// <para>
    /// **可见外框不能由 `Window.Position` 构造**：后者是外层窗口左上角，与 DWM 可见外框相差系统装饰边框。
    /// 这里以拖动起点的可见外框为基准平移，保留两者的偏移；真实值随后由 `ModuleWindowManager` 回推校正。
    /// </para>
    /// </remarks>
    private void ApplyShellDelta(int offsetXPixels, int offsetYPixels)
    {
        if (!_dragGroup.Contains(ShellNodeId) || _shellDragOrigin is not { } origin)
        {
            return;
        }

        _shellDragPxX = offsetXPixels;
        _shellDragPxY = offsetYPixels;

        // **绝对物理目标**：起点 + 整数位移；位移本身就是整像素，不再二次取整。
        var target = new PixelPoint(
            origin.X + offsetXPixels,
            origin.Y + offsetYPixels);

        ShellMoveRequested?.Invoke(this, target);
        SetShellWindowPosition(target);

        if (_shellDragStartFrame is not { IsValid: true } start)
        {
            return;
        }

        // 尺寸优先取最近一次真实探测（跨屏缩放时可能变）；原点只按同一位移平移。
        var current = ShellPhysicalRect is { IsValid: true } live ? live : start;
        var frame = new CanvasRect(
            start.X + offsetXPixels,
            start.Y + offsetYPixels,
            current.Width,
            current.Height);

        SetShellFrame(ScreenGeometry.ToDip(frame, ShellScaling), frame);
    }

    /// <summary>把外壳恢复到本次拖动的物理起点（Ctrl 拆分 / 取消拖动）。</summary>
    private void RestoreShellOrigin()
    {
        if (_shellDragOrigin is not { } origin
            || (_shellDragPxX == 0 && _shellDragPxY == 0))
        {
            return;
        }

        ShellMoveRequested?.Invoke(this, origin);
        SetShellWindowPosition(origin);

        if (_shellDragStartFrame is { IsValid: true } frame)
        {
            SetShellFrame(ScreenGeometry.ToDip(frame, ShellScaling), frame);
        }

        _shellDragPxX = 0;
        _shellDragPxY = 0;
    }

    /// <summary>
    /// **外壳移动 = 平移"与外壳拼成一体的那一组"**。
    /// </summary>
    /// <remarks>
    /// <list type="bullet">
    ///   <item><b>外壳是拼图里的一个节点</b>（不是"应用的框架"）：它与模块之间只有**单侧吸附**（只提供右边缘）；
    ///     模块贴着它就算拼上，于是它们同属一个**接触连通分量**；</item>
    ///   <item><b>只有吸附完成后才整体运动</b>：移动外壳 → 只平移与它相连的那一组（**没拼上的模块留在原处**）；
    ///     与"拖动一个模块会带走整组"是**同一套成组逻辑**，只是把外壳也纳入进来；</item>
    ///   <item>之所以不用"所有模块无条件跟随"：那会让与外壳毫无关系的模块也被拖走，不符合"拼装"的直觉；</item>
    ///   <item><b>锁定</b> = 钉在桌面上：既不在组内被带走，也不随外壳移动；</item>
    ///   <item><b>不逐帧落盘</b>：只改内存并记录待提交，由调用方在移动停下后调
    ///     <see cref="CommitSceneTranslation"/> 合并成一次变更（逐帧提交会触发建网重算并让外壳拖动卡顿）；</item>
    ///   <item>位置仍存**绝对屏幕坐标**（组合件格式不变）。</item>
    /// </list>
    /// </remarks>
    /// <param name="dxPixels">外壳位移 X（**物理像素**）。</param>
    /// <param name="dyPixels">外壳位移 Y（**物理像素**）。</param>
    /// <param name="preMoveShellPixels">
    /// **移动前**的外壳物理矩形：分组必须用它判定"谁与外壳拼在一起"。
    /// </param>
    /// <remarks>
    /// <b>为什么必须传"移动前"的矩形</b>：调用方通常已把外壳刷新成移动后的位置，
    /// 而此时模块还在旧位置上 —— 若用新矩形判分组，"贴上"会被判成"没贴上"，于是**模块不跟随**
    /// （同一类错误：**任何"谁和谁相连"的判定都必须用同一时刻的几何**）。
    /// </remarks>
    public void TranslateShellGroup(double dxPixels, double dyPixels, CanvasRect preMoveShellPixels)
    {
        if ((Math.Abs(dxPixels) < 0.5 && Math.Abs(dyPixels) < 0.5) || !preMoveShellPixels.IsValid)
        {
            return;
        }

        // **本次拖动开始时**就与外壳拼在一起的那些模块（冻结集合）：
        // 单独拖动主窗口时不做吸附更符合直觉：若**逐帧**重算分组，拖着外壳**经过**某个模块时，
        // 那一帧它就会被判成"已拼上"并跟着走
        // ⇒ 现象正是"经过模块时导致模块**错位粘连**（没有产生吸附行为却拼上去了）"。
        // ⇒ 冻结集合只在**一次拖动开始时**算一次（用移动前的外壳矩形），拖动过程中**不吸收新成员**。
        _shellDragGroup ??= GroupOfNode(ShellNodeId, preMoveShellPixels);

        foreach (var module in Modules)
        {
            if (!_shellDragGroup.Contains(module.InstanceId) || module.IsLocked || !module.Rect.IsValid)
            {
                continue;
            }

            var moduleScale = module.Scaling > 0 ? module.Scaling : 1;
            module.X += dxPixels / moduleScale;
            module.Y += dyPixels / moduleScale;
            RaiseGeometry(module);
            _ = _pendingSceneCommits.Add(module.InstanceId);
        }
    }

    /// <summary>
    /// **外壳拖动结束**：清掉冻结的分组（下一次拖动重新按"当前拼合情况"取组）。
    /// </summary>
    /// <remarks>由 `ModuleWindowManager` 在外壳停止移动后调用（静默 ~250ms 视为一次拖动结束）。</remarks>
    public void EndShellDragGesture() => _shellDragGroup = null;

    /// <summary>一次外壳拖动期间**冻结**的跟随成员（null = 本次拖动尚未取组）。</summary>
    private HashSet<string>? _shellDragGroup;

    /// <summary>
    /// 把"整体平移"造成的位置变化**合并落盘**（一次 ChangeSet）。
    /// </summary>
    /// <remarks>
    /// 由调用方在外壳停止移动后调用（见 `ModuleWindowManager` 的防抖计时器），避免逐帧提交；
    /// 保存组合件前也应调用一次（否则最后一次移动可能还没落盘）。
    /// </remarks>
    /// <returns>是否真的提交了（false = 没有待提交的平移变化）。</returns>
    public bool CommitSceneTranslation()
    {
        if (_pendingSceneCommits.Count == 0)
        {
            return false;
        }

        var layouts = _pendingSceneCommits
            .Select(Find)
            .Where(m => m is not null)
            .Select(m => m!.ToLayout())
            .ToList();

        _pendingSceneCommits.Clear();

        if (layouts.Count == 0)
        {
            return false;
        }

        _ = _command.UpdateModuleLayouts(layouts);
        return true;
    }

    /// <summary>拖动中：吸附 → 整组平移 → 更新辅助线。</summary>
    /// <param name="pointerX">指针 X（屏幕**物理像素**）。</param>
    /// <param name="pointerY">指针 Y（屏幕**物理像素**）。</param>
    /// <param name="snapEnabled">本次是否吸附（null = 用开关值；按住 Alt 传 false）。</param>
    /// <remarks>
    /// **拖动中实时吸附**（松手才吸附操作不友好）；
    /// 目标手感 = **Visio 式**：吸附半径小（几像素）、离开即松、靠参考线给反馈。
    /// 写入模型前把目标落到**整数物理像素**（不是整 DIP）：125%/150% 下 1 DIP 不是 1 像素，
    /// 按 DIP 取整会让窗口每次跳 1.25/1.5 px，表现为水平拖动的弹簧与细缝。
    /// </remarks>
    public void DragTo(double pointerX, double pointerY, bool? snapEnabled = null)
    {
        if (_draggingId is not { Length: > 0 } draggingId || Find(draggingId) is not { } module)
        {
            return;
        }

        // **从手势起点重算**，不累加上一帧：吸附是"指针原始位置"的纯函数。
        // 指针是物理像素，先按被拖窗口自己的缩放换成 DIP（吸附在 DIP 模型里做）；
        // 量化只在最后落到**整数物理像素**一次，整组共用这个整数位移。
        var dragScale = module.Scaling > 0 ? module.Scaling : 1;
        var startRect = _dragStartRects.TryGetValue(draggingId, out var recordedStart)
            ? recordedStart
            : module.Rect;
        var startPixels = _dragStartPixels.TryGetValue(draggingId, out var recordedPixels)
            ? recordedPixels
            : module.PixelRect;

        // 层是叠放顺序的派生值：每一帧先按当前几何/层序重算；若层变了，拖动期的缓存必须一起换。
        if (RefreshLayers())
        {
            _dragLines = CollectLines(draggingId);
            _dragAnchors = BuildPortAnchors(draggingId);
            _dragObstacles = BuildDragObstacles(dragScale, module.Layer);
        }

        var pointer = new PixelPoint(ScreenPixels.ToPixel(pointerX), ScreenPixels.ToPixel(pointerY));
        var rawDeltaX = pointer.X - _dragStartPointer.X;
        var rawDeltaY = pointer.Y - _dragStartPointer.Y;
        var rawOffsetX = rawDeltaX / dragScale;
        var rawOffsetY = rawDeltaY / dragScale;
        var targetX = startRect.X + rawOffsetX;
        var targetY = startRect.Y + rawOffsetY;

        IReadOnlyList<GuideLine> guides = [];
        var snapping = snapEnabled ?? SnapEnabled;

        if (snapping)
        {
            // 组吸附：候选来自组内**每个成员**，整组共用同一个平移；单模块就是只有一个成员的退化情形。
            var anchorIndex = _dragStartDragRects.Count > 0
                && _dragAnchorIndex >= 0
                && _dragAnchorIndex < _dragStartDragRects.Count
                ? _dragAnchorIndex
                : 0;
            var moving = _dragStartDragRects.Count > 0
                ? _dragStartDragRects.Select(rect => rect.OffsetBy(rawOffsetX, rawOffsetY)).ToList()
                : [new CanvasRect(targetX, targetY, startRect.Width, startRect.Height)];

            var snapped = SnapEngine.SnapGroup(
                moving,
                anchorIndex,
                _dragLines,
                SnapDistance,                 // 对齐类容差 = 设置里的「吸附距离」
                _dragAnchors,
                SnapDistance * 2,             // 贴合类容差 = 2×（拼接是主用途，值得给宽容差）
                obstacles: _dragObstacles,
                memory: _snapMemory);

            targetX = snapped.X;
            targetY = snapped.Y;
            guides = snapped.Guides;
            _snapMemory = new SnapMemory(snapped.XLine, snapped.YLine);
        }
        else
        {
            _snapMemory = default;
        }

        // 量化点：被拖模块的目标先落到**整数物理像素**；整组共用"目标像素 − 起点像素"这个整数位移。
        // 不再用 `X * Scaling`（可能是小数）做增量基准 —— 那会让每个窗口独立取整，公共边被拉开。
        var targetPixels = ScreenPixels.PixelRectOf(
            new CanvasRect(targetX, targetY, startRect.Width, startRect.Height),
            dragScale);
        var groupDelta = new PixelPoint(
            targetPixels.Left - startPixels.Left,
            targetPixels.Top - startPixels.Top);

        // 死区只约束**窗口几何**：整组整数位移没变 ⇒ 不再重复 ApplyPixelOrigin/RaiseGeometry。
        // **预览不能一起跳过**：两轴都被吸附锁住时指针仍可能在动，而 groupDelta 不变；
        // 若这里提前 return，接触那一帧又被 UpdateDockPreview 的 50ms 节流吞掉，端口就永远不亮。
        var moved = _lastGroupDelta != groupDelta;

        if (moved)
        {
            _lastGroupDelta = groupDelta;

            foreach (var memberId in _dragGroup)
            {
                if (Find(memberId) is { } member
                    && _dragStartPixels.TryGetValue(memberId, out var memberStart))
                {
                    member.ApplyPixelOrigin(
                        memberStart.Left + groupDelta.X,
                        memberStart.Top + groupDelta.Y);
                    RaiseGeometry(member);
                }
            }

            // 组内含外壳（模块单侧吸附在外壳上）→ 外壳窗口也一起走（"视觉整体"）
            ApplyShellDelta(groupDelta.X, groupDelta.Y);
        }

        // 窗内绘制用：辅助线挂到**被拖的模块**上（其余成员只跟随位移）
        if (Find(draggingId) is { } dragged)
        {
            dragged.Guides = guides;
        }

        // **即将自动建网**预览：按"接触且对齐"规则问 L4"现在松手会连上什么"，
        // 高亮对应的端口标记 —— 判定规则与真正建网同一份实现，预览必然与结果一致。
        // 即使本帧窗口没动也调用：吸附锁住两轴时，接触状态可能刚刚成立。
        UpdateDockPreview();
    }

    /// <summary>把"将会连上"的端口标记高亮（拖动中实时；松手后由网络行为落地）。</summary>
    /// <summary>当前已建网的 `源 → 宿` 字面量对（预览不能把已有连接再标成“即将建网”）。</summary>
    /// <param name="anonymousOnly">是否只取匿名/直连网络（拖动断线判定只需要这一种）。</param>
    private HashSet<(string Source, string Target)> BuildExistingBindings(bool anonymousOnly = false)
    {
        var existing = new HashSet<(string Source, string Target)>();

        foreach (var network in _query.GetNetworks())
        {
            if (anonymousOnly && !network.IsAnonymous)
            {
                continue;
            }
            IReadOnlyList<string> sources = network.Sources.Count > 0 ? network.Sources : [network.Source];
            foreach (var source in sources)
            {
                if (source.Length == 0)
                {
                    continue;
                }

                foreach (var target in network.Targets)
                {
                    _ = existing.Add((source, target));
                }
            }
        }

        return existing;
    }

    private void UpdateDockPreview()
    {
        // Core 的建网预览要遍历全部候选；拖动帧率远高于它需要的刷新率 ⇒ 约 20 Hz + 数量变化才发提示。
        // **节流必须能自恢复**：被推迟的那一次要安排补算，否则接触帧一旦错过就再也不会重算
        //（吸附锁住两轴时 groupDelta 不变，不会再来新的"变化帧"）。
        const int interval = 50;
        var now = Environment.TickCount64;
        var elapsed = now - _lastDockPreviewTicks;

        if (elapsed < interval)
        {
            _dockPreviewRetry ??= UiFrameClock.ScheduleOnce(
                TimeSpan.FromMilliseconds(interval - elapsed),
                () =>
                {
                    _dockPreviewRetry = null;
                    if (_draggingId is not null)
                    {
                        UpdateDockPreview();
                    }
                });
            return;
        }

        _dockPreviewRetry?.Dispose();
        _dockPreviewRetry = null;
        _lastDockPreviewTicks = now;

        var provisional = _dragGroup
            .Select(Find)
            .Where(m => m is not null)
            .Select(m => m!.ToLayout())
            .ToList();

        var directBefore = BuildExistingBindings(anonymousOnly: true);
        var candidates = _query.PreviewConnections(provisional);
        var candidatePairs = candidates
            .Select(candidate => (Source: candidate.SourceLiteral, Target: candidate.TargetLiteral))
            .ToHashSet();

        // 已有直连在当前临时几何下不再成立 ⇒ 立即走现有布局提交，让 ReconcileNetworks 拆网。
        // 这里不保存任何“预览网络”：布局仍是唯一事实源，PreviewConnections 只负责触发。
        var lost = directBefore.Any(pair =>
            PairTouchesDragGroup(pair)
            && !candidatePairs.Contains(pair));

        if (lost && _command.UpdateModuleLayouts(provisional).Succeeded)
        {
            _dragCommittedLive = true;
        }

        var existing = BuildExistingBindings();
        var filtered = DockPreviewPolicy.Filter(
            candidates,
            existing,
            _dragGroup);

        var marked = new HashSet<string>(StringComparer.Ordinal);

        foreach (var candidate in filtered)
        {
            _ = marked.Add(candidate.SourceLiteral);
            _ = marked.Add(candidate.TargetLiteral);
        }

        foreach (var module in Modules)
        {
            foreach (var marker in module.Ports)
            {
                marker.IsDockCandidate = marked.Contains($"{module.InstanceId}.{marker.PortName}");
            }
        }

        if (filtered.Count > 0 && filtered.Count != _lastDockPreviewCount)
        {
            _lastDockPreviewCount = filtered.Count;
            RaiseNotice(TextCatalog.Get(typeof(ModuleWindowsViewModel).Assembly, "windows.notice.autoNetwork", filtered.Count));
        }
        else if (filtered.Count == 0)
        {
            _lastDockPreviewCount = -1;
        }
    }

    /// <summary>这个直连端口对是否涉及本次拖动组。</summary>
    private bool PairTouchesDragGroup((string Source, string Target) pair) =>
        _dragGroup.Any(instanceId =>
            pair.Source.StartsWith(instanceId + ".", StringComparison.Ordinal)
            || pair.Target.StartsWith(instanceId + ".", StringComparison.Ordinal));

    /// <summary>取消被节流推迟的建网预览补算（拖动开始/结束/取消时调用）。</summary>
    private void CancelDockPreviewRetry()
    {
        _dockPreviewRetry?.Dispose();
        _dockPreviewRetry = null;
    }

    /// <summary>清掉"即将建网"高亮（拖动结束/取消时）。</summary>
    private void ClearDockPreview()
    {
        CancelDockPreviewRetry();
        _lastDockPreviewTicks = 0;
        _lastDockPreviewCount = -1;
        foreach (var module in Modules)
        {
            foreach (var marker in module.Ports)
            {
                marker.IsDockCandidate = false;
            }
        }
    }

    // ---------------------------------------------------------------- 网络标签的"连接对齐"吸附

    /// <summary>
    /// **拖动网络标签时的"连接对齐"吸附**（允许连接对齐式吸附 / 边框高亮）。
    /// </summary>
    /// <param name="module">标记所属模块。</param>
    /// <param name="marker">正在拖的标记。</param>
    /// <param name="edge">标记**当前**所在边（拖动中可能已换边）。</param>
    /// <param name="offset">指针算出来的归一化偏移（未吸附）。</param>
    /// <returns>吸附后的偏移（无候选时原样返回）。</returns>
    /// <remarks>
    /// <list type="bullet">
    ///   <item><b>解决什么</b>：模块**已经拼在一起**、但两个口没对齐（因而没连上）时，
    ///     用户拖动网络标签到附近就能被"吸到对齐位置"，并高亮两侧标记 ⇒ 松手即连上；</item>
    ///   <item><b>判据在 Core</b>（<see cref="IRuntimeQuery.SuggestAnchorDock"/>，复用 `DockRule`）：
    ///     本方法**只做呈现**（赋偏移 + 高亮 + 提示），所以"能吸上" ⟺ "松手真的连上"；</item>
    ///   <item>每帧先清旧高亮再重算（拖动中换边/移开都要跟着变）；提示**只在换了对象时发一次**
    ///     （每帧发会把状态栏刷屏）。</item>
    /// </list>
    /// </remarks>
    public double SnapAnchorToConnection(
        ModuleViewViewModel module,
        PortMarkerViewModel marker,
        PortEdge edge,
        double offset)
    {
        ArgumentNullException.ThrowIfNull(module);
        ArgumentNullException.ThrowIfNull(marker);

        ClearDockPreview();

        var hint = _query.SuggestAnchorDock(module.InstanceId, marker.PortName, edge, offset);

        if (hint is null)
        {
            _anchorDockNoticeKey = null;

            // **网络标签与中线对齐**：
            // 没有"吸到哪里就能连上"的候选时，退一步做**纯排版**的对齐（吸到模块边缘中点 = 中线）。
            // 优先级明确：**连接对齐 > 中线对齐**（能连上的位置永远优先于"好看的位置"）。
            return SnapAnchorToCenterLine(module, marker, edge, offset);
        }

        HighlightMarker(hint.InstanceId, hint.PortName);
        HighlightMarker(hint.PeerInstanceId, hint.PeerPortName);

        var key = $"{hint.PeerInstanceId}.{hint.PeerPortName}";

        if (!string.Equals(_anchorDockNoticeKey, key, StringComparison.Ordinal))
        {
            _anchorDockNoticeKey = key;
            RaiseNotice(TextCatalog.Get(typeof(ModuleWindowsViewModel).Assembly, "windows.notice.aligned", key));
        }

        return hint.Offset;
    }

    /// <summary>
    /// 把标签吸到**所在边的中线**（= 归一化偏移 0.5）——"网络标签与中线对齐"能力的落点。
    /// </summary>
    /// <param name="module">标签所属模块（提供该边方向的长度）。</param>
    /// <param name="marker">正在拖的标签（提供"是否已经在线上"的判断依据）。</param>
    /// <param name="edge">目标边。</param>
    /// <param name="offset">指针算出来的归一化偏移。</param>
    /// <returns>吸附后的偏移。</returns>
    /// <remarks>
    /// 容差取设置里的**「吸附距离」**（与模块拖动吸附同一个偏好）；提示**只在真的吸上去时发一次**
    /// （每帧发会把状态栏刷屏，与连接对齐同一条纪律）。
    /// </remarks>
    private double SnapAnchorToCenterLine(
        ModuleViewViewModel module,
        PortMarkerViewModel marker,
        PortEdge edge,
        double offset)
    {
        var extent = PortEdgeGeometry.IsVertical(edge) ? module.Height : module.Width;
        var snapped = AnchorSnap.ToEdgeCenter(offset, extent, SnapDistance);

        var atCenter = AnchorSnap.IsAtCenter(snapped);

        if (atCenter && !AnchorSnap.IsAtCenter(marker.Offset))
        {
            if (!string.Equals(_anchorCenterNoticeKey, marker.PortName, StringComparison.Ordinal))
            {
                _anchorCenterNoticeKey = marker.PortName;
                RaiseNotice(TextCatalog.Get(typeof(ModuleWindowsViewModel).Assembly, "windows.notice.centerAligned", marker.PortName));
            }
        }
        else if (!atCenter)
        {
            _anchorCenterNoticeKey = null;
        }

        return snapped;
    }

    /// <summary>结束网络标签拖动：清掉"连接对齐"高亮与提示去重状态。</summary>
    public void EndAnchorDockPreview()
    {
        ClearDockPreview();
        _anchorDockNoticeKey = null;
        _anchorCenterNoticeKey = null;
    }

    /// <summary>上一次提示过的"连接对齐"对端（null = 当前没有）——避免每帧刷状态栏。</summary>
    private string? _anchorDockNoticeKey;

    /// <summary>上一次提示过的"已与中线对齐"的口名（同上，只为不刷屏）。</summary>
    private string? _anchorCenterNoticeKey;

    /// <summary>把某个口标成"即将建网"（找不到就静默跳过：拖动中对方可能刚好被移除）。</summary>
    private void HighlightMarker(string instanceId, string portName)
    {
        foreach (var module in Modules)
        {
            if (!string.Equals(module.InstanceId, instanceId, StringComparison.Ordinal))
            {
                continue;
            }

            foreach (var marker in module.Ports)
            {
                if (string.Equals(marker.PortName, portName, StringComparison.Ordinal))
                {
                    marker.IsDockCandidate = true;
                }
            }
        }
    }

    /// <summary>结束拖动：**一次 ChangeSet** 提交整组位置（原子），随后清辅助线。</summary>
    public void EndDrag()
    {
        if (_draggingId is null)
        {
            return;
        }

        var layouts = _dragGroup
            .Select(Find)
            .Where(m => m is not null)
            .Select(m => m!.ToLayout())
            .ToList();

        foreach (var memberId in _dragGroup)
        {
            if (Find(memberId) is { } member)
            {
                member.IsInteracting = false;
                member.Guides = [];
            }
        }

        _draggingId = null;
        _dragGroup = [];
        _dragStartRects.Clear();
        _dragStartPixels.Clear();
        _dragStartLayouts.Clear();
        _dragStartDragRects.Clear();
        _dragAnchorIndex = 0;
        _dragObstacles = [];
        _lastGroupDelta = null;
        _dragCommittedLive = false;
        ClearDockPreview();

        if (layouts.Count > 0)
        {
            _ = _command.UpdateModuleLayouts(layouts);
        }

        RefreshLayers();
    }

    /// <summary>缩放手柄对应的边位标志（1 左 · 2 右 · 4 上 · 8 下）。</summary>
    private static int EdgesOf(ResizeHandle handle) => handle switch
    {
        ResizeHandle.Left => 1,
        ResizeHandle.Right => 2,
        ResizeHandle.Top => 4,
        ResizeHandle.Bottom => 8,
        ResizeHandle.TopLeft => 1 | 4,
        ResizeHandle.TopRight => 2 | 4,
        ResizeHandle.BottomLeft => 1 | 8,
        _ => 2 | 8,
    };

    /// <summary>该手柄是否改变水平尺寸。</summary>
    private static bool MovesHorizontally(ResizeHandle handle) =>
        handle is ResizeHandle.Left or ResizeHandle.Right
            or ResizeHandle.TopLeft or ResizeHandle.TopRight
            or ResizeHandle.BottomLeft or ResizeHandle.BottomRight;

    /// <summary>该手柄是否改变竖直尺寸。</summary>
    private static bool MovesVertically(ResizeHandle handle) =>
        handle is ResizeHandle.Top or ResizeHandle.Bottom
            or ResizeHandle.TopLeft or ResizeHandle.TopRight
            or ResizeHandle.BottomLeft or ResizeHandle.BottomRight;

    /// <summary>取消拖动（Esc）：回到提交前的几何（重新从布局读回）。</summary>
    public void CancelDrag()
    {
        if (_draggingId is null)
        {
            return;
        }

        var group = _dragGroup;
        _draggingId = null;
        _dragGroup = [];
        _lastGroupDelta = null;

        // 组内含外壳时，外壳窗口也被拖走了 → 还原到本次拖动的绝对物理起点
        RestoreShellOrigin();

        foreach (var memberId in group)
        {
            if (Find(memberId) is not { } member)
            {
                continue;
            }

            member.IsInteracting = false;

            if (_dragStartLayouts.TryGetValue(memberId, out var layout))
            {
                member.ApplyLayout(layout);
            }

            RaiseGeometry(member);
        }

        if (_dragCommittedLive)
        {
            // 断线判定已经提交过中间布局：取消要把布局和网络一起还原到拖动前。
            var layouts = group
                .Where(_dragStartLayouts.ContainsKey)
                .Select(id => _dragStartLayouts[id])
                .ToList();

            if (layouts.Count > 0)
            {
                _ = _command.UpdateModuleLayouts(layouts);
            }

            _dragCommittedLive = false;
        }

        _dragStartRects.Clear();
        _dragStartPixels.Clear();
        _dragStartLayouts.Clear();
        _dragStartDragRects.Clear();
        _dragAnchorIndex = 0;
        _dragObstacles = [];
        CancelDockPreviewRetry();
        RefreshLayers();
    }

    // ---------------------------------------------------------------- 缩放

    /// <summary>开始缩放（边框的边中点/角点手柄）。锁定或不可见时不进入缩放。</summary>
    /// <param name="instanceId">实例 ID。</param>
    /// <param name="handle">手柄。</param>
    /// <returns>是否进入缩放。</returns>
    public bool BeginResize(string instanceId, ResizeHandle handle)
    {
        if (Find(instanceId) is not { IsLocked: false } module)
        {
            return false;
        }

        _resizingId = instanceId;
        _resizeHandle = handle;
        _resizeStartRect = module.Rect;
        module.IsInteracting = true;
        module.ResizeEdges = EdgesOf(handle);
        Select(instanceId);
        return true;
    }

    /// <summary>
    /// 缩放中：按手柄移动对应边（指针位置为**屏幕坐标**）。
    /// </summary>
    /// <param name="pointerX">指针 X（屏幕坐标）。</param>
    /// <param name="pointerY">指针 Y（屏幕坐标）。</param>
    public void ResizeTo(double pointerX, double pointerY)
    {
        if (_resizingId is not { Length: > 0 } resizingId || Find(resizingId) is not { } module)
        {
            return;
        }

        var start = _resizeStartRect;

        // 指针是物理像素；缩放的起点/尺寸都是 DIP ⇒ 按本窗口缩放换回来。
        var resizeScale = module.Scaling > 0 ? module.Scaling : 1;
        pointerX /= resizeScale;
        pointerY /= resizeScale;

        // 缩放约束来自**本模块的代码声明**：
        // 下限 = max(框架下限, 模块声明的 min)，上限 = 模块声明的 max（无声明 ⇒ 无上限）。
        // **四个数都是 DIP**（与模型同单位）：本次缩放的指针位置由调用方按该窗口的缩放换成 DIP
        // （见 `ModuleWindow.ScreenPoint` 的调用点），模型与带宽因此不必再乘缩放。
        var minWidth = module.EffectiveMinWidth;
        var minHeight = module.EffectiveMinHeight;
        var maxWidth = module.EffectiveMaxWidth;
        var maxHeight = module.EffectiveMaxHeight;

        var left = start.Left;
        var top = start.Top;
        var right = start.Right;
        var bottom = start.Bottom;

        if (_resizeHandle is ResizeHandle.Left or ResizeHandle.TopLeft or ResizeHandle.BottomLeft)
        {
            left = Math.Min(pointerX, right - minWidth);
            if (maxWidth is { } widthLimit)
            {
                left = Math.Max(left, right - widthLimit);
            }
        }

        if (_resizeHandle is ResizeHandle.Right or ResizeHandle.TopRight or ResizeHandle.BottomRight)
        {
            right = Math.Max(pointerX, left + minWidth);
            if (maxWidth is { } widthLimit)
            {
                right = Math.Min(right, left + widthLimit);
            }
        }

        if (_resizeHandle is ResizeHandle.Top or ResizeHandle.TopLeft or ResizeHandle.TopRight)
        {
            top = Math.Min(pointerY, bottom - minHeight);
            if (maxHeight is { } heightLimit)
            {
                top = Math.Max(top, bottom - heightLimit);
            }
        }

        if (_resizeHandle is ResizeHandle.Bottom or ResizeHandle.BottomLeft or ResizeHandle.BottomRight)
        {
            bottom = Math.Max(pointerY, top + minHeight);
            if (maxHeight is { } heightLimit)
            {
                bottom = Math.Min(bottom, top + heightLimit);
            }
        }

        // **尺寸吸附**（Visio 式）：拖动边时，若结果尺寸接近其他模块的宽/高 → 吸附到该尺寸，
        // 这样"把几个模块调成一样大"很容易；只吸附尺寸，不改变边的方向语义。
        if (SnapEnabled)
        {
            foreach (var other in Modules.Where(m => m.IsViewVisible && !string.Equals(m.InstanceId, resizingId, StringComparison.Ordinal)))
            {
                if (right > left && Math.Abs((right - left) - other.Width) <= GroupTolerance && MovesHorizontally(_resizeHandle))
                {
                    if (_resizeHandle is ResizeHandle.Left or ResizeHandle.TopLeft or ResizeHandle.BottomLeft)
                    {
                        left = right - other.Width;
                    }
                    else
                    {
                        right = left + other.Width;
                    }
                }

                if (bottom > top && Math.Abs((bottom - top) - other.Height) <= GroupTolerance && MovesVertically(_resizeHandle))
                {
                    if (_resizeHandle is ResizeHandle.Top or ResizeHandle.TopLeft or ResizeHandle.TopRight)
                    {
                        top = bottom - other.Height;
                    }
                    else
                    {
                        bottom = top + other.Height;
                    }
                }
            }
        }

        // 最终尺寸再夹一次（吸附可能把尺寸拉到别的模块的宽/高上；json 的上限必须说话算数）
        var width = Math.Max(minWidth, right - left);
        var height = Math.Max(minHeight, bottom - top);
        if (maxWidth is { } widthCap)
        {
            width = Math.Min(width, widthCap);
        }

        if (maxHeight is { } heightCap)
        {
            height = Math.Min(height, heightCap);
        }

        // **四个数都是 DIP、都不取整**：DIP 不是像素，取整只会在缩放 ≠100% 时让"贴齐"差几个物理像素
        // （物理像素的取整只发生在窗口边界：`ModuleWindowManager` 落位时乘 `RenderScaling` 那一步）。
        module.X = left;
        module.Y = top;
        module.Width = width;
        module.Height = height;
        RaiseGeometry(module);
        RefreshLayers();
    }

    /// <summary>结束缩放：一次 ChangeSet 提交几何。</summary>
    public void EndResize()
    {
        if (_resizingId is not { Length: > 0 } resizingId || Find(resizingId) is not { } module)
        {
            _resizingId = null;
            return;
        }

        _resizingId = null;
        module.IsInteracting = false;
        module.ResizeEdges = 0;
        _ = _command.UpdateModuleLayout(module.ToLayout());
        RefreshLayers();
    }

    /// <summary>把尺寸夹进"有效区间"（**两侧同为 DIP** ⇒ 不涉及缩放）。</summary>
    /// <param name="dip">待夹的值（DIP）。</param>
    /// <param name="minDip">有效最小（DIP）。</param>
    /// <param name="maxDip">有效最大（DIP；<see langword="null"/> = 无上限）。</param>
    private static double ClampToBounds(double dip, double minDip, double? maxDip)
    {
        var value = Math.Max(dip, minDip);
        return maxDip is { } upper ? Math.Min(value, upper) : value;
    }

    // ---------------------------------------------------------------- 端口标签（建网）

    /// <summary>提交端口网络标签（**唯一建网机制**：同名即连）。</summary>
    /// <param name="module">所属模块视图。</param>
    /// <param name="marker">端口标记。</param>
    /// <returns>结果文本（成功/失败原因）。</returns>
    /// <remarks>
    /// 失败时**保持编辑框打开**并把原因显示在框内：无论成功失败都先关框的话，
    /// 校验失败（如名字含空白）时用户只看到"框关了、什么都没变" → 误判为"改标签不生效"。
    /// </remarks>
    public string CommitPortLabel(ModuleViewViewModel module, PortMarkerViewModel marker)
    {
        ArgumentNullException.ThrowIfNull(module);
        ArgumentNullException.ThrowIfNull(marker);

        var text = marker.EditText.Trim();

        var result = _command.UpdatePortNetwork(module.InstanceId, marker.PortName, text.Length == 0 ? null : text);

        string message;

        if (result.Succeeded)
        {
            marker.IsEditing = false;
            marker.EditError = null;
            marker.Label = text.Length == 0 ? null : text;
            marker.EditText = marker.Label ?? string.Empty;

            var notices = result.Notices.Count > 0 ? $" —— {string.Join("；", result.Notices)}" : string.Empty;
            message = text.Length == 0
                ? TextCatalog.Get(typeof(ModuleWindowsViewModel).Assembly, "windows.notice.networkCleared", module.Title, marker.PortName) + notices
                : TextCatalog.Get(typeof(ModuleWindowsViewModel).Assembly, "windows.notice.networkSet", module.Title, marker.PortName, text) + notices;
        }
        else
        {
            // 保持打开：让用户就地改（不清空输入，不丢上下文）
            marker.IsEditing = true;
            message = TextCatalog.Get(typeof(ModuleWindowsViewModel).Assembly, "windows.notice.networkFailed", module.Title, marker.PortName, string.Join("；", result.Failures));
            marker.EditError = string.Join("；", result.Failures);
        }

        // `marker.EditResult`（悬停里显示"上次操作"）**不保留**：
        //   ① 悬停提示不该携带"最近一次操作结果"这类**瞬时状态**；② 该数据本身也不保存（见 `PortMarkerViewModel`）。
        //   结果文本仍然**返回并送到状态栏**（`RaiseNotice`）—— 反馈闭环不丢，只是不挂在标记上。

        // **反馈闭环**：提示送到状态栏（返回值被调用方丢弃的话，结果就完全看不到）
        RaiseNotice(message, isError: !result.Succeeded);
        return message;
    }

    /// <summary>取消端口标签编辑（Esc）。</summary>
    /// <param name="marker">端口标记。</param>
    public static void CancelPortLabel(PortMarkerViewModel marker)
    {
        ArgumentNullException.ThrowIfNull(marker);
        marker.EditText = marker.Label ?? string.Empty;
        marker.IsEditing = false;
    }

    // ---------------------------------------------------------------- 端口锚点

    /// <summary>
    /// **提交端口锚点几何**（拖动松手时调用）：边缘 + 偏移 + 固定标志**一次写全**。
    /// </summary>
    /// <param name="module">所属模块视图。</param>
    /// <param name="marker">端口标记（其 <c>Edge</c>/<c>Offset</c>/<c>IsAnchorFixed</c> 已是**拖动后的目标值**）。</param>
    /// <param name="fromEdge">拖动前的边缘（失败时回滚用）。</param>
    /// <param name="fromOffset">拖动前的偏移（失败时回滚用）。</param>
    /// <returns>结果文本（成功/失败原因）。</returns>
    /// <remarks>
    /// <list type="bullet">
    ///   <item>**为什么一次写全**：`WorkspaceLayout.SetPort` 是**整条替换**（不是字段合并）⇒ 只写偏移会把
    ///     标签、固定标志、默认边一起抹掉。故这里从标记读回全套字段，语义上**必须**如此（踩过：组合件里
    ///     `Label` 为 null 就清掉了网络名）。</item>
    ///   <item>失败时**就地回滚**到拖动前的位置（不像标签编辑可以"保持打开让用户改"—— 拖动没有输入框），
    ///     并把原因送到状态栏，用户不会看到"标记跳到了新位置但重启后又回去了"。</item>
    /// </list>
    /// </remarks>
    public string MovePortAnchor(
        ModuleViewViewModel module,
        PortMarkerViewModel marker,
        PortEdge fromEdge,
        double fromOffset)
    {
        ArgumentNullException.ThrowIfNull(module);
        ArgumentNullException.ThrowIfNull(marker);

        // 拖动不改「固定」态 ⇒ 回滚目标就是当前值
        var ok = TryWriteAnchorGeometry(
            module,
            marker,
            fromEdge,
            fromOffset,
            marker.IsAnchorFixed,
            out var failure,
            out var notices);

        var message = ok
            ? TextCatalog.Get(typeof(ModuleWindowsViewModel).Assembly, "windows.notice.anchorMoved", module.Title, marker.PortName, EdgeText(marker.Edge), marker.Offset.ToString("0.00", CultureInfo.CurrentCulture))
                + (notices.Length > 0 ? $" —— {notices}" : string.Empty)
            : TextCatalog.Get(typeof(ModuleWindowsViewModel).Assembly, "windows.notice.anchorMoveFailed", module.Title, marker.PortName, failure);

        RaiseNotice(message, isError: !ok);
        return message;
    }

    /// <summary>
    /// **端口锚点几何的唯一写入路径**（拖动落位、单口固定/解锁、全局批量都走它）。
    /// </summary>
    /// <param name="module">所属模块视图。</param>
    /// <param name="marker">端口标记（其当前 <c>Edge</c>/<c>Offset</c>/<c>IsAnchorFixed</c> 即**目标值**）。</param>
    /// <param name="fromEdge">写之前的值（失败时回滚）。</param>
    /// <param name="fromOffset">写之前的值（失败时回滚）。</param>
    /// <param name="fromFixed">写之前的「固定」态（失败时回滚）。</param>
    /// <param name="failure">失败原因（成功时为空串）。</param>
    /// <param name="notices">框架的附带说明（`CommandResult.Notices` 拼成的一段文本；无则空串）。</param>
    /// <returns>是否写入成功。</returns>
    /// <remarks>
    /// <list type="bullet">
    ///   <item><b>为什么一次写全</b>：`WorkspaceLayout.SetPort` 是**整条替换**（不是字段合并）⇒ 只写一个字段会把
    ///     标签、固定标志、边、偏移一起抹掉。故这里从标记读回全套字段（踩过：`Label` 传 null 就清掉了网络名）。</item>
    ///   <item><b>为什么失败要回滚</b>：本地呈现**绝不能**停留在"写不进去"的状态 ——
    ///     否则用户看到标记跳到了新位置，重启后又回去了（比当场拒绝更糟）。</item>
    ///   <item><b>发提示不在这里</b>：批量切换要发一条汇总 ⇒ 报告职责归调用方。</item>
    /// </list>
    /// </remarks>
    private bool TryWriteAnchorGeometry(
        ModuleViewViewModel module,
        PortMarkerViewModel marker,
        PortEdge fromEdge,
        double fromOffset,
        bool fromFixed,
        out string failure,
        out string notices)
    {
        var geometry = new PortGeometryView(
            marker.PortName,
            marker.Offset,
            marker.IsAnchorFixed,
            marker.Label,
            marker.Edge);

        var result = _command.UpdatePortGeometry(module.InstanceId, geometry);

        if (result.Succeeded)
        {
            failure = string.Empty;
            notices = result.Notices.Count > 0 ? string.Join("；", result.Notices) : string.Empty;
            return true;
        }

        marker.Edge = fromEdge;
        marker.Offset = fromOffset;
        marker.IsAnchorFixed = fromFixed;
        failure = string.Join("；", result.Failures);
        notices = string.Empty;
        return false;
    }

    /// <summary>
    /// **固定 / 解锁单个端口锚点**（快捷键之外的**操作**入口）。
    /// </summary>
    /// <param name="module">所属模块视图。</param>
    /// <param name="marker">端口标记。</param>
    /// <param name="fixedState">目标状态（true = 固定）。</param>
    /// <returns>结果文本（送状态栏）。</returns>
    /// <remarks>
    /// 与 <see cref="MovePortAnchor"/> **共用同一条纪律**：整条几何一次写全（`SetPort` 是整条替换）、
    /// 失败就地回滚（本地呈现绝不停留在"写不进去"的状态）。
    /// 它是 <see cref="ToggleAllAnchorsFixed"/>（全局批量）的**单口版**；两者都不改"模块声明"，
    /// 只写**用户覆盖**。
    /// </remarks>
    public string SetPortAnchorFixed(ModuleViewViewModel module, PortMarkerViewModel marker, bool fixedState)
    {
        ArgumentNullException.ThrowIfNull(module);
        ArgumentNullException.ThrowIfNull(marker);

        if (marker.IsAnchorFixed == fixedState)
        {
            return string.Empty;   // 已经是目标状态：不产生无意义的写与提示
        }

        var ok = TryWriteAnchorFixed(module, marker, fixedState, out var failure);
        var message = ok
            ? fixedState
                ? TextCatalog.Get(typeof(ModuleWindowsViewModel).Assembly, "windows.notice.anchorFixed", module.Title, marker.PortName)
                : TextCatalog.Get(typeof(ModuleWindowsViewModel).Assembly, "windows.notice.anchorUnlocked", module.Title, marker.PortName)
            : TextCatalog.Get(typeof(ModuleWindowsViewModel).Assembly, "windows.notice.anchorToggleFailed", module.Title, marker.PortName, failure);

        RaiseNotice(message, isError: !ok);
        return message;
    }

    /// <summary>**只改「固定」态**：把目标值写在标记上，再走通用写入（回滚目标 = 取反）。</summary>
    /// <param name="module">所属模块视图。</param>
    /// <param name="marker">端口标记。</param>
    /// <param name="fixedState">目标状态。</param>
    /// <param name="failure">失败原因（成功时为空串）。</param>
    /// <returns>是否写入成功。</returns>
    private bool TryWriteAnchorFixed(
        ModuleViewViewModel module,
        PortMarkerViewModel marker,
        bool fixedState,
        out string failure)
    {
        var fromEdge = marker.Edge;
        var fromOffset = marker.Offset;
        marker.IsAnchorFixed = fixedState;
        return TryWriteAnchorGeometry(module, marker, fromEdge, fromOffset, !fixedState, out failure, out _);
    }

    /// <summary>
    /// **全局切换端口锚点「固定 / 解锁」**。
    /// </summary>
    /// <returns>结果文本（送状态栏）。</returns>
    /// <remarks>
    /// <list type="bullet">
    ///   <item>**全局**（全部模块的全部端口**统一**取反）：与"全部锁定 / 解锁"模块窗口同一口味，
    ///     单口固定留给模块声明（`PortDeclaration.AnchorDraggable = false`）；</item>
    ///   <item>取反基准 = **当前所有口是否都已固定**：全固定 ⇒ 解锁全部，否则 ⇒ 固定全部
    ///     （否则混状态下用户按一次只翻转了一半，第二次才齐 —— 反直觉）。</item>
    /// </list>
    /// </remarks>
    public string ToggleAllAnchorsFixed()
    {
        var markers = Modules.SelectMany(m => m.Ports).ToList();

        if (markers.Count == 0)
        {
            var empty = TextCatalog.Get(typeof(ModuleWindowsViewModel).Assembly, "windows.anchor.none");
            RaiseNotice(empty);
            return empty;
        }

        // 全固定 ⇒ 这次是"解锁"；否则（含混合态）⇒ 这次是"固定"
        var target = !markers.All(m => m.IsAnchorFixed);
        var changed = 0;
        var failed = 0;

        foreach (var module in Modules)
        {
            foreach (var marker in module.Ports)
            {
                if (marker.IsAnchorFixed == target)
                {
                    continue;
                }

                if (TryWriteAnchorFixed(module, marker, target, out _))
                {
                    changed++;
                }
                else
                {
                    failed++;
                }
            }
        }

        var text = target
            ? TextCatalog.Get(typeof(ModuleWindowsViewModel).Assembly, "windows.anchor.fixed", changed)
                + (failed > 0 ? TextCatalog.Get(typeof(ModuleWindowsViewModel).Assembly, "windows.anchor.failed", failed) : string.Empty)
            : TextCatalog.Get(typeof(ModuleWindowsViewModel).Assembly, "windows.anchor.unlocked", changed)
                + (failed > 0 ? TextCatalog.Get(typeof(ModuleWindowsViewModel).Assembly, "windows.anchor.failed", failed) : string.Empty);

        RaiseNotice(text, isError: failed > 0);
        return text;
    }

    /// <summary>边缘的界面用语。</summary>
    /// <param name="edge">边缘。</param>
    private static string EdgeText(PortEdge edge) => edge switch
    {
        PortEdge.Right => TextCatalog.Get(typeof(ModuleWindowsViewModel).Assembly, "windows.edge.right"),
        PortEdge.Top => TextCatalog.Get(typeof(ModuleWindowsViewModel).Assembly, "windows.edge.top"),
        PortEdge.Bottom => TextCatalog.Get(typeof(ModuleWindowsViewModel).Assembly, "windows.edge.bottom"),
        PortEdge.Left => TextCatalog.Get(typeof(ModuleWindowsViewModel).Assembly, "windows.edge.left"),
        _ => TextCatalog.Get(typeof(ModuleWindowsViewModel).Assembly, "windows.edge.default"),
    };

    /// <summary>设置/清除某模块的壁纸（只影响该模块）。</summary>
    /// <param name="module">模块视图。</param>
    /// <param name="png">PNG 字节；null = 清除。</param>
    public void SetWallpaper(ModuleViewViewModel module, byte[]? png)
    {
        ArgumentNullException.ThrowIfNull(module);

        var result = _command.SetModuleWallpaper(module.InstanceId, png);
        if (result.Succeeded)
        {
            module.ReloadWallpaper();
            RaiseNotice(png is { Length: > 0 }
                ? TextCatalog.Get(typeof(ModuleWindowsViewModel).Assembly, "windows.wallpaper.changed")
                : TextCatalog.Get(typeof(ModuleWindowsViewModel).Assembly, "windows.wallpaper.cleared"));
            return;
        }

        RaiseNotice(TextCatalog.Get(typeof(ModuleWindowsViewModel).Assembly, "windows.wallpaper.failed", string.Join("；", result.Failures)), isError: true);
    }

    /// <summary>
    /// **还原模块**：回到实例化默认尺寸，可选连配置一起还原。
    /// </summary>
    /// <remarks>
    /// 默认尺寸 = **视图根的 `InitialWidth/InitialHeight`（含外框的实际窗口尺寸）**；
    /// 位置不变（只还原尺寸与配置，不动用户摆放的位置）。
    /// 还原值再夹进该模块的**运行期最小/最大**区间；初始尺寸与最小尺寸是两个独立事实。
    /// </remarks>
    /// <param name="module">模块视图。</param>
    /// <param name="scope">还原范围（只尺寸 / 尺寸与配置）。</param>
    public void ApplyReset(ModuleViewViewModel module, ModuleResetScope scope)
    {
        ArgumentNullException.ThrowIfNull(module);

        // ① 尺寸回到视图声明的初始尺寸，并写回布局（**不是**回到最小尺寸）。
        module.Width = ClampToBounds(module.StartupWindowWidth, module.EffectiveMinWidth, module.EffectiveMaxWidth);
        module.Height = ClampToBounds(module.StartupWindowHeight, module.EffectiveMinHeight, module.EffectiveMaxHeight);
        module.HasLayout = true;

        _ = _command.UpdateModuleLayout(module.ToLayout());
        RaiseGeometry(module);

        // ② 配置（可选）：清掉该实例已保存的配置，模块重新读到默认值
        if (scope == ModuleResetScope.SizeAndConfiguration)
        {
            var result = _command.ResetModuleConfiguration(module.InstanceId);

            RaiseNotice(
                result.Succeeded
                    ? TextCatalog.Get(typeof(ModuleWindowsViewModel).Assembly, "windows.reset.done", module.Title)
                    : TextCatalog.Get(typeof(ModuleWindowsViewModel).Assembly, "windows.reset.configFailed", module.Title, string.Join("；", result.Failures)),
                isError: !result.Succeeded);
        }
        else
        {
            RaiseNotice(TextCatalog.Get(typeof(ModuleWindowsViewModel).Assembly, "windows.reset.sizeDone", module.Title), isError: false);
        }
    }

    /// <summary>
    /// 窗口**打开后**把实际尺寸落盘（未摆放过的模块由此获得第一条布局记录）。
    /// </summary>
    /// <remarks>
    /// 尺寸默认值归**模块代码声明**：这里只把窗口的**实际尺寸**写进模型与布局，
    /// 不做任何"按内容估算"。之后用户的拖动/缩放照常覆盖。
    /// </remarks>
    /// <param name="module">模块视图。</param>
    public void CommitInitialLayout(ModuleViewViewModel module)
    {
        ArgumentNullException.ThrowIfNull(module);
        _ = _command.UpdateModuleLayout(module.ToLayout());
    }

    // ---------------------------------------------------------------- 内部

    private ModuleViewViewModel? Find(string instanceId) =>
        Modules.FirstOrDefault(m => string.Equals(m.InstanceId, instanceId, StringComparison.Ordinal));

    /// <summary>几何比较的容差（DIP）：与位置回写用的同一个 0.5。</summary>
    private const double GeometryEpsilon = 0.5;

    /// <summary>
    /// 布局回声与内存模型**是不是真的有差**（差 &lt; <see cref="GeometryEpsilon"/> 视为没有）。
    /// </summary>
    /// <remarks>
    /// "没有差就不动"是"首帧即终值"的必要条件：几何只该因为**真实变化**（用户动作、真实 DPI 变化）
    /// 被重新应用，不该因为"读回了一份数值上等价的记录"再应用一次。
    /// </remarks>
    private static bool HasGeometryDifference(ModuleViewViewModel module, ModuleLayoutView layout) =>
        Math.Abs(layout.X - module.X) >= GeometryEpsilon
        || Math.Abs(layout.Y - module.Y) >= GeometryEpsilon
        || Math.Abs(layout.Width - module.Width) >= GeometryEpsilon
        || Math.Abs(layout.Height - module.Height) >= GeometryEpsilon
        || layout.IsViewVisible != module.IsViewVisible;

    private void RaiseGeometry(ModuleViewViewModel module) =>
        GeometryChanged?.Invoke(
            this,
            new ModuleWindowGeometryChange(module.InstanceId, module.X, module.Y, module.Width, module.Height));

    /// <summary>
    /// 新模块的摆放位置（物理像素）：**从外壳右侧开始、竖着一列排开，互不堆叠**。
    /// </summary>
    /// <remarks>
    /// 新模块若都落在左上角（`48,48` 起、每 40px 斜叠）⇒ **压在主窗口上、彼此重叠**。
    /// 故规则（纯几何、可单测）：
    /// <list type="number">
    ///   <item>起点 X = **外壳右边缘 + 间距**（外壳不可用时回退到 <see cref="PlaceOrigin"/>），起点 Y = 外壳上边缘（同回退）；</item>
    ///   <item>**已有的模块**逐个检查：与候选矩形相交就让位（向下挪一个"模块高 + 间距"）；</item>
    ///   <item>一列到底（超出工作区下沿）就**换到右面一列**（X 挪"模块宽 + 间距"，Y 回到起点）；
    ///     工作区不可用时不换列（无限向下，仍保证不堆叠）。</item>
    /// </list>
    /// 尺寸用**视图根 XAML 的窗口 MinWidth/MinHeight（含外框）**（由调用方先加载视图得到）。
    /// 有循环上限兜底，极端情况下宁可重叠也不死循环。
    /// </remarks>
    /// <summary>
    /// 按给定尺寸找空闲摆放位置（DIP）：**按现有模块的真实下沿 + 间距**向下让位，
    /// 不再按“候选模块声明高度”整块跳过（旧实现会在矮模块之间留下过大的 Y 空隙）。
    /// </summary>
    private (double X, double Y) NextPlacementForSize(double width, double height)
    {
        var shell = ShellRect is { IsValid: true } rect ? rect : (CanvasRect?)null;
        var baseX = shell is { } s ? s.Right + PlaceGap : PlaceOrigin;
        var baseY = shell is { } s2 ? s2.Y : PlaceOrigin;
        var work = WorkArea is { IsValid: true } area ? area : (CanvasRect?)null;

        // 外壳靠近工作区下沿时，按外壳上沿起排会把模块放到屏幕外；先夹到还能容纳一行的高度。
        if (work is { } workArea && baseY + height > workArea.Bottom)
        {
            baseY = Math.Max(workArea.Top, workArea.Bottom - height);
        }

        var x = baseX;
        var y = baseY;

        for (var guard = 0; guard < 256; guard++)
        {
            if (work is { } area2 && y + height > area2.Bottom)
            {
                // 起始行本身就放不下 ⇒ 换列也不会变好；继续加 X 只会在屏幕上横向跑出巨大间隔。
                if (y <= baseY + 0.5)
                {
                    return (x, y);
                }

                // 本列已到底 → 换右边一列。列宽不能只按“内容最小宽”估：XAML 声明的是含外框的实际窗口宽，
                // 若按内容宽步进，实际只剩不到 PlaceGap 的缝，会被碰撞判定当成仍重叠，进而多跳一列。
                x = CurrentColumnRight(x, width) + PlaceGap;
                y = baseY;
            }

            if (LowestOverlappingBottom(x, y, width, height) is not { } bottom)
            {
                return (x, y);
            }

            // **贴住最低障碍物的下沿 + 呼吸间距**：不按候选高度整块跳，避免过大空档。
            y = bottom + PlaceGap;
        }

        return (x, y);
    }

    /// <summary>
    /// 当前列实际占用的右边界：取候选宽与**本列已有模块真实宽度**的较大者。
    /// </summary>
    /// <param name="columnLeft">列左边界（DIP）。</param>
    /// <param name="nominalWidth">候选模块声明宽度（DIP）。</param>
    /// <returns>本列右边界（DIP）。</returns>
    private double CurrentColumnRight(double columnLeft, double nominalWidth)
    {
        var right = columnLeft + Math.Max(1, nominalWidth);

        foreach (var module in Modules)
        {
            var moduleWidth = module.Width > 0 ? module.Width : module.StartupWindowWidth;
            if (moduleWidth <= 0)
            {
                continue;
            }

            var moduleRight = module.X + moduleWidth;

            // 只吸收与本列横向相交的模块；右侧后续列不参与本次列宽。
            if (module.X < right + PlaceGap && moduleRight > right)
            {
                right = moduleRight;
            }
        }

        return right;
    }

    /// <summary>候选矩形与现有模块相交时，返回最低的相交下沿；不相交 = null。</summary>
    private double? LowestOverlappingBottom(double x, double y, double width, double height)
    {
        double? lowest = null;
        var right = x + width;
        var bottom = y + height;

        foreach (var module in Modules)
        {
            var moduleWidth = module.Width > 0 ? module.Width : module.StartupWindowWidth;
            var moduleHeight = module.Height > 0 ? module.Height : module.StartupWindowHeight;
            if (moduleWidth <= 0 || moduleHeight <= 0)
            {
                continue;
            }

            var moduleRight = module.X + moduleWidth;
            var moduleBottom = module.Y + moduleHeight;
            if (x < moduleRight + PlaceGap && right + PlaceGap > module.X
                && y < moduleBottom + PlaceGap && bottom + PlaceGap > module.Y)
            {
                lowest = lowest is { } current ? Math.Max(current, moduleBottom) : moduleBottom;
            }
        }

        return lowest;
    }

    /// <summary>候选矩形是否与**已有模块**相交（留 <see cref="PlaceGap"/> 的呼吸间距）。全程 DIP。</summary>
    private bool OverlapsAnyModule(double x, double y, double width, double height)
    {
        var right = x + width;
        var bottom = y + height;

        foreach (var module in Modules)
        {
            if (module.Width <= 0 || module.Height <= 0)
            {
                // 尺寸还没读回来（窗口未打开）→ 用视图声明的初始尺寸估（同为 DIP）
                var w = module.StartupWindowWidth;
                var h = module.StartupWindowHeight;
                if (x < module.X + w + PlaceGap && right + PlaceGap > module.X
                    && y < module.Y + h + PlaceGap && bottom + PlaceGap > module.Y)
                {
                    return true;
                }

                continue;
            }

            if (x < module.Rect.Right + PlaceGap && right + PlaceGap > module.Rect.Left
                && y < module.Rect.Bottom + PlaceGap && bottom + PlaceGap > module.Rect.Top)
            {
                return true;
            }
        }

        return false;
    }

    private void SyncModules()
    {
        var instances = _query.GetInstances();
        var modulesChanged = false;

        // 本帧的实例视图（结构签名与"要不要重跑同步"都以它的引用为准）。
        _lastInstances = instances;

        // **加载组合时布局必须先于窗口创建**：新窗口若先按 NextPlacement 建、再应用布局，
        // ModuleWindowManager 会在 Opened 时用初摆位置写回布局，覆盖刚恢复的位置。
        _layoutById.Clear();
        foreach (var layout in _query.GetLayout())
        {
            _layoutById[layout.InstanceId] = layout;
        }

        // 先把已存在的实例就地更新，并收集本拍新出现的实例；只有真正的新实例才做组级落位判断。
        _seenInstances.Clear();
        _incomingInstances.Clear();

        foreach (var instance in instances)
        {
            _ = _seenInstances.Add(instance.InstanceId);

            if (Find(instance.InstanceId) is { } existing)
            {
                existing.Apply(instance);
                continue;
            }

            _incomingInstances.Add(instance);
        }

        // **组合件整组落位**：文件里的位置是组内相对关系。现场非空时，若整组与现有模块相交，
        // 就像单个新模块一样找一块空地，再整体平移；空现场则保留文件里的绝对位置。
        // 平移量写回布局，避免下一次刷新又按文件原位置覆盖。
        if (Modules.Count > 0 && _incomingInstances.Count > 0)
        {
            var incomingLayouts = _incomingInstances
                .Select(instance => _layoutById.TryGetValue(instance.InstanceId, out var layout) ? layout : null)
                .ToList();

            if (incomingLayouts.All(layout => layout is not null))
            {
                var minX = incomingLayouts.Min(layout => layout!.X);
                var minY = incomingLayouts.Min(layout => layout!.Y);
                var maxX = incomingLayouts.Max(layout => layout!.X + layout.Width);
                var maxY = incomingLayouts.Max(layout => layout!.Y + layout.Height);
                var width = Math.Max(1, maxX - minX);
                var height = Math.Max(1, maxY - minY);

                if (OverlapsAnyModule(minX, minY, width, height))
                {
                    var placement = NextPlacementForSize(width, height);
                    var dx = placement.X - minX;
                    var dy = placement.Y - minY;
                    var shifted = incomingLayouts
                        .Select(layout => layout! with { X = layout.X + dx, Y = layout.Y + dy })
                        .ToList();

                    if (_command.UpdateModuleLayouts(shifted).Succeeded)
                    {
                        foreach (var layout in shifted)
                        {
                            _layoutById[layout.InstanceId] = layout;
                        }
                    }
                }
            }
        }

        foreach (var instance in _incomingInstances)
        {
            // 有已保存布局 ⇒ 直接消费；只有真正的新实例才使用错位摆放。
            _ = _layoutById.TryGetValue(instance.InstanceId, out var savedLayout);
            var created = new ModuleViewViewModel(
                instance,
                layout: savedLayout,
                _query,
                _command,
                fallbackX: 0,
                fallbackY: 0,
                _viewState,
                _colorPicker)
            {
                WallpaperVeilOpacity = WallpaperVeilOpacity,
            };

            // 新实例的尺寸来自视图根 XAML 的初始尺寸（InitialWidth/InitialHeight）；
            // 先构造视图拿到尺寸，再决定摆放，最后才创建窗口。
            if (savedLayout is null)
            {
                if (created.EnsureView()?.Content is Control content)
                {
                    created.SetWindowSize(content);
                }

                created.Width = created.StartupWindowWidth;
                created.Height = created.StartupWindowHeight;
                var placement = NextPlacementForSize(
                    created.StartupWindowWidth,
                    created.StartupWindowHeight);
                created.X = placement.X;
                created.Y = placement.Y;
            }

            Modules.Add(created);
            modulesChanged = true;

            // 组合页已经初始化过选择 ⇒ 新加入的模块默认进入选择集（刚加的通常就是要保存的）。
            if (_selectionInitialized && !_selectedInstanceIds.Contains(created.InstanceId, StringComparer.Ordinal))
            {
                _selectedInstanceIds.Add(created.InstanceId);
            }

            GeometryChanged?.Invoke(
                this,
                new ModuleWindowGeometryChange(created.InstanceId, created.X, created.Y, created.Width, created.Height));

            if (created.IsViewVisible)
            {
                VisibilityChanged?.Invoke(this, new ModuleWindowVisibilityChange(created.InstanceId, IsVisible: true));
            }
        }

        for (var index = Modules.Count - 1; index >= 0; index--)
        {
            var stale = Modules[index];
            if (_seenInstances.Contains(stale.InstanceId))
            {
                continue;
            }

            Modules.RemoveAt(index);
            stale.ReleaseCachedResources();
            _ = _overviewById.Remove(stale.InstanceId);
            modulesChanged = true;
            VisibilityChanged?.Invoke(this, new ModuleWindowVisibilityChange(stale.InstanceId, IsVisible: false));
        }

        // 几何：拖动/缩放中的成员由交互过程主导（避免与指针打架）
        foreach (var module in Modules)
        {
            module.WallpaperVeilOpacity = WallpaperVeilOpacity;

            if (_draggingId is { Length: > 0 } dragging
                && (string.Equals(module.InstanceId, dragging, StringComparison.Ordinal)
                    || _dragGroup.Contains(module.InstanceId, StringComparer.Ordinal)))
            {
                continue;
            }

            if (string.Equals(module.InstanceId, _resizingId, StringComparison.Ordinal))
            {
                continue;
            }

            // **整体平移尚未落盘**的模块同样不能被回写：
            // 刷新是 60Hz 的，若这里用"已落盘的旧布局"覆盖内存位置，模块会被一帧帧拉回原位 ——
            // 表现就是"移动主窗口时模块跟不动"（甚至来回抖）。落盘由防抖合并提交负责。
            if (_pendingSceneCommits.Contains(module.InstanceId))
            {
                continue;
            }

            // **回声不是几何来源**：读回的布局与内存模型**没有真差**时，一个像素都不动。
            // 新模块开窗的那 0.x 秒里，"窗口已按内存模型落位"与"刚写下去的布局回声"之间会带一点
            // 平台取整的小数差 —— 无条件重应用会让窗口尺寸被再写一次、内容跟着重排，
            // 而**由实得尺寸推出的自绘几何（滑块圆钮）就跳 1px**（用户症状："卡一下归位"）。
            if (_layoutById.TryGetValue(module.InstanceId, out var layout)
                && HasGeometryDifference(module, layout))
            {
                module.ApplyLayout(layout);
                RaiseGeometry(module);
            }
        }

        // **选中态对账**：只在模块增删时重算；选择操作本身仍由各选择方法立即对账。
        if (modulesChanged)
        {
            ApplySelection();
        }

        if (Modules.Count != _lastModuleCount)
        {
            _lastModuleCount = Modules.Count;
            OnPropertyChanged(nameof(ModuleCount));
        }
    }

    private void RefreshSubscriptions(IReadOnlyList<NetworkView> networks)
    {
        // 设置只在启动/应用设置时读取；没有做过 Load 的调用点（测试/设计时）在此补一次。
        if (_settings is null)
        {
            RefreshSettings();
        }

        _sources.Clear();
        _frameSources.Clear();

        foreach (var source in _query.GetSubscriptionSources())
        {
            _sources[(source.InstanceId, source.DataSourceName)] = source;
            _frameSources.Add(source);
        }

        // 【结构变了就立刻刷新】**连接状态/停流/角色投影**都以**网络成员关系**为唯一来源（"指标来源唯一"）：
        // 它们不是"每 0.5 秒才追上"的滞后态 —— 建网/断网/改标签/增删模块的**当帧**就刷新，
        // 否则用户刚连上却看不到连接灯与连线（"看得见"是功能的一部分）。
        // 网络与实例视图都按修订号缓存：**引用没变 ⇒ 结构必然没变**，不必每帧重新拼一遍签名。
        // 计数（投递/丢弃/速率）会随数据流动一直变，不参与这里的判据；它们走下面的指标节拍。
        var topologyChanged = !ReferenceEquals(networks, _lastNetworks)
            || !ReferenceEquals(_lastInstances, _instancesForSignature);

        if (topologyChanged)
        {
            _structureChanged = true;
            _peersDirty = true;
            _instancesForSignature = _lastInstances;
        }
        else
        {
            _structureChanged = false;
        }

        _lastNetworks = networks;

        // 队列水位与网络一样每拍只取一次；只有真正刷指标的那一拍才取。
        Dictionary<string, PortQueueDepthView>? queues = null;

        // 同网络名不兼容端口：也是每拍只取一次（Core 侧按修订号缓存）。
        IReadOnlyDictionary<string, string>? conflicts = null;

        // 指标节拍按**时间**而不是帧数：刷新率调低时指标仍按约 0.5 秒更新，不被帧率拖慢。
        var now = Stopwatch.GetTimestamp();
        var metricsDue = _lastMetricsTimestamp == 0
            || Stopwatch.GetElapsedTime(_lastMetricsTimestamp, now) >= MetricsInterval;

        if (metricsDue)
        {
            _lastMetricsTimestamp = now;
        }

        var refreshMetrics = _structureChanged || metricsDue;

        foreach (var module in Modules)
        {
            if (!module.IsViewVisible)
            {
                continue;
            }

            // 显示写入已在模块实例线程直达显示缓冲（唯一写者 = 实例线程）；本帧只刷新视图。

            // 口标记的网络信息（连接状态 + **投递量与估算字节**）与**角色方块**：
            // ① **结构变化时**立即刷一次；② 否则按约 0.5 秒的节拍刷（指标会随数据流动持续变化）
            if (refreshMetrics)
            {
                queues ??= QueueDepthsByPort();
                conflicts ??= BuildContractConflictMap();
                RefreshMarkerStatus(module, networks, queues, conflicts);
                RefreshRoleBoxes(module);
            }

            // **表面每帧刷一次**：取数、就地更新、输入反馈回填都在这一条路里。
            module.RefreshSurface(_frameSources);
        }

        // **角色投影第二遍**：连线的对端锚点要用**对端方块的位置** ⇒ 必须等所有模块的方块都摆好
        if (refreshMetrics)
        {
            foreach (var module in Modules)
            {
                if (module.IsViewVisible)
                {
                    RefreshRoleLinks(module);
                }
            }
        }

    }

    /// <summary>
    /// 刷新某模块**口标记的网络信息**：连接状态、网络名、累计投递/丢弃、字节数与速率。
    /// </summary>
    /// <remarks>
    /// <list type="bullet">
    ///   <item><b>指标一律经唯一口径 <see cref="IRuntimeQuery.GetPortMetrics"/></b>（Core 算、UI 只格式化）：
    ///     一个端口的指标 = **该口参与的"流"上的指标之和**（发送口 = 以它为源的全部绑定；接收口 = 以它为宿的全部绑定）；</item>
    ///   <item><b>为什么不按"逐源诊断表"查</b>：先按<b>网络身份键</b>查、失败再拿
    ///     <b>口字面量</b>去查<b>逐源诊断表</b>（键 = `单源 → 宿子集`，是**源**键）⇒ **宿口永远匹配不上**
    ///     ⇒ 宿口标记的投递/丢弃/速率**静默为 0**（多源网络 / 加载期有未锚定源 / 宿被逐对兼容过滤时必现；
    ///     1:1 单源单宿时两个键恰好相等，所以一直没被发现）。现在"一个口该读哪些流"由框架回答；</item>
    ///   <item>**速率与角色方块同一口径**：都取逐源诊断的搬运字节速率（同一批框架速率计），
    ///     故同一根线两侧不会再显示不同的数（接收侧若走"每口队列"速率 ⇒ 与发送侧可能对不上）。</item>
    /// </list>
    /// </remarks>
    private void RefreshMarkerStatus(
        ModuleViewViewModel module,
        IReadOnlyList<NetworkView> networks,
        Dictionary<string, PortQueueDepthView> queues,
        IReadOnlyDictionary<string, string> conflicts)
    {
        if (module.Ports.Count == 0)
        {
            return;
        }

        foreach (var marker in module.Ports)
        {
            var literal = $"{module.InstanceId}.{marker.PortName}";

            // 同网络名但契约不兼容：与连接状态无关，必须先落（未入网也可能有冲突）。
            if (conflicts.TryGetValue(literal, out var conflictReason))
            {
                marker.ContractConflictReason = conflictReason;
                marker.HasContractConflict = true;
            }
            else
            {
                marker.HasContractConflict = false;
                marker.ContractConflictReason = null;
            }

            NetworkView? network = null;

            foreach (var candidate in networks)
            {
                // **成员判定必须用全部声明源**：`NetworkView.Source` 只是兼容访问器（多源时只代表第一个源），
                // 用它会让第二个及以后的发送端找不到网络 ⇒ 标记恒显示 `---`（关掉一个源后又“恢复”）。
                if (candidate.Sources.Contains(literal, StringComparer.Ordinal)
                    || candidate.Targets.Contains(literal, StringComparer.Ordinal))
                {
                    network = candidate;
                    break;
                }
            }

            if (network is null)
            {
                marker.IsConnected = false;
                marker.NetworkName = null;
                marker.Delivered = 0;
                marker.Dropped = 0;
                marker.BytesText = null;
                marker.RateText = "---";   // 未入网 ⇒ 通路速率无值（不显示上一段的旧数字）
                marker.QueueText = null;   // 未入网 ⇒ 队列压力也清掉（不留上一段的旧值）
                continue;
            }

            marker.IsConnected = true;
            marker.NetworkName = network.IsAnonymous ? null : network.Name;
            marker.IsStalled = network.IsStalled;

            // **框架算、界面只格式化**：不自行差分估算、
            // 也不拿"投递数 × 契约尺寸"自己乘一遍（累计字节由框架给出，同一口径）。
            var metrics = _query.GetPortMetrics(module.InstanceId, marker.PortName);

            marker.Delivered = metrics.Delivered;

            // `Dropped` = **传输丢弃**（队列满 + 无承载 + 载荷非法）；**停流未投递**是合法状态，
            // 不算故障，故不显示在这里（三类丢弃不混算）
            marker.Dropped = metrics.Dropped;

            // **界面显示的是有效口径**（"速率只要有效数据"）：
            // 速率取 `PayloadBytesPerSecond`、累计流量取 `PayloadBytesDelivered`
            // （逐单元读类型声明的有效长度字段 ⇒ 不含尾部补零/对齐填充）。
            // 搬运口径（`BytesPerSecond` / `BytesDelivered`）仍是 API 事实，但**界面不显示它**
            // ——否则同一块标签上"速率"与"流量"会各说一套。
            marker.BytesText = metrics.PayloadBytesDelivered > 0 ? FormatBytes(metrics.PayloadBytesDelivered) : null;
            // 速率**如实显示**：`---` 只表示"**无连接**"（上面那条分支）；
            // 有连接但这一秒没数据 ⇒ `0 B/s`（**不拿 `---` 冒充 0** —— 再低的速率也是有数据，
            // 如实显示，没有数据就写 0.00，--- 表示无连接）。
            marker.RateText = FormatRate(metrics.PayloadBytesPerSecond);

            // 队列压力（接收口才有队列）：只格式化框架给的队列账本，不在界面上算
            marker.QueueText = QueuePressureText(queues, literal);
        }
    }

    /// <summary>
    /// 本拍“同网络名但契约不兼容”的端口字面量 → 原因（每轮只建一次）。
    /// </summary>
    /// <remarks>
    /// 一个口可能与多个同名对端都不兼容；提示里把不同原因去重后用 `；` 连接，避免重复刷屏。
    /// </remarks>
    private Dictionary<string, string> BuildContractConflictMap()
    {
        var map = new Dictionary<string, string>(StringComparer.Ordinal);
        var reasons = new Dictionary<string, List<string>>(StringComparer.Ordinal);

        foreach (var conflict in _query.GetPortContractConflicts())
        {
            Add($"{conflict.InstanceId}.{conflict.PortName}", conflict.Reason);
            Add($"{conflict.PeerInstanceId}.{conflict.PeerPortName}", conflict.Reason);
        }

        foreach (var (literal, list) in reasons)
        {
            map[literal] = string.Join("；", list);
        }

        return map;

        void Add(string literal, string reason)
        {
            if (!reasons.TryGetValue(literal, out var list))
            {
                list = [];
                reasons[literal] = list;
            }

            if (!list.Contains(reason, StringComparer.Ordinal))
            {
                list.Add(reason);
            }
        }
    }

    /// <summary>本拍各 (实例, 口) 的队列水位（键 = `实例.口名`；每轮只取一次）。</summary>
    private Dictionary<string, PortQueueDepthView> QueueDepthsByPort()
    {
        var map = new Dictionary<string, PortQueueDepthView>(StringComparer.Ordinal);

        foreach (var depth in _query.GetQueueDepths())
        {
            map[$"{depth.InstanceId}.{depth.PortName}"] = depth;
        }

        return map;
    }

    /// <summary>
    /// 口标记的**队列压力一行**（无队列 / 无压力 ⇒ <see langword="null"/>，悬停里不加这一行）。
    /// </summary>
    /// <param name="queues">本拍的队列水位表。</param>
    /// <param name="literal">`实例.口名`。</param>
    private static string? QueuePressureText(Dictionary<string, PortQueueDepthView> queues, string literal)
    {
        if (!queues.TryGetValue(literal, out var queue))
        {
            return null;
        }

        // 有压力才显示：积压、饱和、或这一段里已经丢过
        if (queue.Depth <= 0 && !queue.Saturated && queue.DroppedWhileSaturatedUnits <= 0)
        {
            return null;
        }

        var assembly = typeof(ModuleWindowsViewModel).Assembly;
        var dropped = queue.DroppedEvictedUnits + queue.DroppedRejectedUnits;

        return TextCatalog.Get(assembly, "portMarker.queue.state", queue.Depth, queue.Capacity)
            + TextCatalog.Get(assembly, "portMarker.queue.dropped", dropped)
            + (queue.Saturated ? TextCatalog.Get(assembly, "portMarker.queue.saturatedSuffix") : string.Empty);
    }

    /// <summary>
    /// **角色投影第一遍**：摆好本模块的**角色方块**（功能分块）与信息区。
    /// </summary>
    /// <remarks>
    /// 数据来源 = 公开查询面 <c>GetRoles</c>（Core 算、UI 只画）；此处只做"查询面 → 视图模型"的搬运，
    /// **不派生角色**（角色来自模块声明）。连线的对端锚点需要**所有模块的方块**都已就位 ⇒ 连线在第二遍算。
    /// </remarks>
    private void RefreshRoleBoxes(ModuleViewViewModel module)
    {
        var roles = _query.GetRoles(module.InstanceId);
        var contractSizes = _query.TryGetInstance(module.InstanceId, out var instance) && instance is not null
            ? instance.Ports.Count
            : 0;

        var boxes = new List<RoleBoxViewModel>(roles.Count);
        foreach (var role in roles)
        {
            var box = new RoleBoxViewModel(
                role.Key,
                role.RoleId,
                role.DisplayName,
                role.TypeKey,
                role.TypeText,
                role.Inputs,
                role.Outputs,
                role.InboundRoleCount,
                role.OutboundRoleCount,
                role.SemanticText,
                role.NominalRateHz);

            // **角色自身的信息**（画在角色方块上方、左对齐、大字速率、禁止过长描述）——
            // 数值全部来自框架的**角色维度聚合**（`GetRoles` 已按角色汇总），UI 只做格式化。
            // 速率是**有效载荷字节速率**（"速率只要有效数据"）。
            box.RateText = role.PayloadRateBytesPerSecond > 0.5
                ? $"{FormatBytes((long)role.PayloadRateBytesPerSecond)}/s"
                : "---";
            box.MetaText = RoleTextMetrics.TypeTagOf(role.SemanticText);

            boxes.Add(box);
        }

        module.ApplyRoleBoxes(
            boxes,
            RoleOverlayInfoBuilder.Build(OverlayIdentity(module), roles.Count, contractSizes));
    }

    /// <summary>
    /// 叠加层展示的身份：**外部语义名 + 当前现场内的连续编号**，不暴露实例 ID（英文模块名）。
    /// </summary>
    /// <remarks>
    /// 编号按 <see cref="Modules"/> 的当前顺序、以**外部展示名**（<see cref="ModuleViewViewModel.Title"/>）分组
    /// 现场计算；关闭一个实例后剩余实例重新连续编号，新实例补到当前数量之后 —— 与“全局自增计数器”
    /// 不同，不会留下关闭实例的旧编号。
    /// </remarks>
    private string OverlayIdentity(ModuleViewViewModel module)
    {
        var title = string.IsNullOrWhiteSpace(module.Title) ? TextCatalog.Get(typeof(ModuleWindowsViewModel).Assembly, "windows.module.defaultTitle") : module.Title;
        var number = 1;

        foreach (var peer in Modules)
        {
            if (string.Equals(peer.InstanceId, module.InstanceId, StringComparison.Ordinal))
            {
                break;
            }

            if (string.Equals(peer.Title, module.Title, StringComparison.Ordinal))
            {
                number++;
            }
        }

        return $"{title} {number}";
    }

    /// <summary>
    /// **角色投影第二遍**：算**角色间数据流**（线）与**内部处理顺序**。
    /// </summary>
    /// <remarks>
    /// <list type="bullet">
    ///   <item><b>线的两点</b>：本端角色**方块中心**与**对端角色方块中心**（连线从角色方块中心出发）
    ///     —— 对端那一点用**同一套** <c>ConnectionOverlay.LayoutRoles</c>
    ///     从对端模块的方块算出并换算到本窗局部坐标 ⇒ 两端算出的**是同一条线段**
    ///     （各窗只画自己窗内那段 ⇒ 严格共线，不会出现"两根错位的线"）；</item>
    ///   <item>对端方块算不出（对端没有该角色 / 已被裁掉 / 不在现场）⇒ 回退为**对端窗口中心**；</item>
    ///   <item><b>线不承载指标</b>：角色维度指标画在**角色方块**上（第一遍已备好），
    ///     通路维度指标画在**端口标记**上；这里只算几何与对端信息，不重复算数字。</item>
    /// </list>
    /// </remarks>
    private void RefreshRoleLinks(ModuleViewViewModel module)
    {
        var flows = _query.GetRoleFlows(module.InstanceId);
        var links = new List<RoleLinkViewModel>(flows.Count);
        var selfLoops = new Dictionary<(string Local, string PeerRole, string PeerInstance), int>();

        foreach (var flow in flows)
        {
            var link = new RoleLinkViewModel(
                flow.LocalRoleKey,
                flow.LocalIsSend,
                flow.PeerInstanceId,
                flow.PeerRoleKey,
                flow.PortPairCount);

            // **自回环判据只看"同实例 + 同角色"**：不读网络类型、口数、命名/对齐 ——
            // 它是叠加层的呈现事实，与底层怎么连出来无关。
            link.IsSelfLoop = string.Equals(flow.PeerInstanceId, module.InstanceId, StringComparison.Ordinal)
                && string.Equals(flow.PeerRoleKey, flow.LocalRoleKey, StringComparison.Ordinal);

            // 对端角色方块中心（本窗局部坐标）；自环时它就是本方块中心；算不出 ⇒ 对端窗口中心
            link.Target = ResolvePeerAnchor(module, flow) ?? ResolvePeerCenter(module, flow.PeerInstanceId);

            // 自环没有"窗外对端"：不画断口、不标对端名（环的两端都在本方块上）。
            link.PeerAdjacent = link.IsSelfLoop || IsPeerAdjacent(module, flow.PeerInstanceId);
            link.PeerText = link.PeerAdjacent
                ? null
                : Find(flow.PeerInstanceId) is { } peer
                    ? OverlayIdentity(peer)
                    : null;

            if (link.IsSelfLoop)
            {
                // 同一（本角色, 对端角色, 对端实例）的自环只画一个环：**保留发送侧**那条。
                // 口对数仍由 Core 聚合（链接已带着它），UI 不重新累加。
                var key = (link.LocalRoleKey, link.PeerRoleKey, link.PeerInstanceId);
                if (selfLoops.TryGetValue(key, out var existingIndex))
                {
                    if (!links[existingIndex].LocalIsSend && link.LocalIsSend)
                    {
                        links[existingIndex] = link;
                    }

                    continue;
                }

                selfLoops[key] = links.Count;
            }

            links.Add(link);
        }

        var order = new List<RoleOrderViewModel>();
        foreach (var step in _query.GetRoleOrder(module.InstanceId))
        {
            order.Add(new RoleOrderViewModel(step.FromRoleKey, step.ToRoleKey, step.Label));
        }

        module.ApplyRoleLinks(links, order);
    }

    /// <summary>类型语义 · 标称速率（无声明 ⇒ 空串，不占位）。</summary>
    /// <summary>类型语义 · 标称速率（无声明 ⇒ 空串，不占位）。</summary>
    /// <param name="semanticText">类型的语义标签（可空）。</param>
    /// <param name="nominalRateHz">类型的标称速率（可空）。</param>
    /// <returns>`语义 · 标称 100 Hz` 形式（无声明 ⇒ 空串）。</returns>
    private static string DescribeMeta(string semanticText, double? nominalRateHz)
    {
        var parts = new List<string>(2);

        if (semanticText.Length > 0)
        {
            parts.Add(semanticText);
        }

        if (nominalRateHz is { } nominal)
        {
            parts.Add(TextCatalog.Get(typeof(ModuleWindowsViewModel).Assembly, "windows.role.nominal", nominal.ToString("0.#", CultureInfo.CurrentCulture)));
        }

        return string.Join(" · ", parts);
    }

    /// <summary>
    /// **对端角色方块中心**（本窗局部坐标，**DIP**；线以两个角色方块中心为端点）。
    /// </summary>
    /// <remarks>
    /// <b>单位口径（唯一事实源）：模型与叠加层都是 DIP</b>。
    /// 这里**一次换算都没有** —— 模型的 <c>X/Y/Width/Height</c> 与叠加层的画布、方块布局同为 DIP
    /// （口径见 <see cref="HYWDebugAssistant.Core.Api.ModuleLayoutView">ModuleLayoutView</see>），
    /// 世界增量直接相减即可。
    /// <para>
    /// 旧实现里"模型 = 物理像素、叠加层 = DIP"的错位（完全水平/垂直的线也会错开）根因就在这里：
    /// 那一版必须把 px 增量除以缩放才对齐。**全 DIP 改造之后这个换算点整体消失**，
    /// 别处也不许再引入任何乘除缩放。
    /// </para>
    /// </remarks>
    private Avalonia.Point? ResolvePeerAnchor(ModuleViewViewModel module, RoleFlowView flow)
    {
        if (Find(flow.PeerInstanceId) is not { } peer || !module.Rect.IsValid || !peer.Rect.IsValid)
        {
            return null;
        }

        // 对端方块布局：用**对端自己的画布**（与对端绘制时是同一对数字）
        var layout = ConnectionOverlay.LayoutRoles(peer.Roles, peer.CanvasWidth, peer.CanvasHeight);
        var box = layout.Boxes.FirstOrDefault(b => string.Equals(b.RoleKey, flow.PeerRoleKey, StringComparison.Ordinal));

        if (box.Box.Width <= 0)
        {
            return null;   // 该角色没被画出来（放不下被裁掉）⇒ 交给回退
        }

        // 世界增量：先算**物理像素**差（跨屏缩放不同也成立），再换回本窗 DIP。
        var scale = module.Scaling > 0 ? module.Scaling : 1;
        var dx = (peer.PhysicalRect.X - module.PhysicalRect.X) / scale;
        var dy = (peer.PhysicalRect.Y - module.PhysicalRect.Y) / scale;

        return new Avalonia.Point(box.Center.X + dx, box.Center.Y + dy);
    }

    /// <summary>回退：对端**窗口中心**（本窗局部坐标，**DIP**，同单位直接相减）。</summary>
    private Avalonia.Point ResolvePeerCenter(ModuleViewViewModel module, string peerInstanceId)
    {
        if (Find(peerInstanceId) is { } peer && peer.Rect.IsValid && module.Rect.IsValid)
        {
            var scale = module.Scaling > 0 ? module.Scaling : 1;
            return new Avalonia.Point(
                ((peer.PhysicalRect.X + (peer.PhysicalRect.Width / 2)) - module.PhysicalRect.X) / scale,
                ((peer.PhysicalRect.Y + (peer.PhysicalRect.Height / 2)) - module.PhysicalRect.Y) / scale);
        }

        return new Avalonia.Point(module.CanvasWidth / 2, module.CanvasHeight / 2);
    }

    /// <summary>对端是否与本模块**拼接贴合**（按实例 ID 判；对端不在现场 ⇒ false ⇒ 末端画断口 + 标对端）。</summary>
    /// <remarks>这是连线的几何邻接判断，不受模块层限制：命名网络可以跨层。</remarks>
    private bool IsPeerAdjacent(ModuleViewViewModel module, string peerInstanceId) =>
        Find(peerInstanceId) is { } peer
        && peer.Rect.IsValid
        && SnapEngine.AreJoined(module.PhysicalRect, peer.PhysicalRect, GroupTolerance);
    /// <summary>
    /// **速率文本**（每秒字节数）：**如实显示**，不做"低于阈值就显示占位符"的模糊化。
    /// </summary>
    /// <param name="bytesPerSecond">每秒搬运字节数（框架算，界面只格式化）。</param>
    /// <returns>`0 B/s` / `512 B/s` / `1.0 KB/s` …（再低的速率也是有数据，如实显示）。</returns>
    private static string FormatRate(double bytesPerSecond) =>
        ByteQuantity.Format(bytesPerSecond, perSecond: true);

    private static string FormatBytes(long bytes) => ByteQuantity.Format(bytes);

    /// <summary>成组判定用的模块矩形 + 层（**物理像素**；不同缩放屏上才有共同单位）。</summary>
    private IReadOnlyList<(string Id, CanvasRect Rect, int Layer)> ModuleRects() =>
        [.. Modules.Where(m => m.IsViewVisible).Select(m => (m.InstanceId, m.PhysicalRect, m.Layer))];

    /// <summary>外壳在**拼图图**里的节点 id（与实例 id 不会冲突）。</summary>
    public const string ShellNodeId = "#shell";

    /// <summary>拼图图的全部节点：外壳（若可用）+ 可见模块（成组判定的输入）。</summary>
    /// <param name="shellOverridePixels">
    /// 用指定的外壳**物理矩形**代替模型里的当前值（**分组判定要用移动前的几何**）。
    /// </param>
    private List<(string Id, CanvasRect Rect, int Layer)> TilingNodes(CanvasRect? shellOverridePixels = null)
    {
        var nodes = ModuleRects().ToList();
        var shell = shellOverridePixels ?? ShellPhysicalRect;

        // 向后兼容：测试/旧调用只设了 ShellRect 时，按当前缩放投成物理再判定。
        if (shell is not { IsValid: true } && ShellRect is { IsValid: true } dip)
        {
            var shellScale = ShellScaling > 0 ? ShellScaling : 1;
            shell = new CanvasRect(dip.X * shellScale, dip.Y * shellScale, dip.Width * shellScale, dip.Height * shellScale);
        }

        if (shell is { IsValid: true } validShell)
        {
            nodes.Add((ShellNodeId, validShell, 0));
        }

        return nodes;
    }

    /// <summary>
    /// 两节点是否"拼在一起"（成组谓词）。
    /// </summary>
    /// <remarks>
    /// <list type="bullet">
    ///   <item>模块 ↔ 模块：先要求**同层**，再沿用 <see cref="SnapEngine.AreJoined"/>（相邻边贴合 + 交叉轴有重叠）；</item>
    ///   <item><b>模块 ↔ 外壳：只有"模块左边缘贴外壳右边缘"算</b>（外壳只提供右边缘），
    ///     不允许贴它的左/上/下边缘；<b>外壳是跨层锚点，不做层相等检查</b>；</item>
    ///   <item><b>外壳用严格容差</b>（<see cref="ShellJoinTolerance"/>）：模块间用
    ///     <see cref="GroupTolerance"/>（贴合容差 8px，"差一点也算拼上"对拼装手感友好），但**外壳不能用它** ——
    ///     否则"停在离外壳 8px 处、根本没吸附"的模块也被判成已拼上，拖动主窗口时被一并带走，
    ///     表现为"没产生吸附行为却粘在主窗口上"。
    ///     外壳侧的判据是"**真的贴上了**"⇒ 吸附产生的 0 间隙（允许 1px 取整误差）。</item>
    /// </list>
    /// </remarks>
    private static bool AreNodesJoined(
        (string Id, CanvasRect Rect, int Layer) a,
        (string Id, CanvasRect Rect, int Layer) b,
        double tolerance)
    {
        if (string.Equals(a.Id, ShellNodeId, StringComparison.Ordinal) || string.Equals(b.Id, ShellNodeId, StringComparison.Ordinal))
        {
            var (shell, module) = string.Equals(a.Id, ShellNodeId, StringComparison.Ordinal) ? (a.Rect, b.Rect) : (b.Rect, a.Rect);
            if (SnapEngine.RectsOverlap(module, shell))
            {
                return false;   // 压在壳上不算贴壳
            }

            var overlap = Math.Min(module.Bottom, shell.Bottom) - Math.Max(module.Top, shell.Top);
            return Math.Abs(module.Left - shell.Right) <= Math.Min(tolerance, ShellJoinTolerance)
                && overlap > ShellJoinTolerance;
        }

        return a.Layer == b.Layer
            && SnapEngine.AreJoined(a.Rect, b.Rect, tolerance);
    }

    /// <summary>
    /// 某模块当前所在的**模块组**（不包含外壳）：供“焦点进入任一模块 ⇒ 抬起整组”使用。
    /// </summary>
    /// <param name="instanceId">模块实例 ID。</param>
    /// <returns>实例 ID 列表（至少包含自身）。</returns>
    public IReadOnlyList<string> GroupMembersOf(string instanceId) =>
        [.. GroupOfNode(instanceId).Where(id => !string.Equals(id, ShellNodeId, StringComparison.Ordinal) && Find(id) is not null)];

    /// <summary>
    /// 按**物理位移**平移一组模块（跨屏缩放变化后的组内重贴用）。
    /// </summary>
    /// <param name="instanceIds">要平移的实例（不含调用方自己）。</param>
    /// <param name="dxPixels">物理位移 X。</param>
    /// <param name="dyPixels">物理位移 Y。</param>
    public void TranslateModulesByPixels(IReadOnlyList<string> instanceIds, double dxPixels, double dyPixels)
    {
        if ((Math.Abs(dxPixels) < 0.5 && Math.Abs(dyPixels) < 0.5) || instanceIds.Count == 0)
        {
            return;
        }

        foreach (var instanceId in instanceIds)
        {
            if (Find(instanceId) is not { } module || module.IsLocked)
            {
                continue;
            }

            var scale = module.Scaling > 0 ? module.Scaling : 1;
            module.X += dxPixels / scale;
            module.Y += dyPixels / scale;
            RaiseGeometry(module);
            _ = _pendingSceneCommits.Add(module.InstanceId);
        }
    }

    /// <summary>某节点所在的**拼图组**（接触连通分量，含自身；外壳也参与）。</summary>
    /// <param name="nodeId">起始节点 id（外壳用 <see cref="ShellNodeId"/>）。</param>
    /// <param name="shellOverride">用指定的外壳矩形参与分组（**移动前的几何**）。</param>
    /// <param name="layerFilter">
    /// 只允许走到该层的模块；null = 从模块起点自动取它的层，从外壳起点（移动外壳）跨所有层。
    /// 外壳本身不参与层比较，但经过外壳再往模块走时仍受此过滤。
    /// </param>
    private HashSet<string> GroupOfNode(string nodeId, CanvasRect? shellOverride = null, int? layerFilter = null)
    {
        var filter = layerFilter;
        if (filter is null
            && !string.Equals(nodeId, ShellNodeId, StringComparison.Ordinal)
            && Find(nodeId) is { } start)
        {
            filter = start.Layer;
        }

        var nodes = TilingNodes(shellOverride);
        var group = new HashSet<string>(StringComparer.Ordinal) { nodeId };
        var queue = new Queue<string>();
        queue.Enqueue(nodeId);

        while (queue.Count > 0)
        {
            var current = queue.Dequeue();
            var currentIndex = nodes.FindIndex(n => string.Equals(n.Id, current, StringComparison.Ordinal));
            if (currentIndex < 0)
            {
                continue;
            }

            for (var i = 0; i < nodes.Count; i++)
            {
                if (i == currentIndex || group.Contains(nodes[i].Id))
                {
                    continue;
                }

                if (!AreNodesJoined(nodes[currentIndex], nodes[i], GroupTolerance))
                {
                    continue;
                }

                // 模块起点按层走；外壳是跨层锚点，本身不受层限制，但经它再往模块走仍受 filter 约束。
                if (filter is { } layer
                    && !string.Equals(nodes[i].Id, ShellNodeId, StringComparison.Ordinal)
                    && nodes[i].Layer != layer)
                {
                    continue;
                }

                _ = group.Add(nodes[i].Id);
                queue.Enqueue(nodes[i].Id);
            }
        }

        return group;
    }

    /// <summary>组吸附的障碍矩形（同层组外可见模块 + 外壳），统一换算到被拖窗口 DIP。</summary>
    private List<CanvasRect> BuildDragObstacles(double dragScale, int dragLayer)
    {
        var obstacles = new List<CanvasRect>();

        foreach (var module in Modules.Where(m => m.IsViewVisible
            && m.Layer == dragLayer
            && !_dragGroup.Contains(m.InstanceId, StringComparer.Ordinal)))
        {
            obstacles.Add(ToDragDip(module.PhysicalRect, dragScale));
        }

        // 外壳是跨层锚点：任何层都可以贴它，因此始终作为障碍（贴着但不重合才允许）。
        if (!_dragGroup.Contains(ShellNodeId, StringComparer.Ordinal))
        {
            var shellPhysical = ShellPhysicalRect;
            if (shellPhysical is not { IsValid: true } && ShellRect is { IsValid: true } dip)
            {
                var shellScale = ShellScaling > 0 ? ShellScaling : 1;
                shellPhysical = new CanvasRect(
                    dip.X * shellScale,
                    dip.Y * shellScale,
                    dip.Width * shellScale,
                    dip.Height * shellScale);
            }

            if (shellPhysical is { IsValid: true } shell)
            {
                obstacles.Add(ToDragDip(shell, dragScale));
            }
        }

        return obstacles;
    }

    /// <summary>
    /// **参照线**集合：其他模块的外框（4 边 + 中心线）+ 外壳边。
    /// </summary>
    /// <remarks>
    /// 外壳只是"**只暴露一条右边缘的对象**"，与模块进同一张线表（不做特殊通道）。
    /// 同组成员与自身不提供外框线；**外壳在拖动组里时也不提供** —— 它跟着组一起走，
    /// 留在起点的旧线会把被拖模块拉回去。
    /// </remarks>
    private List<SnapLine> CollectLines(string draggingId)
    {
        var lines = new List<SnapLine>();
        var dragging = Find(draggingId);
        var dragScale = dragging?.Scaling ?? 1.0;
        if (dragScale <= 0)
        {
            dragScale = 1.0;
        }

        var dragLayer = dragging?.Layer ?? 0;

        foreach (var module in Modules.Where(m => m.IsViewVisible
            && m.Layer == dragLayer
            && !_dragGroup.Contains(m.InstanceId, StringComparer.Ordinal)
            && !string.Equals(m.InstanceId, draggingId, StringComparison.Ordinal)))
        {
            lines.AddRange(SnapEngine.LinesOf(ToDragDip(module.PhysicalRect, dragScale)));
        }

        // 外壳是跨层锚点：任何层都能贴它；只在它跟着本组走时不再作为参照线。
        if (!_dragGroup.Contains(ShellNodeId, StringComparer.Ordinal))
        {
            var shellPhysical = ShellPhysicalRect;
            if (shellPhysical is not { IsValid: true } && ShellRect is { IsValid: true } dip)
            {
                var shellScale = ShellScaling > 0 ? ShellScaling : 1;
                shellPhysical = new CanvasRect(
                    dip.X * shellScale,
                    dip.Y * shellScale,
                    dip.Width * shellScale,
                    dip.Height * shellScale);
            }

            if (shellPhysical is { IsValid: true } shell)
            {
                var shellRect = ToDragDip(shell, dragScale);
                lines.Add(SnapEngine.LineOf(new SnapReferenceLine(
                    true,
                    shellRect.X + shellRect.Width,
                    shellRect.Y,
                    shellRect.Y + shellRect.Height)));
            }
        }

        return lines;
    }

    /// <summary>把物理矩形换算成"被拖窗口的 DIP"（吸附/端口锚点在同一坐标系里比较）。</summary>
    private static CanvasRect ToDragDip(CanvasRect physical, double dragScale)
    {
        var scale = dragScale > 0 ? dragScale : 1;
        return new CanvasRect(
            physical.X / scale,
            physical.Y / scale,
            physical.Width / scale,
            physical.Height / scale);
    }

    /// <summary>
    /// 端口锚点对：被拖模块的发送口 ↔ 其他模块的接收口（及反向）。
    /// </summary>
    /// <remarks>
    /// <list type="bullet">
    ///   <item><b>对齐轴由端口所在的边决定</b>：左/右边的口 → 约束 Y（两个口沿竖直方向对齐）；上/下边的口 → 约束 X；</item>
    ///   <item><b>只有"面对面"的两个口才参与</b>：对方的口必须挂在**对面那条边**上
    ///     （我朝右 → 对方朝左；我朝下 → 对方朝上）。缺这一条时，两个**背对背/错向**的口
    ///     （例如上下相邻的两模块，一个右边缘口、一个左边缘口）也会产生"对齐吸附"，
    ///     把模块吸到一个**永远不会连上**的位置（表现为"明明对齐的下侧却导致左右两侧的吸附"）；</item>
    ///   <item>接近范围由引擎按两模块的交叉轴间隙判定（不按缝隙大小猜邻接方向）。</item>
    /// </list>
    /// 同组成员与自身不参与；此通道**恒开**（不依赖"叠加信息层"开关）。
    /// </remarks>
    private List<PortAnchorPair> BuildPortAnchors(string draggingId)
    {
        if (Find(draggingId) is not { } anchor)
        {
            return [];
        }

        var pairs = new List<PortAnchorPair>();
        var dragScale = anchor.Scaling > 0 ? anchor.Scaling : 1.0;
        var memberIndex = -1;

        foreach (var memberId in _dragGroup)
        {
            if (Find(memberId) is not { } moving)
            {
                continue;
            }

            memberIndex++;

            foreach (var other in Modules.Where(m => m.IsViewVisible
                && m.Layer == moving.Layer
                && !_dragGroup.Contains(m.InstanceId, StringComparer.Ordinal)))
            {
                foreach (var mine in moving.Ports)
                {
                    var facing = PortEdgeGeometry.Opposite(mine.Edge);

                    foreach (var theirs in other.Ports)
                    {
                        if (mine.IsSend == theirs.IsSend)
                        {
                            continue;   // 只考虑"发送 ↔ 接收"
                        }

                        if (theirs.Edge != facing)
                        {
                            continue;   // **不是面对面** ⇒ 插不上，不产生对齐候选
                        }

                        // 端口的对齐轴 = 它所在的边：左/右边 → 约束 Y（横向参照线）；上/下边 → 约束 X。
                        // 对端几何先换成"被拖窗口的 DIP"（跨屏缩放不同也成立）。
                        var otherRect = ToDragDip(other.PhysicalRect, dragScale);
                        var alignsX = !mine.IsVertical;
                        var otherCoordinate = alignsX
                            ? otherRect.X + (otherRect.Width * theirs.Offset)
                            : otherRect.Y + (otherRect.Height * theirs.Offset);

                        pairs.Add(new PortAnchorPair(alignsX, otherCoordinate, otherRect, mine.Offset, memberIndex));
                    }
                }
            }
        }

        return pairs;
    }
}

/// <summary>窗口几何变化事件参数（模型 DIP；落位时按窗口 Scale 投影为物理像素）。</summary>
/// <param name="InstanceId">实例 ID。</param>
/// <param name="X">X。</param>
/// <param name="Y">Y。</param>
/// <param name="Width">宽。</param>
/// <param name="Height">高。</param>
public sealed record ModuleWindowGeometryChange(string InstanceId, double X, double Y, double Width, double Height);

/// <summary>窗口可见性变化事件参数。</summary>
/// <param name="InstanceId">实例 ID。</param>
/// <param name="IsVisible">是否应显示。</param>
public sealed record ModuleWindowVisibilityChange(string InstanceId, bool IsVisible);

/// <summary>窗口点击要求抬起的整组（<paramref name="Primary"/> 最后抬、在最上）。</summary>
/// <param name="InstanceIds">组内实例 ID（抬升顺序）。</param>
/// <param name="Primary">被点击的锚点。</param>
public sealed record ModuleGroupRaiseRequest(IReadOnlyList<string> InstanceIds, string Primary);
