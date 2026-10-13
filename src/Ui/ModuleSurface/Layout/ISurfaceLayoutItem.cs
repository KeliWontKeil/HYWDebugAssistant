using HYWDebugAssistant.Sdk.Views;

namespace HYWDebugAssistant.Ui.ModuleSurface.Layout;

/// <summary>
/// **排布项** —— 排布层眼里的一块内容：一个声明节点 + 它的块轴事实（+ 它的子项）。
/// </summary>
/// <remarks>
/// <para>
/// 这是"面板不认识控件"的落点：面板只读 <see cref="Node"/> 与 <see cref="Metrics"/>，
/// 既不知道这块内容长什么样，也不知道它是哪一种控件。
/// </para>
/// <para>
/// <see cref="Items"/> 与 <see cref="IsExpanded"/> 是给**编排层**的（建子控件、判可达性），
/// **不是排布输入**：面板排的是当前这一层的项，子项由子面板自己排。
/// </para>
/// </remarks>
public interface ISurfaceLayoutItem
{
    /// <summary>声明节点（排布键的唯一来源）。</summary>
    ViewNode Node { get; }

    /// <summary>块轴事实（吃不吃余量 + 地板）。</summary>
    SurfaceMetrics Metrics { get; }

    /// <summary>是否展开（编排层据此跳过折叠起来的子树）。</summary>
    bool IsExpanded { get; }

    /// <summary>直接子项（叶子为空表）。</summary>
    IReadOnlyList<ISurfaceLayoutItem> Items { get; }
}
