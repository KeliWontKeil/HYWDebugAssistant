using System.Diagnostics;
using HYWDebugAssistant.Core.Api;
using HYWDebugAssistant.Sdk.Views;
using Shouldly;
using Xunit;

namespace HYWDebugAssistant.Tests;

/// <summary>
/// 显示缓冲：**追加摊还常数、总量有界、增量与全量一致**。
/// </summary>
/// <remarks>
/// 这里锁住的是"不换行显示"那条路的形态：整场会话只有一条逻辑行。
/// 对同一条行做 <c>+=</c> 的代价随行长度平方增长：高频数据下内存与卡顿都由这条路径决定。
/// </remarks>
public class DisplayBufferTests
{
    [Fact]
    public void 连续追加同一行_应摊还常数且总量有界()
    {
        var buffer = new DisplayBuffer(maxChars: 64 * 1024);

        var before = GC.GetAllocatedBytesForCurrentThread();
        var watch = Stopwatch.StartNew();

        for (var index = 0; index < 20_000; index++)
        {
            buffer.AppendText(new string('x', 64));
        }

        watch.Stop();
        var allocated = GC.GetAllocatedBytesForCurrentThread() - before;

        buffer.TotalChars.ShouldBeLessThanOrEqualTo(64 * 1024, "总字符预算必须封顶");
        buffer.Snapshot().Count.ShouldBeLessThanOrEqualTo(2, "封口点只允许把当前行切成两段（不是每个包一条）");
        allocated.ShouldBeLessThan(8L * 1024 * 1024, "追加是摊还常数，不应产生与行长度成正比的垃圾");
        watch.Elapsed.ShouldBeLessThan(TimeSpan.FromSeconds(2));
    }

    [Fact]
    public void 不换行追加_仍是一条逻辑行()
    {
        var buffer = new DisplayBuffer();

        buffer.AppendText("AB");
        buffer.AppendText("CD");

        buffer.Snapshot().ShouldBe(["ABCD"]);
    }

    [Fact]
    public void 行容量到顶_应丢最旧并计数()
    {
        var buffer = new DisplayBuffer(capacity: 3);

        for (var index = 0; index < 5; index++)
        {
            buffer.AppendLine($"l{index}");
        }

        buffer.Snapshot().ShouldBe(["l2", "l3", "l4"]);
        buffer.DroppedCount.ShouldBe(2);
    }

    [Fact]
    public void 增量_应只给出新增部分()
    {
        var buffer = new DisplayBuffer();
        var scratch = new List<string>();

        var cursor = DisplayCursor.Empty;

        buffer.AppendText("AB");
        var first = buffer.ReadDelta(cursor, scratch);
        first.Reset.ShouldBeFalse();
        first.SealedCount.ShouldBe(0);
        first.OpenSuffix.ShouldBe("AB");
        cursor = first.Cursor;

        buffer.AppendText("CD");
        var second = buffer.ReadDelta(cursor, scratch);
        second.OpenSuffix.ShouldBe("CD", "世代未变 ⇒ 只给新增后缀，不是整行全文");
        cursor = second.Cursor;

        buffer.AppendLine("EF");
        var third = buffer.ReadDelta(cursor, scratch);
        scratch.ShouldBe(["ABCDEF"], "「追加一行」是把这一段接在当前行后并封口，不是另起一条");
        third.OpenText.ShouldBeEmpty();
        third.OpenGeneration.ShouldNotBe(second.OpenGeneration);
    }

    [Fact]
    public void 游标落后于裁剪_应从头重给封口行而不是要求全量重建()
    {
        var buffer = new DisplayBuffer(capacity: 2);
        var scratch = new List<string>();

        buffer.AppendLine("a");
        var cursor = buffer.ReadDelta(DisplayCursor.Empty, scratch).Cursor;

        buffer.AppendLine("b");
        buffer.AppendLine("c");
        buffer.AppendLine("d");

        var stale = buffer.ReadDelta(cursor, scratch);

        stale.Reset.ShouldBeFalse("封口行虽然全被丢掉了，当前内容仍然可以整段给出，不必退回全量快照");
        stale.SealedFromStart.ShouldBeTrue();
        scratch.ShouldBe(["c", "d"], "从头给的是**当前全部**封口行");
    }

    [Fact]
    public void 满载过_应记得满过_清空后复位()
    {
        var buffer = new DisplayBuffer(capacity: 2, maxChars: 8);
        buffer.HasTrimmed.ShouldBeFalse();

        buffer.AppendText("1234567890");

        buffer.HasTrimmed.ShouldBeTrue("当前行被头部裁剪也属于“满过”");

        buffer.Clear();
        buffer.HasTrimmed.ShouldBeFalse("清空之后这块缓冲说的是另一份内容");
    }

    [Fact]
    public void 增量与全量_在追加封口与裁剪混合下应始终一致()
    {
        var random = new Random(20261103);
        var buffer = new DisplayBuffer(capacity: 40, maxChars: 2048);
        var scratch = new List<string>();

        var sealedLines = new List<string>();
        var openText = string.Empty;
        var droppedApplied = 0;
        var cursor = DisplayCursor.Empty;

        for (var step = 0; step < 800; step++)
        {
            if (random.Next(3) == 0)
            {
                buffer.AppendLine(new string((char)('a' + random.Next(26)), random.Next(0, 70)));
            }
            else
            {
                buffer.AppendText(new string((char)('A' + random.Next(26)), random.Next(1, 90)));
            }

            var delta = buffer.ReadDelta(cursor, scratch);
            cursor = delta.Cursor;

            if (delta.Reset)
            {
                var snapshot = buffer.Snapshot();
                sealedLines.Clear();

                if (delta.Cursor.OpenLength > 0 && snapshot.Count > 0)
                {
                    sealedLines.AddRange(snapshot.Take(snapshot.Count - 1));
                    openText = snapshot[^1];
                }
                else
                {
                    sealedLines.AddRange(snapshot);
                    openText = string.Empty;
                }

                droppedApplied = delta.Dropped;
                continue;
            }

            if (delta.SealedFromStart)
            {
                sealedLines.Clear();
                sealedLines.AddRange(scratch);
                openText = delta.OpenText ?? openText + (delta.OpenSuffix ?? string.Empty);
                droppedApplied = delta.Dropped;
            }
            else
            {
                var removed = delta.Dropped - droppedApplied;
                if (removed > 0)
                {
                    sealedLines.RemoveRange(0, removed);
                    droppedApplied = delta.Dropped;
                }

                var index = 0;
                if (openText.Length > 0 && scratch.Count > 0)
                {
                    sealedLines.Add(scratch[0]);
                    openText = string.Empty;
                    index = 1;
                }

                for (; index < scratch.Count; index++)
                {
                    sealedLines.Add(scratch[index]);
                }

                if (delta.OpenText is not null)
                {
                    openText = delta.OpenText;
                }
                else if (!string.IsNullOrEmpty(delta.OpenSuffix))
                {
                    openText += delta.OpenSuffix;
                }
            }

            var expected = buffer.Snapshot();
            var actual = new List<string>(sealedLines);

            if (openText.Length > 0)
            {
                actual.Add(openText);
            }

            actual.ShouldBe(expected, $"第 {step} 步：增量拼出来的行必须与全量快照一致");
        }
    }
}
