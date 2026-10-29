using HYWDebugAssistant.Core.Settings;
using HYWDebugAssistant.Infrastructure.Storage;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using Xunit;

namespace HYWDebugAssistant.Tests;

/// <summary>
/// **按功能分根的路径口径**：设置 / 模块 / 组合 / 工作区 / 日志 各自成根，
/// 出厂种子单独住在 `resources\seeds\`（只读程序资源）。
/// </summary>
/// <remarks>
/// <para>
/// 界线：设置（含外观主题）只住 `settings\`；模块包只住 `modules\`；组合只住 `composites\`；
/// 工作区（含一次性重启恢复快照）只住 `workspace\`；日志只住 `logs\`。
/// </para>
/// <para>
/// 为什么值得一条用例：路径一旦散开或互相嵌套，"备份 = 拷哪几个目录"就会悄悄失效；这条用例把五个根钉住。
/// </para>
/// </remarks>
public class StoragePathsTests
{
    [Fact]
    public void 五个功能根应同级且互不嵌套()
    {
        var appRoot = AppContext.BaseDirectory;

        StoragePaths.AppRoot.ShouldBe(appRoot);

        var roots = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["settings"] = StoragePaths.SettingsRoot,
            ["modules"] = StoragePaths.ModulesRoot,
            ["composites"] = StoragePaths.CompositesRoot,
            ["workspace"] = StoragePaths.WorkspaceRoot,
            ["logs"] = StoragePaths.LogsRoot,
        };

        foreach (var (name, root) in roots)
        {
            root.ShouldBe(Path.Combine(appRoot, name), $"功能根 {name} 必须直接位于程序目录下");
        }

        foreach (var (name, root) in roots)
        {
            foreach (var (otherName, other) in roots)
            {
                if (string.Equals(name, otherName, StringComparison.Ordinal))
                {
                    continue;
                }

                Path.GetRelativePath(root, other)
                    .StartsWith("..", StringComparison.Ordinal)
                    .ShouldBeTrue($"功能根不能互相嵌套：{name} 与 {otherName}");
            }
        }

        // 出厂种子是程序层只读资源：不属于任何用户功能根
        StoragePaths.ResourcesRoot.ShouldBe(Path.Combine(appRoot, "resources"));
        StoragePaths.SeedsRoot.ShouldBe(Path.Combine(appRoot, "resources", "seeds"));

        foreach (var (name, root) in roots)
        {
            Path.GetRelativePath(root, StoragePaths.SeedsRoot)
                .StartsWith("..", StringComparison.Ordinal)
                .ShouldBeTrue($"出厂种子必须在用户功能根之外：{name}");
        }

        StoragePaths.ExternalModulesRoot.ShouldBe(
            Path.Combine(StoragePaths.ModulesRoot, "external"),
            "外部导入包是模块根的子目录，不是独立功能根");
    }

    [Fact]
    public void 各功能根下的文件与子根应落在自己的根内()
    {
        StoragePaths.SettingsFile.ShouldBe(Path.Combine(StoragePaths.SettingsRoot, "settings.json"));
        StoragePaths.ThemesRoot.ShouldBe(Path.Combine(StoragePaths.SettingsRoot, "appearance", "themes"));
        StoragePaths.WorkspaceFile.ShouldBe(Path.Combine(StoragePaths.WorkspaceRoot, "workspace.json"));
        StoragePaths.WorkspaceAssetsRoot.ShouldBe(Path.Combine(StoragePaths.WorkspaceRoot, "assets"));
        StoragePaths.RestartFile.ShouldBe(
            Path.Combine(StoragePaths.WorkspaceRoot, "restart.json"),
            "重启恢复快照是工作区的一次性快照，不另立根");

        var inside = new (string Root, string Path)[]
        {
            (StoragePaths.SettingsRoot, StoragePaths.SettingsFile),
            (StoragePaths.SettingsRoot, StoragePaths.ThemesRoot),
            (StoragePaths.WorkspaceRoot, StoragePaths.WorkspaceFile),
            (StoragePaths.WorkspaceRoot, StoragePaths.WorkspaceAssetsRoot),
            (StoragePaths.WorkspaceRoot, StoragePaths.RestartFile),
            (StoragePaths.ResourcesRoot, StoragePaths.SeedsRoot),
        };

        foreach (var (root, path) in inside)
        {
            Path.GetRelativePath(root, path)
                .StartsWith("..", StringComparison.Ordinal)
                .ShouldBeFalse($"{path} 必须在 {root} 之下");
        }
    }

    [Fact]
    public void 组合根装配的设置路径_必须取自StoragePaths()
    {
        // 上两条钉的是"值"，这一条钉的是"**装配**"：生产组合根真的把设置服务指到设置根。
        var (_, provider) = HYWDebugAssistant.App.CompositionRoot.Build();
        try
        {
            var settings = provider.GetRequiredService<SettingsService>();

            settings.FilePath.ShouldBe(StoragePaths.SettingsFile);
            Path.GetRelativePath(StoragePaths.SettingsRoot, settings.FilePath)
                .ShouldBe("settings.json", "设置必须落在 `settings\\` 下");
        }
        finally
        {
            provider.Dispose();
        }
    }
}
