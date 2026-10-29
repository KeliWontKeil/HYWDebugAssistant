using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using HYWDebugAssistant.Sdk.Ui.Controls;
using HYWDebugAssistant.Ui.ViewModels;

namespace HYWDebugAssistant.Ui.Views;

/// <summary>
/// **模块窗口的内容区** —— 把模块自带的视图挂进窗口。
/// </summary>
/// <remarks>
/// <para>
/// 它自己<b>不含任何控件知识</b>：内容全部由模块的视图工厂产出，这里只负责"挂上去、跟着换"。
/// 这个文件存在的理由是"视图模型不直接造控件树"，而窗口的 XAML 需要一个可
/// <c>DataContext="{Binding}"</c> 的元素。
/// </para>
/// <para>
/// 视图**在第一次被渲染时构造**（见 <see cref="Apply"/>），而不是构造视图模型时构造：
/// XAML 载入 + 控件树是一条不便宜的链，隐藏的窗口不该为它付钱。
/// </para>
/// </remarks>
public sealed class ModuleSurfaceView : UserControl
{
    private ModuleViewViewModel? _module;

    /// <summary>
    /// **命中宿主**（透明背景、铺满内容区）：让“内容空白/预留带”也能命中并冒泡到
    /// <see cref="ModuleSurfaceView"/> 的 `PointerPressed`（取消选中）。不改变任何像素。
    /// </summary>
    private Border? _host;

    private Border? _frame;

    /// <summary>构造内容区。</summary>
    public ModuleSurfaceView()
    {
        HorizontalAlignment = HorizontalAlignment.Stretch;
        VerticalAlignment = VerticalAlignment.Stretch;
        DataContextChanged += (_, _) => Attach(DataContext as ModuleViewViewModel);
    }

    /// <summary>当前渲染的模块视图（没有自带视图 ⇒ <see langword="null"/>）。</summary>
    public ModuleViewHost? View => _module?.ModuleView;

    private void Attach(ModuleViewViewModel? module)
    {
        if (_module is { } previous)
        {
            previous.PropertyChanged -= OnModulePropertyChanged;
        }

        _module = module;

        if (module is not null)
        {
            module.PropertyChanged += OnModulePropertyChanged;
        }

        Apply();
    }

    private void OnModulePropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        // 只有"换了一份视图"才需要换内容；每帧的数据更新走 RefreshSurface()，不经过属性通知。
        if (string.Equals(e.PropertyName, nameof(ModuleViewViewModel.ModuleView), System.StringComparison.Ordinal))
        {
            Apply();
        }
    }

    private void Apply()
    {
        var view = _module?.EnsureView();

        if (view is null)
        {
            if (_host is not null)
            {
                _host.Child = null;   // 先断开视图的父级，避免旧视图被宿主继续持有
            }

            _host = null;
            _frame = null;
            Content = null;
            return;
        }

        // 预留带是**窗口内容层的共同机制**：端口标记与运行指示叠在窗口边缘，
        // 模块视图必须让开它们；不能让每个模块自己在 XAML 里写一套 margin（那会再次漂）。
        //
        // 复用同一个 Border：视图只能有一个父级；换新 Border 会让**同一个**视图再挂一次，
        // 另一个父级仍握着它 ⇒ "control already has a visual parent"（重开/换视图时炸）。
        //
        // **透明宿主**：内容区空白/预留带原本没有任何可命中的背景 ⇒ 点上去会落到 Frame，
        // `OnContentPressed` 不触发（“点空白取消选中”时灵时不灵）。透明 Background 只参与命中，
        // 不改变观感；它在端口标记/交互带之下，不会吃掉它们的事件。
        if (_host is null)
        {
            _host = new Border { Background = Brushes.Transparent };
            Content = _host;
        }

        if (_frame is null)
        {
            _frame = new Border { ClipToBounds = true };
            _host.Child = _frame;
        }

        _frame.Margin = ContentInset();

        if (!ReferenceEquals(_frame.Child, view))
        {
            _frame.Child = view;
        }
    }

    /// <summary>
    /// 断开当前模块视图的父级（窗口关闭前调用）。
    /// </summary>
    /// <remarks>
    /// 模块的 <c>ModuleViewHost</c> 是**按实例缓存**的；窗口关闭时若只关闭窗口而不摘掉
    /// <c>_frame.Child</c>，旧窗口仍握着视图。再次“显示全部”时新内容区把同一个视图挂到
    /// 第二个父级 ⇒ <c>InvalidOperationException: The Control already has a parent.</c>
    /// </remarks>
    public void Detach()
    {
        if (_frame is { } frame)
        {
            frame.Child = null;
        }

        if (_host is { } host)
        {
            host.Child = null;
        }

        Content = null;
        _host = null;
        _frame = null;

        if (_module is { } module)
        {
            module.PropertyChanged -= OnModulePropertyChanged;
        }

        _module = null;
    }

    /// <summary>模块内容区四周的预留带（来自 `Ui/Styles/ModuleMetrics.axaml`）。</summary>
    private Thickness ContentInset() => new(
        Dip("SurfaceContentInsetLeft", 20),
        Dip("SurfaceContentInsetTop", 22),
        Dip("SurfaceContentInsetRight", 20),
        Dip("SurfaceContentInsetBottom", 24));

    /// <summary>取一个尺寸资源；缺失时用安全兜底值（不会把内容区缩成 0）。</summary>
    private double Dip(string key, double fallback) =>
        this.TryFindResource(key, out var value) && value is double dip ? dip : fallback;
}
