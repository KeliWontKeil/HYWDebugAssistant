using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Markup.Xaml.MarkupExtensions;
using Avalonia.VisualTree;

namespace HYWDebugAssistant.Sdk.Ui.Controls;

/// <summary>
/// **模块浮层面板**：把一个独立的面板正文放到模块窗口之外的**无边框独立窗口**里显示。
/// </summary>
/// <remarks>
/// <para>
/// 标准的三件套（见 `docs/zh/module-dev/controls.md`）：面板正文是独立视图
/// （`Views/*SettingsPanel.axaml`，继承 <see cref="ModuleView"/>，可被预览器单独打开）；
/// 模块视图只声明本控件并给出正文类型；窗口、标题行、定位、关闭时机与逐帧刷新由本控件负责。
/// </para>
/// <para>
/// <b>为什么不是 Popup / 视图内浮层</b>：视图内浮层被模块窗口内容区裁切、还会挡住可视化；
/// 平台 Popup 的落位与是否使用 overlay 层由平台决定，透明弹窗还会让设置项发灰。
/// 独立窗口的尺寸、位置、不透明度与关闭时机完全可控，并且能稳定放在模块之外。
/// </para>
/// <para>
/// <b>内容刷新</b>：正文离开模块可视树后由内部 <see cref="ModuleViewHost"/> 承载，
/// 本控件在每帧 <see cref="Refresh"/> 里转发一次；关闭只是隐藏窗口，正文状态保留。
/// </para>
/// </remarks>
public sealed class ModulePopupPanel : Control, IModuleViewElement
{
    /// <summary>面板窗口与外壳的底色 token（不透明）。</summary>
    private const string BackgroundToken = "BgModule";

    private Window? _window;
    private Window? _owner;
    private ModuleViewHost? _host;
    private ModulePanelSurface? _surface;
    private IModuleViewContext? _context;
    private Type? _builtPanelType;
    private bool _isOpen;

    /// <summary>面板正文类型（<see cref="ModuleView"/> 派生、公共无参构造）。</summary>
    public Type? PanelType { get; set; }

    /// <summary>标题行文本（模块 i18n）。</summary>
    public string Title { get; set; } = string.Empty;

    /// <summary>标题行刷新按钮触发的命令名（空 = 不显示刷新按钮）。</summary>
    public string ResetCommandName { get; set; } = string.Empty;

    /// <summary>定位锚点（通常是打开面板的按钮）。</summary>
    public Control? Anchor { get; set; }

    /// <summary>
    /// 构造容器：它只要行为、不要命中 —— 铺在模块视图最上层时不能把齿轮/表盘的点击吃掉。
    /// </summary>
    public ModulePopupPanel()
    {
        IsHitTestVisible = false;
        Focusable = false;
    }

    /// <summary>面板是否处于打开状态。</summary>
    public bool IsOpen => _isOpen;

    /// <summary>打开面板（设计环境不动作：正文自己可被单独预览）。</summary>
    public void Open()
    {
        if (ModuleView.IsDesignEnvironment)
        {
            return;
        }

        if (_window is not null && _builtPanelType != PanelType)
        {
            DisposeWindow();
        }

        EnsureWindow();
        if (_window is null)
        {
            return;
        }

        _isOpen = true;
        _host?.Refresh();
        if (_window.IsVisible)
        {
            _window.Activate();
            return;
        }

        if (_owner is not null)
        {
            _window.Show(_owner);
        }
        else
        {
            _window.Show();
        }

        PlaceWindow();
    }

    /// <summary>收起面板（不销毁正文，再次打开保持原状态）。</summary>
    public void Close()
    {
        _isOpen = false;
        if (_window is { IsVisible: true })
        {
            _window.Hide();
        }
    }

    /// <summary>开/关切换。</summary>
    public void Toggle()
    {
        if (_isOpen)
        {
            Close();
        }
        else
        {
            Open();
        }
    }

    /// <inheritdoc />
    public void Refresh(IModuleViewContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        _context = context;
        if (_isOpen)
        {
            _host?.Refresh();
        }
    }

