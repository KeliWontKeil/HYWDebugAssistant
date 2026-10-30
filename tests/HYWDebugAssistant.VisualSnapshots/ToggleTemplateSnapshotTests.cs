using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Avalonia.VisualTree;
using HYWDebugAssistant.Core.Api;
using HYWDebugAssistant.FrameworkUi.ViewModels;
using HYWDebugAssistant.FrameworkUi.Views;
using HYWDebugAssistant.Sdk.Ui.Controls;
using HYWDebugAssistant.Ui.Controls;
using HYWDebugAssistant.Ui.Design;
using HYWDebugAssistant.Ui.Theming;
using Shouldly;
using Xunit;

namespace HYWDebugAssistant.VisualSnapshots;

/// <summary>
/// **外观页 + 开关模板回归**（同一真实渲染会话，避免无头会话数量继续膨胀）。
/// </summary>
/// <remarks>
/// <list type="bullet">
///   <item><b>外观页</b>：主题卡 / 单列色板必须真的画出来（结构 + 出像素冒烟；不钉像素/间距）。</item>
///   <item><b>开关回归</b>：`ButtonChrome` 的 `:is(Button)` 曾把 `CheckBox` / `RadioButton` 一起匹配
///     （它们派生自 `ToggleButton`，而 `ToggleButton` 派生自 `Button`），
///     结果设置页与交互页的开关全部拿到了 Button 模板，看起来像按钮。判据：
///     ① `CheckBox` 视觉树里不能出现 Button 模板的 `Border#Chrome`；
///     ② `ModuleToggle` 必须有 `PART_Track` / `PART_Knob`；
///     ③ 普通 `Button` 仍必须有 `Border#Chrome`；
///     ④ 留 PNG 供人工看。</item>
///   <item><b>同 collection</b>：真实渲染会话是进程级配置，必须串行（见 `ColorPickerDialogSnapshotTests`）。</item>
/// </list>
/// </remarks>
[Collection("visual-snapshots")]
public class ToggleTemplateSnapshotTests
{
    [Fact]
    public async Task 外观页与开关_必须真的画出主题卡色板与各自模板()
    {
        var session = SnapshotSession.Shared;

        // 独立目录：`OverlaySnapshotTests` 每轮会清空它自己的目录，别让本测试的证据被删。
        var output = Path.Combine(Path.GetTempPath(), "hyw-settings-snapshot");
        _ = Directory.CreateDirectory(output);
        var settingsFile = Path.Combine(output, "settings-appearance.png");
        var settingsSystemFile = Path.Combine(output, "settings-system.png");
        var toggleFile = Path.Combine(output, "toggle-regression.png");
        var issuesFile = Path.Combine(output, "issues.png");
        var topologyFile = Path.Combine(output, "topology.png");
        var topologyNormalFile = Path.Combine(output, "topology-normal.png");

        var themeCardCount = 0;
        var paletteGroupCount = 0;
        var systemLightChoiceCount = 0;
        var systemDarkChoiceCount = 0;
        var issueCount = 0;
        var issueDetail = string.Empty;
        var topologyNetworkCount = 0;
        var topologySourceCount = 0;
        var checkBoxHasButtonChrome = true;
        var toggleHasTrack = false;
        var toggleHasKnob = false;
        var uiToggleHasTrack = false;
        var uiToggleHasKnob = false;
        var buttonHasChrome = false;

        await session.Dispatch(
            () =>
            {
                // ① 外观页：主题卡 + 单列色板
                var settings = new SettingsPanelViewModel(EmptyRuntime.Instance, EmptyRuntime.Instance);
                settings.Load();

                var settingsView = new SettingsView { DataContext = settings };
                var settingsWindow = new Window
                {
                    Width = 780,
                    Height = 620,
                    // 与 DialogWindow 一致：设置内容住在 BgModule 上（设计样例是深色自定义方案）
                    Background = new SolidColorBrush(Color.Parse("#252526")),
                    Content = settingsView,
                };

                settingsWindow.Show();
                Pump();

                var tabs = settingsView.GetVisualDescendants().OfType<TabControl>().First();
                tabs.SelectedIndex = 1;   // 常规 / 外观 / 窗口与交互 / 快捷键 / 关于
                Pump();

                var themeCards = settingsView.FindControl<ItemsControl>("ThemeCards");
                var paletteGroups = settingsView.FindControl<ItemsControl>("PaletteGroups");

                themeCards.ShouldNotBeNull("外观页必须有主题卡容器");
                paletteGroups.ShouldNotBeNull("外观页必须有单列色板容器");

                themeCardCount = themeCards!.Items.Count;
                paletteGroupCount = paletteGroups!.Items.Count;

                Render(settingsWindow, settingsFile);

                // 跟随系统：两个模式方案下拉（不显示改名输入 / 新建按钮）
                settings.SelectTheme(ThemeIds.System);
                Pump();
                systemLightChoiceCount = settings.SystemLightChoices.Count;
                systemDarkChoiceCount = settings.SystemDarkChoices.Count;
                Render(settingsWindow, settingsSystemFile);

                settingsWindow.Close();
                Pump();

                // 清掉设计样例装上去的自定义主题，避免静态主题字典把后续会话拖进深色/旧资源。
                ThemeApplier.Apply(
                    new ApplicationSettings
                    {
                        Appearance = new AppearanceSettings
                        {
                            ThemeSelection = ThemeIds.System,
                            UiFonts = new FontFamilySetting(),
                            DataFonts = new FontFamilySetting(),
                        },
                    },
                    EmptyRuntime.Instance.GetThemeFiles());

                // ② 开关模板回归：CheckBox / 宿主 UiToggle / 模块 ModuleToggle / 普通 Button
                var checkBox = new CheckBox { Content = "设置开关", IsChecked = true };
                var uiToggle = new UiToggle { Content = "宿主开关", IsChecked = true };
                var toggle = new ModuleToggle { Text = "模块开关", IsChecked = true };
                var button = new Button { Content = "普通按钮" };

                var toggleWindow = new Window
                {
                    Width = 320,
                    Height = 260,
                    Background = Brushes.White,
                    Content = new StackPanel
                    {
                        Margin = new Thickness(16),
                        Spacing = 12,
                        Children = { checkBox, uiToggle, toggle, button },
                    },
                };

                toggleWindow.Show();
                Pump();

                checkBoxHasButtonChrome = checkBox.GetVisualDescendants()
                    .OfType<Border>()
                    .Any(border => string.Equals(border.Name, "Chrome", StringComparison.Ordinal));

                uiToggleHasTrack = uiToggle.GetVisualDescendants()
                    .OfType<Border>()
                    .Any(border => string.Equals(border.Name, "PART_Track", StringComparison.Ordinal));

                uiToggleHasKnob = uiToggle.GetVisualDescendants()
                    .OfType<Border>()
                    .Any(border => string.Equals(border.Name, "PART_Knob", StringComparison.Ordinal));

                toggleHasTrack = toggle.GetVisualDescendants()
                    .OfType<Border>()
                    .Any(border => string.Equals(border.Name, "PART_Track", StringComparison.Ordinal));

                toggleHasKnob = toggle.GetVisualDescendants()
                    .OfType<Border>()
                    .Any(border => string.Equals(border.Name, "PART_Knob", StringComparison.Ordinal));

                buttonHasChrome = button.GetVisualDescendants()
                    .OfType<Border>()
                    .Any(border => string.Equals(border.Name, "Chrome", StringComparison.Ordinal));

                Render(toggleWindow, toggleFile);

                // ③ Issue：列表 + 详情；拓扑：网络卡（展开以验证成员与指标真的画出来）
                var issues = new IssuesPanelViewModel(EmptyRuntime.Instance, EmptyRuntime.Instance);
                issues.Refresh();
                issues.SelectedIssue = issues.Issues.FirstOrDefault();

                var issuesWindow = new Window
                {
                    Width = 780,
                    Height = 620,
                    Background = Brushes.White,
                    Content = new IssuesView { DataContext = issues },
                };

                issuesWindow.Show();
                Pump();
                issueCount = issues.Issues.Count;
                issueDetail = issues.SelectedDetailText ?? string.Empty;
                Render(issuesWindow, issuesFile);
                issuesWindow.Close();
                Pump();

                var topology = new TopologyViewModel(EmptyRuntime.Instance);
                topology.Refresh();

                var topologyWindow = new Window
                {
                    Width = 780,
                    Height = 620,
                    Background = Brushes.White,
                    Content = new TopologyView { DataContext = topology },
                };

                topologyWindow.Show();
                Pump();
                topologyNetworkCount = topology.Networks.Count;
                topologySourceCount = topology.Networks.Sum(network => network.SourceCount);
                Render(topologyWindow, topologyFile);

                // 正常网络：源指标表应有真实数字（异常网络的未锚定源只显示破折号）
                topology.SelectedNetwork = topology.Networks.First(network => !network.IsProblem);
                Pump();
                Render(topologyWindow, topologyNormalFile);

                topologyWindow.Close();
                Pump();
            },
            CancellationToken.None);

        File.Exists(settingsFile).ShouldBeTrue($"应留下外观页快照：{settingsFile}");
        File.Exists(settingsSystemFile).ShouldBeTrue($"应留下跟随系统快照：{settingsSystemFile}");
        File.Exists(toggleFile).ShouldBeTrue($"应留下开关快照：{toggleFile}");
        File.Exists(issuesFile).ShouldBeTrue($"应留下 Issue 快照：{issuesFile}");
        File.Exists(topologyFile).ShouldBeTrue($"应留下拓扑快照：{topologyFile}");
        File.Exists(topologyNormalFile).ShouldBeTrue($"应留下拓扑正常网络快照：{topologyNormalFile}");

        themeCardCount.ShouldBeGreaterThanOrEqualTo(3, "至少应有跟随系统 + 浅色 + 深色三张主题卡");
        paletteGroupCount.ShouldBeGreaterThanOrEqualTo(1, "色板至少应有一组");
        systemLightChoiceCount.ShouldBeGreaterThanOrEqualTo(1, "跟随系统至少有一个浅色方案可选");
        systemDarkChoiceCount.ShouldBeGreaterThanOrEqualTo(1, "跟随系统至少有一个深色方案可选");
        issueCount.ShouldBeGreaterThanOrEqualTo(1, "Issue 列表应至少有一条设计样例");
        issueDetail.ShouldNotBeNullOrWhiteSpace("选中 Issue 后详情应包含对象信息");
        topologyNetworkCount.ShouldBeGreaterThanOrEqualTo(2, "拓扑样例应至少包含正常与异常两条网络");
        topologySourceCount.ShouldBeGreaterThanOrEqualTo(2, "拓扑应把源成员分组呈现");

        checkBoxHasButtonChrome.ShouldBeFalse(
            "CheckBox 不能使用 Button 模板（ButtonChrome 的 :is(Button) 必须排除 Toggle 派生控件）");
        uiToggleHasTrack.ShouldBeTrue("宿主 UiToggle 必须使用轨道 + 圆钮模板（PART_Track）");
        uiToggleHasKnob.ShouldBeTrue("宿主 UiToggle 必须使用轨道 + 圆钮模板（PART_Knob）");
        toggleHasTrack.ShouldBeTrue("ModuleToggle 必须仍使用轨道 + 圆钮模板（PART_Track）");
        toggleHasKnob.ShouldBeTrue("ModuleToggle 必须仍使用轨道 + 圆钮模板（PART_Knob）");
        buttonHasChrome.ShouldBeTrue("普通 Button 仍必须拿到 ButtonChrome（修复不能把按钮壳一起停掉）");
    }

    /// <summary>跑一遍 UI 队列与渲染节拍（无头环境要手动推）。</summary>
    private static void Pump()
    {
        Dispatcher.UIThread.RunJobs();
        AvaloniaHeadlessPlatform.ForceRenderTimerTick();
        Dispatcher.UIThread.RunJobs();
    }

    /// <summary>把窗口渲染成 PNG（只作人工排查用，像素内容不做断言）。</summary>
    private static void Render(Window window, string file)
    {
        var size = new PixelSize(
            Math.Max(1, (int)Math.Ceiling(window.Bounds.Width)),
            Math.Max(1, (int)Math.Ceiling(window.Bounds.Height)));

        using var bitmap = new RenderTargetBitmap(size);
        bitmap.Render(window);

#pragma warning disable CS0618   // 快照工具：旧的 Save(string) 足够
        bitmap.Save(file);
#pragma warning restore CS0618
    }
}
