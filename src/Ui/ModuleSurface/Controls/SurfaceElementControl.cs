using System.ComponentModel;
using System.Globalization;
using System.Runtime.CompilerServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;
using Avalonia.Markup.Xaml.MarkupExtensions;
using Avalonia.Media;
using HYWDebugAssistant.Sdk.Views;
using HYWDebugAssistant.Ui.ModuleSurface.Layout;
using HYWDebugAssistant.Ui.ModuleSurface.Theme;

namespace HYWDebugAssistant.Ui.ModuleSurface.Controls;

/// <summary>
/// **表面元素控件的基类** —— 一个"声明节点 + 数据"的可视化。
/// </summary>
/// <remarks>
/// <para>
/// 它不要求子类长成某个形状：<b>内容怎么写、就地结果行放哪儿，都由子类决定</b>
/// （见 <see cref="BuildChrome"/>）—— "结果行贴在输入框下面"与"结果行跟在按钮下面"
/// 是两种正当排布，把它写死成一种会逼着控件去对抗基类。
/// </para>
/// <para>
/// 它认的是两件事：<see cref="Node"/>（声明）与 <see cref="State"/>（数据面）。
/// **不认模块、不认服务**：需要取数或提交的子类拿到的是注入的 <see cref="ISurfaceHost"/>。
/// </para>
/// <para>
/// 界面更新走<b>命令式一条路</b>（订阅 <see cref="SurfaceElementState.PropertyChanged"/> 后直接改控件），
/// 不用数据绑定：绑定在这里既省不掉几行，又会把"外观由主题给"这条纪律搅成"外观由转换器给"。
/// </para>
/// </remarks>
public abstract class SurfaceElementControl : ContentControl
{
    /// <summary>造一个表面元素控件。</summary>
    /// <param name="node">本控件的声明节点（几何、标题、绑定名的唯一来源）。</param>
    /// <param name="state">本控件的数据面。</param>
    protected SurfaceElementControl(ViewNode node, SurfaceElementState state)
    {
        ArgumentNullException.ThrowIfNull(node);
        ArgumentNullException.ThrowIfNull(state);
        Node = node;
        State = state;
        Classes.Add(SurfaceClasses.Element);

        // **声明的框就是硬边界**：控件内部堆得比框大时裁到框内，而不是画到相邻元素身上。
        // 不这么做的话，"声明 96×30、内部却要 96×80"的控件会把多出来的部分**压在下方的元素上**
        // （屏幕上就是"一个被遮挡的按钮加一条线"），而布局、溢出、声明读取**全都不报错**。
        // 裁掉是**可见且可查**的：体检用例会量出"有东西伸出框外"并报失败，于是它不会静默。
        ClipToBounds = true;

        // 声明的单位是"这个读数的单位"，不是某一帧的数据 ⇒ 在这里落一次就不会丢。
        State.Unit = node.Unit ?? string.Empty;

        // 从未取过数时读数位显示占位符，**不是空白**：空白会让"还没数据"与"这一位不存在"
        // 长得一模一样（实测：占位为空时那一列塌成 0 px，整行看起来只有标题）。
        // 先立状态、再建读数块 —— 两者从第一帧起就一致。
        State.Text = SurfaceElementState.Format(null);

        // 读数与单位是**结果行之外的第二个公共零件**：凡是"要显示一个当前值"的控件都挂它。
        // 它在基类里造、由基类按 State 刷字 —— 于是子类只负责"把这个零件放到自己的排布里"，
        // 不会出现某一种控件改了 State 却没画出来的事。
        Readout = new TextBlock
        {
            Text = Format(State),
            VerticalAlignment = VerticalAlignment.Center,
        };
        Readout.Classes.Add(SurfaceClasses.Monospace);
        Readout.Classes.Add(SurfaceClasses.Readout);

        State.PropertyChanged += OnStateChanged;
    }

    /// <summary>本控件的声明节点。</summary>
    public ViewNode Node { get; }

    /// <summary>本控件的数据面（子类改它，界面跟着变）。</summary>
    public SurfaceElementState State { get; }

    /// <summary>
    /// 读数块（显示 <see cref="SurfaceElementState.Text"/> 与 <see cref="SurfaceElementState.Unit"/>）。
    /// </summary>
    /// <remarks>
    /// 由基类按数据面刷字，子类只决定它<b>放在自己的哪一处</b>（一行、一列、还是贴在标题后面）。
    /// 单位与读数同一个块：单位是读数的属性，分开放会让"数值右对齐"这类要求变成两处对齐。
    /// </remarks>
    protected TextBlock Readout { get; }

    /// <summary>
    /// 把这一帧的新数据取进来（宿主每帧调一次；不取数的控件保持空实现）。
    /// </summary>
    /// <remarks>
    /// 只有<b>数据驱动</b>的控件重写它：纯输入控件（文本框、开关、按钮）的外观由用户与
    /// <see cref="State"/> 决定，与帧节拍无关。
    /// </remarks>
    public virtual void Refresh()
    {
    }

