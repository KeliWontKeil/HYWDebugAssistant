using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.VisualTree;
using HYWDebugAssistant.Sdk.Resources;
using HYWDebugAssistant.Sdk.Values;
using IconControl = HYWDebugAssistant.Sdk.Ui.Controls.Icon;

namespace HYWDebugAssistant.Sdk.Ui.Controls;

/// <summary>模块按钮的外观档位（由宿主按钮壳翻译成色板）。</summary>
public enum ModuleButtonAppearance
{
    /// <summary>常规按钮。</summary>
    Normal = 0,

    /// <summary>主题色强调（主命令）。</summary>
    Accent = 1,

    /// <summary>删除/破坏性动作。</summary>
    Danger = 2,
}

/// <summary>
/// **命令按钮**：点一下触发模块的一条命令；可选带一个"值菜单"的小箭头。
/// </summary>
/// <remarks>
/// <para>
/// <b>按钮只管触发</b>：它不显示结果 —— 结果由模块写成一句话落在它自己的显示源上
/// （"消息不是控件的能力"）。框架的"已投递"这类回执也不上屏：用户要看的是**产生了什么结果**。
/// </para>
/// <para>
/// 它派生自 <see cref="Button"/> 而不是 <see cref="ModuleElement"/>：按钮没有"值"要按帧刷新，
/// 只有"哪条命令"与外观；外观直接由应用级按钮主题给（五态、焦点、禁用），不必再造一遍。
/// </para>
/// <para>
/// <b>图标是数据</b>：<see cref="Icon"/> 只存一个键，图形来自**本模块自己的图标表**指向的 SVG
/// （见 <see cref="IconCatalog"/>）——视图里不再出现任何几何，换图标只换 SVG 文件。
/// </para>
/// <para>
/// <b>小箭头菜单</b>：<see cref="MenuValue"/> 非空时，模板右侧的分隔与箭头可见（模板住在宿主按钮壳里）；
/// 点箭头打开一个单选菜单，选项来自该**值声明的 Choices**，当前值打勾，点选即提交。
/// 箭头只改值，不触发 <see cref="CommandName"/>：它是**独立命中段**，按下时不会交给 Button 基类，
/// 因此不存在“先触发外层 Click、再靠标志吞掉”的状态竞争。菜单段与主体段的悬停/按下态
/// 由控件代码按实际指针位置写入 `segment-*` 类，模板不再让两段各自响应 `:pointerover`。
/// </para>
/// </remarks>
public sealed class ModuleButton : Button, IModuleViewElement
{
    private IModuleViewContext? _context;
    private string _icon = string.Empty;
    private bool _hasVisualAncestor;
    private string _menuValue = string.Empty;
    private ModuleButtonAppearance _appearance;
    private Border? _bodyHost;
    private Border? _menuHost;
    private MenuFlyout? _openFlyout;
    private ButtonSegment _hoverSegment;
    private ButtonSegment _pressedSegment;
    private bool _menuOpen;
    private bool _pointerInside;
    private Point _lastPointerPoint;

    private enum ButtonSegment
    {
        None,
        Body,
        Menu,
    }

    /// <summary>造一个命令按钮。</summary>
    public ModuleButton()
    {
        HorizontalAlignment = HorizontalAlignment.Stretch;
        VerticalAlignment = VerticalAlignment.Stretch;
        Click += (_, _) => Invoke();
    }

    /// <summary>本按钮触发的命令名（模块在 <c>OnRegistered</c> 里声明过）。</summary>
    /// <remarks>
    /// 刻意不叫 <c>Command</c>：那个名字在 Avalonia 里已经是 <c>ICommand</c> 绑定槽，
    /// 两者同名会让"按钮为什么没反应"变成一个很难查的问题（一个是命令名、一个是命令对象）。
    /// </remarks>
    public string CommandName { get; set; } = string.Empty;

    /// <summary>
    /// **图标属主程序集**（可选）：不设时按所在 <see cref="ModuleView"/> 的程序集推断。
    /// </summary>
    /// <remarks>
    /// 内容被托管到模块视图之外（例如 <see cref="ModulePopupPanel"/> 的浮层里）时，
    /// 祖先链上没有 <see cref="ModuleView"/>，必须在这里显式给属主，否则图标键查不到资源。
    /// </remarks>
    public System.Reflection.Assembly? IconOwner { get; set; }

