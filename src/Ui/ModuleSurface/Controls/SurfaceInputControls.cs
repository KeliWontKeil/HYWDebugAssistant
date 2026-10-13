using Avalonia;
using Avalonia.Animation;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using Avalonia.VisualTree;
using HYWDebugAssistant.Sdk.Views;
using HYWDebugAssistant.Ui.Controls;
using HYWDebugAssistant.Ui.ModuleSurface.Layout;
using HYWDebugAssistant.Ui.ModuleSurface.Theme;
using HYWDebugAssistant.Ui.Views;

namespace HYWDebugAssistant.Ui.ModuleSurface.Controls;

/// <summary>
/// **输入控件的底座** —— 从用户拿一个值、提交给模块、把框架的反馈显示出来。
/// </summary>
/// <remarks>
/// <para>
/// 它守一条纪律：<b>提交走框架，结果等框架</b>。控件不自己判断"这个值对不对"，
/// 也不自己编一句"成功/失败"——<c>SubmitInput</c> 交出去，反馈由框架回填到
/// <see cref="SurfaceElementState.Result"/>（浮在元素左下角的状态角上）。<b>只有一个提交口</b>，
/// 所以"到底提没提"永远只有一个答案。
/// </para>
/// <para>
/// 另一条：<b>未提交文本要留住</b>。用户打了一半的文本如果因为重建控件（切窗口尺寸、
/// 换标签页）而消失，是很讨厌的一件事 —— 所以每次改动都写进状态槽
/// <see cref="SurfaceStateSlot.PendingText"/>，构造时再取回来。
/// </para>
/// </remarks>
public abstract class SurfaceInputControl : SurfaceElementControl
{
    private readonly ISurfaceHost _host;

    /// <summary>造一个输入控件。</summary>
    /// <param name="node">声明节点。</param>
    /// <param name="state">数据面。</param>
    /// <param name="host">表面宿主。</param>
    protected SurfaceInputControl(ViewNode node, SurfaceElementState state, ISurfaceHost host)
        : base(node, state)
    {
        ArgumentNullException.ThrowIfNull(host);
        _host = host;
        Slot = node.Input ?? string.Empty;
    }

    /// <summary>本控件提交到的输入槽名（声明面 <c>input</c>；空 = 没绑）。</summary>
    public string Slot { get; }

    /// <summary>表面宿主。</summary>
    protected ISurfaceHost Host => _host;

    /// <summary>读一条自己的持久状态。</summary>
    /// <param name="slot">状态槽。</param>
    /// <returns>存过的值。</returns>
    protected string? Recall(SurfaceStateSlot slot) => _host.Recall(Node.StateKey, Node.Input, slot);

    /// <summary>写一条自己的持久状态。</summary>
    /// <param name="slot">状态槽。</param>
    /// <param name="value">值。</param>
    protected void Remember(SurfaceStateSlot slot, string? value) => _host.Remember(Node.StateKey, Node.Input, slot, value);

    /// <summary>把值提交给模块（空槽名 = 没绑，什么也不做）。</summary>
    /// <param name="text">提交的文本。</param>
    protected void Submit(string text)
    {
        if (Slot.Length == 0)
        {
            return;
        }

        _host.SubmitInput(Node.Input, text);
    }

    /// <summary>构造时取回"用户打了一半的文本"（没有 ⇒ 空串）。</summary>
    /// <returns>未提交文本。</returns>
    protected string RecallPending() => Recall(SurfaceStateSlot.PendingText) ?? string.Empty;
}

/// <summary>
/// **开关**（<c>toggleSwitch</c>）：一个勾选框，提交 <c>"true"</c> / <c>"false"</c>。
/// </summary>
/// <remarks>
/// 提交的是<b>文本</b>（"true"/"false"）：输入槽是文本通道（<c>SubmitInput(string)</c>），
/// 布尔语义由模块自己解释。不在这里把它翻成别的形状，否则同一件事会有两种表示。
/// </remarks>
public sealed class SurfaceToggleSwitchControl : SurfaceInputControl
{
    private readonly CheckBox _check;

