using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;
using HYWDebugAssistant.FrameworkUi.Design;
using HYWDebugAssistant.FrameworkUi.ViewModels;
using HYWDebugAssistant.FrameworkUi.Views;
using HYWDebugAssistant.Modules.TextSender;
using HYWDebugAssistant.Modules.TextSender.Views;
using HYWDebugAssistant.Sdk.Resources;
using HYWDebugAssistant.Ui.Controls;
using HYWDebugAssistant.Ui.ViewModels;
using HYWDebugAssistant.Ui.Views;
using Shouldly;
using Xunit;
using SdkIcon = HYWDebugAssistant.Sdk.Ui.Controls.Icon;
using ShapesPath = Avalonia.Controls.Shapes.Path;

namespace HYWDebugAssistant.VisualSnapshots;

/// <summary>
/// **外壳图标迁移的判据**：图形来自 Ui 的图标表（键 → 相对 `.svg`），界面里不再有硬编码码位。
/// </summary>
/// <remarks>
/// 覆盖两个单控件（`IconButton` / `Glyph`）与四个外壳视图：活动栏 7 个入口、模块窗的按钮簇与锚点两态、
/// 模块清单的分类 / 占位字形、组合页的删除按钮。
/// </remarks>
[Collection("visual-snapshots")]
public sealed class ShellIconMigrationTests
{
    [Fact]
    public async Task IconButton_按键取图_缺键退回键名_空值不碰内容()
    {
        var session = SnapshotSession.Shared;

        await session.Dispatch(
            () =>
            {
                var hit = new IconButton { Icon = ShellIcons.Reset, Classes = { "icon" } };
                var miss = new IconButton { Icon = "icon.shell.absent", Classes = { "icon" } };
                var text = new IconButton { Content = "label", Classes = { "icon" } };

                var window = new Window
                {
                    Width = 160,
                    Height = 80,
                    Content = new StackPanel { Children = { hit, miss, text } },
                };
                window.Show();
                Pump();

                var hitIcon = hit.Content.ShouldBeOfType<SdkIcon>();
                hitIcon.IconKey.ShouldBe(ShellIcons.Reset);
                hitIcon.Owner.ShouldBe(typeof(IconButton).Assembly, "图标来自 Ui 自己的清单");
                hitIcon.GetVisualDescendants().OfType<ShapesPath>().Single().Data.ShouldNotBeNull("命中时必须画出几何");
                hitIcon.GetVisualDescendants().OfType<Viewbox>().Single().IsVisible.ShouldBeTrue();

                // **有面**：图标按钮与普通按钮共用同一套面 / 边 / 字色（不再是"漂浮的符号"）。
                ColorOf(hit.Background).ShouldBe(TokenColor("SurfaceButtonFaceBrush"), "静止时必须有按钮面");
                ColorOf(hit.BorderBrush).ShouldBe(TokenColor("SurfaceButtonEdgeBrush"), "静止时必须有按钮边");
                hit.BorderThickness.ShouldBe(new Thickness(1));
                hit.CornerRadius.ShouldBe(new CornerRadius(4));
                ColorOf(hit.Foreground).ShouldBe(TokenColor("TextSecondary"), "静止时图标是次要色");

                SetPseudo(hit, ":pointerover", true);
                Pump();

                ColorOf(hit.Background).ShouldBe(TokenColor("SurfaceButtonFaceHoverBrush"), "指向时面提亮");
                ColorOf(hit.BorderBrush).ShouldBe(TokenColor("AccentPrimary"), "指向时边换主色");
                ColorOf(hit.Foreground).ShouldBe(TokenColor("TextPrimary"), "指向时图标提到正文色");
                ColorOf(hitIcon.Foreground).ShouldBe(TokenColor("TextPrimary"), "图标前景跟随按钮（继承）");

                SetPseudo(hit, ":pointerover", false);
                SetPseudo(hit, ":pressed", true);
                Pump();

                ColorOf(hit.Background).ShouldBe(TokenColor("SurfaceButtonFacePressedBrush"), "按下时面更深");
                ColorOf(hit.Foreground).ShouldBe(TokenColor("TextPrimary"), "按下时图标仍是正文色");

                SetPseudo(hit, ":pressed", false);
                Pump();

                var missIcon = miss.Content.ShouldBeOfType<SdkIcon>();
                missIcon.IconKey.ShouldBe("icon.shell.absent");
                missIcon.GetVisualDescendants().OfType<Viewbox>().Single().IsVisible.ShouldBeFalse("缺键不画几何");
                var fallback = missIcon.GetVisualDescendants().OfType<TextBlock>()
                    .Single(text => text.Name == "PART_Fallback");
                fallback.IsVisible.ShouldBeTrue("缺键要显示键名（界面字体），不能是一块空白");
                fallback.Text.ShouldBe("icon.shell.absent");

                text.Content.ShouldBe("label", "空 Icon = 文本模式，本类不碰 Content");

                window.Close();
                Pump();
            },
            CancellationToken.None);
    }

