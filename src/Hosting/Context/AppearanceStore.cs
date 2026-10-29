using HYWDebugAssistant.Sdk.Context;

namespace HYWDebugAssistant.Hosting.Context;

/// <summary>
/// 某个元素**运行时被设定的外观**（模块给的颜色与字号）＋**动作复原信号**。
/// </summary>
/// <param name="SourceName">元素绑定的数据源名。</param>
/// <param name="Outline">边框色（<c>0xAARRGGBB</c>）；<see langword="null"/> = 没设 ⇒ 用主题的。</param>
/// <param name="Text">文字色（<c>0xAARRGGBB</c>）；<see langword="null"/> = 没设 ⇒ 用声明 / 主题的。</param>
/// <param name="OutlineRole">边框的**角色词**（相对色）；<see langword="null"/> = 没设。与 <paramref name="Outline"/> 互斥。</param>
/// <param name="TextRole">文字的**角色词**（相对色）；<see langword="null"/> = 没设。与 <paramref name="Text"/> 互斥。</param>
/// <param name="TextSize">文字大小（DIP）；<see langword="null"/> = 没设 ⇒ 用声明 / 主题那一档。</param>
/// <param name="ReadOnly">
/// 运行期只读开关；<see langword="null"/> = 没设 ⇒ 按声明那一档。
/// 与声明里的 <c>readonly</c> 分工：那个是"这一格一贯只读"（作者定），这个是"它此刻不让改"（模块定）。
/// </param>
/// <param name="Revision">
/// **动作复原信号**：每 <see cref="IViewAppearanceAccess.ResetInteraction"/> 一次就自增。
/// 控件记住上次见到的号，变了就把**用户刚做的那次交互**复原（勾选框拨回）。
/// </param>
public readonly record struct AppearanceEntry(
    string SourceName,
    uint? Outline,
    uint? Text,
    double? TextSize = null,
    long Revision = 0,
    bool? ReadOnly = null,
    string? OutlineRole = null,
    string? TextRole = null,
    bool? Enabled = null);

/// <summary>
/// **元素外观的存放处**：模块给的颜色放在这儿，UI 在刷新节拍里取走。
/// </summary>
/// <remarks>
/// <list type="bullet">
///   <item><b>谁写</b>：模块经 <see cref="IViewAppearanceAccess"/>（即 <c>IModuleContext.Appearance</c>）
///     在**自己的线程**上写；</item>
///   <item><b>它不含语义</b>：这里只存"什么颜色"，**不认识**"红 = 失败"这类约定 ——
///     颜色与它何时变都是模块的 C# 逻辑；框架删掉"成功/失败"这类参数，是为了不替模块规定"失败长什么样"；</item>
///   <item><b>谁读</b>：L4 经 <c>Core.Api</c> 的查询面转出；UI 按刷新节拍取；
///     **不参与**建网/兼容/投流/计量，**不进组合件**（纯运行期呈现状态）；</item>
///   <item><b>回退</b>：某键位被设成 <see langword="null"/> ⇒ 该键位回到声明 / 主题给的值
///     （整条都空时从表里移除，避免留下"空条目"）；</item>
///   <item><b>线程</b>：模块线程写、UI 线程读 ⇒ 一把短锁保护（不在数据面热路径上）。</item>
/// </list>
/// </remarks>
public sealed class AppearanceStore : IViewAppearanceAccess
{
    private readonly Lock _gate = new();
    private readonly Dictionary<string, AppearanceEntry> _entries = new(StringComparer.Ordinal);

    /// <inheritdoc />
    public void SetOutlineColor(string sourceName, uint? argb) =>
        Update(sourceName, entry => entry with { Outline = argb, OutlineRole = null });

    /// <inheritdoc />
    public void SetOutlineRole(string sourceName, string? role) =>
        Update(sourceName, entry => entry with { OutlineRole = role, Outline = null });

    /// <inheritdoc />
    public void SetTextColor(string sourceName, uint? argb) =>
        Update(sourceName, entry => entry with { Text = argb, TextRole = null });

    /// <inheritdoc />
    public void SetTextRole(string sourceName, string? role) =>
        Update(sourceName, entry => entry with { TextRole = role, Text = null });

    /// <inheritdoc />
    public void SetTextSize(string sourceName, double? dips) => Update(sourceName, entry => entry with { TextSize = dips });

    /// <inheritdoc />
    public void SetReadOnly(string sourceName, bool isReadOnly) =>
        Update(sourceName, entry => entry with { ReadOnly = isReadOnly ? true : null });

    /// <inheritdoc />
    public void SetEnabled(string sourceName, bool isEnabled) =>
        Update(sourceName, entry => entry with { Enabled = isEnabled });

    /// <inheritdoc />
    public void ResetInteraction(string sourceName) =>
        Update(sourceName, entry => entry with { Revision = entry.Revision + 1 });

    /// <summary>清除某个源的全部外观设定（实例重置 / 元素重建时用）。</summary>
    /// <param name="sourceName">数据源名。</param>
    public void Clear(string sourceName)
    {
        if (string.IsNullOrWhiteSpace(sourceName))
        {
            return;
        }

        lock (_gate)
        {
            _ = _entries.Remove(sourceName);
        }
    }

    /// <summary>清除全部外观设定。</summary>
    public void ClearAll()
    {
        lock (_gate)
        {
            _entries.Clear();
        }
    }

    /// <summary>取快照（只读，按源名排序以便稳定呈现）。</summary>
    /// <returns>当前全部外观设定。</returns>
    public IReadOnlyList<AppearanceEntry> Snapshot()
    {
        lock (_gate)
        {
            return [.. _entries.Values.OrderBy(e => e.SourceName, StringComparer.Ordinal)];
        }
    }

    /// <summary>改一条记录（空条目自动移除）。</summary>
    /// <param name="sourceName">数据源名（空 ⇒ 忽略，不抛）。</param>
    /// <param name="change">怎么改。</param>
    public void Update(string sourceName, Func<AppearanceEntry, AppearanceEntry> change)
    {
        ArgumentNullException.ThrowIfNull(change);

        if (string.IsNullOrWhiteSpace(sourceName))
        {
            return;   // 纪律：设定路径不抛
        }

        lock (_gate)
        {
            var current = _entries.TryGetValue(sourceName, out var existing)
                ? existing
                : new AppearanceEntry(sourceName, null, null, null);

            var updated = change(current);

            // **只剩"没设过颜色/字号/只读"且没有复原信号时**才把条目摘掉
            // （复原信号本身也要留下：它就是"这一格刚被复原过"这条事实）。
            if (updated.Outline is null && updated.Text is null && updated.TextSize is null
                && updated.ReadOnly is null && updated.Enabled is null
                && updated.OutlineRole is null && updated.TextRole is null
                && updated.Revision == 0)
            {
                _ = _entries.Remove(sourceName);
                return;
            }

            _entries[sourceName] = updated;
        }
    }
}
