using System.ComponentModel;
using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using HYWDebugAssistant.Core.Api;
using HYWDebugAssistant.FrameworkUi.Controls;
using HYWDebugAssistant.Sdk.Text;
using HYWDebugAssistant.Ui.Theming;
using HYWDebugAssistant.Ui.Views;

namespace HYWDebugAssistant.FrameworkUi.Views;

/// <summary>候选色一格：第一格是“自定义色”，其后是推荐色；选中推荐色只移动边框，不覆盖第一格。</summary>
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
/// 明度条顶部最暗、底部最亮；平面上的黑色遮罩随 Value 变化，二者共享同一个 HSV 状态。
/// 第二页顶部是整体 RGB/HEX 精确输入，下面三条 RGB 渐变滑轨各自显示当前值。
/// </para>
/// <para>
/// 色条使用自绘 <see cref="GradientBar"/>，不挂普通 Slider；因此没有滑块主题的端点控件、
/// 圆形拇指或滚动条。第一格“自定义”只由调色板输入更新；推荐色只做选择，确定时返回
/// <see cref="ResultColor"/>。当前色以调用方传入的 <see cref="Color"/> 为初值，窗口打开后再次应用，
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
    private string _selectedCandidateId = "current";
    private bool _suppress;
    private bool _normalizingRgbText;
    private bool _planeDragging;
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

        Spectrum.LayoutUpdated += (_, _) => UpdateCrosshair();
        SpectrumInput.PointerPressed += OnSpectrumPointerPressed;
        SpectrumInput.PointerMoved += OnSpectrumPointerMoved;
        SpectrumInput.PointerReleased += OnSpectrumPointerReleased;
        SpectrumInput.PointerCaptureLost += OnSpectrumPointerCaptureLost;
        HueBar.ValueChanged += (_, _) => OnHueChanged();
        ValueBar.ValueChanged += (_, _) => OnValueChanged();
        RBar.ValueChanged += (_, _) => OnRgbBarChanged();
        GBar.ValueChanged += (_, _) => OnRgbBarChanged();
        BBar.ValueChanged += (_, _) => OnRgbBarChanged();
        RBox.TextChanged += (_, _) => OnRgbTextChanged(RBox);
        GBox.TextChanged += (_, _) => OnRgbTextChanged(GBox);
        BBox.TextChanged += (_, _) => OnRgbTextChanged(BBox);
        HexBox.TextChanged += (_, _) => OnHexChanged();

        Opened += (_, _) =>
        {
            // 模板初始化可能晚于外部 Color 赋值；打开后再应用一次，确保从原色开始。
            ApplyHsv(new HsvColor(_initialColor));
            _ready = true;
            UpdateCrosshair();
        };
    }

    /// <summary>当前自定义颜色（调用方据此设初值）。</summary>
    public Color Color
    {
        get => _color;
        set
        {
            _initialColor = value;
            ApplyHsv(new HsvColor(value));
        }
    }

    /// <summary>
    /// 确定时真正返回的颜色：选中推荐色时是推荐色；否则是当前自定义色。
    /// </summary>
    public Color ResultColor =>
        string.Equals(_selectedCandidateId, "current", StringComparison.Ordinal)
            ? _hsv.ToRgb()
            : _candidates.FirstOrDefault(
                candidate => string.Equals(candidate.Id, _selectedCandidateId, StringComparison.Ordinal))
                ?.Color ?? _hsv.ToRgb();

    /// <summary>取一档推荐色的显示名（Ui 表；未知 id 退回键本身）。</summary>
    /// <param name="id">推荐色稳定标识。</param>
    private static string LabelFor(string id) =>
        TextCatalog.Get(
            typeof(HYWDebugAssistant.Ui.ViewLocator).Assembly,
            PresetLabelKeys.TryGetValue(id, out var key) ? key : id);

    private static string Loc(string key) =>
        TextCatalog.Get(typeof(HYWDebugAssistant.Ui.ViewLocator).Assembly, key);

    /// <summary>
    /// 唯一颜色写入点：规范化 HSV 并同步平面、两条、RGB/HEX、明度遮罩、预览与第一位候选。
    /// 任何调色板输入（平面/两条/RGB/HEX/初始化/重置）都走这里；推荐色点击不走这里。
    /// </summary>
    /// <param name="hsv">新 HSV 值。</param>
    private void ApplyHsv(HsvColor hsv)
    {
        _hsv = new HsvColor(
            Math.Clamp(hsv.A, 0, 1),
            hsv.H,
            Math.Clamp(hsv.S, 0, 1),
            Math.Clamp(hsv.V, 0, 1));
        _color = _hsv.ToRgb();

        _suppress = true;

        // 平面只保存 H/S；固定 V=1，明度由 ValueShade/ValueBar 表达。
        // 这样改 V 不会驱动 ColorSpectrum 重新计算选择点（V=0 时尤其会丢 H/S）。
        Spectrum.HsvColor = new HsvColor(_hsv.A, _hsv.H, _hsv.S, 1);
        HueBar.Value = _hsv.H;
        ValueBar.Value = _hsv.V * 100;
        ValueShade.Opacity = 1 - _hsv.V;

        RBar.Value = _color.R;
        GBar.Value = _color.G;
        BBar.Value = _color.B;

        ValueBar.Gradient = BuildValueGradient(_hsv);
        RBar.Gradient = BuildChannelGradient(red: true, green: false, _color);
        GBar.Gradient = BuildChannelGradient(red: false, green: true, _color);
        BBar.Gradient = BuildChannelGradient(red: false, green: false, _color);

        SetText(RBox, _color.R);
        SetText(GBox, _color.G);
        SetText(BBox, _color.B);
        SetLabel(RValue, _color.R);
        SetLabel(GValue, _color.G);
        SetLabel(BValue, _color.B);

        var hex = $"#{_color.R:X2}{_color.G:X2}{_color.B:X2}";
        if (!string.Equals(HexBox.Text, hex, StringComparison.Ordinal))
        {
            HexBox.Text = hex;
        }

        _suppress = false;

        _current.SetColor(_color);
        SelectCandidate(_current);
        UpdateCrosshair();
    }

    /// <summary>
    /// 只改候选选中态与预览，不改自定义颜色、平面或两条。
    /// </summary>
    /// <param name="candidate">要选中的候选。</param>
    private void SelectCandidate(ColorCandidate candidate)
    {
        _selectedCandidateId = candidate.Id;

        foreach (var item in _candidates)
        {
            item.IsSelected = string.Equals(item.Id, _selectedCandidateId, StringComparison.Ordinal);
        }

        var displayed = string.Equals(candidate.Id, "current", StringComparison.Ordinal)
            ? _hsv.ToRgb()
            : candidate.Color;

        Preview.Background = new SolidColorBrush(displayed);
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

    /// <summary>平面输入：只按指针的 H/S 坐标改色，V 永远保留当前值。</summary>
    private void OnSpectrumPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (!e.GetCurrentPoint(SpectrumInput).Properties.IsLeftButtonPressed)
        {
            return;
        }

        _planeDragging = true;
        e.Pointer.Capture(SpectrumInput);
        ApplySpectrumPoint(e.GetPosition(SpectrumInput));
        e.Handled = true;
    }

    private void OnSpectrumPointerMoved(object? sender, PointerEventArgs e)
    {
        if (!_planeDragging)
        {
            return;
        }

        ApplySpectrumPoint(e.GetPosition(SpectrumInput));
        e.Handled = true;
    }

    private void OnSpectrumPointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (!_planeDragging)
        {
            return;
        }

        _planeDragging = false;
        e.Pointer.Capture(null);
        e.Handled = true;
    }

    private void OnSpectrumPointerCaptureLost(object? sender, PointerCaptureLostEventArgs e)
    {
        _planeDragging = false;
    }

    private void ApplySpectrumPoint(Point point)
    {
        var width = SpectrumInput.Bounds.Width;
        var height = SpectrumInput.Bounds.Height;
        if (width <= 0 || height <= 0)
        {
            return;
        }

        var hue = Math.Clamp(point.X / width, 0, 1) * 360;
        var saturation = 1 - Math.Clamp(point.Y / height, 0, 1);
        ApplyHsv(new HsvColor(_hsv.A, hue, saturation, _hsv.V));
    }

    private void OnHueChanged()
    {
        if (_suppress || !_ready)
        {
            return;
        }

        ApplyHsv(new HsvColor(_hsv.A, HueBar.Value, _hsv.S, _hsv.V));
    }

    private void OnValueChanged()
    {
        if (_suppress || !_ready)
        {
            return;
        }

        ApplyHsv(new HsvColor(_hsv.A, _hsv.H, _hsv.S, ValueBar.Value / 100d));
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

        ApplyHsv(new HsvColor(color));
    }

    /// <summary>RGB 文本输入：只允许数字；超过 255 立即写回 255。</summary>
    /// <param name="box">被编辑的输入框。</param>
    private void OnRgbTextChanged(TextBox box)
    {
        if (_suppress || _normalizingRgbText)
        {
            return;
        }

        var normalized = NormalizeRgbText(box.Text);
        if (!string.Equals(normalized, box.Text, StringComparison.Ordinal))
        {
            _normalizingRgbText = true;
            box.Text = normalized;
            box.CaretIndex = normalized.Length;
            _normalizingRgbText = false;
        }

        if (!byte.TryParse(RBox.Text, NumberStyles.None, CultureInfo.InvariantCulture, out var red)
            || !byte.TryParse(GBox.Text, NumberStyles.None, CultureInfo.InvariantCulture, out var green)
            || !byte.TryParse(BBox.Text, NumberStyles.None, CultureInfo.InvariantCulture, out var blue))
        {
            return;
        }

        var color = Color.FromRgb(red, green, blue);
        if (color == _color)
        {
            // TextBox 的 TextChanged 由 Dispatcher 异步派发；ApplyHsv 写回文本后事件才到，
            // 此时值就是当前色。再走一遍会把 V=0 的 #000000 反解成 H/S=0，导致十字飞掉。
            return;
        }

        ApplyHsv(new HsvColor(color));
    }

    /// <summary>只允许 ASCII 数字进入 RGB 输入框；粘贴/输入法含其它字符时整次拒绝。</summary>
    private void OnRgbTextInput(object? sender, Avalonia.Input.TextInputEventArgs e)
    {
        if (e.Text is { Length: > 0 } text
            && text.Any(character => character is < '0' or > '9'))
        {
            e.Handled = true;
        }
    }

    private static string NormalizeRgbText(string? text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return string.Empty;
        }

        var digits = new string(text.Where(character => character is >= '0' and <= '9').ToArray());
        if (digits.Length == 0)
        {
            return string.Empty;
        }

        return int.TryParse(digits, NumberStyles.None, CultureInfo.InvariantCulture, out var value)
               && value > 255
            ? "255"
            : digits;
    }

    private void OnHexChanged()
    {
        if (_suppress)
        {
            return;
        }

        if (Color.TryParse(HexBox.Text, out var color) && color != _color)
        {
            ApplyHsv(new HsvColor(color));
        }
    }

    /// <summary>点候选色：只移动选中态与预览；第一位自定义色不因推荐色点击而改变。</summary>
    private void OnPickCandidate(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (sender is Button { DataContext: ColorCandidate candidate })
        {
            SelectCandidate(candidate);
        }
    }

    /// <summary>
    /// 把十字画到当前 H/S 对应的平面坐标。
    /// </summary>
    /// <remarks>
    /// 不读取 ColorSpectrum 内部的 SelectionEllipsePanel：第三方控件在 V=0 等边界会
    /// 重算/重置选择点，导致明度变化时十字飞掉。十字只由本窗口的 H/S 投影决定，
    /// V 不参与；平面内部圆点仍由样式隐藏。
    /// </remarks>
    private void UpdateCrosshair()
    {
        var width = Spectrum.Bounds.Width;
        var height = Spectrum.Bounds.Height;
        if (width <= 0 || height <= 0)
        {
            CrosshairLayer.IsVisible = false;
            return;
        }

        var xPercent = Math.Clamp(_hsv.H / 360d, 0, 1);
        var yPercent = Math.Clamp(1 - _hsv.S, 0, 1);
        var x = width * xPercent;
        var y = height * yPercent;

        CrosshairLayer.IsVisible = true;
        Canvas.SetLeft(CrosshairGlyph, x - (CrosshairGlyph.Width / 2));
        Canvas.SetTop(CrosshairGlyph, y - (CrosshairGlyph.Height / 2));
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
            // ValueBar 使用 Invert：顶部 0（黑），底部 100（当前 H/S 满亮度）。
            StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative),
            EndPoint = new RelativePoint(0, 1, RelativeUnit.Relative),
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
            result = new ColorPickerResult(true, dialog.ResultColor, false);
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
