using Avalonia.Controls;
using Avalonia.VisualTree;
using HYWDebugAssistant.Core.Api;
using HYWDebugAssistant.Core.Runtime;
using HYWDebugAssistant.Core.Services;
using HYWDebugAssistant.Core.Settings;
using HYWDebugAssistant.FrameworkUi.ViewModels;
using HYWDebugAssistant.FrameworkUi.Views;
using HYWDebugAssistant.Hosting.Registry;
using HYWDebugAssistant.Sdk;
using HYWDebugAssistant.Sdk.Context;
using HYWDebugAssistant.Sdk.Contracts;
using HYWDebugAssistant.Sdk.Lifecycle;
using HYWDebugAssistant.Sdk.Ports;
using Shouldly;
using Xunit;

namespace HYWDebugAssistant.Tests;

/// <summary>
/// **外壳"外框"层**（状态栏 · 弹窗尺寸 · 设置页的全局还原）—— 2026-09 用户第五批反馈的回归网。
/// </summary>
/// <remarks>
/// <para>用户报的五项里，三项落在这一层：</para>
/// <list type="number">
///   <item>**③ 状态栏**：尾部那个 `Issue N` 按钮是**重复入口**（活动栏已有），且提示被挤成"拖动…"、还永不过期；</item>
///   <item>**⑤ 弹窗尺寸**：`DialogWindow.axaml` 写着 780×620，代码后置又按种类覆盖成三种大小（设置 520×460 / Issue 900×520 / 清单 720×520）⇒ 观感不一致；</item>
///   <item>**② 字体与全局还原**：字体下拉框为空（候选是单名、值是回退列表）、且没有一键还原。</item>
/// </list>
/// <para>
/// 无头会话的用例与 `ModuleWindowVisualTests` 同属一个**禁止并行**的集合（无头会话会在自己的线程上建 `Application`）。
/// </para>
/// </remarks>
[Collection(AvaloniaUiGroup.Name)]
public class ShellChromeTests : IClassFixture<HeadlessUiFixture>
{
    private readonly HeadlessUiFixture _ui;

    /// <summary>创建测试。</summary>
    /// <param name="ui">无头会话夹具（真实窗口 + 真实资源）。</param>
    public ShellChromeTests(HeadlessUiFixture ui) => _ui = ui;

    /// <summary>
    /// **四个弹窗的默认尺寸必须一致**（用户："侧边栏的默认大小需要一致化（设置/Issue 等）"）。
    /// </summary>
    /// <remarks>
    /// 缺陷现场：`DialogWindow.axaml` 声明 780×620（注释还强调"外层容器尺寸固定"），而代码后置**又按种类覆盖**
    /// ⇒ 三种不同大小。本用例逐个种类造真实窗口量尺寸 ⇒ "谁再按种类改尺寸"会当场红。
    /// </remarks>
    [Fact]
    public void 弹窗_四个种类的默认尺寸必须一致()
    {
        _ui.Run(() =>
        {
            var catalog = new DialogWindow(DialogKind.ModuleCatalog);
            var issues = new DialogWindow(DialogKind.Issues);
            var settings = new DialogWindow(DialogKind.Settings);
            var topology = new DialogWindow(DialogKind.Topology);

            catalog.Width.ShouldBe(780, "清单：XAML 里写的那个值就是唯一来源");
            catalog.Height.ShouldBe(620);

            issues.Width.ShouldBe(catalog.Width, "Issue 不得比清单窄/宽（旧实现 900）");
            issues.Height.ShouldBe(catalog.Height, "Issue 不得比清单矮/高");
            settings.Width.ShouldBe(catalog.Width, "设置不得再是那个小尺寸（旧实现 520×460）");
            settings.Height.ShouldBe(catalog.Height);
            topology.Width.ShouldBe(catalog.Width, "拓扑诊断同上");
            topology.Height.ShouldBe(catalog.Height);

            // 标题各就各位（顺带证明四个种类真的装的是不同视图）
            catalog.Title.ShouldBe("模块清单");
            issues.Title.ShouldBe("Issue");
            settings.Title.ShouldBe("设置");
            topology.Title.ShouldNotBeNullOrEmpty();
        });
    }

