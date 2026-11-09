namespace HYWDebugAssistant.Modules.WaveformGenerator;

/// <summary>波形数学与组帧率/一次性帧数计算（纯函数，便于测试）。</summary>
internal static class WaveformMath
{
    /// <summary>组帧率 = 组频率 × 启用通道 resolution 的最大值；没有启用通道/频率非法 = 0。</summary>
    public static double GroupRate(GeneratorGroupConfig group)
    {
        var frequency = group.FrequencyHz;
        if (!double.IsFinite(frequency) || frequency <= 0)
        {
            return 0;
        }

        var maxResolution = 0;
        foreach (var channel in group.Channels)
        {
            if (channel.Enabled && channel.Resolution > maxResolution)
            {
                maxResolution = channel.Resolution;
            }
        }

        return maxResolution * frequency;
    }

    /// <summary>一次性发送 N 个组基波周期所需帧数；向上取整。</summary>
    public static long OneShotFrames(GeneratorGroupConfig group, int periods)
    {
        var rate = GroupRate(group);
        var frequency = group.FrequencyHz;
        if (periods <= 0 || rate <= 0 || !double.IsFinite(frequency) || frequency <= 0)
        {
            return 0;
        }

        var frames = Math.Ceiling(periods * rate / frequency);
        return frames >= long.MaxValue ? long.MaxValue : (long)frames;
    }

    /// <summary>计算某通道在给定组频率与时间（秒）的样本值。</summary>
    public static float Sample(GeneratorChannelConfig channel, double groupFrequencyHz, double time, Random random)
    {
        var phase = groupFrequencyHz * time + (channel.PhaseDegrees / 360d);
        var fraction = phase - Math.Floor(phase);

        var shape = channel.Kind switch
        {
            WaveformKind.Sine => Math.Sin(2 * Math.PI * phase),
            WaveformKind.Square => fraction < channel.DutyCycle ? 1d : -1d,
            WaveformKind.Triangle => 1d - (4d * Math.Abs(fraction - 0.5)),
            WaveformKind.Noise => (random.NextDouble() * 2d) - 1d,
            WaveformKind.Spwm => Spwm(channel, groupFrequencyHz, time),
            WaveformKind.Svpwm => Svpwm(channel, groupFrequencyHz, time),
            _ => 0d,
        };

        return (float)(channel.Offset + (channel.Amplitude * shape));
    }

    private static double Spwm(GeneratorChannelConfig channel, double groupFrequencyHz, double time)
    {
        var angle = 2 * Math.PI * ((groupFrequencyHz * time) + (channel.PhaseDegrees / 360d));
        var reference = channel.ModulationIndex * Math.Sin(angle);
        return reference >= Carrier(channel, time) ? 1d : -1d;
    }

    private static double Svpwm(GeneratorChannelConfig channel, double groupFrequencyHz, double time)
    {
        var baseAngle = 2 * Math.PI * ((groupFrequencyHz * time) + (channel.PhaseDegrees / 360d));
        var a = Math.Sin(baseAngle);
        var b = Math.Sin(baseAngle - (2 * Math.PI / 3));
        var c = Math.Sin(baseAngle + (2 * Math.PI / 3));
        var common = -(Math.Max(a, Math.Max(b, c)) + Math.Min(a, Math.Min(b, c))) / 2;
        var reference = channel.ModulationIndex * (Math.Sin(baseAngle) + common);
        return reference >= Carrier(channel, time) ? 1d : -1d;
    }

    private static double Carrier(GeneratorChannelConfig channel, double time)
    {
        var phase = (channel.CarrierFrequencyHz * time) + (channel.CarrierPhaseDegrees / 360d);
        var fraction = phase - Math.Floor(phase);
        return (2d * Math.Abs((2d * fraction) - 1d)) - 1d;
    }

    /// <summary>波形种类的稳定键（供输入/JSON 使用）。</summary>
    public static string KindKey(WaveformKind kind) => kind switch
    {
        WaveformKind.Sine => "sine",
        WaveformKind.Square => "square",
        WaveformKind.Triangle => "triangle",
        WaveformKind.Noise => "noise",
        WaveformKind.Spwm => "spwm",
        WaveformKind.Svpwm => "svpwm",
        _ => "sine",
    };

    /// <summary>解析波形种类键；未知 = false。</summary>
    public static bool TryParseKind(string? key, out WaveformKind kind)
    {
        switch (key)
        {
            case "sine":
                kind = WaveformKind.Sine;
                return true;
            case "square":
                kind = WaveformKind.Square;
                return true;
            case "triangle":
                kind = WaveformKind.Triangle;
                return true;
            case "noise":
                kind = WaveformKind.Noise;
                return true;
            case "spwm":
                kind = WaveformKind.Spwm;
                return true;
            case "svpwm":
                kind = WaveformKind.Svpwm;
                return true;
            default:
                kind = WaveformKind.Sine;
                return false;
        }
    }

    /// <summary>是否 SPWM/SVPWM 类型。</summary>
    public static bool IsModulated(WaveformKind kind) =>
        kind is WaveformKind.Spwm or WaveformKind.Svpwm;
}
