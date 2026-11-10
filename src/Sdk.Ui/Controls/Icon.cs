using System.Reflection;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Media;
using HYWDebugAssistant.Sdk.Resources;
using Path = Avalonia.Controls.Shapes.Path;

namespace HYWDebugAssistant.Sdk.Ui.Controls;

/// <summary>
/// **一个图标**：<see cref="IconKey"/> 是键、<see cref="Owner"/> 是图标表的拥有者，
/// 图形来自 `&lt;owner&gt;/icons.json` 指向的 `.svg`（见 <see cref="IconCatalog"/>）。
/// </summary>
/// <remarks>
/// <list type="bullet">
///   <item><b>画的是几何</b>：模板里 `Viewbox` 包一层按 `viewBox` 尺寸的 `Canvas`，里面两条 `Path`
///     （填充 / 仅描边）；路径数据由 <see cref="IconCatalog"/> 解析出的字符串在这里合成 `GeometryGroup`——
///     `Sdk` 零依赖、不能引 Avalonia，所以"数据 → 几何"的合成只能留在界面层；</item>
///   <item><b>填充色/线条色跟随 <see cref="TemplatedControl.Foreground"/></b>：外壳/模块按钮的
///     `:pointerover` / `:pressed` 换前景色时图标自动跟着走，不需要每个按钮另写一条；
///     所有 path 的有效填充为 `none` 的资产按线条模式绘制（不填充、统一描边宽度）；</item>
///   <item><b>缺键 / 缺表显示键名</b>（界面字体）并由 <see cref="IconCatalog.Diagnostics"/> 报一条诊断——
///     宁可界面上露出缺的是哪个键，也不要一块看不见的空白；</item>
///   <item><b>尺寸由使用方给</b>：控件本身只有 16×16 的默认值，具体大小靠样式
///     （活动栏 16、分类 13、预览占位 18 等）。</item>
/// </list>
/// </remarks>
public class Icon : TemplatedControl
{
    /// <summary>图标键（见 `ShellIcons` / 模块 `ViewKeys`）。</summary>
    public static readonly StyledProperty<string> IconKeyProperty =
        AvaloniaProperty.Register<Icon, string>(nameof(IconKey), string.Empty);

    /// <summary>图标表的拥有者程序集（可空；空 = 本控件所在程序集，通常没有图标表 ⇒ 显示键名）。</summary>
    public static readonly StyledProperty<Assembly?> OwnerProperty =
        AvaloniaProperty.Register<Icon, Assembly?>(nameof(Owner));

    private Viewbox? _svg;
    private Canvas? _canvas;
    private Path? _path;
    private Path? _strokePath;
    private TextBlock? _fallback;

    static Icon()
    {
        IconKeyProperty.Changed.AddClassHandler<Icon>(static (icon, _) => icon.Apply());
        OwnerProperty.Changed.AddClassHandler<Icon>(static (icon, _) => icon.Apply());
    }

    /// <summary>图标键（空 = 不画任何东西）。</summary>
    public string IconKey
    {
        get => GetValue(IconKeyProperty);
        set => SetValue(IconKeyProperty, value);
    }

    /// <summary>图标表的拥有者程序集（见 <see cref="IconCatalog"/>）。</summary>
    public Assembly? Owner
    {
        get => GetValue(OwnerProperty);
        set => SetValue(OwnerProperty, value);
    }

    /// <inheritdoc />
    protected override void OnApplyTemplate(TemplateAppliedEventArgs e)
    {
        base.OnApplyTemplate(e);

        _svg = e.NameScope.Find<Viewbox>("PART_Svg");
        _canvas = e.NameScope.Find<Canvas>("PART_Canvas");
        _path = e.NameScope.Find<Path>("PART_Path");
        _strokePath = e.NameScope.Find<Path>("PART_StrokePath");
        _fallback = e.NameScope.Find<TextBlock>("PART_Fallback");

        Apply();
    }

    /// <inheritdoc />
    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        Apply();
    }

    private void Apply()
    {
        // 模板还没套上时属性变化先记着，`OnApplyTemplate` 会再调一次。
        if (_svg is null || _canvas is null || _path is null || _fallback is null)
        {
            return;
        }

        var key = IconKey;

        if (string.IsNullOrEmpty(key))
        {
            HidePaths();
            _svg.IsVisible = false;
            _fallback.IsVisible = false;
            return;
        }

        var owner = Owner ?? typeof(Icon).Assembly;

        if (IconCatalog.TryGet(owner, key, out var asset) && asset is not null)
        {
            var group = new GeometryGroup
            {
                FillRule = asset.EvenOdd ? FillRule.EvenOdd : FillRule.NonZero,
            };

            foreach (var data in asset.PathData)
            {
                group.Children.Add(StreamGeometry.Parse(data));
            }

            // 线条资产只描边；混合/填充资产保持原“填充 + 细描边”观感。
            var strokeOnly = asset.StrokeOnly && _strokePath is not null;
            _path!.Data = strokeOnly ? null : group;
            _path.IsVisible = !strokeOnly;
            if (_strokePath is not null)
            {
                _strokePath.Data = strokeOnly ? group : null;
                _strokePath.IsVisible = strokeOnly;
            }

            _canvas!.Width = asset.ViewBoxWidth;
            _canvas.Height = asset.ViewBoxHeight;
            _svg.IsVisible = true;
            _fallback!.IsVisible = false;
            return;
        }

        HidePaths();
        _svg.IsVisible = false;
        _fallback!.Text = IconCatalog.MissingValue(key);
        _fallback.IsVisible = true;
    }

    private void HidePaths()
    {
        if (_path is not null)
        {
            _path.Data = null;
            _path.IsVisible = false;
        }

        if (_strokePath is not null)
        {
            _strokePath.Data = null;
            _strokePath.IsVisible = false;
        }
    }
}
