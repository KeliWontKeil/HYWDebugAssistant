using System.ComponentModel;
using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.VisualTree;
using HYWDebugAssistant.Core.Api;
using HYWDebugAssistant.FrameworkUi.Controls;
using HYWDebugAssistant.Sdk.Text;
using HYWDebugAssistant.Ui.Theming;
using HYWDebugAssistant.Ui.Views;

namespace HYWDebugAssistant.FrameworkUi.Views;

/// <summary>候选色一格：第一格始终是"用户当前色"，其后是推荐色。</summary>
public sealed class ColorCandidate : INotifyPropertyChanged
{
    private Color _color;
    private bool _isSelected;

    /// <summary>创建一格候选色。</summary>
    /// <param name="id">稳定标识（<c>current</c> 或推荐色 id）。</param>
    /// <param name="label">悬停显示名。</param>
    /// <param name="color">色值。</param>
    public ColorCandidate(string id, string label, Color color)
    {
        Id = id;
        Label = label;
        _color = color;
    }

    /// <summary>稳定标识。</summary>
    public string Id { get; }

    /// <summary>悬停显示名。</summary>
    public string Label { get; }

    /// <summary>色值。</summary>
    public Color Color => _color;

    /// <summary>色块画刷。</summary>
    public IBrush Brush => new SolidColorBrush(_color);

    /// <summary>是否被选中（边框高亮）。</summary>
    public bool IsSelected
    {
        get => _isSelected;
        set
        {
            if (_isSelected == value)
            {
                return;
            }

            _isSelected = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsSelected)));
        }
    }

    /// <inheritdoc />
    public event PropertyChangedEventHandler? PropertyChanged;

    /// <summary>更新色值（第一格跟随当前色）。</summary>
    /// <param name="color">新色值。</param>
    public void SetColor(Color color)
    {
        if (_color == color)
        {
            return;
        }

        _color = color;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Color)));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Brush)));
    }
}

/// <summary>
/// **调色板窗口**：HSV 平面 + Hue/Value 条、整体 RGB + 三条 RGB 滑轨、当前色预览与候选色。
/// </summary>
/// <remarks>
/// <para>
/// 第一页使用**H/S 平面**和两条直接可拖的条：横向 Hue（色相）、纵向 Value（明度）。
/// 第二页顶部是整体 RGB/HEX 精确输入，下面三条 RGB 渐变滑轨各自显示当前值。
/// </para>
/// <para>
/// 色条使用自绘 <see cref="GradientBar"/>，不挂普通 Slider；因此没有滑块主题的端点控件、
/// 圆形拇指或滚动条。当前色以调用方传入的 <see cref="Color"/> 为初值，窗口打开后再次应用，
/// 避免 ColorSpectrum 模板初始化把颜色拉回白色。
/// </para>
/// </remarks>
public partial class ColorPickerDialog : Window
{
    private static readonly Dictionary<string, string> PresetLabelKeys =
        new(StringComparer.Ordinal)
        {
            ["blue"] = "palette.preset.blue",
            ["cyan"] = "palette.preset.cyan",
            ["green"] = "palette.preset.green",
            ["orange"] = "palette.preset.orange",
            ["red"] = "palette.preset.red",
            ["purple"] = "palette.preset.purple",
            ["pink"] = "palette.preset.pink",
            ["gray"] = "palette.preset.gray",
        };

    private readonly List<ColorCandidate> _candidates = [];
    private readonly ColorCandidate _current;
    private Color _initialColor = Colors.White;
    private Color _color = Colors.White;
    private HsvColor _hsv;
    private bool _suppress;
    private bool _ready;

