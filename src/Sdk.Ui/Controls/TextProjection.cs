using System.Globalization;
using System.Text;
using HYWDebugAssistant.Sdk.Text;

namespace HYWDebugAssistant.Sdk.Ui.Controls;

/// <summary>投影段种类。</summary>
internal enum TextProjectionKind
{
    /// <summary>普通可见文字。</summary>
    Literal = 0,

    /// <summary>警示色 <c>⟨XX⟩</c> 标记。</summary>
    Marker = 1,

    /// <summary>方框占位符。</summary>
    Box = 2,
}

/// <summary>投影里的一段：源区间 → 显示区间。</summary>
internal readonly struct TextProjectionSegment
{
    public TextProjectionSegment(
        int rawStart,
        int rawLength,
        int displayStart,
        int displayLength,
        TextProjectionKind kind)
    {
        RawStart = rawStart;
        RawLength = rawLength;
        DisplayStart = displayStart;
        DisplayLength = displayLength;
        Kind = kind;
    }

    public int RawStart { get; }

    public int RawLength { get; }

    public int DisplayStart { get; }

    public int DisplayLength { get; }

    public TextProjectionKind Kind { get; }

    public int RawEnd => RawStart + RawLength;

    public int DisplayEnd => DisplayStart + DisplayLength;
}

/// <summary>一个显示在文本面上的标记区间（显示坐标）。</summary>
internal readonly record struct TextMarkerSpan(int Start, int Length);

/// <summary>
/// **原始文本 → 显示投影**：只把真实不可显示字符替换成 <c>⟨XX⟩</c> / <c>□</c>。
/// </summary>
/// <remarks>
/// 它同时提供源↔显示的边界映射，供只读选择/命中和可编辑输入的光标/差分使用。
/// 普通文本走等长段；标记段长度可以不同，但整段是一个原子区间。
/// </remarks>
internal sealed class TextProjection
{
    private readonly List<TextProjectionSegment> _segments = [];
    private readonly StringBuilder _display = new();
    private string? _displayText;

    private TextProjection(bool markers)
    {
        Markers = markers;
    }

    /// <summary>是否显示警示色标记；<see langword="false"/> 时用方框。</summary>
    public bool Markers { get; }

    /// <summary>是否含至少一个标记/方框段。</summary>
    public bool HasMarkers { get; private set; }

    /// <summary>原始文本长度（UTF-16 字符数）。</summary>
    public int RawLength { get; private set; }

    /// <summary>显示文本长度。</summary>
    public int DisplayLength => _display.Length;

    /// <summary>显示文本（缓存；追加后失效）。</summary>
    public string Display => _displayText ??= _display.ToString();

    /// <summary>全部投影段（按显示顺序）。</summary>
    public IReadOnlyList<TextProjectionSegment> Segments => _segments;

    /// <summary>构造一份完整投影。</summary>
    public static TextProjection Create(string raw, bool markers)
    {
        var projection = new TextProjection(markers);
        projection.Append(raw);
        return projection;
    }

    /// <summary>追加一段源文本（只读面按行追加用）。</summary>
    public void Append(string suffix)
    {
        if (string.IsNullOrEmpty(suffix))
        {
            return;
        }

        var index = 0;

        while (index < suffix.Length)
        {
            var clusterStart = index;
            var clusterLength = ReadClusterLength(suffix.AsSpan(), index);

            if (clusterLength <= 0)
            {
                clusterLength = 1;
            }

            var cluster = suffix.AsSpan(clusterStart, clusterLength);
            var first = Rune.DecodeFromUtf16(cluster, out var rune, out _) == System.Buffers.OperationStatus.Done
                ? rune
                : new Rune('\uFFFD');

            if (TextControlCharacters.NeedsPlaceholder(first))
            {
                AppendMarker(cluster);
            }
            else
            {
                AppendLiteral(cluster);
            }

            index += clusterLength;
        }

        _displayText = null;
    }