    /// <summary>
    /// **菜单值名**（可选）：非空时按钮右侧出现小箭头，菜单编辑这个值。
    /// </summary>
    /// <remarks>
    /// 值必须是枚举且声明了 <see cref="ValueChoice"/>；选项文本与稳定键都来自声明，
    /// 控件不再维护第二份选项表。箭头只提交值，不触发命令。
    /// </remarks>
    public string MenuValue
    {
        get => _menuValue;
        set
        {
            var normalized = value ?? string.Empty;
            if (string.Equals(_menuValue, normalized, StringComparison.Ordinal))
            {
                return;
            }

            _menuValue = normalized;
            SetClass("menu", normalized.Length > 0);

            if (normalized.Length == 0)
            {
                CloseMenu();
                _hoverSegment = ButtonSegment.None;
                UpdateSegmentVisuals();
            }
            else if (_openFlyout is not null)
            {
                CloseMenu();
            }
        }
    }

    /// <summary>外观档位（常规 / 主题色强调 / 删除红）。</summary>
    public ModuleButtonAppearance Appearance
    {
        get => _appearance;
        set
        {
            if (_appearance == value)
            {
                return;
            }

            _appearance = value;
            ApplyAppearanceClasses();
        }
    }

    /// <summary>
    /// 图标键（来自**本模块自己的图标表**）：非空 ⇒ 把内容换成字形。
    /// </summary>
    /// <remarks>
    /// 空 = 文本模式：`Content` 由视图给（打开 / 关闭这类按钮走它），本类绝不覆盖。
    /// 非空时按键查表，**缺表 / 缺键把键名本身写进 `Content`**——宁可界面上露出缺的是哪个键，
    /// 也不要一枚看不见的空白；此时字体退回界面字体（图标字体里的字母是空白或方块）。
    /// </remarks>
    public string Icon
    {
        get => _icon;
        set
        {
            var normalized = value ?? string.Empty;
            if (string.Equals(_icon, normalized, StringComparison.Ordinal))
            {
                return;
            }

            _icon = normalized;

            if (_hasVisualAncestor)
            {
                ApplyIcon();
            }
        }
    }

    /// <summary>挂在视图里时记住取数入口（点下去要用）。</summary>
    /// <param name="context">视图上下文。</param>
    /// <remarks>
    /// 面板外壳的标题行不在正文 <see cref="ModuleViewHost"/> 的祖先链上，
    /// 由 <see cref="ModulePanelSurface"/> 显式调用本方法；普通按钮挂树时仍自动查找宿主。
    /// </remarks>
    internal void Attach(IModuleViewContext context) => _context = context;

