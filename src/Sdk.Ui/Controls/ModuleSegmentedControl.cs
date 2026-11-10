using Avalonia;
using Avalonia.Collections;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;
using Avalonia.VisualTree;
using IconControl = HYWDebugAssistant.Sdk.Ui.Controls.Icon;

namespace HYWDebugAssistant.Sdk.Ui.Controls;

/// <summary>一个互斥分段选项。</summary>
/// <remarks>
/// 记录提供无参构造与 `init` 属性，让视图可以在 XAML 的 `PreviewOptions` 里直接声明预览选项；
/// 运行期仍可继续用位置构造。
/// </remarks>
public sealed record ModuleSegmentOption
{
    /// <summary>造一个空选项（XAML 用）。</summary>
    public ModuleSegmentOption()
    {
    }

    /// <summary>造一个互斥分段选项。</summary>
    /// <param name="Key">提交给模块的稳定键。</param>
    /// <param name="Label">界面显示文本（模块 i18n）。</param>
    /// <param name="IconKey">可选图标键。</param>
    /// <param name="IsEnabled">是否可点。</param>
    /// <param name="ToolTip">悬停提示。</param>
    public ModuleSegmentOption(
        string Key,
        string Label,
        string? IconKey = null,
        bool IsEnabled = true,
        string? ToolTip = null)
    {
        this.Key = Key;
        this.Label = Label;
        this.IconKey = IconKey;
        this.IsEnabled = IsEnabled;
        this.ToolTip = ToolTip;
    }

    /// <summary>提交给模块的稳定键。</summary>
    public string Key { get; init; } = string.Empty;

    /// <summary>界面显示文本（模块 i18n）。</summary>
    public string Label { get; init; } = string.Empty;

    /// <summary>可选图标键。</summary>
    public string? IconKey { get; init; }

    /// <summary>是否可点。</summary>
    public bool IsEnabled { get; init; } = true;

    /// <summary>悬停提示。</summary>
    public string? ToolTip { get; init; }
}

/// <summary>通用互斥分段选择：文字或图标按钮，选中态唯一。</summary>
public sealed class ModuleSegmentedControl : TemplatedControl
{
    /// <summary>选项集合。</summary>
    public static readonly StyledProperty<IReadOnlyList<ModuleSegmentOption>?> OptionsProperty =
        AvaloniaProperty.Register<ModuleSegmentedControl, IReadOnlyList<ModuleSegmentOption>?>(nameof(Options));

    /// <summary>当前选中键。</summary>
    public static readonly StyledProperty<string> SelectedKeyProperty =
        AvaloniaProperty.Register<ModuleSegmentedControl, string>(nameof(SelectedKey), string.Empty);

    /// <summary>是否显示文字标签。</summary>
    public static readonly StyledProperty<bool> ShowLabelsProperty =
        AvaloniaProperty.Register<ModuleSegmentedControl, bool>(nameof(ShowLabels), true);

    /// <summary>图标尺寸。</summary>
    public static readonly StyledProperty<double> IconSizeProperty =
        AvaloniaProperty.Register<ModuleSegmentedControl, double>(nameof(IconSize), 14);

    private StackPanel? _items;
    private bool _syncing;

    static ModuleSegmentedControl()
    {
        OptionsProperty.Changed.AddClassHandler<ModuleSegmentedControl>(static (control, _) => control.Rebuild());
        SelectedKeyProperty.Changed.AddClassHandler<ModuleSegmentedControl>(static (control, _) => control.ApplySelection());
        ShowLabelsProperty.Changed.AddClassHandler<ModuleSegmentedControl>(static (control, _) => control.Rebuild());
        IconSizeProperty.Changed.AddClassHandler<ModuleSegmentedControl>(static (control, _) => control.Rebuild());
    }

    /// <summary>造一个分段控件。</summary>
    public ModuleSegmentedControl()
    {
        PreviewOptions.CollectionChanged += (_, _) =>
        {
            if (ModuleView.IsDesignEnvironment)
            {
                Rebuild();
            }
        };
    }

    /// <summary>选项集合。</summary>
    public IReadOnlyList<ModuleSegmentOption>? Options
    {
        get => GetValue(OptionsProperty);
        set => SetValue(OptionsProperty, value);
    }

