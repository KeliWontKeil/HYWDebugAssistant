using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Avalonia.VisualTree;
using HYWDebugAssistant.FrameworkUi.Controls;
using HYWDebugAssistant.FrameworkUi.ViewModels;
using HYWDebugAssistant.FrameworkUi.Views;
using HYWDebugAssistant.Ui.Design;
using Shouldly;
using Xunit;

namespace HYWDebugAssistant.VisualSnapshots;

/// <summary>
/// **字体选择器的判据**：转换器兜底、原生可编辑 ComboBox、输入过滤、选中回写、逐项字体预览、渲染冒烟。
/// </summary>
/// <remarks>
/// 无头环境没有真实系统字体服务，所以候选项一律由用例注入（产品里来自设置视图模型的
/// `FontFamilyOptions`）。真正“用眼睛看下拉长什么样”的部分仍由人验收。
/// </remarks>
[Collection("visual-snapshots")]
public sealed class FontFamilyPickerTests
{
    private static readonly string[] Fonts = ["Cascadia Mono", "Consolas", "Segoe UI", "SimSun"];

    [Fact]
    public void 转换器_合法名字保留_空名字回退默认字体_任意名字不抛()
    {
        var converter = new FontFamilyPreviewConverter();

        var valid = converter.Convert("Consolas", typeof(FontFamily), null, CultureInfo.InvariantCulture)
            .ShouldBeOfType<FontFamily>();
        valid.Name.ShouldBe("Consolas");

        foreach (var candidate in new string?[] { null, string.Empty, "   " })
        {
            var fallback = converter.Convert(candidate, typeof(FontFamily), null, CultureInfo.InvariantCulture)
                .ShouldBeOfType<FontFamily>();
            fallback.Name.ShouldBe(FontFamily.Default.Name, "空名字必须回退默认字体，不能让绑定抛异常");
        }

        var arbitrary = converter.Convert("!!! not a font !!!", typeof(FontFamily), null, CultureInfo.InvariantCulture)
            .ShouldBeOfType<FontFamily>();
        arbitrary.Name.ShouldNotBeNullOrWhiteSpace();
    }

    [Fact]
    public async Task 字体选择器_复用原生可编辑ComboBox模板()
    {
        var session = SnapshotSession.Shared;

        await session.Dispatch(
            () =>
            {
                var (picker, window) = CreatePicker(selected: "Cascadia Mono");

                try
                {
                    picker.IsEditable.ShouldBeTrue("字体名允许手工输入");
                    picker.Template.ShouldNotBeNull("必须使用主题里的 ComboBox 模板");
                    picker.GetVisualDescendants().OfType<ToggleButton>().ShouldBeEmpty(
                        "不得再自建灰色方块箭头按钮");
                    picker.GetVisualDescendants().OfType<PathIcon>()
                        .ShouldContain(icon => icon.Name == "DropDownGlyph", "必须使用 ComboBox 自带的下拉箭头");
                }
                finally
                {
                    window.Close();
                    Pump();
                }
            },
            CancellationToken.None);
    }

    [Fact]
    public async Task 输入片段_只收窄候选_不把搜索词写成选中字体()
    {
        var session = SnapshotSession.Shared;

        await session.Dispatch(
            () =>
            {
                var (picker, window) = CreatePicker(selected: "Cascadia Mono");

                try
                {
                    picker.IsDropDownOpen = true;
                    picker.Text = "cons";
                    Pump();

                    var items = ItemsOf(picker);
                    items.ShouldNotBeEmpty();
                    items.ShouldAllBe(item => item.Contains("cons", StringComparison.OrdinalIgnoreCase));
                    items.ShouldContain("Consolas");
                    items.ShouldNotContain("Segoe UI");
                }
                finally
                {
                    window.Close();
                    Pump();
                }
            },
            CancellationToken.None);
    }