    /// <summary>
    /// 把**说明文字 + 主体 + 脚注**按声明的方位装成一个块，并给主体压一块**浮动状态角**。
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>说明文字要不要、摆在哪，全由声明面的 <see cref="ViewNode.TextPlacement"/> 决定</b>
    /// （<c>none</c> / <c>above</c> / <c>below</c> / <c>left</c> / <c>right</c>）；文字取
    /// <see cref="ViewNode.Title"/>。控件构造函数**不再各自决定**"我有没有说明文字" ——
    /// 那是声明面的事；写在代码里就会出现"作者看不出、也改不了"的特例
    /// （曾实测：只读文本框的说明文字被一条 `if (ReadOnly)` 静默吃掉）。
    /// </para>
    /// <para>
    /// <b>缺省值逐 kind 在 schema 的 <c>defaultText</c> 段</b>（今天会画的那几种保持原方位、其余
    /// <c>none</c>）⇒ 默认值搬家不改变任何现有模块的渲染，而控件代码里零默认字面量。
    /// </para>
    /// <para>
    /// <b>没有说明文字 ≠ 一条高度 0 的空行</b>：<c>none</c> 时这一项根本不进行列表，直接走下面
    /// <c>rows.Count == 1</c> 的近路返回主体 —— 结构上就没有那一行可占位。
    /// </para>
    /// <para>
    /// <b>状态角是浮动的、不占布局行</b>：它压在主体上（<c>IsHitTestVisible=false</c>），
    /// 出现与消失**不改变任何尺寸**。以前它是一行真布局（`Auto` 行 + `MinHeight`），
    /// 一按按钮就把下面的控件整体顶下去 —— 用户看到的"消息把控件挤走了"就是这个。
    /// </para>
    /// <para>
    /// <b>主体一律吃满"内容矩形"</b>（= 元素的框 − 说明/脚注那几行）：说明与脚注各占一行 <c>Auto</c>，
    /// 主体那一行是 <c>1*</c>。"主体按内容高、余量留底"那条路**已经删掉** —— 它让同一个 kind
    /// 在"有说明 / 没说明"两条路上落在**不同位置**，也让 <c>fill</c> 的主体拿不到高度。
    /// </para>
    /// <para>
    /// <b>文字型主体（<see cref="TextBlock"/>）在内容矩形里居中</b>（见 <see cref="FitTextToContent"/>）：
    /// 字体给的是**行盒**，行盒比框矮时"贴顶"会让视觉中心偏上。控件的**内部零件**（读数块、轨道、方框）
    /// 由子类在这条规则下自己摆 —— 那是控件自己的事，不进声明面。
    /// </para>
    /// </remarks>
    /// <param name="body">控件的主体。</param>
    /// <param name="footer">挂在主体下方的脚注（可空；例如"已丢弃 N 行"）。</param>
    /// <returns>可挂到 <see cref="ContentControl.Content"/> 的控件。</returns>
    protected Control BuildChrome(Control body, Control? footer = null)
    {
        ArgumentNullException.ThrowIfNull(body);

        FitTextToContent(body);

        var placement = Node.TextPlacement;

        // **控件自带文字槽时，说明交给它自己的内容槽**（勾选框 / 拨动开关的文字就是
        // "框旁边那句话"）：这样"方块与文字"用的是**同一套模板度量**，中线对齐由控件保证 ——
        // 换成旁边另立一个 TextBlock 兄弟，两者的垂直定位互不相干，字体一换就漂。
        // **只认"并排"这一个方位**：`above` / `below` 的表达是"文字另占一行"，那与自带的
        // 行内槽不是一回事（写进内容槽就变成"同一行里的第二句话"）。
        if (body is ContentControl slot && Node.Title.Length > 0 && placement is "left" or "right")
        {
            ApplyContentGap(slot, placement);
            return body;
        }

        // **主体不再包任何"角"**：控件只有"主体 + 说明 + 脚注"这三样按声明摆出来。
        // 消息不在这条路上 —— 它是模块写进某条源、由声明面指定的一块文字（`label` / 只读文本面）显示的，
        // 不是每个控件自带的浮层（那会把文字散到各处，也是"把功能限定死"）。
        var overlay = body;
        var label = BuildLabel(placement);

        // 左右并排：说明与主体**同一行盒**，所以不能走"上下两行"那条路。
        if (label is not null && placement is "left" or "right")
        {
            return BuildSideBySide(overlay, label, placement);
        }

        var rows = new List<Control>();

        // 上下的顺序就是方位的顺序：`above` ⇒ 说明在前，`below` ⇒ 说明在后。
        if (label is not null && placement is "above")
        {
            rows.Add(label);
        }

        rows.Add(overlay);

        if (label is not null && placement is "below")
        {
            rows.Add(label);
        }

        if (footer is not null)
        {
            rows.Add(footer);
        }

        if (rows.Count == 1)
        {
            return overlay;
        }

        // **说明与脚注各自 `Auto`、主体那一行 `1*`**：主体一律吃满"内容矩形"。
        // **这里只有这一条路**：`StackPanel` 量孩子时给的是无限高，天生表达不了"吃满剩余" ——
        // 按内容高排会让同一个 kind 在"有说明 / 没说明"两条路上落在**不同位置**（看着像偏下），
        // 且 `fill` 的主体拿不到高度（元素 300 高、里面的文本面只有 18 高）。
        var grid = new Grid();

        foreach (var row in rows)
        {
            grid.RowDefinitions.Add(new RowDefinition(
                ReferenceEquals(row, overlay) ? new GridLength(1, GridUnitType.Star) : GridLength.Auto));
        }

        for (var index = 0; index < rows.Count; index++)
        {
            Grid.SetRow(rows[index], index);
            grid.Children.Add(rows[index]);
        }

        return grid;
    }

