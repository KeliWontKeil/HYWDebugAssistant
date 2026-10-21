using System.Reflection;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Markup.Xaml.Styling;
using Avalonia.Styling;

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
    /// 当前进程是否是 **XAML 预览宿主**（`tools/PreviewHost`）。
    /// </summary>
    /// <remarks>
    /// 预览器通常会把 <see cref="Design.IsDesignMode"/> 置真；但部分宿主/会话不保证。
    /// 预览宿主的 Application 显式置位本开关，于是设计主题与配置上下文只由"谁在预览"决定，
    /// 不再依赖某个框架布尔值是否被设置。产品 App 不置位。
    /// </remarks>
    public static bool IsPreviewHost { get; set; }

    private static bool IsDesignEnvironment => Design.IsDesignMode || IsPreviewHost;

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
            // 由视图上的 `[ModuleViewDesignData]` 指定配置类型，从同一份标注反射出字段。
            if (GetType().GetCustomAttribute<ModuleViewDesignDataAttribute>() is { } designData
                && DataContext is null)
            {
                DataContext = new DesignModuleViewContext(designData.ConfigType);
            }
        }
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
