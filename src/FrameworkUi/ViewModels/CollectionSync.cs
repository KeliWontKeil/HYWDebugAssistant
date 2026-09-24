using System.Collections.ObjectModel;

namespace HYWDebugAssistant.FrameworkUi.ViewModels;

/// <summary>
/// **集合增量同步**：把"每帧重建列表"改为"只在真的变了才动列表"。
/// </summary>
/// <remarks>
/// <list type="bullet">
///   <item><b>为什么需要</b>：UI 按刷新率（默认 60 Hz）驱动面板刷新；若每次 <c>Clear()</c> + 重新 <c>Add</c>，
///     列表项容器会被整批重建 → **鼠标悬停等瞬时视觉状态每帧被重置**（表现为"所有项乱闪"），
///     同时产生大量无谓分配；</item>
///   <item><b>做法</b>：按**结构相等**比较目标集合与期望集合；完全一致则**一个元素都不动**；
///     否则只从首个差异处开始替换/补删尾部（未变项保持同一个实例 → 容器与悬停/选中状态保留）；</item>
///   <item>行模型必须是**值语义**（<c>record</c> 或实现了值相等）才能靠相等比较判断"有没有变"。</item>
/// </list>
/// </remarks>
internal static class CollectionSync
{
    /// <summary>
    /// 使 <paramref name="target"/> 与 <paramref name="desired"/> 一致（内容相同则不做任何改动）。
    /// </summary>
    /// <typeparam name="T">元素类型（应具备值语义）。</typeparam>
    /// <param name="target">目标集合（界面绑定）。</param>
    /// <param name="desired">期望内容（本次刷新算出的结果）。</param>
    /// <returns>是否发生了改动。</returns>
    internal static bool Sync<T>(ObservableCollection<T> target, IReadOnlyList<T> desired)
    {
        ArgumentNullException.ThrowIfNull(target);
        ArgumentNullException.ThrowIfNull(desired);

        var shared = Math.Min(target.Count, desired.Count);
        var firstDifference = shared;

        for (var i = 0; i < shared; i++)
        {
            if (!EqualityComparer<T>.Default.Equals(target[i], desired[i]))
            {
                firstDifference = i;
                break;
            }
        }

        if (firstDifference == shared && target.Count == desired.Count)
        {
            return false;   // 完全一致：不动列表（**这是消除闪烁的关键**）
        }

        for (var i = firstDifference; i < shared; i++)
        {
            target[i] = desired[i];
        }

        for (var i = shared; i < desired.Count; i++)
        {
            target.Add(desired[i]);
        }

        while (target.Count > desired.Count)
        {
            target.RemoveAt(target.Count - 1);
        }

        return true;
    }
}