    /// <summary>
    /// 正在**由代码**改勾选态（构造时按框架那份恢复、或模块要求复原）⇒ 不要回写、不要提交。
    /// </summary>
    /// <remarks>局部变量不够用：模块可以在运行期的任意时刻要求复原，那时构造早已结束。</remarks>
    private bool _restoring;

    /// <summary>造一个开关控件。</summary>
    /// <param name="node">声明节点。</param>
    /// <param name="state">数据面。</param>
    /// <param name="host">表面宿主。</param>
    public SurfaceToggleSwitchControl(ViewNode node, SurfaceElementState state, ISurfaceHost host)
        : base(node, state, host)
    {
        // **勾选框自己不带文字**（`Content` 留空），文字由 `BuildChrome` 按声明的方位摆 ——
        // 写进 `Content` 会与旁边那一行重复，而两个"暂停显示"会把控件撑得比声明的宽度还宽。
        _check = new CheckBox();

        var pending = RecallPending();
        SetFromSource(pending);

        _check.IsCheckedChanged += (_, _) =>
        {
            if (_restoring)
            {
                return;
            }

            // **只提交意愿，不翻转本地位置**：这一格的值只有一个来源（模块判定后写回的那一份）。
            Submit(_check.IsChecked == true ? "true" : "false");
        };

        // **框与文字共享元素那一块空间**：所以这里**不能**用 `FitBodyToDeclaration`（那是"主体独占
        // 整块空间"的口径，用在共享场景会把框拉满它的列并居中 ⇒ 文字被挤到列尾）。
        // 内边距与内容对齐在这儿就地设：主题里按类名写不管用 —— 那个类名挂在**外层元素**上，
        // 内层 `CheckBox` 身上没有，选择器永远匹配不到（实测 `Padding` 一直是缺省的 `8,0,0,0`）。
        _check.Padding = default;
        _check.HorizontalContentAlignment = Avalonia.Layout.HorizontalAlignment.Left;
        _check.VerticalContentAlignment = Avalonia.Layout.VerticalAlignment.Center;

        // **不许它有"未声明的下限"**：Fluent 的 `CheckBox` 模板自带一个 32 的最小高，
        // 声明比它矮的元素会被顶破（"框 = 内容"标准里，控制这种量的只能是声明）。
        // 仍欠的一条（已登记缺口）：模板内部那个 20×20 方框是**贴顶**的、文字是**居中**的，
        // 两个墨迹的中心不重合 —— 要消掉它只能自绘方框（本 kind 目前零使用，故不在此轮做）。
        _check.MinHeight = 0;

        Content = BuildChrome(_check);
        Classes.Add(SurfaceClasses.Toggle);
    }



    /// <summary>当前是否勾上（**事实源**的呈现，不是控件自己的状态）。</summary>
    public bool IsChecked => _check.IsChecked == true;

    /// <summary>
    /// **每帧从事实源取一次位置**（宿主每帧调 <see cref="SurfaceElementControl.Refresh"/>）。
    /// </summary>
    /// <remarks>
    /// 与滑动开关同一条纪律：这一格的"值"只有框架那一份（模块判定后写回），控件每帧读它、画它。
    /// 值没变就一个像素都不动（重画会搅乱焦点与过渡）。
    /// </remarks>
    public override void Refresh()
    {
        var pending = RecallPending();

        if ((_check.IsChecked == true) != string.Equals(pending, "true", StringComparison.Ordinal))
        {
            SetFromSource(pending);
        }
    }

    /// <summary>按事实源落一次勾选态（不提交 —— 这不是一次用户操作）。</summary>
    /// <param name="pending">事实源里那一份值。</param>
    private void SetFromSource(string? pending)
    {
        _restoring = true;

        try
        {
            _check.IsChecked = string.Equals(pending, "true", StringComparison.Ordinal);
        }
        finally
        {
            _restoring = false;
        }
    }
}

