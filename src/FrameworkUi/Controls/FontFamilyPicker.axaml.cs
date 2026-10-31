using System.Collections.ObjectModel;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Data;
using Avalonia.Layout;
using Avalonia.Media;
using HYWDebugAssistant.Sdk.Text;

namespace HYWDebugAssistant.FrameworkUi.Controls;

/// <summary>
/// **字体选择器**：原生可编辑 <see cref="ComboBox"/> + 字体预览项；允许输入列表外的字体名。
/// </summary>
/// <remarks>
/// <list type="bullet">
///   <item><b>外观一致性</b>：直接继承主题里的 ComboBox 模板，箭头、边框、焦点环、弹层与设置页其他
///     下拉框是同一套，不再自建 TextBox + ToggleButton + Popup；</item>
///   <item><b>候选项</b>：<see cref="Options"/> 来自设置视图模型；下拉项用该项自己的字体渲染名字 + 样例，
///     关合态只显示字体名（<c>SelectionBoxItemTemplate</c>）；</item>
///   <item><b>输入</b>：可编辑 ComboBox 的原生文本通道；文本落到 <see cref="SelectedFont"/>，
///     列表外名字原样保留；弹层打开时按文本收窄候选，关闭后恢复完整列表。</item>
/// </list>
/// </remarks>
public partial class FontFamilyPicker : ComboBox
{
    /// <summary>当前字体名（与 ComboBox 文本/选中项双向同步；允许列表外的名字）。</summary>
    public static readonly StyledProperty<string?> SelectedFontProperty =
        AvaloniaProperty.Register<FontFamilyPicker, string?>(
            nameof(SelectedFont),
            defaultBindingMode: BindingMode.TwoWay);

    /// <summary>候选项（字体名列表；由设置视图模型提供，控件不自己枚举系统字体）。</summary>
    public static readonly StyledProperty<IReadOnlyList<string>?> OptionsProperty =
        AvaloniaProperty.Register<FontFamilyPicker, IReadOnlyList<string>?>(nameof(Options));

    private readonly List<string> _allOptions = [];
    private readonly ObservableCollection<string> _filteredOptions = [];
    private bool _syncing;

    static FontFamilyPicker()
    {
        SelectedFontProperty.Changed.AddClassHandler<FontFamilyPicker>((picker, _) => picker.OnSelectedFontChanged());
        OptionsProperty.Changed.AddClassHandler<FontFamilyPicker>((picker, _) => picker.RebuildOptions());
    }

    /// <summary>创建字体选择器。</summary>
    public FontFamilyPicker()
    {
        IsEditable = true;
        MaxDropDownHeight = 240;
        ItemsSource = _filteredOptions;
        ItemTemplate = BuildItemTemplate();
        SelectionBoxItemTemplate = BuildSelectionTemplate();
        PlaceholderText = Loc("appearance.font.picker.placeholder");

        DropDownOpened += (_, _) => ResetFilter();
        DropDownClosed += (_, _) => ResetFilter();
    }

    /// <inheritdoc />
    protected override Type StyleKeyOverride => typeof(ComboBox);

