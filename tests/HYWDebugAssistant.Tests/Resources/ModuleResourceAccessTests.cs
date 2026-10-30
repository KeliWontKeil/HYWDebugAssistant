using HYWDebugAssistant.Sdk.Resources;
using Shouldly;
using Xunit;

namespace HYWDebugAssistant.Tests.Resources;

/// <summary>
/// **模块自有资源访问面**：只读、只读自己的包、相对路径不越界、失败不抛。
/// </summary>
/// <remarks>
/// 这套用例只碰 "目录 + 相对路径 → 文件" 这一层，不启动模块、不依赖真实包：
/// 临时目录就是"模块目录下的 resources/"，边界条件（越界 / 缺失 / 非法 kind）都靠它造。
/// </remarks>
public sealed class ModuleResourceAccessTests : IDisposable
{
    private readonly string _root;

    /// <summary>造一个临时"模块 resources 目录"。</summary>
    public ModuleResourceAccessTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "hyw-module-res-" + Guid.NewGuid().ToString("N"));

        var custom = Path.Combine(_root, "templates");
        _ = Directory.CreateDirectory(custom);
        File.WriteAllText(Path.Combine(custom, "greeting.txt"), "hello");

        _ = Directory.CreateDirectory(Path.Combine(_root, "templates", "nested"));
        File.WriteAllText(Path.Combine(_root, "templates", "nested", "deep.txt"), "deep");
    }

    /// <inheritdoc />
    public void Dispose()
    {
        try
        {
            Directory.Delete(_root, recursive: true);
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }

        GC.SuppressFinalize(this);
    }

    [Fact]
    public void 自定义kind_应按相对路径读到内容()
    {
        var access = new HYWDebugAssistant.Hosting.Context.ModuleResourceAccess(_root);

        access.TryOpen("templates", "greeting.txt", out var stream).ShouldBeTrue();
        using (stream)
        {
            using var reader = new StreamReader(stream!);
            reader.ReadToEnd().ShouldBe("hello");
        }

        access.TryOpen("templates", "nested/deep.txt", out var nested).ShouldBeTrue();
        using (nested)
        {
            using var reader = new StreamReader(nested!);
            reader.ReadToEnd().ShouldBe("deep");
        }

        access.ReadAllBytes("templates", "greeting.txt").ShouldBe("hello"u8.ToArray());
    }

    [Theory]
    [InlineData("templates", "../outside.txt")]
    [InlineData("templates", "nested/../../outside.txt")]
    [InlineData("templates", "/absolute.txt")]
    [InlineData("templates", "C:/windows/win.ini")]
    [InlineData("../templates", "greeting.txt")]
    [InlineData("", "greeting.txt")]
    public void 越界或非法路径_必须拒绝(string kind, string relativePath)
    {
        var access = new HYWDebugAssistant.Hosting.Context.ModuleResourceAccess(_root);

        access.TryOpen(kind, relativePath, out var stream).ShouldBeFalse();
        stream.ShouldBeNull();
    }

    [Fact]
    public void 缺文件_返回false不抛()
    {
        var access = new HYWDebugAssistant.Hosting.Context.ModuleResourceAccess(_root);

        access.TryOpen("templates", "absent.txt", out var stream).ShouldBeFalse();
        stream.ShouldBeNull();

        access.ReadAllBytes("templates", "absent.txt").ShouldBeNull();
    }

    [Fact]
    public void 未接线的回落_读不到东西也不抛()
    {
        ResourceAccess.Empty.TryOpen("templates", "greeting.txt", out var stream).ShouldBeFalse();
        stream.ShouldBeNull();
        ResourceAccess.Empty.ReadAllBytes("templates", "greeting.txt").ShouldBeNull();
    }
}
