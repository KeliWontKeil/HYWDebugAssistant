using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using HYWDebugAssistant.Core.Api;
using HYWDebugAssistant.Sdk.Views;
using HYWDebugAssistant.Ui.Geometry;

namespace HYWDebugAssistant.Ui.ViewModels;

/// <summary>
/// 模块视图视图模型：**一个模块 = 一个无边框顶层窗口**（ADR-0034 §2）——
/// 屏幕位置/尺寸/锁定 + 按声明渲染的元素 + 口上的标记块。
/// </summary>
/// <remarks>
/// <list type="bullet">
///   <item><b>视图 = 实例的派生物</b>（主文档 §10.1）：实例出现即出现、实例移除即移除，可独立隐藏（不影响运行）；</item>
///   <item><b>坐标单位 = 物理像素（屏幕坐标）</b>：窗口位置本身是 <c>PixelPoint</c>，用同一单位做吸附比较才自洽；
///     窗口的宽高属性（DIP）由窗口管理器按各自缩放换算（多显示器不同 DPI 见 `11` §2.1 风险点）；</item>
///   <item><b>没有"工作区"容器</b>（§10.1-3）：本类不持有画布、不做边界钳制——背景就是用户桌面；</item>
///   <item><b>锁定</b>（O-3）= 不可拖动 / 不可缩放 / 不可选中（仍显示数据）；锁定**不持久化**（属会话内交互状态）。</item>
/// </list>
/// </remarks>
public sealed partial class ModuleViewViewModel : ViewModelBase
{
    /// <summary>
    /// 新模块窗口的默认尺寸（DIP）——**与 `ModuleWindow.axaml` 的 `Width`/`Height` 保持一致**。
    /// </summary>
    /// <remarks>
    /// 【用户第九轮反馈 · UI 开发方式修正】**尺寸的默认值由 XAML 拥有**，这里只是"与 XAML 同步的常量"，
    /// 用于：① 未摆放模块的摆放阶梯计算；② 测试里造确定几何。
    /// **不再有任何"按内容估算尺寸"的逻辑**（内容排不下由内容区滚动条处理）。
    /// </remarks>
    public const double DefaultWidth = 320;

    /// <summary>新模块窗口的默认高（DIP；与 `ModuleWindow.axaml` 一致）。</summary>
    public const double DefaultHeight = 220;

    /// <summary>最小宽（DIP；缩放下限，与 `ModuleWindow.axaml` 的 `MinWidth` 一致）。</summary>
    public const double MinWidth = 180;

    /// <summary>最小高（DIP；与 `ModuleWindow.axaml` 的 `MinHeight` 一致）。</summary>
    public const double MinHeight = 120;

    /// <summary>
    /// 本模块声明（或框架兜底）的**首选窗口尺寸**（DIP）——「还原」回到这个尺寸。
    /// </summary>
    /// <remarks>
    /// 【用户第十一轮反馈】尺寸**由模块声明**（`ViewDeclaration.PreferredWidth/Height`），不按元素类型猜；
    /// 模块没声明就用框架默认（与 `ModuleWindow.axaml` 的 `Width/Height` 一致）。
    /// </remarks>
    public double PreferredWidth { get; }

    /// <summary>首选窗口高度（DIP）。</summary>
    public double PreferredHeight { get; }

    /// <summary>模块是否**声明了**首选尺寸（用于判断"未摆放的新窗口要不要按声明尺寸开"）。</summary>
    public bool HasPreferredSize { get; }

    private readonly IRuntimeQuery _query;
    private readonly IRuntimeCommand _command;
    private readonly double _scale;