    [Fact]
    public async Task Glyph_按键取图_并命中icon与rail样式()
    {
        var session = SnapshotSession.Shared;

        await session.Dispatch(
            () =>
            {
                var plain = new Glyph { Icon = ShellIcons.Settings, Classes = { "icon" } };
                var railed = new Glyph { Icon = ShellIcons.Modules, Classes = { "icon" } };
                var rail = new Button { Classes = { "rail" }, Content = railed };

                var window = new Window
                {
                    Width = 200,
                    Height = 120,
                    Content = new StackPanel { Children = { plain, rail } },
                };
                window.Show();
                Pump();

                plain.IconKey.ShouldBe(ShellIcons.Settings);
                plain.Owner.ShouldBe(typeof(Glyph).Assembly, "`Glyph` 的 owner 固定为 Ui 程序集");
                plain.Width.ShouldBeGreaterThan(0, "`ui|Glyph.icon` 必须真的占住一块");
                plain.Height.ShouldBeGreaterThan(0);
                ColorOf(plain.Foreground).ShouldBe(TokenColor("TextSecondary"));

                railed.IconKey.ShouldBe(ShellIcons.Modules);
                railed.Width.ShouldBeGreaterThan(0, "`Button.rail ui|Glyph.icon` 的尺寸也必须命中 Glyph");
                railed.Height.ShouldBeGreaterThan(0);
                railed.Width.ShouldBeGreaterThanOrEqualTo(plain.Width, "活动栏里的字形不小于普通字形");
                railed.Height.ShouldBeGreaterThanOrEqualTo(plain.Height);

                window.Close();
                Pump();
            },
            CancellationToken.None);
    }

    [Fact]
    public async Task 外壳活动栏_七个入口都是按键取图()
    {
        var session = SnapshotSession.Shared;

        await session.Dispatch(
            () =>
            {
                var window = new MainWindow { DataContext = ShellDesignData.Shell };
                window.Show();
                Pump();

                var rail = window.GetVisualDescendants()
                    .OfType<StackPanel>()
                    .Single(panel => panel.Name == "RailButtons");
                var glyphs = rail.GetVisualDescendants().OfType<Glyph>().ToList();

                glyphs.Select(glyph => glyph.Icon).ShouldBe(
                    [
                        ShellIcons.Modules,
                        ShellIcons.Composite,
                        ShellIcons.Interaction,
                        ShellIcons.Topology,
                        ShellIcons.Issue,
                        ShellIcons.Settings,
                        ShellIcons.Help,
                    ],
                    "活动栏 7 个入口的图标键按声明顺序");
                glyphs.ShouldAllBe(glyph => !string.IsNullOrEmpty(glyph.IconKey), "每个入口都必须真的把键交给图标控件");

                // 侧栏底部的性能监视四行已接上数据源（不在这里断言具体 CPU/内存数值）。
                AssertPerformanceLine(window, "PerformanceCpu");
                AssertPerformanceLine(window, "PerformanceMemory");
                AssertPerformanceLine(window, "PerformanceThreads");
                AssertPerformanceLine(window, "PerformanceGc");

                // 活动栏是**导航**：不盒装（静止透明、指向浅底、选中浅主色），但按下要有反馈。
                // 取一个**未选中**的入口：选中的那一个自带 OverlaySelection 底（那不是"盒子"，是选中态）。
                var railButton = rail.GetVisualDescendants()
                    .OfType<Button>()
                    .First(button => !button.Classes.Contains("active"));
                ColorOf(railButton.Background).A.ShouldBe(
                    (byte)0,
                    "活动栏按钮静止时不盒装（`Button.rail` 的面仍是透明）");

                SetPseudo(railButton, ":pressed", true);
                Pump();

                ColorOf(railButton.Background).ShouldBe(
                    TokenColor("SurfaceButtonFacePressedBrush"),
                    "按下态与普通按钮同一档");

                SetPseudo(railButton, ":pressed", false);
                Pump();

                SaveSnapshot(window, "shell-main");

                window.Close();
                Pump();
            },
            CancellationToken.None);
    }

