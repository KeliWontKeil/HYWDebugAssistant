using Avalonia.Controls;
using HYWDebugAssistant.FrameworkUi.ViewModels;
using HYWDebugAssistant.Sdk.Text;

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
            DialogKind.ModuleCatalog => Loc("dialog.title.catalog"),
            DialogKind.Issues => "Issue",
            DialogKind.Settings => Loc("dialog.title.settings"),
            _ => Loc("dialog.title.topology"),
        };

        // 侧边栏各弹窗的默认大小必须一致化：这里若按种类覆盖尺寸
        // （清单 720×520 / Issue 900×520 / 设置与拓扑 520×460），
        // 就与 `DialogWindow.axaml` 里写死的「外层容器尺寸固定 = 780×620」**直接矛盾** ⇒ 四个弹窗大小各异。
        // ⇒ 尺寸**只保留 XAML 那一处**（尺寸只有一个来源；内容排不下由各自的 ScrollViewer 滚动）。
        Body.DataContext = DataContext;
        DataContextChanged += (_, _) => Body.DataContext = DataContext;
    }

    /// <summary>取一条本程序集文本表里的文案。</summary>
    /// <param name="key">键。</param>
    private static string Loc(string key) => TextCatalog.Get(typeof(DialogWindow).Assembly, key);
}