    [Fact]
    public async Task 选中候选项_应回写SelectedFont_外部改SelectedFont应更新文本()
    {
        var session = SnapshotSession.Shared;

        await session.Dispatch(
            () =>
            {
                var (picker, window) = CreatePicker(selected: "Cascadia Mono");

                try
                {
                    picker.SelectedFont.ShouldBe("Cascadia Mono", "初始选中字体必须已同步");
                    picker.SelectedItem.ShouldBe("Cascadia Mono", "初始选中项必须已同步");

                    var changes = new List<string>();
                    picker.PropertyChanged += (_, e) =>
                        changes.Add($"{e.Property.Name}:font='{picker.SelectedFont}',text='{picker.Text}',item='{picker.SelectedItem}'");

                    picker.SelectedItem = "Consolas";
                    Pump();

                    picker.SelectedItem.ShouldBe("Consolas", "选中项必须真的切到 Consolas");
                    picker.SelectedFont.ShouldBe(
                        "Consolas",
                        $"选中候选项必须落到同一个字体属性；Text='{picker.Text}'，SelectedItem='{picker.SelectedItem}'；{string.Join(" | ", changes)}");
                    picker.IsDropDownOpen.ShouldBeFalse("原生 ComboBox 选中后关闭下拉");

                    picker.SelectedFont = "SimSun";
                    Pump();
                    picker.Text.ShouldBe("SimSun", "外部改 SelectedFont 必须同步到编辑器文本");
                }
                finally
                {
                    window.Close();
                    Pump();
                }
            },
            CancellationToken.None);
    }

    [Fact]
    public async Task 手输列表外的字体名_也落到同一个属性()
    {
        var session = SnapshotSession.Shared;

        await session.Dispatch(
            () =>
            {
                var (picker, window) = CreatePicker(selected: "Cascadia Mono");

                try
                {
                    picker.Text = "Not Installed Font";
                    Pump();

                    picker.SelectedFont.ShouldBe("Not Installed Font");
                    picker.IsDropDownOpen.ShouldBeFalse("手输不应自动打开候选层");
                }
                finally
                {
                    window.Close();
                    Pump();
                }
            },
            CancellationToken.None);
    }

    [Fact]
    public async Task 候选项模板_名字与样例都用该项自己的字体()
    {
        var session = SnapshotSession.Shared;

        await session.Dispatch(
            () =>
            {
                var (picker, window) = CreatePicker(selected: "Cascadia Mono");
                var previewWindow = new Window
                {
                    Width = 360,
                    Height = 80,
                    Content = new ContentControl
                    {
                        Content = "Consolas",
                        ContentTemplate = picker.ItemTemplate,
                    },
                };

                try
                {
                    previewWindow.Show();
                    Pump();

                    var blocks = previewWindow.GetVisualDescendants().OfType<TextBlock>().ToList();
                    blocks.Count.ShouldBe(2, "一行两列：字体名 + 样例文本");

                    blocks.ShouldAllBe(
                        block => block.FontFamily.Name == "Consolas",
                        "两列都必须按候选自己的字体绘制（不是设置页的字号/字体）");

                    blocks.Select(block => block.Text ?? string.Empty)
                        .ShouldContain(text => text == "Consolas");
                    blocks.Select(block => block.Text ?? string.Empty)
                        .ShouldContain(text => text.Length > 0 && text != "Consolas");
                }
                finally
                {
                    previewWindow.Close();
                    window.Close();
                    Pump();
                }
            },
            CancellationToken.None);
    }

