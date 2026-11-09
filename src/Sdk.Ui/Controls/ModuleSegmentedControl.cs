using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.VisualTree;
using HYWDebugAssistant.Sdk.Resources;
using IconControl = HYWDebugAssistant.Sdk.Ui.Controls.Icon;

namespace HYWDebugAssistant.Sdk.Ui.Controls;

/// <summary>一个互斥分段选项。</summary>
public sealed record ModuleSegmentOption(
    string Key,
    string Label,
    string? IconKey = null,
    bool IsEnabled = true,
    string? ToolTip = null);

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

    /// <summary>选项集合。</summary>
    public IReadOnlyList<ModuleSegmentOption>? Options
    {
        get => GetValue(OptionsProperty);
        set => SetValue(OptionsProperty, value);
    }

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

    private void Rebuild()
    {
        if (_items is null)
        {
            return;
        }

        _items.Children.Clear();
        if (Options is not { Count: > 0 } options)
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
