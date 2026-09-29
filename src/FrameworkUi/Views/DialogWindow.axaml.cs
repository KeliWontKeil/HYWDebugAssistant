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

        // 【2026-09 缺陷修复 · 用户实测"侧边栏的默认大小需要一致化（设置/Issue 等）"】
        // 这里**原先按种类覆盖了尺寸**（清单 720×520 / Issue 900×520 / 设置与拓扑 520×460），
        // 与 `DialogWindow.axaml` 里写死的"外层容器尺寸固定 = 780×620"**直接矛盾** ⇒ 四个弹窗大小各异。
        // 现在**只保留 XAML 那一处**（尺寸只有一个来源；内容排不下由各自的 ScrollViewer 滚动）。
        Body.DataContext = DataContext;
        DataContextChanged += (_, _) => Body.DataContext = DataContext;
    }
}
