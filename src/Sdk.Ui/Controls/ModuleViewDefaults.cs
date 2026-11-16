namespace HYWDebugAssistant.Sdk.Ui.Controls;

/// <summary>
/// **模块视图的默认值辅助**：把"框架设置 → 模块值的首次默认值"收敛到一处。
/// </summary>
/// <remarks>
/// 模块值一旦被用户改过就不再回退；这里只在值还没有事实（或为空）时写入一次。
/// </remarks>
public static class ModuleViewDefaults
{
    /// <summary>
    /// 首次把框架的"多行输入提交键"播种到模块自己的提交键值。
    /// </summary>
    /// <param name="context">视图上下文。</param>
    /// <param name="valueName">模块声明的提交键值名。</param>
    /// <remarks>
    /// 只在值未知/为空时写入：已有值（含用户选择或组合件恢复）不覆盖。
    /// 框架值为 <c>Enter</c> / <c>CtrlEnter</c> 时原样落值，其他值（含 <c>None</c>）落 <c>CtrlEnter</c>。
    /// </remarks>
    public static void EnsureSubmitKey(IModuleViewContext context, string valueName)
    {
        ArgumentNullException.ThrowIfNull(context);

        if (string.IsNullOrWhiteSpace(valueName))
        {
            return;
        }

        var state = context.ReadValue(valueName);
        if (!state.IsKnown || state.Value.Length > 0)
        {
            return;
        }

        context.CommitValue(
            valueName,
            string.Equals(context.MultilineSubmitKey, "Enter", StringComparison.Ordinal)
                ? "Enter"
                : "CtrlEnter");
    }
}
