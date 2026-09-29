using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Threading;
using HYWDebugAssistant.Core.Api;
using HYWDebugAssistant.Ui.Diagnostics;
using HYWDebugAssistant.Ui.ViewModels;
using Microsoft.Extensions.DependencyInjection;

namespace HYWDebugAssistant.App;

/// <summary>
/// **临时自检**（用户第十二轮第二轮反馈："两个窗口靠很近时中线对齐才会抖，远一点就不抖"）。
/// </summary>
/// <remarks>
/// 纯几何扫描已排除吸附决策层、指针换算已改系统光标仍抖 ⇒ 需要在**真实窗口 + 真实 DPI** 下拿到逐帧事实。
/// 本自检**不模拟鼠标、不抢焦点**：直接在应用内部走同一条拖动路径（`BeginDrag/DragTo/EndDrag`），
/// 把每一步的模型几何与**窗口实际几何**一起落盘。由环境变量 `HYW_DRAG_SELFTEST=1` 触发；**定位后整体删除**。
/// </remarks>
internal static class DragSelfTest
{
    private const string EnvVar = "HYW_DRAG_SELFTEST";

    /// <summary>若环境变量已设置则启动自检（否则什么都不做）。</summary>
    /// <param name="desktop">桌面生命周期。</param>
    /// <param name="provider">组合根。</param>
    /// <param name="shell">外壳视图模型（模块窗口集控制器挂在它上面）。</param>
    public static void RunIfRequested(
        IClassicDesktopStyleApplicationLifetime desktop,
        IServiceProvider provider,
        HYWDebugAssistant.FrameworkUi.ViewModels.ShellViewModel shell)
    {
        ArgumentNullException.ThrowIfNull(desktop);
        ArgumentNullException.ThrowIfNull(provider);
        ArgumentNullException.ThrowIfNull(shell);

        if (!string.Equals(Environment.GetEnvironmentVariable(EnvVar), "1", StringComparison.Ordinal))
        {
            return;
        }

        var windows = shell.Windows;
        var command = provider.GetRequiredService<IRuntimeCommand>();

        // 让外壳与窗口先就绪，再开始（脚本化拖动需要真实的窗口与 DPI）
        DispatcherTimer.RunOnce(() => Arrange(windows, command), TimeSpan.FromSeconds(3));
        DispatcherTimer.RunOnce(() => Drag(windows, desktop), TimeSpan.FromSeconds(5));
        DispatcherTimer.RunOnce(() => MeasureShellEdge(windows, command, desktop), TimeSpan.FromSeconds(10));
    }

    /// <summary>
    /// **外壳边缘量测**（修"缝"的证据）：把模块吸到外壳右边缘上，分别记录
    /// "参照矩形右边缘（可见外框）"、"客户区右边缘"与"模块左边缘"，
    /// 差值即"缝"——修复前参照取客户区边缘 ⇒ 差 ≈ 可见边框宽（约 1px）。
    /// </summary>
    private static void MeasureShellEdge(
        ModuleWindowsViewModel windows,
        IRuntimeCommand command,
        IClassicDesktopStyleApplicationLifetime desktop)
    {
        if (desktop.MainWindow is not { } shell)
        {
            return;
        }

        // 把外壳放到确定位置，便于读数
        shell.Position = new PixelPoint(140, 160);

        DispatcherTimer.RunOnce(
            () =>
            {
                _ = command.AddModule("selftest-c", "hyw/text-sender/1.0.0");
                windows.Refresh();

                if (windows.Modules.FirstOrDefault(m => m.InstanceId == "selftest-c") is not { } module
                    || windows.ShellRect is not { IsValid: true } shellRect)
                {
                    _ = DragDiagnostics.Flush("外壳边缘量测失败：模块或外壳矩形不可用");
                    return;
                }

                // 目标：让模块左边缘落在"外壳右边缘 + 3px"，由吸附把它吸到边缘上
                const double gap = 3;
                var startX = shellRect.Right + 40;
                _ = command.UpdateModuleLayout(new ModuleLayoutView("selftest-c", startX, shellRect.Y + 10, module.Width, module.Height, true));
                windows.Refresh();

                var grabX = startX + 10;
                var grabY = module.Y + 10;

                if (windows.BeginDrag("selftest-c", grabX, grabY, independent: true))
                {
                    windows.DragTo(shellRect.Right + gap + 10, grabY);
                    windows.EndDrag();
                }

                var scaling = shell.RenderScaling <= 0 ? 1.0 : shell.RenderScaling;
                var clientRight = shell.PointToScreen(new Point(0, 0)).X + (shell.ClientSize.Width * scaling);
                var after = windows.Modules.First(m => m.InstanceId == "selftest-c");

                DragDiagnostics.Record(
                    $"[外壳边缘量测] 参照右边缘(可见外框)={shellRect.Right:0.#} 客户区右边缘={clientRight:0.#} "
                    + $"两者之差={shellRect.Right - clientRight:0.#}（修复前参照取后者 ⇒ 这就是「缝」）");
                DragDiagnostics.Record(
                    $"[外壳边缘量测] 模块左边缘={after.X:0.#} → 与参照边缘之差={after.X - shellRect.Right:0.#}"
                    + $"（应为 0）；与客户区边缘之差={after.X - clientRight:0.#}");

                var path = DragDiagnostics.Flush("自检：外壳右边缘吸附量测（修\"缝\"的证据）");
                Console.WriteLine($"[selftest] 外壳边缘量测日志：{path}");
                Console.Out.Flush();
            },
            TimeSpan.FromSeconds(1));
    }

