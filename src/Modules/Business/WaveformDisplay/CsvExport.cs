using System.Globalization;
using System.Text;

namespace HYWDebugAssistant.Modules.WaveformDisplay;

/// <summary>活动组 CSV 的分批导出器（宽表；每 Tick 写有限行，不长时间阻塞接收）。</summary>
internal sealed class CsvExport : IDisposable
{
    private readonly StreamWriter _writer;
    private readonly DisplayGroupRuntime _group;
    private readonly string[] _channelNames;
    private readonly double _rate;
    private readonly long _end;
    private readonly StringBuilder _row = new(256);

    private long _index;

    public CsvExport(string path, DisplayGroupRuntime group, int channelCount, string[] channelNames, double rate, long start, long end)
    {
        _group = group;
        _channelNames = channelNames;
        _rate = rate;
        _end = end;
        _index = start;
        TotalRows = Math.Max(0, end - start + 1);

        _writer = new StreamWriter(path, append: false, new UTF8Encoding(encoderShouldEmitUTF8Identifier: true));
        _writer.Write("sample_index,time_seconds");
        for (var index = 0; index < channelCount; index++)
        {
            _writer.Write(',');
            _writer.Write(Escape(index < channelNames.Length ? channelNames[index] : "CH" + (index + 1).ToString(CultureInfo.InvariantCulture)));
        }

        _writer.WriteLine();
    }

    public long TotalRows { get; }

    public long WrittenRows { get; private set; }

    public bool IsComplete => _index > _end;

    /// <summary>写一批行；返回是否完成。异常由调用方处理。</summary>
    public bool WriteChunk(int maxRows)
    {
        for (var rowIndex = 0; rowIndex < maxRows && _index <= _end; rowIndex++, _index++, WrittenRows++)
        {
            _row.Clear();
            _row.Append(_index.ToString(CultureInfo.InvariantCulture));
            _row.Append(',');
            _row.Append(_rate > 0 ? (_index / _rate).ToString("0.######", CultureInfo.InvariantCulture) : "0");

            for (var channel = 0; channel < _group.ChannelCount; channel++)
            {
                _row.Append(',');
                if (channel >= _group.Histories.Length || _index >= _group.Histories[channel].Count)
                {
                    continue;
                }

                var value = _group.Histories[channel][_index];
                if (float.IsFinite(value))
                {
                    _row.Append(value.ToString("R", CultureInfo.InvariantCulture));
                }
            }

            _writer.WriteLine(_row);
        }

        return _index > _end;
    }

    public void Dispose() => _writer.Dispose();

    private static string Escape(string text)
    {
        if (text.IndexOfAny([',', '"', '\r', '\n']) < 0)
        {
            return text;
        }

        return '"' + text.Replace("\"", "\"\"", StringComparison.Ordinal) + '"';
    }
}
