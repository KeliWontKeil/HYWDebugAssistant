using System.Globalization;
using System.Linq;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;
using HYWDebugAssistant.Core.Api;
using HYWDebugAssistant.Sdk.Views;
using HYWDebugAssistant.Ui.ModuleSurface.ControlCatalog;
using HYWDebugAssistant.Ui.ModuleSurface.Controls;
using HYWDebugAssistant.Ui.ModuleSurface.Layout;
using HYWDebugAssistant.Ui.ModuleSurface.Theme;

namespace HYWDebugAssistant.Ui.ModuleSurface.Hosting;

/// <summary>
/// **一个模块窗口的内容表面** —— 声明树在这里变成控件树，数据在这里被取。
/// </summary>
/// <remarks>
/// <para>
/// 它是 <see cref="ISurfaceHost"/> 的唯一实现，也是<b>唯一知道"哪种 kind 对应哪个控件"</b>的地方
/// （见 <see cref="Create"/>）。控件、排布面板、容器全都只认
/// <see cref="ViewNode"/> / <see cref="ISurfaceLayoutItem"/> / <see cref="SurfaceMetrics"/>。
/// 于是"加一种控件"只动 <see cref="Create"/> 一处。
/// </para>
/// <para>
/// <b>视觉按声明节点缓存</b>（<see cref="Visual"/>）：同一个排布项无论被问多少次，拿到的都是同一个
/// 控件实例。切页、折叠都只是"重新排布同一批控件"，不重建控件树 ——
/// 焦点、滚动位置、未提交文本因此都不丢。**切页若改成重建控件树，这三样会被静默重置**
/// （用户看到的是"一切页光标就跳回开头、滚动位置也回去了"）。
/// </para>
/// <para>
/// <b>它只消费声明树 <see cref="ViewNode"/></b>：内容区不引入"元素视图模型"这一层中间物，
/// 于是"声明面多一个键"与"控件多认一个键"之间没有第三方要同步。
/// </para>
/// </remarks>
public sealed class ModuleSurfaceHost : ISurfaceHost
{
    private readonly IRuntimeQuery _query;
    private readonly IRuntimeCommand _command;
    private readonly IViewStateAccess? _viewState;
    private readonly Func<string>? _multilineSubmitKey;
    private readonly SurfaceSourceReader _reader;

    // 排布项 → 控件（按需创建、此后复用）· 控件 → 排布项（每帧刷数据时反查声明）
    private readonly Dictionary<ISurfaceLayoutItem, Control> _controls = new(ReferenceEqualityComparer.Instance);
    private readonly Dictionary<Control, ISurfaceLayoutItem> _owners = new(ReferenceEqualityComparer.Instance);
    private readonly Dictionary<ISurfaceLayoutItem, ISurfaceLayoutItem?> _parents = new(ReferenceEqualityComparer.Instance);

    /// <summary>
    /// 目录里记着 <c>Rendered</c>、但**实现还没接上它要的数据**的 kind ⇒ 走占位控件。
    /// </summary>
    /// <remarks>
    /// 这一份是"欠账清单"，不是"设计选择"：每清一笔就删一行。它必须在这里（渲染侧）而不是控件目录里 ——
    /// 目录描述的是<b>词汇表</b>("这一种控件是什么")，不是"本机实现到了哪一步"。
    /// </remarks>
    private static readonly HashSet<string> Deferred = new(StringComparer.Ordinal)
    {
        // 渲染它需要"配置表单"这份数据（服务面今天没有取得配置项声明的查询）。
        // 把那份数据接到新表面之前明说尚未渲染 —— 画一个空表单会让人以为"这个模块的配置项就是空的"。
        "configurationEditor",
    };

