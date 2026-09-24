namespace HYWDebugAssistant.Ui.Views;

/// <summary>
/// **还原范围**【用户第十一轮反馈】：模块窗口的「还原」按钮在确认弹窗里让用户选择还原到什么程度。
/// </summary>
public enum ModuleResetScope
{
    /// <summary>只还原**默认尺寸**（配置保持当前值）。</summary>
    SizeOnly,

    /// <summary>还原**尺寸 + 配置**（配置回到模块默认值）。</summary>
    SizeAndConfiguration,
}