    /// <summary>摆两个模块：先隔开（≈430px），随后把左侧那个拖到右侧那个旁边（"靠很近"）。</summary>
    private static void Arrange(ModuleWindowsViewModel windows, IRuntimeCommand command)
    {
        _ = command.AddModule("selftest-a", "hyw/text-sender/1.0.0");
        _ = command.AddModule("selftest-b", "hyw/data-monitor/1.0.0");
        windows.Refresh();

        _ = command.UpdateModuleLayout(new ModuleLayoutView("selftest-a", 200, 200, 413, 413, true));
        _ = command.UpdateModuleLayout(new ModuleLayoutView("selftest-b", 1050, 200, 450, 575, true));
        windows.Refresh();

        DragDiagnostics.Record($"自检摆放：模块数={windows.Modules.Count}（{string.Join(",", windows.Modules.Select(m => m.InstanceId + ":" + m.X))}）");
        RecordWindowFacts(windows, "摆放后");
    }

    /// <summary>
    /// 脚本化拖动 A → 靠近 B → 在"中线附近"用 ±1px 抖动来回抖 40 次（用户描述的场景）。
    /// </summary>
    private static void Drag(ModuleWindowsViewModel windows, IClassicDesktopStyleApplicationLifetime desktop)
    {
        if (windows.Modules.FirstOrDefault(m => m.InstanceId == "selftest-a") is not { } a)
        {
            _ = DragDiagnostics.Flush("自检失败：找不到 selftest-a");
            return;
        }

        // 指针起点取 A 的"上边框带"内侧（与用户抓住边框带拖动一致）
        var pointerX = a.X + (a.Width / 2);
        var pointerY = a.Y + 3;

        if (!windows.BeginDrag("selftest-a", pointerX, pointerY, independent: false))
        {
            _ = DragDiagnostics.Flush("自检失败：BeginDrag 返回 false");
            return;
        }

        var step = 0;

        // 第一段：平滑靠近（每次 4px，共 100 次 ≈ 400px）
        // 第二段：在中线附近做 ±1px 抖动（模拟手抖/系统量化）
        var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(8) };
        timer.Tick += (_, _) =>
        {
            step++;

            if (step <= 100)
            {
                pointerX += 4;
            }
            else if (step <= 140)
            {
                pointerX += (step % 2 == 0) ? 1 : -1;   // ±1px 抖动
            }
            else
            {
                timer.Stop();
                windows.EndDrag();
                RecordWindowFacts(windows, "拖动结束");
                var path = DragDiagnostics.Flush("自检：A 靠近 B + 中线附近 ±1px 抖动");
                Console.WriteLine($"[selftest] 诊断日志：{path}");
                Console.Out.Flush();
                return;
            }

            windows.DragTo(pointerX, pointerY);
            RecordWindowFacts(windows, $"step={step}");
        };

        timer.Start();
        _ = desktop;
    }

    /// <summary>把**窗口实际几何**也记下来（模型尺寸 vs 窗口尺寸是"尺寸不匹配"的直接证据）。</summary>
    private static void RecordWindowFacts(ModuleWindowsViewModel windows, string label)
    {
        if (Application.Current?.ApplicationLifetime is not IClassicDesktopStyleApplicationLifetime desktop)
        {
            return;
        }

        foreach (var window in desktop.Windows)
        {
            if (window.DataContext is not ModuleViewViewModel module)
            {
                continue;
            }

            var scaling = window.RenderScaling <= 0 ? 1.0 : window.RenderScaling;

            DragDiagnostics.Record(
                $"[{label}] 窗口 {module.InstanceId} pos=({window.Position.X},{window.Position.Y}) "
                + $"客户区×缩放=({window.ClientSize.Width * scaling:0.#}×{window.ClientSize.Height * scaling:0.#}) "
                + $"缩放={scaling:0.###} 模型=({module.X:0.#},{module.Y:0.#},{module.Width:0.#}×{module.Height:0.#}) "
                + $"差=(X Δ{window.Position.X - module.X:0.#}, Y Δ{window.Position.Y - module.Y:0.#}, "
                + $"W Δ{window.ClientSize.Width * scaling - module.Width:0.#}, H Δ{window.ClientSize.Height * scaling - module.Height:0.#})");
        }
    }
}
