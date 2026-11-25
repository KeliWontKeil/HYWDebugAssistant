namespace HYWDebugAssistant.Ui.Geometry;

/// <summary>
/// **拼接件**：几何上贴合、且成员**两两不重合**的一组模块——可以整体平移的刚体，也是"层"的最小单位。
/// </summary>
/// <remarks>
/// <para>
/// <b>为什么必须"两两不重合"</b>：真刚体不能自己压自己。一个**压住**件内某块的模块永远进不了这一件，
/// 这正是"高层模块不许拼到低层组上"的守门条件——层号只是这条几何事实的近似编号。
/// 少了这条，两个互相压住的模块会被抹成同层，视觉上就成了"第二层的模块拼到了第一层组上"。
/// </para>
/// <para>
/// <b>为什么外壳不是成员</b>：外壳是底板，贴壳的模块可以处在不同层。把外壳并进并查集会把它桥接成一张网，
/// 所有贴壳件被压成同一个层号。贴壳只用 <see cref="TouchesShell"/> 表达；
/// "拖一个贴壳件带走整个贴壳整体"由调用方的**接地整体**负责（见 <c>ModuleWindowsViewModel.DragScopeOf</c>）。
/// </para>
/// </remarks>
public sealed class TilingAssembly
{
    internal TilingAssembly(IReadOnlyList<TilingNode> members, int topZ, bool touchesShell)
    {
        Members = members;
        TopZ = topZ;
        TouchesShell = touchesShell;
    }

    /// <summary>成员（按处理顺序：z 升序、同 z 按 id 序数）。</summary>
    public IReadOnlyList<TilingNode> Members { get; }

    /// <summary>
    /// 叠放键：成员里**最大**的 z 序——刚体在叠放序列里的位置 = 它最上方成员的位置。
    /// </summary>
    /// <remarks>
    /// 取 max 而不是 min：件内"压着别人的那一半"才是它必须在别人之上的原因，取 min 会让它被排到下面，
    /// 与遮挡关系矛盾（表现为压在别人身上的模块反而被画到下面）。
    /// </remarks>
    public int TopZ { get; }

    /// <summary>是否有成员**单侧贴在**外壳右边缘上（贴壳 ≠ 压壳）。</summary>
    public bool TouchesShell { get; }

    /// <summary>是否包含某实例。</summary>
    /// <param name="instanceId">模块实例 id。</param>
    public bool Contains(string instanceId)
    {
        foreach (var member in Members)
        {
            if (string.Equals(member.Id, instanceId, StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }
}