    /// <summary>把显示偏移映射回源偏移。</summary>
    /// <param name="displayOffset">显示偏移（0…DisplayLength）。</param>
    /// <param name="preferRight">落在标记内部时是否取标记的右边界。</param>
    public int DisplayToRaw(int displayOffset, bool preferRight = false)
    {
        if (displayOffset <= 0)
        {
            return 0;
        }

        if (displayOffset >= DisplayLength)
        {
            return RawLength;
        }

        var segment = FindSegmentByDisplay(displayOffset);

        if (segment is not { } value)
        {
            return RawLength;
        }

        if (value.Kind != TextProjectionKind.Literal)
        {
            if (displayOffset <= value.DisplayStart)
            {
                return value.RawStart;
            }

            if (displayOffset >= value.DisplayEnd)
            {
                return value.RawEnd;
            }

            return preferRight ? value.RawEnd : value.RawStart;
        }

        return Math.Clamp(value.RawStart + (displayOffset - value.DisplayStart), value.RawStart, value.RawEnd);
    }

    /// <summary>把源偏移映射到显示偏移。</summary>
    /// <param name="rawOffset">源偏移（0…RawLength）。</param>
    /// <param name="preferRight">落在标记内部或边界时是否取标记的右边界。</param>
    public int RawToDisplay(int rawOffset, bool preferRight = true)
    {
        if (rawOffset <= 0)
        {
            return 0;
        }

        if (rawOffset >= RawLength)
        {
            return DisplayLength;
        }

        var segment = FindSegmentByRaw(rawOffset);

        if (segment is not { } value)
        {
            return DisplayLength;
        }

        if (value.Kind != TextProjectionKind.Literal)
        {
            if (rawOffset <= value.RawStart)
            {
                return value.DisplayStart;
            }

            if (rawOffset >= value.RawEnd)
            {
                return value.DisplayEnd;
            }

            return preferRight ? value.DisplayEnd : value.DisplayStart;
        }

        return Math.Clamp(value.DisplayStart + (rawOffset - value.RawStart), value.DisplayStart, value.DisplayEnd);
    }

    /// <summary>一个源区间对应的显示区间（标记段按整段扩展）。</summary>
    public (int Start, int Length) RawRangeToDisplay(int rawStart, int rawEnd)
    {
        rawStart = Math.Clamp(rawStart, 0, RawLength);
        rawEnd = Math.Clamp(rawEnd, rawStart, RawLength);

        if (rawEnd <= rawStart)
        {
            var point = RawToDisplay(rawStart, preferRight: false);
            return (point, 0);
        }

        var start = RawToDisplay(rawStart, preferRight: false);
        var end = RawToDisplay(rawEnd, preferRight: true);
        return (start, Math.Max(0, end - start));
    }

    /// <summary>一个显示区间对应的源区间；部分覆盖标记时扩展到整标记。</summary>
    public (int Start, int End) DisplayRangeToRaw(int displayStart, int displayEnd)
    {
        displayStart = Math.Clamp(displayStart, 0, DisplayLength);
        displayEnd = Math.Clamp(displayEnd, displayStart, DisplayLength);

        var start = DisplayToRaw(displayStart, preferRight: false);
        var end = DisplayToRaw(displayEnd, preferRight: true);

        if (displayStart == displayEnd
            && TryGetSegmentByDisplay(displayStart, out var segment)
            && segment.Kind != TextProjectionKind.Literal
            && displayStart > segment.DisplayStart
            && displayStart < segment.DisplayEnd)
        {
            // 光标落在标记内部时，编辑应替换整个不可显示字符。
            start = segment.RawStart;
            end = segment.RawEnd;
        }

        return (start, Math.Max(start, end));
    }

    /// <summary>是否落在某个标记/方框内部（不含两端边界）。</summary>
    public bool IsInsideMarker(int displayOffset)
    {
        return TryGetSegmentByDisplay(displayOffset, out var segment)
            && segment.Kind != TextProjectionKind.Literal
            && displayOffset > segment.DisplayStart
            && displayOffset < segment.DisplayEnd;
    }

    /// <summary>取警示色标记段（显示坐标）。</summary>
    public IReadOnlyList<TextMarkerSpan> GetMarkerSpans()
    {
        if (!HasMarkers)
        {
            return [];
        }

        var spans = new List<TextMarkerSpan>();

        foreach (var segment in _segments)
        {
            if (segment.Kind == TextProjectionKind.Marker)
            {
                spans.Add(new TextMarkerSpan(segment.DisplayStart, segment.DisplayLength));
            }
        }

        return spans;
    }

