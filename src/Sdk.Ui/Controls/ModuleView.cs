using System.Collections.Concurrent;
using System.Reflection;
using Avalonia;
using Avalonia.Controls;
using Avalonia.LogicalTree;
using Avalonia.Markup.Xaml.Styling;
using Avalonia.Media;
using Avalonia.Styling;
using HYWDebugAssistant.Sdk.Ports;
using HYWDebugAssistant.Sdk.Ui.Geometry;

namespace HYWDebugAssistant.Sdk.Ui.Controls;

/// <summary>
/// **模块 XAML 视图的基类**：只负责把控件库的主题带进 XAML 预览器。
/// </summary>
/// <remarks>
/// <para>
/// 运行时，宿主应用已经把 `Sdk.Ui/Themes/Controls.axaml` 挂进 `Application.Styles`；
/// 预览器只加载单个模块视图，没有 App 资源，于是自定义控件**没有模板**、看起来像"预览器坏了"。
/// 设计时补上控件主题与一份设计时 token；宿主若没挂主题，运行期只补**同一份**控件主题，
/// 不加载设计时 token（运行时色源仍是 App 的色板）。
/// </para>
/// <para>
/// 设计时 token 只用于预览，不是第二套主题；运行时色源仍是 `Ui/Styles/Tokens.axaml`。
/// </para>
/// </remarks>
public class ModuleView : UserControl
{
    /// <summary>
    /// **实例化默认窗口宽**（DIP，**含外框**；0 = 未声明，回退到 <see cref="Avalonia.Layout.Layoutable.MinWidth"/>）。
    /// </summary>
    /// <remarks>
    /// 它只用于“新建模块窗口/还原尺寸”的初始值，不参与运行期最小尺寸约束。
    /// 唯一的运行期声明就是本属性；预览器所需的 Avalonia `Design.Width` 由本 setter 镜像，
    /// 因此模块 XAML 不再重复写 `d:DesignWidth`。
    /// </remarks>
    public double InitialWidth
    {
        get => _initialWidth;
        set
        {
            _initialWidth = value;
            Design.SetWidth(this, value);
        }
    }

    /// <summary>
    /// **实例化默认窗口高**（DIP，**含外框**；0 = 未声明，回退到 <see cref="Avalonia.Layout.Layoutable.MinHeight"/>）。
    /// </summary>
    /// <remarks>预览器所需的 `Design.Height` 由本 setter 镜像，避免第二个 XAML 尺寸事实。</remarks>
    public double InitialHeight
    {
        get => _initialHeight;
        set
        {
            _initialHeight = value;
            Design.SetHeight(this, value);
        }
    }

    /// <summary>
    /// XAML 声明的**窗口最小宽**（DIP，含外框）；运行期根控件的布局 `MinWidth` 是内容下限（声明值 − chrome）。
    /// </summary>
    /// <remarks>
    /// 声明值必须保留：窗口模型与预览器要的是含外框尺寸；而模块视图本身位于 20/22/20/24 预留带内，
    /// 若把外框下限直接留在根控件上，内容区会被撑破。这里只做同一份声明的派生，不产生第二尺寸源。
    /// </remarks>
    public double DeclaredMinWidth { get; private set; }

    /// <summary>XAML 声明的**窗口最小高**（DIP，含外框）；布局 `MinHeight` 是内容下限。</summary>
    public double DeclaredMinHeight { get; private set; }

    private bool _normalizingMinimum;