    /// <summary>
    /// **预览选项**：只在设计环境、且 <see cref="Options"/> 为空时使用；运行期视图不读它。
    /// </summary>
    public AvaloniaList<ModuleSegmentOption> PreviewOptions { get; } = [];

    /// <summary>当前选中键；程序设置不触发 <see cref="OptionSelected"/>。</summary>
    public string SelectedKey
    {
        get => GetValue(SelectedKeyProperty);
        set => SetValue(SelectedKeyProperty, value ?? string.Empty);
    }

    /// <summary>是否显示文字标签。</summary>
    public bool ShowLabels
    {
        get => GetValue(ShowLabelsProperty);
        set => SetValue(ShowLabelsProperty, value);
    }

    /// <summary>图标尺寸。</summary>
    public double IconSize
    {
        get => GetValue(IconSizeProperty);
        set => SetValue(IconSizeProperty, value);
    }

    /// <summary>用户选择了某一段。</summary>
    public event EventHandler? OptionSelected;

    /// <inheritdoc />
    protected override void OnApplyTemplate(TemplateAppliedEventArgs e)
    {
        base.OnApplyTemplate(e);
        _items = e.NameScope.Find<StackPanel>("PART_Items");
        Rebuild();
    }

    /// <inheritdoc />
    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        Rebuild();
    }

    /// <summary>当前实际参与显示的选项：运行期优先，预览期才回退到 <see cref="PreviewOptions"/>。</summary>
    private IReadOnlyList<ModuleSegmentOption> EffectiveOptions
    {
        get
        {
            if (Options is { Count: > 0 } options)
            {
                return options;
            }

            return ModuleView.IsDesignEnvironment ? PreviewOptions : [];
        }
    }

    private void Rebuild()
    {
        if (_items is null)
        {
            return;
        }

        _items.Children.Clear();
        var options = EffectiveOptions;
        if (options.Count == 0)
        {
            return;
        }

        var owner = this.FindAncestorOfType<ModuleView>()?.GetType().Assembly;
        foreach (var option in options)
        {
            var content = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Spacing = 4,
                VerticalAlignment = VerticalAlignment.Center,
            };

            if (!string.IsNullOrEmpty(option.IconKey))
            {
                content.Children.Add(new IconControl
                {
                    IconKey = option.IconKey,
                    Owner = owner,
                    Width = IconSize,
                    Height = IconSize,
                    VerticalAlignment = VerticalAlignment.Center,
                });
            }

            if (ShowLabels && !string.IsNullOrEmpty(option.Label))
            {
                content.Children.Add(new ContentControl
                {
                    Content = option.Label,
                    FontSize = 11,
                    VerticalAlignment = VerticalAlignment.Center,
                });
            }

            var button = new Button
            {
                Content = content,
                IsEnabled = option.IsEnabled,
                Tag = option.Key,
                HorizontalContentAlignment = HorizontalAlignment.Center,
                VerticalContentAlignment = VerticalAlignment.Center,
            };
            button.Classes.Add("module-segment");
            if (string.Equals(option.Key, SelectedKey, StringComparison.Ordinal))
            {
                button.Classes.Add("selected");
            }

            if (!string.IsNullOrEmpty(option.ToolTip))
            {
                ToolTip.SetTip(button, option.ToolTip);
            }

            button.Click += OnSegmentClick;
            _items.Children.Add(button);
        }
    }

    private void ApplySelection()
    {
        if (_items is null)
        {
            return;
        }

        foreach (var child in _items.Children)
        {
            if (child is not Button button
                || button.Classes.Contains("module-segment") is false)
            {
                continue;
            }

            var selected = string.Equals(button.Tag as string, SelectedKey, StringComparison.Ordinal);
            button.Classes.Set("selected", selected);
        }
    }

    private void OnSegmentClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        _ = e;
        if (_syncing
            || sender is not Button button
            || button.Tag is not string key
            || !button.IsEnabled
            || string.Equals(key, SelectedKey, StringComparison.Ordinal))
        {
            return;
        }

        _syncing = true;
        SelectedKey = key;
        _syncing = false;
        OptionSelected?.Invoke(this, EventArgs.Empty);
    }
}