    /// <summary>造一个模块内容表面。</summary>
    /// <param name="instanceId">模块实例 ID（取数与视图状态都按它走）。</param>
    /// <param name="root">模块内容的声明根节点；<see langword="null"/> = 这个实例没有界面声明。</param>
    /// <param name="query">运行期查询面。</param>
    /// <param name="command">运行期命令面。</param>
    /// <param name="viewState">
    /// 视图状态存取面；<see langword="null"/> = 设计期 / 未接线的调用点 ⇒ 折叠态、选中页、未提交文本
    /// 都回落成"只记在本地、重建即忘"（与旧链在 <c>viewState</c> 为空时的口径一致）。
    /// </param>
    /// <param name="multilineSubmitKey">
    /// 多行文本框的提交键档位提供者（框架设置里那一项）；<see langword="null"/> = 用缺省档。
    /// 传的是<b>取值函数</b>而不是取到的字符串：设置随时会变，而控件每次按键都要拿当前值。
    /// </param>
    public ModuleSurfaceHost(
        string instanceId,
        ViewNode? root,
        IRuntimeQuery query,
        IRuntimeCommand command,
        IViewStateAccess? viewState,
        Func<string>? multilineSubmitKey = null)
    {
        ArgumentException.ThrowIfNullOrEmpty(instanceId);
        ArgumentNullException.ThrowIfNull(query);
        ArgumentNullException.ThrowIfNull(command);

        InstanceId = instanceId;
        _query = query;
        _command = command;
        _viewState = viewState;
        _multilineSubmitKey = multilineSubmitKey;
        _reader = new SurfaceSourceReader(query, viewState);

        Panel = new ModuleSurfacePanel();

        if (root is not null)
        {
            Root = Build(root, "v");
        }

        // 声明了根节点就立刻装配一次：控件树随宿主构造而存在，不依赖"哪一帧才开始渲染"。
        Panel.Populate(root, TopLevel, Need);

        var view = new ScrollViewer
        {
            // **横向不滚**：宽度这一轴不该出现滚动条 —— 内容比视口宽只说明"有个控件报错了宽度"
            // （实测过：会折行的文本框在无约束下报整行文本的宽 1946，把模块右边界挤到屏幕外）。
            // 正确做法是让文字在框里折、让声明的两轴约束生效，而不是让用户横向滚去找内容。
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,

            // 内容外面套一层"知道视口高"的壳：`ScrollViewer` 在可纵向滚动时**按无限高测量内容**，
            // 而面板的排布不会把子控件拉到超过它量到的高度 —— 于是面板的 `*` 行与填满型都拿不到余量，
            // 内容比视口矮时还会被 `ScrollContentPresenter` 居中。理由与实测值写在 `SurfaceViewport`。
            Content = new SurfaceViewport { Child = Panel },
        };

        // 预留带**必须在首次布局之前**写完：`Loaded` 是**首次布局之后**的事件，写在那边会让
        // "首帧没有留白、第二帧才有" ⇒ 一切由实得尺寸推出的几何都会在第二帧跳一下
        // （实测：滑块圆钮"实例化后 0.x 秒动 1px"）。挂树早于首次布局，且此时主题资源已可解析。
        // 预留带只有一个写入点，落在 `ScrollViewer` 之外的 `Margin` 上。
        view.AttachedToVisualTree += (_, _) => view.Margin = SurfaceTheme.ContentInset(view);

        View = view;
    }

    /// <inheritdoc />
    public string MultilineSubmitKey => _multilineSubmitKey?.Invoke() ?? "CtrlEnter";

    /// <summary>模块实例 ID。</summary>
    public string InstanceId { get; }

    /// <summary>
    /// 排布树的根（<see langword="null"/> = 这个实例没有界面声明）。
    /// </summary>
    /// <remarks>
    /// 整棵排布树在构造时一次建好，此后**结构不变**：声明是加载时的一次性声明（模块不支持在线改排布），
    /// 而展开态、选中页、未提交文本这些都不在结构里。于是"每帧刷新"只动数据、不动树。
    /// </remarks>
    internal SurfaceItem? Root { get; }

    /// <summary>
    /// 本表面的顶层排布面板。
    /// </summary>
    /// <remarks>
    /// 控件树长在它里面（<see cref="View"/> 是它的外层）；测试与诊断按类型找它。
    /// </remarks>
    public ModuleSurfacePanel Panel { get; }

    /// <summary>
    /// 交给窗口内容区的那一个控件（面板 + 预留带 + 滚动）。
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>预留带是 <see cref="ScrollViewer"/> 之外的 <c>Margin</c>，不是 <c>Padding</c></b>：
    /// `Padding` 算在视口之内 ⇒ 内容恒大于视口 ⇒ 任何模块在默认尺寸下都常驻一条滚动条
    /// （**"总是有一条滚动条"正是这个错法的特征**，而它看起来只像"内容稍微大了一点"）。
    /// </para>
    /// <para>
    /// <c>VerticalAlignment="Stretch"</c>：<see cref="ScrollViewer"/> 会把"比视口矮的内容"拉伸到视口高
    /// ⇒ 面板拿到剩余高度 ⇒ **填满型**（多行输入 / 显示区）才会随窗口变高。
    /// 内容更高时面板保持最小高、滚动条接管。
    /// </para>
    /// </remarks>
    public Control View { get; }

