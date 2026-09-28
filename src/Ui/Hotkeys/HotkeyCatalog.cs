using Avalonia.Input;

namespace HYWDebugAssistant.Ui.Hotkeys;

/// <summary>
/// **快捷键动作目录**（ADR-0036 D7）：动作 = 固定枚举，标题用于设置界面展示，默认绑定可被用户覆盖。
/// </summary>
/// <remarks>
/// 作用域 = **应用内**（本应用任一窗口聚焦时生效）；不实现系统级全局热键。
/// 清单以**设置界面**为准（`10` §5 的既有规定）。
/// </remarks>
public static class HotkeyCatalog
{
    /// <summary>打开模块清单弹窗。</summary>
    public const string OpenCatalog = "openCatalog";

    /// <summary>打开 Issue 弹窗。</summary>
    public const string OpenIssues = "openIssues";

    /// <summary>打开设置弹窗。</summary>
    public const string OpenSettings = "openSettings";

    /// <summary>打开拓扑诊断弹窗。</summary>
    public const string OpenTopology = "openTopology";

    /// <summary>切换**叠加信息层**显示（原"口上标记"；ADR-0039 D5）。</summary>
    public const string ToggleMarkers = "toggleMarkers";

    /// <summary>切换几何吸附。</summary>
    public const string ToggleSnap = "toggleSnap";

    /// <summary>全部锁定 / 解锁。</summary>
    public const string ToggleLockAll = "toggleLockAll";

    /// <summary>**固定 / 解锁全部端口锚点**（G5 · C5-7：锚点是否可拖动）。</summary>
    /// <remarks>
    /// 与 <see cref="ToggleLockAll"/> **不是一回事**：那个锁的是**模块窗口**（不可移动、不可缩放），
    /// 这个固定的是**端口锚点**（不可沿边拖动、不可换边）。用词按 `decisions.md` 7.14 一律「固定」。
    /// </remarks>
    public const string ToggleAnchorFixed = "toggleAnchorFixed";

    /// <summary>应用整体置顶（全体窗口一起）。</summary>
    public const string ToggleAppTopmost = "toggleAppTopmost";

    /// <summary>全部置前。</summary>
    public const string RaiseAllWindows = "raiseAllWindows";

    /// <summary>显示全部模块窗口。</summary>
    public const string ShowAllWindows = "showAllWindows";

    /// <summary>动作定义（顺序即设置界面的展示顺序）。</summary>
    public static readonly IReadOnlyList<HotkeyAction> Actions =
    [
        new(OpenCatalog, "打开模块清单", "M"),
        new(OpenIssues, "打开 Issue", "I"),
        new(OpenSettings, "打开设置", "S"),
        new(OpenTopology, "打开拓扑诊断", "T"),
        new(ToggleMarkers, "切换叠加信息层", "L"),
        new(ToggleSnap, "切换几何吸附", "G"),
        new(ToggleLockAll, "全部锁定 / 解锁", "Ctrl+L"),
        new(ToggleAnchorFixed, "固定 / 解锁端口锚点", "Ctrl+P"),
        new(ToggleAppTopmost, "应用整体置顶", "Ctrl+T"),
        new(RaiseAllWindows, "全部置前", "Ctrl+R"),
        new(ShowAllWindows, "显示全部模块窗口", "Ctrl+W"),
    ];

    /// <summary>取动作定义（未知动作返回 null）。</summary>
    /// <param name="actionId">动作标识。</param>
    public static HotkeyAction? Find(string actionId) =>
        Actions.FirstOrDefault(a => string.Equals(a.Id, actionId, StringComparison.Ordinal));

    /// <summary>默认绑定表（动作 → 绑定）。</summary>
    public static IReadOnlyDictionary<string, string> Defaults { get; } =
        Actions.ToDictionary(a => a.Id, a => a.DefaultBinding, StringComparer.Ordinal);
}

/// <summary>快捷键动作定义。</summary>
/// <param name="Id">动作标识（存进设置的键）。</param>
/// <param name="Title">界面标题。</param>
/// <param name="DefaultBinding">默认绑定（如 <c>Ctrl+L</c>、<c>M</c>）。</param>
public sealed record HotkeyAction(string Id, string Title, string DefaultBinding);

/// <summary>
/// 快捷键**绑定**（键 + 修饰键）：解析 / 格式化 / 匹配。
/// </summary>
/// <param name="Key">主键。</param>
/// <param name="Control">是否要求 Ctrl。</param>
/// <param name="Shift">是否要求 Shift。</param>
/// <param name="Alt">是否要求 Alt。</param>
public readonly record struct HotkeyBinding(Key Key, bool Control = false, bool Shift = false, bool Alt = false)
{
    /// <summary>空绑定（未设置）。</summary>
    public static HotkeyBinding None => new(Key.None);

    /// <summary>是否为空绑定。</summary>
    public bool IsEmpty => Key == Key.None;

    /// <summary>
    /// 解析绑定文本（如 <c>Ctrl+Shift+L</c>、<c>M</c>）；非法/空 → <see cref="None"/>。
    /// </summary>
    /// <param name="text">绑定文本。</param>
    public static HotkeyBinding Parse(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return None;
        }

        var control = false;
        var shift = false;
        var alt = false;
        var key = Key.None;

        foreach (var part in text.Split('+', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            switch (part.ToUpperInvariant())
            {
                case "CTRL" or "CONTROL":
                    control = true;
                    break;

                case "SHIFT":
                    shift = true;
                    break;

                case "ALT":
                    alt = true;
                    break;

                default:
                    if (Enum.TryParse<Key>(part, ignoreCase: true, out var parsed))
                    {
                        key = parsed;
                    }
                    else
                    {
                        return None;
                    }

                    break;
            }
        }

        return key == Key.None ? None : new HotkeyBinding(key, control, shift, alt);
    }

    /// <summary>由按键事件构造（修饰键取自事件）。</summary>
    /// <param name="key">主键。</param>
    /// <param name="modifiers">修饰键集合。</param>
    public static HotkeyBinding From(Key key, KeyModifiers modifiers) =>
        key is Key.LeftCtrl or Key.RightCtrl or Key.LeftShift or Key.RightShift or Key.LeftAlt or Key.RightAlt
            ? None   // 只按修饰键不算绑定
            : new HotkeyBinding(
                key,
                modifiers.HasFlag(KeyModifiers.Control),
                modifiers.HasFlag(KeyModifiers.Shift),
                modifiers.HasFlag(KeyModifiers.Alt));

    /// <summary>格式化（存设置与界面展示的统一形态）。</summary>
    public override string ToString()
    {
        if (IsEmpty)
        {
            return string.Empty;
        }

        var parts = new List<string>(4);
        if (Control)
        {
            parts.Add("Ctrl");
        }

        if (Shift)
        {
            parts.Add("Shift");
        }

        if (Alt)
        {
            parts.Add("Alt");
        }

        parts.Add(Key.ToString());
        return string.Join("+", parts);
    }
}
