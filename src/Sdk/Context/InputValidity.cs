using HYWDebugAssistant.Sdk.Values;
using HYWDebugAssistant.Sdk.Views;

namespace HYWDebugAssistant.Sdk.Context;

/// <summary>
/// **输入有效性的唯一记账**：无效时给外观通道一个失败轮廓，有效时清除；原因文本走值通道。
/// </summary>
/// <remarks>
/// <para>
/// 它是模块侧的状态，不是控件状态：红圈由 <see cref="IViewAppearanceAccess.SetOutlineRole"/> 驱动，
/// 控件只在收到外观变化时画/清；控件不因为每帧刷新去擦圈。
/// </para>
/// <para>
/// 重复调用只写一次外观：同一个字段从有效变无效时才设轮廓，从无效变有效时才清轮廓；
/// 原因文本变化时才重报。这样每帧校验不会产生外观/通知的抖动。
/// </para>
/// </remarks>
public sealed class InputValidity
{
    private readonly HashSet<string> _invalid = new(StringComparer.Ordinal);
    private readonly Dictionary<string, string> _messages = new(StringComparer.Ordinal);

    /// <summary>某个源当前是否被标为无效。</summary>
    /// <param name="source">值名。</param>
    public bool IsInvalid(string source) =>
        !string.IsNullOrEmpty(source) && _invalid.Contains(source);

    /// <summary>设置一个源的有效性；只有状态/原因变化时才写外观与通知。</summary>
    /// <param name="context">模块上下文。</param>
    /// <param name="source">值名。</param>
    /// <param name="invalid">是否无效。</param>
    /// <param name="message">给人看的原因（可空；空 = 只描边不换提示）。</param>
    public void Set(IModuleContext context, string source, bool invalid, string? message = null)
    {
        ArgumentNullException.ThrowIfNull(context);
        if (string.IsNullOrEmpty(source))
        {
            return;
        }

        if (invalid)
        {
            var becameInvalid = _invalid.Add(source);

            if (becameInvalid)
            {
                context.Appearance.SetOutlineRole(source, ViewInkRoles.Danger);
            }

            var text = message ?? string.Empty;
            var messageChanged = !_messages.TryGetValue(source, out var previous)
                || !string.Equals(previous, text, StringComparison.Ordinal);

            if (becameInvalid || messageChanged)
            {
                // 空串也占位：下一次同状态调用不再重复写值通道。
                _messages[source] = text;

                context.Values.ReportValidity(
                    source,
                    text.Length > 0
                        ? ValueValidity.Error(text)
                        : new ValueValidity(true, null));
            }

            return;
        }

        var wasInvalid = _invalid.Remove(source);

        if (wasInvalid)
        {
            context.Appearance.SetOutlineRole(source, null);
        }

        var hadMessage = _messages.Remove(source);

        if (wasInvalid || hadMessage)
        {
            context.Values.ReportValidity(source, ValueValidity.Valid);
        }
    }

    /// <summary>清除所有源的无效标记与原因（模块重置/还原时用）。</summary>
    /// <param name="context">模块上下文。</param>
    public void ClearAll(IModuleContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        var sources = new HashSet<string>(_invalid, StringComparer.Ordinal);
        sources.UnionWith(_messages.Keys);

        foreach (var source in sources)
        {
            context.Appearance.SetOutlineRole(source, null);
            context.Values.ReportValidity(source, ValueValidity.Valid);
        }

        _invalid.Clear();
        _messages.Clear();
    }
}
