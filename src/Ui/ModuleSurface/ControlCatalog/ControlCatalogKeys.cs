namespace HYWDebugAssistant.Ui.ModuleSurface.ControlCatalog;

/// <summary>
/// 目录里**被多条描述共用**的键集合 —— 只为免去逐条重复，不构成第二份定义。
/// </summary>
/// <remarks>
/// <para>
/// 全部是**键名**，不含任何数值：**几何没有缺省口径**（每个会渲染的元素两轴必写，
/// 漏写由读取器报硬伤），外观数值的唯一事实源是主题资源。这里出现数字即违反 §3.3。
/// </para>
/// <para>
/// 这是编译期常量（`static readonly`），不是内联数组表达式 —— 后者会被 CA1861 判为
/// "每处调用都新建一份"，在本仓库（警告即错误）不通过。
/// </para>
/// </remarks>
internal static class ControlCatalogKeys
{
    /// <summary>所有节点共用的轴键（宽 / 高 / 是否随窗口缩放）。</summary>
    internal static readonly string[] Axes = ["width", "height", "scaleWithWindow"];

    /// <summary>仅 <c>grid</c> 容器内有效的格子键。</summary>
    internal static readonly string[] GridPlacement = ["column", "row", "columnSpan", "rowSpan"];

    /// <summary>格子内的对齐键。</summary>
    internal static readonly string[] RegionAlignment = ["align", "verticalAlign"];

    /// <summary>节点可用的**全部**几何排布键（轴 + 格子 + 对齐）。</summary>
    internal static readonly string[] AllGeometry = [.. Axes, .. GridPlacement, .. RegionAlignment];

    /// <summary>容器自带的排布键（仅 <c>container</c> 拥有）。</summary>
    internal static readonly string[] ContainerLayout =
        ["layout", "overflow", "padding", "spacing", "columns", "rows", "columnSpacing", "rowSpacing", "children"];

    /// <summary>语义包装容器也接受的间距键（<c>group</c>/<c>collapsible</c>）。</summary>
    internal static readonly string[] WrapperSpacing = ["padding", "spacing"];
}
