using System.Reflection;
using Avalonia.Markup.Xaml;
using HYWDebugAssistant.Sdk.Text;

namespace HYWDebugAssistant.Sdk.Ui.Controls;

/// <summary>
/// **`{loc:Tr &lt;key&gt;}`**：把文本表里的键解析成当前语言的文案（解析一次，返回字符串）。
/// </summary>
/// <remarks>
/// <list type="bullet">
///   <item><b>属主程序集（按顺序）</b>：① <see cref="IUriContext.BaseUri"/> 的 host（`avares://&lt;程序集名&gt;/...`）——
///     **`DataTemplate` 内的扩展拿不到"这份 XAML 的根"**，而编译期给的 BaseUri 才是真正的属主；
///     ② 目标对象的程序集（表若直接挂在控件所属程序集上，这是最短路径）；③ 根对象的程序集；
///     ④ 兜底。任何一步取不到都继续往下，不让一次失败挡住后面的路径；</item>
///   <item><b>为什么跳过 `Avalonia.*`</b>：框架控件的程序集**永远没有**本应用的文本表，
///     拿它查表只会报一条"文本表目录缺失"的假诊断（`DataTemplate` 里的 `Button` / `TextBlock` 就是这种情形），
///     最终仍要落到可见缺失标记。于是这类属主不查表、直接给标记；真正的缺表诊断留给本应用自己的程序集；</item>
///   <item><b>为什么返回字符串而不是绑定</b>：语言只在启动时定一次（设置项承诺"重启完全生效"），
///     返回字符串可以落在 `Text` / `Content` / `Header` / `ToolTip.Tip` 任意属性上，且没有绑定开销；</item>
///   <item><b>为什么住这一层</b>：模块视图（`ModuleView` 的子类）在模块程序集里，框架界面在
///     `FrameworkUi`/`Ui` 里 —— 两边的 XAML 都要能用同一个扩展，而它们共同依赖的只有 SDK 界面契约层
///     （`Sdk.Ui`）；放在 `Ui` 里会让模块与框架都反向依赖 L5。</item>
///   <item>四处都找不到 ⇒ <see cref="TextCatalog.MissingValue"/> 的**可见缺失标记**，绝不返回空白。</item>
/// </list>
/// </remarks>
public sealed class TrExtension : MarkupExtension
{
    /// <summary>框架程序集的简名前缀（这些程序集没有也不该有本应用的文本表）。</summary>
    private const string FrameworkAssemblyPrefix = "Avalonia.";

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

        // ① URI 的 host：`DataTemplate` 里的扩展只有这条路能拿到真正的属主。
        var uriAssembly = TryResolveUriAssembly(serviceProvider);
        if (TryGetOwnedText(uriAssembly, Key, out var uriText))
        {
            return uriText;
        }

        // ② 目标对象自己的程序集
        var targetAssembly = target?.GetType().Assembly;
        if (TryGetOwnedText(targetAssembly, Key, out var targetText))
        {
            return targetText;
        }

        // ③ 根对象：`{loc:Tr}` 落在框架控件上，表的属主是这份 XAML 的 UserControl / Window
        Assembly? rootAssembly = null;
        if (serviceProvider.GetService(typeof(IRootObjectProvider)) is IRootObjectProvider roots)
        {
            var root = roots.RootObject ?? roots.IntermediateRootObject;
            if (root is not null)
            {
                rootAssembly = root.GetType().Assembly;

                if (TryGetOwnedText(rootAssembly, Key, out var rootText))
                {
                    return rootText;
                }
            }
        }

        // ④ 兜底：框架程序集不查表（否则只会得到一条假"目录缺失"），其余照旧保留真实缺表诊断。
        var owner = uriAssembly ?? targetAssembly ?? rootAssembly;

        return owner is null || IsFrameworkAssembly(owner)
            ? TextCatalog.MissingValue(Key)
            : TextCatalog.Get(owner, Key);
    }

    /// <summary>只在**属于本应用**的程序集上查表（框架程序集没有表，查了只会产生假诊断）。</summary>
    /// <param name="owner">候选属主程序集（可为空）。</param>
    /// <param name="key">文本表键。</param>
    /// <param name="text">命中的文案。</param>
    /// <returns>是否命中。</returns>
    private static bool TryGetOwnedText(Assembly? owner, string key, out string text)
    {
        text = string.Empty;
        return owner is not null
            && !IsFrameworkAssembly(owner)
            && TextCatalog.TryGet(owner, key, out text);
    }

    /// <summary>把 `IUriContext.BaseUri` 的 host 解析成程序集（`avares://&lt;程序集简名&gt;/...`）。</summary>
    /// <param name="serviceProvider">XAML 服务。</param>
    /// <returns>解析到的程序集；没有 URI / 不是 avares / 解析失败 ⇒ <see langword="null"/>。</returns>
    /// <remarks>只按已加载的程序集名匹配，失败再按名加载一次——两条都失败就跳过，不影响后面的路径。</remarks>
    private static Assembly? TryResolveUriAssembly(IServiceProvider serviceProvider)
    {
        if (serviceProvider.GetService(typeof(IUriContext)) is not IUriContext { BaseUri: { IsAbsoluteUri: true } baseUri }
            || !string.Equals(baseUri.Scheme, "avares", StringComparison.OrdinalIgnoreCase)
            || baseUri.Host.Length == 0)
        {
            return null;
        }

        var name = baseUri.Host;

        try
        {
            // 已加载的程序集优先：编译期给出的 host 一般就是当前程序，不需要再走一次按名加载。
            foreach (var loaded in AppDomain.CurrentDomain.GetAssemblies())
            {
                if (string.Equals(loaded.GetName().Name, name, StringComparison.OrdinalIgnoreCase))
                {
                    return loaded;
                }
            }

            return Assembly.Load(new AssemblyName(name));
        }
        catch (Exception exception) when (exception is ArgumentException
                                              or FileNotFoundException
                                              or FileLoadException
                                              or BadImageFormatException
                                              or IOException)
        {
            return null;
        }
    }

    /// <summary>程序集是不是框架自己的（没有也不该有本应用的文本表）。</summary>
    /// <param name="assembly">候选程序集。</param>
    private static bool IsFrameworkAssembly(Assembly assembly) =>
        assembly.GetName().Name is { } name
        && name.StartsWith(FrameworkAssemblyPrefix, StringComparison.Ordinal);
}
