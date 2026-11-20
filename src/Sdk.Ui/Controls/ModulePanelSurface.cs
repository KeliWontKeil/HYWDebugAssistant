using System.Reflection;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Controls.Primitives;
using HYWDebugAssistant.Sdk.Text;

namespace HYWDebugAssistant.Sdk.Ui.Controls;

/// <summary>
/// **浮层面板的统一外壳**：标题行（标题 + 刷新 + 关闭图标）+ 内容区。
/// </summary>
/// <remarks>
/// <para>
/// 它只负责"面板长什么样"：模板在 `Themes/Controls.axaml`（边框、标题行、两个图标按钮、内容呈现器），
/// 控件代码只取部件、接事件、把标题与命令名落到部件上（AR-21：视觉结构不进代码）。
/// </para>
/// <para>
/// 标题行的两个图标固定用 `icon.refresh` / `icon.close`（属主由 <see cref="IconOwner"/> 指定）：
/// 面板内容不在模块视图里，祖先链上没有 <see cref="ModuleView"/>，必须显式给属主。
/// </para>
/// </remarks>
public sealed class ModulePanelSurface : ContentControl
{
    /// <summary>标题行高度（DIP）；面板窗口在正文尺寸之外再加这一段。</summary>
    public const double HeaderHeight = 26;

    /// <summary>标题文本。</summary>
    public static readonly StyledProperty<string> TitleProperty =
        AvaloniaProperty.Register<ModulePanelSurface, string>(nameof(Title), string.Empty);

    /// <summary>刷新按钮触发的命令名（空 = 不显示刷新按钮）。</summary>
    public static readonly StyledProperty<string> ResetCommandNameProperty =
        AvaloniaProperty.Register<ModulePanelSurface, string>(nameof(ResetCommandName), string.Empty);

    private TextBlock? _title;
    private ModuleButton? _reset;
    private ModuleButton? _close;
    private ContentPresenter? _contentPresenter;
    private IModuleViewContext? _context;

    /// <summary>标题行的两枚图标按钮的属主程序集（模块程序集）。</summary>
    public Assembly? IconOwner { get; set; }

    /// <summary>用户点了关闭图标。</summary>
    public event EventHandler? CloseRequested;

    /// <summary>标题文本。</summary>
    public string Title
    {
        get => GetValue(TitleProperty);
        set => SetValue(TitleProperty, value);
    }

    /// <summary>刷新按钮触发的命令名（空 = 不显示刷新按钮）。</summary>
    public string ResetCommandName
    {
        get => GetValue(ResetCommandNameProperty);
        set => SetValue(ResetCommandNameProperty, value);
    }

    /// <inheritdoc />
    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);

        if (change.Property == TitleProperty)
        {
            ApplyTitle();
        }
        else if (change.Property == ResetCommandNameProperty)
        {
            ApplyReset();
        }
        else if (change.Property == ContentProperty && _contentPresenter is not null)
        {
            _contentPresenter.Content = Content;
        }
    }

    /// <inheritdoc />
    protected override void OnApplyTemplate(TemplateAppliedEventArgs e)
    {
        base.OnApplyTemplate(e);

        // ContentControl 只认显式注册的呈现器：不注册，Content 永远不显示。
        _contentPresenter = e.NameScope.Find<ContentPresenter>("PART_ContentPresenter");
        if (_contentPresenter is not null)
        {
            RegisterContentPresenter(_contentPresenter);
            _contentPresenter.Content = Content;
        }

        if (_close is not null)
        {
            _close.Click -= OnCloseClick;
        }

        _title = e.NameScope.Find<TextBlock>("PART_Title");
        _reset = e.NameScope.Find<ModuleButton>("PART_Reset");
        _close = e.NameScope.Find<ModuleButton>("PART_Close");

        if (_close is not null)
        {
            _close.Click += OnCloseClick;
        }

        ApplyTitle();
        ApplyIcons();
        ApplyReset();
    }

    private void ApplyTitle()
    {
        if (_title is not null)
        {
            _title.Text = Title ?? string.Empty;
        }
    }

    private void ApplyIcons()
    {
        var owner = IconOwner;
        if (_reset is not null)
        {
            _reset.IconOwner = owner;
            _reset.Icon = ModulePanelIcons.Refresh;
            ToolTip.SetTip(_reset, TextCatalog.Get(typeof(ModulePanelSurface).Assembly, "panel.reset"));
        }

        if (_close is not null)
        {
            _close.IconOwner = owner;
            _close.Icon = ModulePanelIcons.Close;
            ToolTip.SetTip(_close, TextCatalog.Get(typeof(ModulePanelSurface).Assembly, "panel.close"));
        }
    }

    /// <summary>
    /// 把视图上下文交给标题行按钮。
    /// </summary>
    /// <param name="context">取数 / 提交 / 命令入口。</param>
    /// <remarks>
    /// 外壳住在面板独立窗口里，不在正文 <see cref="ModuleViewHost"/> 的祖先链上；
    /// 刷新按钮无法像正文控件那样自己找到上下文，必须由容器显式绑定。
    /// </remarks>
    internal void BindContext(IModuleViewContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        _context = context;
        ApplyContext();
    }

    private void ApplyReset()
    {
        if (_reset is null)
        {
            return;
        }

        var command = ResetCommandName ?? string.Empty;
        _reset.CommandName = command;
        _reset.IsVisible = command.Length > 0;
        ApplyContext();
    }

    /// <summary>刷新按钮挂到当前上下文；模板重建后由 <see cref="ApplyReset"/> 重新调用。</summary>
    private void ApplyContext()
    {
        if (_reset is null || _context is not { } context)
        {
            return;
        }

        _reset.Attach(context);
        _reset.Refresh(context);
    }

    private void OnCloseClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        _ = sender;
        _ = e;
        CloseRequested?.Invoke(this, EventArgs.Empty);
    }
}
