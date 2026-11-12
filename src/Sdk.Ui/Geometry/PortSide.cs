namespace HYWDebugAssistant.Sdk.Ui.Geometry;

/// <summary>
/// 模块窗口的**四条边**：端口所在边、锚点/标记的排布边、连线的出边。
/// </summary>
/// <remarks>
/// 放在 Sdk.Ui 是因为运行时窗口与预览外壳都要用同一套边语义；它是纯几何枚举，
/// 不依赖任何框架对象。
/// </remarks>
public enum PortSide
{
    /// <summary>左边缘（接收口默认所在边）。</summary>
    Left,

    /// <summary>右边缘（发送口默认所在边）。</summary>
    Right,

    /// <summary>上边缘。</summary>
    Top,

    /// <summary>下边缘。</summary>
    Bottom,
}
