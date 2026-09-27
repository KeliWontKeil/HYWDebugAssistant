using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.VisualTree;

namespace HYWDebugAssistant.Ui.Hotkeys;

/// <summary>
/// 快捷键要触发的动作面（由**外壳视图模型**实现；`Ui` 只声明契约，保持依赖方向 `FrameworkUi → Ui`）。
/// </summary>
public interface IHotkeyActions
{
    /// <summary>执行一个动作（动作标识来自 <see cref="HotkeyCatalog"/>）。</summary>
    /// <param name="actionId">动作标识。</param>
    void Execute(string actionId);
}

/// <summary>
/// **应用内快捷键服务**（ADR-0036 D7）：把绑定挂到**本应用每一个窗口**上，
/// 因此外壳窗口或任一模块窗口聚焦时快捷键都生效（此前只在 Shell 聚焦时生效）。
/// </summary>
/// <remarks>
/// <list type="bullet">
///   <item>只处理**未被控件消费**的按键（隧道阶段不拦截，交给内容控件优先）；</item>
///   <item>绑定文本 → <see cref="HotkeyBinding"/> 解析失败即忽略（不抛）；</item>
///   <item>作用域为应用内：不注册系统级全局热键（v1 明确不做）。</item>
/// </list>
/// </remarks>
public sealed class HotkeyService
{
    private readonly IHotkeyActions _actions;
    private readonly Dictionary<HotkeyBinding, string> _bindings = [];
    private readonly List<Window> _attached = [];

    /// <summary>创建快捷键服务。</summary>
    /// <param name="actions">动作面。</param>
    public HotkeyService(IHotkeyActions actions)
    {
        ArgumentNullException.ThrowIfNull(actions);
        _actions = actions;
    }

    /// <summary>按绑定表重建（设置变更后调用；同键重复时**先到者胜**）。</summary>
    /// <param name="hotkeys">动作 → 绑定文本。</param>
    /// <returns>被忽略的重复绑定（动作标题列表）。</returns>
    public IReadOnlyList<string> Rebuild(IReadOnlyDictionary<string, string>? hotkeys)
    {
        _bindings.Clear();
        var conflicts = new List<string>();

        foreach (var action in HotkeyCatalog.Actions)
        {
            var text = hotkeys is not null && hotkeys.TryGetValue(action.Id, out var configured)
                ? configured
                : action.DefaultBinding;

            var binding = HotkeyBinding.Parse(text);
            if (binding.IsEmpty)
            {
                continue;   // 空 = 不绑定
            }

            if (_bindings.TryGetValue(binding, out var owner))
            {
                conflicts.Add($"{action.Title}（与「{HotkeyCatalog.Find(owner)?.Title ?? owner}」冲突：{binding}）");
                continue;
            }

            _bindings[binding] = action.Id;
        }

        return conflicts;
    }

    /// <summary>把快捷键挂到窗口上（外壳、模块窗口、弹窗都要挂）。</summary>
    /// <param name="window">目标窗口。</param>
    public void Attach(Window window)
    {
        ArgumentNullException.ThrowIfNull(window);

        if (_attached.Contains(window))
        {
            return;
        }

        _attached.Add(window);
        window.AddHandler(InputElement.KeyDownEvent, OnKeyDown, RoutingStrategies.Bubble);
        window.Closed += (_, _) => _attached.Remove(window);
    }

    /// <summary>当前生效的绑定（动作 → 绑定文本；界面展示与冲突检测用）。</summary>
    public IReadOnlyDictionary<string, string> EffectiveBindings =>
        _bindings.ToDictionary(pair => pair.Value, pair => pair.Key.ToString(), StringComparer.Ordinal);

    private void OnKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Handled)
        {
            return;
        }

        // 【用户第三十四轮反馈："在输入网络标签名的时候，会触发快捷键"】**正在打字 ⇒ 不是快捷键**：
        // 端口标记的编辑框、模块视图里的文本框、设置页的快捷键输入框…只要焦点在**文本输入类控件**上，
        // 这一层就完全不介入（否则输入 `l` 会顺手把叠加层切掉、输入空格会触发别的动作）。
        if (IsTextEntry(e.Source))
        {
            return;
        }

        var binding = HotkeyBinding.From(e.Key, e.KeyModifiers);
        if (binding.IsEmpty || !_bindings.TryGetValue(binding, out var actionId))
        {
            return;
        }

        e.Handled = true;
        _actions.Execute(actionId);
    }

    /// <summary>事件是否来自**文本输入类控件**（文本框 / 数值框 / 下拉可输入框及其内部元素）。</summary>
    /// <param name="source">事件源（通常是获得焦点的控件）。</param>
    /// <returns>是文本输入 ⇒ 快捷键层让路。</returns>
    private static bool IsTextEntry(object? source)
    {
        if (source is TextBox or NumericUpDown or AutoCompleteBox)
        {
            return true;
        }

        // 事件源可能是输入控件模板内部的元素（如 TextPresenter / ScrollViewer）⇒ 向上找一层
        return source is Visual visual
            && visual.GetVisualAncestors().Any(a => a is TextBox or NumericUpDown or AutoCompleteBox);
    }
}