    /// <summary>创建模块视图视图模型。</summary>
    /// <param name="view">实例视图（声明来源）。</param>
    /// <param name="layout">布局（可为空 = 未摆放）。</param>
    /// <param name="query">查询面（配置 Schema 读取）。</param>
    /// <param name="command">命令面（按字段写回配置）。</param>
    /// <param name="scale">屏幕缩放（1.0 = 100%；用于默认尺寸的像素换算）。</param>
    /// <param name="fallbackX">未摆放时的默认 X（物理像素）。</param>
    /// <param name="fallbackY">未摆放时的默认 Y（物理像素）。</param>
    public ModuleViewViewModel(
        ModuleInstanceView view,
        ModuleLayoutView? layout,
        IRuntimeQuery query,
        IRuntimeCommand command,
        double scale,
        double fallbackX,
        double fallbackY)
    {
        ArgumentNullException.ThrowIfNull(view);
        ArgumentNullException.ThrowIfNull(query);
        ArgumentNullException.ThrowIfNull(command);

        _query = query;
        _command = command;
        _scale = scale <= 0 ? 1.0 : scale;

        // 尺寸来源优先级：① 组合件里的布局（用户拖过）② **模块声明的首选尺寸** ③ 框架默认（= XAML 默认）
        HasPreferredSize = view.View is { PreferredWidth: > 0, PreferredHeight: > 0 };
        PreferredWidth = HasPreferredSize ? view.View!.PreferredWidth : DefaultWidth;
        PreferredHeight = HasPreferredSize ? view.View!.PreferredHeight : DefaultHeight;

        InstanceId = view.InstanceId;
        Title = view.DisplayName;
        IsPlaceholder = view.IsPlaceholder;
        HasLayout = layout is not null;

        // 尺寸与位置一律**整数物理像素**（唯一量化口径，见 `ScreenPixels`；用户第十二轮反馈）
        X = ScreenPixels.Round(layout?.X ?? fallbackX);
        Y = ScreenPixels.Round(layout?.Y ?? fallbackY);
        Width = ScreenPixels.Round(layout is { Width: > 0 } sized ? sized.Width : PreferredWidth * _scale);
        Height = ScreenPixels.Round(layout is { Height: > 0 } sizedHeight ? sizedHeight.Height : PreferredHeight * _scale);
        IsViewVisible = layout?.IsViewVisible ?? true;

        ApplyState(view.State);
        Rebuild(view);
    }

    /// <summary>实例 ID（工作现场内唯一、稳定）。</summary>
    public string InstanceId { get; }

    /// <summary>展示名（内容区顶部一行轻量文本，09 §5 修订）。</summary>
    public string Title { get; private set; }

    /// <summary>是否占位实例（模块定义缺失/版本不兼容）。</summary>
    public bool IsPlaceholder { get; private set; }

    /// <summary>是否已有布局记录（false = 由 UI 取默认位并写回）。</summary>
    public bool HasLayout { get; set; }

    /// <summary>X（**屏幕坐标，物理像素**）。</summary>
    [ObservableProperty]
    public partial double X { get; set; }

    /// <summary>Y（屏幕坐标，物理像素）。</summary>
    [ObservableProperty]
    public partial double Y { get; set; }

    /// <summary>宽（物理像素）。</summary>
    [ObservableProperty]
    public partial double Width { get; set; }

    /// <summary>高（物理像素）。</summary>
    [ObservableProperty]
    public partial double Height { get; set; }

    /// <summary>状态文本（Active/Disabled/Missing…）。</summary>
    [ObservableProperty]
    public partial string StateText { get; set; } = "Active";

    /// <summary>状态语义（颜色 + 形状双重编码，09 §7）。</summary>
    [ObservableProperty]
    public partial StateVisual State { get; set; } = StateVisual.Active;

    /// <summary>是否选中（点击边框带 → 主色边框 + 手柄 + 按钮簇）。</summary>
    [ObservableProperty]
    public partial bool IsSelected { get; set; }

    /// <summary>是否处于运行态（Active）；false = 已停用（保留实例/拓扑/配置，只停流）。</summary>
    public bool IsEnabled => string.Equals(StateText, "运行中", StringComparison.Ordinal);

    /// <summary>是否隐藏视图（false 时窗口关闭但模块仍在运行，§10.1）。</summary>
    [ObservableProperty]
    public partial bool IsViewVisible { get; set; } = true;

    /// <summary>是否锁定（不可拖动/缩放/选中；仍显示数据 —— O-3）。</summary>
    [ObservableProperty]
    public partial bool IsLocked { get; set; }

    /// <summary>本窗口的**对齐辅助线**（屏幕坐标；拖动中命中吸附时由控制器写入，窗内以虚线绘制）。</summary>
    [ObservableProperty]
    public partial IReadOnlyList<GuideLine> Guides { get; set; } = [];

    /// <summary>正在缩放的边（位标志：1 左 · 2 右 · 4 上 · 8 下；0 = 不在缩放）。</summary>
    [ObservableProperty]
    public partial int ResizeEdges { get; set; }

    /// <summary>是否正在被拖动或缩放（视觉反馈用）。</summary>
    [ObservableProperty]
    public partial bool IsInteracting { get; set; }

    /// <summary>是否显示**叠加信息层**（窗内连接线；由控制器按应用级设置同步写入 —— ADR-0039 D5）。</summary>
    [ObservableProperty]
    public partial bool ShowInfoOverlay { get; set; } = true;

    /// <summary>口上的标记块（边缘小块 = 口名/网络名 + 信息类型 + 方向 + 连接状态）。</summary>
    public ObservableCollection<PortMarkerViewModel> Ports { get; } = [];

    /// <summary>元素视图模型（按声明生成）。</summary>
    public ObservableCollection<ViewElementViewModel> Elements { get; } = [];

