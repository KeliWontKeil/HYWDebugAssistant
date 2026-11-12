using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Markup.Xaml;
using HYWDebugAssistant.Sdk.Ui.Geometry;

namespace HYWDebugAssistant.Sdk.Ui.Controls;

/// <summary>
/// **模块窗口的预览外壳**：外框 + 预留带 + 端口占位标记。
/// </summary>
/// <remarks>
/// <list type="bullet">
///   <item>运行时窗口、IDE 预览器与快照共用同一套几何（<see cref="ModuleSurfaceMetrics"/> /
///     <see cref="PortMarkerLayout"/>）与外框样式（`Border.moduleFrame`）；</item>
///   <item>端口只画位置/尺寸/方向色 + 口名，不伪造网络名/速率/指示灯；</item>
///   <item>只用于设计/截图；运行期不创建。</item>
/// </list>
/// </remarks>
public partial class ModulePreviewChrome : UserControl
{
    private readonly ContentPresenter? _contentHost;
    private readonly ItemsControl? _portHost;
    private Control? _child;

    /// <summary>构造预览外壳（XAML 载入）。</summary>
    public ModulePreviewChrome()
    {
        InitializeComponent();

        _contentHost = this.FindControl<ContentPresenter>("ContentHost");
        _portHost = this.FindControl<ItemsControl>("PortHost");

        if (this.FindControl<Border>("ContentPaddingHost") is { } paddingHost)
        {
            paddingHost.Padding = ModuleSurfaceMetrics.Inset;
        }

        if (_portHost is not null)
        {
            _portHost.Margin = new Thickness(-ModuleSurfaceMetrics.FrameBorder);
        }
    }

    /// <summary>要包进外壳的内容（模块视图宿主）。</summary>
    public Control? Child
    {
        get => _child;
        set
        {
            _child = value;
            if (_contentHost is not null)
            {
                _contentHost.Content = value;
            }
        }
    }

    /// <summary>视图声明的端口（空 = 只画外框）。</summary>
    public IReadOnlyList<ModulePort> Ports
    {
        get => _portHost?.ItemsSource as IReadOnlyList<ModulePort> ?? [];
        set
        {
            if (_portHost is not null)
            {
                _portHost.ItemsSource = value;
            }
        }
    }

    /// <summary>把内容包进模块窗口外壳。</summary>
    /// <param name="content">模块视图内容（运行时是宿主，预览时是视图根内容）。</param>
    /// <param name="ports">视图声明的端口（空 = 只画外框）。</param>
    public static Control Create(Control content, IReadOnlyList<ModulePort>? ports)
    {
        ArgumentNullException.ThrowIfNull(content);

        return new ModulePreviewChrome
        {
            Child = content,
            Ports = ports ?? [],
        };
    }

    private void InitializeComponent() => AvaloniaXamlLoader.Load(this);
}

/// <summary>
/// **预览外壳的端口占位层**：按 <see cref="PortMarkerLayout"/> 摆标记，不参与命中。
/// </summary>
internal sealed class PreviewPortPanel : Panel
{
    /// <inheritdoc />
    protected override Size MeasureOverride(Size availableSize)
    {
        foreach (var child in Children)
        {
            child.Measure(availableSize);
        }

        return availableSize;
    }

    /// <inheritdoc />
    protected override Size ArrangeOverride(Size finalSize)
    {
        foreach (var child in Children)
        {
            if (child.DataContext is not ModulePort port)
            {
                continue;
            }

            var marker = new Size(port.MarkerWidth, port.MarkerHeight);
            child.Arrange(PortMarkerLayout.MarkerRect(port.Side, port.Offset, finalSize, marker));
        }

        return finalSize;
    }
}