/// <summary>
/// **滑动开关**（<c>toggleSlider</c>）：一个拨动开关，提交 <c>"true"</c> / <c>"false"</c>。
/// </summary>
/// <remarks>
/// <para>
/// 与 <see cref="SurfaceToggleSwitchControl"/>（勾选框）的差别只有<b>外观</b>：提交口径完全一样。
/// 两个控件而不是一个带开关的控件，是因为"长什么样"要能各自被主题改，而"提交什么"必须两处一致。
/// </para>
/// <para>
/// <b>为什么自绘而不套 Fluent 的 `ToggleSwitch`</b>：那个模板的分支状态散在一堆 `PART_*` 部件与
/// 主题资源里（`ToggleSwitchFillOn` 之类），名号写错就是"改了没生效"这类最难查的缺陷；
/// 自绘只用本项目已经在用的 `Border` + `DynamicResource`，观感完全可控、深浅色都稳定。
/// </para>
/// </remarks>
public sealed class SurfaceToggleSliderControl : SurfaceInputControl
{
    /// <summary>轨道宽（胶囊总长）—— **固有尺寸**：那一行剩下的宽度留给说明文字。</summary>
    private const double TrackWidth = 36;

    /// <summary>圆钮与轨道边之间的边距（左右各一，同时决定可移动行程；**用前按设备像素取整**，见 `PlaceKnob`）。</summary>
    private const double KnobInset = 2;

    private readonly Border _track;
    private readonly Border _knob;
    private readonly Canvas _knobLayer;