    /// <summary>创建窗口（只给 XAML 加载器用；调用方请走 <see cref="ShowAsync"/>）。</summary>
    public ColorPickerDialog()
    {
        InitializeComponent();

        _current = new ColorCandidate("current", Loc("picker.current"), _color);
        _candidates.Add(_current);

        foreach (var preset in ThemePresets.Accents)
        {
            var color = ThemeApplier.Parse(preset.Hex) ?? Colors.Transparent;
            _candidates.Add(new ColorCandidate(preset.Id, LabelFor(preset.Id), color));
        }

        RecommendedColors.ItemsSource = _candidates;
        HueBar.Gradient = BuildHueGradient();

        Spectrum.ColorChanged += (_, _) => OnSpectrumChanged();
        Spectrum.LayoutUpdated += (_, _) => UpdateCrosshair();
        HueBar.ValueChanged += (_, _) => OnHueChanged();
        ValueBar.ValueChanged += (_, _) => OnValueChanged();
        RBar.ValueChanged += (_, _) => OnRgbBarChanged();
        GBar.ValueChanged += (_, _) => OnRgbBarChanged();
        BBar.ValueChanged += (_, _) => OnRgbBarChanged();
        RBox.TextChanged += (_, _) => OnRgbTextChanged();
        GBox.TextChanged += (_, _) => OnRgbTextChanged();
        BBox.TextChanged += (_, _) => OnRgbTextChanged();
        HexBox.TextChanged += (_, _) => OnHexChanged();

        Opened += (_, _) =>
        {
            // 模板初始化可能晚于外部 Color 赋值；打开后再应用一次，确保从原色开始。
            SetColor(_initialColor, selectedId: null);
            _ready = true;
            UpdateCrosshair();
        };
    }

    /// <summary>当前颜色（调用方据此设初值 / 读结果）。</summary>
    public Color Color
    {
        get => _color;
        set
        {
            _initialColor = value;
            SetColor(value, selectedId: null);
        }
    }

    /// <summary>取一档推荐色的显示名（Ui 表；未知 id 退回键本身）。</summary>
    /// <param name="id">推荐色稳定标识。</param>
    private static string LabelFor(string id) =>
        TextCatalog.Get(
            typeof(HYWDebugAssistant.Ui.ViewLocator).Assembly,
            PresetLabelKeys.TryGetValue(id, out var key) ? key : id);

    private static string Loc(string key) =>
        TextCatalog.Get(typeof(HYWDebugAssistant.Ui.ViewLocator).Assembly, key);

    /// <summary>把当前色同步到平面、三条色条、RGB/HEX、预览与候选色。</summary>
    /// <param name="color">新色。</param>
    /// <param name="selectedId">要标记为选中的候选；null ⇒ 第一格"当前色"。</param>
    private void SetColor(Color color, string? selectedId)
    {
        _color = color;
        _hsv = new HsvColor(color);

        _suppress = true;

        Spectrum.HsvColor = _hsv;
        HueBar.Value = _hsv.H;
        ValueBar.Value = _hsv.V * 100;
        RBar.Value = color.R;
        GBar.Value = color.G;
        BBar.Value = color.B;

        ValueBar.Gradient = BuildValueGradient(_hsv);
        RBar.Gradient = BuildChannelGradient(red: true, green: false, color);
        GBar.Gradient = BuildChannelGradient(red: false, green: true, color);
        BBar.Gradient = BuildChannelGradient(red: false, green: false, color);

        SetText(RBox, color.R);
        SetText(GBox, color.G);
        SetText(BBox, color.B);
        SetLabel(RValue, color.R);
        SetLabel(GValue, color.G);
        SetLabel(BValue, color.B);

        var hex = $"#{color.R:X2}{color.G:X2}{color.B:X2}";
        if (!string.Equals(HexBox.Text, hex, StringComparison.Ordinal))
        {
            HexBox.Text = hex;
        }

        _suppress = false;

        Preview.Background = new SolidColorBrush(color);
        _current.SetColor(color);

        var selected = string.IsNullOrEmpty(selectedId) ? "current" : selectedId;
        foreach (var candidate in _candidates)
        {
            candidate.IsSelected = string.Equals(candidate.Id, selected, StringComparison.Ordinal);
        }

        UpdateCrosshair();
    }

    private static void SetText(TextBox box, byte value)
    {
        var text = value.ToString(CultureInfo.InvariantCulture);
        if (!string.Equals(box.Text, text, StringComparison.Ordinal))
        {
            box.Text = text;
        }
    }

