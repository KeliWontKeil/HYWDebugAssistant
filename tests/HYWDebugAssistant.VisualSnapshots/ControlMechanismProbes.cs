using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Templates;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.VisualTree;
using HYWDebugAssistant.FrameworkUi.ViewModels;
using HYWDebugAssistant.FrameworkUi.Views;
using HYWDebugAssistant.Sdk.Ui.Controls;
using Xunit;

namespace HYWDebugAssistant.VisualSnapshots;

/// <summary>
/// **控件机制探针**（按需运行，不是截图判据，也不进自动化门禁）：
/// 验证“声明的高度被采用”“滚动条是细条且不展开”这两个容易被默认样式悄悄改掉的事实。
/// </summary>
[Collection("visual-snapshots")]
public class ControlMechanismProbes
{
    [Fact]
    public async Task ModuleSelect_声明高度应生效()
    {
        await SnapshotSession.Shared.Dispatch(
            () =>
            {
                var select = new ModuleSelect
                {
                    Width = 80,
                    Height = 18,
                    Options = [new ModuleSelectOption("index", "A")],
                    SelectedKey = "index",
                };

                var window = new Window { Width = 200, Height = 100, Content = select };

                try
                {
                    window.Show();
                    SnapshotTool.Pump();
                    Assert.InRange(select.Bounds.Height, 17, 19);
                }
                finally
                {
                    window.Close();
                }
            },
            CancellationToken.None);
    }

    [Fact]
    public async Task 滚动条_应常驻且不展开()
    {
        await SnapshotSession.Shared.Dispatch(
            () =>
            {
                var viewer = new ScrollViewer
                {
                    Width = 120,
                    Height = 60,
                    HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
                    VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                    Content = new Border
                    {
                        Width = 100,
                        Height = 400,
                        Background = Brushes.Transparent,
                    },
                };

                var window = new Window { Width = 200, Height = 120, Content = viewer };

                try
                {
                    window.Show();
                    SnapshotTool.Pump();

                    var bar = viewer.GetVisualDescendants().OfType<ScrollBar>().First();
                    Assert.False(bar.AllowAutoHide);
                    Assert.True(bar.Bounds.Width < 8, "滚动条应为细条，不得展开到默认宽条");
                }
                finally
                {
                    window.Close();
                }
            },
            CancellationToken.None);
    }

    [Fact]
    public async Task 模块清单列表_标题不可选且模块行可显示()
    {
        await SnapshotSession.Shared.Dispatch(
            () =>
            {
                var header = new ModuleCatalogListItem("header:terminal", Header: "终端");
                var module = new ModuleCatalogListItem(
                    "module:waveform-display",
                    Entry: new ModuleCatalogRowViewModel(
                        "hyw/waveform-display",
                        "波形显示器",
                        string.Empty,
                        string.Empty));

                var list = new ModuleCatalogListBox
                {
                    Width = 240,
                    Height = 120,
                    ItemsSource = new[] { header, module },
                    ItemTemplate = new FuncDataTemplate<ModuleCatalogListItem>((item, _) => new TextBlock
                    {
                        Height = 24,
                        Text = item.IsHeader ? item.Header : item.DisplayName,
                    }),
                };

                var window = new Window { Width = 260, Height = 160, Content = list };

                try
                {
                    window.Show();
                    SnapshotTool.Pump();

                    var containers = list.GetVisualDescendants().OfType<ListBoxItem>().ToList();
                    Assert.Equal(2, containers.Count);
                    Assert.False(containers[0].IsEnabled);
                    Assert.False(containers[0].Focusable);
                    Assert.False(containers[0].IsHitTestVisible);
                    Assert.True(containers[1].IsEnabled);

                    list.SelectedItem = module;
                    SnapshotTool.Pump();
                    Assert.Same(module, list.SelectedItem);
                    Assert.NotSame(header, list.SelectedItem);
                }
                finally
                {
                    window.Close();
                }
            },
            CancellationToken.None);
    }

    [Fact]
    public async Task 只有横向滚动_普通滚轮应横向滚动()
    {
        await SnapshotSession.Shared.Dispatch(
            () =>
            {
                var panel = new StackPanel
                {
                    Orientation = Orientation.Horizontal,
                    Width = 600,
                    Height = 40,
                };

                for (var index = 0; index < 10; index++)
                {
                    panel.Children.Add(new Border
                    {
                        Width = 60,
                        Height = 40,
                        Background = Brushes.Transparent,
                    });
                }

                var viewer = new ScrollViewer
                {
                    Width = 120,
                    Height = 60,
                    HorizontalAlignment = HorizontalAlignment.Left,
                    VerticalAlignment = VerticalAlignment.Top,
                    HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
                    VerticalScrollBarVisibility = ScrollBarVisibility.Disabled,
                    Content = panel,
                };

                var window = new Window { Width = 200, Height = 120, Content = viewer };

                try
                {
                    window.Show();
                    SnapshotTool.Pump();

                    Assert.True(WheelScroll.GetHorizontalFallback(viewer), "滚轮回退没有挂到 ScrollViewer");
                    Assert.True(viewer.Extent.Width > viewer.Viewport.Width, "测试内容没有产生横向滚动范围");

                    window.MouseWheel(new Point(50, 30), new Vector(0, -1), RawInputModifiers.None);
                    SnapshotTool.Pump();

                    Assert.True(viewer.Offset.X > 0, "只有横向滚动时，普通滚轮应横向滚动");
                }
                finally
                {
                    window.Close();
                }
            },
            CancellationToken.None);
    }
}