    /// <summary>造一个滑动开关控件。</summary>
    /// <param name="node">声明节点。</param>
    /// <param name="state">数据面。</param>
    /// <param name="host">表面宿主。</param>
    public SurfaceToggleSliderControl(ViewNode node, SurfaceElementState state, ISurfaceHost host)
        : base(node, state, host)
    {
        // **圆钮的位置用"画布坐标"（`Canvas.Left`）表达，不用渲染变换**：
        // 位置最终是布局坐标这一轴的事，而 `Canvas.Left` 是注册过的附加属性 ⇒ 过渡认它；
        // 挂在 `RenderTransform` 里的 `TranslateTransform.X` 上时过渡**不被认**（实测：改一次状态，
        // 10% 时长处值就已经是终点 ⇒ 硬跳）。
        // **直径由轨道的高推出来**（见 `PlaceKnob`）：所以这里只定"贴左上、内缩一个常量"，
        // 不写任何"居中的算式" —— 那种算式一旦读到的框高不是它以为的那个值，钮就错位。
        _knob = new Border
        {
            CornerRadius = new CornerRadius(KnobInset),
        };

        // **位置与尺寸只有一个写入点**（`PlaceKnob`，它在框高变化时按设备像素重算）：
        // 这里不预设 `Canvas.Left/Top` —— 预设一个 DIP 常量就会与"按缩放取整"的那份打架。
        _knob.Classes.Add(SurfaceClasses.ToggleKnob);

        // 圆钮放在一个按坐标摆的画布上：它不再受"父容器怎么对齐子元素"影响，
        // 位置只由这一个坐标决定（**几何只有一个来源**）。
        // **这块画布就是"圆钮所表达的那个空间"**：直径按它的高算、坐标也相对它 ——
        // 两者同一个矩形，才不会出现"尺寸按外框算、位置落在外框减掉框线的坐标系里"（差那 1–2px）。
        // 它是**轨道的兄弟**（见下面那个单格 `Panel`），不是轨道的孩子：轨道的 `BorderThickness`
        // 恒为 2（主题里写死，聚焦只换颜色），孩子会被挤进"外框减 4px"的内容矩形里。
        _knobLayer = new Canvas
        {
            Width = TrackWidth,
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Stretch,
            IsHitTestVisible = false,   // 命中归命中区那一层（圆钮只是画出来的零件）
            Children = { _knob },
        };

        _track = new Border
        {
            Width = TrackWidth,
            // **不写高**：轨道吃掉"内容矩形"的高（"框 = 内容"标准，见 `排布与坐标.md` §1.3）。
            // **写死一个高就会造出隐形留白**：声明 32 的格子若按 18 摆，上下各留 7px，
            // 屏幕上就是"同一行里滑块看着比按钮矮一截、还偏下"。
            CornerRadius = new CornerRadius(KnobInset),

            // **贴在命中区左侧**（与"文字在右"的声明语义一致），并且**由它自己说了算**：
            // 不靠"父容器怎么对齐子元素"，几何只有一个来源。
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Stretch,
        };

        _track.Classes.Add(SurfaceClasses.ToggleTrack);

        // **轨道与圆钮是同一格里的兄弟**（单格 `Panel`）：两者拿到**同一个矩形** ——
        // 圆钮的直径与坐标都相对它算，轨道的框线只画在它自己那一圈里，挤不到圆钮。
        // 框高由排布给 ⇒ 高低变了要重算直径与胶囊圆角（同一处算，不散在别处）。
        var layer = new Panel { Children = { _track, _knobLayer } };

        // **环境定稿 ⇒ 不做过场**（`animated: false`）：框高由排布给，这一笔只是把钮摆到它该在的地方。
        _knobLayer.SizeChanged += (_, _) => PlaceKnob(IsChecked, animated: false);

        // **命中区是"滑块所在的那一整块"**，不是轨道那 36×18 本身：
        // 格子通常比滑块大得多（声明里常见 128×32），只认轨道就等于"周围全是死区"——
        // 用户点在滑块旁边什么都不会发生，看起来就是"这个开关点不动"。
        // 这一层铺满可用空间、背景透明（透明**也要**给，否则不参与命中测试），轨道在它里面居中。
        var hit = new Border
        {
            Background = Brushes.Transparent,
            Padding = default,
            Margin = default,
            Cursor = new Cursor(StandardCursorType.Hand),
            Child = layer,
        };

        hit.PointerPressed += (_, args) =>
        {
            args.Handled = true;

            // **只提交意愿，不翻转本地位置**：这一格的"值"只有一个来源（框架那一份），
            // 由模块判定后写回。本地先翻转就等于自己造了第二份事实 ——
            // 那样"能不能开"就得靠额外的信号去凑，而信号会漏、会重复、会与本地状态打架。
            SubmitRequested(!IsChecked);
            _ = _track.Focus();
        };

        // **键盘可达必须自己补**：原生 `CheckBox` / `ToggleSwitch` 自带"Tab 到它 + 空格切换"，
        // 自绘不补这一条，键盘用户就完全操作不到这个开关。
        _track.Focusable = true;
        _track.FocusAdorner = null;   // 焦点环由主题按类名画（原生那种虚线框与本项目的语言不一致）
        _track.KeyDown += OnKeyDown;

        // **内边距归零**：这一层只是"命中区 + 居中"，缝由元素那一层统一给。
        _track.Padding = default;
        _track.Margin = default;

        SetChecked(string.Equals(RecallPending(), "true", StringComparison.Ordinal));
        Content = BuildChrome(hit);
        Classes.Add(SurfaceClasses.Toggle);

        // **挂树后再摆一次**（此刻窗口缩放已经可解析），并订阅"缩放真变化"：
        // 窗口被拖到另一块屏 / 系统改 DPI 时重摆一次 —— 那是真实变化，允许位移。
        AttachedToVisualTree += (_, _) =>
        {
            if (TopLevel.GetTopLevel(this) is ModuleWindow window)
            {
                window.ResolvedScalingChanged += OnResolvedScalingChanged;
            }

            // **环境定稿**（挂树时缩放才可解析）⇒ 不做过场。
            PlaceKnob(IsChecked, animated: false);
        };

        DetachedFromVisualTree += (_, _) =>
        {
            if (TopLevel.GetTopLevel(this) is ModuleWindow window)
            {
                window.ResolvedScalingChanged -= OnResolvedScalingChanged;
            }
        };
    }

    private void OnResolvedScalingChanged(object? sender, EventArgs e) =>
        PlaceKnob(IsChecked, animated: false);   // 真实换屏/DPI 变化：环境定稿，不做过场

