using System.Text.Json.Serialization;

namespace HYWDebugAssistant.Modules.WaveformGenerator;

/// <summary>波形种类。</summary>
public enum WaveformKind
{
    /// <summary>正弦。</summary>
    Sine = 0,

    /// <summary>方波。</summary>
    Square = 1,

    /// <summary>三角。</summary>
    Triangle = 2,

    /// <summary>噪声。</summary>
    Noise = 3,

    /// <summary>正弦 PWM（开关波形）。</summary>
    Spwm = 4,

    /// <summary>空间矢量 PWM（开关波形）。</summary>
    Svpwm = 5,
}

/// <summary>单通道波形参数。</summary>
public sealed record GeneratorChannelConfig
{
    /// <summary>通道名（仅本模块 UI；不上线）。</summary>
    public string Name { get; init; } = "CH1";

    /// <summary>波形种类。</summary>
    public WaveformKind Kind { get; init; }

    /// <summary>相位（度）。</summary>
    public double PhaseDegrees { get; init; }

    /// <summary>直流偏移。</summary>
    public double Offset { get; init; }

    /// <summary>幅值。</summary>
    public double Amplitude { get; init; } = 1;

    /// <summary>每周期点数（2–4096）。</summary>
    public int Resolution { get; init; } = 100;

    /// <summary>方波占空比（0.01–0.99）。</summary>
    public double DutyCycle { get; init; } = 0.5;

    /// <summary>SPWM/SVPWM 载波频率（Hz；初始值，界面可改）。</summary>
    public double CarrierFrequencyHz { get; init; } = 100;

    /// <summary>SPWM/SVPWM 调制比（初始值，界面可改）。</summary>
    public double ModulationIndex { get; init; } = 0.8;

    /// <summary>SPWM/SVPWM 载波相位（度；初始值，界面可改）。</summary>
    public double CarrierPhaseDegrees { get; init; }

    /// <summary>是否启用。</summary>
    public bool Enabled { get; init; } = true;

    /// <summary>
    /// **配置迁移字段**：基波频率以组为唯一来源。
    /// 该字段只用于读取工作区中的通道频率；归一化后清空，写入时 null 不落盘。
    /// </summary>
    [JsonPropertyName("FrequencyHz")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public double? LegacyFrequencyHz { get; init; }
}

/// <summary>一个通道组。</summary>
public sealed record GeneratorGroupConfig
{
    /// <summary>组 ID（上线；模块内唯一）。</summary>
    public uint GroupId { get; init; } = 1;

    /// <summary>组名（仅本模块 UI；不上线）。</summary>
    public string Name { get; init; } = "G1";

    /// <summary>组是否启用。</summary>
    public bool Enabled { get; init; } = true;

    /// <summary>组基波频率（Hz）；组内所有通道共享。</summary>
    public double FrequencyHz { get; init; } = 10;

    /// <summary>通道列表。</summary>
    public List<GeneratorChannelConfig> Channels { get; init; } = [];
}

/// <summary>波形发生器配置（随组合件持久化）。</summary>
public sealed record GeneratorConfig
{
    /// <summary>组列表。</summary>
    public List<GeneratorGroupConfig> Groups { get; init; } = [];

    /// <summary>一次性发送周期数。</summary>
    public int OneShotPeriods { get; init; } = 5;

    /// <summary>活动组索引（UI 状态；越界时模块自行收敛）。</summary>
    public int ActiveGroupIndex { get; init; }

    /// <summary>活动通道索引（UI 状态）。</summary>
    public int ActiveChannelIndex { get; init; }

    /// <summary>组数上限（UI/配置的实际上限，不是数据帧上限）。</summary>
    public const int MaxGroups = 8;

    /// <summary>每组通道上限（与数据帧一致）。</summary>
    public const int MaxChannels = WaveformFrameContract.MaxChannels;

    /// <summary>单组帧率上限。</summary>
    public const double MaxGroupRateHz = 10_000;

    /// <summary>模块总请求帧率上限。</summary>
    public const double MaxTotalRateHz = 40_000;

    /// <summary>名字最大长度。</summary>
    public const int MaxNameLength = 32;

    /// <summary>造一个单组单通道的默认配置。</summary>
    public static GeneratorConfig CreateDefault() => new()
    {
        Groups =
        [
            new GeneratorGroupConfig
            {
                GroupId = 1,
                Name = "G1",
                FrequencyHz = 10,
                Channels = [new GeneratorChannelConfig { Name = "CH1" }],
            },
        ],
    };

    /// <summary>造一个默认通道。</summary>
    public static GeneratorChannelConfig CreateChannel(int index) => new()
    {
        Name = "CH" + (index + 1).ToString(System.Globalization.CultureInfo.InvariantCulture),
    };

    /// <summary>造一个默认组。</summary>
    public static GeneratorGroupConfig CreateGroup(uint groupId, int index) => new()
    {
        GroupId = groupId,
        Name = "G" + (index + 1).ToString(System.Globalization.CultureInfo.InvariantCulture),
        FrequencyHz = 10,
        Channels = [CreateChannel(0)],
    };
}