    [Fact]
    public async Task 模块清单_分类与占位图标都按键取图()
    {
        var session = SnapshotSession.Shared;

        await session.Dispatch(
            () =>
            {
                var view = new ModuleCatalogView { DataContext = ShellDesignData.Catalog };
                var window = new Window { Width = 420, Height = 760, Content = view };
                window.Show();
                Pump();

                var glyphs = view.GetVisualDescendants().OfType<Glyph>().ToList();
                var glyphKeys = glyphs.Select(glyph => glyph.Icon).ToList();

                glyphKeys.Take(5).ShouldBe(
                    [
                        ShellIcons.CatalogAll,
                        ShellIcons.CatalogSource,
                        ShellIcons.CatalogSink,
                        ShellIcons.CatalogVerify,
                        ShellIcons.CatalogOther,
                    ],
                    "分类按钮的字形键（第一行 5 个，按声明顺序）");

                foreach (var extra in glyphKeys.Skip(5))
                {
                    extra.ShouldBe(ShellIcons.Modules, "列表里的占位字形复用模块清单图标");
                }

                glyphs.ShouldAllBe(glyph => !string.IsNullOrEmpty(glyph.IconKey), "分类 / 占位图标必须真的把键交给图标控件");

                // 选中的分类：图标与文字同为强调色（图标是 `Glyph`，样式必须比 `ui|Glyph.icon` 晚声明才盖得住）。
                var activeTab = view.GetVisualDescendants()
                    .OfType<Button>()
                    .First(button => button.Classes.Contains("tab") && button.Classes.Contains("active"));
                ColorOf(activeTab.GetVisualDescendants().OfType<Glyph>().Single().Foreground).ShouldBe(
                    TokenColor("AccentPrimary"),
                    "选中的分类图标必须与文字同为强调色");

                window.Close();
                Pump();
            },
            CancellationToken.None);
    }

    [Fact]
    public async Task 组合页_删除按钮用图标按钮取关闭图标()
    {
        var session = SnapshotSession.Shared;

        await session.Dispatch(
            () =>
            {
                var page = ShellDesignData.Composites;
                page.Rows.ShouldNotBeEmpty("设计时样例必须有组合行，否则删除按钮不会实例化");

                var view = new CompositePageView { DataContext = page };
                var window = new Window { Width = 420, Height = 760, Content = view };
                window.Show();
                Pump();

                var removeButtons = view.GetVisualDescendants()
                    .OfType<IconButton>()
                    .Where(button => string.Equals(button.Icon, ShellIcons.Close, StringComparison.Ordinal))
                    .ToList();

                removeButtons.Count.ShouldBe(page.Rows.Count, "每行一个删除按钮");

                foreach (var button in removeButtons)
                {
                    button.Content.ShouldBeOfType<SdkIcon>().IconKey.ShouldBe(ShellIcons.Close, "删除按钮画的是关闭图标");
                    ColorOf(button.Background).ShouldBe(TokenColor("SurfaceButtonFaceBrush"), "删除按钮必须有按钮面");
                    button.BorderThickness.ShouldBe(new Thickness(1), "删除按钮必须有 1px 边");
                }

                window.Close();
                Pump();
            },
            CancellationToken.None);
    }