    /// <summary>顶层排布项（根节点的子节点；无声明 ⇒ 空表）。</summary>
    public IReadOnlyList<ISurfaceLayoutItem> TopLevel => Root?.Items ?? [];

    /// <summary>已经创建出来的控件数（"哪些 kind 真的被造出来了"运行期可数）。</summary>
    public int VisualCount => _controls.Count;

    /// <inheritdoc />
    public Control Visual(ISurfaceLayoutItem item)
    {
        ArgumentNullException.ThrowIfNull(item);

        if (_controls.TryGetValue(item, out var existing))
        {
            return existing;
        }

        var control = Create(item);
        _controls[item] = control;
        _owners[control] = item;

        return control;
    }

    /// <inheritdoc />
    public SurfaceMetrics Metrics(ISurfaceLayoutItem item)
    {
        ArgumentNullException.ThrowIfNull(item);
        return item.Metrics;
    }

    /// <inheritdoc />
    public double? ReadLatest(string sourceName)
    {
        _reader.RefreshSources();
        return _reader.ReadLatest(InstanceId, sourceName);
    }

    /// <inheritdoc />
    public IReadOnlyList<double> ReadSeries(string sourceName)
    {
        _reader.RefreshSources();
        return _reader.ReadSeries(InstanceId, sourceName);
    }

    /// <inheritdoc />
    public SurfaceBufferView ReadBuffer(string sourceName) => _reader.ReadBuffer(InstanceId, sourceName);

    /// <inheritdoc />
    public void SubmitInput(string? input, string text)
    {
        if (input is not { Length: > 0 } source)
        {
            return;
        }

        // 提交只投递、不等结果：`SubmitInput` 的回执**只反映"是否投递入队"**，不反映模块处理结果
        // （模块的处理结论经 `feedbackSource` 回来）。
        _ = _command.SubmitInput(InstanceId, source, text);

        // 提交过的文本不再是"未提交的"，把它从恢复位里清掉。
        _viewState?.SetInputText(InstanceId, source, null);
    }

    /// <inheritdoc />
    public SurfaceCommandOutcome InvokeCommand(string? command)
    {
        if (command is not { Length: > 0 } name)
        {
            return new SurfaceCommandOutcome(true, "这个控件没有声明命令名");
        }

        var result = _command.SendCommand(InstanceId, name);

        return result.Succeeded
            ? new SurfaceCommandOutcome(false, Join(result.Notices))
            : new SurfaceCommandOutcome(true, Join(result.Failures));
    }

    /// <inheritdoc />
    public string? Recall(string? stateKey, string? input, SurfaceStateSlot slot)
    {
        return slot switch
        {
            SurfaceStateSlot.Collapsed => stateKey is { Length: > 0 } collapsedKey
                ? _viewState?.GetCollapsed(InstanceId, collapsedKey) is { } collapsed
                    ? collapsed ? "true" : "false"
                    : null
                : null,

            SurfaceStateSlot.SelectedPage => stateKey is { Length: > 0 } pageKey
                ? _viewState?.GetSelectedPage(InstanceId, pageKey)?.ToString(CultureInfo.InvariantCulture)
                : null,

            // 未提交文本按**输入源名**记（键路与"折叠/选中页"不同：文本状态属于那个输入源，不属于容器）。
            _ => input is { Length: > 0 } source ? _viewState?.GetInputText(InstanceId, source) : null,
        };
    }

