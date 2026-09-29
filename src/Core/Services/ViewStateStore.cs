using HYWDebugAssistant.Core.Api;
using HYWDebugAssistant.Sdk.Context;

namespace HYWDebugAssistant.Core.Services;

/// <summary>
/// **视图状态存储**（`IViewStateAccess` 的唯一实现）〔2026-09 用户裁定 · `decisions.md` 7.28/7.31〕。
/// </summary>
/// <remarks>
/// <list type="bullet">
///   <item><b>为什么状态不放在元素视图模型里</b>：UI 的**元素表会在运行期重建**（切页 / 改设置 / 重新声明）
///     ⇒ 状态挂在元素实例上就会"重建即丢"（现状：切页会把**未提交的编辑**丢掉）。用户取向是"**符合 winforms
///     的开发直觉**"（`decisions.md` 7.30）—— 而 WinForms 里"控件活着，数据就在"⇒ 状态必须**独立于元素表**；</item>
///   <item><b>线程</b>：写方 = **UI 线程**（切页 / 文本变化 / 追加行）；读方 = **模块线程**（拉取快照）。
///     字典本身用锁保护；<see cref="DisplayBuffer"/> 自带锁；</item>
///   <item><b>不持久化</b>：既不入组合件，也不落盘 —— 重启即空（与"开关不落盘"同一取向）。</item>
/// </list>
/// </remarks>
public sealed class ViewStateStore : IViewStateAccess
{
    private readonly Dictionary<string, InstanceState> _instances = new(StringComparer.Ordinal);
    private readonly Lock _gate = new();

    /// <inheritdoc />
    public IViewStateReader ReaderFor(string instanceId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(instanceId);
        return new InstanceViewState(this, instanceId);
    }

    /// <inheritdoc />
    public void SetSelectedPage(string instanceId, string stateKey, int page)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(instanceId);
        ArgumentException.ThrowIfNullOrWhiteSpace(stateKey);

