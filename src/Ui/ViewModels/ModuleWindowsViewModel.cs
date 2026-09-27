using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using HYWDebugAssistant.Core.Api;
using HYWDebugAssistant.Ui.Controls;
using HYWDebugAssistant.Ui.Geometry;
using HYWDebugAssistant.Ui.Runtime;
using HYWDebugAssistant.Ui.Views;

namespace HYWDebugAssistant.Ui.ViewModels;

/// <summary>窗口上的缩放手柄（边框的边中点/角点 —— Office 直觉，ADR-0034 §2.2）。</summary>
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
///   <item><b>没有"工作区"容器</b>：本类不持有画布、不画背景、不做边界钳制——背景就是用户桌面（§10.1-3）；</item>
///   <item><b>坐标 = 屏幕坐标（物理像素）</b>：拖动/缩放直接改窗口位置与大小；吸附把**屏幕工作区、
///     框架外壳窗口、其他模块窗口**一并作为参照（`11` §2.1）；</item>
///   <item><b>组关系由位置派生</b>（C5-1）：拖动开始时求贴合连通分量并冻结、期间整组位移、结束后重算；
///     按住 Ctrl 或选中态"独立拖动"只动一个；</item>
///   <item><b>本类与窗口技术无关</b>（只操作数值几何）→ 可独立单元测试；真正的开窗/关窗在窗口管理器。</item>
/// </list>
/// </remarks>
public sealed partial class ModuleWindowsViewModel : ViewModelBase
{
    /// <summary>吸附容差（px；Gate C 定稿前用 8，C5-1 约束 4）。</summary>
    /// <summary>对齐类吸附容差（px）——**默认值**；实际值取设置里的「吸附距离」（见 <see cref="SnapDistance"/>）。</summary>
    public const double SnapTolerance = SnapEngine.DefaultTolerance;

    /// <summary>贴合（接触拼接）容差（px）——**宽**：拼接是主用途，值得给宽容差；同时它也是"是否算一组"的口径。</summary>
    public const double GroupTolerance = SnapEngine.FlushTolerance;

    /// <summary>
    /// **当前生效的吸附距离**（px）——【ADR-0042 D6 / 用户裁决】设置项 `snapDistance`（默认 4，2–8），
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
    private readonly Dictionary<(string InstanceId, string Source), long> _textReadCounts = [];

    private int _frameCounter;

    /// <summary>上一次看到的**拓扑结构签名**（网络成员 + 口标签）——变了就立刻刷新连接状态与角色投影。</summary>
    private string _structureSignature = string.Empty;

    /// <summary>本次拖动**起点**的各成员几何（拖动中按 Ctrl 拆分时用来"放回原位"）。</summary>
    private readonly Dictionary<string, CanvasRect> _dragStartRects = new(StringComparer.Ordinal);

    private readonly byte[] _sampleScratch = new byte[1024];
    private readonly byte[] _seriesScratch = new byte[CurveElementViewModel.MaxPoints * 8];
    private readonly byte[] _textScratch = new byte[4096];
    private readonly double[] _seriesValues = new double[CurveElementViewModel.MaxPoints];

    private string? _draggingId;
    private IReadOnlyList<string> _dragGroup = [];
    private double _dragPointerOffsetX;
    private double _dragPointerOffsetY;

    /// <summary>【临时诊断】当前拖动组的成员数。</summary>
    internal int DragGroupSize => _dragGroup.Count;

    /// <summary>【临时诊断】参与吸附的其他模块数（最近一帧）。</summary>
    internal int SnapNeighbourCount { get; private set; }

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
    /// 【用户第十二轮第四轮反馈】"主窗口与模块形成一个视觉整体后，移动谁都是整体动"——
    /// 外壳不是"应用的框架"，而是**拼图里的一个节点**；因此拖模块与拖外壳走的是同一套成组逻辑。
    /// </remarks>
    public event EventHandler<(double Dx, double Dy)>? ShellTranslationRequested;

    private string? _resizingId;
    private ResizeHandle _resizeHandle;
    private CanvasRect _resizeStartRect;

    /// <summary>创建控制器。</summary>
    /// <param name="query">查询面（UI 唯一读通道）。</param>
    /// <param name="command">命令面（UI 唯一写通道）。</param>
    public ModuleWindowsViewModel(IRuntimeQuery query, IRuntimeCommand command)
    {
        ArgumentNullException.ThrowIfNull(query);
        ArgumentNullException.ThrowIfNull(command);

        _query = query;
        _command = command;
    }

    /// <summary>模块窗口（每个模块一项）。</summary>
    public ObservableCollection<ModuleViewViewModel> Modules { get; } = [];

    /// <summary>屏幕缩放（1.0 = 100%；由窗口管理器按主屏设置）。</summary>
    [ObservableProperty]
    public partial double Scale { get; set; } = 1.0;

    /// <summary>屏幕工作区（物理像素；外壳窗口关闭时为 null = 不做屏幕吸附与钳制）。</summary>
    [ObservableProperty]
    public partial CanvasRect? WorkArea { get; set; }

    /// <summary>框架外壳窗口矩形（拼图"起始点"，可被贴靠 —— O-6）。</summary>
    [ObservableProperty]
    public partial CanvasRect? ShellRect { get; set; }

    /// <summary>
    /// 是否显示**叠加信息层**（原 `ShowPortMarkers`：**端口标记改为常显**，开关改为控制叠加层）。
    /// **默认关闭**【ADR-0045 D1：它是调试/观测层，不进常驻界面】。
    /// </summary>
    [ObservableProperty]
    public partial bool ShowInfoOverlay { get; set; }