    /// <summary>
    /// **状态栏：重复的 Issue 入口点必须已删除、临时提示独占一列**。
    /// </summary>
    /// <remarks>
    /// 用户原话："后面的 ISSUE1 信息重复了，ISSUE 的入口点也重复了，建议**直接删掉最尾部的 ISSUE 入口点**"。
    /// 这里在**真实窗口**上验：状态栏那一条里**没有任何按钮**（入口只剩左侧活动栏那个），
    /// 且提示有自己的 `TextBlock`（不再被拼进摘要里截成"拖动…"）。
    /// </remarks>
    [Fact]
    public void 状态栏_不得再有重复的Issue入口点_且提示独占一列()
    {
        _ui.Run(() =>
        {
            using var harness = new ShellHarness();
            var window = new MainWindow { DataContext = harness.Shell };
            window.Show();
            Layout(window);

            try
            {
                // 找状态栏：它是外壳里**唯一**一个高度 28、底边有 1px 上边框的 Border
                var statusBar = window.GetVisualDescendants()
                    .OfType<Border>()
                    .FirstOrDefault(b => Math.Abs(b.Height - 28) < 0.01);

                statusBar.ShouldNotBeNull("状态栏必须存在（高度 28 的那条）");

                statusBar!.GetVisualDescendants().OfType<Button>().ShouldBeEmpty(
                    "状态栏里不得再有任何按钮（尾部那个 Issue 入口点已删；入口只剩活动栏）");

                // 提示与摘要**各占一个 TextBlock**（旧实现把提示拼进摘要 ⇒ 被截断成"拖动…"）
                var texts = statusBar.GetVisualDescendants().OfType<TextBlock>().ToList();
                texts.Count.ShouldBeGreaterThanOrEqualTo(2, "摘要与临时提示各一个 TextBlock");

                var notice = texts.FirstOrDefault(t => t.Classes.Contains("notice"));
                notice.ShouldNotBeNull("临时提示必须有自己的 TextBlock（带 notice 类）");

                // 摘要文本里**不再包含** Issue 之外的东西……更准确地说：摘要不再是"摘要 + 提示"的拼接体
                var summary = texts.First(t => !t.Classes.Contains("notice"));
                summary.Text.ShouldNotBeNull();
                summary.Text!.Contains('▏').ShouldBeFalse(
                    "提示不再拼进摘要（那正是「拖动…」被截断的根因）");
            }
            finally
            {
                window.Close();
            }
        });
    }