    /// <inheritdoc />
    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnDetachedFromVisualTree(e);
        DisposeWindow();
    }

    private void EnsureWindow()
    {
        if (_window is not null)
        {
            return;
        }

        _context ??= this.FindAncestorOfType<ModuleViewHost>()?.Context;
        if (_context is null || PanelType is not { } panelType || Activator.CreateInstance(panelType) is not Control body)
        {
            return;
        }

        var width = body is ModuleView bodyView && bodyView.InitialWidth > 0 ? bodyView.InitialWidth : 320;
        var height = body is ModuleView tallView && tallView.InitialHeight > 0 ? tallView.InitialHeight : 360;

        _host = new ModuleViewHost(_context, body);
        _surface = new ModulePanelSurface
        {
            Title = Title,
            ResetCommandName = ResetCommandName,
            IconOwner = panelType.Assembly,
            Content = _host,
        };
        _surface.CloseRequested += OnCloseRequested;

        _owner = TopLevel.GetTopLevel(this) as Window;

        _window = new Window
        {
            Content = _surface,
            WindowDecorations = WindowDecorations.None,
            CanResize = false,
            ShowInTaskbar = false,
            WindowStartupLocation = WindowStartupLocation.Manual,
            SizeToContent = SizeToContent.Manual,
            Width = Math.Max(120, width + 2),
            Height = Math.Max(120, height + ModulePanelSurface.HeaderHeight + 2),
        };

        // 不透明底：面板叠在模块上时设置项不发灰。
        _window[!Window.BackgroundProperty] = new DynamicResourceExtension(BackgroundToken);
        _window.KeyDown += OnWindowKeyDown;

        if (_owner is not null)
        {
            _owner.Closed += OnOwnerClosed;
            _owner.PropertyChanged += OnOwnerPropertyChanged;
        }

        _builtPanelType = panelType;
    }

    /// <summary>优先放模块右侧；屏幕放不下放左侧；都放不下居中叠在模块上；始终夹在工作区内。</summary>
    private void PlaceWindow()
    {
        if (_window is null)
        {
            return;
        }

        var screens = _owner?.Screens;
        var screen = (_owner is not null ? screens?.ScreenFromWindow(_owner) : null) ?? screens?.Primary;
        if (_owner is null || screen is null)
        {
            return;
        }

        var work = screen.WorkingArea;
        var scaling = screen.Scaling > 0 ? screen.Scaling : 1;
        var width = (_window.Bounds.Width > 0 ? _window.Bounds.Width : _window.Width) * scaling;
        var height = (_window.Bounds.Height > 0 ? _window.Bounds.Height : _window.Height) * scaling;
        var origin = _owner.PointToScreen(default);
        var ownerWidth = _owner.Bounds.Width * scaling;
        var gap = 8.0;

        double x;
        var right = origin.X + ownerWidth + gap;
        var left = origin.X - width - gap;
        if (right + width <= work.Right)
        {
            x = right;
        }
        else if (left >= work.X)
        {
            x = left;
        }
        else
        {
            x = Math.Clamp(origin.X + ((ownerWidth - width) / 2), work.X, Math.Max(work.X, work.Right - width));
        }

        var y = Math.Clamp((double)origin.Y, work.Y, Math.Max(work.Y, work.Bottom - height));
        _window.Position = new PixelPoint((int)Math.Round(x), (int)Math.Round(y));
    }

    private void OnWindowKeyDown(object? sender, KeyEventArgs e)
    {
        _ = sender;
        if (e.Key == Key.Escape)
        {
            e.Handled = true;
            Close();
        }
    }

    private void OnOwnerPropertyChanged(object? sender, AvaloniaPropertyChangedEventArgs e)
    {
        _ = sender;
        if (e.Property == Visual.IsVisibleProperty && _owner is { IsVisible: false })
        {
            Close();
        }
    }

    private void OnOwnerClosed(object? sender, EventArgs e)
    {
        _ = sender;
        _ = e;
        DisposeWindow();
    }

    private void OnCloseRequested(object? sender, EventArgs e)
    {
        _ = sender;
        _ = e;
        Close();
    }

    private void DisposeWindow()
    {
        if (_owner is not null)
        {
            _owner.Closed -= OnOwnerClosed;
            _owner.PropertyChanged -= OnOwnerPropertyChanged;
            _owner = null;
        }

        if (_surface is not null)
        {
            _surface.CloseRequested -= OnCloseRequested;
            _surface = null;
        }

        if (_window is not null)
        {
            _window.KeyDown -= OnWindowKeyDown;
            _window.Close();
            _window = null;
        }

        _host = null;
        _builtPanelType = null;
        _isOpen = false;
    }
}
