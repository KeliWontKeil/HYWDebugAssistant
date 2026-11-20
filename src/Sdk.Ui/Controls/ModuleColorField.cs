using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml.MarkupExtensions;
using Avalonia.Media;
using Avalonia.VisualTree;
using HYWDebugAssistant.Sdk.Ui;

namespace HYWDebugAssistant.Sdk.Ui.Controls;

/// <summary>
/// **颜色值编辑器**：一个色块按钮，点开框架统一取色窗，确认即交值。
/// </summary>
/// <remarks>
/// <para>
/// 它只承担“颜色”这一种值：值名来自 <see cref="Value"/>，颜色文本的解析/格式化走
/// <see cref="ColorText"/>，取色窗由 <see cref="IModuleViewContext.ColorPicker"/> 提供。
/// 不做文本输入、不做透明度编辑器，也不自建色板。
/// </para>
/// <para>
/// 值为空（或非法）时显示 <see cref="DefaultBrushToken"/> 指向的主题色块；用户选过色后显示固定 ARGB。
/// 取色窗点“恢复”交空串，值层因此回到主题默认。
/// </para>
/// </remarks>
public sealed class ModuleColorField : Button, IModuleViewElement
{
    private IModuleViewContext? _context;
    private string _value = string.Empty;
    private string _lastValue = string.Empty;
    private string _lastAppliedKey = "\0";
    private string _defaultBrushToken = "SurfaceControlBrush";

    /// <summary>造一个颜色块（尺寸/内边距由 XAML 给，控件不设最小尺寸）。</summary>
    public ModuleColorField()
    {
        MinWidth = 0;
        MinHeight = 0;
        Padding = default;
        BorderThickness = new Thickness(1);
        CornerRadius = new CornerRadius(2);
        HorizontalContentAlignment = Avalonia.Layout.HorizontalAlignment.Stretch;
        VerticalContentAlignment = Avalonia.Layout.VerticalAlignment.Stretch;

        Click += OnClick;
        DataContextChanged += (_, _) => Load();
        AttachedToVisualTree += (_, _) => Load();
    }

    /// <summary>绑定的颜色值名。</summary>
    public string Value
    {
        get => _value;
        set
        {
            var normalized = value ?? string.Empty;
            if (string.Equals(_value, normalized, StringComparison.Ordinal))
            {
                return;
            }

            _value = normalized;
            _lastAppliedKey = "\0";
            Load();
        }
    }

    /// <summary>设计预览色（空 = 用 <see cref="DefaultBrushToken"/>）。</summary>
    public string InitialColor { get; set; } = string.Empty;

    /// <summary>值为空时显示色块的主题 token（空 = 透明）。</summary>
    public string DefaultBrushToken
    {
        get => _defaultBrushToken;
        set
        {
            var normalized = value ?? string.Empty;
            if (string.Equals(_defaultBrushToken, normalized, StringComparison.Ordinal))
            {
                return;
            }

            _defaultBrushToken = normalized;
            _lastAppliedKey = "\0";
            Load();
        }
    }

    /// <summary>取色窗标题（空 = 实现给默认标题）。</summary>
    public string DialogTitle { get; set; } = string.Empty;

    /// <summary>取色窗“恢复”按钮文案（空 = 不显示恢复按钮）。</summary>
    public string ResetText { get; set; } = string.Empty;

    /// <inheritdoc />
    public void Refresh(IModuleViewContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        _context = context;
        Load();
    }

    /// <summary>读一次值状态并刷新色块；没有上下文时用设计预览色。</summary>
    private void Load()
    {
        var text = InitialColor ?? string.Empty;
        _context ??= DataContext as IModuleViewContext;

        if (_context is { } context && Value.Length > 0)
        {
            var state = context.ReadValue(Value);
            if (state.IsKnown)
            {
                text = state.Value;
                IsEnabled = !state.IsReadOnly && state.IsEnabled;
            }
        }

        Apply(text);
    }

    /// <summary>把颜色文本落到色块；空/非法回主题色。</summary>
    /// <param name="text">颜色文本。</param>
    private void Apply(string? text)
    {
        var normalized = text ?? string.Empty;
        var key = normalized + "\0" + _defaultBrushToken;
        if (string.Equals(key, _lastAppliedKey, StringComparison.Ordinal))
        {
            return;
        }

        _lastAppliedKey = key;
        _lastValue = normalized;

        if (ColorText.TryParseArgb(normalized, out var argb))
        {
            Background = new SolidColorBrush(Color.FromUInt32(argb));
            return;
        }

        if (DefaultBrushToken.Length > 0)
        {
            this[!BackgroundProperty] = new DynamicResourceExtension(DefaultBrushToken);
        }
        else
        {
            ClearValue(BackgroundProperty);
        }
    }

    /// <summary>取用户此刻**看见的颜色**作为取色窗初值。</summary>
    /// <remarks>
    /// 空值/非法值时色块显示的是 <see cref="DefaultBrushToken"/> 解析后的画刷；
    /// 这里直接读 <c>Background</c>，不重新查资源 —— 面板外壳在独立窗口里，
    /// 只靠 <c>TryFindResource</c> 可能拿不到主题里已经解析好的画刷，最终把初值丢成灰色。
    /// </remarks>
    private uint? ResolveDisplayedArgb()
    {
        if (Background is ISolidColorBrush displayed)
        {
            return ColorText.ToArgb(displayed.Color);
        }

        if (DefaultBrushToken.Length == 0
            || !this.TryFindResource(DefaultBrushToken, out var resource)
            || resource is not ISolidColorBrush brush)
        {
            return null;
        }

        return ColorText.ToArgb(brush.Color);
    }

    /// <summary>取色窗初值：当前值 → 设计预览色 → 色块实际显示色。</summary>
    private uint? ResolvePickerInitial()
    {
        var text = _lastValue;
        if (_context is { } context && Value.Length > 0)
        {
            var state = context.ReadValue(Value);
            if (state.IsKnown)
            {
                text = state.Value;
            }
        }

        if (ColorText.TryParseArgb(text, out var current))
        {
            return current;
        }

        if (ColorText.TryParseArgb(InitialColor, out var preview))
        {
            return preview;
        }

        return ResolveDisplayedArgb();
    }

    private async void OnClick(object? sender, RoutedEventArgs e)
    {
        _ = sender;
        _ = e;

        if (ModuleView.IsDesignEnvironment || Value.Length == 0)
        {
            return;
        }

        _context ??= this.FindAncestorOfType<ModuleViewHost>()?.Context;
        if (_context?.ColorPicker is not { } picker)
        {
            return;
        }

        var initial = ResolvePickerInitial();

        var owner = TopLevel.GetTopLevel(this) as Window;
        ModuleColorPickResult result;
        try
        {
            result = await picker.PickAsync(owner, initial, DialogTitle, ResetText);
        }
        catch (Exception)
        {
            // 取色窗失败不应打断设置面板。
            return;
        }

        if (!result.Confirmed)
        {
            return;
        }

        var text = result.Reset ? string.Empty : ColorText.FormatArgb(result.ColorArgb);
        Apply(text);
        _context.CommitValue(Value, text);
    }
}
