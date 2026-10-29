using CommunityToolkit.Mvvm.ComponentModel;
using HYWDebugAssistant.Sdk.Ports;
using HYWDebugAssistant.Sdk.Text;
using HYWDebugAssistant.Ui.Geometry;

namespace HYWDebugAssistant.Ui.ViewModels;

/// <summary>
/// 口上的**标记块**（网络表现依附于口）——
/// 口名 · 网络名 · 方向 · 连接状态 · **累计投递量与估算字节数**。
/// </summary>
/// <remarks>
/// <list type="bullet">
///   <item>标记块是**唯一**的用户建网入口：填写"网络名"即设置端口标签（同名自动连成一条网络）；</item>
///   <item>只承担视觉 + 输入，改标签经 <c>IRuntimeCommand.UpdatePortNetwork</c> 提交；</item>
///   <item>发送口在右边缘、接收口在左边缘（与数据流方向一致）；标记块画在**预留带**内，不覆盖内容；</item>
///   <item>指标是**该口参与的通路**口径：速率/有效载荷累计按**源**计（多宿不放大）；
///     投递/丢弃是绑定级**按宿**计数（宿口在 1:N/N:M 下含同源其他宿的份额）。
///     界面只显示框架给的数，不自行差分或估算。</item>
/// </list>
/// </remarks>
public sealed partial class PortMarkerViewModel : ViewModelBase
{
    /// <summary>创建端口标记。</summary>
    /// <param name="instanceId">所属实例 ID。</param>
    /// <param name="portName">口名。</param>
    /// <param name="isSend">是否为发送口。</param>
    /// <param name="label">网络标签（空 = 未命名，不参与命名网络）。</param>
    /// <param name="offset">在所属边缘上的归一化位置（0–1）。</param>
    /// <param name="typeText">端口的**信息类型**描述（如 <c>f32</c>；null = 未知 → 显示占位符）。</param>
    /// <param name="typeKey">
    /// 端口的**类型身份**（`标识@v版本`；未声明类型 ⇒ 结构描述）——**色板键**：
    /// 颜色按身份映射，才能与角色方块同色、且不把"不同标识同形状"混为一色。
    /// </param>
    /// <remarks>
    /// **不接收连接状态/停流状态**（"数据指标来源唯一"）：二者由
    /// <c>ModuleWindowsViewModel.RefreshMarkerStatus</c> 按网络成员关系**唯一生产**，
    /// 构造时一律为初始值（未连、非停流）。
    /// </remarks>
    public PortMarkerViewModel(
        string instanceId,
        string portName,
        bool isSend,
        string? label,
        double offset,
        string? typeText = null,
        string? typeKey = null,
        PortEdge edge = PortEdge.Auto,
        bool isAnchorFixed = false)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(instanceId);
        ArgumentException.ThrowIfNullOrWhiteSpace(portName);

