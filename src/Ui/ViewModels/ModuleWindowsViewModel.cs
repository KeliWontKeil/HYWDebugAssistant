using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using HYWDebugAssistant.Core.Api;
using HYWDebugAssistant.Ui.Geometry;
using HYWDebugAssistant.Ui.Runtime;

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
    public const double SnapTolerance = SnapEngine.DefaultTolerance;

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

        _dragGroup = independent
            ? [instanceId]
            : SnapEngine.GroupOf(instanceId, ModuleRects(), SnapTolerance);

        _dragPointerOffsetX = pointerX - module.X;
        _dragPointerOffsetY = pointerY - module.Y;
        Select(instanceId);
        return true;
    }

    /// <summary>拖动中：吸附 → 整组平移 → 更新辅助线。</summary>
    /// <param name="pointerX">指针 X（屏幕坐标）。</param>
    /// <param name="pointerY">指针 Y（屏幕坐标）。</param>
    /// <param name="snapEnabled">本次是否吸附（null = 用开关值；按住 Alt 传 false）。</param>
    public void DragTo(double pointerX, double pointerY, bool? snapEnabled = null)
    {
        if (_draggingId is not { Length: > 0 } draggingId || Find(draggingId) is not { } module)
        {
            return;
        }

        var targetX = pointerX - _dragPointerOffsetX;
        var targetY = pointerY - _dragPointerOffsetY;

        var dx = targetX - module.X;
        var dy = targetY - module.Y;
        IReadOnlyList<GuideLine> guides = [];

        if (snapEnabled ?? SnapEnabled)
        {
            var others = CollectSnapTargets(draggingId);

            // 端口对齐优先：**无网络标签**时按口对齐（有标签时标签承担对齐职责，C5-2）
            var snapped = SnapEngine.Snap(
                module.Rect.At(targetX, targetY),
                others,
                SnapTolerance,
                ShowInfoOverlay ? null : BuildPortAlignment(draggingId),
                CollectReferenceLines());

            dx = snapped.X - module.X;
            dy = snapped.Y - module.Y;
            guides = snapped.Guides;
        }

        foreach (var memberId in _dragGroup)
        {
            if (Find(memberId) is { } member)
            {
                member.X += dx;
                member.Y += dy;
                RaiseGeometry(member);
            }
        }

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
            }
        }

        foreach (var memberId in _dragGroup)
        {
            if (Find(memberId) is { } member)
            {
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
                if (right > left && Math.Abs((right - left) - other.Width) <= SnapTolerance && MovesHorizontally(_resizeHandle))
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

                if (bottom > top && Math.Abs((bottom - top) - other.Height) <= SnapTolerance && MovesVertically(_resizeHandle))
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

        module.X = left;
        module.Y = top;
        module.Width = right - left;
        module.Height = bottom - top;
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

        marker.EditResult = message;

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

            // 新模块：错位摆放（不叠在一起），随即把默认位写回布局（否则用户不拖就不进组合件）
            var created = new ModuleViewViewModel(
                instance,
                layout: null,
                _query,
                _command,
                Scale,
                PlaceOrigin + (Modules.Count % 8 * PlaceStepX),
                PlaceOrigin + (Modules.Count % 8 * PlaceStepY));

            Modules.Add(created);
            _ = _command.UpdateModuleLayout(created.ToLayout());

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

            if (layoutById.TryGetValue(module.InstanceId, out var layout))
            {
                module.ApplyLayout(layout);
                RaiseGeometry(module);
            }
        }

        OnPropertyChanged(nameof(ModuleCount));
    }

    private void RefreshSubscriptions()
    {
        _frameCounter++;
        RefreshRateHz = _query.GetSettings().UiRefreshRateHz;

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

    private List<CanvasRect> CollectSnapTargets(string draggingId)
    {
        var rects = Modules
            .Where(m => m.IsViewVisible
                && m.Rect.IsValid
                && !_dragGroup.Contains(m.InstanceId, StringComparer.Ordinal)
                && !string.Equals(m.InstanceId, draggingId, StringComparison.Ordinal))
            .Select(m => m.Rect)
            .ToList();

        // 外壳（拼图起始点）**只提供其右边缘**一条参照【M1-16 / ADR-0038 D5 · 用户裁决 D-2】。
        // 参照线是**线不是矩形**（线的宽度为 0），故走 SnapEngine 的 referenceLines 通道；
        // **屏幕工作区退出吸附候选**（仍用于默认摆放与不出屏钳制）。
        return rects;
    }

    /// <summary>吸附**参照线**（当前只有一条：外壳的右边缘）。</summary>
    private List<(bool IsVertical, double Position)> CollectReferenceLines() =>
        ShellRect is { IsValid: true } shell ? [(true, shell.X + shell.Width)] : [];

    private IReadOnlyList<(string Id, CanvasRect Rect)> ModuleRects() =>
        [.. Modules.Where(m => m.IsViewVisible).Select(m => (m.InstanceId, m.Rect))];

    /// <summary>端口对齐候选（C5-2）：被拖模块的发送口 ↔ 其他模块的接收口（及反向）。</summary>
    private List<(double MovingY, double OtherY)> BuildPortAlignment(string draggingId)
    {
        if (Find(draggingId) is not { } moving)
        {
            return [];
        }

        var pairs = new List<(double MovingY, double OtherY)>();

        foreach (var mine in moving.Ports.Where(p => p.IsSend))
        {
            foreach (var other in Modules.Where(m => !string.Equals(m.InstanceId, draggingId, StringComparison.Ordinal) && m.IsViewVisible))
            {
                foreach (var theirs in other.Ports.Where(p => !p.IsSend))
                {
                    pairs.Add((moving.Y + (moving.Height * mine.Offset), other.Y + (other.Height * theirs.Offset)));
                }
            }
        }

        foreach (var mine in moving.Ports.Where(p => !p.IsSend))
        {
            foreach (var other in Modules.Where(m => !string.Equals(m.InstanceId, draggingId, StringComparison.Ordinal) && m.IsViewVisible))
            {
                foreach (var theirs in other.Ports.Where(p => p.IsSend))
                {
                    pairs.Add((moving.Y + (moving.Height * mine.Offset), other.Y + (other.Height * theirs.Offset)));
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
