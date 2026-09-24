namespace HYWDebugAssistant.Ui.Geometry;

/// <summary>状态语义（09 §7：颜色 + 形状**双重编码**，不只靠颜色）。</summary>
public enum StateVisual
{
    /// <summary>正常 / 参与数据流（semantic.ok + 实心点）。</summary>
    Active = 0,

    /// <summary>禁用 / 停流（semantic.idle + 空心点；**合法状态非错误**）。</summary>
    Idle = 1,

    /// <summary>无数据流 / 方向不匹配（semantic.warn + 三角）。</summary>
    Warn = 2,

    /// <summary>悬空（口消失）：保留声明 + Issue（semantic.warn + 断续边框）。</summary>
    Dangling = 3,

    /// <summary>异常停机（semantic.error + 叉）。</summary>
    Error = 4,

    /// <summary>占位实例（模块定义缺失 / 版本不兼容）。</summary>
    Placeholder = 5,
}

/// <summary>连线层的线元（信息类层；**默认隐藏**，热键切换，§6.3）。</summary>
/// <param name="NetworkName">网络名（匿名为空串）。</param>
/// <param name="SourceX">起点 X（源口锚点）。</param>
/// <param name="SourceY">起点 Y。</param>
/// <param name="TargetX">终点 X（宿口锚点）。</param>
/// <param name="TargetY">终点 Y。</param>
/// <param name="State">状态语义（停流/悬空等 → 视觉降级）。</param>
/// <param name="HasDataFlow">是否已投递过数据（有向流动样式的强调依据）。</param>
public readonly record struct LinkVisual(
    string NetworkName,
    double SourceX,
    double SourceY,
    double TargetX,
    double TargetY,
    StateVisual State,
    bool HasDataFlow);

/// <summary>标记层的网络标签块（贴在模块边缘的小矩形；§5.5 / 09 §6）。</summary>
/// <param name="InstanceId">所属实例。</param>
/// <param name="PortName">锚定口。</param>
/// <param name="X">块左上角 X。</param>
/// <param name="Y">块左上角 Y。</param>
/// <param name="Width">块宽（随依附边缘长度拉伸，C5-8）。</param>
/// <param name="Height">块高（09 §5：20 px）。</param>
/// <param name="Label">显示的文本（网络名弱化显示；匿名=直连）。</param>
/// <param name="IsSource">是否为网络的源端（决定方向箭头朝向）。</param>
/// <param name="State">状态语义（连接状态点）。</param>
public readonly record struct MarkerVisual(
    string InstanceId,
    string PortName,
    double X,
    double Y,
    double Width,
    double Height,
    string Label,
    bool IsSource,
    StateVisual State);