    /// <inheritdoc />
    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);

        _hasVisualAncestor = true;

        // 自己找宿主要上下文：模块视图里不必为此写任何代码（按钮一挂树就能用）。
        if (_context is null && this.FindAncestorOfType<ModuleViewHost>() is { } host)
        {
            Attach(host.Context);
        }

        ApplyAppearanceClasses();

        if (_icon.Length > 0)
        {
            ApplyIcon();
        }
    }

    /// <inheritdoc />
    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnDetachedFromVisualTree(e);

        CloseMenu();
        _hasVisualAncestor = false;
        _bodyHost = null;
        _menuHost = null;
        _hoverSegment = ButtonSegment.None;
        _pressedSegment = ButtonSegment.None;
        _pointerInside = false;
    }

    /// <inheritdoc />
    protected override void OnApplyTemplate(TemplateAppliedEventArgs e)
    {
        base.OnApplyTemplate(e);

        _bodyHost = e.NameScope.Find<Border>("PART_BodyHost");
        _menuHost = e.NameScope.Find<Border>("PART_MenuHost");
        _hoverSegment = ButtonSegment.None;
        _pressedSegment = ButtonSegment.None;
        _pointerInside = false;
        UpdateSegmentVisuals();
    }

    /// <inheritdoc />
    protected override void OnPointerEntered(PointerEventArgs e)
    {
        base.OnPointerEntered(e);
        UpdatePointer(e.GetPosition(this));
    }

    /// <inheritdoc />
    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        UpdatePointer(e.GetPosition(this));
    }

    /// <inheritdoc />
    protected override void OnPointerExited(PointerEventArgs e)
    {
        base.OnPointerExited(e);

        _pointerInside = false;
        _hoverSegment = ButtonSegment.None;
        UpdateSegmentVisuals();
    }

    /// <inheritdoc />
    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        if (!IsEnabled || !e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
        {
            base.OnPointerPressed(e);
            return;
        }

        var point = e.GetPosition(this);
        _pointerInside = true;
        _lastPointerPoint = point;

        if (SegmentAt(point) == ButtonSegment.Menu)
        {
            // 箭头段不交给 Button 基类：它既不该被按下，也不该产生 Click。
            _pressedSegment = ButtonSegment.Menu;
            _hoverSegment = ButtonSegment.Menu;
            UpdateSegmentVisuals();
            OpenMenu();
            _pressedSegment = ButtonSegment.None;
            UpdateSegmentVisuals();
            e.Handled = true;
            return;
        }

        _pressedSegment = ButtonSegment.Body;
        _hoverSegment = ButtonSegment.Body;
        UpdateSegmentVisuals();
        base.OnPointerPressed(e);
    }

    /// <inheritdoc />
    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);

        _pressedSegment = ButtonSegment.None;
        UpdateSegmentVisuals();
    }

    /// <inheritdoc />
    protected override void OnPointerCaptureLost(PointerCaptureLostEventArgs e)
    {
        base.OnPointerCaptureLost(e);

        _pressedSegment = ButtonSegment.None;
        _pointerInside = Bounds.Contains(_lastPointerPoint);
        _hoverSegment = _pointerInside ? SegmentAt(_lastPointerPoint) : ButtonSegment.None;
        UpdateSegmentVisuals();
    }

    /// <inheritdoc />
    protected override void OnKeyDown(KeyEventArgs e)
    {
        if (IsEnabled
            && _menuValue.Length > 0
            && (e.Key == Key.F4 || (e.Key == Key.Down && e.KeyModifiers.HasFlag(KeyModifiers.Alt))))
        {
            OpenMenu();
            e.Handled = true;
            return;
        }

        base.OnKeyDown(e);
    }

    /// <inheritdoc />
    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);

        if (change.Property == IsEnabledProperty && !IsEnabled)
        {
            _pressedSegment = ButtonSegment.None;
            _hoverSegment = ButtonSegment.None;
            _pointerInside = false;
            CloseMenu();
            UpdateSegmentVisuals();
        }
    }

    /// <inheritdoc />
    public void Refresh(IModuleViewContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        _context = context;

        if (CommandName.Length == 0)
        {
            return;
        }

        var enabled = context.Appearance(CommandName).Enabled ?? true;
        if (IsEnabled != enabled)
        {
            IsEnabled = enabled;
        }
    }

    /// <summary>触发一次（供测试直接调用）。</summary>
    public void Invoke()
    {
        if (!IsEnabled || _context is null || CommandName.Length == 0)
        {
            return;
        }

        // 结论由框架收进 Issue（失败时用户能看到为什么）；按钮自己不解释、不弹窗。
        _ = _context.InvokeCommand(CommandName);
    }

    /// <summary>把当前外观档位翻译成样式类（色板在宿主按钮壳里）。</summary>
    private void ApplyAppearanceClasses()
    {
        SetClass("accent", _appearance == ModuleButtonAppearance.Accent);
        SetClass("danger", _appearance == ModuleButtonAppearance.Danger);
    }

    private void SetClass(string name, bool on)
    {
        if (on)
        {
            if (!Classes.Contains(name))
            {
                Classes.Add(name);
            }
        }
        else
        {
            _ = Classes.Remove(name);
        }
    }

    /// <summary>图标模式的落点：把内容换成**一个图标控件**（键 + 本模块程序集）。</summary>
    private void ApplyIcon()
    {
        if (_icon.Length == 0)
        {
            // 文本模式：`Content` 是视图给的文字，这里不碰。
            return;
        }

        var icon = new IconControl
        {
            IconKey = _icon,
            Owner = IconOwner ?? this.FindAncestorOfType<ModuleView>()?.GetType().Assembly,
        };

        // 图标不是独立的颜色档位：它和按钮里的文字共用同一个 Foreground，
        // danger/accent 变白、指针态变深时一起走。
        icon[!TemplatedControl.ForegroundProperty] =
            this[!TemplatedControl.ForegroundProperty];

        Content = icon;
    }

    /// <summary>指针位置对应的段；菜单段几何直接取模板部件的实际 Bounds。</summary>
    private ButtonSegment SegmentAt(Point point)
    {
        if (_menuValue.Length == 0
            || _menuHost is not { IsVisible: true } menu
            || menu.Bounds.Width <= 0)
        {
            return ButtonSegment.Body;
        }

        if (menu.TranslatePoint(default, this) is not { } origin)
        {
            return ButtonSegment.Body;
        }

        return point.X >= origin.X && point.X < origin.X + menu.Bounds.Width
            ? ButtonSegment.Menu
            : ButtonSegment.Body;
    }

    /// <summary>更新指针悬停段；只在段确实变化时写类，避免每帧重刷视觉。</summary>
    private void UpdatePointer(Point point)
    {
        _lastPointerPoint = point;
        _pointerInside = Bounds.Contains(point);

        var segment = _pointerInside ? SegmentAt(point) : ButtonSegment.None;
        if (segment == _hoverSegment)
        {
            return;
        }

        _hoverSegment = segment;
        UpdateSegmentVisuals();
    }

    /// <summary>把当前段状态写到两个模板部件上；这是唯一的视觉状态写入点。</summary>
    private void UpdateSegmentVisuals()
    {
        if (_menuValue.Length == 0)
        {
            // 普通按钮完全交给外层 Button 的原生五态；段类必须清干净，避免残留底色盖住悬停。
            SetSegmentClass(_bodyHost, "segment-hover", false);
            SetSegmentClass(_menuHost, "segment-hover", false);
            SetSegmentClass(_bodyHost, "segment-pressed", false);
            SetSegmentClass(_menuHost, "segment-pressed", false);
            SetSegmentClass(_menuHost, "segment-open", false);
            return;
        }

        SetSegmentClass(_bodyHost, "segment-hover", _hoverSegment == ButtonSegment.Body);
        SetSegmentClass(_menuHost, "segment-hover", _hoverSegment == ButtonSegment.Menu);
        SetSegmentClass(_bodyHost, "segment-pressed", _pressedSegment == ButtonSegment.Body);
        SetSegmentClass(_menuHost, "segment-pressed", _pressedSegment == ButtonSegment.Menu);
        SetSegmentClass(_menuHost, "segment-open", _menuOpen);
    }

    private static void SetSegmentClass(Border? host, string name, bool on)
    {
        if (host is null)
        {
            return;
        }

        if (on)
        {
            if (!host.Classes.Contains(name))
            {
                host.Classes.Add(name);
            }
        }
        else
        {
            _ = host.Classes.Remove(name);
        }
    }

    /// <summary>
    /// 按值的当前声明重建菜单：选项 = 声明的 Choices，打勾 = 当前值。
    /// </summary>
    private void OpenMenu()
    {
        if (_context is null || _menuValue.Length == 0)
        {
            return;
        }

        if (_openFlyout is { IsOpen: true } alreadyOpen)
        {
            alreadyOpen.Hide();
            return;
        }

        var valueName = _menuValue;
        var state = _context.ReadValue(valueName);
        if (state.Choices is not { Count: > 0 } choices)
        {
            return;
        }

        var flyout = new MenuFlyout
        {
            Placement = PlacementMode.BottomEdgeAlignedRight,
        };

        foreach (var choice in choices)
        {
            var key = choice.Value;
            var item = new MenuItem
            {
                Header = choice.Title,
                ToggleType = MenuItemToggleType.Radio,
                GroupName = "module-button-menu",
                IsChecked = string.Equals(key, state.Value, StringComparison.Ordinal),
            };

            item.Click += (_, _) => _context?.CommitValue(valueName, key);
            flyout.Items.Add(item);
        }

        flyout.Closed += (_, _) =>
        {
            if (!ReferenceEquals(_openFlyout, flyout))
            {
                return;
            }

            _openFlyout = null;
            _menuOpen = false;
            UpdateSegmentVisuals();

            if (_pointerInside)
            {
                UpdatePointer(_lastPointerPoint);
            }
        };

        _openFlyout = flyout;
        _menuOpen = true;
        UpdateSegmentVisuals();
        flyout.ShowAt(this);
    }

    /// <summary>关闭值菜单并把箭头段恢复为未打开态。</summary>
    private void CloseMenu()
    {
        var flyout = _openFlyout;
        _openFlyout = null;
        _menuOpen = false;

        if (flyout is { IsOpen: true })
        {
            flyout.Hide();
        }

        UpdateSegmentVisuals();
    }
}