    /// <summary>
    /// **造说明文字**（<c>none</c>、没有方位、或没有文字 ⇒ <see langword="null"/>）。
    /// </summary>
    /// <param name="placement">声明的方位。</param>
    /// <returns>说明文字控件；不需要 ⇒ <see langword="null"/>。</returns>
    /// <remarks>
    /// <b>间距由这里就地给</b>：上下堆叠时文字与主体各占一行，要一点下边距把两者拉开；
    /// 左右并排时**不能给** —— 那点边距会把文字顶高半个边距，屏幕上就是"框与文字没对齐"
    /// （并排的水平间距由两列各自的宽度决定，不由边距决定）。
    /// 类名只有一个 <see cref="SurfaceClasses.Title"/>，"堆叠还是并排"是排布的事，不该让类名分家。
    /// </remarks>
    private TextBlock? BuildLabel(string placement)
    {
        if (placement.Length == 0
            || string.Equals(placement, "none", StringComparison.Ordinal)
            || Node.Title.Length == 0)
        {
            return null;
        }

        var stacked = placement is "above" or "below";
        var label = new TextBlock
        {
            Text = Node.Title,
            Margin = stacked
                ? new Thickness(0, 0, 0, SurfaceTheme.Dip(this, SurfaceTheme.LabelGapKey, 0))
                : default,
        };

        label.Classes.Add(SurfaceClasses.Title);

        // **说明文字的外观由它自己的键说**（`titleFontSize`/`titleColor`）：
        // 与主文字分开，且与同一行里别的控件无关。
        ApplyLook(label, TitleLook());

        return label;
    }

    /// <summary>
    /// **在控件自己的文字槽上留出"框与文字之间那点缝"**。
    /// </summary>
    /// <param name="slot">带内容槽的控件（勾选框 / 拨动开关）。</param>
    /// <param name="placement">方位（<c>left</c> / <c>right</c>）。</param>
    /// <remarks>
    /// 缝必须**由这一侧**给：模板里的内容与方块是挨着的（实测缝 = 0），
    /// 完全没有缝在屏幕上"糊成一团"；缝放在说明那一侧，于是 `left` / `right` 都能用同一个值。
    /// 值取自主题（<see cref="SurfaceTheme.LabelGapKey"/>）—— 与上下堆叠时用的**是同一个键**：
    /// "说明与主体之间留多少"只该有一处出处，换个主题就该一起变。
    /// </remarks>
    private void ApplyContentGap(ContentControl slot, string placement)
    {
        // **缺省值写在这里而不是 0**：控件在这一步还**没进可视树**，`TryFindResource` 取不到主题，
        // 于是回退值就是实际用的值 —— 传 0 会让"缝"永远不存在（实测缝 = 0，主题里改了也没反应）。
        var gap = SurfaceTheme.Dip(this, SurfaceTheme.LabelGapKey, 4);

        // 包一层 TextBlock 再给边距，而不是设 `Padding`：实测 `Padding` 在这两个控件上
        // 不改变"框与字"的距离（模板把它吃在内层），而内容自己的边距一定生效。
        var text = new TextBlock
        {
            Text = Node.Title,
            Margin = placement is "left"
                ? new Thickness(0, 0, gap, 0)
                : new Thickness(gap, 0, 0, 0),
        };

        // 说明文字的外观同并排那一档：由 `titleFontSize`/`titleColor` 说。
        ApplyLook(text, TitleLook());

        slot.Content = text;
    }

