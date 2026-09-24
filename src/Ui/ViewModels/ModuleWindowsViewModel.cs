using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using HYWDebugAssistant.Core.Api;
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

    /// <summary>新模块默认摆放的起始偏移与步长（物理像素）。</summary>
    private const double PlaceOrigin = 48;
    private const double PlaceStepX = 40;
    private const double PlaceStepY = 40;

    private readonly IRuntimeQuery _query;
    private readonly IRuntimeCommand _command;
    private readonly Dictionary<(string InstanceId, string Source), SubscriptionSourceView> _sources = [];
    private readonly Dictionary<(string InstanceId, string Source), long> _textReadCounts = [];

    private int _frameCounter;

    /// <summary>速率的上一采样（字节数 + 时刻）：按 (实例, 口) 记录，用于算"每秒多少"。</summary>
    private readonly Dictionary<(string InstanceId, string PortName), (long Bytes, DateTime Time)> _rateSamples = [];

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
    /// 是否显示**叠加信息层**【M1-16 / ADR-0039 D5】（原 `ShowPortMarkers`：**端口标记改为常显**，开关改为控制叠加层）。
    /// </summary>
    [ObservableProperty]
    public partial bool ShowInfoOverlay { get; set; } = true;

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
    /// 计算**叠加信息层**所需的"对端方位与名称"【M1-16 / ADR-0039 D5】。
    /// </summary>
    /// <remarks>
    /// 数据来源 = 公开查询面 <c>GetNetworks()</c>（网络 → 成员字面量），**不外扩公开面**；
    /// 对端在现场 → 按其窗口矩形的相对方位决定线的走向（拼接后两条线在公共边自然相连）；
    /// 对端不在现场（未装入/隐藏） → 按端口自身所在边（发送朝外、接收朝内）画到本窗口边缘，
    /// 并在端部标出对端名（D-4 裁决：不做跨窗口覆盖窗）。
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
                var literal = $"{marker.InstanceId}.{marker.PortName}";
                if (!peers.TryGetValue(literal, out var peer))
                {
                    marker.PeerText = null;
                    marker.PeerSide = null;
                    continue;
                }

                marker.PeerText = peer;
                marker.PeerSide = ResolvePeerSide(module, marker, peer);
            }
        }
    }

    /// <summary>对端相对本模块的方位（对端在现场按矩形中心判方位；不在现场按端口所在边）。</summary>
    private PortSide ResolvePeerSide(ModuleViewViewModel module, PortMarkerViewModel marker, string peerLiteral)
    {
        var separator = peerLiteral.LastIndexOf('.');
        var peerId = separator > 0 ? peerLiteral[..separator] : peerLiteral;

        if (Find(peerId) is { } peer && peer.Rect.IsValid && module.Rect.IsValid)
        {
            var dx = peer.Rect.X + (peer.Rect.Width / 2) - (module.Rect.X + (module.Rect.Width / 2));
            var dy = peer.Rect.Y + (peer.Rect.Height / 2) - (module.Rect.Y + (module.Rect.Height / 2));

            return Math.Abs(dx) >= Math.Abs(dy)
                ? dx >= 0 ? PortSide.Right : PortSide.Left
                : dy >= 0 ? PortSide.Bottom : PortSide.Top;
        }

        return marker.Side;
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

        var group = GroupOfNode(ShellNodeId, preMoveShell);

        foreach (var module in Modules)
        {
            if (!group.Contains(module.InstanceId) || module.IsLocked || !module.Rect.IsValid)
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
            var created = new ModuleViewViewModel(
                instance,
                layout: null,
                _query,
                _command,
                Scale,
                PlaceOrigin + (Modules.Count % 8 * PlaceStepX),
                PlaceOrigin + (Modules.Count % 8 * PlaceStepY));

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

        foreach (var module in Modules)
        {
            // 配置 Schema 读取要反射配置类型 → 不按帧重读：约每 2 秒一次，捕获外部（组合件加载/JSON 写回）变更
            if (_frameCounter % 120 == 0)
            {
                module.ReloadConfigurationForms();
            }

            // 口标记的网络信息（连接状态 + **投递量与估算字节**）：网络粒度诊断，约每 0.5 秒刷一次
            if (_frameCounter % 30 == 0)
            {
                RefreshMarkerStatus(module);
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
    }

    /// <summary>
    /// 刷新某模块**口标记的网络信息**：连接状态、网络名、累计投递/丢弃、估算字节数。
    /// </summary>
    /// <remarks>
    /// 诊断是**按网络**统计的（框架粒度）：同一网络的所有成员显示同一组数字；
    /// 字节数 = 投递单元数 × 发送方契约尺寸（估算，界面以工具提示注明）。
    /// </remarks>
    private void RefreshMarkerStatus(ModuleViewViewModel module)
    {
        if (module.Ports.Count == 0)
        {
            return;
        }

        var diagnostics = _query.GetDiagnostics().ToDictionary(d => d.NetworkName, StringComparer.Ordinal);
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
                continue;
            }

            marker.IsConnected = true;
            marker.NetworkName = network.IsAnonymous ? null : network.Name;
            marker.IsStalled = network.IsStalled;

            if (!diagnostics.TryGetValue(network.Name, out var diagnostic))
            {
                continue;
            }

            marker.Delivered = diagnostic.Delivered;
            marker.Dropped = diagnostic.Dropped;

            var size = contractSizes.TryGetValue(marker.PortName, out var bytes) ? bytes : 0;
            var totalBytes = diagnostic.Delivered * size;
            marker.BytesText = size > 0 ? FormatBytes(totalBytes) : null;

            // 速率：两次采样之间的增量 / 时间（无流量显示 ---）
            var key = (module.InstanceId, marker.PortName);
            var now = DateTime.UtcNow;

            if (size > 0 && _rateSamples.TryGetValue(key, out var previous))
            {
                var elapsed = (now - previous.Time).TotalSeconds;
                var delta = totalBytes - previous.Bytes;

                marker.RateText = elapsed > 0.05 && delta > 0
                    ? $"{FormatBytes((long)(delta / elapsed))}/s"
                    : "---";
            }

            _rateSamples[key] = (totalBytes, now);
        }
    }

    /// <summary>把字节数格式化为紧凑文本（B/KB/MB/GB）。</summary>
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
            && SampleCodec.TryDecode(source.ValueType, _sampleScratch.AsSpan(0, length), out var decoded)
                ? decoded
                : null;
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
            if (!SampleCodec.TryDecode(source.ValueType, _seriesScratch.AsSpan(i * sampleSize, sampleSize), out var value))
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