    /// <summary>
    /// **取这块控件所在窗口的缩放**（一次解析、一份发布 —— 见 <see cref="ModuleWindow.ResolvedScaling"/>）。
    /// </summary>
    /// <returns>缩放；<c>0</c> = 此刻还不知道（调用方**不写几何**，等下一遍）。</returns>
    /// <remarks>
    /// 模块窗口自己发布的那一份优先（它在开窗前就按目标屏解析好了）；拿不到（快照宿主、非模块窗口）
    /// 退回 `TopLevel.RenderScaling`；两者都没有 ⇒ <c>0</c>（**未知**）—— 不按 1.0 猜。
    /// </remarks>
    private double ResolveScaling()
    {
        if (TopLevel.GetTopLevel(this) is ModuleWindow window && window.ResolvedScaling > 0)
        {
            return window.ResolvedScaling;
        }

        return TopLevel.GetTopLevel(this)?.RenderScaling is > 0 and var fallback ? fallback : 0;
    }

    /// <summary>当前是否拨到"开"（**事实源**的呈现，不是控件自己的状态）。</summary>
    public bool IsChecked { get; private set; }

    /// <summary>
    /// **每帧从事实源取一次位置**（宿主每帧调 <see cref="SurfaceElementControl.Refresh"/>）。
    /// </summary>
    /// <remarks>
    /// <para>
    /// 这一格的"值"只有一个来源：框架那一份（模块判定后写回）。控件**每帧读它、画它**，
    /// 于是"界面上的位置 == 那一份值"永远成立，不需要任何额外的"拨回"信号去凑。
    /// </para>
    /// <para>
    /// 值没变就一个像素都不动 —— 这既是省事，也是"不打断用户正在做的事"（重画会搅乱焦点与过渡）。
    /// </para>
    /// </remarks>
    public override void Refresh()
    {
        var wanted = string.Equals(RecallPending(), "true", StringComparison.Ordinal);

        if (wanted != IsChecked)
        {
            SetChecked(wanted);
        }
    }

    /// <summary>
    /// **拨一次这个开关**（等价于用户点一下）：给"位移过渡认不认"这类探针用。
    /// </summary>
    /// <remarks>
    /// 探针要验的是"这条过渡有没有被认"，与"谁来触发"无关；走指针事件会把探针变成对输入层的测试
    /// （命中、捕获、坐标），而那几条都已经另有覆盖。它按用户的语义**提交意愿**，位置仍由事实源给。
    /// </remarks>
    public void ToggleFromUserForProbe() => SubmitRequested(!IsChecked);

    /// <summary>按空格 / 回车切换（与原生开关的手感一致：只提交意愿，位置由事实源给）。</summary>
    /// <param name="sender">事件源。</param>
    /// <param name="args">按键参数。</param>
    private void OnKeyDown(object? sender, KeyEventArgs args)
    {
        if (args.Key is not (Key.Space or Key.Enter))
        {
            return;
        }

        args.Handled = true;
        SubmitRequested(!IsChecked);
    }

    /// <summary>把"用户要它变成这个值"提交给模块（<b>不改本地位置</b>）。</summary>
    /// <param name="value">用户想要的值。</param>
    /// <remarks>
    /// 提交之后位置由事实源决定：模块认了就写同一个值（开关真的动），不认就写回原值
    /// （看起来"按不动"）—— 两者都是**同一个机制**的结果，不存在"先动了再被拨回来"这个窗口。
    /// </remarks>
    private void SubmitRequested(bool value) => Submit(value ? "true" : "false");

    /// <summary>落一次状态。</summary>
    /// <param name="value">事实源给的值。</param>
    /// <remarks>
    /// <b>几何只有一个来源</b>：圆钮的落点就是下面这一个平移量 ——
    /// 关 = 左内缩处、开 = 左内缩 + 可移动行程。位置不再受"父容器怎么对齐子元素"影响，
    /// 也就不存在"某个状态把它挪了一两像素"这种看不见成因的偏移。
    /// </remarks>
    private void SetChecked(bool value)
    {
        IsChecked = value;
        PlaceKnob(value, animated: true);

        // **"开"这一档同时挂在外框与轨道上**：外框那一份给"整块"的档位，轨道那一份是色与圆钮位置
        // 真正要看的（选择器直接打在画出来的那个元素上最稳）。
        Classes.Set(SurfaceClasses.ToggleOn, value);
        _track.Classes.Set(SurfaceClasses.ToggleOn, value);
    }