    /// <summary>
    /// **左右并排**：说明与主体同一行盒，主体按内容宽、说明紧随其后。
    /// </summary>
    /// <param name="overlay">主体（已包状态角）。</param>
    /// <param name="label">说明文字。</param>
    /// <param name="placement">方位（<c>left</c> / <c>right</c>）。</param>
    /// <returns>并排容器。</returns>
    /// <remarks>
    /// <para>
    /// <b>用"按顺序排"而不是"分两列的网格"</b>：并排的语义是"主体与它的说明挨着"，
    /// 两者之间**没有可分配的余量**。用网格时主体占的那一列要么抢满（主体被拉宽、方框跑到格子中央，
    /// 文字被挤到列尾）、要么按内容（那与按顺序排是同一件事，白搭一层）。
    /// 按顺序排的额外好处：说明再长也只会顶到元素自己的边界并被省略号收尾，不会改变主体的位置。
    /// </para>
    /// <para>
    /// <b>行高与高度都取主体那一轴</b>：主体声明了 <c>height.pixels</c> ⇒ 就用它。
    /// 这样"框中线 = 文字中线"对**任何**主体高度都成立；换成主题里的一个静态常量，
    /// 就会与主体高度（那是声明值）漂移。
    /// </para>
    /// <para>
    /// <b>为什么不能只靠 <c>VerticalAlignment = Center</c></b>：字体缺省的行盒比控件矮，
    /// 于是居中的只是"行盒"而不是"眼睛看到的字"，实测差 2–3 像素 ——
    /// 布局、溢出、声明兑现**全都合格**，只有眼睛看得出不对。
    /// </para>
    /// <para>
    /// <b>副轴锚点在这里生效</b>：并排时 <see cref="ViewNode.TextAnchor"/> 管**纵向**（顶 / 中 / 底）。
    /// </para>
    /// </remarks>
    private StackPanel BuildSideBySide(Control overlay, Control label, string placement)
    {
        // 主体：横向只要自己那点内容宽（它是共享空间的一方，不抢满），纵向吃满这一行。
        overlay.VerticalAlignment = VerticalAlignment.Stretch;

        if (Node.Height.Pixels is { } rowHeight)
        {
            label.Height = rowHeight;
        }

        // **行盒与居中走共用那一处实现**（`FitTextToContent`）：这里原来内联又写了一套
        // （自己设 Height/LineHeight/Center）—— 同一意图两套实现必然漂移，而这一处漂移的
        // 表现正是"同一行里同为说明文字却看起来一个高一个低"。
        FitTextToContent(label);

        // **副轴锚点在这里生效**：并排时 `TextAnchor` 管**纵向**（顶 / 中 / 底）。
        // 放在共用口径**之后**：共用那一步先给"居中"，再由声明覆盖成顶/底。
        label.VerticalAlignment = Node.TextAnchor switch
        {
            "start" => VerticalAlignment.Top,
            "end" => VerticalAlignment.Bottom,
            _ => VerticalAlignment.Center,
        };

        var row = new StackPanel { Orientation = Orientation.Horizontal };

        if (placement is "left")
        {
            row.Children.Add(label);
            row.Children.Add(overlay);
        }
        else
        {
            row.Children.Add(overlay);
            row.Children.Add(label);
        }

        return row;
    }

    /// <summary>取一个数值资源（语法糖：等价于 <see cref="Theme.SurfaceTheme.Dip"/>）。</summary>
    /// <param name="key">资源键。</param>
    /// <param name="fallback">缺失时的回退。</param>
    /// <returns>DIP 值。</returns>
    protected double ThemedDip(string key, double fallback) => SurfaceTheme.Dip(this, key, fallback);

    /// <summary>
    /// **文字型主体在内容矩形里居中**：这一轴声明了固定像素 ⇒ 行盒直接取那个值
    /// （行盒 = 框 ⇒ 视觉中心 = 框中心，不受字体 half-leading 影响）；其余档位靠居中。
    /// </summary>
    /// <param name="body">控件的主体（不是 <see cref="TextBlock"/> 时什么都不做）。</param>
    /// <remarks>
    /// "布局、溢出、声明兑现全都合格、只有眼睛看得出不对"的那一类偏移，根因就在这里：
    /// 居中的只是**行盒**，而不是眼睛看到的字（字体缺省行盒比控件矮，实测差 2–3 像素）。
    /// 行盒取声明高度可以从根上消掉它。控件的**内部零件**（读数块）也调这一条。
    /// </remarks>
    protected void FitTextToContent(Control body)
    {
        ArgumentNullException.ThrowIfNull(body);

        if (body is not TextBlock text)
        {
            return;
        }

        text.VerticalAlignment = VerticalAlignment.Center;

        if (Node.Height.Pixels is { } height)
        {
            text.LineHeight = height;
        }
    }

