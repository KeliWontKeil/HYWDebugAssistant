using CommunityToolkit.Mvvm.ComponentModel;
using HYWDebugAssistant.Ui.Geometry;

namespace HYWDebugAssistant.Ui.ViewModels;

/// <summary>
/// 口上的**标记块**（ADR-0034 §2.3：网络表现依附于口）——
/// 口名 · 网络名 · 方向 · 连接状态 · **累计投递量与估算字节数**。
/// </summary>
/// <remarks>
/// <list type="bullet">
///   <item>标记块是**唯一**的用户建网入口：填写"网络名"即设置端口标签（同名自动连成一条网络）；</item>
///   <item>只承担视觉 + 输入，改标签经 <c>IRuntimeCommand.UpdatePortNetwork</c> 提交（C4-4）；</item>
///   <item>发送口在右边缘、接收口在左边缘（与数据流方向一致，05 §C5-2）；标记块画在**预留带**内，不覆盖内容；</item>
///   <item>投递量/字节数是**网络粒度**（框架诊断按网络统计）——同一网络的所有成员显示同一组数字，
///     界面以工具提示说明，不假装是"按宿口"的精确值（字节 = 投递数 × 发送方契约尺寸的估算）。</item>
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
    /// 端口的**类型身份**（`标识@v版本`；未声明类型 ⇒ 结构描述）——**色板键**【ADR-0049 D6】：
    /// 颜色按身份映射，才能与角色方块同色、且不把"不同标识同形状"混为一色。
    /// </param>
    /// <remarks>
    /// **不接收连接状态/停流状态**（用户裁定"数据指标来源唯一"）：二者由
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
        string? typeKey = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(instanceId);
        ArgumentException.ThrowIfNullOrWhiteSpace(portName);

        InstanceId = instanceId;
        PortName = portName;
        IsSend = isSend;
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

    /// <summary>是否发送口（决定所在边缘与箭头方向）。</summary>
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

    /// <summary>该网络累计投递的数据单元数（**网络粒度**）。</summary>
    [ObservableProperty]
    public partial long Delivered { get; set; }

    /// <summary>该网络累计丢弃的数据单元数。</summary>
    [ObservableProperty]
    public partial long Dropped { get; set; }

    /// <summary>估算字节数文本（投递数 × 契约尺寸；空 = 未知）。</summary>
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
    /// **通路速率**（**通路维度**）【ADR-0053 · 用户第三十四轮】：文本**常驻**显示在标记上
    /// （用户："速率显示**常驻**于网络标签上，**它不属于叠加层**"）；未入网/无流量 = 占位符。
    /// </summary>
    /// <remarks>
    /// 与角色方块上的"角色自身速率"是**两个维度**（读同一份框架诊断，表达对象不同）——
    /// 详情见 ADR-0053 §2 D5。这里只存文本，数值由 <c>ModuleWindowsViewModel.RefreshMarkerStatus</c> 填。
    /// </remarks>
    [ObservableProperty]
    public partial string RateText { get; set; } = "---";

    /// <summary>标记宽度（标准尺寸：左右边缘 20、上下边缘 114 —— 所有标记占地统一）。</summary>
    /// <remarks>
    /// 【M1-16 / ADR-0039 D3】**长度加长 50%**（原 76 → 114），为"名称 / 类型 / 箭头 / 速率"四段预留空间，
    /// 使信息变化**不引起排版抖动**。取值与 `Tokens.axaml` 的 `PortMarkerWidth`/`PortMarkerLength` 一致。
    /// </remarks>
    public double MarkerWidth => IsVertical ? 20 : 114;

    /// <summary>标记高度（标准尺寸：左右边缘 114、上下边缘 20）。</summary>
    public double MarkerHeight => IsVertical ? 114 : 20;

    /// <summary>端口**信息类型**文本（如 <c>f32</c>；未知为 <c>—</c>）。</summary>
    [ObservableProperty]
    public partial string TypeText { get; set; } = "—";

    /// <summary>
    /// 端口**类型身份**（色板键）【ADR-0049 D6】：已声明类型 ⇒ `标识@v版本`；未声明 ⇒ 结构描述；未知 ⇒ null。
    /// </summary>
    /// <remarks>颜色**按身份**映射（不是按呈现文本）：与角色方块同色，且不会把"不同标识同形状"混为一色（ADR-0051）。</remarks>
    public string? TypeKey { get; }

    /// <summary>
    /// **工具提示**用的对端信息（`实例.口名`；空 = 无对端或对端未装入）【M1-16 / ADR-0039 D5】。
    /// </summary>
    /// <remarks>
    /// 【来源唯一】叠加层的**连线**不再读标记的对端方位（改由**角色对**驱动，ADR-0049 D4）——
    /// 这里只服务**悬停提示**这一个消费者。曾经的 `PeerSide` / `PeerAdjacent` / `HasLink` 三个字段
    /// 已随叠加层改造**删除**（它们与角色对是同一事实的第二个来源）。
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

    /// <summary>方向文本（发送 / 接收）。</summary>
    public string DirectionText => IsSend ? "发送" : "接收";

    /// <summary>
    /// 方向箭头（**按边取向外 / 向内** · ADR-0039 D3）：数据流向恒为"左 → 右"，
    /// 因此**发送口（右边缘）= 向外**、**接收口（左边缘）= 向内**，屏幕上都表现为**指向右侧**。
    /// </summary>
    /// <remarks>
    /// 左右边缘的标记内容整体旋转 −90°：旋转把 <c>(x,y)</c> 映射为 <c>(y,−x)</c>，
    /// 故竖排分支必须写 <c>↓</c> 才能在屏幕上呈现为 <c>→</c>；
    /// 上下边缘（横排、无旋转）直接用 <c>→</c>（当前框架只把口放在左右，横排为将来留口）。
    /// 【用户第十三轮反馈】箭头"过小看不清" → 字号由标记字号（11）提到 **16**（见 `Tokens.axaml` 的 `PortMarkerArrowFontSize`）。
    /// </remarks>
    public string Arrow => IsVertical ? "↓" : "→";

    /// <summary>连接状态文本（**只用于工具提示**；标记本身不显示连接状态 —— 指示灯已经表达，用户第十三轮反馈）。</summary>
    public string StateText => IsStalled
        ? "停流"
        : IsConnected ? "已连接" : "未连接";

    /// <summary>
    /// 标记上显示的网络名（**标签优先**）。
    /// </summary>
    /// <remarks>
    /// 【用户第十三轮反馈】① **不再追加"（未连）"** —— 没连上时**指示灯会灭**，文字再说一遍既冗余又抢宽度；
    /// ② 无标签但已连上（接触即连的匿名网络）显示 <c>（直连）</c>，既未连也无标签显示 <c>无网络</c> ——
    /// 这两个是**网络名占位**，不是连接状态提示。
    /// </remarks>
    public string NetworkText => Label is { Length: > 0 } label
        ? label
        : IsConnected ? "（直连）" : "无网络";

    /// <summary>状态语义（连接状态点用；颜色 + 形状双重编码，09 §7）。</summary>
    public StateVisual State => IsStalled ? StateVisual.Dangling : IsConnected ? StateVisual.Active : StateVisual.Idle;

    /// <summary>所在边缘。</summary>
    public PortSide Side => IsSend ? PortSide.Right : PortSide.Left;

    /// <summary>标记是否为竖直形态（左右边缘 → 竖排；上下边缘 → 横排）。</summary>
    public bool IsVertical => Side is PortSide.Left or PortSide.Right;

    /// <summary>
    /// **窄颜色条 = 数据包类型**（用户第十三轮反馈）：
    /// 颜色按**类型身份**（<see cref="TypeKey"/>）查 <see cref="PacketTypePalette"/>【ADR-0049 D6】——
    /// 与叠加层的**角色方块填充色同源**（同一键 ⇒ 同一色，两处永远一致）；
    /// 身份未知时回落类型文本（旧路径），再未知则占位色。
    /// </summary>
    public Avalonia.Media.IBrush TypeBrush => PacketTypePalette.For(TypeKey ?? TypeText);

    /// <summary>
    /// **类型颜色改了 ⇒ 让绑定的 <see cref="TypeBrush"/> 重新求值**（色板是静态单例，不经过本视图模型）。
    /// </summary>
    /// <remarks>由"数据结构 → 颜色"映射表在改色后统一调用（用户第三十四轮新增该表）。</remarks>
    public void RefreshTypeBrush() => OnPropertyChanged(nameof(TypeBrush));

    /// <summary>编辑框的帮助文本（**收进问号按钮的弹窗**，用户第十三轮反馈："提示信息收入帮助按钮"）。</summary>
    public string Hint => IsSend
        ? "发送口：填网络名后，同名接收口自动连上（一个网络只能有一个发送端）"
        : "接收口：填网络名后，同名发送口自动连上；没有同名发送端时只保留标签并提示";

    /// <summary>
    /// 完整信息（**工具提示**）。
    /// </summary>
    /// <remarks>
    /// 【用户第十三轮反馈】① **不再显示"上次操作"**（那个字段也不再保存）；② 标记本身不显示类型，
    /// 但**允许在悬停里显式给出数据包类型**（信息量大、只在需要时出现）。
    /// </remarks>
    public string Tooltip
    {
        get
        {
            var text = $"{PortName}（{DirectionText}）\n{StateText}\n类型：{TypeText}";
            if (Label is { Length: > 0 } name)
            {
                text += $"\n网络名：{name}" + (IsConnected ? string.Empty : "（暂无同名对端口）");
            }

            if (IsConnected)
            {
                text += $"\n网络：{(NetworkName is { Length: > 0 } n ? n : "（直连）")}";
                if (PeerText is { Length: > 0 } peer)
                {
                    text += $"\n对端：{peer}";
                }

                text += $"\n投递 {Delivered} 单元 · 丢弃 {Dropped} 单元"
                    + (BytesText is { Length: > 0 } bytes ? $"\n流量约 {bytes}（网络粒度估算）" : string.Empty);
            }

            return text + "\n双击填写/清除网络名";
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