    /// <inheritdoc />
    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);

        if (change.Property == SelectedItemProperty)
        {
            OnSelectedItemChanged();
        }
        else if (change.Property == TextProperty)
        {
            OnTextChanged();
        }
    }

    /// <summary>当前字体名（与 ComboBox 文本/选中项双向同步）。</summary>
    public string? SelectedFont
    {
        get => GetValue(SelectedFontProperty);
        set => SetValue(SelectedFontProperty, value);
    }

    /// <summary>候选项（字体名列表）。</summary>
    public IReadOnlyList<string>? Options
    {
        get => GetValue(OptionsProperty);
        set => SetValue(OptionsProperty, value);
    }

    private void OnSelectedFontChanged()
    {
        if (_syncing)
        {
            return;
        }

        _syncing = true;
        try
        {
            var value = SelectedFont ?? string.Empty;
            if (!string.Equals(Text, value, StringComparison.Ordinal))
            {
                SetCurrentValue(TextProperty, value);
            }

            SetCurrentValue(
                SelectedItemProperty,
                _allOptions.Contains(value, StringComparer.Ordinal) ? value : null);
        }
        finally
        {
            _syncing = false;
        }
    }

    private void OnTextChanged()
    {
        if (_syncing)
        {
            return;
        }

        // 选中项变化时 ComboBox 内部会短暂把可编辑文本置空；选中项才是这一刻的事实，
        // 不能让这次过渡文本把 SelectedFont 清掉。
        if (SelectedItem is string selected && string.IsNullOrEmpty(Text))
        {
            return;
        }

        _syncing = true;
        try
        {
            SetCurrentValue(SelectedFontProperty, Text ?? string.Empty);
        }
        finally
        {
            _syncing = false;
        }

        if (IsDropDownOpen)
        {
            ApplyFilter(Text);
        }
    }

    private void OnSelectedItemChanged()
    {
        if (_syncing || SelectedItem is not string font)
        {
            return;
        }

        _syncing = true;
        try
        {
            SetCurrentValue(SelectedFontProperty, font);
        }
        finally
        {
            _syncing = false;
        }

        IsDropDownOpen = false;
        ResetFilter();
    }

    private void RebuildOptions()
    {
        _allOptions.Clear();
        _allOptions.AddRange(Options ?? Array.Empty<string>());
        ResetFilter();
        OnSelectedFontChanged();
    }

    private void ApplyFilter(string? text)
    {
        var query = (text ?? string.Empty).Trim();
        var selected = SelectedItem as string;

        _filteredOptions.Clear();
        foreach (var option in _allOptions)
        {
            if (query.Length == 0 || option.Contains(query, StringComparison.OrdinalIgnoreCase))
            {
                _filteredOptions.Add(option);
            }
        }

        if (selected is not null && !_filteredOptions.Contains(selected, StringComparer.Ordinal))
        {
            _syncing = true;
            try
            {
                SetCurrentValue(SelectedItemProperty, null);
            }
            finally
            {
                _syncing = false;
            }
        }
    }

    private void ResetFilter()
    {
        var selected = SelectedFont ?? SelectedItem as string;
        if (selected is not null && !_allOptions.Contains(selected, StringComparer.Ordinal))
        {
            selected = null;
        }

        _filteredOptions.Clear();
        foreach (var option in _allOptions)
        {
            _filteredOptions.Add(option);
        }

        // 清空/重建 ItemsSource 会让 ComboBox 丢掉选中项与文本；这里把选中项放回去。
        if (selected is null)
        {
            return;
        }

        _syncing = true;
        try
        {
            SetCurrentValue(SelectedItemProperty, selected);
            SetCurrentValue(TextProperty, selected);
            SetCurrentValue(SelectedFontProperty, selected);
        }
        finally
        {
            _syncing = false;
        }
    }

    private static FuncDataTemplate<string> BuildItemTemplate() =>
        new FuncDataTemplate<string>((font, _) =>
        {
            var family = FontFamilyPreviewConverter.ToFontFamily(font);
            var name = new TextBlock
            {
                Text = font,
                FontFamily = family,
                TextTrimming = TextTrimming.CharacterEllipsis,
                VerticalAlignment = VerticalAlignment.Center,
            };
            var sample = new TextBlock
            {
                Text = Loc("appearance.font.sample"),
                FontFamily = family,
                Opacity = 0.6,
                VerticalAlignment = VerticalAlignment.Center,
            };
            var grid = new Grid
            {
                ColumnDefinitions = new ColumnDefinitions("*,Auto"),
                ColumnSpacing = 10,
            };
            grid.Children.Add(name);
            grid.Children.Add(sample);
            Grid.SetColumn(sample, 1);
            return grid;
        });

    private static FuncDataTemplate<string> BuildSelectionTemplate() =>
        new FuncDataTemplate<string>((font, _) => new TextBlock
        {
            Text = font,
            VerticalAlignment = VerticalAlignment.Center,
        });

    private static string Loc(string key) =>
        TextCatalog.Get(typeof(FontFamilyPicker).Assembly, key);
}