    /// <summary>
    /// **主体填满内容矩形**（两轴 <c>Stretch</c>）—— "框 = 内容"这条标准的落地动作。
    /// </summary>
    /// <param name="body">控件体内那个"主体"元素（按钮 / 下拉 / 文本面…）。</param>
    /// <remarks>
    /// <para>
    /// 存在的理由：**不少控件自带"内容对齐"型的对齐** —— Avalonia 的 `Button` / `CheckBox` /
    /// `ComboBox` 缺省都按内容摆。于是它们按**自己的内容宽**摆、把排布给的矩形丢掉：
    /// 声明 `width.pixels` **静默失效**（实测：声明 200 的按钮实得 44，界面上就是"改了宽度没反应"）。
    /// </para>
    /// <para>
    /// <b>只给对齐、不给尺寸</b>：尺寸由排布按声明给矩形（`panel` 用坐标、`grid` 用格子）。
    /// 这里再设 `Width` / `Height` 是**第二份尺寸来源**，而且会打架 —— 实测把框设成"声明宽"
    /// 之后，它比所在的格子还宽（格 = 元素宽 − 说明宽），居中结果是**负偏移**，整块被裁掉。
    /// 两轴都 <c>Stretch</c>：与"框 = 内容"一致（`fill` / `adaptive` 的矩形由排布给，它就该吃满）。
    /// </para>
    /// </remarks>
    protected static void FillContentRect(Control body)
    {
        ArgumentNullException.ThrowIfNull(body);

        body.HorizontalAlignment = HorizontalAlignment.Stretch;
        body.VerticalAlignment = VerticalAlignment.Stretch;
    }

    /// <summary>数据面变了 ⇒ 只刷该刷的那一处。</summary>
    private void OnStateChanged(object? sender, PropertyChangedEventArgs args)
    {
        if (args.PropertyName is nameof(SurfaceElementState.Text)
            or nameof(SurfaceElementState.Unit))
        {
            Readout.Text = Format(State);
        }

        // **外观是基础能力**：模块设了什么颜色就画什么颜色，框架不解释它的含义
        // （见 `IViewAppearanceAccess`）。`null` ⇒ 回到主题 / 声明给的值。
        if (args.PropertyName is nameof(SurfaceElementState.OutlineColor))
        {
            ApplyOutlineColor(State.OutlineColor);
        }

        // **角色词那条路**：模块说的是"配色的哪一支"（相对色）⇒ 画成对主题资源的绑定，
        // 于是换深浅主题、用户在设置里改那一支色，这一圈跟着变（绝对色那条路不会）。
        if (args.PropertyName is nameof(SurfaceElementState.OutlineRole))
        {
            ApplyOutlineRole(State.OutlineRole);
        }

        // **文字外观只由这一条路重画**：解析"状态 → 声明 → 主题"，再交给控件写到自己那块文字上。
        if (args.PropertyName is nameof(SurfaceElementState.TextColor)
            or nameof(SurfaceElementState.TextRole)
            or nameof(SurfaceElementState.TextSize))
        {
            OnMainTextLookChanged();
        }

        // **运行期只读**同族：模块说"这一格现在不让改"就画成只读（子类决定画在哪）。
        if (args.PropertyName is nameof(SurfaceElementState.ReadOnly))
        {
            ApplyReadOnly(State.ReadOnly);
        }
    }

    /// <summary>
    /// 把模块给的**运行期只读**落到主体上（<see langword="null"/> ⇒ 回到声明那一档）。
    /// </summary>
    /// <param name="isReadOnly">只读；<see langword="null"/> = 没设。</param>
    /// <remarks>
    /// 默认什么都不做：只有"支持只读"的控件（文本面）才接管 —— 其余控件收到这条只是忽略。
    /// 这与"外观是基础能力"同一条路子：框架只负责把模块说的话画出来，不解释为什么。
    /// </remarks>
    protected virtual void ApplyReadOnly(bool? isReadOnly)
    {
        _ = isReadOnly;
    }

    /// <summary>把模块给的边框色画到主体上（`null` ⇒ 清掉本控件自己设过的那一份）。</summary>
    /// <param name="argb">颜色；<see langword="null"/> = 没设。</param>
    private void ApplyOutlineColor(uint? argb)
    {
        // **交给子类定"画在哪、怎么画"**：多数控件就是外层元素的一条边框；
        // 自带边框的控件（文本面）自己接管 —— 见 <see cref="ApplyOutlineColorCore"/>。
        ApplyOutlineColorCore(argb);
    }

    /// <summary>把模块给的边框**角色词**画到主体上（`null` ⇒ 交还主题那一档）。</summary>
    /// <param name="role">角色词；<see langword="null"/> = 没设。</param>
    /// <remarks>
    /// 解析只有一处（<see cref="SurfaceTheme.TryRoleKey"/>）：词 → 主题键；非法/没登记的词**视同没设**
    /// （界面回到声明 / 主题那一档）—— 与同族"设定路径不抛"一致。
    /// </remarks>
    private void ApplyOutlineRole(string? role)
    {
        ApplyOutlineRoleCore(
            role is { Length: > 0 } word && SurfaceTheme.TryRoleKey(word, out var key) ? key : null);
    }