    /// <summary>当前矩形（屏幕坐标；吸附与命中计算用）。</summary>
    public CanvasRect Rect => new(X, Y, Width, Height);

    /// <summary>屏幕缩放（默认尺寸换算与最小尺寸钳制用）。</summary>
    public double Scale => _scale;

    /// <summary>刷新实例状态（实例视图变化时调用；元素与数据源声明在实例生命周期内不变）。</summary>
    public void Apply(ModuleInstanceView view)
    {
        ArgumentNullException.ThrowIfNull(view);

        Title = view.DisplayName;
        IsPlaceholder = view.IsPlaceholder;
        ApplyState(view.State);
        SyncPorts(view);

        OnPropertyChanged(nameof(Title));
        OnPropertyChanged(nameof(IsPlaceholder));
    }

    /// <summary>接受外部几何（来自布局数据；拖动/缩放中不调用）。</summary>
    public void ApplyLayout(ModuleLayoutView layout)
    {
        ArgumentNullException.ThrowIfNull(layout);

        X = ScreenPixels.Round(layout.X);
        Y = ScreenPixels.Round(layout.Y);
        Width = ScreenPixels.Round(layout.Width > 0 ? layout.Width : DefaultWidth * _scale);
        Height = ScreenPixels.Round(layout.Height > 0 ? layout.Height : DefaultHeight * _scale);
        IsViewVisible = layout.IsViewVisible;
        HasLayout = true;
    }

    /// <summary>导出当前几何（保存/提交用）。</summary>
    public ModuleLayoutView ToLayout() => new(InstanceId, X, Y, Width, Height, IsViewVisible);

    /// <summary>按 refresh：把所有可编辑字段的当前值读回来（配置 Schema 表单）。</summary>
    public void ReloadConfigurationForms()
    {
        foreach (var element in Elements)
        {
            if (element is ConfigurationEditorElementViewModel editor)
            {
                editor.Reload(_query);
            }
        }
    }

    /// <summary>提交一个字段的写回（返回是否成功；失败原因由返回消息带回）。</summary>
    /// <param name="fieldName">字段名。</param>
    /// <param name="value">文本值。</param>
    public CommandResult ApplyConfigurationField(string fieldName, string value) =>
        _command.UpdateConfigurationField(InstanceId, fieldName, value);

    private void ApplyState(string state)
    {
        StateText = state switch
        {
            "Active" => "运行中",
            "Disabled" => "已停用",
            "Missing" => "定义缺失",
            "Placeholder" => "占位",
            "Removed" => "已移除",
            _ => state,
        };

        State = state switch
        {
            "Active" => StateVisual.Active,
            "Disabled" => StateVisual.Idle,
            "Missing" or "Placeholder" => StateVisual.Placeholder,
            "Removed" => StateVisual.Error,
            _ => StateVisual.Warn,
        };

        OnPropertyChanged(nameof(IsEnabled));
    }

    private void Rebuild(ModuleInstanceView view)
    {
        Elements.Clear();
        SyncPorts(view);

        // **每个模块实例都有一个窗口**（用户裁决 M1-15）：声明了视图就按声明渲染；
        // 未声明视图的模块也照常开窗（至少显示模块名行），不引入"无界面模块"这一例外概念。
        if (view.View is not null)
        {
            var dataSources = view.View.DataSources.ToDictionary(s => s.Name, StringComparer.Ordinal);
            foreach (var element in Flatten(view.View.Elements))
            {
                // 【M1-16 / ADR-0040 D1】命令按钮：元素只负责**位置**，标题/图标/说明来自命令声明；
                // 元素引用了未声明的命令 → 走"未渲染"占位（不假装支持，也不静默）。
                if (element.Kind == ViewElementKind.CommandButton)
                {
                    var declared = view.View.Commands.FirstOrDefault(
                        c => string.Equals(c.Name, element.DataSource, StringComparison.Ordinal));

                    Elements.Add(declared is null
                        ? new UnsupportedElementViewModel(element.Kind, element.Title)
                        : CreateCommand(declared));
                    continue;
                }

                Elements.Add(CreateElement(element, dataSources));
            }
        }

        // 配置表单：模块声明里没有 B1 元素时由**框架补一个**——"配置编辑在模块视图内"（C5-4）
        // 对所有模块都成立（否则模块的配置在界面上无处可改）。
        if (!Elements.Any(e => e is ConfigurationEditorElementViewModel))
        {
            Elements.Add(CreateConfigurationEditor());
        }

        ReloadConfigurationForms();

        // **窗口尺寸不由内容计算**【用户第九轮反馈 · UI 开发方式修正】：
        // 未摆放的新窗口用 **XAML 里声明的默认尺寸**（`ModuleWindow.axaml` 的 Width/Height），
        // 内容排不下时由内容区的 ScrollViewer 滚动 —— 不再在代码里按"元素类型 → 高度表"估算尺寸
        // （那样等于把布局写死在代码里，用户无法在设计器/XAML 里调整）。
    }

