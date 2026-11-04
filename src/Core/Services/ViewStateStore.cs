using HYWDebugAssistant.Core.Api;
using HYWDebugAssistant.Sdk.Context;

namespace HYWDebugAssistant.Core.Services;

/// <summary>
/// **视图状态存储**（`IViewStateAccess` 的唯一实现）。
/// </summary>
/// <remarks>
/// <list type="bullet">
///   <item><b>为什么状态不放在元素视图模型里</b>：UI 的**元素表会在运行期重建**（切页 / 改设置 / 重新声明）
///     ⇒ 状态挂在元素实例上就会「重建即丢」（切页会把**未提交的编辑**丢掉）。取向是**符合 WinForms
///     的开发直觉** —— 而 WinForms 里「控件活着，数据就在」⇒ 状态必须**独立于元素表**；</item>
///   <item><b>线程</b>：写方 = **UI 线程**（切页 / 输入文本）与 **实例线程**（显示缓冲，经
///     <c>IViewDisplayAccess</c> 直接写）；读方 = 模块（输入/保存快照）与 UI（显示窗口）。
///     字典用锁保护；<see cref="DisplayBuffer"/> 自带锁；</item>
///   <item><b>不持久化</b>：既不入组合件，也不落盘 —— 重启即空（与"开关不落盘"同一取向）。</item>
/// </list>
/// </remarks>
public sealed class ViewStateStore : IViewStateAccess
{
    private readonly Dictionary<string, InstanceState> _instances = new(StringComparer.Ordinal);
    private readonly DisplayRegistry _displays = new();
    private readonly Lock _gate = new();

    /// <inheritdoc />
    public IViewStateReader ReaderFor(string instanceId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(instanceId);
        return new InstanceViewState(this, instanceId);
    }

    /// <summary>输入文本/选中页变化时的通知（由运行期接到实例承载者；UI 线程调用）。</summary>
    public Action<string>? InputChanged { get; set; }

    /// <inheritdoc />
    public void SetSelectedPage(string instanceId, string stateKey, int page)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(instanceId);
        ArgumentException.ThrowIfNullOrWhiteSpace(stateKey);

        lock (_gate)
        {
            Get(instanceId).SelectedPages[stateKey] = page;
        }

        InputChanged?.Invoke(instanceId);
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
    public void SetCollapsed(string instanceId, string stateKey, bool collapsed)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(instanceId);
        ArgumentException.ThrowIfNullOrWhiteSpace(stateKey);