    /// <summary>
    /// **角色词的边框色落到哪里**（默认：本元素 `BorderBrush` 上的一条**主题资源绑定**）。
    /// </summary>
    /// <param name="roleKey">主题资源键（已解析）；<see langword="null"/> = 清掉。</param>
    /// <remarks>
    /// 走 <see cref="DynamicResourceExtension"/> 而不是取色：换主题 / 用户改色时由资源系统重新解析，
    /// 控件不必重建（与 `<c>ApplyLook</c>` 里那条角色绑定同一条口径）。
    /// </remarks>
    protected virtual void ApplyOutlineRoleCore(string? roleKey)
    {
        if (roleKey is null)
        {
            ClearValue(Border.BorderBrushProperty);
            return;
        }

        this[!Border.BorderBrushProperty] = new DynamicResourceExtension(roleKey);
    }

    /// <summary>
    /// **边框色落到哪里**（默认：本元素的 `BorderBrush`）。
    /// </summary>
    /// <param name="argb">颜色；<see langword="null"/> = 清掉。</param>
    /// <remarks>
    /// 需要覆盖的场合：**主体自己带一圈可见边框**的控件 —— 那时外层元素上再画一条就成了两道线，
    /// 而"把其中一道染色"看起来就是"红圈外套一圈灰"。判据永远是"**用户看得见的那一圈是谁画的**"。
    /// </remarks>
    protected virtual void ApplyOutlineColorCore(uint? argb)
    {
        if (argb is not { } value)
        {
            ClearValue(Border.BorderBrushProperty);
            return;
        }

        SetValue(Border.BorderBrushProperty, new SolidColorBrush(Color.FromUInt32(value)));
    }

    /// <summary>
    /// **把用户刚做的那次交互复原**（模块判"这次动作不成立"时要求）。
    /// </summary>
    /// <remarks>
    /// 默认**什么都不做**：多数控件没有"可退回的交互状态"（读数、标签、文本面）。
    /// 有交互状态的那几种（勾选框 / 滑块）自己覆盖它 —— 于是"谁需要复原"在类型上看得见。
    /// </remarks>
    public virtual void ApplyInteractionReset()
    {
        // 默认无交互状态可退。
    }

    /// <summary>上一次见到过的**动作复原信号**（由宿主在刷新节拍里推进）。</summary>
    public long SeenInteractionRevision { get; set; }

    /// <summary>
    /// **这块控件的主文字该长什么样**：运行期状态 → 声明 → <see langword="null"/>（交还主题）。
    /// </summary>
    /// <remarks>
    /// 主文字 = 用户看这块控件看的是"内容"：`label` 的正文、`textBox` 的输入文本、
    /// `numeric`/`curve` 的读数、`commandButton` 按钮上的字。**说明文字**另有 <see cref="TitleLook"/> ——
    /// 两块文字各由自己的键描述，别的控件长什么样与本控件无关。
    /// <para>
    /// **声明是初值、逻辑说了算**：运行期可按 `Appearance` 覆盖；清空 ⇒ 回到声明；
    /// 两者都没有 ⇒ <see langword="null"/> ⇒ 由**主题**那一档决定（代码里不写第二份默认值）。
    /// </para>
    /// </remarks>
    protected (double? Size, Color? Color, string? Role) MainTextLook()
    {
        // 运行期给的那一份**优先于声明**；角色词与绝对值是同一键位的两种写法（后写的清掉另一种）。
        var (color, role) = State.TextRole is { Length: > 0 } runtimeRole
            && SurfaceTheme.TryRoleKey(runtimeRole, out var runtimeKey)
                ? ((Color?)null, (string?)runtimeKey)
                : State.TextColor is { } argb
                    ? ((Color?)Color.FromUInt32(argb), (string?)null)
                    : DeclaredInk(Node.TextColor);

        return (State.TextSize ?? (Node.FontSize > 0 ? Node.FontSize : null), color, role);
    }

    /// <summary>
    /// **这块控件的说明文字该长什么样**：只有声明（说明文字没有运行期通道），没有 ⇒ <see langword="null"/>。
    /// </summary>
    protected (double? Size, Color? Color, string? Role) TitleLook()
    {
        var (color, role) = DeclaredInk(Node.TitleColor);
        return (Node.TitleFontSize > 0 ? Node.TitleFontSize : null, color, role);
    }