    [Fact]
    public async Task 模块窗口_按钮簇与锚点两态都按键取图()
    {
        var session = SnapshotSession.Shared;

        await session.Dispatch(
            () =>
            {
                using var harness = new OverlaySnapshotTests.SnapshotHarness();
                harness.Register(
                    "icon-module",
                    typeof(TextSenderModule),
                    typeof(TextSenderViewFactory),
                    windowWidth: 400,
                    windowHeight: 240);
                harness.AddModule("icon-module", "test/icon-module", 400, 240);

                var shell = new Window { Width = 800, Height = 600 };
                shell.Show();

                var manager = new ModuleWindowManager(harness.Windows, shell);

                try
                {
                    manager.SyncAll();
                    Pump();

                    var window = manager.WindowFor("icon-module");
                    window.ShouldNotBeNull();
                    var module = (ModuleViewViewModel)window!.DataContext!;

                    // 按钮簇平时淡出（悬停/选中才可见）：选中它，让快照里真的能看到图标。
                    module.IsPrimary = true;
                    Pump();

                    ButtonNamed(window, "WallpaperButton").Icon.ShouldBe(ShellIcons.Image);
                    ButtonNamed(window, "ResetButton").Icon.ShouldBe(ShellIcons.Reset);
                    ButtonNamed(window, "LockButton").Icon.ShouldBe(ShellIcons.Lock, "未锁定时图标表达的是**动作**（上锁）");
                    ButtonNamed(window, "HideButton").Icon.ShouldBe(ShellIcons.Hide);
                    ButtonNamed(window, "CloseButton").Icon.ShouldBe(ShellIcons.Close);

                    // 簇：5 个按钮**各自有面**，外框不再画底/边（否则是"外框 + 内框"的双重边框）。
                    var cluster = window.FindControl<Border>("Cluster")
                        ?? throw new InvalidOperationException("按钮簇 Border#Cluster 找不到");
                    cluster.Background.ShouldBeNull("簇不再画底（面在按钮上）");
                    cluster.BorderThickness.ShouldBe(new Thickness(0), "簇不再画边（边在按钮上）");
                    cluster.Padding.ShouldBe(new Thickness(2), "簇只保留 2px 内边距");

                    var clusterButtons = cluster.GetVisualDescendants().OfType<IconButton>().ToList();
                    clusterButtons.Count.ShouldBe(5, "按钮簇 = 壁纸 / 还原 / 锁定 / 隐藏 / 关闭");

                    foreach (var button in clusterButtons)
                    {
                        ColorOf(button.Background).ShouldBe(TokenColor("SurfaceButtonFaceBrush"), "簇里每个按钮都要有面");
                        button.BorderThickness.ShouldBe(new Thickness(1), "簇里每个按钮都要有边");
                        button.Content.ShouldBeOfType<SdkIcon>().IconKey.ShouldBe(button.Icon, "按钮内容必须是对应键的图标控件");
                    }

                    var anchor = window.GetVisualDescendants()
                        .OfType<Button>()
                        .Single(button => button.Classes.Contains("anchorToggle"));
                    var anchorGlyph = anchor.GetVisualDescendants().OfType<Glyph>().Single();
                    var marker = (PortMarkerViewModel)anchor.DataContext!;

                    // 初值取决于口所在边缘的默认（左右边默认"固定"），所以断言取**当前状态**而不是假设 false。
                    var wasFixed = marker.IsAnchorFixed;
                    var expectedKey = wasFixed ? ShellIcons.Lock : ShellIcons.Unlock;

                    marker.AnchorIconKey.ShouldBe(expectedKey, "图标键必须反映当前固定状态");
                    anchorGlyph.Icon.ShouldBe(expectedKey, "绑定 + StyledProperty 必须把键真的落到 Glyph 上");
                    anchorGlyph.IconKey.ShouldBe(expectedKey, "Glyph 把 Icon 映射到图标控件的 IconKey");

                    // 走真实路径：点一下锚点按钮 → 控制器提交切换 → 刷新后**同一枚** Glyph 换图标。
                    anchor.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                    Pump();

                    marker.IsAnchorFixed.ShouldBe(!wasFixed, "点击锚点按钮应切换该口的固定状态");
                    marker.AnchorIconKey.ShouldBe(
                        wasFixed ? ShellIcons.Unlock : ShellIcons.Lock,
                        "固定状态变化必须通知 AnchorIconKey");
                    anchorGlyph.Icon.ShouldBe(wasFixed ? ShellIcons.Unlock : ShellIcons.Lock, "同一枚 Glyph 必须随之换图标");
                    anchorGlyph.IconKey.ShouldBe(wasFixed ? ShellIcons.Unlock : ShellIcons.Lock);

                    // 锚点按钮已有面：静止时是 BgChrome + 1px 边；指向时面提亮、字形提正文色。
                    ColorOf(anchor.Background).A.ShouldBeGreaterThan((byte)0, "锚点按钮必须有面");
                    anchor.BorderThickness.ShouldBe(new Thickness(1));

                    SetPseudo(anchor, ":pointerover", true);
                    Pump();

                    ColorOf(anchor.Background).ShouldBe(TokenColor("SurfaceButtonFaceHoverBrush"), "指向时面提亮");
                    ColorOf(anchorGlyph.Foreground).ShouldBe(TokenColor("TextPrimary"), "指向时字形提到正文色");

                    SetPseudo(anchor, ":pointerover", false);
                    Pump();

                    module.IsLocked = true;
                    Pump();

                    ButtonNamed(window, "LockButton").Icon.ShouldBe(ShellIcons.Unlock, "锁定后图标切成解锁");
                    ButtonNamed(window, "LockButton").Content.ShouldBeOfType<SdkIcon>().IconKey
                        .ShouldBe(ShellIcons.Unlock, "图标控件也要跟着换成解锁");

                    // 簇平时淡出、选中后 120 ms 才淡入：等过渡完成再拍照，否则快照里几乎看不见按钮面。
                    SettleTransitions();
                    SaveSnapshot(window, "shell-module");
                    manager.WindowFor("icon-module").ShouldNotBeNull();
                }
                finally
                {
                    manager.Dispose();
                    if (shell.IsVisible)
                    {
                        shell.Close();
                    }

                    Pump();
                }
            },
            CancellationToken.None);
    }

