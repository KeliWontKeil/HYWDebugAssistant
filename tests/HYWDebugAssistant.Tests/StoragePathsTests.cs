using HYWDebugAssistant.Core.Settings;
using HYWDebugAssistant.Infrastructure.Storage;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using Xunit;

namespace HYWDebugAssistant.Tests;

/// <summary>
/// **分层保存的路径口径**（P5/P6）。
/// </summary>
/// <remarks>
/// <para>
/// 界线：**所有需要持久化的数据全部进 `data`** ⇒ 既有开发暂态**不做迁移、不发提示**。
/// 本用例把"两层界线"钉住：`modules\` 是**构建产物**（程序层），其余用户数据全在 `data\` 下。
/// </para>
/// <para>
/// 为什么值得一条用例：这条界线以前**没有单一答案**（设置在 exe 目录、组合件在用户选的任意路径、模块包在两处）；
/// 路径一旦又散开，"备份 = 拷一个目录"就会悄悄失效。
/// </para>
/// </remarks>
public class StoragePathsTests
{
    [Fact]
    public void 用户数据必须全在data目录下_模块包留在程序层()
    {
        // 程序层（只读：随程序分发的构建产物）
        StoragePaths.AppRoot.ShouldBe(AppContext.BaseDirectory);
        StoragePaths.ModulesRoot.ShouldBe(Path.Combine(AppContext.BaseDirectory, "modules"));

        // 用户数据层：全部挂在 `<程序目录>\data` 下（备份/搬走 = 拷这一个目录）
        StoragePaths.DataRoot.ShouldBe(Path.Combine(AppContext.BaseDirectory, "data"));
        StoragePaths.SettingsFile.ShouldBe(Path.Combine(StoragePaths.DataRoot, "settings.json"));
        StoragePaths.CompositesRoot.ShouldBe(Path.Combine(StoragePaths.DataRoot, "composites"));
        StoragePaths.LogsRoot.ShouldBe(Path.Combine(StoragePaths.DataRoot, "logs"));

        // 关键判据：**用户数据一个都不许落在 data 之外**
        foreach (var userPath in new[] { StoragePaths.SettingsFile, StoragePaths.CompositesRoot, StoragePaths.LogsRoot })
        {
            Path.GetRelativePath(StoragePaths.DataRoot, userPath)
                .StartsWith("..", StringComparison.Ordinal)
                .ShouldBeFalse($"{userPath} 必须在 {StoragePaths.DataRoot} 之下");
        }

        // 程序层与用户数据层**不重叠**（模块包是构建产物，不进 data）
        Path.GetRelativePath(StoragePaths.DataRoot, StoragePaths.ModulesRoot)
            .StartsWith("..", StringComparison.Ordinal)
            .ShouldBeTrue("内置模块包属程序层（构建产物），不该在用户数据目录里");
    }

    [Fact]
    public void 组合根装配的设置路径_必须取自StoragePaths()
    {
        // 上一条钉的是"值"，这一条钉的是"**装配**"：生产组合根真的把设置服务指到用户数据层。
        // `CompositionRoot.Build()` 与生产启动走同一条装配代码（既有用例也这么用），因此这条断言
        // 覆盖的是"路径解析器有没有被接上"—— 只读服务上的路径事实，**不触发任何写盘**。
        var (_, provider) = HYWDebugAssistant.App.CompositionRoot.Build();
        try
        {
            var settings = provider.GetRequiredService<SettingsService>();

            settings.FilePath.ShouldBe(StoragePaths.SettingsFile);
            Path.GetRelativePath(StoragePaths.DataRoot, settings.FilePath)
                .ShouldBe("settings.json", "设置必须落在 `data\\` 根下（P6）");
        }
        finally
        {
            provider.Dispose();
        }
    }
}