    /// <summary>
    /// 把圆钮摆到它该在的位置：**几何按设备像素算**（直径与坐标都取自圆钮所在的那块画布）。
    /// </summary>
    /// <param name="isOn">是否拨到"开"。</param>
    /// <param name="animated">
    /// 这一笔**该不该给用户看过场**：<see langword="true"/> = **状态变化**（用户拨开/关，该动给他看）；
    /// <see langword="false"/> = **环境定稿**（首次排布、窗口换屏改 DPI —— 只是把几何摆到它**该在**的地方）。
    /// 后者若也走过场，窗口刚出现时会看到圆钮"自己滑一下"：实测初值 0 → 1.6 DIP（= 2 设备像素），
    /// 主题那条 0.15 秒的 `Canvas.Left` 过场正好落在"窗口出现后 0.x 秒"⇒ 看起来就是"卡一下归位"。
    /// </param>
    /// <remarks>
    /// <para>
    /// <b>直径 = 该矩形的高 − 上下边距各一</b>（"框 = 内容"标准：声明多高，轨道就多高，钮随之伸缩）。
    /// </para>
    /// <para>
    /// <b>为什么整段都换算到设备像素再算</b>：声明是 DIP，而窗口缩放常常不是 100%。
    /// 取 125% 的那台机器做例子：36 DIP 的轨道 = 45 设备像素（整数）、**18 DIP 的高 = 22.5 像素（半像素）**、
    /// 内缩 2 DIP = **2.5 像素（半像素）** ⇒ 上下两条缝各自四舍五入，结果一条 2、一条 3 ——
    /// 屏幕上就是"圆钮偏了 1px"（实测四张状态图一致：上 2 / 下 3）。
    /// 把**边距**取整到整设备像素（`round(内缩 × 缩放)`）之后，两条缝在设备像素上精确相等，
    /// 相位也不再被框高那半个像素推走；圆钮直径随之变成分数 DIP —— 它是曲线、由抗锯齿画，不受影响。
    /// </para>
    /// <para>
    /// <b>为什么用 <c>_knobLayer</c> 的高、而不取轨道的高</b>：圆钮的坐标相对 `Canvas`，而轨道的
    /// `BorderThickness`（主题里恒为 2）会把孩子的内容矩形吃掉 4px ⇒ 拿外框的高算直径、坐标却落在
    /// 内容矩形的原点上，会稳定偏 1–2px。**算尺寸的矩形必须与放坐标的坐标系是同一个。**
    /// </para>
    /// </remarks>
    private void PlaceKnob(bool isOn, bool animated)
    {
        var height = _knobLayer.Bounds.Height;

        if (height <= 0)
        {
            // 还没排布（框高未知）⇒ 什么都不写：钮此刻是 0×0，写任何坐标都是猜。
            return;
        }

        var scale = ResolveScaling();

        if (scale <= 0)
        {
            // **缩放未知 ⇒ 不写**（等下一遍）：按 1.0 猜出来的位置，等真值到了还得再动一次 ——
            // 那正是"实例化后动 1px、然后归位"的成因，宁可这一帧不摆。
            return;
        }

        // **环境定稿不走过场**：这一笔只是把钮摆到它**该在**的地方（首次排布、换屏改 DPI），
        // 动画化它就会在窗口刚出现时"自己滑一下"（实测初值 0 → 1.6 DIP = 2 设备像素，
        // 主题那条 0.15 秒的过场正落在"窗口出现后 0.x 秒"）。
        // 做法：**只在这一次写入上**把过场清空，写完 `ClearValue` 交还主题 ——
        // 不留下任何本地值，主题仍是过场的唯一来源（状态变化照旧有动画）。
        if (!animated)
        {
            _knob.Transitions = null;
        }

        // 设备像素下的四个量（**这里是渲染边界上唯一的一次取整**：模型仍是连续 DIP）。
        var trackPixels = TrackWidth * scale;
        var layerPixels = height * scale;
        var gapPixels = Math.Round(KnobInset * scale);
        var diameterPixels = Math.Max(0, layerPixels - (gapPixels * 2));

        var diameter = diameterPixels / scale;

        if (double.IsNaN(_knob.Width) || Math.Abs(_knob.Width - diameter) > 0.01)
        {
            _knob.Width = diameter;
            _knob.Height = diameter;
            _knob.CornerRadius = new CornerRadius(diameter / 2);
        }

        var radius = height / 2;
        if (Math.Abs(_track.CornerRadius.TopLeft - radius) > 0.01)
        {
            _track.CornerRadius = new CornerRadius(radius);
        }

        // 竖直位置 = 取整后的边距（**不再是常量**：它随缩放变，才有"上下等距"这条保证）。
        var top = gapPixels / scale;
        if (double.IsNaN(Canvas.GetTop(_knob)) || Math.Abs(Canvas.GetTop(_knob) - top) > 0.01)
        {
            Canvas.SetTop(_knob, top);
        }

        // 横向：关 = 左边距、开 = 左边距 + 行程（行程 = 轨道宽 − 钮直径 − 左右边距各一，同样在设备像素里算）。
        var travel = Math.Max(0, trackPixels - diameterPixels - (gapPixels * 2));
        var left = isOn ? gapPixels + travel : gapPixels;
        Canvas.SetLeft(_knob, left / scale);

        if (!animated)
        {
            // 环境定稿写完就把过场交还主题（本地值清掉 ⇒ 样式那一份重新生效）。
            _knob.ClearValue(Animatable.TransitionsProperty);
        }
    }
}

