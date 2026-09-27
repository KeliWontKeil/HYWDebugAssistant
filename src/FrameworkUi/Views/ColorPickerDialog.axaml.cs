using Avalonia.Controls;
using Avalonia.Media;
using HYWDebugAssistant.Ui.Theming;

namespace HYWDebugAssistant.FrameworkUi.Views;

/// <summary>
/// **调色板窗口**：任选颜色（左侧取色器：光谱/色相/分量/十六进制 + 右侧常驻推荐色）【ADR-0054 · 用户第三十四轮】。
/// </summary>
/// <remarks>
/// <list type="bullet">
///   <item><b>为什么统一到一个窗口</b>：用户原话 —— "调色板窗口将可以**被所有颜色选择相关位置复用**"；
///     此前每个位置各自内联"预设 + 十六进制输入"（还只能输 RGB/挑内置色）⇒ 观感与能力都不一致；</item>
///   <item><b>一屏全见</b>：用户裁定 —— "改成一屏全见：**左取色器 + 右侧常驻推荐色**" ⇒ 推荐色**不必切页签**就能点；
///     控件自带的"色板"页签关掉（`IsColorPaletteVisible=False`）⇒ 推荐色**只有一个来源**；</item>
///   <item><b>推荐色与主题一套</b>：推荐色 = <see cref="ThemeApplier.Presets"/>（与设置页"主题色"同一套 8 档）；
///     而**类型颜色本身由用户自定、与主题解耦**（用户裁定："一套颜色，由用户自己改，不与主题一套色，
///     不过调色板中的推荐颜色与主题一套色"）；</item>
///   <item><b>精简</b>：不做"自定义颜色"格、不带透明度（用户："建议做精简（可以减少内置颜色和用户自定义颜色的候选数量）"）；</item>
///   <item><b>可选"恢复"</b>：调用方给文案（如"恢复自动色"/"跟随主题"）时显示该按钮 ⇒ 结果为
///     <see cref="ColorPickerResult.Reset"/> = true，由调用方按自己的语义处理（移除覆盖 / 置空跟随主题）。</item>
/// </list>
/// </remarks>
public partial class ColorPickerDialog : Window
{
    /// <summary>创建窗口（只给 XAML 加载器用；调用方请走 <see cref="ShowAsync"/>）。</summary>
    public ColorPickerDialog()
    {
        InitializeComponent();

        // 推荐色 = **与主题同一套**（用户裁定），常驻显示在右侧 8 个
        RecommendedColors.ItemsSource = ThemeApplier.Presets;
    }

    /// <summary>当前颜色（= 窗口内取色控件的值；调用方据此设初值 / 读结果）。</summary>
    public Color Color
    {
        get => View.Color;
        set => View.Color = value;
    }

    /// <summary>点了某个推荐色 ⇒ 直接把它作为当前颜色（= 用户不必去光谱里凑）。</summary>
    private void OnPickRecommended(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (sender is Button { DataContext: ThemeApplier.AccentPreset preset }
            && ThemeApplier.Parse(preset.Hex) is { } color)
        {
            Color = color;
        }
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
    /// <param name="hint">底部提示（空 ⇒ 用默认提示）。</param>
    /// <returns>取色结果。</returns>
    public static async Task<ColorPickerResult> ShowAsync(
        Window? owner,
        Color initial,
        string? title = null,
        string? resetText = null,
        string? hint = null)
    {
        var dialog = new ColorPickerDialog
        {
            Color = initial,
        };

        if (title is { Length: > 0 })
        {
            dialog.Title = title;   // 标题只由**窗口标题栏**给（窗口内不再重复写一遍）
        }

        if (hint is { Length: > 0 })
        {
            dialog.HintText.Text = hint;
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