    /// <summary>同步口标记（口集合在实例生命周期内稳定；每次刷新顺带更新连接状态与标签）。</summary>
    private void SyncPorts(ModuleInstanceView view)
    {
        foreach (var port in view.Ports)
        {
            var existing = Ports.FirstOrDefault(m => string.Equals(m.PortName, port.Name, StringComparison.Ordinal));
            if (existing is null)
            {
                Ports.Add(new PortMarkerViewModel(
                    InstanceId,
                    port.Name,
                    string.Equals(port.Direction, "Send", StringComparison.Ordinal),
                    port.NetworkLabel,
                    port.AnchorOffset,
                    port.IsConnected,
                    port.IsStalled,
                    port.ContractText));
                continue;
            }

            existing.Offset = port.AnchorOffset;
            existing.IsConnected = port.IsConnected;
            existing.IsStalled = port.IsStalled;

            if (!existing.IsEditing && !string.Equals(existing.Label, port.NetworkLabel, StringComparison.Ordinal))
            {
                existing.Label = port.NetworkLabel;
                existing.EditText = port.NetworkLabel ?? string.Empty;
            }
        }

        // 口消失（占位 → 真实定义等）：移除对应标记
        foreach (var stale in Ports
            .Where(m => !view.Ports.Any(p => string.Equals(p.Name, m.PortName, StringComparison.Ordinal)))
            .ToList())
        {
            _ = Ports.Remove(stale);
        }
    }

    private ViewElementViewModel CreateElement(ViewElement element, Dictionary<string, ViewDataSource> dataSources)
    {
        ViewDataSource? source = null;
        if (element.DataSource is { Length: > 0 } name)
        {
            _ = dataSources.TryGetValue(name, out source);
        }

        return element.Kind switch
        {
            ViewElementKind.Numeric => new ValueElementViewModel(element.Title, source),
            ViewElementKind.Curve => new CurveElementViewModel(element.Title, source),
            ViewElementKind.TextLog => new TextLogElementViewModel(element.Title, source),

            // 【M1-16 / ADR-0040】人机操作三通道：命令按钮 / 文本输入 / 开关 —— 全部**投递到模块线程**
            ViewElementKind.TextInput => new TextInputElementViewModel(element, SubmitInput),
            ViewElementKind.ToggleSlider => new ToggleElementViewModel(element, SubmitInput),
            ViewElementKind.ConfigurationEditor => CreateConfigurationEditor(),
            _ => new UnsupportedElementViewModel(element.Kind, element.Title),
        };
    }

    /// <summary>命令按钮：按命令声明生成（未声明的命令不会出现在界面上）。</summary>
    /// <param name="command">命令声明。</param>
    private CommandElementViewModel CreateCommand(ViewCommand command) =>
        new(command, name => _command.SendCommand(InstanceId, name));

    /// <summary>提交一个输入值（输入框/开关共用）。</summary>
    private CommandResult SubmitInput(string source, string text) =>
        _command.SubmitInput(InstanceId, source, text);

    private ConfigurationEditorElementViewModel CreateConfigurationEditor()
    {
        var editor = new ConfigurationEditorElementViewModel();
        editor.Attach(InstanceId, ApplyConfigurationField);
        return editor;
    }

    /// <summary>预留带厚度（与 <c>PortMarkerPanel.BandThickness</c> 一致；内容区内边距用它）。</summary>
    public const double ModuleContentBands = 20;

    /// <summary>
    /// 把容器元素展平：<c>Group</c>/<c>Scroll</c>/<c>Collapsible</c> 的子元素一并显示；
    /// <c>TabPages</c> 只取首页（页签交互属 Gate C）。
    /// </summary>
    private static IEnumerable<ViewElement> Flatten(IReadOnlyList<ViewElement> elements)
    {
        foreach (var element in elements)
        {
            if (element.Kind == ViewElementKind.TabPages)
            {
                var first = element.Children.Count > 0 ? element.Children[0] : null;
                if (first is not null)
                {
                    foreach (var nested in Flatten(first.Children.Count > 0 ? first.Children : [first]))
                    {
                        yield return nested;
                    }
                }

                continue;
            }

            if (element.Children.Count > 0 && element.Kind is ViewElementKind.Group or ViewElementKind.Collapsible or ViewElementKind.Scroll)
            {
                foreach (var nested in Flatten(element.Children))
                {
                    yield return nested;
                }

                continue;
            }

            yield return element;
        }
    }
}