    /// <summary>
    /// **设置页：两个字体下拉框必须可编辑**（用户实测"字体选择框为空"的根治）。
    /// </summary>
    /// <remarks>
    /// 缺陷现场：下拉框是**非编辑**的、候选表里全是**单个系统字体名**，而设置值是**回退列表**
    /// （`Segoe UI, Microsoft YaHei UI`）⇒ `SelectedItem` 匹配不到 ⇒ **显示为空**；
    /// 用户从列表里挑了一个单名保存后就再也不空（正是他观察到的现象）。
    /// 现在：可编辑 + 绑 `Text` ⇒ 任何值都显示得出来，也能手输回退列表。
    /// </remarks>
    [Fact]
    public void 设置页_字体下拉必须可编辑_且候选里含两条默认回退列表()
    {
        _ui.Run(() =>
        {
            using var harness = new ShellHarness();
            var view = new SettingsView { DataContext = harness.Shell.Settings };
            var window = new Window { Content = view, Width = 780, Height = 620 };
            window.Show();
            Layout(window);

            try
            {
                // 字体行在「外观」页里，而设置页是**左侧 TabControl**（默认停在"常规"）
                // ⇒ 必须先把「外观」选出来，那一页才会被实例化（否则搜不到任何控件）。
                var tabs = view.GetVisualDescendants().OfType<TabControl>().ShouldHaveSingleItem();
                var appearance = tabs.Items.OfType<TabItem>()
                    .FirstOrDefault(t => t.Header as string == "外观与显示")
                    .ShouldNotBeNull("设置页必须有「外观与显示」页（字体在那里）");
                tabs.SelectedItem = appearance;
                Layout(window);

                var combos = view.GetVisualDescendants().OfType<ComboBox>().ToList();
                var editable = combos.Where(c => c.IsEditable).ToList();

                editable.Count.ShouldBe(
                    2,
                    $"只有两个字体下拉框是可编辑的（其余下拉是固定候选，不该手输）；当前共 {combos.Count} 个下拉");
                editable.ShouldAllBe(
                    c => c.Text != null && c.Text.Length > 0,
                    "可编辑下拉必须显示当前字体值（空 = 用户报的那个缺陷）");
                editable.ShouldAllBe(c => c.ItemsSource != null, "候选项仍在（去系统字体里挑；可编辑只是允许手输回退列表）");

                harness.Shell.Settings.FontFamilyOptions.ShouldContain(SettingsPanelViewModel.DefaultUiFontFamily);
                harness.Shell.Settings.FontFamilyOptions.ShouldContain(SettingsPanelViewModel.DefaultDataFontFamily);

                // 底部按钮行：全局还原按钮与"保存类"按钮同行（用户裁决的落点）
                var buttons = view.GetVisualDescendants().OfType<Button>().Select(b => b.Content as string).ToList();
                buttons.ShouldContain("恢复全部默认设置", "底部那一行要有全局式的一键还原按钮");
                buttons.ShouldContain("取消");
                buttons.ShouldContain("应用");
                buttons.ShouldContain("确认");
            }
            finally
            {
                window.Close();
            }
        });
    }

    /// <summary>跑一次布局（无头会话里布局是异步排队的，量尺寸前必须让它落地）。</summary>
    private static void Layout(Window window)
    {
        window.Measure(new Avalonia.Size(window.Width, window.Height));
        window.Arrange(new Avalonia.Rect(0, 0, window.Width, window.Height));
        window.UpdateLayout();
    }

    /// <summary>外壳测试现场（真实运行时 + 真实服务面 + 外壳视图模型）。</summary>
    private sealed class ShellHarness : IDisposable
    {
        public ShellHarness()
        {
            var settingsDirectory = Path.Combine(Path.GetTempPath(), "hyw-chrome-" + Guid.NewGuid().ToString("N"));
            _ = Directory.CreateDirectory(settingsDirectory);
            _settingsPath = Path.Combine(settingsDirectory, "settings.json");

            Runtime = new ModuleRuntime();
            var settings = new SettingsService(Runtime, _settingsPath);
            _ = settings.Load();

            Services = new RuntimeServices(Runtime, settings);
            Shell = new ShellViewModel(Services, Services);
            Shell.LoadSettings();
        }

        private readonly string _settingsPath;

        public ModuleRuntime Runtime { get; }

        public RuntimeServices Services { get; }

        public ShellViewModel Shell { get; }

        public void Dispose()
        {
            Runtime.Dispose();

            try
            {
                Directory.Delete(Path.GetDirectoryName(_settingsPath)!, recursive: true);
            }
            catch (IOException)
            {
                // 清理失败不影响结论
            }
        }
    }
}

