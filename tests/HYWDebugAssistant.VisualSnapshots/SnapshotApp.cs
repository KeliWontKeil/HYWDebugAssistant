using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml.Styling;
using Avalonia.Themes.Fluent;

namespace HYWDebugAssistant.VisualSnapshots;

/// <summary>
/// 快照专用的**极简 Application**：只装"模块窗口渲染所必需的资源与样式"。
/// </summary>
/// <remarks>
/// <list type="bullet">
///   <item><b>为什么不直接用 `HYWDebugAssistant.App.App`</b>：那会让本测试项目**引用 App 项目** ⇒ 构建它就要重新
///     拷贝 App 的输出 ⇒ **应用正在运行时（App 的 `bin` 输出被锁）快照工具就不可用** —— 而调 UI 恰恰发生在应用开着的时候。
///     本类只做 App 的资源装载（同一条 `avares://HYWDebugAssistant.Ui/...` 路径），与 App 解耦换来"随时可跑"。</item>
///   <item><b>保真度</b>：与 `App.axaml` **同一批资源**（Tokens 合并字典 + Fluent 主题 + Controls 样式）；
///     差别仅在于不装 `ViewLocator`（模块窗口不靠它解析视图）。若将来 App 增加**渲染必需**的资源，
///     这里要同步补一行 —— 缺了会**大声失败**（资源找不到 ⇒ 渲染抛异常），不会静默画错。</item>
/// </list>
/// </remarks>
public sealed class SnapshotApp : Application
{
    /// <inheritdoc />
    public override void Initialize()
    {
        // 与 App.axaml 等价：Fluent 主题 + Ui 的控件样式 + Tokens 合并字典
        Styles.Add(new FluentTheme());

        // **调色板控件的模板**（与 App.axaml 同一行）：不挂 ⇒ `ColorView` 没有模板 ⇒ 什么都不画
        Styles.Add(new StyleInclude(new Uri("avares://Avalonia.Controls.ColorPicker/"))
        {
            Source = new Uri("avares://Avalonia.Controls.ColorPicker/Themes/Fluent/Fluent.xaml"),
        });

        Styles.Add(new StyleInclude(new Uri("avares://HYWDebugAssistant.Ui/"))
        {
            Source = new Uri("avares://HYWDebugAssistant.Ui/Styles/Controls.axaml"),
        });

        // 模块内容区自己的外观（与 App.axaml 同一行，且必须排在 Controls.axaml 之后）：
        // 内容区要按自己的密度压掉全局给 `TextBox` 定的固定高，因此**渲染必需**，缺了会画错。
        Styles.Add(new StyleInclude(new Uri("avares://HYWDebugAssistant.Ui/"))
        {
            Source = new Uri("avares://HYWDebugAssistant.Ui/ModuleSurface/Theme/ModuleSurfaceStyles.axaml"),
        });

        // **按钮的壳与五态**（与 App.axaml 同一行，且同样排在最末）：
        // Fluent 有一条按模板部件名匹配的指针态样式，声明在前的会被它盖掉（实测）。
        Styles.Add(new StyleInclude(new Uri("avares://HYWDebugAssistant.Ui/"))
        {
            Source = new Uri("avares://HYWDebugAssistant.Ui/Styles/ButtonChrome.axaml"),
        });

        Resources.MergedDictionaries.Add(new ResourceInclude(new Uri("avares://HYWDebugAssistant.Ui/"))
        {
            Source = new Uri("avares://HYWDebugAssistant.Ui/Styles/Tokens.axaml"),
        });

        // 模块内容区自己的**尺寸**（与 App.axaml 同一行）：模块表面渲染必需，缺了会大声失败。
        // 颜色不在这里 —— 色板只有一份（上面的 Tokens.axaml）。
        Resources.MergedDictionaries.Add(new ResourceInclude(new Uri("avares://HYWDebugAssistant.Ui/"))
        {
            Source = new Uri("avares://HYWDebugAssistant.Ui/ModuleSurface/Theme/ModuleSurfaceMetrics.axaml"),
        });

        // **Fluent 桥接表**（与 App.axaml 同一行、同样排在最末）：
        // 它不影响"能不能渲染"（控件都有模板），但影响**画出来的颜色**（按钮字、滚动条、下拉、列表选中…）。
        // 少了它，人眼在快照里看到的与真实应用**不是同一套色** —— 那种差别不会报错，只会误导。
        Styles.Add(new StyleInclude(new Uri("avares://HYWDebugAssistant.Ui/"))
        {
            Source = new Uri("avares://HYWDebugAssistant.Ui/Styles/FluentBridge.axaml"),
        });
    }

    /// <inheritdoc />
    public override void OnFrameworkInitializationCompleted()
    {
        // 快照不需要主窗口（窗口由测试自己 Show）——这里什么都不做，只让生命周期正常结束。
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime)
        {
            return;
        }

        base.OnFrameworkInitializationCompleted();
    }
}