    /// <summary>吸附是否启用（交互组开关；按住 Alt 可临时停用）。</summary>
    [ObservableProperty]
    public partial bool SnapEnabled { get; set; } = true;

    /// <summary>拖动中的吸附辅助线（屏幕坐标）。</summary>
    [ObservableProperty]
    public partial IReadOnlyList<GuideLine> Guides { get; set; } = [];

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
    /// 【来源唯一 · ADR-0049 D4】叠加层的**连线**已完全改由**角色对**（`GetRolePairs`）驱动，
    /// 不再读标记上的"对端方位/是否贴合" ⇒ 那三个字段（`PeerSide`/`PeerAdjacent`/`HasLink`）已删除。
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

            foreach (var target in network.Targets)
            {
                peers[target] = network.Source;
            }

            if (network.Targets.Count > 0)
            {
                peers[network.Source] = network.Targets[0];
            }
        }

        foreach (var module in Modules)
        {
            module.ShowInfoOverlay = ShowInfoOverlay;

            foreach (var marker in module.Ports)
            {
                _ = peers.TryGetValue($"{marker.InstanceId}.{marker.PortName}", out var peer);
                marker.PeerText = peer;
            }
        }
    }

    /// <summary>
    /// **类型颜色改了** ⇒ 让所有端口标记重读类型画刷【ADR-0054：数据结构 → 颜色映射表】。
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

    /// <summary>选中一个模块（点击边框带时调用；锁定或未选中都不影响数据）。</summary>
    /// <param name="instanceId">实例 ID（null = 取消选择）。</param>
    public void Select(string? instanceId)
    {
        if (string.Equals(SelectedInstanceId, instanceId, StringComparison.Ordinal))
        {
            return;
        }

        if (SelectedInstanceId is { Length: > 0 } previous
            && Modules.FirstOrDefault(m => string.Equals(m.InstanceId, previous, StringComparison.Ordinal)) is { } previousModule)
        {
            previousModule.IsSelected = false;
        }

        SelectedInstanceId = instanceId;

        if (instanceId is { Length: > 0 } current
            && Modules.FirstOrDefault(m => string.Equals(m.InstanceId, current, StringComparison.Ordinal)) is { } module)
        {
            module.IsSelected = true;
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

    /// <summary>切换某模块的**运行 / 停用**（停用 = 保留实例、拓扑与配置，只停流 —— §8.4）。</summary>
    /// <param name="instanceId">实例 ID。</param>
    public void ToggleEnabled(string instanceId)
    {
        if (Find(instanceId) is not { } module)
        {
            return;
        }

        _ = _command.SetModuleEnabled(instanceId, !module.IsEnabled);
    }

    /// <summary>隐藏某个模块窗口（模块继续运行，只收起表现层 —— §10.1）。</summary>
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
    /// 开始拖动（C5-1 约束 1：**此刻**求贴合连通分量并冻结）。锁定或不可见时不进入拖动。
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

        // 【用户第十一轮反馈】**锁定成员不随组移动**：编组时就把锁定的成员排除，
        // 于是拖动整组时它们留在原地（组自然"解开"）—— 这比"拖完再纠正"更直观。
        // 【用户第十二轮第四轮反馈】**外壳也在这个组里**（单侧吸附完成即同组）：
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

        // 【用户第十二轮反馈 · `architecture/15` §3.1】组内成员**也由指针驱动**：
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
    /// 【用户第十七轮反馈】原交互要求"**拖动前**按住 Ctrl 才独立拖动"，拖动开始后按 Ctrl 无效 ——
    /// 用户："建议拖动期间按下后立刻断开，更符合常见类似功能的操作习惯"。
    /// <list type="bullet">
    ///   <item>**语义等价于"从一开始就按住 Ctrl"**：其余成员（含外壳）**回到本次拖动的起点**，被拖模块继续跟着指针；</item>
    ///   <item>**单向**：拆分后即使松开 Ctrl 也不再重新合组（组在拖动开始时冻结 —— C5-1 约束 1）；</item>
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
    /// 这是"主窗口与模块形成视觉整体后，移动谁都是整体动"的落点（用户第十二轮第四轮反馈）。
    /// 真实窗口由 `ModuleWindowManager` 移动；它移动时会回推一次 `ShellRect`（交互期间只刷新参照、不再联动平移）。
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
    /// **外壳移动 = 平移"与外壳拼成一体的那一组"**（用户第十二轮第四轮反馈）。
    /// </summary>
    /// <remarks>
    /// <list type="bullet">
    ///   <item><b>外壳是拼图里的一个节点</b>（不是"应用的框架"）：它与模块之间只有**单侧吸附**（只提供右边缘 —— ADR-0038 D5）；
    ///     模块贴着它就算拼上，于是它们同属一个**接触连通分量**；</item>
    ///   <item><b>只有吸附完成后才整体运动</b>：移动外壳 → 只平移与它相连的那一组（**没拼上的模块留在原处**）；
    ///     与"拖动一个模块会带走整组"是**同一套成组逻辑**，只是把外壳也纳入进来；</item>
    ///   <item>之所以不再用"所有模块无条件跟随"：那会让与外壳毫无关系的模块也被拖走，不符合"拼装"的直觉；</item>
    ///   <item><b>锁定</b> = 钉在桌面上：既不在组内被带走，也不随外壳移动（沿用第十一轮裁决）；</item>
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
    /// （与 `implementation/02` P33 是同一类错误：**任何"谁和谁相连"的判定都必须用同一时刻的几何**）。
    /// </remarks>
    public void TranslateShellGroup(double dx, double dy, CanvasRect preMoveShell)
    {
        if ((Math.Abs(dx) < 0.5 && Math.Abs(dy) < 0.5) || !preMoveShell.IsValid)
        {
            return;
        }

        // **本次拖动开始时**就与外壳拼在一起的那些模块（冻结集合）：
        // 【用户第三十四轮裁定："单独拖动主窗口时不进行吸附行为，更符合直觉"】
        // 旧实现**逐帧**重算分组 ⇒ 拖着外壳**经过**某个模块时，那一帧它被判成"已拼上"并跟着走
        // ⇒ 现象正是"经过模块时导致模块**错位粘连**（没有产生吸附行为却拼上去了）"。
        // ⇒ 冻结集合只在**一次拖动开始时**算一次（用移动前的外壳矩形），拖动过程中**不再吸收新成员**。
        _shellDragGroup ??= GroupOfNode(ShellNodeId, preMoveShell);

        foreach (var module in Modules)
        {
            if (!_shellDragGroup.Contains(module.InstanceId) || module.IsLocked || !module.Rect.IsValid)
            {
                continue;
            }

            module.X = ScreenPixels.Round(module.X + dx);
            module.Y = ScreenPixels.Round(module.Y + dy);
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
    /// <param name="pointerX">指针 X（屏幕坐标）。</param>
    /// <param name="pointerY">指针 Y（屏幕坐标）。</param>
    /// <param name="snapEnabled">本次是否吸附（null = 用开关值；按住 Alt 传 false）。</param>
    /// <remarks>
    /// 【用户第十二轮第二轮反馈 · **保持拖动中实时吸附**】曾试过"松手才吸附"，被用户否决（操作不友好）；
    /// 目标手感 = **Visio 式**：吸附半径小（几像素）、离开即松、靠参考线给反馈（详见 ADR-0042）。
    /// 另：位置在写入模型前**统一取整到整数物理像素**（<see cref="ScreenPixels.Round"/>），修"缝/错位"。
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

        // 统一取整：模型几何一律是整数物理像素（唯一量化点）
        var dx = ScreenPixels.Round(targetX) - module.X;
        var dy = ScreenPixels.Round(targetY) - module.Y;

        // 【临时诊断】控制器侧一帧（原始目标 → 吸附后、实际施加的位移、组与邻居数）
        Diagnostics.DragDiagnostics.Record(Diagnostics.DragDiagnostics.FormatSnap(
            raw: (X: pointerX - _dragPointerOffsetX, Y: pointerY - _dragPointerOffsetY),
            finalX: targetX,
            finalY: targetY,
            appliedDx: dx,
            appliedDy: dy,
            sticky: $"group={_dragGroup.Count}",
            groupSize: _dragGroup.Count,
            neighbourCount: neighbourCount));

        SnapNeighbourCount = neighbourCount;

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

        Guides = guides;

        // 窗内绘制用：辅助线挂到**被拖的模块**上（其余成员只跟随位移）
        if (Find(draggingId) is { } dragged)
        {
            dragged.Guides = guides;
        }

        // **即将自动建网**预览（ADR-0037）：按"接触且对齐"规则问 L4"现在松手会连上什么"，
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

    /// <summary>结束拖动：**一次 ChangeSet** 提交整组位置（原子），随后清辅助线（C5-1 约束 1）。</summary>
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
        Guides = [];
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
        Guides = [];

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
        var minWidth = ModuleViewViewModel.MinWidth * module.Scale;
        var minHeight = ModuleViewViewModel.MinHeight * module.Scale;

        var left = start.Left;
        var top = start.Top;
        var right = start.Right;
        var bottom = start.Bottom;

        if (_resizeHandle is ResizeHandle.Left or ResizeHandle.TopLeft or ResizeHandle.BottomLeft)
        {
            left = Math.Min(pointerX, right - minWidth);
        }

        if (_resizeHandle is ResizeHandle.Right or ResizeHandle.TopRight or ResizeHandle.BottomRight)
        {
            right = Math.Max(pointerX, left + minWidth);
        }

        if (_resizeHandle is ResizeHandle.Top or ResizeHandle.TopLeft or ResizeHandle.TopRight)
        {
            top = Math.Min(pointerY, bottom - minHeight);
        }

        if (_resizeHandle is ResizeHandle.Bottom or ResizeHandle.BottomLeft or ResizeHandle.BottomRight)
        {
            bottom = Math.Max(pointerY, top + minHeight);
        }

        // **尺寸吸附**（Visio 式，用户要求）：拖动边时，若结果尺寸接近其他模块的宽/高 → 吸附到该尺寸，
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

        module.X = ScreenPixels.Round(left);
        module.Y = ScreenPixels.Round(top);
        module.Width = ScreenPixels.Round(right - left);
        module.Height = ScreenPixels.Round(bottom - top);
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

    // ---------------------------------------------------------------- 端口标签（建网）

    /// <summary>提交端口网络标签（**唯一建网机制**：同名即连）。</summary>
    /// <param name="module">所属模块视图。</param>
    /// <param name="marker">端口标记。</param>
    /// <returns>结果文本（成功/失败原因）。</returns>
    /// <remarks>
    /// 【缺陷修复】失败时**保持编辑框打开**并把原因显示在框内：此前无论成功失败都先关框，
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

        // 【用户第十三轮反馈】原 `marker.EditResult = message`（悬停里显示"上次操作"）**已删除**：
        //   ① 悬停提示不该携带"最近一次操作结果"这类**瞬时状态**；② 该数据本身也不再保存（见 `PortMarkerViewModel`）。
        //   结果文本仍然**返回并送到状态栏**（`RaiseNotice`）—— 反馈闭环不丢，只是不再挂在标记上。

        // **反馈闭环**：提示送到状态栏（此前返回值被调用方丢弃 → 用户完全看不到结果）
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

    /// <summary>
    /// **还原模块**【用户第十一轮反馈 · 替代原「暂停」】：回到默认尺寸，可选连配置一起还原。
    /// </summary>
    /// <remarks>
    /// 默认尺寸 = **模块声明的首选尺寸**（`ViewDeclaration.PreferredWidth/Height`；未声明则框架默认），
    /// **不是**按元素类型估算的结果；位置不变（只还原尺寸与配置，不动用户摆放的位置）。
    /// </remarks>
    /// <param name="module">模块视图。</param>
    /// <param name="scope">还原范围（只尺寸 / 尺寸与配置）。</param>
    public void ApplyReset(ModuleViewViewModel module, ModuleResetScope scope)
    {
        ArgumentNullException.ThrowIfNull(module);

        // ① 尺寸回到模块声明的首选尺寸（× 屏幕缩放，**取整到整数物理像素**），并写回布局
        module.Width = ScreenPixels.Round(module.PreferredWidth * module.Scale);
        module.Height = ScreenPixels.Round(module.PreferredHeight * module.Scale);
        module.X = ScreenPixels.Round(module.X);
        module.Y = ScreenPixels.Round(module.Y);
        module.HasLayout = true;

        _ = _command.UpdateModuleLayout(module.ToLayout());
        RaiseGeometry(module);

        // ② 配置（可选）：清掉该实例已保存的配置，模块重新读到默认值并刷新表单
        if (scope == ModuleResetScope.SizeAndConfiguration)
        {
            var result = _command.ResetModuleConfiguration(module.InstanceId);
            module.ReloadConfigurationForms();

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
    /// 【用户第九轮反馈 · UI 开发方式】尺寸默认值归 XAML：这里只把窗口的**实际尺寸**写进模型与布局，
    /// 不做任何"按内容估算"。之后用户的拖动/缩放照常覆盖。
    /// </remarks>
    /// <param name="module">模块视图。</param>
    public void CommitInitialLayout(ModuleViewViewModel module)
    {
        ArgumentNullException.ThrowIfNull(module);
        _ = _command.UpdateModuleLayout(module.ToLayout());
    }

    /// <summary>
    /// 清除全部选中【M1-16 / ADR-0039 D1】：点内容区 / 点外壳 / 点本应用其他窗口。
    /// </summary>
    /// <remarks>用户第六轮反馈："现在无法取消选中模块"——此前选中只能被"选中另一个"覆盖，没有退出口。</remarks>
    public void ClearSelection()
    {
        foreach (var module in Modules)
        {
            module.IsSelected = false;
        }
    }

    // ---------------------------------------------------------------- 内部

    private ModuleViewViewModel? Find(string instanceId) =>
        Modules.FirstOrDefault(m => string.Equals(m.InstanceId, instanceId, StringComparison.Ordinal));

    private void RaiseGeometry(ModuleViewViewModel module) =>
        GeometryChanged?.Invoke(
            this,
            new ModuleWindowGeometryChange(module.InstanceId, module.X, module.Y, module.Width, module.Height));

    /// <summary>
    /// 新模块的摆放位置（物理像素）：**从外壳右侧开始、竖着一列排开，互不堆叠**。
    /// </summary>
    /// <remarks>
    /// 【用户第十七轮反馈】原先新模块都落在左上角（`48,48` 起、每 40px 斜叠）⇒ **压在主窗口上、彼此重叠**。
    /// 现规则（纯几何、可单测）：
    /// <list type="number">
    ///   <item>起点 X = **外壳右边缘 + 间距**（外壳不可用时回退到 <see cref="PlaceOrigin"/>），起点 Y = 外壳上边缘（同回退）；</item>
    ///   <item>**已有的模块**逐个检查：与候选矩形相交就让位（向下挪一个"模块高 + 间距"）；</item>
    ///   <item>一列到底（超出工作区下沿）就**换到右面一列**（X 挪"模块宽 + 间距"，Y 回到起点）；
    ///     工作区不可用时不换列（无限向下，仍保证不堆叠）。</item>
    /// </list>
    /// 尺寸用**模块的声明默认尺寸 × 缩放**估算（真实尺寸要等窗口打开才知道 —— 布局归 XAML）。有循环上限兜底，
    /// 极端情况下宁可重叠也不死循环。
    /// </remarks>
    /// <returns>摆放位置（物理像素）。</returns>
    private (double X, double Y) NextPlacement()
    {
        var width = Math.Max(1, ModuleViewViewModel.DefaultWidth * Scale);
        var height = Math.Max(1, ModuleViewViewModel.DefaultHeight * Scale);

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
                return (ScreenPixels.Round(x), ScreenPixels.Round(y));
            }

            y += height + PlaceGap;
        }

        return (ScreenPixels.Round(x), ScreenPixels.Round(y));
    }

    /// <summary>候选矩形是否与**已有模块**相交（留 <see cref="PlaceGap"/> 的呼吸间距）。</summary>
    private bool OverlapsAnyModule(double x, double y, double width, double height)
    {
        var right = x + width;
        var bottom = y + height;

        foreach (var module in Modules)
        {
            if (module.Width <= 0 || module.Height <= 0)
            {
                // 尺寸还没读回来（窗口未打开）→ 用声明默认尺寸估
                var w = ModuleViewViewModel.DefaultWidth * Scale;
                var h = ModuleViewViewModel.DefaultHeight * Scale;
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

            // 新模块：**只分配错位摆放的位置**；尺寸由窗口的 **XAML 默认值**决定。
            // 窗口打开后（ModuleWindowManager.PublishActualSize）把**实际尺寸**读回并经 CommitInitialLayout 落盘。
            // 【用户第九轮反馈】此前这里就写回"模型算出来的默认尺寸"，等于让代码决定外层容器尺寸。
            // 【用户第十七轮反馈】原先是"左上角起、每 40px 斜着叠" ⇒ 新模块压在主窗口上且互相堆叠；
            // 现改为**在右侧排开、互不堆叠**（见 NextPlacement）。
            var placement = NextPlacement();
            var created = new ModuleViewViewModel(
                instance,
                layout: null,
                _query,
                _command,
                Scale,
                placement.X,
                placement.Y);

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

            // 【用户第十二轮第四轮反馈 · 关键】**整体平移尚未落盘**的模块同样不能被回写：
            // 刷新是 60Hz 的，若这里用"已落盘的旧布局"覆盖内存位置，模块会被一帧帧拉回原位 ——
            // 表现就是"移动主窗口时模块跟不动"（甚至来回抖）。落盘由防抖合并提交负责。
            if (_pendingSceneCommits.Contains(module.InstanceId))
            {
                continue;
            }

            if (layoutById.TryGetValue(module.InstanceId, out var layout))
            {
                // 【临时诊断】拖动期间"其他模块被回写"会让贴着它的模块跟着抖 —— 必须记下来
                var beforeX = module.X;
                var beforeY = module.Y;
                module.ApplyLayout(layout);

                if (_draggingId is { Length: > 0 }
                    && (Math.Abs(module.X - beforeX) > 0.5 || Math.Abs(module.Y - beforeY) > 0.5))
                {
                    Diagnostics.DragDiagnostics.RecordNeighbourWriteBack(module.InstanceId, beforeX, beforeY, module.X, module.Y);
                }

                RaiseGeometry(module);
            }
        }

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

    private void RefreshSubscriptions()
    {
        _frameCounter++;
        RefreshRateHz = _query.GetSettings().UiRefreshRateHz;

        // 吸附距离（设置项，默认 4，2–8）：在线即时生效 —— 每次刷新同步一次，拖动时按它取容差
        SnapDistance = Math.Clamp(_query.GetSettings().SnapDistance, 2, 8);

        _sources.Clear();
        foreach (var source in _query.GetSubscriptionSources())
        {
            _sources[(source.InstanceId, source.DataSourceName)] = source;
        }

        // 【结构变了就立刻刷新】**连接状态/停流/角色投影**都以**网络成员关系**为唯一来源（用户裁定"指标来源唯一"）：
        // 它们不再是"每 0.5 秒才追上"的滞后态 —— 建网/断网/改标签/增删模块的**当帧**就刷新，
        // 否则用户刚连上却看不到连接灯与连线（"看得见"是功能的一部分）。
        var structure = BuildStructureSignature();
        var structureChanged = !string.Equals(structure, _structureSignature, StringComparison.Ordinal);
        _structureSignature = structure;

        foreach (var module in Modules)
        {
            // 配置 Schema 读取要反射配置类型 → 不按帧重读：约每 2 秒一次，捕获外部（组合件加载/JSON 写回）变更
            if (_frameCounter % 120 == 0)
            {
                module.ReloadConfigurationForms();
            }

            // 口标记的网络信息（连接状态 + **投递量与估算字节**）与**角色方块**：
            // ① **结构变化时**立即刷一次；② 否则按约 0.5 秒的节拍刷（指标会随数据流动持续变化）
            if (structureChanged || _frameCounter % 30 == 0)
            {
                RefreshMarkerStatus(module);
                RefreshRoleBoxes(module);
            }

            foreach (var element in module.Elements)
            {
                if (element.DataSourceName is not { Length: > 0 } name
                    || !_sources.TryGetValue((module.InstanceId, name), out var source))
                {
                    continue;
                }

                switch (element)
                {
                    case ValueElementViewModel value:
                        value.Update(ReadLatest(source));
                        break;

                    case CurveElementViewModel curve:
                        curve.Update(ReadSeries(source));
                        break;

                    case TextLogElementViewModel log:
                        AppendTextLines(module.InstanceId, source, log);
                        break;

                    default:
                        break;
                }
            }
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
    ///   <item>诊断按**连接**（逐源绑定）统计：单源网络下同一网络的所有成员显示同一组数字；
    ///     **多源网络按源归属**取（键 = `源 → 宿子集`）；</item>
    ///   <item>字节数 = 投递单元数 × 发送方契约尺寸（**累计量**，可由此反推）；</item>
    ///   <item>**速率一律取框架口径**【ADR-0046 D8】：接收侧用**每 (实例,口) 队列**的搬运字节速率
    ///     （按宿计，ADR-0050 Q2），发送侧用本条连接的搬运字节速率——**UI 不再自行差分估算**。</item>
    /// </list>
    /// </remarks>
    private void RefreshMarkerStatus(ModuleViewViewModel module)
    {
        if (module.Ports.Count == 0)
        {
            return;
        }

        // 【P40】诊断按**网络身份键**索引，**不能按网络名**：匿名网络（自动建网）名为空串，
        // 现场出现第二条匿名网络时，`ToDictionary(d => d.NetworkName)` 会抛"同一键已存在（键为空串）"（实测崩溃）。
        var diagnostics = new Dictionary<string, BindingDiagnosticView>(StringComparer.Ordinal);
        var diagnosticsBySource = new Dictionary<string, BindingDiagnosticView>(StringComparer.Ordinal);

        foreach (var diagnostic in _query.GetDiagnostics())
        {
            _ = diagnostics.TryAdd(diagnostic.Key, diagnostic);   // 键重复也不抛：宁可少显示，不让界面崩

            // 【ADR-0050 M2/M7】逐源键 = `源 → 宿子集`：多源网络下**按源归属**取到本条连接的诊断
            var separator = diagnostic.Key.IndexOf('→', StringComparison.Ordinal);
            if (separator > 0)
            {
                _ = diagnosticsBySource.TryAdd(diagnostic.Key[..separator], diagnostic);
            }
        }

        // 【ADR-0050 Q2 / ADR-0046 D8】**每 (实例, 口) 队列**：水位与"按宿计"的搬运字节速率（框架口径）
        var queueDepths = new Dictionary<string, PortQueueDepthView>(StringComparer.Ordinal);
        foreach (var depth in _query.GetQueueDepths())
        {
            _ = queueDepths.TryAdd($"{depth.InstanceId}.{depth.PortName}", depth);
        }

        var networks = _query.GetNetworks();

        // 端口契约尺寸（字节数估算用）：一次取实例视图即可
        var contractSizes = _query.TryGetInstance(module.InstanceId, out var instance) && instance is not null
            ? instance.Ports.ToDictionary(p => p.Name, p => p.ContractSizeInBytes, StringComparer.Ordinal)
            : [];

        foreach (var marker in module.Ports)
        {
            var literal = $"{module.InstanceId}.{marker.PortName}";
            NetworkView? network = null;

            foreach (var candidate in networks)
            {
                if (string.Equals(candidate.Source, literal, StringComparison.Ordinal)
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

            // 【ADR-0050 M2/M7】**键空间要分清**（实测踩坑）：
            //   · `BindingDiagnosticView.Key` = **逐源键**（`源 → 宿子集`，多源下按源归属）⇒ 用它取"这条连接"的投递/丢弃；
            //   · `PortQueueDepthView` = **每 (实例, 口)** 一条队列 ⇒ 用它取"本口按宿计"的速率（源口/宿口都有）。
            // 早先只按网络键查逐源表 ⇒ 宿口查不到 ⇒ 该口速率/投递**静默不更新**（一直显示占位符）。
            if (!diagnostics.TryGetValue(network.Key, out var diagnostic))
            {
                _ = diagnosticsBySource.TryGetValue(literal, out diagnostic);
            }

            marker.Delivered = diagnostic?.Delivered ?? 0;
            marker.Dropped = diagnostic?.Dropped ?? 0;

            var size = contractSizes.TryGetValue(marker.PortName, out var bytes) ? bytes : 0;
            var totalBytes = marker.Delivered * size;
            marker.BytesText = size > 0 ? FormatBytes(totalBytes) : null;

            // 【ADR-0046 D8 / R4】**速率由框架给出**（不再由 UI 差分估算）：
            // 接收侧取**按宿计**的口径（每口一条队列）；取不到队列指标时回退到本条连接的搬运字节速率。
            var busRate = queueDepths.TryGetValue(literal, out var depth)
                ? depth.BusByteRate
                : diagnostic?.BusByteRate ?? 0;

            marker.RateText = busRate > 0.5 ? $"{FormatBytes((long)busRate)}/s" : "---";
        }
    }

    /// <summary>
    /// **角色投影第一遍**：摆好本模块的**角色方块**（功能分块）与信息区【ADR-0052】。
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

            // **角色自身的信息**（用户第三十四轮：画在角色方块上方、左对齐、大字速率、禁止过长描述）——
            // 数值全部来自框架的**角色维度聚合**（`GetRoles` 已按角色汇总），UI 只做格式化。
            box.RateText = role.RateBytesPerSecond > 0.5
                ? $"{FormatBytes((long)role.RateBytesPerSecond)}/s"
                : "---";
            box.MetaText = RoleTextMetrics.TypeTagOf(role.SemanticText);

            boxes.Add(box);
        }

        module.ApplyRoleBoxes(
            boxes,
            RoleOverlayInfoBuilder.Build($"{module.InstanceId} · {module.Title}", roles.Count, contractSizes));
    }

    /// <summary>
    /// **角色投影第二遍**：算**角色间数据流**（线）与**内部处理顺序**。
    /// </summary>
    /// <remarks>
    /// <list type="bullet">
    ///   <item><b>线的两点</b>：本端角色**方块中心**与**对端角色方块中心**【ADR-0053 · 用户第三十四轮：
    ///     "连线从角色方块中心出发"】—— 对端那一点用**同一套** <c>ConnectionOverlay.LayoutRoles</c>
    ///     从对端模块的方块算出并换算到本窗局部坐标 ⇒ 两端算出的**是同一条线段**
    ///     （各窗只画自己窗内那段 ⇒ 严格共线，不会出现"两根错位的线"）；</item>
    ///   <item>对端方块算不出（对端没有该角色 / 已被裁掉 / 不在现场）⇒ 回退为**对端窗口中心**；</item>
    ///   <item><b>线不承载指标</b>（用户第三十四轮）：角色维度指标画在**角色方块**上（第一遍已备好），
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
            link.PeerText = link.PeerAdjacent ? null : flow.PeerInstanceId;

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
    /// **对端角色方块中心**（本窗局部坐标，**DIP**）【ADR-0053：线以两个角色方块中心为端点】。
    /// </summary>
    /// <remarks>
    /// <b>单位口径（唯一事实源，用户第三十四轮："多套坐标系相互冲突，请确定唯一事实源"）</b>：
    /// <list type="number">
    ///   <item><b>叠加层几何 = DIP</b>：画布尺寸用 <see cref="ModuleViewViewModel.CanvasWidth"/>/<c>CanvasHeight</c>
    ///     （= 模型 px ÷ 缩放），方块布局也用它；</item>
    ///   <item><b>模型几何 = 物理像素</b>（窗口位置是 `PixelPoint`）：世界增量因此必须**除以缩放**才是 DIP；
    ///     这就是"完全水平/垂直的线也错位"的根因 —— 旧代码把 px 增量直接当 DIP 用（缩放 ≠100% 时整体放大/缩小）；</item>
    ///   <item>换算只发生在**本方法与 `CanvasWidth/CanvasHeight`** 两处，其余位置一律不许再乘除缩放。</item>
    /// </list>
    /// </remarks>
    private Avalonia.Point? ResolvePeerAnchor(ModuleViewViewModel module, RoleFlowView flow)
    {
        if (Find(flow.PeerInstanceId) is not { } peer || !module.Rect.IsValid || !peer.Rect.IsValid)
        {
            return null;
        }

        // 对端方块布局：用**对端自己的 DIP 画布**（与对端绘制时是同一对数字）
        var layout = ConnectionOverlay.LayoutRoles(peer.Roles, peer.CanvasWidth, peer.CanvasHeight);
        var box = layout.Boxes.FirstOrDefault(b => string.Equals(b.RoleKey, flow.PeerRoleKey, StringComparison.Ordinal));

        if (box.Box.Width <= 0)
        {
            return null;   // 该角色没被画出来（放不下被裁掉）⇒ 交给回退
        }

        // 世界增量：px → DIP（**必须除缩放**，否则缩放 ≠100% 时两端算出的位置不同 ⇒ 线错位）
        var dx = module.ToDip(peer.Rect.X - module.Rect.X);
        var dy = module.ToDip(peer.Rect.Y - module.Rect.Y);

        return new Avalonia.Point(box.Center.X + dx, box.Center.Y + dy);
    }

    /// <summary>回退：对端**窗口中心**（本窗局部坐标，**DIP**；px → DIP 同样必须除以缩放）。</summary>
    private Avalonia.Point ResolvePeerCenter(ModuleViewViewModel module, string peerInstanceId)
    {
        if (Find(peerInstanceId) is { } peer && peer.Rect.IsValid && module.Rect.IsValid)
        {
            return new Avalonia.Point(
                module.ToDip(peer.Rect.X + (peer.Rect.Width / 2) - module.Rect.X),
                module.ToDip(peer.Rect.Y + (peer.Rect.Height / 2) - module.Rect.Y));
        }

        return new Avalonia.Point(module.CanvasWidth / 2, module.CanvasHeight / 2);
    }

    /// <summary>对端是否与本模块**拼接贴合**（按实例 ID 判；对端不在现场 ⇒ false ⇒ 末端画断口 + 标对端）。</summary>
    private bool IsPeerAdjacent(ModuleViewViewModel module, string peerInstanceId) =>
        Find(peerInstanceId) is { } peer
        && peer.Rect.IsValid
        && module.Rect.IsValid
        && AreNodesJoined((module.InstanceId, module.Rect), (peer.InstanceId, peer.Rect), GroupTolerance);
    private static string FormatBytes(long bytes) => bytes switch
    {
        < 1024 => $"{bytes} B",
        < 1024 * 1024 => $"{bytes / 1024.0:F1} KB",
        < 1024L * 1024 * 1024 => $"{bytes / (1024.0 * 1024):F1} MB",
        _ => $"{bytes / (1024.0 * 1024 * 1024):F2} GB",
    };

    private double? ReadLatest(SubscriptionSourceView source)
    {
        var sampleSize = source.SampleSizeInBytes > 0
            ? source.SampleSizeInBytes
            : SampleCodec.SizeOf(source.ValueType);

        if (sampleSize <= 0 || sampleSize > _sampleScratch.Length)
        {
            return null;
        }

        return _query.TryReadLatest(source.InstanceId, source.DataSourceName, _sampleScratch, out var length)
            && length == sampleSize
            && DecodeSample(source, _sampleScratch.AsSpan(0, length), out var decoded)
                ? decoded
                : null;
    }

    /// <summary>
    /// 解码一个样本：**模块注册的解码器优先**（框架只回调）【ADR-0046 D1 ③】；
    /// 未提供时按**参考实现**回退（旧模块形态，登记为历史兼容路径——不猜类型，原语未声明即失败）。
    /// </summary>
    private static bool DecodeSample(SubscriptionSourceView source, ReadOnlySpan<byte> sample, out double value)
    {
        if (source.Decoder is { } decoder)
        {
            return decoder(sample, out value);
        }

        if (source.ValueType is { } primitive)
        {
            return SampleCodec.TryDecode(primitive, sample, out value);
        }

        value = 0;
        return false;
    }

    private IReadOnlyList<double> ReadSeries(SubscriptionSourceView source)
    {
        var sampleSize = source.SampleSizeInBytes > 0
            ? source.SampleSizeInBytes
            : SampleCodec.SizeOf(source.ValueType);

        if (sampleSize <= 0 || sampleSize > 8)
        {
            return [];
        }

        var maxSamples = Math.Min(CurveElementViewModel.MaxPoints, _seriesScratch.Length / sampleSize);
        var count = _query.ReadSeries(source.InstanceId, source.DataSourceName, _seriesScratch, maxSamples);

        if (count <= 0)
        {
            return [];
        }

        var points = new List<double>(count);
        for (var i = 0; i < count; i++)
        {
            if (!DecodeSample(source, _seriesScratch.AsSpan(i * sampleSize, sampleSize), out var value))
            {
                break;
            }

            points.Add(value);
        }

        // UI 侧抽样：序列比窗口长时等间隔抽到 MaxPoints（降采样不插值，§4.3）
        if (points.Count <= CurveElementViewModel.MaxPoints)
        {
            return points;
        }

        var step = (double)points.Count / CurveElementViewModel.MaxPoints;
        for (var i = 0; i < CurveElementViewModel.MaxPoints; i++)
        {
            _seriesValues[i] = points[(int)(i * step)];
        }

        return _seriesValues.ToArray();
    }

    /// <summary>
    /// 文本日志：读**新增**的文本样本并逐行追加（用已读计数避免重复追加）。
    /// </summary>
    private void AppendTextLines(string instanceId, SubscriptionSourceView source, TextLogElementViewModel log)
    {
        var sampleSize = source.SampleSizeInBytes;
        if (sampleSize <= 0 || sampleSize > _textScratch.Length)
        {
            return;
        }

        var key = (instanceId, source.DataSourceName);
        var read = _textReadCounts.TryGetValue(key, out var value) ? value : 0;

        // 只取最近窗口内的样本（窗口上限 = 缓冲容量；超出直接从头开始，不做补算）
        var maxSamples = _textScratch.Length / sampleSize;
        var count = _query.ReadSeries(instanceId, source.DataSourceName, _textScratch, maxSamples);
        if (count <= 0)
        {
            return;
        }

        if (source.Written <= read)
        {
            // 无新增（或缓冲被覆盖导致计数倒退）：本次不追加
            _textReadCounts[key] = source.Written;
            return;
        }

        var available = Math.Min(count, (int)Math.Min(int.MaxValue, source.Written - read));
        var skip = count - available;

        for (var i = skip; i < count; i++)
        {
            var span = _textScratch.AsSpan(i * sampleSize, sampleSize);
            var end = span.IndexOf((byte)0);
            var text = System.Text.Encoding.UTF8.GetString(end >= 0 ? span[..end] : span).TrimEnd();

            if (text.Length > 0)
            {
                log.Append(text);
            }
        }

        _textReadCounts[key] = source.Written;
    }

    private IReadOnlyList<(string Id, CanvasRect Rect)> ModuleRects() =>
        [.. Modules.Where(m => m.IsViewVisible).Select(m => (m.InstanceId, m.Rect))];

    /// <summary>外壳在**拼图图**里的节点 id（与实例 id 不会冲突）。</summary>
    public const string ShellNodeId = "#shell";

    /// <summary>拼图图的全部节点：外壳（若可用）+ 可见模块（ADR-0042 之后的"成组"输入）。</summary>
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
    /// 模块 ↔ 模块：沿用 <see cref="SnapEngine.AreJoined"/>（相邻边贴合 + 交叉轴有重叠）。
    /// **模块 ↔ 外壳：只有"模块左边缘贴外壳右边缘"算**（外壳只提供右边缘 —— ADR-0038 D5 / 用户裁决 D-2），
    /// 不允许贴它的左/上/下边缘。
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
            return Math.Abs(module.Left - shell.Right) <= tolerance && overlap > tolerance;
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
    /// **参照线**集合（ADR-0042 D2）：其他模块的外框（4 边 + 中心线）+ 外壳边。
    /// </summary>
    /// <remarks>
    /// 外壳只是"**只暴露一条边的对象**"，与模块进同一张线表 —— 不再是特殊通道（ADR-0042 D2）。
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
    /// 端口锚点对（ADR-0042 D2/D4）：被拖模块的发送口 ↔ 其他模块的接收口（及反向）。
    /// </summary>
    /// <remarks>
    /// **对齐轴由端口所在的边决定**：当前模型里口只在左/右边（`PortMarkerViewModel.Side`），
    /// 因此端口锚点恒为 **Y 对齐**（左右相邻时"插上"要看高度对齐）。
    /// 接近范围由引擎按两模块的交叉轴间隙判定（不再"按缝隙大小猜邻接方向" —— ADR-0042 D4）。
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
                foreach (var theirs in other.Ports)
                {
                    if (mine.IsSend == theirs.IsSend)
                    {
                        continue;   // 只考虑"发送 ↔ 接收"
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
