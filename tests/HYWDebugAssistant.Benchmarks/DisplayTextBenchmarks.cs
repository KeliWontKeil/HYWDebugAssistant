using BenchmarkDotNet.Attributes;
using HYWDebugAssistant.Core.Api;
using HYWDebugAssistant.Sdk.Views;

namespace HYWDebugAssistant.Benchmarks;

/// <summary>
/// 清单：**显示文本追加的成本随缓冲长度如何变化**（高频长时间显示的核心量）。
/// </summary>
/// <remarks>
/// <list type="bullet">
///   <item><b>不换行路径</b>（<see cref="AppendTextIntoOpenLine"/>）：整场会话只有一条逻辑行，
///     这是数据显示器的默认形态；</item>
///   <item><b>换行路径</b>（<see cref="AppendLinePerPacket"/>）：每包一条，到顶后删头追尾；</item>
///   <item><b>增量读取</b>（<see cref="ReadDeltaPerFrame"/>）：视图每帧只取新增部分。</item>
/// </list>
/// <para>数值只记录、不设阈值；判据是"追加成本不随已显示长度增长"。</para>
/// </remarks>
[MemoryDiagnoser]
public class DisplayTextBenchmarks
{
    private const int Packets = 20_000;
    private const int PacketChars = 64;

    private readonly string _packet = new('x', PacketChars);
    private DisplayBuffer _buffer = null!;
    private TextCursor _cursor;
    private readonly List<string> _scratch = [];

    [GlobalSetup]
    public void Setup()
    {
        _buffer = new DisplayBuffer();
        _cursor = TextCursor.Empty;
    }

    /// <summary>不换行：把 2 万个包追加到同一条逻辑行。</summary>
    [Benchmark(OperationsPerInvoke = Packets)]
    public int AppendTextIntoOpenLine()
    {
        for (var index = 0; index < Packets; index++)
        {
            _buffer.AppendText(_packet);
        }

        return _buffer.TotalChars;
    }

    /// <summary>每包一行：到顶后删头追尾（容量按行）。</summary>
    [Benchmark(OperationsPerInvoke = Packets)]
    public int AppendLinePerPacket()
    {
        for (var index = 0; index < Packets; index++)
        {
            _buffer.AppendLine(_packet);
        }

        return _buffer.DroppedCount;
    }

    /// <summary>视图侧：每帧读一次窗口并把游标推到位。</summary>
    [Benchmark(OperationsPerInvoke = Packets)]
    public int ReadWindowPerFrame()
    {
        var appended = 0;

        for (var index = 0; index < Packets; index++)
        {
            _buffer.AppendText(_packet);

            var window = _buffer.ReadWindow(_cursor, _scratch);
            _cursor = new TextCursor(
                window.Version, window.Dropped, window.SealedCount, window.OpenLength, window.OpenVersion);
            appended += window.OpenSuffix?.Length ?? 0;
        }

        return appended;
    }
}