    private static void SetLabel(TextBlock label, byte value) =>
        label.Text = value.ToString(CultureInfo.InvariantCulture);

    private void OnSpectrumChanged()
    {
        if (_suppress || !_ready)
        {
            return;
        }

        SetColor(Spectrum.HsvColor.ToRgb(), selectedId: null);
    }

    private void OnHueChanged()
    {
        if (_suppress || !_ready)
        {
            return;
        }

        _hsv = new HsvColor(_hsv.A, HueBar.Value, _hsv.S, _hsv.V);
        SetColor(_hsv.ToRgb(), selectedId: null);
    }

    private void OnValueChanged()
    {
        if (_suppress || !_ready)
        {
            return;
        }

        _hsv = new HsvColor(_hsv.A, _hsv.H, _hsv.S, ValueBar.Value / 100d);
        SetColor(_hsv.ToRgb(), selectedId: null);
    }

    private void OnRgbBarChanged()
    {
        if (_suppress || !_ready)
        {
            return;
        }

        var color = Color.FromRgb(
            (byte)Math.Round(RBar.Value),
            (byte)Math.Round(GBar.Value),
            (byte)Math.Round(BBar.Value));

        SetColor(color, selectedId: null);
    }

    private void OnRgbTextChanged()
    {
        if (_suppress)
        {
            return;
        }

        if (!byte.TryParse(RBox.Text, NumberStyles.None, CultureInfo.InvariantCulture, out var red)
            || !byte.TryParse(GBox.Text, NumberStyles.None, CultureInfo.InvariantCulture, out var green)
            || !byte.TryParse(BBox.Text, NumberStyles.None, CultureInfo.InvariantCulture, out var blue))
        {
            return;
        }

        SetColor(Color.FromRgb(red, green, blue), selectedId: null);
    }

    private void OnHexChanged()
    {
        if (_suppress)
        {
            return;
        }

        if (Color.TryParse(HexBox.Text, out var color))
        {
            SetColor(color, selectedId: null);
        }
    }

    /// <summary>点候选色：选中它并把它作为当前色；确定后才应用。</summary>
    private void OnPickCandidate(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (sender is Button { DataContext: ColorCandidate candidate })
        {
            SetColor(candidate.Color, candidate.Id);
        }
    }

    /// <summary>
    /// 把 2 DIP 十字移到 ColorSpectrum 维护的选中点。
    /// </summary>
    /// <remarks>
    /// 不自己算 Hue/Saturation 到坐标的映射：隐藏默认圆点后直接读
    /// <c>PART_SelectionEllipsePanel</c> 的位置，因此换光谱分量/形状也不会偏。
    /// </remarks>
    private void UpdateCrosshair()
    {
        var panel = Spectrum.GetVisualDescendants()
            .OfType<Control>()
            .FirstOrDefault(control => control.Name == "PART_SelectionEllipsePanel");

        if (panel is null || panel.Bounds.Width <= 0 || panel.Bounds.Height <= 0)
        {
            CrosshairLayer.IsVisible = false;
            return;
        }

        panel.Opacity = 0;

        var center = panel.TranslatePoint(
            new Point(panel.Bounds.Width / 2, panel.Bounds.Height / 2),
            CrosshairLayer);

        if (center is not { } point)
        {
            CrosshairLayer.IsVisible = false;
            return;
        }

        CrosshairLayer.IsVisible = true;
        Canvas.SetLeft(CrosshairGlyph, point.X - (CrosshairGlyph.Width / 2));
        Canvas.SetTop(CrosshairGlyph, point.Y - (CrosshairGlyph.Height / 2));
    }

    private static LinearGradientBrush BuildHueGradient()
    {
        // 绝对色：色相条必须显示完整可见光谱，不能跟随主题色。
        var brush = new LinearGradientBrush
        {
            StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative),
            EndPoint = new RelativePoint(1, 0, RelativeUnit.Relative),
        };

