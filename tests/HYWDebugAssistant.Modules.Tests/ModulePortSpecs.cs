using System.Reflection;
using HYWDebugAssistant.Sdk.Ports;

namespace HYWDebugAssistant.Modules.Tests;

/// <summary>
/// **内置模块的端口声明源**：从仓库里的 `Views/*.axaml` 读取（与包内 `views/` 同源）。
/// </summary>
/// <remarks>
/// 模块测试项目不引用 Avalonia，因此不能实例化视图；但 `PortSpecReader` 只依赖 BCL，
/// 直接读源文件即可。读不到/有错误 ⇒ 抛出，让契约自检明确失败。
/// </remarks>
internal static class ModulePortSpecs
{
    public static IReadOnlyList<PortDeclarationSpec> Read(string moduleFolder, Assembly entryAssembly)
    {
        var root = FindRepoRoot();
        var directory = Path.Combine(root, "src", "Modules", "Business", moduleFolder, "Views");
        var result = PortSpecReader.ReadDirectory(directory, entryAssembly);

        if (result.Errors.Count > 0)
        {
            var first = result.Errors[0];
            throw new InvalidOperationException(
                $"端口声明读取失败：{first.Code}（{first.FilePath}）{first.Detail}");
        }

        return result.Specs;
    }

    private static string FindRepoRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);

        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "HYWDebugAssistant.slnx")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new InvalidOperationException("找不到仓库根（HYWDebugAssistant.slnx）");
    }
}
