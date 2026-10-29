using Avalonia;
using Avalonia.Controls;
using Avalonia.Markup.Xaml.MarkupExtensions;
using Avalonia.Media;
using Avalonia.VisualTree;

namespace HYWDebugAssistant.Sdk.Ui.Controls;

/// <summary>
/// **可被框架按帧驱动的模块视图元素**。
/// </summary>
/// <remarks>
/// 绝大多数元件派生自 <see cref="ModuleElement"/>；少数必须继承别的控件（例如页签要继承
/// <see cref="TabControl"/>）时实现本接口即可。两条纪律不变：**无变化不碰视觉**、**刷新不重建对象**。
/// </remarks>
public interface IModuleViewElement
{
    /// <summary>按帧刷新一次（框架调用；实现方自己判断有没有变化）。</summary>
    /// <param name="context">取数入口。</param>
    void Refresh(IModuleViewContext context);
}

/// <summary>
/// **模块界面元件的底座**：一个"绑到某个名字上"的控件，按帧刷新、并承接运行期外观。
/// </summary>
/// <remarks>
/// <para>
/// 它承担两件在这一层**必须统一**的事：
/// </para>
/// <list type="number">
///   <item><b>刷新</b>：每帧由 <see cref="ModuleViewHost"/> 调一次 <see cref="Refresh"/>，
///     控件自己决定"值变了没有、要不要重画"（无变化就一个像素都不动）。</item>
///   <item><b>运行期外观</b>：模块可以按源名设边框色 / 文字色 / 字号 / 只读，
///     落点**只有这里一处**（<see cref="ApplyAppearance"/>）——
///     这就是"外观通道不被散落成第二套实现"的落点。</item>
/// </list>
/// <para>
/// <b>只覆盖本库的控件</b>：模块自写的裸控件不走这条通道（它自己负责自己的外观）。
/// 这条边界写在契约上，比"到处兜底"可预测得多。
/// </para>
/// </remarks>
public abstract class ModuleElement : ContentControl, IModuleViewElement
{
    /// <summary>本元件绑定的名字（显示源名 / 输入源名 / 字段名；空 = 不绑）。</summary>
    /// <remarks>它同时是**运行期外观的寻址键** —— 模块说的"这一格现在描红"就是按这个名字找到它。</remarks>
    public string Source { get; set; } = string.Empty;

    /// <summary>
    /// 每帧刷新一次。
    /// </summary>
    /// <param name="context">取数入口。</param>
    /// <remarks>
    /// 默认什么都不做：只有**数据驱动**的元件才重写它。每个实现都必须做到
    /// "值没变就不碰视觉" —— 否则每帧重排会打断用户正在做的事（焦点、选择、悬停）。
    /// </remarks>
    public virtual void Refresh(IModuleViewContext context)
    {
        // 默认：纯静态元件（说明文字、分隔件）。
    }

    /// <summary>
    /// 把模块设的外观落到**画得出来的那一块**上（默认：本元件的边框与前景）。
    /// </summary>
    /// <param name="appearance">外观（全空 ⇒ 交还主题）。</param>
    /// <remarks>
    /// 自带边框/文字的元件覆盖本方法、把颜色画到内层去 ——
    /// 画在外层会让"看起来像多了一圈"（内外两条边）。**判据永远是：用户看得见的那一圈是谁画的。**
    /// </remarks>
    protected internal virtual void ApplyAppearance(ViewAppearance appearance)
    {
        ArgumentNullException.ThrowIfNull(appearance);

        if (appearance.Outline is { } outline)
        {
            BorderBrush = new SolidColorBrush(Color.FromUInt32(outline));
        }
        else
        {
            SetTokenBrush(this, BorderBrushProperty, appearance.OutlineToken);
        }

        if (appearance.Text is { } text)
        {
            Foreground = new SolidColorBrush(Color.FromUInt32(text));
        }
        else
        {
            SetTokenBrush(this, ForegroundProperty, appearance.TextToken);
        }

        ApplyTextSize(appearance.TextSize);
        ApplyReadOnly(appearance.ReadOnly is true);
        ApplyEnabled(appearance.Enabled);
    }

    /// <summary>把运行期启用状态落到本元件（只在实际设置时改，null 不干预）。</summary>
    /// <param name="enabled">启用状态；<see langword="null"/> = 不干预。</param>
    protected void ApplyEnabled(bool? enabled)
    {
        if (enabled is { } value)
        {
            IsEnabled = value;
        }
    }

    /// <summary>把文字大小落到真正显示文字的那一块（默认：本元件继承值）。</summary>
    /// <param name="size">字号（DIP）；<see langword="null"/> = 交还主题。</param>
    protected virtual void ApplyTextSize(double? size)
    {
        if (size is { } value)
        {
            FontSize = value;
        }
        else
        {
            ClearValue(FontSizeProperty);
        }
    }