        lock (_gate)
        {
            Get(instanceId).SelectedPages[stateKey] = page;
        }
    }

    /// <inheritdoc />
    public int? GetSelectedPage(string instanceId, string stateKey)
    {
        if (string.IsNullOrWhiteSpace(instanceId) || string.IsNullOrWhiteSpace(stateKey))
        {
            return null;
        }

        lock (_gate)
        {
            return _instances.TryGetValue(instanceId, out var state)
                && state.SelectedPages.TryGetValue(stateKey, out var page)
                    ? page
                    : null;
        }
    }

    /// <inheritdoc />
    public void SetInputText(string instanceId, string sourceName, string? text)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(instanceId);
        ArgumentException.ThrowIfNullOrWhiteSpace(sourceName);

        lock (_gate)
        {
            var state = Get(instanceId);

            if (text is null)
            {
                _ = state.InputTexts.Remove(sourceName);
            }
            else
            {
                state.InputTexts[sourceName] = text;
            }
        }
    }

    /// <inheritdoc />
    public string? GetInputText(string instanceId, string sourceName)
    {
        if (string.IsNullOrWhiteSpace(instanceId) || string.IsNullOrWhiteSpace(sourceName))
        {
            return null;
        }

        lock (_gate)
        {
            return _instances.TryGetValue(instanceId, out var state)
                && state.InputTexts.TryGetValue(sourceName, out var text)
                    ? text
                    : null;
        }
    }

    /// <inheritdoc />
    public DisplayBuffer Display(string instanceId, string sourceName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(instanceId);
        ArgumentException.ThrowIfNullOrWhiteSpace(sourceName);

        lock (_gate)
        {
            var state = Get(instanceId);

            if (!state.Displays.TryGetValue(sourceName, out var buffer))
            {
                buffer = new DisplayBuffer();
                state.Displays[sourceName] = buffer;
            }

            return buffer;
        }
    }

    /// <inheritdoc />
    public void RemoveInstance(string instanceId)
    {
        if (string.IsNullOrWhiteSpace(instanceId))
        {
            return;
        }

        lock (_gate)
        {
            _ = _instances.Remove(instanceId);
        }
    }

    /// <inheritdoc />
    public void RequestClear(string instanceId, string sourceName)
    {
        if (string.IsNullOrWhiteSpace(instanceId) || string.IsNullOrWhiteSpace(sourceName))
        {
            return;
        }

        lock (_gate)
        {
            _ = Get(instanceId).PendingClears.Add(sourceName);
        }
    }

    /// <inheritdoc />
    public bool TryTakeClear(string instanceId, out string? sourceName)
    {
        sourceName = null;

        if (string.IsNullOrWhiteSpace(instanceId))
        {
            return false;
        }

        lock (_gate)
        {
            if (!_instances.TryGetValue(instanceId, out var state) || state.PendingClears.Count == 0)
            {
                return false;
            }

            sourceName = state.PendingClears.First();
            _ = state.PendingClears.Remove(sourceName);
            return true;
        }
    }

    /// <inheritdoc />
    public void RequestAppend(string instanceId, string sourceName, string text, bool lineBreak)
    {
        if (string.IsNullOrWhiteSpace(instanceId)
            || string.IsNullOrWhiteSpace(sourceName)
            || string.IsNullOrEmpty(text))
        {
            return;     // 未声明/空的源与空文本一律忽略（**不抛** —— 与"未知一律安全"同一纪律）
        }

        lock (_gate)
        {
            Get(instanceId).PendingAppends.Add(new DisplayAppend(sourceName, text, lineBreak));
        }
    }

    /// <inheritdoc />
    public IReadOnlyList<DisplayAppend> DrainAppends(string instanceId)
    {
        if (string.IsNullOrWhiteSpace(instanceId))
        {
            return [];
        }

        lock (_gate)
        {
            if (!_instances.TryGetValue(instanceId, out var state) || state.PendingAppends.Count == 0)
            {
                return [];
            }

            var drained = state.PendingAppends.ToArray();
            state.PendingAppends.Clear();
            return drained;
        }
    }

    /// <inheritdoc />
    public IReadOnlyList<DisplayAppend> PeekAppends(string instanceId, string sourceName)
    {
        if (string.IsNullOrWhiteSpace(instanceId) || string.IsNullOrWhiteSpace(sourceName))
        {
            return [];
        }

        lock (_gate)
        {
            if (!_instances.TryGetValue(instanceId, out var state) || state.PendingAppends.Count == 0)
            {
                return [];
            }

            return [.. state.PendingAppends.Where(a => string.Equals(a.SourceName, sourceName, StringComparison.Ordinal))];
        }
    }

    /// <summary>某实例的**显示请求面**（交给模块的那一份；模块只能用"清空"这一类请求）。</summary>
    /// <param name="instanceId">实例 ID。</param>
    /// <returns>请求面。</returns>
    public Sdk.Context.IViewDisplayAccess DisplayFor(string instanceId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(instanceId);
        return new InstanceDisplay(this, instanceId);
    }

    private InstanceState Get(string instanceId)
    {
        if (!_instances.TryGetValue(instanceId, out var state))
        {
            state = new InstanceState();
            _instances[instanceId] = state;
        }

        return state;
    }

    /// <summary>单实例的视图状态容器。</summary>
    private sealed class InstanceState
    {
        public Dictionary<string, int> SelectedPages { get; } = new(StringComparer.Ordinal);

        public Dictionary<string, string> InputTexts { get; } = new(StringComparer.Ordinal);

        public Dictionary<string, DisplayBuffer> Displays { get; } = new(StringComparer.Ordinal);

        /// <summary>待 UI 执行的"清空显示源"请求（模块 → 框架的请求，UI 在刷新节拍里取走）。</summary>
        public HashSet<string> PendingClears { get; } = new(StringComparer.Ordinal);

        /// <summary>
        /// 待 UI 落地的"追加文本"请求（**有序** —— 追加与顺序有关，不能像清空那样用集合）。
        /// </summary>
        public List<DisplayAppend> PendingAppends { get; } = [];
    }

    /// <summary>**模块侧**的显示请求适配器（按实例绑定；模块拿不到别的写能力）。</summary>
    private sealed class InstanceDisplay : Sdk.Context.IViewDisplayAccess
    {
        private readonly ViewStateStore _owner;
        private readonly string _instanceId;

        public InstanceDisplay(ViewStateStore owner, string instanceId)
        {
            _owner = owner;
            _instanceId = instanceId;
        }

        /// <inheritdoc />
        public void Clear(string dataSourceName) => _owner.RequestClear(_instanceId, dataSourceName);

        /// <inheritdoc />
        public void AppendText(string dataSourceName, string text) =>
            _owner.RequestAppend(_instanceId, dataSourceName, text, lineBreak: false);

        /// <inheritdoc />
        public void AppendLine(string dataSourceName, string text) =>
            _owner.RequestAppend(_instanceId, dataSourceName, text, lineBreak: true);
    }

    /// <summary>
    /// **模块侧只读适配器**：把存储按实例绑成一个 <see cref="IViewStateReader"/>（模块拿不到写入面）。
    /// </summary>
    private sealed class InstanceViewState : IViewStateReader
    {
        private readonly ViewStateStore _owner;
        private readonly string _instanceId;

        public InstanceViewState(ViewStateStore owner, string instanceId)
        {
            _owner = owner;
            _instanceId = instanceId;
        }

        /// <inheritdoc />
        public int? GetSelectedPage(string stateKey) => _owner.GetSelectedPage(_instanceId, stateKey);

        /// <inheritdoc />
        public string? GetInputText(string sourceName) => _owner.GetInputText(_instanceId, sourceName);

        /// <inheritdoc />
        public IReadOnlyList<string> GetDisplayLines(string sourceName)
        {
            if (string.IsNullOrWhiteSpace(sourceName))
            {
                return [];
            }

            lock (_owner._gate)
            {
                if (!_owner._instances.TryGetValue(_instanceId, out var state))
                {
                    return [];
                }

                var lines = state.Displays.TryGetValue(sourceName, out var buffer)
                    ? new List<string>(buffer.Snapshot())
                    : [];

                // **把"还没被 UI 取走的追加"叠上来**（同一份拼装规则）：
                // UI 一个节拍才落地一次，而模块可能**随时**回读（保存）⇒
                // 不叠的话"刚收到就保存"会丢掉尾巴，就不是"所见即所存"了。
                foreach (var append in state.PendingAppends)
                {
                    if (string.Equals(append.SourceName, sourceName, StringComparison.Ordinal))
                    {
                        DisplayCompose.Apply(lines, append.Text, append.LineBreak);
                    }
                }

                return lines;
            }
        }
    }
}
