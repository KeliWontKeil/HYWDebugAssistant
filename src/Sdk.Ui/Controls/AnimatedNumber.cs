using System.Diagnostics;

namespace HYWDebugAssistant.Sdk.Ui.Controls;

/// <summary>显示值向目标值的逐帧逼近（数据显示控件共用）。
/// </summary>
/// <remarks>
/// 只做"显示值追目标值"一件事：目标值由模块发布、视图推进来。控件不判断"有没有数据"，
/// 也不另起时钟——每次由框架的每帧 <c>Refresh</c> 调 <see cref="Advance"/>；
/// 差值落到阈值以下就对齐并停止返回变化，视觉不再重绘。
/// </remarks>
internal sealed class AnimatedNumber
{
    /// <summary>单帧最大步长（毫秒）：窗口隐藏后重新可见时不能一步跳到底。</summary>
    private const double MaxStepMs = 100;

    private long _lastTimestamp;
    private bool _tracking;

    /// <summary>当前显示值。</summary>
    public double Value { get; private set; }

    /// <summary>直接落到某个值（不做动画，下一次 <see cref="Advance"/> 重新计时）。</summary>
    /// <param name="value">落点。</param>
    public void Snap(double value)
    {
        Value = value;
        _tracking = false;
    }

    /// <summary>推进一帧；返回是否真的改变了显示值（只有改变才需要重绘）。</summary>
    /// <param name="target">目标值（非有限值被忽略：目标值的合法性由模块负责）。</param>
    /// <param name="smoothing">是否平滑。</param>
    /// <param name="timeConstantMs">时间常数（毫秒；&lt;= 0 视为不平滑）。</param>
    /// <param name="epsilon">对齐阈值（与值同单位）。</param>
    /// <returns>显示值是否改变。</returns>
    public bool Advance(double target, bool smoothing, double timeConstantMs, double epsilon)
    {
        if (!double.IsFinite(target))
        {
            return false;
        }

        var now = Stopwatch.GetTimestamp();
        var smooth = smoothing && timeConstantMs > 0 && double.IsFinite(timeConstantMs);

        if (!_tracking)
        {
            // 第一帧直接落到目标值：窗口刚打开就显示当前数据，而不是从 0 开始扫一遍。
            _tracking = true;
            _lastTimestamp = now;
            return Move(target);
        }

        var dtMs = (now - _lastTimestamp) * 1000.0 / Stopwatch.Frequency;
        _lastTimestamp = now;
        if (!(dtMs > 0))
        {
            return false;
        }

        if (dtMs > MaxStepMs)
        {
            dtMs = MaxStepMs;
        }

        if (!smooth)
        {
            return Move(target);
        }

        var gap = target - Value;
        if (Math.Abs(gap) <= epsilon)
        {
            return Move(target);
        }

        var alpha = 1 - Math.Exp(-dtMs / timeConstantMs);
        var next = Value + (gap * alpha);
        if (Math.Abs(target - next) <= epsilon)
        {
            next = target;
        }

        if (next == Value)
        {
            return false;
        }

        Value = next;
        return true;
    }

    private bool Move(double target)
    {
        if (target == Value)
        {
            return false;
        }

        Value = target;
        return true;
    }
}
