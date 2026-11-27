using Avalonia.Controls;
using Avalonia.Media.Imaging;
using HYWDebugAssistant.FrameworkUi.ViewModels;
using HYWDebugAssistant.FrameworkUi.Views;
using HYWDebugAssistant.Ui.Design;
using Xunit;

namespace HYWDebugAssistant.VisualSnapshots;

/// <summary>
/// 模块清单页的截图：**类型色外圈**（单类型 / 双类型转换器 / 零端口）与**封面**一起看。
/// </summary>
/// <remarks>
/// <list type="bullet">
///   <item><b>不是判据</b>：这里只出图（<c>modules\catalog-list.png</c> 与 <c>modules\catalog-list-?*.png</c>），
///     圈画得对不对、分色好不好看，都由人眼看这个文件夹里的 PNG。</item>
///   <item><b>数据是"像真的"</b>：用设计时替身运行时（<see cref="EmptyRuntime"/>）给出真实数量的模块行，
///     预览图直接取 <c>artifacts\snapshots\modules\*-cover.png</c>——也就是**包封面本身**；
///     没有那张图时行会退化成占位字形，那张图同样值得看一眼（它就是"没有封面"的样子）。</item>
/// </list>
/// </remarks>
[Collection("visual-snapshots")]
public class CatalogViewSnapshots
{
    /// <summary>整页：分类标签 + 模块列表（含分类标题行、来源标注、类型色外圈）。</summary>
    [Fact]
    public async Task 生成快照_模块清单() =>
        await SaveAsync("modules/catalog-list.png", 420, 760);

    /// <summary>列表特写：把窗口压到"只有几行"的宽度，便于看清图块外圈的颜色与分色。</summary>
    [Fact]
    public async Task 生成快照_模块清单_图块特写() =>
        await SaveAsync("modules/catalog-list-tiles.png", 420, 300);

    private static async Task SaveAsync(string path, double width, double height)
    {
        await SnapshotSession.Shared.Dispatch(
            () =>
            {
                var viewModel = new ModuleCatalogViewModel(EmptyRuntime.Instance, EmptyRuntime.Instance);
                viewModel.Refresh();

                var view = new ModuleCatalogView { DataContext = viewModel };

                var window = new Window
                {
                    Width = width,
                    Height = height,
                    Content = view,
                };

                window.Show();
                Pump();

                // **必须放在 Refresh 之后、且之后不能再 Refresh**：
                // 清单每刷新一次就从运行时重建行模型，覆盖这里塞进去的封面图。
                ReplacePreviews(viewModel);
                Pump();

                SnapshotTool.Save(window, path);
                window.Close();
            },
            CancellationToken.None);
    }

    /// <summary>
    /// 把有封面的行换成**真封面图**：清单截图的用途之一就是看"封面 + 外圈"合在一起的样子。
    /// </summary>
    /// <param name="viewModel">清单视图模型。</param>
    /// <remarks>
    /// 找不到对应封面就保持原样（占位字形）——那也是一种要看的形态，不因缺图失败。
    /// 模块标识 `hyw/raw-to-waveform` → 截图文件名 `RawToWaveform-cover.png` 的映射在
    /// <see cref="CoverFileFor"/> 里：它是"按类型名"的产物名，与包目录名不同。
    /// </remarks>
    private static void ReplacePreviews(ModuleCatalogViewModel viewModel)
    {
        var coverRoot = Path.Combine(SnapshotTool.OutputRoot, "modules");
        if (!Directory.Exists(coverRoot))
        {
            return;
        }

        for (var index = 0; index < viewModel.Catalog.Count; index++)
        {
            if (viewModel.Catalog[index].Entry is not { } entry)
            {
                continue;
            }

            var file = Path.Combine(coverRoot, CoverFileFor(entry.ModuleId));
            if (!File.Exists(file))
            {
                continue;
            }

            try
            {
                var bitmap = new Bitmap(file);
                viewModel.Catalog[index] = viewModel.Catalog[index] with
                {
                    Entry = entry with { PreviewImage = bitmap },
                };
            }
            catch (Exception ex) when (ex is IOException or ArgumentException or NotSupportedException)
            {
                // 读不出来就当没有封面（行仍然显示占位字形），不影响出图。
            }
        }
    }

    /// <summary>模块标识 → 截图产物文件名（`hyw/attitude-display` → `AttitudeDisplay-cover.png`）。</summary>
    /// <param name="moduleId">模块标识字面量。</param>
    /// <returns>文件名的候选上界（找不到就算了）。</returns>
    private static string CoverFileFor(string moduleId)
    {
        var name = moduleId.Split('/').Last();
        var builder = new System.Text.StringBuilder();

        var upperNext = true;
        foreach (var character in name)
        {
            if (character == '-')
            {
                upperNext = true;
                continue;
            }

            _ = builder.Append(upperNext ? char.ToUpperInvariant(character) : character);
            upperNext = false;
        }

        return builder.Append("-cover.png").ToString();
    }

    private static void Pump() => SnapshotTool.Pump();
}