    /// <summary>
    /// **主文字的外观要重画一次**：每个"有主文字"的控件在这里把它写到**自己那一块**上。
    /// </summary>
    /// <remarks>
    /// 默认什么都不做 —— 没有主文字的控件（滑块、勾选框）不必接管。
    /// 必须写到**真正显示文字的元素**上（局部值），不能靠外层元素继承：主题的类选择器
    /// （`TextBlock.surface-label` / `TextBox.surface` / `Button.surface-command`）会在那个元素自身上设字号/前景，
    /// 而**样式值压过继承** ⇒ 只改外层等于没改（实测："声明了字号却没反应"）。
    /// </remarks>
    protected virtual void OnMainTextLookChanged()
    {
    }

    /// <summary>把"这一块文字该长什么样"写到**真正显示它的那个文字元素**上（`null` ⇒ 交还主题）。</summary>
    /// <param name="text">显示文字的元素。</param>
    /// <param name="look">字号与颜色。</param>
    /// <remarks>
    /// 两个重载各自用**自己类型的属性实例**（`TextBlock` 不是 `TemplatedControl`，两者不能共用一个参数类型）：
    /// 解析只有一处（<see cref="MainTextLook"/> / <see cref="TitleLook"/>），落点各写各的。
    /// </remarks>
    protected static void ApplyLook(TextBlock text, (double? Size, Color? Color, string? Role) look)
    {
        ArgumentNullException.ThrowIfNull(text);

        if (look.Size is { } size)
        {
            text.FontSize = size;
        }
        else
        {
            text.ClearValue(TextBlock.FontSizeProperty);
        }

        if (look.Role is { } roleKey)
        {
            // **角色词 ⇒ 主题绑定**（换深浅主题、用户改色都自动跟随）；hex 才是局部值（刻意固定）。
            text[!TextBlock.ForegroundProperty] = new DynamicResourceExtension(roleKey);
        }
        else if (look.Color is { } color)
        {
            text.Foreground = new SolidColorBrush(color);
        }
        else
        {
            text.ClearValue(TextBlock.ForegroundProperty);
        }
    }

    /// <summary>同 <see cref="ApplyLook(TextBlock, (double?, Color?, string?))"/>，但目标是自带文字外观的模板控件（按钮）。</summary>
    /// <param name="control">显示文字的元素。</param>
    /// <param name="look">字号、颜色与角色。</param>
    protected static void ApplyLook(TemplatedControl control, (double? Size, Color? Color, string? Role) look)
    {
        ArgumentNullException.ThrowIfNull(control);

        if (look.Size is { } size)
        {
            control.FontSize = size;
        }
        else
        {
            control.ClearValue(TemplatedControl.FontSizeProperty);
        }

        if (look.Role is { } roleKey)
        {
            control[!TemplatedControl.ForegroundProperty] = new DynamicResourceExtension(roleKey);
        }
        else if (look.Color is { } color)
        {
            control.Foreground = new SolidColorBrush(color);
        }
        else
        {
            control.ClearValue(TemplatedControl.ForegroundProperty);
        }
    }

    /// <summary>
    /// 解析声明里的墨色：**角色词**（相对色 ⇒ 随主题）或 **hex**（绝对色 ⇒ 刻意固定）；都没有 ⇒ <c>(null, null)</c>。
    /// </summary>
    /// <param name="text">声明里写的字面量。</param>
    private static (Color? Color, string? Role) DeclaredInk(string text) =>
        SurfaceTheme.TryRoleKey(text, out var roleKey)
            ? (null, roleKey)
            : SurfaceTheme.TryParseColor(text, out var color) ? (color, null) : (null, null);

    /// <summary>把读数与单位拼成一行显示文本。</summary>
    /// <param name="state">数据面。</param>
    /// <returns>显示文本（单位为空 ⇒ 只有读数）。</returns>
    private static string Format(SurfaceElementState state) => state.Unit.Length == 0
        ? state.Text
        : $"{state.Text} {state.Unit}";
}

/// <summary>
/// **表面元素的数据面** —— 一个控件上"会变的那几个字"。
/// </summary>
/// <remarks>
/// <para>
/// 只用 <see cref="INotifyPropertyChanged"/>，不引 CommunityToolkit 的源生成器：
/// 控件的取值少、写入点集中（<see cref="Set"/>），手写一个 <see cref="Set"/> 比给每种控件
/// 各生成一套 <c>ObservableProperty</c> 更清楚。
/// </para>
/// <para>
/// <b>它不持有服务、不做取数</b>：取数在 <c>SurfaceSourceReader</c>，控件把结果写进来。
/// </para>
/// </remarks>
public sealed class SurfaceElementState : INotifyPropertyChanged
{
    private readonly Dictionary<string, object?> _values = [];

    /// <inheritdoc />
    public event PropertyChangedEventHandler? PropertyChanged;

    /// <summary>主显示文本（数值的读数、按钮的标题、日志的占位…）。</summary>
    public string Text
    {
        get => Get(string.Empty);
        set => Set(value);
    }