    private void AppendLiteral(ReadOnlySpan<char> cluster)
    {
        var rawStart = RawLength;
        var displayStart = _display.Length;
        var length = cluster.Length;

        if (_segments.Count > 0)
        {
            var last = _segments[^1];

            if (last.Kind == TextProjectionKind.Literal
                && last.RawEnd == rawStart
                && last.DisplayEnd == displayStart)
            {
                _segments[^1] = new TextProjectionSegment(
                    last.RawStart,
                    last.RawLength + length,
                    last.DisplayStart,
                    last.DisplayLength + length,
                    TextProjectionKind.Literal);
                RawLength += length;
                _ = _display.Append(cluster);
                return;
            }
        }

        _segments.Add(new TextProjectionSegment(
            rawStart,
            length,
            displayStart,
            length,
            TextProjectionKind.Literal));

        RawLength += length;
        _ = _display.Append(cluster);
    }

    private void AppendMarker(ReadOnlySpan<char> cluster)
    {
        var displayStart = _display.Length;
        var kind = Markers ? TextProjectionKind.Marker : TextProjectionKind.Box;

        if (Markers)
        {
            TextControlCharacters.AppendClusterMarker(_display, cluster);
        }
        else
        {
            _ = _display.Append(TextControlCharacters.Placeholder);
        }

        _segments.Add(new TextProjectionSegment(
            RawLength,
            cluster.Length,
            displayStart,
            _display.Length - displayStart,
            kind));

        RawLength += cluster.Length;
        HasMarkers = true;
    }

    private TextProjectionSegment? FindSegmentByDisplay(int offset)
    {
        var low = 0;
        var high = _segments.Count - 1;
        TextProjectionSegment? found = null;

        while (low <= high)
        {
            var middle = low + ((high - low) / 2);
            var segment = _segments[middle];

            if (segment.DisplayStart <= offset)
            {
                found = segment;
                low = middle + 1;
            }
            else
            {
                high = middle - 1;
            }
        }

        return found;
    }

    private TextProjectionSegment? FindSegmentByRaw(int offset)
    {
        var low = 0;
        var high = _segments.Count - 1;
        TextProjectionSegment? found = null;

        while (low <= high)
        {
            var middle = low + ((high - low) / 2);
            var segment = _segments[middle];

            if (segment.RawStart <= offset)
            {
                found = segment;
                low = middle + 1;
            }
            else
            {
                high = middle - 1;
            }
        }

        return found;
    }

    private bool TryGetSegmentByDisplay(int offset, out TextProjectionSegment segment)
    {
        var found = FindSegmentByDisplay(offset);

        if (found is { } value)
        {
            segment = value;
            return true;
        }

        segment = default;
        return false;
    }

    private static int ReadClusterLength(ReadOnlySpan<char> text, int index)
    {
        if (index >= text.Length)
        {
            return 0;
        }

        var status = Rune.DecodeFromUtf16(text[index..], out var rune, out var consumed);

        if (status != System.Buffers.OperationStatus.Done)
        {
            return 1;
        }

        var length = consumed;
        var firstValue = rune.Value;
        var joinNext = false;

        while (index + length < text.Length)
        {
            var nextStatus = Rune.DecodeFromUtf16(text[(index + length)..], out var next, out var nextConsumed);

            if (nextStatus != System.Buffers.OperationStatus.Done)
            {
                break;
            }

            var category = Rune.GetUnicodeCategory(next);
            var join = joinNext
                || category is UnicodeCategory.NonSpacingMark
                    or UnicodeCategory.SpacingCombiningMark
                    or UnicodeCategory.EnclosingMark
                || next.Value is '\u200D'
                    or >= 0x1F3FB and <= 0x1F3FF
                    or >= 0xE0020 and <= 0xE007F
                    or >= 0xFE00 and <= 0xFE0F
                    or >= 0xE0100 and <= 0xE01EF
                || (firstValue is >= 0x1F1E6 and <= 0x1F1FF
                    && next.Value is >= 0x1F1E6 and <= 0x1F1FF);

            if (!join)
            {
                break;
            }

            joinNext = next.Value == '\u200D';
            length += nextConsumed;

            if (joinNext && index + length >= text.Length)
            {
                break;
            }
        }

        return length;
    }
}