        lock (_gate)
        {
            Get(instanceId).Collapsed[stateKey] = collapsed;
        }
    }

    /// <inheritdoc />
    public bool? GetCollapsed(string instanceId, string stateKey)
    {
        if (string.IsNullOrWhiteSpace(instanceId) || string.IsNullOrWhiteSpace(stateKey))
        {
            return null;
        }

        lock (_gate)
        {
            return _instances.TryGetValue(instanceId, out var state)
                && state.Collapsed.TryGetValue(stateKey, out var collapsed)
                    ? collapsed
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

        InputChanged?.Invoke(instanceId);
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
    public IDisplayBufferReader Display(string instanceId, string sourceName) =>
        _displays.Display(instanceId, sourceName);

    /// <summary>写面内部用：取可变缓冲（只给 <see cref="DisplayFor"/> 的实现，不外泄）。</summary>
    internal DisplayBuffer DisplayBufferFor(string instanceId, string sourceName) =>
        _displays.Display(instanceId, sourceName);

    /// <summary>
    /// 捕获一个实例的可持久化视图状态：只收声明为持久化的输入文本；
    /// 当前页与折叠态全量保留（它们是界面状态，不属于任何输入的动作语义）。
    /// </summary>
    /// <param name="instanceId">实例 ID。</param>
    /// <param name="persistentInputs">声明为持久化的输入源名。</param>
    public ViewStateSnapshot Capture(string instanceId, IEnumerable<string> persistentInputs)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(instanceId);
        ArgumentNullException.ThrowIfNull(persistentInputs);

        var names = new HashSet<string>(persistentInputs, StringComparer.Ordinal);

        lock (_gate)
        {
            if (!_instances.TryGetValue(instanceId, out var state))
            {
                return ViewStateSnapshot.Empty;
            }

            var inputs = state.InputTexts
                .Where(pair => names.Contains(pair.Key))
                .ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal);

            return new ViewStateSnapshot(
                inputs,
                new Dictionary<string, int>(state.SelectedPages, StringComparer.Ordinal),
                new Dictionary<string, bool>(state.Collapsed, StringComparer.Ordinal));
        }
    }

    /// <summary>恢复一个实例的视图状态（加载组合件/工作区时、激活模块之前调用）。</summary>
    /// <param name="instanceId">实例 ID。</param>
    /// <param name="snapshot">快照；null/空 = 不写。</param>
    public void Restore(string instanceId, ViewStateSnapshot? snapshot)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(instanceId);

        if (snapshot is null || snapshot.IsEmpty)
        {
            return;
        }

        lock (_gate)
        {
            var state = Get(instanceId);
            state.InputTexts.Clear();
            state.SelectedPages.Clear();
            state.Collapsed.Clear();

            foreach (var (source, text) in snapshot.InputTexts)
            {
                state.InputTexts[source] = text;
            }

            foreach (var (key, page) in snapshot.SelectedPages)
            {
                state.SelectedPages[key] = page;
            }

            foreach (var (key, collapsed) in snapshot.Collapsed)
            {
                state.Collapsed[key] = collapsed;
            }
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

        _displays.RemoveInstance(instanceId);
    }

    /// <summary>某实例的**显示写面**（交给模块的那一份；写者是模块实例线程）。</summary>
    /// <param name="instanceId">实例 ID。</param>
    /// <returns>写面。</returns>
    public Sdk.Context.IViewDisplayAccess DisplayFor(string instanceId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(instanceId);
        return new InstanceDisplay(this, instanceId);
    }

    private bool IsDisplayFull(string instanceId, string sourceName) =>
        _displays.TryGet(instanceId, sourceName, out var buffer) && buffer is not null && buffer.HasTrimmed;

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

        /// <summary>折叠容器的折叠态（键 = 容器声明的 <c>StateKey</c>；未记录 ⇒ 展开）。</summary>
        public Dictionary<string, bool> Collapsed { get; } = new(StringComparer.Ordinal);

        public Dictionary<string, string> InputTexts { get; } = new(StringComparer.Ordinal);
    }

    /// <summary>**模块侧**的显示写面（按实例绑定；写者是模块实例线程）。</summary>
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
        public void Clear(string dataSourceName)
        {
            if (!string.IsNullOrWhiteSpace(dataSourceName))
            {
                _owner.DisplayBufferFor(_instanceId, dataSourceName).Clear();
            }
        }

        /// <inheritdoc />
        public void SetText(string dataSourceName, string text)
        {
            if (!string.IsNullOrWhiteSpace(dataSourceName))
            {
                _owner.DisplayBufferFor(_instanceId, dataSourceName).SetText(text);
            }
        }

        /// <inheritdoc />
        public void AppendText(string dataSourceName, string text)
        {
            if (!string.IsNullOrWhiteSpace(dataSourceName))
            {
                _owner.DisplayBufferFor(_instanceId, dataSourceName).AppendText(text);
            }
        }

        /// <inheritdoc />
        public void AppendLine(string dataSourceName, string text)
        {
            if (!string.IsNullOrWhiteSpace(dataSourceName))
            {
                _owner.DisplayBufferFor(_instanceId, dataSourceName).AppendLine(text);
            }
        }

        /// <inheritdoc />
        public bool IsFull(string dataSourceName)
        {
            if (string.IsNullOrWhiteSpace(dataSourceName))
            {
                return false;
            }

            return _owner.IsDisplayFull(_instanceId, dataSourceName);
        }
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
        public void SetInputValue(string sourceName, string? text)
        {
            if (string.IsNullOrWhiteSpace(sourceName))
            {
                return;   // 纪律：设定路径不抛
            }

            _owner.SetInputText(_instanceId, sourceName, text);
        }

        /// <inheritdoc />
        public IReadOnlyList<string> GetDisplayLines(string sourceName)
        {
            if (string.IsNullOrWhiteSpace(sourceName))
            {
                return [];
            }

            return _owner._displays.TryGet(_instanceId, sourceName, out var buffer) && buffer is not null
                ? new List<string>(buffer.Snapshot())
                : [];
        }
    }
}
