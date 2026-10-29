using HYWDebugAssistant.Access;
using HYWDebugAssistant.Access.Parameters;
using HYWDebugAssistant.Hosting.Diagnostics;
using HYWDebugAssistant.Hosting.Instances;
using HYWDebugAssistant.Hosting.Packages;
using HYWDebugAssistant.Hosting.Registry;
using HYWDebugAssistant.Sdk.Access;

namespace HYWDebugAssistant.StandaloneModuleHost;

/// <summary>
/// dev-only 验证宿主：加载一个模块包目录，按内置 catalog 创建实例，断言外部接入声明与参数类型。
/// </summary>
/// <remarks>
/// 退出码：0 成功；1 加载/断言失败；2 参数用法错误。
/// 这是"独立编译"的闭环证据 —— 示例模块只靠 SDK NuGet 包构建，宿主按真实加载路径校验。
/// </remarks>
internal static class Program
{
    private static int Main(string[] args)
    {
        if (args.Length != 1)
        {
            Console.Error.WriteLine("用法：HYWDebugAssistant.StandaloneModuleHost <模块包目录>");
            return 2;
        }

        var packageDirectory = Path.GetFullPath(args[0]);
        Console.WriteLine($"[host] 加载模块包：{packageDirectory}");

        var issues = new CollectingIssueSink();
        var load = ModulePackageLoader.Load(packageDirectory);
        if (!load.Succeeded || load.Package is null)
        {
            Console.Error.WriteLine($"[host] 加载失败：{load.FailureReason}");
            return 1;
        }

        var definition = ModuleDefinition.FromPackage(load.Package);
        var catalog = new BuiltInExternalAccessCatalog();
        var instance = ModuleInstanceFactory.Create(
            "standalone",
            definition,
            issues,
            catalog: catalog);

        foreach (var issue in issues.Issues)
        {
            Console.Error.WriteLine($"[host] 声明期问题（{issue.Issue.Severity}）：{issue.Issue.Message}");
        }

        if (instance.ExternalAccesses.Count != 1)
        {
            Console.Error.WriteLine($"[host] 断言失败：外部接入数量应为 1，实际 {instance.ExternalAccesses.Count}");
            return 1;
        }

        var declaration = instance.ExternalAccesses[0];
        if (string.IsNullOrWhiteSpace(declaration.Name)
            || string.IsNullOrWhiteSpace(declaration.StandardId))
        {
            Console.Error.WriteLine("[host] 断言失败：接入名或标准标识为空");
            return 1;
        }

        if (!catalog.TryResolve(declaration.StandardId, out var descriptor))
        {
            Console.Error.WriteLine($"[host] 断言失败：标准标识无法解析：{declaration.StandardId}");
            return 1;
        }

        if (descriptor.ParametersType != typeof(SerialConfig))
        {
            Console.Error.WriteLine(
                $"[host] 断言失败：serial 标准的参数类型应为 {typeof(SerialConfig).FullName}，实际 {descriptor.ParametersType.FullName}");
            return 1;
        }

        var typeName = descriptor.ParametersType.FullName;
        if (typeName is null || !instance.Context.ConfigurationSnapshot.ContainsKey(typeName))
        {
            Console.Error.WriteLine("[host] 断言失败：参数类型未登记进实例配置快照");
            return 1;
        }

        Console.WriteLine(
            $"[host] OK：模块 {definition.Id}，接入 '{declaration.Name}'，"
            + $"标准 '{declaration.StandardId}'，参数类型 {descriptor.ParametersType.Name}");
        return 0;
    }
}