        for (var index = 0; index <= 6; index++)
        {
            var offset = index / 6d;
            brush.GradientStops.Add(new GradientStop(
                HsvColor.FromHsv(index * 60d, 1, 1).ToRgb(),
                offset));
        }

        return brush;
    }

    private static LinearGradientBrush BuildValueGradient(HsvColor hsv)
    {
        var brush = new LinearGradientBrush
        {
            StartPoint = new RelativePoint(0, 1, RelativeUnit.Relative),
            EndPoint = new RelativePoint(0, 0, RelativeUnit.Relative),
        };

        brush.GradientStops.Add(new GradientStop(
            new HsvColor(hsv.A, hsv.H, hsv.S, 0).ToRgb(), 0));
        brush.GradientStops.Add(new GradientStop(
            new HsvColor(hsv.A, hsv.H, hsv.S, 1).ToRgb(), 1));

        return brush;
    }

    private static LinearGradientBrush BuildChannelGradient(bool red, bool green, Color color) =>
        red
            ? BuildRgbGradient(Color.FromRgb(0, color.G, color.B), Color.FromRgb(255, color.G, color.B))
            : green
                ? BuildRgbGradient(Color.FromRgb(color.R, 0, color.B), Color.FromRgb(color.R, 255, color.B))
                : BuildRgbGradient(Color.FromRgb(color.R, color.G, 0), Color.FromRgb(color.R, color.G, 255));

    private static LinearGradientBrush BuildRgbGradient(Color from, Color to)
    {
        // 绝对色：RGB 通道条展示 0–255 的纯通道变化，不能跟随主题色。
        var brush = new LinearGradientBrush
        {
            StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative),
            EndPoint = new RelativePoint(1, 0, RelativeUnit.Relative),
        };

        brush.GradientStops.Add(new GradientStop(from, 0));
        brush.GradientStops.Add(new GradientStop(to, 1));
        return brush;
    }

    /// <summary>取色结果。</summary>
    /// <param name="Confirmed">是否点了"确定"（false = 取消/直接关闭）。</param>
    /// <param name="Color">选中的颜色（取消时 = 初值）。</param>
    /// <param name="Reset">是否点了"恢复"（调用方按自己的语义处理：移除覆盖 / 跟随主题）。</param>
    public readonly record struct ColorPickerResult(bool Confirmed, Color Color, bool Reset);

    /// <summary>弹出调色板窗口并等待结果（**所有"选颜色"的位置都走这里**）。</summary>
    /// <param name="owner">属主窗口（居中显示；null ⇒ 非模态显示，供测试使用）。</param>
    /// <param name="initial">初值。</param>
    /// <param name="title">标题（如"编辑颜色 · 主题色"）；空 ⇒ 用默认标题。</param>
    /// <param name="resetText">"恢复"按钮文案（null/空 ⇒ 不显示该按钮）。</param>
    /// <returns>取色结果。</returns>
    public static async Task<ColorPickerResult> ShowAsync(
        Window? owner,
        Color initial,
        string? title = null,
        string? resetText = null)
    {
        var dialog = new ColorPickerDialog
        {
            Color = initial,
        };

        if (title is { Length: > 0 })
        {
            dialog.Title = title;
        }

        if (resetText is { Length: > 0 })
        {
            dialog.ResetButton.Content = resetText;
            dialog.ResetButton.IsVisible = true;
        }

        var result = new ColorPickerResult(false, initial, false);

        dialog.ResetButton.Click += (_, _) =>
        {
            result = new ColorPickerResult(true, initial, true);
            dialog.Close();
        };

        dialog.ConfirmButton.Click += (_, _) =>
        {
            result = new ColorPickerResult(true, dialog.Color, false);
            dialog.Close();
        };

        dialog.CancelButton.Click += (_, _) => dialog.Close();

        var group = AppWindowGroup.Of(owner);
        group?.Track(dialog, AppWindowLayer.Modal);
        using var modal = group?.BeginModal(dialog);

        if (owner is null)
        {
            dialog.Show();
        }
        else
        {
            await dialog.ShowDialog(owner);
        }

        return result;
    }
}