        InstanceId = instanceId;
        PortName = portName;
        IsSend = isSend;
        // **不按惯例兜底**：边只来自包内声明（或用户拖动）。
        // 传进来的 <c>Auto</c> 只可能出现在"声明缺失 ⇒ 已判硬伤、整份忽略"的实例上 ——
        // 那正是"未声明就没有位置"的呈现，**不能**在这里改写成"发送右 / 接收左"。
        Edge = edge;
        IsAnchorFixed = isAnchorFixed;
        Label = string.IsNullOrWhiteSpace(label) ? null : label;
        Offset = Math.Clamp(offset, 0, 1);
        TypeText = string.IsNullOrWhiteSpace(typeText) ? "—" : typeText;
        TypeKey = string.IsNullOrWhiteSpace(typeKey) ? null : typeKey;
        EditText = Label ?? string.Empty;
    }

    /// <summary>所属实例 ID。</summary>
    public string InstanceId { get; }

    /// <summary>口名。</summary>
    public string PortName { get; }

    /// <summary>是否发送口（决定箭头朝向与朝向判定）。</summary>
    public bool IsSend { get; }

    /// <summary>网络标签（**建网唯一机制**：同名即连）。</summary>
    [ObservableProperty]
    public partial string? Label { get; set; }

    /// <summary>在所属边缘上的归一化位置（0–1）。</summary>
    [ObservableProperty]
    public partial double Offset { get; set; }

    /// <summary>是否已加入网络。</summary>
    [ObservableProperty]
    public partial bool IsConnected { get; set; }

    /// <summary>是否停流。</summary>
    [ObservableProperty]
    public partial bool IsStalled { get; set; }

    /// <summary>所属网络名（未入网为空）。</summary>
    [ObservableProperty]
    public partial string? NetworkName { get; set; }

    /// <summary>
    /// 该口参与的流的累计投递单元数（**绑定级按宿**：一条 `源→N宿` 绑定的计数是 N 份；
    /// 宿口会含同源其他宿的份额）。
    /// </summary>
    [ObservableProperty]
    public partial long Delivered { get; set; }

    /// <summary>该口参与的流的累计传输丢弃数（**绑定级按宿**；不含停流未投递）。</summary>
    [ObservableProperty]
    public partial long Dropped { get; set; }

    /// <summary>累计有效载荷字节文本（按源计；空 = 未知）。</summary>
    [ObservableProperty]
    public partial string? BytesText { get; set; }

    /// <summary>是否处于编辑状态（双击进入；编辑用**弹出输入框**，不嵌在标记里）。</summary>
    [ObservableProperty]
    public partial bool IsEditing { get; set; }

    /// <summary>编辑中的文本。</summary>
    [ObservableProperty]
    public partial string EditText { get; set; }

    /// <summary>**即将自动建网**（拖动中按"接触且对齐"规则预览到会连上；松手后由网络行为落地）。</summary>
    [ObservableProperty]
    public partial bool IsDockCandidate { get; set; }

    /// <summary>
    /// **通路速率**（**通路维度**）：文本**常驻**显示在标记上
    /// （速率**常驻**于网络标签上，**不属于叠加层**）。
    /// **未入网 = `---`**；已连接但上一秒无数据 = `0 B/s`（零速率与“没连接”可区分）。
    /// </summary>
    /// <remarks>
    /// 与角色方块上的"角色自身速率"是**两个维度**（读同一份框架诊断，表达对象不同）——
    /// 这里只存文本，数值由 <c>ModuleWindowsViewModel.RefreshMarkerStatus</c> 填。
    /// </remarks>
    [ObservableProperty]
    public partial string RateText { get; set; } = "---";

    /// <summary>标记宽度（标准尺寸：左右边缘 20、上下边缘 150 —— 所有标记占地统一）。</summary>
    /// <remarks>
    /// 长度取 <see cref="PortMarkerMetrics.Length"/>（**唯一来源**，不在 Tokens 里写第二份）：
    /// 它必须给名称/速率/箭头/灯留够空间（信息变化不引起排版抖动），
    /// 短了名称会被速率吃掉（长度 114 时「（直连）」只剩约 10px）。
    /// </remarks>
    public double MarkerWidth => IsVertical ? PortMarkerMetrics.Width : PortMarkerMetrics.Length;

    /// <summary>标记高度（标准尺寸：左右边缘 150、上下边缘 20）。</summary>
    public double MarkerHeight => IsVertical ? PortMarkerMetrics.Length : PortMarkerMetrics.Width;

    /// <summary>
    /// **网络名列的最大宽**（"名称优先"：空间不够时先裁速率，不裁名字）。
    /// </summary>
    /// <remarks>
    /// 由**本标记自己的沿边延长**减去固定槽位算出（单一来源）⇒ 模板里不写死数字：改了标记长度，
    /// 名称可用宽自动跟着走（写死会出现"加长了标签、名称列还是老宽度"这种半修状态）。
    /// </remarks>
    public double NameMaxWidth => (IsVertical ? MarkerHeight : MarkerWidth) - PortMarkerMetrics.FixedChrome;

    /// <summary>端口**信息类型**文本（如 <c>f32</c>；未知为 <c>—</c>）。</summary>
    [ObservableProperty]
    public partial string TypeText { get; set; } = "—";

    /// <summary>
    /// 端口**类型身份**（色板键）：已声明类型 ⇒ `标识@v版本`；未声明 ⇒ 结构描述；未知 ⇒ null。
    /// </summary>
    /// <remarks>颜色**按身份**映射（不是按呈现文本）：与角色方块同色，且不会把"不同标识同形状"混为一色。</remarks>
    public string? TypeKey { get; }

    /// <summary>
    /// **工具提示**用的对端信息（`实例.口名`；空 = 无对端或对端未装入）。
    /// </summary>
    /// <remarks>
    /// 【来源唯一】叠加层的**连线**不读标记的对端方位（改由**角色对**驱动）——
    /// 这里只服务**悬停提示**这一个消费者（对端方位字段与角色对是同一事实的第二个来源，故不留）。
    /// </remarks>
    [ObservableProperty]
    public partial string? PeerText { get; set; }

    partial void OnPeerTextChanged(string? value) => OnPropertyChanged(nameof(Tooltip));

    /// <summary>编辑失败原因（**弹窗内就地显示**；空 = 无错误）。</summary>
    [ObservableProperty]
    public partial string? EditError { get; set; }

    /// <summary>是否有编辑错误（弹窗内错误行显隐）。</summary>
    public bool HasEditError => EditError is { Length: > 0 };

    /// <summary>标记主行显示文本（口名 + 网络名）。</summary>
    public string Display => string.IsNullOrWhiteSpace(Label) ? PortName : $"{PortName}·{Label}";

    /// <summary>网络名编辑弹窗的标题（`设置网络名：{端口名}`；XAML 直接绑定，避免 StringFormat 拼文案）。</summary>
    public string EditTitle => TextCatalog.Get(typeof(PortMarkerViewModel).Assembly, "moduleWindow.portLabel.editorTitle", PortName);

    /// <summary>方向文本（发送 / 接收）。</summary>
    public string DirectionText => TextCatalog.Get(typeof(PortMarkerViewModel).Assembly, IsSend ? "portMarker.direction.send" : "portMarker.direction.receive");

    /// <summary>
    /// **方向箭头字形**（箭头方向**不写死**，由**所在边**与**输入输出关系**推出）。
    /// </summary>
    /// <remarks>
    /// <list type="bullet">
    ///   <item><b>语义</b>：发送 = **朝外**（数据从本模块出去）、接收 = **朝内**（数据进本模块）
    ///     ⇒ 屏幕方向由「所在边」+「发送/接收」唯一确定，不假设"数据流恒为左→右"；</item>
    ///   <item><b>为什么两个分支可以共用一张字形表</b>：竖排分支外面套了 −90° 旋转
    ///     （内容 <c>(x,y)</c> → 屏幕 <c>(y,−x)</c>：内容 `↓` 在屏幕上才呈 `→`），
    ///     而"低侧边（上/左）朝外"与"高侧边（下/右）朝内"在这套映射下**恰好都对应内容字形 `↑`**
    ///     ⇒ 同一个公式对两种形态都成立（各写一张表迟早会漂，正是本项目反复踩过的坑）；</item>
    ///   <item><b>默认边上的表现</b>：发送器默认在**下边**、显示器默认在**上边**（业务模块声明）
    ///     ⇒ 发送显示 `↓`（朝外向下）、接收显示 `↓`（朝内向下）—— 都由"数据向下流"这一事实推出，
    ///     不是写死的 `→`；</item>
    ///   <item>箭头太小时看不清 ⇒ 字号由标记字号（11）提到 **18**
    ///     （见 `Tokens.axaml` 的 `PortMarkerArrowFontSize`）。</item>
    /// </list>
    /// </remarks>
    public string Arrow => ArrowPointsUp ? "↑" : "↓";

    /// <summary>箭头在**屏幕上朝上**（= 该口"朝外"的那一侧在屏幕上是上/左 ⇒ 字形 `↑`）。</summary>
    private bool ArrowPointsUp => IsSend
        ? Edge is PortEdge.Top or PortEdge.Left
        : Edge is PortEdge.Bottom or PortEdge.Right;

    /// <summary>连接状态文本（**只用于工具提示**；标记本身不显示连接状态 —— 指示灯已经表达）。</summary>
    public string StateText => IsStalled
        ? TextCatalog.Get(typeof(PortMarkerViewModel).Assembly, "portMarker.state.stalled")
        : IsConnected ? TextCatalog.Get(typeof(PortMarkerViewModel).Assembly, "portMarker.state.connected") : TextCatalog.Get(typeof(PortMarkerViewModel).Assembly, "portMarker.state.disconnected");

    /// <summary>
    /// 标记上显示的网络名（**标签优先**）。
    /// </summary>
    /// <remarks>
    /// ① **不追加"（未连）"** —— 没连上时**指示灯会灭**，文字再说一遍既冗余又抢宽度；
    /// ② 无标签但已连上（接触即连的匿名网络）显示 <c>（直连）</c>，既未连也无标签显示 <c>无网络</c> ——
    /// 这两个是**网络名占位**，不是连接状态提示。
    /// </remarks>
    public string NetworkText => Label is { Length: > 0 } label
        ? label
        : IsConnected ? TextCatalog.Get(typeof(PortMarkerViewModel).Assembly, "portMarker.network.direct") : TextCatalog.Get(typeof(PortMarkerViewModel).Assembly, "portMarker.network.none");

    /// <summary>状态语义（连接状态点用；颜色 + 形状双重编码）。</summary>
    public StateVisual State => IsStalled ? StateVisual.Dangling : IsConnected ? StateVisual.Active : StateVisual.Idle;

    /// <summary>
    /// **所在边缘**——来自 `PortView.Edge` 的**有效边**（用户拖动覆盖 &gt; 模块 `PortDeclaration.Edge`）。
    /// </summary>
    /// <remarks>
    /// 不是写死的 `IsSend ? Right : Left`：**模块在代码里声明边、用户可以把标记拖到任意边**。
    /// **没有惯例回退**：`Auto` 只会出现在"声明缺失 ⇒ 已判硬伤"的实例上（= 这个口没有位置），
    /// 界面按"未声明"呈现，框架不替它猜方向。
    /// **可变**：拖动过程中就地更新形成预览；松手时由 <c>ModuleWindowsViewModel.MovePortAnchor</c> 提交，
    /// 失败则回滚（提交是唯一写入方，本地值只是"待提交的意图"）。
    /// </remarks>
    [ObservableProperty]
    public partial PortEdge Edge { get; set; }

    /// <summary>
    /// 锚点是否「**固定**」——固定 ⇒ **不可拖动**。
    /// </summary>
    /// <remarks>
    /// **与「模块窗口锁定」区分**：后者是整个模块不可移动、不可缩放。
    /// 用词一律「固定」，不用「锁定」（避免与「模块窗口锁定」混为一谈）。
    /// </remarks>
    [ObservableProperty]
    public partial bool IsAnchorFixed { get; set; }

    /// <summary>所在边缘（视图用的枚举；与 <see cref="Edge"/> 同源）。</summary>
    /// <remarks>
    /// <see cref="PortEdge.Auto"/> 落到 <see cref="PortSide.Left"/> 只是**渲染兜底**：它只出现在
    /// "包内声明缺失 ⇒ 已判硬伤、整份忽略"的实例上（那个口没有位置），不参与建网判定与吸附。
    /// </remarks>
    public PortSide Side => Edge switch
    {
        PortEdge.Right => PortSide.Right,
        PortEdge.Top => PortSide.Top,
        PortEdge.Bottom => PortSide.Bottom,
        _ => PortSide.Left,
    };

    /// <summary>锚点**可拖动**（= 未固定）——只用于光标与悬停提示。</summary>
    public bool IsDraggable => !IsAnchorFixed;

    /// <summary>
    /// **指针是否悬停在本标记上**——「固定 / 解锁」按钮的显隐条件。
    /// </summary>
    /// <remarks>
    /// 按钮在**另一层**（位置由 `PortMarkerPanel` 排布）⇒ 样式选择器够不到"悬停标记 ⇒ 显示按钮"，
    /// 故由视图在 `PointerEntered/Exited` 里写回本属性。**纯呈现状态**：不进组合件、不下发模块。
    /// </remarks>
    [ObservableProperty]
    public partial bool IsMarkerHovered { get; set; }

    /// <summary>
    /// **固定 / 解锁**按钮的悬停提示——按钮本身就是该口的开关，提示必须说清"按下去会怎样"。
    /// </summary>
    public string AnchorToggleTip => IsAnchorFixed
        ? TextCatalog.Get(typeof(PortMarkerViewModel).Assembly, "portMarker.anchor.tip.fixed")
        : TextCatalog.Get(typeof(PortMarkerViewModel).Assembly, "portMarker.anchor.tip.draggable");

    /// <summary>
    /// **正在被拖动**——拖动期间本地 <see cref="Edge"/>/<see cref="Offset"/> 是"待提交的意图"，
    /// 刷新节拍的 <c>SyncPorts</c> **不得**用框架权威值覆盖它（否则拖到一半会被拽回原位）。
    /// </summary>
    /// <remarks>
    /// 与 <see cref="IsEditing"/> 保护 <see cref="Label"/> 是**同一条纪律**（瞬态本地意图 vs 框架权威值）。
    /// 由视图在手势期间置位，松手（提交后）立即复位。
    /// </remarks>
    [ObservableProperty]
    public partial bool IsDraggingAnchor { get; set; }

    /// <summary>标记是否为竖直形态（左右边缘 → 竖排；上下边缘 → 横排）。</summary>
    public bool IsVertical => Side is PortSide.Left or PortSide.Right;

    /// <summary>
    /// **窄颜色条 = 数据包类型**：
    /// 颜色按**类型身份**（<see cref="TypeKey"/>）查 <see cref="PacketTypePalette"/>——
    /// 与叠加层的**角色方块填充色同源**（同一键 ⇒ 同一色，两处永远一致）；
    /// 身份未知时回落类型文本，再未知则占位色。
    /// </summary>
    public Avalonia.Media.IBrush TypeBrush => PacketTypePalette.For(TypeKey ?? TypeText);

    /// <summary>
    /// **类型颜色改了 ⇒ 让绑定的 <see cref="TypeBrush"/> 重新求值**（色板是静态单例，不经过本视图模型）。
    /// </summary>
    /// <remarks>由"数据结构 → 颜色"映射表在改色后统一调用。</remarks>
    public void RefreshTypeBrush() => OnPropertyChanged(nameof(TypeBrush));

    /// <summary>编辑框的帮助文本（**收进问号按钮的弹窗**，不占标记宽度）。</summary>
    public string Hint => IsSend
        ? TextCatalog.Get(typeof(PortMarkerViewModel).Assembly, "portMarker.hint.send")
        : TextCatalog.Get(typeof(PortMarkerViewModel).Assembly, "portMarker.hint.receive");

    /// <summary>
    /// 完整信息（**工具提示**）。
    /// </summary>
    /// <remarks>
    /// ① **不显示"上次操作"**（那个字段也不保存）；② 标记本身不显示类型，
    /// 但**允许在悬停里显式给出数据包类型**（信息量大、只在需要时出现）。
    /// </remarks>
    public string Tooltip
    {
        get
        {
            var text = TextCatalog.Get(typeof(PortMarkerViewModel).Assembly, "portMarker.tooltip.header", PortName, DirectionText, StateText, TypeText);
            if (Label is { Length: > 0 } name)
            {
                text += TextCatalog.Get(typeof(PortMarkerViewModel).Assembly, 
                    IsConnected ? "portMarker.tooltip.networkName" : "portMarker.tooltip.networkNameNoPeer",
                    name);
            }

            if (IsConnected)
            {
                text += TextCatalog.Get(typeof(PortMarkerViewModel).Assembly, 
                    "portMarker.tooltip.network",
                    NetworkName is { Length: > 0 } n ? n : TextCatalog.Get(typeof(PortMarkerViewModel).Assembly, "portMarker.network.direct"));
                if (PeerText is { Length: > 0 } peer)
                {
                    text += TextCatalog.Get(typeof(PortMarkerViewModel).Assembly, "portMarker.tooltip.peer", peer);
                }

                text += TextCatalog.Get(typeof(PortMarkerViewModel).Assembly, "portMarker.tooltip.metrics", Delivered, Dropped)
                    + (BytesText is { Length: > 0 } bytes ? TextCatalog.Get(typeof(PortMarkerViewModel).Assembly, "portMarker.tooltip.bytes", bytes) : string.Empty);
            }

            return text + TextCatalog.Get(typeof(PortMarkerViewModel).Assembly, IsAnchorFixed
                ? "portMarker.tooltip.anchorFixed"
                : "portMarker.tooltip.hint");
        }
    }

    partial void OnLabelChanged(string? value)
    {
        OnPropertyChanged(nameof(Display));
        OnPropertyChanged(nameof(NetworkText));
        OnPropertyChanged(nameof(StateText));
        OnPropertyChanged(nameof(Tooltip));
    }

    partial void OnEditErrorChanged(string? value) => OnPropertyChanged(nameof(HasEditError));

    /// <summary>
    /// **换了边 ⇒ 形态全套跟着变**：竖排/横排、标记宽高、箭头字形、悬停提示。
    /// </summary>
    /// <remarks>
    /// 拖动跨边时（左右 ↔ 上下）标记从 20×114 变 114×20，必须通知到位，否则 <c>PortMarkerPanel</c>
    /// 会按旧尺寸排布（表现为"拖过去了但还是一条竖条"）。
    /// </remarks>
    partial void OnEdgeChanged(PortEdge value)
    {
        OnPropertyChanged(nameof(Side));
        OnPropertyChanged(nameof(IsVertical));
        OnPropertyChanged(nameof(MarkerWidth));
        OnPropertyChanged(nameof(MarkerHeight));
        OnPropertyChanged(nameof(Arrow));
        OnPropertyChanged(nameof(Tooltip));
    }

    partial void OnIsAnchorFixedChanged(bool value)
    {
        OnPropertyChanged(nameof(IsDraggable));
        OnPropertyChanged(nameof(AnchorToggleTip));
        OnPropertyChanged(nameof(Tooltip));
    }

    partial void OnTypeTextChanged(string value)
    {
        OnPropertyChanged(nameof(TypeBrush));
        OnPropertyChanged(nameof(Tooltip));
    }

    partial void OnIsConnectedChanged(bool value)
    {
        OnPropertyChanged(nameof(StateText));
        OnPropertyChanged(nameof(NetworkText));
        OnPropertyChanged(nameof(State));
        OnPropertyChanged(nameof(Tooltip));
    }

    partial void OnIsStalledChanged(bool value)
    {
        OnPropertyChanged(nameof(StateText));
        OnPropertyChanged(nameof(State));
        OnPropertyChanged(nameof(Tooltip));
    }

    partial void OnDeliveredChanged(long value)
    {
        OnPropertyChanged(nameof(NetworkText));
        OnPropertyChanged(nameof(Tooltip));
    }

    partial void OnBytesTextChanged(string? value)
    {
        OnPropertyChanged(nameof(NetworkText));
        OnPropertyChanged(nameof(Tooltip));
    }
}
