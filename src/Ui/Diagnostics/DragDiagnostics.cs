using System.Globalization;
using System.Text;

namespace HYWDebugAssistant.Ui.Diagnostics;

/// <summary>
/// **临时诊断**（用户第十二轮第二轮反馈："还是振荡；**两个窗口靠很近时中线对齐才会抖，远一点就不抖**"）。
/// </summary>
/// <remarks>
/// 目的：把一次拖动**每一帧**的原始数据落盘，供离线定位"抖"的来源。记录三类事实：
/// <list type="number">
///   <item><b>指针</b>：系统光标 vs "窗口位置 + 局部坐标换算"，两者之差即回授误差；</item>
///   <item><b>几何</b>：模型几何（吸附前/后）与窗口实际位置/客户区尺寸 → 验证"模型尺寸 = 窗口尺寸"；</item>
///   <item><b>邻居</b>：拖动期间**其他模块是否被回写**（帧间移动的邻居会把贴着它的模块一起带抖）。</item>
/// </list>
/// **定位完成后本文件整体删除。**
/// </remarks>
public static class DragDiagnostics
{
    private const int Capacity = 400;
    private const long MaxLogBytes = 512 * 1024;

    private static readonly Queue<string> Frames = new();
    private static readonly string FilePath = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "hyw-drag-diag.log");

    /// <summary>日志文件路径（供状态栏提示）。</summary>
    public static string LogFilePath => FilePath;

    /// <summary>记录一行（保留最近 <see cref="Capacity"/> 行）。</summary>
    /// <param name="line">内容。</param>
    public static void Record(string line)
    {
        ArgumentNullException.ThrowIfNull(line);

        Frames.Enqueue(string.Format(CultureInfo.InvariantCulture, "{0:HH:mm:ss.fff} {1}", DateTime.Now, line));
        while (Frames.Count > Capacity)
        {
            _ = Frames.Dequeue();
        }
    }

    /// <summary>记录一条**邻居被回写**的事实（拖动期间才记）。</summary>
    /// <param name="instanceId">被回写的实例。</param>
    /// <param name="beforeX">回写前 X。</param>
    /// <param name="beforeY">回写前 Y。</param>
    /// <param name="afterX">回写后 X。</param>
    /// <param name="afterY">回写后 Y。</param>
    public static void RecordNeighbourWriteBack(string instanceId, double beforeX, double beforeY, double afterX, double afterY) =>
        Record($"!!邻居被回写 {instanceId} ({beforeX:0.#},{beforeY:0.#})→({afterX:0.#},{afterY:0.#})");

    /// <summary>清空缓冲区（一次拖动开始时调用）。</summary>
    public static void Reset() => Frames.Clear();

    /// <summary>
    /// 把本次拖动的全部帧**追加**落盘（拖动结束时调用）；返回日志路径（失败时返回 null）。
    /// </summary>
    /// <param name="session">本次拖动的一句话摘要（写进标题）。</param>
    /// <returns>日志路径。</returns>
    public static string? Flush(string session)
    {
        ArgumentNullException.ThrowIfNull(session);

        try
        {
            var text = new StringBuilder();
            _ = text.AppendLine();
            _ = text.AppendLine(CultureInfo.InvariantCulture, $"===== 拖动会话 {DateTime.Now:yyyy-MM-dd HH:mm:ss} · {session} =====");
            _ = text.AppendLine("# cursor=系统光标 derived=窗口位置+局部坐标换算 err=两者之差 ptrΔ=指针位移");
            _ = text.AppendLine("# raw=吸附前目标 model=吸附后模型 win=窗口实际(位置+客户区×缩放) sticky=当前粘滞候选");
            _ = text.AppendLine("# group=本次拖动组（含自身）neighbours=参与吸附的其他模块数");

            foreach (var frame in Frames)
            {
                _ = text.AppendLine(frame);
            }

            if (File.Exists(FilePath) && new FileInfo(FilePath).Length > MaxLogBytes)
            {
                File.Delete(FilePath);
            }

            File.AppendAllText(FilePath, text.ToString(), Encoding.UTF8);
            return FilePath;
        }
        catch (IOException)
        {
            return null;
        }
        catch (UnauthorizedAccessException)
        {
            return null;
        }
    }

    /// <summary>格式化一帧（窗口侧：指针与窗口几何）。</summary>
    public static string Format(
        (double X, double Y) cursor,
        (double X, double Y) derived,
        (double X, double Y) pointerDelta,
        (double X, double Y, double W, double H) modelBefore,
        (double X, double Y, double W, double H) module,
        (int X, int Y) windowPosition,
        (double W, double H) windowSize,
        string sticky)
    {
        return string.Format(
            CultureInfo.InvariantCulture,
            "cursor=({0:0.#},{1:0.#}) derived=({2:0.#},{3:0.#}) err=({4:0.#},{5:0.#}) ptrΔ=({6:0.#},{7:0.#}) "
            + "model=({8:0.#},{9:0.#},{10:0.#}×{11:0.#})→({12:0.#},{13:0.#},{14:0.#}×{15:0.#}) "
            + "win=({16},{17},{18:0.#}×{19:0.#}) sticky={20}",
            cursor.X, cursor.Y,
            derived.X, derived.Y,
            derived.X - cursor.X, derived.Y - cursor.Y,
            pointerDelta.X, pointerDelta.Y,
            modelBefore.X, modelBefore.Y, modelBefore.W, modelBefore.H,
            module.X, module.Y, module.W, module.H,
            windowPosition.X, windowPosition.Y, windowSize.W, windowSize.H,
            sticky);
    }

    /// <summary>格式化一帧（控制器侧：原始目标与吸附结果）。</summary>
    public static string FormatSnap(
        (double X, double Y) raw,
        double finalX,
        double finalY,
        double appliedDx,
        double appliedDy,
        string sticky,
        int groupSize,
        int neighbourCount) =>
        string.Format(
            CultureInfo.InvariantCulture,
            "raw=({0:0.#},{1:0.#}) → ({2:0.#},{3:0.#}) Δ=({4:0.#},{5:0.#}) sticky={6} group={7} neighbours={8}",
            raw.X, raw.Y, finalX, finalY, appliedDx, appliedDy, sticky, groupSize, neighbourCount);
}