/// <summary>
/// **外壳刷新节拍与设置页**的纯视图模型回归网（无 UI 会话）—— 2026-09 用户第五批反馈 ②③④。
/// </summary>
/// <remarks>
/// 这三条都**不需要真窗口**就能拦：① 提示过期是节拍行为；② 拓扑刷新漏了一行；③ 全局还原是字段映射。
/// 放在无头会话之外 ⇒ 跑得快、也不会被 UI 会话的线程规则牵连。
/// </remarks>
public class ShellStatusAndSettingsTests
{
    /// <summary>
    /// **临时提示：不再拼进摘要、且到点自动过期**（用户："这里的'拖动'似乎是未完成显示的信息"）。
    /// </summary>
    /// <remarks>
    /// 两条根因一起钉住：① 提示原先被拼在摘要**末尾**，主窗口只有 396 宽 ⇒ 被 `TextTrimming` 截成"拖动…"；
    /// ② 提示**从来没有过期机制**（只有下一条提示才会替换它）⇒ 陈旧提示长期挂着。
    /// 用 60 ms 的存活时长测"到点清除"（生产是 8 秒；用真等 8 秒的用例既慢又容易偶发）。
    /// </remarks>
    [Fact]
    public void 状态栏_提示应独占且到点自动过期()
    {
        using var harness = new ShellHarness(TimeSpan.FromMilliseconds(60));

        harness.Shell.Windows.RaiseNotice("拖动诊断日志已写入：C:\\temp\\hyw-drag.log");

        harness.Shell.StatusNotice.ShouldNotBeNullOrEmpty("提示本身仍在（它是有用的反馈）");
        harness.Shell.StatusText.Contains("拖动", StringComparison.Ordinal).ShouldBeFalse(
            "摘要里**不得**再出现提示文本（旧实现把提示拼在结尾 ⇒ 只有 396 宽时会被截成「拖动…」）");

        Thread.Sleep(150);
        harness.Shell.Refresh();

        harness.Shell.StatusNotice.ShouldBeNull("到点必须自动清掉（旧实现只有下一条提示才会替换它）");
        harness.Shell.StatusNoticeIsError.ShouldBeFalse();
    }

    /// <summary>
    /// **网络拓扑诊断器必须真的刷新**（用户："无论是否建网，都没有任何东西，这个问题很早就有了"）。
    /// </summary>
    /// <remarks>
    /// 真因：`ShellViewModel.Refresh()` 刷了 `Windows` / `Catalog` / `Issues`，**漏了 `Topology`**
    /// ⇒ 拓扑视图模型从来没被刷新过（只有设计时样例与测试里手工调用）⇒ 弹窗打开后表恒空、摘要恒"网络 0"。
    /// 本用例走**真实运行时**：建网 → 外壳刷新一次 ⇒ 拓扑行必须非空（旧实现必红）。
    /// </remarks>
    [Fact]
    public void 拓扑诊断_外壳刷新后必须真的有内容()
    {
        using var harness = new ShellHarness();
        harness.Register("chrome-sender", typeof(ChromeSenderModule));
        harness.Register("chrome-receiver", typeof(ChromeReceiverModule));

        harness.Services.AddModule("s1", "test/chrome-sender/1.0.0").Succeeded.ShouldBeTrue();
        harness.Services.AddModule("r1", "test/chrome-receiver/1.0.0").Succeeded.ShouldBeTrue();

        // 建网（两侧写同名标签 = 界面上的唯一建网机制）
        harness.Services.UpdatePortNetwork("s1", "out", "bus").Succeeded.ShouldBeTrue();
        harness.Services.UpdatePortNetwork("r1", "in", "bus").Succeeded.ShouldBeTrue();

        harness.Shell.Refresh();

        harness.Shell.Topology.Rows.ShouldNotBeEmpty(
            "外壳刷新一次之后拓扑表必须真的有行（旧实现漏刷 Topology ⇒ 永远空）");
        harness.Shell.Topology.Summary.ShouldContain("网络 1");
        harness.Shell.Topology.Rows.ShouldContain(r => r.NetworkName == "bus" && r.Direction == "源");
    }

