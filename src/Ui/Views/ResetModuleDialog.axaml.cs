using Avalonia.Controls;
using Avalonia.Interactivity;
using HYWDebugAssistant.Sdk.Text;

namespace HYWDebugAssistant.Ui.Views;

/// <summary>
/// **还原模块的确认弹窗**【用户第十一轮反馈 · 替代原「暂停」按钮】。
/// </summary>
/// <remarks>
/// 三选一：只还原默认尺寸 / 尺寸和配置都还原 / 取消。
/// 用法：<c>var scope = await new ResetModuleDialog(title).ShowDialog&lt;ModuleResetScope?&gt;(owner);</c>
/// （取消或直接关闭 → <see langword="null"/>）。
/// </remarks>
public partial class ResetModuleDialog : Window
{
    /// <summary>设计器用。</summary>
    public ResetModuleDialog() => InitializeComponent();

    /// <summary>创建弹窗。</summary>
    /// <param name="moduleTitle">模块展示名（写进提示文本）。</param>
    public ResetModuleDialog(string moduleTitle)
        : this() =>
        Message.Text = TextCatalog.Get(typeof(ResetModuleDialog).Assembly, "reset.titleWithModule", moduleTitle);

    private void OnSizeOnly(object? sender, RoutedEventArgs e) => Close(ModuleResetScope.SizeOnly);

    private void OnSizeAndConfiguration(object? sender, RoutedEventArgs e) => Close(ModuleResetScope.SizeAndConfiguration);

    private void OnCancel(object? sender, RoutedEventArgs e) => Close(null);
}
