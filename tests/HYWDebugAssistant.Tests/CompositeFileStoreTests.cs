using HYWDebugAssistant.Core.Persistence;
using Shouldly;
using Xunit;

namespace HYWDebugAssistant.Tests;

/// <summary>
/// 默认组合目录的文件列表：只收直接子目录的 <c>*.json</c>，按修改时间倒序；目录不存在为空。
/// </summary>
public class CompositeFileStoreTests
{
    [Fact]
    public void 扫描_只收json并按修改时间倒序()
    {
        var directory = Path.Combine(Path.GetTempPath(), "hyw-composite-files-" + Guid.NewGuid().ToString("N"));
        _ = Directory.CreateDirectory(directory);
        try
        {
            var older = Path.Combine(directory, "older.json");
            var newer = Path.Combine(directory, "newer.json");
            var ignored = Path.Combine(directory, "note.txt");
            File.WriteAllText(older, "{}");
            File.WriteAllText(newer, "{\"x\":1}");
            File.WriteAllText(ignored, "not a composite");
            File.SetLastWriteTimeUtc(older, new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc));
            File.SetLastWriteTimeUtc(newer, new DateTime(2026, 2, 1, 0, 0, 0, DateTimeKind.Utc));

            var files = CompositeFileStore.Scan(directory);

            files.Select(file => file.FileName).ShouldBe(["newer.json", "older.json"]);
            files.ShouldAllBe(file => file.Path.StartsWith(directory, StringComparison.OrdinalIgnoreCase));
            files[0].SizeBytes.ShouldBe(new FileInfo(newer).Length);
        }
        finally
        {
            try
            {
                Directory.Delete(directory, recursive: true);
            }
            catch (IOException)
            {
            }
        }
    }

    [Fact]
    public void 扫描_目录不存在应返回空列表()
    {
        CompositeFileStore.Scan(Path.Combine(Path.GetTempPath(), "hyw-missing-" + Guid.NewGuid().ToString("N")))
            .ShouldBeEmpty();
    }
}
