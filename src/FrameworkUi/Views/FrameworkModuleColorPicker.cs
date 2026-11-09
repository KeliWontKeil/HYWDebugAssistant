using Avalonia.Controls;
using Avalonia.Media;
using HYWDebugAssistant.Core.Api;
using HYWDebugAssistant.Sdk.Ui;

namespace HYWDebugAssistant.FrameworkUi.Views;

/// <summary>
/// **模块取色服务**：复用框架统一调色窗口，并把推荐色作为模块自动颜色来源。
/// </summary>
/// <remarks>
/// 模块只依赖 <see cref="IModuleColorPicker"/>；本类在 FrameworkUi 里把接口转成
/// <see cref="ColorPickerDialog"/> 与 <see cref="ThemePresets.Accents"/>，由组合根注入。
/// </remarks>
public sealed class FrameworkModuleColorPicker : IModuleColorPicker
{
    /// <summary>推荐色（与调色窗口右侧常驻推荐色同源）。</summary>
    public IReadOnlyList<uint> SuggestedColors { get; } = BuildSuggestedColors();

    /// <inheritdoc />
    public async Task<ModuleColorPickResult> PickAsync(
        Window? owner,
        uint? initialArgb,
        string? title,
        string? resetText)
    {
        var initial = initialArgb is { } argb
            ? Color.FromUInt32(argb)
            : Colors.Gray;

        var result = await ColorPickerDialog.ShowAsync(
            owner,
            initial,
            title,
            resetText: resetText);

        return new ModuleColorPickResult(
            result.Confirmed,
            ToArgb(result.Color),
            result.Reset);
    }

    private static List<uint> BuildSuggestedColors()
    {
        var colors = new List<uint>(ThemePresets.Accents.Count);
        foreach (var preset in ThemePresets.Accents)
        {
            if (Color.TryParse(preset.Hex, out var color))
            {
                colors.Add(ToArgb(color));
            }
        }

        return colors;
    }

    private static uint ToArgb(Color color) =>
        ((uint)color.A << 24)
        | ((uint)color.R << 16)
        | ((uint)color.G << 8)
        | color.B;
}
