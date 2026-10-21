using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using HYWDebugAssistant.Core.Api;
using HYWDebugAssistant.Sdk.Ports;
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

    private int _frameCounter;

    /// <summary>上一次看到的**拓扑结构签名**（网络成员 + 口标签）——变了就立刻刷新连接状态与角色投影。</summary>
    private string _structureSignature = string.Empty;

    /// <summary>本次拖动**起点**的各成员几何（拖动中按 Ctrl 拆分时用来"放回原位"）。</summary>
    private readonly Dictionary<string, CanvasRect> _dragStartRects = new(StringComparer.Ordinal);

    private string? _draggingId;
    private IReadOnlyList<string> _dragGroup = [];
    private double _dragPointerOffsetX;
    private double _dragPointerOffsetY;

    /// <summary>整体平移造成的位置变化、**尚未落盘**的实例（合并提交用，见 <see cref="CommitSceneTranslation"/>）。</summary>
    private readonly HashSet<string> _pendingSceneCommits = new(StringComparer.Ordinal);

    /// <summary>本次拖动已施加到外壳上的累计位移（用于取消拖动时还原，以及终结时落盘）。</summary>
    private double _shellDragDx;
    private double _shellDragDy;

    /// <summary>
    /// **请求把外壳窗口平移 (dx, dy)**：拖动组包含外壳时（模块单侧吸附在外壳上）触发，
    /// 由 `ModuleWindowManager` 落到真实窗口上。
    /// </summary>
    /// <remarks>
    /// 主窗口与模块形成一个视觉整体后，移动谁都是整体动 ——
    /// 外壳不是"应用的框架"，而是**拼图里的一个节点**；因此拖模块与拖外壳走的是同一套成组逻辑。
    /// </remarks>
    public event EventHandler<(double Dx, double Dy)>? ShellTranslationRequested;

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
    public ModuleWindowsViewModel(IRuntimeQuery query, IRuntimeCommand command, IViewStateAccess? viewState = null)
    {
        ArgumentNullException.ThrowIfNull(query);
        ArgumentNullException.ThrowIfNull(command);

        _query = query;
        _command = command;
        _viewState = viewState;
    }

    private readonly IViewStateAccess? _viewState;

    /// <summary>模块窗口（每个模块一项）。</summary>
    public ObservableCollection<ModuleViewViewModel> Modules { get; } = [];

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

    /// <summary>
    /// 是否显示**叠加信息层**（端口标记本身常显，本开关只控制叠加层）。
    /// **默认关闭**（它是调试/观测层，不进常驻界面）。
    /// </summary>
    [ObservableProperty]
    public partial bool ShowInfoOverlay { get; set; }

    /// <summary>吸附是否启用（交互组开关；按住 Alt 可临时停用）。</summary>
    [ObservableProperty]
    public partial bool SnapEnabled { get; set; } = true;

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

    /// <summary>当前选中的模块实例（选中态视觉）。</summary>
    [ObservableProperty]
    public partial string? SelectedInstanceId { get; set; }

    /// <summary>UI 刷新率（Hz；来自应用级设置）。</summary>
    [ObservableProperty]
    public partial int RefreshRateHz { get; set; } = 60;

    /// <summary>全部窗口是否已锁定（"锁定全部"开关的状态）。</summary>
    [ObservableProperty]
    public partial bool AllLocked { get; set; }

    /// <summary>是否正在拖动或缩放。</summary>
    public bool IsInteracting => _draggingId is not null || _resizingId is not null;

    /// <summary>模块数（状态栏用）。</summary>
    public int ModuleCount => Modules.Count;

    /// <summary>窗口几何变化（窗口管理器订阅：把数值几何落到真正的窗口上）。</summary>
    public event EventHandler<ModuleWindowGeometryChange>? GeometryChanged;

    /// <summary>窗口可见性变化（窗口管理器订阅：开窗/关窗）。</summary>
    public event EventHandler<ModuleWindowVisibilityChange>? VisibilityChanged;

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
    public void Refresh()
    {
        SyncModules();
        RefreshSubscriptions();
        RefreshOverlayPeers();
    }

    /// <summary>
    /// 刷新**叠加层开关**与口标记的**对端文本**（仅服务**悬停提示**）。
    /// </summary>
    /// <remarks>
    /// 【来源唯一】叠加层的**连线**改由**角色对**（`GetRolePairs`）驱动，
    /// 不读标记上的"对端方位/是否贴合"（那是同一事实的第二个来源，故不留那三个字段）。
    /// 本方法现在只做两件事：① 把叠加层开关下发到每个模块；② 给悬停提示填一句"对端是谁"。
    /// </remarks>
    private void RefreshOverlayPeers()
    {
        var peers = new Dictionary<string, string>(StringComparer.Ordinal);

        foreach (var network in _query.GetNetworks())
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
            module.ShowInfoOverlay = ShowInfoOverlay;
            module.MultilineSubmitKey = MultilineSubmitKey;

            foreach (var marker in module.Ports)
            {
                _ = peers.TryGetValue($"{marker.InstanceId}.{marker.PortName}", out var peer);
                marker.PeerText = peer;
            }
        }
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
        _ => $"{members[0]} 等 {members.Count} 个",
    };

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

    /// <summary>
    /// 选中一个模块（点击边框带 / 端口标记时调用；**唯一写者**）。
    /// </summary>
    /// <param name="instanceId">实例 ID（null = 取消选择）。</param>
    /// <remarks>
    /// <b>选中态只有一个事实源 = <see cref="SelectedInstanceId"/></b>：本方法每次调用都把
    /// <b>全部模块</b>的 <see cref="ModuleViewViewModel.IsSelected"/> 对账成"是否就是目标"。
    /// 旧实现只在 id 变化时改前后两个模块，且 <c>ClearSelection</c> 不清 <c>SelectedInstanceId</c> ⇒
    /// "点内容清空后再点同一个模块"会被 early-return 吞掉（边框/手柄再也出不来）。对账式写法同时兜住
    /// 模块刷新重建、未知 id、历史脏标志。锁定模块不可选中（锁定 = 不可拖动/缩放/选中）。
    /// </remarks>
    public void Select(string? instanceId)
    {
        var target = instanceId is { Length: > 0 } id
            ? Modules.FirstOrDefault(m => string.Equals(m.InstanceId, id, StringComparison.Ordinal))
            : null;

        if (target is { IsLocked: true })
        {
            target = null;
        }

        var next = target?.InstanceId;
        if (!string.Equals(SelectedInstanceId, next, StringComparison.Ordinal))
        {
            SelectedInstanceId = next;
        }

        foreach (var module in Modules)
        {
            var selected = ReferenceEquals(module, target);
            if (module.IsSelected != selected)
            {
                module.IsSelected = selected;
            }
        }
    }

    /// <summary>切换某窗口的锁定（锁定 = 不可拖动/缩放/选中，仍显示数据）。</summary>
    /// <param name="instanceId">实例 ID。</param>
    public void ToggleLock(string instanceId)
    {
        if (Find(instanceId) is not { } module)
        {
            return;
        }

        module.IsLocked = !module.IsLocked;

        if (module.IsLocked && string.Equals(SelectedInstanceId, instanceId, StringComparison.Ordinal))
        {
            Select(null);
        }
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

        if (locked)
        {
            Select(null);
        }
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
    }

    /// <summary>重新显示某个模块窗口（模块清单弹窗的"显示窗口"）。</summary>
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
    }

    /// <summary>关闭某个模块窗口 = **移除该模块实例**（级联清理绑定）。</summary>
    /// <param name="instanceId">实例 ID。</param>
    public void CloseModule(string instanceId)
    {
        Select(null);
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

        _draggingId = instanceId;
        module.IsInteracting = true;

        // **锁定成员不随组移动**：编组时就把锁定的成员排除，
        // 于是拖动整组时它们留在原地（组自然"解开"）—— 这比"拖完再纠正"更直观。
        // **外壳也在这个组里**（单侧吸附完成即同组）：
        // 拖动一个贴在外壳上的模块时，外壳窗口要跟着一起走（"视觉整体"）；见 _shellDragDx/Dy 与 ShellTranslationRequested。
        _dragGroup = independent
            ? [instanceId]
            : [.. GroupOfNode(instanceId).Where(id => Find(id) is not { IsLocked: true })];

        // 拖动**起点**的几何（拖动中按 Ctrl 拆分时，把已经被带走的其他成员**放回原位** —— 见 DetachDrag）
        _dragStartRects.Clear();
        foreach (var memberId in _dragGroup)
        {
            if (Find(memberId) is { } member)
            {
                _dragStartRects[memberId] = member.Rect;
            }
        }

        _shellDragDx = 0;
        _shellDragDy = 0;

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
        _dragPointerOffsetX = pointerX - module.X;
        _dragPointerOffsetY = pointerY - module.Y;
        Select(instanceId);
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

        // ① 外壳已经跟着走了 → 按累计位移**还回去**（模型 + 真实窗口）
        if ((Math.Abs(_shellDragDx) > 0.5 || Math.Abs(_shellDragDy) > 0.5)
            && ShellRect is { IsValid: true } shell)
        {
            var (backX, backY) = (-_shellDragDx, -_shellDragDy);
            ShellRect = shell.OffsetBy(backX, backY);
            ShellTranslationRequested?.Invoke(this, (backX, backY));
        }

        _shellDragDx = 0;
        _shellDragDy = 0;

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

        // ③ 组收缩为"只有被拖的那个"
        _dragGroup = [draggingId];
        dragged.IsInteracting = true;
        _dragStartRects.Clear();
        _dragStartRects[draggingId] = dragged.Rect;
    }

    /// <summary>
    /// 把位移施加到**外壳**上（仅当本次拖动组包含外壳）：乐观更新模型里的外壳矩形，并请求窗口管理器移动真实窗口。
    /// </summary>
    /// <remarks>
    /// 这是"主窗口与模块形成视觉整体后，移动谁都是整体动"的落点。
    /// 真实窗口由 `ModuleWindowManager` 移动；它移动时会回推一次 `ShellRect`（交互期间只刷新参照、不联动平移）。
    /// </remarks>
    private void ApplyShellDelta(double dx, double dy)
    {
        if (!_dragGroup.Contains(ShellNodeId) || ShellRect is not { IsValid: true } shell)
        {
            return;
        }

        ShellRect = shell.OffsetBy(dx, dy);
        _shellDragDx += dx;
        _shellDragDy += dy;
        ShellTranslationRequested?.Invoke(this, (dx, dy));
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
    /// <param name="dx">外壳位移 X（物理像素）。</param>
    /// <param name="dy">外壳位移 Y（物理像素）。</param>
    /// <param name="preMoveShell">
    /// **移动前**的外壳矩形：分组必须用它判定"谁与外壳拼在一起"。
    /// </param>
    /// <remarks>
    /// <b>为什么必须传"移动前"的矩形</b>：调用方通常已把 `ShellRect` 刷新成移动后的位置，
    /// 而此时模块还在旧位置上 —— 若用新矩形判分组，"贴上"会被判成"没贴上"，于是**模块不跟随**
    /// （同一类错误：**任何"谁和谁相连"的判定都必须用同一时刻的几何**）。
    /// </remarks>
    public void TranslateShellGroup(double dx, double dy, CanvasRect preMoveShell)
    {
        if ((Math.Abs(dx) < 0.5 && Math.Abs(dy) < 0.5) || !preMoveShell.IsValid)
        {
            return;
        }

        // **本次拖动开始时**就与外壳拼在一起的那些模块（冻结集合）：
        // 单独拖动主窗口时不做吸附更符合直觉：若**逐帧**重算分组，拖着外壳**经过**某个模块时，
        // 那一帧它就会被判成"已拼上"并跟着走
        // ⇒ 现象正是"经过模块时导致模块**错位粘连**（没有产生吸附行为却拼上去了）"。
        // ⇒ 冻结集合只在**一次拖动开始时**算一次（用移动前的外壳矩形），拖动过程中**不吸收新成员**。
        _shellDragGroup ??= GroupOfNode(ShellNodeId, preMoveShell);

        foreach (var module in Modules)
        {
            if (!_shellDragGroup.Contains(module.InstanceId) || module.IsLocked || !module.Rect.IsValid)
            {
                continue;
            }

            module.X = ScreenPixels.RoundDip(module.X + dx);
            module.Y = ScreenPixels.RoundDip(module.Y + dy);
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
    /// <param name="pointerX">指针 X（屏幕坐标，**DIP**）。</param>
    /// <param name="pointerY">指针 Y（屏幕坐标，**DIP**）。</param>
    /// <param name="snapEnabled">本次是否吸附（null = 用开关值；按住 Alt 传 false）。</param>
    /// <remarks>
    /// **拖动中实时吸附**（松手才吸附操作不友好）；
    /// 目标手感 = **Visio 式**：吸附半径小（几像素）、离开即松、靠参考线给反馈。
    /// 另：位置在写入模型前**统一取整到整 DIP**（<see cref="ScreenPixels.RoundDip"/>）——
    /// 吸附在整数格上做，才不会出现"小数 DIP 乘缩放后两端各自舍入 ⇒ 看得见的缝"。
    /// </remarks>
    public void DragTo(double pointerX, double pointerY, bool? snapEnabled = null)
    {
        if (_draggingId is not { Length: > 0 } draggingId || Find(draggingId) is not { } module)
        {
            return;
        }

        // 原始位置（指针推出来的，未被吸附修正）——吸附是它的**纯函数**，不累加上一帧的结果
        var targetX = pointerX - _dragPointerOffsetX;
        var targetY = pointerY - _dragPointerOffsetY;

        IReadOnlyList<GuideLine> guides = [];
        var neighbourCount = 0;

        if (snapEnabled ?? SnapEnabled)
        {
            var lines = CollectLines(draggingId);
            neighbourCount = lines.Count;

            var snapped = SnapEngine.Snap(
                module.Rect.At(targetX, targetY),
                lines,
                SnapDistance,                 // 对齐类容差 = 设置里的「吸附距离」
                BuildPortAnchors(draggingId),
                SnapDistance * 2);            // 贴合类容差 = 2×（拼接是主用途，值得给宽容差）

            targetX = snapped.X;
            targetY = snapped.Y;
            guides = snapped.Guides;
        }

        // 统一取整：模型几何一律**整 DIP**（唯一量化点，见 `ScreenPixels.RoundDip`）
        var dx = ScreenPixels.RoundDip(targetX) - module.X;
        var dy = ScreenPixels.RoundDip(targetY) - module.Y;

        foreach (var memberId in _dragGroup)
        {
            if (Find(memberId) is { } member)
            {
                member.X += dx;
                member.Y += dy;
                RaiseGeometry(member);
            }
        }

        // 组内含外壳（模块单侧吸附在外壳上）→ 外壳窗口也一起走（"视觉整体"）
        ApplyShellDelta(dx, dy);

        // 窗内绘制用：辅助线挂到**被拖的模块**上（其余成员只跟随位移）
        if (Find(draggingId) is { } dragged)
        {
            dragged.Guides = guides;
        }

        // **即将自动建网**预览：按"接触且对齐"规则问 L4"现在松手会连上什么"，
        // 高亮对应的端口标记 —— 判定规则与真正建网同一份实现，预览必然与结果一致。
        UpdateDockPreview();
    }

    /// <summary>把"将会连上"的端口标记高亮（拖动中实时；松手后由网络行为落地）。</summary>
    private void UpdateDockPreview()
    {
        var provisional = _dragGroup
            .Select(Find)
            .Where(m => m is not null)
            .Select(m => m!.ToLayout())
            .ToList();

        var candidates = _query.PreviewConnections(provisional);
        var marked = new HashSet<string>(StringComparer.Ordinal);

        foreach (var candidate in candidates)
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

        if (candidates.Count > 0)
        {
            RaiseNotice($"即将自动建网：{candidates.Count} 条连接（松开鼠标后建立）");
        }
    }

    /// <summary>清掉"即将建网"高亮（拖动结束/取消时）。</summary>
    private void ClearDockPreview()
    {
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
            RaiseNotice($"网络标签已对齐：松手即可与 {key} 建立连接");
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
                RaiseNotice($"{marker.PortName}：已与模块中线对齐");
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
    {        if (_draggingId is null)
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
        ClearDockPreview();

        if (layouts.Count > 0)
        {
            _ = _command.UpdateModuleLayouts(layouts);
        }
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

        // 组内含外壳时，外壳窗口也被拖走了 → 还原时把它移回去（累计位移取反）
        if ((Math.Abs(_shellDragDx) > 0.5 || Math.Abs(_shellDragDy) > 0.5)
            && ShellRect is { IsValid: true } shell)
        {
            ShellRect = shell.OffsetBy(-_shellDragDx, -_shellDragDy);
            ShellTranslationRequested?.Invoke(this, (-_shellDragDx, -_shellDragDy));
        }

        _shellDragDx = 0;
        _shellDragDy = 0;

        var layoutById = _query.GetLayout().ToDictionary(l => l.InstanceId, StringComparer.Ordinal);
        foreach (var memberId in group)
        {
            if (Find(memberId) is not { } member)
            {
                continue;
            }

            member.IsInteracting = false;

            if (layoutById.TryGetValue(memberId, out var layout))
            {
                member.ApplyLayout(layout);
            }

            RaiseGeometry(member);
        }
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
                ? $"{module.Title} · {marker.PortName}：已清除网络名（退出该网络）{notices}"
                : $"{module.Title} · {marker.PortName}：网络名已设为「{text}」{notices}";
        }
        else
        {
            // 保持打开：让用户就地改（不清空输入，不丢上下文）
            marker.IsEditing = true;
            message = $"{module.Title} · {marker.PortName}：网络名未生效 —— {string.Join("；", result.Failures)}";
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
            ? $"{module.Title} · {marker.PortName}：锚点已移到{EdgeText(marker.Edge)}（位置 {marker.Offset:0.00}）"
                + (notices.Length > 0 ? $" —— {notices}" : string.Empty)
            : $"{module.Title} · {marker.PortName}：锚点未移动 —— {failure}";

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
                ? $"{module.Title} · {marker.PortName}：锚点已固定（不可拖动）"
                : $"{module.Title} · {marker.PortName}：锚点已解锁（可拖动）"
            : $"{module.Title} · {marker.PortName}：固定状态未改变 —— {failure}";

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
            const string empty = "端口锚点：当前没有端口";
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
            ? $"端口锚点：已固定 {changed} 个（不可拖动）" + (failed > 0 ? $"，{failed} 个失败" : string.Empty)
            : $"端口锚点：已解锁 {changed} 个（可拖动）" + (failed > 0 ? $"，{failed} 个失败" : string.Empty);

        RaiseNotice(text, isError: failed > 0);
        return text;
    }

    /// <summary>边缘的界面用语。</summary>
    /// <param name="edge">边缘。</param>
    private static string EdgeText(PortEdge edge) => edge switch
    {
        PortEdge.Right => "右边",
        PortEdge.Top => "上边",
        PortEdge.Bottom => "下边",
        PortEdge.Left => "左边",
        _ => "默认边",
    };

    /// <summary>
    /// **还原模块**：回到默认尺寸，可选连配置一起还原。
    /// </summary>
    /// <remarks>
    /// 默认尺寸 = **模块的 `[ModuleDefinition(WindowWidth/WindowHeight)]`**（未声明则框架地板），
    /// **不是**按元素类型估算的结果；位置不变（只还原尺寸与配置，不动用户摆放的位置）。
    /// 还原值同样夹进该模块的有效尺寸区间（模块声明的 min/max 是约束，还原也不能越过）。
    /// </remarks>
    /// <param name="module">模块视图。</param>
    /// <param name="scope">还原范围（只尺寸 / 尺寸与配置）。</param>
    public void ApplyReset(ModuleViewViewModel module, ModuleResetScope scope)
    {
        ArgumentNullException.ThrowIfNull(module);

        // ① 尺寸回到包内声明的默认尺寸（**声明就是 DIP** ⇒ 不乘缩放、不取整），并写回布局
        module.Width = ClampToBounds(module.PreferredWidth, module.EffectiveMinWidth, module.EffectiveMaxWidth);
        module.Height = ClampToBounds(module.PreferredHeight, module.EffectiveMinHeight, module.EffectiveMaxHeight);
        module.HasLayout = true;

        _ = _command.UpdateModuleLayout(module.ToLayout());
        RaiseGeometry(module);

        // ② 配置（可选）：清掉该实例已保存的配置，模块重新读到默认值
        if (scope == ModuleResetScope.SizeAndConfiguration)
        {
            var result = _command.ResetModuleConfiguration(module.InstanceId);

            RaiseNotice(
                result.Succeeded
                    ? $"{module.Title}：已还原默认尺寸与配置"
                    : $"{module.Title}：尺寸已还原，但配置还原失败 —— {string.Join("；", result.Failures)}",
                isError: !result.Succeeded);
        }
        else
        {
            RaiseNotice($"{module.Title}：已还原默认尺寸", isError: false);
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

    /// <summary>
    /// 清除全部选中：点内容区 / 点外壳 / 点本应用其他窗口。
    /// </summary>
    /// <remarks>
    /// **委托给唯一写者 <see cref="Select"/>**：只清各模块的 <c>IsSelected</c> 而不清
    /// <see cref="SelectedInstanceId"/> 会让"再次选中同一模块"被吞掉（边框/手柄再也出不来）。
    /// </remarks>
    public void ClearSelection() => Select(null);

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
    /// 尺寸用**模块声明的默认尺寸 × 缩放**估算（真实尺寸要等窗口打开才知道 —— 那时按
    /// <c>[ModuleDefinition]</c> 的窗口尺寸开窗）。有循环上限兜底，极端情况下宁可重叠也不死循环。
    /// </remarks>
    /// <param name="instance">即将摆放的实例（默认尺寸从它的声明面读）。</param>
    /// <returns>摆放位置（**DIP**，与模型同一坐标系）。</returns>
    private (double X, double Y) NextPlacement(ModuleInstanceView instance)
    {
        ArgumentNullException.ThrowIfNull(instance);

        // 模块没有声明默认尺寸时只求"别算出 0 尺寸"：摆放只关心"互不堆叠"，
        // 不在这里造第二份默认尺寸 —— 默认尺寸的唯一来源是 `[ModuleDefinition]`，
        // 框架地板在窗口模型构造时由 `PortMarkerMetrics.MinWindowExtent` 兜底。
        // **尺寸是 DIP（声明就是 DIP）** ⇒ 不乘缩放；而 `ShellRect` / `WorkArea` 由窗口管理器
        // 按同一口径（DIP）发布 ⇒ 本方法整段都在 DIP 里算，位置与尺寸不再混单位。
        var window = instance.Window;
        var width = Math.Max(1, window is { Width: > 0 } declaredWidth ? declaredWidth.Width : 1);
        var height = Math.Max(1, window is { Height: > 0 } declaredHeight ? declaredHeight.Height : 1);

        var shell = ShellRect is { IsValid: true } rect ? rect : (CanvasRect?)null;
        var baseX = shell is { } s ? s.Right + PlaceGap : PlaceOrigin;
        var baseY = shell is { } s2 ? s2.Y : PlaceOrigin;

        var work = WorkArea is { IsValid: true } area ? area : (CanvasRect?)null;

        var x = baseX;
        var y = baseY;

        for (var guard = 0; guard < 256; guard++)
        {
            if (work is { } area2 && y + height > area2.Bottom)
            {
                // 一列到底 → 换右边一列
                x += width + PlaceGap;
                y = baseY;
            }

            if (!OverlapsAnyModule(x, y, width, height))
            {
                return (x, y);
            }

            y += height + PlaceGap;
        }

        return (x, y);
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
                // 尺寸还没读回来（窗口未打开）→ 用**声明面给出的默认尺寸**估（同为 DIP）
                var w = module.PreferredWidth;
                var h = module.PreferredHeight;
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
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var index = 0;

        foreach (var instance in instances)
        {
            _ = seen.Add(instance.InstanceId);
            index++;

            if (Find(instance.InstanceId) is { } existing)
            {
                existing.Apply(instance);
                continue;
            }

            // 新模块：**只分配错位摆放的位置**；尺寸由模块的 `[ModuleDefinition]` 决定。
            // 窗口打开后（ModuleWindowManager.PublishActualSize）把**实际尺寸**读回并经 CommitInitialLayout 落盘。
            // 这里若写回"模型算出来的默认尺寸"，等于让代码决定外层容器尺寸（那是模块声明的职责）。
            // 摆放**在右侧排开、互不堆叠**（见 NextPlacement）—— 从左上角斜着叠会压在主窗口上且互相堆叠。
            var placement = NextPlacement(instance);
            var created = new ModuleViewViewModel(
                instance,
                layout: null,
                _query,
                _command,
                placement.X,
                placement.Y,
                _viewState);

            Modules.Add(created);

            GeometryChanged?.Invoke(
                this,
                new ModuleWindowGeometryChange(created.InstanceId, created.X, created.Y, created.Width, created.Height));

            if (created.IsViewVisible)
            {
                VisibilityChanged?.Invoke(this, new ModuleWindowVisibilityChange(created.InstanceId, IsVisible: true));
            }
        }

        foreach (var stale in Modules.Where(m => !seen.Contains(m.InstanceId)).ToList())
        {
            _ = Modules.Remove(stale);
            VisibilityChanged?.Invoke(this, new ModuleWindowVisibilityChange(stale.InstanceId, IsVisible: false));
        }

        // 几何：拖动/缩放中的成员由交互过程主导（避免与指针打架）
        var layoutById = new Dictionary<string, ModuleLayoutView>(StringComparer.Ordinal);
        foreach (var layout in _query.GetLayout())
        {
            layoutById[layout.InstanceId] = layout;
        }

        foreach (var module in Modules)
        {
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
            if (layoutById.TryGetValue(module.InstanceId, out var layout)
                && HasGeometryDifference(module, layout))
            {
                module.ApplyLayout(layout);
                RaiseGeometry(module);
            }
        }

        // **选中态对账**：模块可能刚被移除/重建（选中标志默认 false，而 SelectedInstanceId 还在）。
        // 每帧按唯一事实源重算一次；无变化不触发通知，成本只是 O(模块数) 的相等判断。
        Select(SelectedInstanceId);

        OnPropertyChanged(nameof(ModuleCount));
    }

    /// <summary>
    /// **拓扑结构签名**（网络成员 + 每条网络是否停流 + 各口标签）——**只含结构、不含计数**。
    /// </summary>
    /// <remarks>
    /// 用途：判断"结构是否变了"。计数（投递/丢弃/速率）会随数据流动一直变，**不能进签名**
    /// （否则每帧都会触发整套刷新）；它们仍走约 0.5 秒的节拍。
    /// </remarks>
    private string BuildStructureSignature()
    {
        var builder = new System.Text.StringBuilder(256);

        foreach (var network in _query.GetNetworks())
        {
            builder.Append(network.Key).Append(network.IsStalled ? '!' : '+');
            foreach (var target in network.Targets)
            {
                builder.Append(target).Append(',');
            }

            builder.Append(';');
        }

        foreach (var module in Modules)
        {
            builder.Append(module.InstanceId).Append('#');
            foreach (var port in module.Ports)
            {
                builder.Append(port.PortName).Append('=').Append(port.Label).Append(',');
            }

            builder.Append(';');
        }

        return builder.ToString();
    }

    /// <summary>
    /// **取完某实例的挂起"清空显示"请求**（模块 → 框架的请求方向；`null` = 本帧没有请求）。
    /// </summary>
    /// <param name="instanceId">实例 ID。</param>
    /// <returns>被请求清空的**数据源名集合**；没有请求时为 <see langword="null"/>。</returns>
    /// <remarks>
    /// 一次性取完（而不是"每遇到一个显示元素取一个"）的理由见调用处的注释：**顺序不该影响语义**，
    /// 而且按元素逐个取会在源名不匹配时把请求**丢掉**。
    /// </remarks>
    private HashSet<string>? DrainClears(string instanceId)
    {
        if (_viewState is null)
        {
            return null;
        }

        HashSet<string>? clears = null;

        while (_viewState.TryTakeClear(instanceId, out var source) && source is { Length: > 0 })
        {
            clears ??= new HashSet<string>(StringComparer.Ordinal);
            _ = clears.Add(source);
        }

        return clears;
    }

    /// <summary>
    /// **落地某实例挂起的"替换显示源文本"请求**（消息 / 状态这类只有当前值的内容）。
    /// </summary>
    /// <param name="instanceId">实例 ID。</param>
    /// <remarks>
    /// 与追加同一模型（模块线程只入队、UI 线程落地），差别在语义：**替换**而不是追加。
    /// 落地时顺带**丢掉该源还没落地的追加** —— 替换与追加混在一起没有确定含义，
    /// 保留旧的追加会让"刚设的消息"被几行旧日志顶掉。
    /// </remarks>
    private void DrainSets(string instanceId)
    {
        if (_viewState is null)
        {
            return;
        }

        while (_viewState.TryTakeSetText(instanceId, out var source, out var text) && source is { Length: > 0 })
        {
            // 缓冲换了内容 ⇒ 视图下一帧读到新行（控件按内容比较决定要不要重排）。
            _viewState.Display(instanceId, source).SetText(text ?? string.Empty);
        }
    }

    /// <summary>
    /// **落地某实例挂起的"追加文本"请求**（WinForms 模型）。
    /// </summary>
    /// <param name="instanceId">实例 ID。</param>
    /// <remarks>
    /// 模块线程只入队（<c>IViewDisplayAccess.AppendText/AppendLine</c>），**显示缓冲的唯一写者仍是 UI 线程**
    /// —— 界面绑定的行集合绝不被跨线程改。本方法在**每帧刷新**里把队列取空并按入队顺序落地：
    /// 「不换行」=`AppendText`（数据直接接在后面、什么都不加）、「通道换行」=`AppendLine`。
    /// <para>清空请求先于追加落地（同一个节拍内的语义见 <c>ViewStateStore</c> 的说明）。</para>
    /// </remarks>
    private void DrainAppends(string instanceId)
    {
        if (_viewState is null)
        {
            return;
        }

        // 取空再落地：取的时候持存储锁、落地时持缓冲锁，两把锁不嵌套（避免锁序问题）
        foreach (var append in _viewState.DrainAppends(instanceId))
        {
            var buffer = _viewState.Display(instanceId, append.SourceName);

            if (append.LineBreak)
            {
                buffer.AppendLine(append.Text);
            }
            else
            {
                buffer.AppendText(append.Text);
            }
        }
    }
    private void RefreshSubscriptions()
    {
        _frameCounter++;
        RefreshRateHz = _query.GetSettings().UiRefreshRateHz;

        // 吸附距离（设置项，默认 4，2–8）：在线即时生效 —— 每次刷新同步一次，拖动时按它取容差
        SnapDistance = Math.Clamp(_query.GetSettings().SnapDistance, 2, 8);

        // 多行输入提交键（G2，设置项，默认 CtrlEnter）：**在线即时生效** —— 每次刷新同步一次，
        // 再由 `RefreshOverlayPeers()` 下发到各模块窗口（与叠加层开关同一条路径）
        MultilineSubmitKey = _query.GetSettings().MultilineSubmitKey;

        _sources.Clear();
        foreach (var source in _query.GetSubscriptionSources())
        {
            _sources[(source.InstanceId, source.DataSourceName)] = source;
        }

        // 【结构变了就立刻刷新】**连接状态/停流/角色投影**都以**网络成员关系**为唯一来源（"指标来源唯一"）：
        // 它们不是"每 0.5 秒才追上"的滞后态 —— 建网/断网/改标签/增删模块的**当帧**就刷新，
        // 否则用户刚连上却看不到连接灯与连线（"看得见"是功能的一部分）。
        var structure = BuildStructureSignature();
        var structureChanged = !string.Equals(structure, _structureSignature, StringComparison.Ordinal);
        _structureSignature = structure;

        foreach (var module in Modules)
        {
            // **本帧先把该实例的挂起清空请求
            // 一次取完**（若在"每遇到一个 A5 元素"时才 `TryTakeClear` 一次 ⇒ ① 顺序不对就**丢掉**
            // 别的源的请求；② 语义上也不该被元素遍历顺序左右）。取到的是一个**集合**，供下面的元素循环按源名匹配。
            var clears = DrainClears(module.InstanceId);

            // **顺序纪律：清空先落地，追加后落地**（模块先 `Clear` 再 `Append` 的语义不能被倒过来）。
            // 缓冲的写者只有这一处（UI 线程）⇒ 两边都安全。
            if (clears is not null && _viewState is not null)
            {
                foreach (var name in clears)
                {
                    _viewState.Display(module.InstanceId, name).Clear();

                    // 告诉**新表面**这个缓冲被清空了：不通知 ⇒ 控件手里的快照看起来还是"没变"
                    // （行数与末行都没动），于是一直显示旧行 —— 表现为"清空按钮按了没反应"。
                }
            }

            // 本帧把挂起的"替换显示源文本"落地（消息 / 状态这类**只有当前值**的内容）
            DrainSets(module.InstanceId);

            // 本帧把挂起的"追加文本"落地（数据到达即落地，不存在"半行/pending tail"）
            DrainAppends(module.InstanceId);

            // 口标记的网络信息（连接状态 + **投递量与估算字节**）与**角色方块**：
            // ① **结构变化时**立即刷一次；② 否则按约 0.5 秒的节拍刷（指标会随数据流动持续变化）
            if (structureChanged || _frameCounter % 30 == 0)
            {
                RefreshMarkerStatus(module);
                RefreshRoleBoxes(module);
            }

            // **表面每帧刷一次**：取数、就地更新、输入反馈回填都在这一条路里。
            module.RefreshSurface();
        }

        // **角色投影第二遍**：连线的对端锚点要用**对端方块的位置** ⇒ 必须等所有模块的方块都摆好
        if (structureChanged || _frameCounter % 30 == 0)
        {
            foreach (var module in Modules)
            {
                RefreshRoleLinks(module);
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
    private void RefreshMarkerStatus(ModuleViewViewModel module)
    {
        if (module.Ports.Count == 0)
        {
            return;
        }

        var networks = _query.GetNetworks();

        foreach (var marker in module.Ports)
        {
            var literal = $"{module.InstanceId}.{marker.PortName}";
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
            // 有连接但这一秒没数据 ⇒ `0.00 B/s`（**不拿 `---` 冒充 0** —— 再低的速率也是有数据，
            // 如实显示，没有数据就写 0.00，--- 表示无连接）。
            marker.RateText = FormatRate(metrics.PayloadBytesPerSecond);
        }
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
        var title = string.IsNullOrWhiteSpace(module.Title) ? "模块" : module.Title;
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

        foreach (var flow in flows)
        {
            var link = new RoleLinkViewModel(
                flow.LocalRoleKey,
                flow.LocalIsSend,
                flow.PeerInstanceId,
                flow.PeerRoleKey,
                flow.PortPairCount);

            // 对端角色方块中心（本窗局部坐标）；算不出 ⇒ 对端窗口中心
            link.Target = ResolvePeerAnchor(module, flow) ?? ResolvePeerCenter(module, flow.PeerInstanceId);
            link.PeerAdjacent = IsPeerAdjacent(module, flow.PeerInstanceId);
            link.PeerText = link.PeerAdjacent
                ? null
                : Find(flow.PeerInstanceId) is { } peer
                    ? OverlayIdentity(peer)
                    : null;

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
            parts.Add($"标称 {nominal:0.#} Hz");
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

        // 世界增量：**两端同单位（DIP）⇒ 直接相减**
        var dx = peer.Rect.X - module.Rect.X;
        var dy = peer.Rect.Y - module.Rect.Y;

        return new Avalonia.Point(box.Center.X + dx, box.Center.Y + dy);
    }

    /// <summary>回退：对端**窗口中心**（本窗局部坐标，**DIP**，同单位直接相减）。</summary>
    private Avalonia.Point ResolvePeerCenter(ModuleViewViewModel module, string peerInstanceId)
    {
        if (Find(peerInstanceId) is { } peer && peer.Rect.IsValid && module.Rect.IsValid)
        {
            return new Avalonia.Point(
                peer.Rect.X + (peer.Rect.Width / 2) - module.Rect.X,
                peer.Rect.Y + (peer.Rect.Height / 2) - module.Rect.Y);
        }

        return new Avalonia.Point(module.CanvasWidth / 2, module.CanvasHeight / 2);
    }

    /// <summary>对端是否与本模块**拼接贴合**（按实例 ID 判；对端不在现场 ⇒ false ⇒ 末端画断口 + 标对端）。</summary>
    private bool IsPeerAdjacent(ModuleViewViewModel module, string peerInstanceId) =>
        Find(peerInstanceId) is { } peer
        && peer.Rect.IsValid
        && module.Rect.IsValid
        && AreNodesJoined((module.InstanceId, module.Rect), (peer.InstanceId, peer.Rect), GroupTolerance);
    /// <summary>
    /// **速率文本**（每秒字节数）：**如实显示**，不做"低于阈值就显示占位符"的模糊化。
    /// </summary>
    /// <param name="bytesPerSecond">每秒搬运字节数（框架算，界面只格式化）。</param>
    /// <returns>`0.00 B/s` / `12.34 B/s` / `1.44 KB/s` …（再低的速率也是有数据，如实显示）。</returns>
    private static string FormatRate(double bytesPerSecond) =>
        bytesPerSecond < 1024
            ? $"{bytesPerSecond:0.00} B/s"
            : $"{bytesPerSecond / 1024:0.00} KB/s";

    private static string FormatBytes(long bytes) => bytes switch
    {
        < 1024 => $"{bytes} B",
        < 1024 * 1024 => $"{bytes / 1024.0:F1} KB",
        < 1024L * 1024 * 1024 => $"{bytes / (1024.0 * 1024):F1} MB",
        _ => $"{bytes / (1024.0 * 1024 * 1024):F2} GB",
    };

    private IReadOnlyList<(string Id, CanvasRect Rect)> ModuleRects() =>
        [.. Modules.Where(m => m.IsViewVisible).Select(m => (m.InstanceId, m.Rect))];

    /// <summary>外壳在**拼图图**里的节点 id（与实例 id 不会冲突）。</summary>
    public const string ShellNodeId = "#shell";

    /// <summary>拼图图的全部节点：外壳（若可用）+ 可见模块（成组判定的输入）。</summary>
    /// <param name="shellOverride">用指定的外壳矩形代替模型里的当前值（**分组判定要用移动前的几何**）。</param>
    private List<(string Id, CanvasRect Rect)> TilingNodes(CanvasRect? shellOverride = null)
    {
        var nodes = ModuleRects().ToList();
        var shell = shellOverride ?? ShellRect;

        if (shell is { IsValid: true } validShell)
        {
            nodes.Add((ShellNodeId, validShell));
        }

        return nodes;
    }

    /// <summary>
    /// 两节点是否"拼在一起"（成组谓词）。
    /// </summary>
    /// <remarks>
    /// <list type="bullet">
    ///   <item>模块 ↔ 模块：沿用 <see cref="SnapEngine.AreJoined"/>（相邻边贴合 + 交叉轴有重叠）；</item>
    ///   <item><b>模块 ↔ 外壳：只有"模块左边缘贴外壳右边缘"算</b>（外壳只提供右边缘），
    ///     不允许贴它的左/上/下边缘；</item>
    ///   <item><b>外壳用严格容差</b>（<see cref="ShellJoinTolerance"/>）：模块间用
    ///     <see cref="GroupTolerance"/>（贴合容差 8px，"差一点也算拼上"对拼装手感友好），但**外壳不能用它** ——
    ///     否则"停在离外壳 8px 处、根本没吸附"的模块也被判成已拼上，拖动主窗口时被一并带走，
    ///     表现为"没产生吸附行为却粘在主窗口上"。
    ///     外壳侧的判据是"**真的贴上了**"⇒ 吸附产生的 0 间隙（允许 1px 取整误差）。</item>
    /// </list>
    /// </remarks>
    private static bool AreNodesJoined(
        (string Id, CanvasRect Rect) a,
        (string Id, CanvasRect Rect) b,
        double tolerance)
    {
        if (string.Equals(a.Id, ShellNodeId, StringComparison.Ordinal) || string.Equals(b.Id, ShellNodeId, StringComparison.Ordinal))
        {
            var (shell, module) = string.Equals(a.Id, ShellNodeId, StringComparison.Ordinal) ? (a.Rect, b.Rect) : (b.Rect, a.Rect);
            var overlap = Math.Min(module.Bottom, shell.Bottom) - Math.Max(module.Top, shell.Top);
            return Math.Abs(module.Left - shell.Right) <= Math.Min(tolerance, ShellJoinTolerance)
                && overlap > ShellJoinTolerance;
        }

        return SnapEngine.AreJoined(a.Rect, b.Rect, tolerance);
    }

    /// <summary>某节点所在的**拼图组**（接触连通分量，含自身；外壳也参与）。</summary>
    /// <param name="nodeId">起始节点 id（外壳用 <see cref="ShellNodeId"/>）。</param>
    /// <param name="shellOverride">用指定的外壳矩形参与分组（**移动前的几何**）。</param>
    private HashSet<string> GroupOfNode(string nodeId, CanvasRect? shellOverride = null)
    {
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

                if (AreNodesJoined(nodes[currentIndex], nodes[i], GroupTolerance))
                {
                    _ = group.Add(nodes[i].Id);
                    queue.Enqueue(nodes[i].Id);
                }
            }
        }

        return group;
    }

    /// <summary>
    /// **参照线**集合：其他模块的外框（4 边 + 中心线）+ 外壳边。
    /// </summary>
    /// <remarks>
    /// 外壳只是"**只暴露一条边的对象**"，与模块进同一张线表（不做特殊通道）。
    /// 同组成员与自身不提供外框线（自己不能吸自己）。
    /// </remarks>
    private List<SnapLine> CollectLines(string draggingId)
    {
        var lines = new List<SnapLine>();

        foreach (var module in Modules.Where(m => m.IsViewVisible
            && !_dragGroup.Contains(m.InstanceId, StringComparer.Ordinal)
            && !string.Equals(m.InstanceId, draggingId, StringComparison.Ordinal)))
        {
            lines.AddRange(SnapEngine.LinesOf(module.Rect));
        }

        if (ShellRect is { IsValid: true } shell)
        {
            lines.Add(SnapEngine.LineOf(new SnapReferenceLine(true, shell.X + shell.Width, shell.Y, shell.Y + shell.Height)));
        }

        return lines;
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
        if (Find(draggingId) is not { } moving)
        {
            return [];
        }

        var pairs = new List<PortAnchorPair>();

        foreach (var other in Modules.Where(m => m.IsViewVisible
            && !_dragGroup.Contains(m.InstanceId, StringComparer.Ordinal)
            && !string.Equals(m.InstanceId, draggingId, StringComparison.Ordinal)))
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

                    // 端口的对齐轴 = 它所在的边：左/右边 → 约束 Y（横向参照线）；上/下边 → 约束 X
                    var alignsX = !mine.IsVertical;
                    var otherCoordinate = alignsX
                        ? other.X + (other.Width * theirs.Offset)
                        : other.Y + (other.Height * theirs.Offset);

                    pairs.Add(new PortAnchorPair(alignsX, otherCoordinate, other.Rect, mine.Offset));
                }
            }
        }

        return pairs;
    }
}

/// <summary>窗口几何变化事件参数（屏幕坐标，物理像素）。</summary>
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
