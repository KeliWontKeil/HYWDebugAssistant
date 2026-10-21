using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using HYWDebugAssistant.Sdk.Ui.Controls;

namespace HYWDebugAssistant.PreviewHost;

/// <summary>
/// 预览器宿主应用：只为模块 XAML 提供主题与资源，不启动真实模块运行时。
/// </summary>
public partial class PreviewApp : Application
{
    /// <inheritdoc />
    public override void Initialize()
    {
        // 预览器宿主自己声明"我在预览"，不依赖 Design.IsDesignMode 是否被外部设置。
        ModuleView.IsPreviewHost = true;
        AvaloniaXamlLoader.Load(this);
    }

    /// <inheritdoc />
    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            desktop.MainWindow = new Window
            {
                Title = "HYWDebugAssistant · Preview Host",
                Width = 560,
                Height = 220,
                Content = new TextBlock
                {
                    Text = "这是模块 Views/*.axaml 的预览宿主。\n"
                        + "在 Avalonia 预览器右上角选择 HYWDebugAssistant.PreviewHost，再打开模块视图。",
                    TextWrapping = Avalonia.Media.TextWrapping.Wrap,
                    Margin = new Thickness(24),
                },
            };
        }

        base.OnFrameworkInitializationCompleted();
    }
}
