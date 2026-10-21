namespace HYWDebugAssistant.Tests;

/// <summary>
/// 测试用的**模块包目录**（只提供一个存在的目录）。
/// </summary>
/// <remarks>
/// 界面改由模块自带视图（代码）之后，框架不再从包目录读任何界面事实，
/// 因此这里只承担"给宿主一个能打开的目录"这一件事。保留它是因为大量用例
/// 只需"一个包目录"，不该为此各自建临时目录。
/// </remarks>
internal static class TestViewPackages
{
    /// <summary>取某个模块类型对应的包目录（不存在则创建）。</summary>
    /// <param name="moduleType">模块入口类型（用它的名字分目录）。</param>
    public static string For(Type moduleType)
    {
        ArgumentNullException.ThrowIfNull(moduleType);

        var directory = Path.Combine(Path.GetTempPath(), "hyw-test-packages", moduleType.Name);
        Directory.CreateDirectory(directory);
        return directory;
    }
}
