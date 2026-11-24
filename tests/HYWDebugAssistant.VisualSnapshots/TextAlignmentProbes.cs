using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using HYWDebugAssistant.Sdk.Ui.Controls;
using SkiaSharp;
using Xunit;

namespace HYWDebugAssistant.VisualSnapshots;

/// <summary>
/// **文字垂直对齐探针**（按需运行，不是截图判据）：
/// 在同一 Top 下分别摆 <see cref="ModuleLabel"/> 与 <see cref="ModuleNumeric"/>，
/// 记录控件矩形、字体行盒、实际墨迹上下沿，用来判断“偏高 2px”究竟来自
/// 控件高度、字体行盒，还是设备像素取整。
/// </summary>
/// <remarks>
/// 输出到 <c>artifacts\snapshots\text-alignment\</c>：一张 PNG + 一份 <c>metrics.txt</c>。
/// 本类不写像素断言；数字只供定位，观感仍由人眼复核。
/// </remarks>
[Collection("visual-snapshots")]
public class TextAlignmentProbes
{
    private sealed record Case(
        string Name,
        bool Numeric,
        double Height,
        string Text,
        string FontFamily,
        bool CenterVertically);

    /// <summary>同一 Top、不同高度与字体的文字组合；最后两行验证显式 Top/Bottom。</summary>
    private static readonly Case[] Cases =
    [
        new("label-22", false, 22, "连续发送", "FontUi", true),
        new("label-24", false, 24, "连续发送", "FontUi", true),
        new("label-26", false, 26, "连续发送", "FontUi", true),
        new("numeric-22", true, 22, "500", "FontData", true),
        new("numeric-24", true, 24, "500", "FontData", true),
        new("numeric-26", true, 26, "500", "FontData", true),
        new("label-data-24", false, 24, "连续发送", "FontData", true),
        new("numeric-ui-24", true, 24, "500", "FontUi", true),
        new("label-top-24", false, 24, "连续发送", "FontUi", false),
    ];

    [Fact]
    public async Task 文字垂直对齐探针_输出控件矩形与墨迹位置()
    {
        await SnapshotSession.Shared.Dispatch(
            () =>
            {
                const double top = 8;
                const double left = 12;
                const double width = 90;
                const double gap = 8;
                var maxHeight = Cases.Max(c => c.Height);
                var totalWidth = left + Cases.Length * (width + gap) + gap;
                var canvas = new Canvas
                {
                    Width = totalWidth,
                    Height = maxHeight + top * 2 + 20,
                    Background = Brushes.White,
                };

                var placed = new List<(Case Case, ModuleTextElement Control)>();
                for (var index = 0; index < Cases.Length; index++)
                {
                    var item = Cases[index];
                    ModuleTextElement control = item.Numeric
                        ? new ModuleNumeric { Text = item.Text }
                        : new ModuleLabel { Text = item.Text };

                    control.Width = width;
                    control.Height = item.Height;
                    control.FontFamily = ResolveFont(item.FontFamily);

                    if (control is ModuleTextElement text)
                    {
                        text.TextVerticalAlignment = item.CenterVertically
                            ? Avalonia.Layout.VerticalAlignment.Center
                            : Avalonia.Layout.VerticalAlignment.Top;
                    }

                    Canvas.SetLeft(control, left + index * (width + gap));
                    Canvas.SetTop(control, top);
                    canvas.Children.Add(control);
                    placed.Add((item, control));
                }

                var window = new Window
                {
                    Width = canvas.Width,
                    Height = canvas.Height,
                    Content = canvas,
                    Background = Brushes.White,
                };

                try
                {
                    window.Show();
                    SnapshotTool.Pump();

                    const string relativePng = "text-alignment/label-numeric.png";
                    SnapshotTool.Save(window, relativePng);

                    var pngPath = Path.Combine(SnapshotTool.OutputRoot, relativePng);
                    var metrics = new List<string>
                    {
                        string.Create(CultureInfo.InvariantCulture,
                            $"window={window.Bounds.Width:0.##}x{window.Bounds.Height:0.##}"),
                    };

                    using var bitmap = SKBitmap.Decode(pngPath);
                    var scale = (double)bitmap.Width / window.Bounds.Width;

                    foreach (var (item, control) in placed)
                    {
                        var layout = control.TextLayout;
                        var origin = control.TranslatePoint(default, window) ?? default;
                        var topPixel = (int)Math.Floor((origin.Y) * scale);
                        var bottomPixel = (int)Math.Ceiling((origin.Y + control.Bounds.Height) * scale);
                        var leftPixel = (int)Math.Floor((origin.X) * scale);
                        var rightPixel = (int)Math.Ceiling((origin.X + control.Bounds.Width) * scale);

                        var inkTop = int.MaxValue;
                        var inkBottom = int.MinValue;
                        for (var y = Math.Max(0, topPixel); y < Math.Min(bitmap.Height, bottomPixel); y++)
                        {
                            for (var x = Math.Max(0, leftPixel); x < Math.Min(bitmap.Width, rightPixel); x++)
                            {
                                var pixel = bitmap.GetPixel(x, y);
                                if (pixel.Red < 128 && pixel.Green < 128 && pixel.Blue < 128)
                                {
                                    inkTop = Math.Min(inkTop, y);
                                    inkBottom = Math.Max(inkBottom, y);
                                    break;
                                }
                            }
                        }

                        var boxTop = origin.Y * scale;
                        var boxBottom = (origin.Y + control.Bounds.Height) * scale;
                        var inkTopDips = inkTop == int.MaxValue ? double.NaN : inkTop / scale - origin.Y;
                        var inkBottomDips = inkBottom == int.MinValue ? double.NaN : (inkBottom + 1) / scale - origin.Y;

                        metrics.Add(string.Create(CultureInfo.InvariantCulture,
                            $"{item.Name}: bounds=({origin.X:0.##},{origin.Y:0.##},{control.Bounds.Width:0.##},{control.Bounds.Height:0.##}) " +
                            $"desired={control.DesiredSize.Width:0.##}x{control.DesiredSize.Height:0.##} " +
                            $"font={control.FontFamily} size={control.FontSize:0.##} " +
                            $"line=({layout.Height:0.###},{layout.Baseline:0.###},{layout.Extent:0.###},{layout.OverhangAfter:0.###}) " +
                            $"offset={ModuleTextVerticalMetrics.ComputeOffset(control.Bounds.Height, layout.Height, layout.Baseline, control.FontSize, layout.TextLines.Count, control.TextVerticalAlignment):0.###} " +
                            $"ink={(inkTopDips):0.###}..{(inkBottomDips):0.###} " +
                            $"gapTop={(inkTopDips):0.###} gapBottom={(control.Bounds.Height - inkBottomDips):0.###} " +
                            $"box=({boxTop:0.##}..{boxBottom:0.##}) scale={scale:0.###}"));
                    }

                    var metricsPath = Path.Combine(
                        SnapshotTool.OutputRoot, "text-alignment", "metrics.txt");
                    Directory.CreateDirectory(Path.GetDirectoryName(metricsPath)!);
                    File.WriteAllLines(metricsPath, metrics);

                    foreach (var line in metrics)
                    {
                        Console.WriteLine("[text-alignment] " + line);
                    }
                }
                finally
                {
                    window.Close();
                }
            },
            CancellationToken.None);
    }

    /// <summary>取与主题同名的字体；从应用资源里读，读不到退回字体名本身。</summary>
    private static FontFamily ResolveFont(string key) =>
        Application.Current?.TryGetResource(key, out var value) == true && value is FontFamily font
            ? font
            : new FontFamily(key);
}