    [Fact]
    public async Task 无系统字体服务时_默认列表仍可显示并渲染出像素()
    {
        var session = SnapshotSession.Shared;

        await session.Dispatch(
            () =>
            {
                var defaults = new[]
                {
                    SettingsPanelViewModel.DefaultUiFontLatin,
                    SettingsPanelViewModel.DefaultUiFontCjk,
                    SettingsPanelViewModel.DefaultDataFontLatin,
                    SettingsPanelViewModel.DefaultDataFontCjk,
                };

                var (picker, window) = CreatePicker(selected: defaults[0], options: defaults);

                try
                {
                    ItemsOf(picker).ShouldBe(defaults.ToList(), ignoreOrder: false);
                    picker.Text.ShouldBe(defaults[0]);
                    picker.Bounds.Width.ShouldBeGreaterThan(0, "选择器必须真的占住一块");

                    var output = Path.Combine(Path.GetTempPath(), "hyw-font-picker");
                    _ = Directory.CreateDirectory(output);
                    var path = Path.Combine(output, "font-picker.png");
                    var size = new PixelSize(
                        Math.Max(1, (int)Math.Ceiling(window.Bounds.Width)),
                        Math.Max(1, (int)Math.Ceiling(window.Bounds.Height)));

                    using (var bitmap = new RenderTargetBitmap(size))
                    {
                        bitmap.Render(window);
#pragma warning disable CS0618   // 探针工具：旧的 Save(string) 足够
                        bitmap.Save(path);
#pragma warning restore CS0618
                    }

                    new FileInfo(path).Length.ShouldBeGreaterThan(500, "选择器不能渲染成一张空白图");
                }
                finally
                {
                    window.Close();
                    Pump();
                }
            },
            CancellationToken.None);
    }

    [Fact]
    public async Task 设置页外观_四处字体选择器都真的接线()
    {
        var session = SnapshotSession.Shared;

        await session.Dispatch(
            () =>
            {
                var settings = new SettingsPanelViewModel(EmptyRuntime.Instance, EmptyRuntime.Instance);
                settings.Load();

                var view = new SettingsView { DataContext = settings };
                var window = new Window { Width = 780, Height = 620, Content = view };

                try
                {
                    window.Show();
                    Pump();

                    var tabs = view.GetVisualDescendants().OfType<TabControl>().First();
                    var pickers = new List<FontFamilyPicker>();
                    for (var index = 0; index < tabs.ItemCount && pickers.Count == 0; index++)
                    {
                        tabs.SelectedIndex = index;
                        Pump();
                        pickers = view.GetVisualDescendants().OfType<FontFamilyPicker>().ToList();
                    }

                    pickers.Count.ShouldBe(4, "外观页有四处字体设置（界面/数据 × 西文/中文）");

                    foreach (var picker in pickers)
                    {
                        picker.Options.ShouldNotBeNull("候选项必须真的从视图模型的 FontFamilyOptions 绑进来");
                        picker.Options!.Count.ShouldBeGreaterThan(
                            0,
                            "空候选 = 下拉永远没东西（绑定没接上时界面不会报错）");
                    }

                    pickers.Select(picker => picker.SelectedFont).ShouldBe(
                        new string?[]
                        {
                            settings.UiFontLatin,
                            settings.UiFontCjk,
                            settings.DataFontLatin,
                            settings.DataFontCjk,
                        },
                        "每处选择器绑到各自那一个字体字段");

                    pickers[0].SelectedFont = "SimSun";
                    Pump();
                    settings.UiFontLatin.ShouldBe("SimSun", "选择器回写必须落到设置视图模型（只绑 SelectedFont）");
                }
                finally
                {
                    window.Close();
                    Pump();
                }
            },
            CancellationToken.None);
    }

    /// <summary>造一个已显示的选择器窗口（候选项由调用方注入，不依赖系统字体服务）。</summary>
    private static (FontFamilyPicker Picker, Window Window) CreatePicker(string selected, string[]? options = null)
    {
        var picker = new FontFamilyPicker
        {
            Width = 280,
            Options = options ?? Fonts,
            SelectedFont = selected,
        };

        var window = new Window
        {
            Width = 320,
            Height = 100,
            Content = new Border { Padding = new Thickness(10), Child = picker },
        };

        window.Show();
        Pump();

        return (picker, window);
    }

    private static List<string> ItemsOf(FontFamilyPicker picker) =>
        picker.ItemsSource?.Cast<string>().ToList() ?? [];

    private static void Pump()
    {
        Dispatcher.UIThread.RunJobs();
        AvaloniaHeadlessPlatform.ForceRenderTimerTick();
        Dispatcher.UIThread.RunJobs();
    }
}
