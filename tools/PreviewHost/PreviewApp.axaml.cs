using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using HYWDebugAssistant.Access.Parameters;
using HYWDebugAssistant.Sdk.Access;
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

        // 外部模块的配置类型在框架侧：把标准标识映射回来，预览器才能画出配置字段。
        ModuleView.RegisterStandardConfig(ExternalAccessStandardIds.Serial, typeof(SerialConfig));
        ModuleView.RegisterStandardConfig(ExternalAccessStandardIds.TcpClient, typeof(TcpClientConfig));
        ModuleView.RegisterStandardConfig(ExternalAccessStandardIds.TcpServer, typeof(TcpServerConfig));
        ModuleView.RegisterStandardConfig(ExternalAccessStandardIds.Udp, typeof(UdpConfig));

        // 仅被项目引用、代码未直接使用的模块程序集，预览器不保证会加载；
        // 先摸一遍视图类型，确保 x:Class 类型在解析时已经可解析。
        ModuleViewAnchors.Touch();

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