    /// <inheritdoc />
    public void Remember(string? stateKey, string? input, SurfaceStateSlot slot, string? value)
    {
        switch (slot)
        {
            case SurfaceStateSlot.Collapsed:
                if (stateKey is { Length: > 0 } collapsedKey)
                {
                    _viewState?.SetCollapsed(InstanceId, collapsedKey, string.Equals(value, "true", StringComparison.Ordinal));
                }

                break;

            case SurfaceStateSlot.SelectedPage:
                if (stateKey is { Length: > 0 } pageKey
                    && int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var page)
                    && page >= 0)
                {
                    _viewState?.SetSelectedPage(InstanceId, pageKey, page);
                }

                break;

            default:
                if (input is { Length: > 0 } source)
                {
                    _viewState?.SetInputText(InstanceId, source, value);
                }

                break;
        }
    }

    /// <summary>
    /// 记下"某个显示源刚被清空"（世代号递增 ⇒ 界面手里的副本作废）。
    /// </summary>
    /// <param name="sourceName">显示源名。</param>
    /// <remarks>
    /// 由**落地清空请求的那一方**调用（<c>ModuleWindowsViewModel</c> 在刷新节拍里
    /// <c>Display(...).Clear()</c> 之后）—— 清空的落地仍然只有一处，这里只维护"版本"。
    /// </remarks>
    public void ForgetBuffer(string sourceName) => _reader.ForgetBuffer(InstanceId, sourceName);

    /// <summary>
    /// 把这一帧的数据推进到各控件（每帧一次；控件可见性也在这里同步）。
    /// </summary>
    /// <remarks>
    /// 只做"取数 + 交给控件"，不重建任何东西：控件个数与声明结构在一帧内不变。
    /// 控件自己决定这次数据要不要改自己的视觉（行数没变、末端行没变，就不动）。
    /// </remarks>
    public void Refresh()
    {
        if (_controls.Count == 0)
        {
            return;
        }

        var visuals = _controls.Values.ToArray();

        foreach (var visual in visuals)
        {
            // 可见性 = **从自己往上所有折叠项都展开**：折叠一个容器，它整棵子树都不显示
            // （只隐藏容器自己是不够的 —— 内层的行仍会被布局引擎算成可见）。
            visual.IsVisible = _owners.TryGetValue(visual, out var item) && IsReachable(item);

            if (visual is SurfaceElementControl element)
            {
                element.Refresh();
            }
        }

        ApplyAppearance(visuals);
    }

    /// <summary>
    /// 把模块设定的**外观（颜色）**发给对应的控件。
    /// </summary>
    /// <param name="visuals">这一帧的控件快照。</param>
    /// <remarks>
    /// <para>
    /// 分派按<b>数据源名</b>（模块唯一知道的键）：模块说"这条源现在是什么颜色"，框架只做"查表 → 转交"。
    /// **框架不解释颜色**（不认识"红 = 失败"）—— 那正是"把功能限定死"的反面。
    /// </para>
    /// <para>
    /// 一次查询服务、多次转交：<c>GetAppearance</c> 每模块每帧只该问一次。
    /// </para>
    /// </remarks>
    private void ApplyAppearance(Control[] visuals)
    {
        if (visuals.Length == 0)
        {
            return;
        }

        var appearance = _query.GetAppearance(InstanceId);

        foreach (var visual in visuals)
        {
            if (visual is not SurfaceElementControl element)
            {
                continue;
            }

            var source = SourceOf(element);

            if (source.Length == 0)
            {
                continue;
            }

            var hit = Find(appearance, source);

            // **查不到就说"没设"**（不保留上一帧的颜色）：服务只报当前的设定，
            // 旧颜色留在这儿会变成"明明已经正常了，却还是红的"。
            element.State.OutlineColor = hit?.Outline;
            element.State.OutlineRole = hit?.OutlineRole;
            element.State.TextColor = hit?.Text;
            element.State.TextRole = hit?.TextRole;
            element.State.TextSize = hit?.TextSize;
            element.State.ReadOnly = hit?.ReadOnly;

            // **动作复原**：模块每 `ResetInteraction` 一次，号就往前走一格；
            // 控件记住上次见到的号，变了就把"用户刚做的那次交互"退回去（勾选框拨回）。
            var revision = hit?.Revision ?? 0;

            if (revision != element.SeenInteractionRevision)
            {
                element.ApplyInteractionReset();
                element.SeenInteractionRevision = revision;
            }
        }
    }

    /// <summary>某个控件绑的是哪条源（数据面 / 输入槽 / 命令）。</summary>
    /// <param name="element">元素控件。</param>
    /// <returns>源名；没绑 ⇒ 空串。</returns>
    private static string SourceOf(SurfaceElementControl element) =>
        element.Node.DataSource
        ?? element.Node.Input
        ?? element.Node.Command
        ?? string.Empty;

    /// <summary>在一帧的外观表里按源名找一条。</summary>
    /// <param name="appearance">外观表。</param>
    /// <param name="source">源名。</param>
    /// <returns>命中项（没有 ⇒ <see langword="null"/>）。</returns>
    private static AppearanceView? Find(IReadOnlyList<AppearanceView> appearance, string source)
    {
        foreach (var candidate in appearance)
        {
            if (string.Equals(candidate.SourceName, source, StringComparison.Ordinal))
            {
                return candidate;
            }
        }

        return null;
    }

    /// <summary>从某一项往上，所有折叠项都展开吗。</summary>
    /// <param name="item">排布项。</param>
    /// <returns>可见为 <see langword="true"/>。</returns>
    private bool IsReachable(ISurfaceLayoutItem item)
    {
        for (var current = item; current is not null; current = _parents.TryGetValue(current, out var parent) ? parent : null)
        {
            if (!current.IsExpanded)
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>
    /// 声明节点 → 排布项子树（含<b>每一层</b>的子项，页节点也在内）。
    /// </summary>
    /// <param name="node">本层声明节点。</param>
    /// <param name="path">诊断用的声明路径。</param>
    /// <returns>本层的排布项。</returns>
    private SurfaceItem Build(ViewNode node, string path, ISurfaceLayoutItem? parent = null)
    {
        var item = new SurfaceItem(node);
        _parents[item] = parent;

        for (var i = 0; i < node.Children.Count; i++)
        {
            var child = node.Children[i];

            // 标签页：**页节点自己不进排布**（它是"页内容"的容器，不画框、不占位），
            // 页只提供两样东西 —— 页签文字与页内容。这是旧实现已经确立的语义，原样保留。
            item.Add(node.Kind == ViewElementKindExtensions.TabPagesName
                ? BuildPage(child, $"{path}.p{i}", item)
                : Build(child, $"{path}.{i}", item));
        }

        return item;
    }

    /// <summary>造一个"页"排布项（页节点自身不可视，它的子项才是页内容）。</summary>
    /// <param name="page">页节点。</param>
    /// <param name="path">诊断用的声明路径。</param>
    /// <param name="parent">父项（可见性判定要往上走）。</param>
    /// <returns>页排布项（不产生控件）。</returns>
    private SurfaceItem BuildPage(ViewNode page, string path, ISurfaceLayoutItem parent)
    {
        var item = new SurfaceItem(page);
        _parents[item] = parent;

        for (var i = 0; i < page.Children.Count; i++)
        {
            item.Add(Build(page.Children[i], $"{path}.{i}", item));
        }

        return item;
    }

    /// <summary>
    /// **kind ⇒ 控件**：全框架唯一的一处"哪种声明对应哪个控件"。
    /// </summary>
    /// <param name="item">排布项（含声明节点）。</param>
    /// <returns>该项的控件。</returns>
    /// <remarks>
    /// <para>
    /// 用<b>显式 switch</b> 而不是"按特性/按模板查找"：加一种控件就该在这里多一行。
    /// </para>
    /// <para>
    /// <b>"这一种本轮渲不渲染"由控件目录说了算</b>（<see cref="ControlDescriptor.IsRendered"/>）：
    /// 目录里记着 <see cref="ControlRenderState.DeclarationOnly"/> 的 kind **一律**落到
    /// <see cref="SurfaceDeclarationOnlyControl"/>，不因为这里恰好有实现就画出来 ——
    /// 声明面 <c>rendered: false</c> 与这里必须永远同口径，否则"声明说没有、界面却有"。
    /// </para>
    /// <para>
    /// 目录里记着 <c>Rendered</c>、但实现还没接上数据的 kind 记在 <see cref="Deferred"/> ——
    /// **明说"尚未渲染"**，而不是画一个看起来能用其实什么都不做的控件。
    /// </para>
    /// </remarks>
    private Control Create(ISurfaceLayoutItem item)
    {
        var node = item.Node;
        var state = new SurfaceElementState();
        var children = item.Items;

        if (ControlCatalog.ControlCatalog.Find(node.Kind) is { IsRendered: false } ||
            Deferred.Contains(node.Kind))
        {
            return new SurfaceDeclarationOnlyControl(node, state);
        }

        return node.Kind switch
        {
            // ── 数据展示 ──
            "numeric" => new SurfaceNumericControl(node, state, this),
            "curve" => new SurfaceCurveControl(node, state, this),

            // ── 文本与说明 ──
            // 文本面**只有一种**：输入与输出的差别是绑定键与 `readonly`，不是两个 kind。
            "textBox" => new SurfaceTextBoxControl(node, state, this),
            "label" => new SurfaceLabelControl(node, state, this),

            // ── 输入与命令 ──
            "toggleSwitch" => new SurfaceToggleSwitchControl(node, state, this),
            "toggleSlider" => new SurfaceToggleSliderControl(node, state, this),
            "commandButton" => new SurfaceCommandButtonControl(node, state, this),

            // ── 容器（子项在容器内部自成一层排布作用域）──
            "group" => new SurfaceGroupControl(node, children, this),
            "container" => new SurfaceLayoutControl(node, children, this),
            "collapsible" => CreateCollapsible(node, children, item),
            ViewElementKindExtensions.TabPagesName => CreateTabs((SurfaceItem)item),

            // 目录里记着 `Rendered` 却还没落地的 kind（见 Deferred）与未来的 kind
            _ => new SurfaceDeclarationOnlyControl(node, state),
        };
    }

    /// <summary>造一个折叠区（展开态来自状态存储，声明面的 <c>expanded</c> 只是缺省）。</summary>
    /// <param name="node">声明节点。</param>
    /// <param name="children">子项。</param>
    /// <param name="item">这一层在排布树上的项（展开态要写回它）。</param>
    /// <returns>折叠区控件。</returns>
    private SurfaceCollapsibleControl CreateCollapsible(
        ViewNode node,
        IReadOnlyList<ISurfaceLayoutItem> children,
        ISurfaceLayoutItem item)
    {
        var expanded = Recall(node.StateKey, node.Input, SurfaceStateSlot.Collapsed) is { } raw
            ? !string.Equals(raw, "true", StringComparison.Ordinal)
            : node.Expanded;

        var control = new SurfaceCollapsibleControl(node, children, this, expanded);

        // **展开态要同时写回排布项与状态存储**：
        // ① 排布项 —— 折叠着不占高是排布引擎的判据（`SurfaceItem.SetExpanded`），漏了这一笔
        //    表现为"展开之后里面还是空的"（控件显示了，引擎仍按折叠算高度）；
        // ② 状态存储 —— 用户拨过就以用户为准，声明面的 `expanded` 只是缺省。
        control.ExpandedChanged += (_, value) =>
        {
            if (item is SurfaceItem collapsible)
            {
                collapsible.SetExpanded(value);
            }

            Remember(node.StateKey, node.Input, SurfaceStateSlot.Collapsed, value ? "false" : "true");
        };

        return control;
    }

    /// <summary>造一个标签页控件（选中页来自状态存储）。</summary>
    /// <param name="tabs">标签页排布项（它的孩子 = 各页）。</param>
    /// <returns>标签页控件。</returns>
    private SurfaceTabsControl CreateTabs(SurfaceItem tabs)
    {
        // 页内容 = 每一页的子项；页节点自身不渲染，所以"页"这一层在排布树里只是**载体**。
        // 但**页内容的排布键取自页节点**（页签条那一层的键只管"这一块怎么分给页签条 / 页内容"）——
        // 拿页签条的键去排页内容，会让第一页与其它页用两套规则（屏幕上只表现为"切页后间距变了"）。
        var pages = tabs.Items.Select(page => page.Items).ToArray();
        var pageNodes = tabs.Items.Select(page => page.Node).ToArray();

        // **页签文字优先取 `pageTitles`**，没写那个键才退回页节点自己的 title。
        // 退回那一档的代价是"页内会多画一条标题带"（页内容因此少一截高度），所以新声明都该走前者。
        var declaredTitles = tabs.Node.PageTitles;
        var titles = declaredTitles.Count == pages.Length
            ? [.. declaredTitles]
            : tabs.Items.Select(page => page.Node.Title).ToArray();

        var node = tabs.Node;

        var selected = Recall(node.StateKey, node.Input, SurfaceStateSlot.SelectedPage) is { } raw
            && int.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out var page)
                ? page
                : 0;

        var control = new SurfaceTabsControl(node, pageNodes, pages, titles, this, selected);
        control.SelectionChanged += (_, index) => Remember(node.StateKey, node.Input, SurfaceStateSlot.SelectedPage, index.ToString(CultureInfo.InvariantCulture));
        return control;
    }

    /// <summary>取一项的子控件（<see cref="ModuleSurfacePanel.Populate"/> 的回调）。</summary>
    /// <param name="item">排布项。</param>
    /// <returns>该项的控件。</returns>
    private Control Need(ISurfaceLayoutItem item) => Visual(item);

    /// <summary>把若干条提示拼成一句话（空表 ⇒ 空串 ⇒ 控件不显示那一行）。</summary>
    /// <param name="lines">提示行。</param>
    /// <returns>一句话。</returns>
    private static string Join(IReadOnlyList<string> lines) => lines.Count == 0
        ? string.Empty
        : string.Join("；", lines);
}
