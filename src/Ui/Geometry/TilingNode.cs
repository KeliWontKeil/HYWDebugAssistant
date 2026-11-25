namespace HYWDebugAssistant.Ui.Geometry;

/// <summary>
/// 拼图图里的一个节点：模块实例 + 它的**物理像素**矩形（分组判定的唯一输入形态）。
/// </summary>
/// <param name="Id">节点 id（模块实例 id；外壳用 <see cref="TilingGraph.ShellNodeId"/>）。</param>
/// <param name="Rect">节点矩形（物理像素——跨屏缩放不同时才有共同单位）。</param>
/// <remarks>
/// **零尺寸矩形（<see cref="CanvasRect.IsValid"/> 为假）不参与建件**：它没有边，谈不上贴合；
/// 调用方负责把它从节点表里剔掉（层规划会给它 0 层）。
/// </remarks>
public readonly record struct TilingNode(string Id, CanvasRect Rect);
