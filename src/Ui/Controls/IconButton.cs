using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;
using IconControl = HYWDebugAssistant.Sdk.Ui.Controls.Icon;

namespace HYWDebugAssistant.Ui.Controls;

/// <summary>
/// **图标按钮**：<see cref="Icon"/> 只是一个键，图形从**本程序集自己的**图标表取
/// （`resources/icons/HYWDebugAssistant.Ui/`，见 <see cref="IconControl"/> 与 `IconCatalog`）。
/// </summary>
/// <remarks>
/// <list type="bullet">
///   <item>命中 ⇒ `Content` 是一个 <see cref="IconControl"/>（图形是显式 SVG，不是字体码位）；</item>
///   <item>缺键 / 缺表 ⇒ <see cref="IconControl"/> 自己显示**键名**并报诊断——界面上看得见缺的是哪个键；</item>
///   <item>空 ⇒ 文本模式：`Content` 由使用方给，本类不碰；</item>
///   <item>面 / 边 / 五态由 `ButtonChrome.axaml` 与 `ui|IconButton.icon` 样式给；
///     图标前景色绑定到按钮 Foreground ⇒ 指针/按下/danger/accent 与文字一起变。</item>
/// </list>
/// <para>
/// <see cref="Icon"/> 是 <see cref="AvaloniaProperty"/> 而不是普通属性：外壳里有 `{Binding …}` 的用法，
/// 必须能收到属性变更通知。
/// </para>
/// </remarks>
public sealed class IconButton : Button
{
    /// <summary>图标键（见 <see cref="ShellIcons"/>）。</summary>
    public static readonly StyledProperty<string> IconProperty =
        AvaloniaProperty.Register<IconButton, string>(nameof(Icon), string.Empty);

    static IconButton()
    {
        IconProperty.Changed.AddClassHandler<IconButton>(static (button, _) => button.ApplyIcon());
    }

    /// <summary>造一个图标按钮（内容居中；尺寸与五态由 `Button.icon` 样式给）。</summary>
    public IconButton()
    {
        HorizontalContentAlignment = HorizontalAlignment.Center;
        VerticalContentAlignment = VerticalAlignment.Center;
    }

    /// <summary>图标键（空 = 文本模式）。</summary>
    public string Icon
    {
        get => GetValue(IconProperty);
        set => SetValue(IconProperty, value);
    }

    /// <inheritdoc />
    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        ApplyIcon();
    }

    private void ApplyIcon()
    {
        var key = Icon;

        if (string.IsNullOrEmpty(key))
        {
            // 文本模式：`Content` 由使用方给。
            return;
        }

        var icon = new IconControl
        {
            IconKey = key,
            Owner = typeof(IconButton).Assembly,
        };

        // 图标与按钮文字共用同一个 Foreground；danger/accent 或指针态不再各写一套颜色。
        icon[!TemplatedControl.ForegroundProperty] =
            this[!TemplatedControl.ForegroundProperty];

        Content = icon;
    }
}