/// <summary>
/// **枚举选择器**（<c>enumSelector</c>）：一个下拉，选中即提交。
/// </summary>
/// <remarks>
/// <c>options</c>（选项表）键已登记但读取器未解析 ⇒ 本轮选项来自 <b>状态里已有的取值</b>
/// （用户选中过的），没有可选项时显示为空。把 <c>options</c> 带进声明面是后续的活；
/// 在那之前不发明选项来源。用可编辑下拉是为了"模块给了新取值也能选"（输入即提交）。
/// </remarks>
public sealed class SurfaceEnumSelectorControl : SurfaceInputControl
{
    private readonly ComboBox _combo;
    private bool _restoring;

    /// <summary>造一个枚举选择器控件。</summary>
    /// <param name="node">声明节点。</param>
    /// <param name="state">数据面。</param>
    /// <param name="host">表面宿主。</param>
    public SurfaceEnumSelectorControl(ViewNode node, SurfaceElementState state, ISurfaceHost host)
        : base(node, state, host)
    {
        _combo = new ComboBox();
        _combo.Classes.Add(SurfaceClasses.Selector);

        // 下拉的水印就是它的说明文字 —— 不另起一行，下拉本身已经够高。
        if (node.Title.Length > 0)
        {
            _combo.PlaceholderText = node.Title;
        }

        var pending = RecallPending();

        if (pending.Length > 0)
        {
            _combo.Items.Add(pending);
            _combo.SelectedIndex = 0;
        }

        _combo.SelectionChanged += (_, _) =>
        {
            if (_restoring || _combo.SelectedItem is not string value)
            {
                return;
            }

            Remember(SurfaceStateSlot.PendingText, value);
            Submit(value);
        };

        // **`ComboBox` 自带的缺省也是"按内容对齐"**：不填满就会丢掉排布给的矩形（见 `FillContentRect`）。
        FillContentRect(_combo);

        Content = BuildChrome(_combo);
    }

    /// <summary>把可选项换成给定的一组（模块回填用；选中项尽量保持不变）。</summary>
    /// <param name="options">可选项。</param>
    public void SetOptions(IReadOnlyList<string> options)
    {
        ArgumentNullException.ThrowIfNull(options);
        var selected = _combo.SelectedItem as string;

        _restoring = true;
        _combo.Items.Clear();

        foreach (var option in options)
        {
            _combo.Items.Add(option);
        }

        if (selected is not null && options.Contains(selected))
        {
            _combo.SelectedItem = selected;
        }

        _restoring = false;
    }
}