    /// <summary>
    /// **全局还原默认值**（用户裁定："全局还原所有设置，包括快捷键，外观"）。
    /// </summary>
    /// <remarks>
    /// 判据：把每一项都改成非默认值（含字体、主题、吸附距离、一条快捷键），点一次还原 ⇒
    /// **全部回到 `ApplicationSettings` 的缺省值**（默认值不另写清单，直接取缺省对象）+ 快捷键回到内置默认
    /// + 状态行提示"点应用/确认生效"（与设置页既有的三键机制一致：还原只是"待生效"）。
    /// </remarks>
    [Fact]
    public void 设置_全局还原应恢复全部默认值含快捷键与字体()
    {
        using var harness = new ShellHarness();
        var settings = harness.Shell.Settings;
        var defaults = new ApplicationSettings();

        // 改成"一眼能看出不是默认"的值
        settings.Theme = "Dark";
        settings.UiFontFamily = "Georgia";
        settings.DataFontFamily = "Cascadia Code";
        settings.SnapDistance = 7;
        settings.UiRefreshRateHz = 30;
        settings.ShowInfoOverlay = true;
        settings.Hotkeys[0].Binding = "Ctrl+Shift+F12";

        settings.ResetAllSettingsCommand.Execute(null);

        settings.Theme.ShouldBe(defaults.Theme);
        settings.UiFontFamily.ShouldBe(defaults.UiFontFamily, "字体也要一起还原（用户点名的两项之一）");
        settings.DataFontFamily.ShouldBe(defaults.DataFontFamily);
        settings.SnapDistance.ShouldBe(defaults.SnapDistance);
        settings.UiRefreshRateHz.ShouldBe(defaults.UiRefreshRateHz);
        settings.ShowInfoOverlay.ShouldBe(defaults.ShowInfoOverlay);
        settings.Hotkeys.ShouldAllBe(row => row.Binding == row.DefaultBinding, "快捷键同样回到内置默认");

        settings.StatusMessage.ShouldNotBeNull();
        settings.StatusMessage!.Contains("应用", StringComparison.Ordinal).ShouldBeTrue(
            "还原只是**待生效**：提示用户点「应用」或「确认」");
    }

    /// <summary>外壳测试现场（可指定提示存活时长）。</summary>
    private sealed class ShellHarness : IDisposable
    {
        public ShellHarness(TimeSpan? noticeLifetime = null)
        {
            var settingsDirectory = Path.Combine(Path.GetTempPath(), "hyw-shell-" + Guid.NewGuid().ToString("N"));
            _ = Directory.CreateDirectory(settingsDirectory);
            _settingsPath = Path.Combine(settingsDirectory, "settings.json");

            Runtime = new ModuleRuntime();
            var settings = new SettingsService(Runtime, _settingsPath);
            _ = settings.Load();

            Services = new RuntimeServices(Runtime, settings);
            Shell = new ShellViewModel(Services, Services, viewState: null, noticeLifetime: noticeLifetime);
            Shell.LoadSettings();
        }

        private readonly string _settingsPath;

        public ModuleRuntime Runtime { get; }

        public RuntimeServices Services { get; }

        public ShellViewModel Shell { get; }

        public void Register(string name, Type entryType)
        {
            var definition = new ModuleDefinition(
                ModuleId.Create("test", name, "1.0.0"),
                entryType.Name,
                string.Empty,
                [],
                ModuleCapability.None,
                [],
                null,
                true,
                entryType,
                string.Empty,
                string.Empty);

            Services.Registry.TryRegister(definition, out var failure).ShouldBeTrue(failure);
        }

        public void Dispose()
        {
            Runtime.Dispose();

            try
            {
                Directory.Delete(Path.GetDirectoryName(_settingsPath)!, recursive: true);
            }
            catch (IOException)
            {
                // 清理失败不影响结论
            }
        }
    }

    /// <summary>拓扑用例用的发送桩（f32 输出口）。</summary>
    [ModuleDefinition("test", "chrome-sender", "1.0.0")]
    public sealed class ChromeSenderModule : IModule
    {
        /// <inheritdoc />
        public void OnRegistered(IModuleContext context) =>
            context.Declaration.DeclarePort(new PortDeclaration(
                "out", PortDirection.Send, ContractShape.OfPrimitive(ContractPrimitive.F32)));
    }

    /// <summary>拓扑用例用的接收桩（f32 输入口）。</summary>
    [ModuleDefinition("test", "chrome-receiver", "1.0.0")]
    public sealed class ChromeReceiverModule : IModule
    {
        /// <inheritdoc />
        public void OnRegistered(IModuleContext context) =>
            context.Declaration.DeclarePort(new PortDeclaration(
                "in", PortDirection.Receive, ContractShape.OfPrimitive(ContractPrimitive.F32)));
    }
}

