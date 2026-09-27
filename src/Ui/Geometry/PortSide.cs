namespace HYWDebugAssistant.Ui.Geometry;

/// <summary>
/// 模块窗口的**四条边**：端口所在边、锚点/标记的排布边、连线的出边。
/// </summary>
/// <remarks>
/// 【为什么单独成文件】它原来寄生在 `PortAnchorCalculator` 里；那个类型只被测试使用、生产不用
/// （同一份"等分规则"在 `PortMarkerPanel` 里还有第二份实现）⇒ 已按"**来源唯一**"删除，
/// 本枚举随之独立（它是被多处真正使用的公共几何概念）。
/// </remarks>
public enum PortSide
{
    /// <summary>左边缘（接收口默认所在边）。</summary>
    Left,

    /// <summary>右边缘（发送口默认所在边）。</summary>
    Right,

    /// <summary>上边缘（当前框架未使用，为将来的上下排布留口）。</summary>
    Top,

    /// <summary>下边缘（同上）。</summary>
    Bottom,
}
