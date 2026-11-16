using System;
using System.IO;
using System.Linq;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.VisualTree;
using HYWDebugAssistant.FrameworkUi.Controls;
using HYWDebugAssistant.FrameworkUi.Views;
using Xunit;

namespace HYWDebugAssistant.VisualSnapshots;

/// <summary>
/// **调色板窗口截图**：光谱页与分量页各一张 PNG（加一份视觉树文本）。
/// </summary>
/// <remarks>
/// 只负责“把它画出来”；“取色控件有没有真的出现在窗口里”由人眼看图判断。
/// 这个窗口的风险面是第三方控件模板：漏挂模板时编译与绑定都不报错，只是什么都不画 ⇒ 列入固定截图场景。
/// </remarks>
[Collection("visual-snapshots")]
public class ColorPickerSnapshots
{
    /// <summary>生成调色板截图（<c>color-picker.png</c> / <c>color-picker-components.png</c>）。</summary>
    [Fact]
    public async Task 生成快照_调色板()
    {
        var session = SnapshotSession.Shared;

        await session.Dispatch(
            () =>
            {
                var dialog = new ColorPickerDialog { Color = Colors.DodgerBlue };
                dialog.Show();
                SnapshotTool.Pump();
                // ColorSpectrum 的平面位图是异步生成的；等它完成后再截图，否则会拍到白色平面。
                Thread.Sleep(200);
                SnapshotTool.Pump();

                Assert.Equal(Colors.DodgerBlue, dialog.Color);
                Assert.Contains(dialog.GetVisualDescendants(), control => control.Name == "HueBar");
                Assert.Contains(dialog.GetVisualDescendants(), control => control.Name == "ValueBar");
                Assert.DoesNotContain(dialog.GetVisualDescendants().OfType<Slider>(), _ => true);

                SnapshotTool.Save(dialog, "color-picker.png");

                // 页签里含十六进制输入的那一个 = 分量页（十六进制改色的唯一入口）
                var tab = FindTabWithHexInput(dialog);
                if (tab >= 0)
                {
                    dialog.GetVisualDescendants().OfType<TabControl>().First().SelectedIndex = tab;
                    SnapshotTool.Pump();
                    SnapshotTool.Save(dialog, "color-picker-components.png");
                }

                SnapshotTool.DumpTree(dialog, "color-picker-tree.txt");
                dialog.Close();
            },
            CancellationToken.None);
    }

    /// <summary>找出“含十六进制输入框”的页签下标（找不到 ⇒ -1）。</summary>
    private static int FindTabWithHexInput(Window window)
    {
        var tabs = window.GetVisualDescendants().OfType<TabControl>().First();

        for (var index = 0; index < tabs.Items.Count; index++)
        {
            tabs.SelectedIndex = index;
            SnapshotTool.Pump();

            if (window.GetVisualDescendants().Any(control => control is TextBox))
            {
                return index;
            }
        }

        return -1;
    }
}