    /// <summary>把运行期只读落到编辑器上（默认：不处理，静态元件没有可编辑内容）。</summary>
    /// <param name="isReadOnly">是否只读。</param>
    /// <remarks>
    /// **只读 ≠ 禁用**：禁用会让文字无法选中、焦点被夺走。输入类控件覆盖本方法，
    /// 把"不让改"实现为编辑器的只读态，而不是把整个控件置灰。
    /// </remarks>
    protected virtual void ApplyReadOnly(bool isReadOnly)
    {
    }

    /// <summary>把宿主解析好的 token 落成动态资源；token 为空 ⇒ 清除本地值、交还主题。</summary>
    /// <param name="target">要设属性的对象。</param>
    /// <param name="property">属性（边框/前景）。</param>
    /// <param name="token">主题 token 名（由宿主色板契约解析）。</param>
    protected static void SetTokenBrush(AvaloniaObject target, AvaloniaProperty<IBrush?> property, string? token)
    {
        if (token is { Length: > 0 })
        {
            target[!property] = new DynamicResourceExtension(token);
            return;
        }

        target.ClearValue(property);
    }
}

/// <summary>
/// **模块视图的宿主**：把模块造的视图挂起来，并按帧驱动刷新与外观。
/// </summary>
/// <remarks>
/// <para>
/// 它是"框架这边唯一碰模块视图的地方"：模块只交一个 <see cref="Control"/>，
/// 之后每帧的 <see cref="Refresh"/> 由框架调 —— 模块不需要（也不该）自己起定时器。
/// </para>
/// <para>
/// <b>一次遍历做两件事</b>：先让每个元件刷新自己的值，再把模块设的外观落下去。
/// 两件事各只有一处实现，于是"改了值没画出来""外观没跟上"这类问题只有一个地方可查。
/// </para>
/// </remarks>
public sealed class ModuleViewHost : ContentControl
{
    private readonly IModuleViewContext _context;

    /// <summary>造一个视图宿主。</summary>
    /// <param name="context">取数 / 提交 / 外观入口。</param>
    /// <param name="view">模块造的视图根。</param>
    public ModuleViewHost(IModuleViewContext context, Control view)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(view);

        _context = context;
        Content = view;

        // **模块视图必须填满宿主**：`ContentControl` 的 Fluent 默认内容对齐是左上，
        // 内容有多宽就只给多宽 —— 视图根里那些 `*` 列/行因此拿不到剩余空间，
        // 表现为"右边一截空白、最右的读数被裁掉"（不是控件自己的布局错）。
        HorizontalContentAlignment = Avalonia.Layout.HorizontalAlignment.Stretch;
        VerticalContentAlignment = Avalonia.Layout.VerticalAlignment.Stretch;

        // 视图根拿到上下文：模块视图可以按普通绑定取用（本库控件则经宿主驱动）。
        DataContext = context;
    }

    /// <summary>本视图的上下文。</summary>
    public IModuleViewContext Context => _context;

    /// <summary>驱动一帧（框架每帧调一次）。</summary>
    public void Refresh()
    {
        ApplyAppearance(Content as Control);
        RefreshElements(Content as Control);
    }

    /// <summary>
    /// 把外观落到整棵子树上（**先于**刷新：外观是"框架说的话"，不该被元件自己的刷新覆盖）。
    /// </summary>
    /// <param name="root">视图根。</param>
    private void ApplyAppearance(Control? root)
    {
        foreach (var element in ModuleElements(root))
        {
            if (element.Source.Length == 0)
            {
                continue;
            }

            element.ApplyAppearance(_context.Appearance(element.Source));
        }
    }

    /// <summary>让每个元件按自己的值刷新一次。</summary>
    /// <param name="root">视图根。</param>
    private void RefreshElements(Control? root)
    {
        foreach (var element in Elements(root))
        {
            element.Refresh(_context);
        }
    }

    /// <summary>视图里的全部模块界面元件（含根自身）。</summary>
    /// <param name="root">视图根。</param>
    private static IEnumerable<IModuleViewElement> Elements(Control? root)
    {
        if (root is null)
        {
            yield break;
        }

        if (root is IModuleViewElement self)
        {
            yield return self;
        }

        foreach (var descendant in root.GetVisualDescendants().OfType<IModuleViewElement>())
        {
            yield return descendant;
        }
    }

    /// <summary>视图里**承接运行期外观**的元件（含根自身）。</summary>
    /// <param name="root">视图根。</param>
    private static IEnumerable<ModuleElement> ModuleElements(Control? root)
    {
        if (root is null)
        {
            yield break;
        }

        if (root is ModuleElement self)
        {
            yield return self;
        }

        foreach (var descendant in root.GetVisualDescendants().OfType<ModuleElement>())
        {
            yield return descendant;
        }
    }
}
