using Avalonia.Controls;
using HYWDebugAssistant.FrameworkUi.ViewModels;

namespace HYWDebugAssistant.FrameworkUi.Views;

/// <summary>弹窗宿主：按种类装入对应的功能视图（模块清单 / Issue / 设置 / 拓扑诊断）。</summary>
public partial class DialogWindow : Window
{
    /// <summary>创建弹窗（设计时用）。</summary>
    public DialogWindow()
        : this(DialogKind.ModuleCatalog)
    {
    }

    /// <summary>按种类创建弹窗。</summary>
    /// <param name="kind">弹窗种类。</param>
    public DialogWindow(DialogKind kind)
    {
        InitializeComponent();

        Body.Content = kind switch
        {
            DialogKind.ModuleCatalog => new ModuleCatalogView(),
            DialogKind.Issues => new IssuesView(),
            DialogKind.Settings => new SettingsView(),
            _ => new TopologyView(),
        };

        Title = kind switch
        {
            DialogKind.ModuleCatalog => "模块清单",
            DialogKind.Issues => "Issue",
            DialogKind.Settings => "设置",
            _ => "拓扑诊断（只读）",
        };

        Width = kind switch
        {
            DialogKind.ModuleCatalog => 720,
            DialogKind.Issues => 900,
            _ => 520,
        };

        Height = kind switch
        {
            DialogKind.Issues => 520,
            DialogKind.ModuleCatalog => 520,
            _ => 460,
        };

        Body.DataContext = DataContext;
        DataContextChanged += (_, _) => Body.DataContext = DataContext;
    }
}