    private static void AssertPerformanceLine(Window window, string name)
    {
        var line = window.GetVisualDescendants()
            .OfType<TextBlock>()
            .Single(text => text.Name == name);
        line.Text.ShouldNotBeNullOrWhiteSpace();
        (ToolTip.GetTip(line) as string).ShouldNotBeNullOrWhiteSpace("每行都必须挂完整口径，鼠标指向时才展开");
    }

    private static IconButton ButtonNamed(Window window, string name) =>
        window.FindControl<IconButton>(name)
        ?? throw new InvalidOperationException($"{name} 不是 IconButton / 找不到");

    /// <summary>取画刷的颜色（测试只认"这一档色"，不钉死具体 ARGB）。</summary>
    private static Color ColorOf(IBrush? brush)
    {
        var solid = brush as ISolidColorBrush;
        solid.ShouldNotBeNull($"{brush} 必须是纯色画刷（按钮面 / 边 / 字色都是纯色 token）");

        return solid!.Color;
    }

    /// <summary>主题 token 的实际颜色（面 / 边 / 字色都从 token 取，测试不抄色值）。</summary>
    private static Color TokenColor(string key)
    {
        Application.Current!.Resources.TryGetResource(key, Application.Current.ActualThemeVariant, out var value)
            .ShouldBeTrue($"{key} 必须由主题 token 给出");

        return ColorOf((IBrush?)value);
    }

    /// <summary>手动置指针态：headless 里没有真实指针，五态只能这样驱动一次。</summary>
    private static void SetPseudo(StyledElement control, string name, bool on) =>
        ((IPseudoClasses)control.Classes).Set(name, on);

    /// <summary>留下真实像素：字形是否看得见只能靠眼睛看（探针落在临时目录，不入库）。</summary>
    private static void SaveSnapshot(Window window, string name)
    {
        var output = Path.Combine(Path.GetTempPath(), "hyw-shell-icons");
        _ = Directory.CreateDirectory(output);

        var size = new PixelSize(
            Math.Max(1, (int)Math.Ceiling(window.Bounds.Width)),
            Math.Max(1, (int)Math.Ceiling(window.Bounds.Height)));

        using var bitmap = new RenderTargetBitmap(size);
        bitmap.Render(window);
#pragma warning disable CS0618   // 探针工具：旧的 Save(string) 足够
        bitmap.Save(Path.Combine(output, name + ".png"));
#pragma warning restore CS0618
    }

    /// <summary>把过渡跑完（无头环境下渲染节拍要手动推；簇的淡入是 120 ms 的 DoubleTransition）。</summary>
    private static void SettleTransitions()
    {
        for (var i = 0; i < 24; i++)
        {
            Pump();
            Thread.Sleep(8);
        }
    }

    private static void Pump()
    {
        Dispatcher.UIThread.RunJobs();
        AvaloniaHeadlessPlatform.ForceRenderTimerTick();
        Dispatcher.UIThread.RunJobs();
    }
}