    /// <inheritdoc />
    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);

        if (change.Property == MinWidthProperty)
        {
            ApplyDeclaredMinimum(change.GetNewValue<double>(), horizontal: true);
        }
        else if (change.Property == MinHeightProperty)
        {
            ApplyDeclaredMinimum(change.GetNewValue<double>(), horizontal: false);
        }
    }

    private void ApplyDeclaredMinimum(double declared, bool horizontal)
    {
        if (_normalizingMinimum || !double.IsFinite(declared) || declared <= 0)
        {
            return;
        }

        if (horizontal)
        {
            DeclaredMinWidth = declared;
            if (IsDesignEnvironment)
            {
                return;
            }

            _normalizingMinimum = true;
            SetCurrentValue(MinWidthProperty, Math.Max(0, declared - ModuleSurfaceMetrics.HorizontalChrome));
            _normalizingMinimum = false;
        }
        else
        {
            DeclaredMinHeight = declared;
            if (IsDesignEnvironment)
            {
                return;
            }

            _normalizingMinimum = true;
            SetCurrentValue(MinHeightProperty, Math.Max(0, declared - ModuleSurfaceMetrics.VerticalChrome));
            _normalizingMinimum = false;
        }
    }

    private PreviewBoundsOverlay? _previewOverlay;
    private Panel? _previewInner;
    private double _initialWidth;
    private double _initialHeight;

    /// <summary>
    /// **端口声明**（视图 XAML 的 `ModuleView.Ports`）：名称/方向/类型/位置/可拖动性。
    /// </summary>
    /// <remarks>
    /// 运行时由框架窗口按它画标记；预览外壳按它画占位标记；包加载器从同一份 XAML 源解析
    /// （字段名必须与 <see cref="ModulePort"/> 一致）。
    /// </remarks>
    public List<ModulePort> Ports { get; } = [];

    /// <summary>端口声明的加载链规格（空 = 没有 XAML 端口声明）。</summary>
    public IReadOnlyList<PortDeclarationSpec> PortSpecs => [.. Ports.Select(port => port.ToSpec())];

    /// <summary>
    /// 当前进程是否是 **XAML 预览宿主**（`tools/PreviewHost`）。
    /// </summary>
    /// <remarks>
    /// 预览器通常会把 <see cref="Design.IsDesignMode"/> 置真；但部分宿主/会话不保证。
    /// 预览宿主的 Application 显式置位本开关，于是设计主题与配置上下文只由"谁在预览"决定，
    /// 不再依赖某个框架布尔值是否被设置。产品 App 不置位。
    /// </remarks>
    public static bool IsPreviewHost { get; set; }

    private static readonly ConcurrentDictionary<string, Type> StandardConfigTypes = new(StringComparer.Ordinal);

    /// <summary>
    /// 注册**外部标准标识 → 设计时配置类型**（预览宿主/框架接线在启动时调用）。
    /// </summary>
    /// <remarks>
    /// 外部模块的参数类型在框架侧，模块只声明标准标识；预览器没有运行时上下文，
    /// 只能用这张表把标准标识映射回配置类型以画出字段。
    /// </remarks>
    /// <param name="standardId">外部标准标识（见 <c>ExternalAccessStandardIds</c>）。</param>
    /// <param name="configType">带配置标注的配置类型。</param>
    public static void RegisterStandardConfig(string standardId, Type configType)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(standardId);
        ArgumentNullException.ThrowIfNull(configType);
        StandardConfigTypes[standardId] = configType;
    }

    /// <summary>按外部标准标识取设计时配置类型。</summary>
    /// <param name="standardId">外部标准标识。</param>
    /// <param name="configType">映射到的配置类型（未注册时为空）。</param>
    public static bool TryGetStandardConfig(string standardId, out Type configType)
    {
        if (!string.IsNullOrWhiteSpace(standardId)
            && StandardConfigTypes.TryGetValue(standardId, out var found))
        {
            configType = found;
            return true;
        }

        configType = null!;
        return false;
    }

    /// <summary>当前进程是否处于**设计/预览环境**（预览范围框与预览值据此显示）。</summary>
    /// <remarks>
    /// 模块视图与控件都只认这一个判定；不要在别处再写 `Design.IsDesignMode || IsPreviewHost`，
    /// 否则预览行为会随入口分叉。
    /// </remarks>
    public static bool IsDesignEnvironment => Design.IsDesignMode || IsPreviewHost;

    private static readonly Uri ControlsThemeUri =
        new("avares://HYWDebugAssistant.Sdk.Ui/Themes/Controls.axaml");

    private static readonly Uri DesignTokensUri =
        new("avares://HYWDebugAssistant.Sdk.Ui/Themes/DesignTokens.axaml");

    /// <summary>构造视图基类。</summary>
    public ModuleView()
    {
        // 模块视图是**填满内容区**的：`UserControl` 的默认内容对齐是左上，
        // 根 Grid 里带 `*` 的行列会因此只拿到"想要多宽就多宽"，右侧留白、最右读数被裁。
        HorizontalContentAlignment = Avalonia.Layout.HorizontalAlignment.Stretch;
        VerticalContentAlignment = Avalonia.Layout.VerticalAlignment.Stretch;

        var hasTheme = HasControlsTheme();

        // 预览器通常没有运行 App，自定义控件就没有模板；在**缺少主题**时补上。
        // 运行时 App 已挂过同一份主题，不会重复挂（`HasControlsTheme` 为 true）。
        if (!hasTheme)
        {
            Styles.Add(new StyleInclude(new Uri("avares://HYWDebugAssistant.Sdk.Ui/"))
            {
                Source = ControlsThemeUri,
            });
        }

        // 设计时没有 App 色板；只补一份预览用的 token。运行时绝不加载这里。
        if (IsDesignEnvironment)
        {
            Resources.MergedDictionaries.Add(new ResourceInclude(new Uri("avares://HYWDebugAssistant.Sdk.Ui/"))
            {
                Source = DesignTokensUri,
            });

            // 配置字段在运行时靠宿主上下文才有标题/编辑器；预览器没有宿主，
            // 由视图上的 `[ModuleViewDesignData]` 指定配置类型（或标准标识 → 配置类型的注册表），
            // 从同一份标注反射出字段。
            if (GetType().GetCustomAttribute<ModuleViewDesignDataAttribute>() is { } designData
                && DataContext is null)
            {
                var configType = designData.ConfigType
                    ?? (designData.StandardId is { } standardId && TryGetStandardConfig(standardId, out var mapped)
                        ? mapped
                        : null);

                if (configType is not null)
                {
                    DataContext = new DesignModuleViewContext(configType, GetType().Assembly);
                }
            }

            // 预览范围框只在设计环境附加；运行期连遍历都不发生。
            AttachedToVisualTree += (_, _) => AttachPreviewOverlay();
            DetachedFromVisualTree += (_, _) => DetachPreviewOverlay();
        }
    }

    /// <summary>
    /// 在预览环境把视图内容包进一层 Panel，并在其上叠一个只画范围框的覆盖层。
    /// </summary>
    /// <remarks>
    /// 不依赖窗口的 AdornerLayer：不同预览宿主是否提供该层并不稳定；这里把覆盖层放在
    /// 视图自身的可视树里，位置与截图都只由模块视图决定。
    /// </remarks>
    private void AttachPreviewOverlay()
    {
        if (_previewOverlay is not null || !IsDesignEnvironment)
        {
            return;
        }

        if (Content is not Control content)
        {
            return;
        }

        var targets = new List<PreviewBoundsTarget>();
        AddPreviewTarget(content, targets);

        foreach (var descendant in content.GetLogicalDescendants().OfType<Control>())
        {
            AddPreviewTarget(descendant, targets);
        }

        if (targets.Count == 0)
        {
            return;
        }

        var panel = new Panel
        {
            HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Stretch,
            VerticalAlignment = Avalonia.Layout.VerticalAlignment.Stretch,
        };

        Content = null;
        panel.Children.Add(content);

        _previewOverlay = new PreviewBoundsOverlay(targets);
        panel.Children.Add(_previewOverlay);
        _previewInner = panel;

        // 预览外壳：外框 + 预留带 + 端口占位。运行时完全不创建。
        Content = ModulePreviewChrome.Create(panel, Ports, InitialWidth, InitialHeight);
    }

    /// <summary>把一个控件加入预览框目标（控件框或容器框）。</summary>
    /// <param name="control">候选控件。</param>
    /// <param name="targets">目标表。</param>
    /// <remarks>
    /// **两种角色**：控件（标签/读数/勾选框/自绘控件）给"虚线的控件框"，其边界就是绘制范围；
    /// 不可见的布局容器（Panel/Grid/Border）给"点线的容器框"，那是槽位而不是控件边界 ——
    /// 两者线型区分，既保留布局调试，又不会把一整片空白槽位误读成控件边界。
    /// 需要"可见的槽位边框"时，在视图里显式放一个 `PreviewOnly` 边框（见 WaveformDisplay 的 ChartLayoutFrame），
    /// 那是可见元素、与这里的虚线覆盖层职责不同。
    /// </remarks>
    private static void AddPreviewTarget(Control control, List<PreviewBoundsTarget> targets)
    {
        if (!PreviewBounds.GetShow(control) || control is PreviewBoundsOverlay)
        {
            return;
        }

        // 勾选框的小方框只表达勾选态，不表达它的布局/命中矩形：
        // 预览框必须覆盖“方框 + 文字”整个控件，而不是只框住那个小方框。
        if (control is ModuleLabel or ModuleNumeric or ModuleCheckBox)
        {
            targets.Add(new PreviewBoundsTarget(control, PreviewBoundsRole.Control));
            return;
        }

        // 自绘控件（仪表盘 / 刻度条 / 姿态）自己画内容、没有可见边框：
        // 由控件声明"边界即绘制范围"（`IModulePreviewBoundsTarget`），这里不写具体类型。
        if (control is IModulePreviewBoundsTarget)
        {
            targets.Add(new PreviewBoundsTarget(control, PreviewBoundsRole.Control));
            return;
        }

        // 自带边框的输入控件已经有可见轮廓，不重复叠加。
        if (control is ModuleElement)
        {
            return;
        }

        if (control is Panel panel && IsInvisible(panel.Background))
        {
            targets.Add(new PreviewBoundsTarget(control, PreviewBoundsRole.Container));
            return;
        }

        if (control is Border border
            && border.BorderThickness == default
            && IsInvisible(border.Background))
        {
            targets.Add(new PreviewBoundsTarget(control, PreviewBoundsRole.Container));
        }
    }

    /// <summary>背景是否"看不见"（null 或全透明）——看不见的容器才有必要补框。</summary>
    private static bool IsInvisible(IBrush? brush) =>
        brush is null || brush is ISolidColorBrush { Color.A: 0 };

    /// <summary>移除预览覆盖层并把内容还原为原来的根。</summary>
    private void DetachPreviewOverlay()
    {
        if (_previewOverlay is null)
        {
            return;
        }

        if (_previewInner is { } panel)
        {
            panel.Children.Remove(_previewOverlay);

            if (panel.Children.Count == 1 && panel.Children[0] is Control original)
            {
                panel.Children.Remove(original);
                Content = original;
            }
        }

        _previewOverlay = null;
        _previewInner = null;
    }

    /// <summary>当前应用是否已经挂了控件库主题（避免模块视图在运行时重复挂一套样式）。</summary>
    private static bool HasControlsTheme()
    {
        if (Application.Current is not { } app)
        {
            return false;
        }

        return Contains(app.Styles);

        static bool Contains(IEnumerable<IStyle> styles)
        {
            foreach (var style in styles)
            {
                if (style is StyleInclude include && include.Source == ControlsThemeUri)
                {
                    return true;
                }

                if (style is Styles nested && Contains(nested))
                {
                    return true;
                }
            }

            return false;
        }
    }
}
