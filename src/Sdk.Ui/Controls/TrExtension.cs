using System.Reflection;
using Avalonia.Markup.Xaml;
using HYWDebugAssistant.Sdk.Text;

namespace HYWDebugAssistant.Sdk.Ui.Controls;

/// <summary>
/// **`{loc:Tr &lt;key&gt;}`**：把文本表里的键解析成当前语言的文案（解析一次，返回字符串）。
/// </summary>
/// <remarks>
/// <list type="bullet">
///   <item><b>属主程序集</b>：先看 <see cref="IProvideValueTarget.TargetObject"/> 的程序集里有没有这条键。
///     目标对象一般是**框架控件**（`TextBlock` / `TabItem` / `UiToggle`），它们所属的程序集里没有表，
///     于是回退到 <see cref="IRootObjectProvider"/> 的根对象 —— 那才是这份 XAML 的属主程序集；</item>
///   <item><b>为什么返回字符串而不是绑定</b>：语言只在启动时定一次（设置项承诺"重启完全生效"），
///     返回字符串可以落在 `Text` / `Content` / `Header` / `ToolTip.Tip` 任意属性上，且没有绑定开销；</item>
///   <item><b>为什么住这一层</b>：模块视图（`ModuleView` 的子类）在模块程序集里，框架界面在
///     `FrameworkUi`/`Ui` 里 —— 两边的 XAML 都要能用同一个扩展，而它们共同依赖的只有 SDK 界面契约层
///     （`Sdk.Ui`）；放在 `Ui` 里会让模块与框架都反向依赖 L5。</item>
///   <item>两处都找不到 ⇒ <see cref="TextCatalog.Get"/> 的**可见缺失标记**，绝不返回空白。</item>
/// </list>
/// </remarks>
public sealed class TrExtension : MarkupExtension
{
    /// <summary>创建文本扩展（键由 XAML 的 `Key=` 或位置参数给出）。</summary>
    public TrExtension()
    {
    }

    /// <summary>创建文本扩展并指定键。</summary>
    /// <param name="key">文本表里的键。</param>
    public TrExtension(string key) => Key = key;

    /// <summary>文本表里的键（<c>area.group.element.role</c>）。</summary>
    public string Key { get; set; } = string.Empty;

    /// <inheritdoc />
    public override object ProvideValue(IServiceProvider serviceProvider)
    {
        ArgumentNullException.ThrowIfNull(serviceProvider);

        var target = serviceProvider.GetService(typeof(IProvideValueTarget)) is IProvideValueTarget provideValue
            ? provideValue.TargetObject
            : null;

        // ① 目标对象自己的程序集（表若就直接挂在控件所属程序集上，这是最短路径）
        if (target is not null && TextCatalog.TryGet(target.GetType().Assembly, Key, out var targetText))
        {
            return targetText;
        }

        // ② 根对象：`{loc:Tr}` 落在框架控件上，表的属主是这份 XAML 的 UserControl / Window
        Assembly? rootAssembly = null;
        if (serviceProvider.GetService(typeof(IRootObjectProvider)) is IRootObjectProvider roots)
        {
            var root = roots.RootObject ?? roots.IntermediateRootObject;
            if (root is not null)
            {
                rootAssembly = root.GetType().Assembly;

                if (TextCatalog.TryGet(rootAssembly, Key, out var rootText))
                {
                    return rootText;
                }
            }
        }

        // ③ 兜底：仍然走同一个入口，拿到的就是可见缺失标记（带键名，可定位）
        return TextCatalog.Get(target?.GetType().Assembly ?? rootAssembly ?? typeof(TrExtension).Assembly, Key);
    }
}