    /// <summary>单位文本（声明了 <c>unit</c> 才有；空 = 不显示）。</summary>
    public string Unit
    {
        get => Get(string.Empty);
        set => Set(value);
    }

    /// <summary>
    /// 模块设的**边框色**（<c>0xAARRGGBB</c>；<see langword="null"/> = 没设 ⇒ 按主题）。
    /// </summary>
    /// <remarks>
    /// 它是**基础外观能力，不含语义**：框架不认识"红 = 失败"，颜色与它何时变都归模块的 C# 逻辑。
    /// </remarks>
    public uint? OutlineColor
    {
        get => Get<uint?>(null);
        set => Set(value);
    }

    /// <summary>
    /// 模块设的**边框角色词**（相对色；<see langword="null"/> = 没设 ⇒ 按主题）。
    /// </summary>
    /// <remarks>
    /// 与 <see cref="OutlineColor"/> 是同一键位的两种写法（写入侧互斥）：绝对值**刻意固定**，
    /// 角色词**随主题与用户改色跟随**。这里是放色值/词的地方，解析在绘制期做（见 <c>ApplyOutlineRoleCore</c>）。
    /// </remarks>
    public string? OutlineRole
    {
        get => Get<string?>(null);
        set => Set(value);
    }

    /// <summary>
    /// 模块设的**文字色**（<c>0xAARRGGBB</c>；<see langword="null"/> = 没设 ⇒ 按声明 / 主题）。
    /// </summary>
    public uint? TextColor
    {
        get => Get<uint?>(null);
        set => Set(value);
    }

    /// <summary>模块设的**文字角色词**（相对色；<see langword="null"/> = 没设）。</summary>
    public string? TextRole
    {
        get => Get<string?>(null);
        set => Set(value);
    }

    /// <summary>
    /// 模块设的**文字大小**（DIP；<see langword="null"/> = 没设 ⇒ 按声明 / 主题那一档）。
    /// </summary>
    public double? TextSize
    {
        get => Get<double?>(null);
        set => Set(value);
    }

    /// <summary>
    /// 模块设的**运行期只读**（<see langword="null"/> = 没设 ⇒ 按声明那一档）。
    /// </summary>
    /// <remarks>
    /// 与声明里的 <c>readonly</c> 分工：那个是"这一格一贯只读"，这个是"它此刻不让改"
    /// （例：自动发送进行中把「间隔」锁住）。框架不认识"为什么锁"。
    /// </remarks>
    public bool? ReadOnly
    {
        get => Get<bool?>(null);
        set => Set(value);
    }

    /// <summary>已丢弃的条目数（滚动缓冲到顶时"丢最旧"的记账；0 = 从未丢过）。</summary>
    public long Dropped
    {
        get => Get(0L);
        set => Set(value);
    }

    /// <summary>
    /// 按属性名读写取值（子类给自家属性用的通道）。
    /// </summary>
    /// <param name="fallback">没有这个键时给的缺省。</param>
    /// <param name="name">属性名（调用点用 <c>[CallerMemberName]</c> 省掉）。</param>
    /// <typeparam name="T">取值类型。</typeparam>
    /// <returns>当前取值。</returns>
    public T Get<T>(T fallback, [CallerMemberName] string? name = null)
    {
        if (name is not null && _values.TryGetValue(name, out var stored) && stored is T typed)
        {
            return typed;
        }

        return fallback;
    }

    /// <summary>写一个取值（变了才发通知）。</summary>
    /// <param name="value">新取值。</param>
    /// <param name="name">属性名（调用点用 <c>[CallerMemberName]</c> 省掉）。</param>
    /// <typeparam name="T">取值类型。</typeparam>
    public void Set<T>(T value, [CallerMemberName] string? name = null)
    {
        if (name is null)
        {
            return;
        }

        if (_values.TryGetValue(name, out var stored) && EqualityComparer<T>.Default.Equals((T)stored!, value))
        {
            return;
        }

        _values[name] = value;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }

    /// <summary>把数值按既有数字写法（最多三位小数）写成显示文本；无值 ⇒ <c>"-"</c>。</summary>
    /// <param name="value">数值。</param>
    /// <returns>显示文本。</returns>
    /// <remarks>
    /// 占位符用**半角连字符**而不是长破折号：长破折号在等宽字体（<c>FontData</c> 的首选
    /// <c>Cascadia Mono</c>）里没有字形，排版宽度会塌成一个点（实测：数值列只剩 1 px）。
    /// </remarks>
    public static string Format(double? value) => value is { } number
        ? number.ToString(NumericFormat, CultureInfo.CurrentCulture)
        : "-";

    /// <summary>数值显示的数字写法（"." = 必要的小数点，"#" = 最多三位小数）。</summary>
    public const string NumericFormat = "0.###";
}
