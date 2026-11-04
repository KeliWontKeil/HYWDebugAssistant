using HYWDebugAssistant.Access;
using HYWDebugAssistant.Hosting.Diagnostics;
using HYWDebugAssistant.Hosting.Instances;
using HYWDebugAssistant.Hosting.Packages;
using HYWDebugAssistant.Hosting.Registry;
using HYWDebugAssistant.Sdk.Reporting;

namespace HYWDebugAssistant.StandaloneModuleHost;

/// <summary>
/// dev-only 验证宿主：加载一个模块包目录，按真实路径创建实例，断言声明面**没有 Error**。
/// </summary>
/// <remarks>
/// <para>
/// 判据与模块形态无关（数据变换模块没有外部接入，外部接入模块没有数据口，
/// step 样例可能两者都没有）：只要求"能被装入、声明面不报错、至少声明了一项可用面
/// （端口 / 外部接入 / 显示源 / 命令）"。
/// </para>
/// <para>
/// 退出码：0 成功；1 加载/断言失败；2 参数用法错误。
/// 这是"独立编译"的闭环证据 —— 示例模块只靠 SDK NuGet 包构建，宿主按真实加载路径校验。
/// </para>
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

        if (issues.Issues.Any(issue => issue.Issue.Severity == IssueSeverity.Error))
        {
            Console.Error.WriteLine("[host] 断言失败：声明期存在 Error");
            return 1;
        }

        if (instance.Ports.Count == 0
            && instance.ExternalAccesses.Count == 0
            && instance.DisplaySources.Count == 0
            && instance.Commands.Count == 0)
        {
            Console.Error.WriteLine("[host] 断言失败：没有端口、外部接入、显示源或命令（这不是一个可用的模块）");
            return 1;
        }

        foreach (var access in instance.ExternalAccesses)
        {
            if (string.IsNullOrWhiteSpace(access.Name) || string.IsNullOrWhiteSpace(access.StandardId))
            {
                Console.Error.WriteLine("[host] 断言失败：接入名或标准标识为空");
                return 1;
            }

            if (!catalog.TryResolve(access.StandardId, out var descriptor))
            {
                Console.Error.WriteLine($"[host] 断言失败：标准标识无法解析：{access.StandardId}");
                return 1;
            }

            var typeName = descriptor.ParametersType.FullName;
            if (typeName is null || !instance.Context.ConfigurationSnapshot.ContainsKey(typeName))
            {
                Console.Error.WriteLine($"[host] 断言失败：{access.StandardId} 的参数类型未登记进实例配置快照");
                return 1;
            }
        }

        Console.WriteLine(
            $"[host] OK：模块 {definition.Id}，端口 {instance.Ports.Count}，"
            + $"接入 {instance.ExternalAccesses.Count}，类型 {instance.DataTypes.Count}");
        return 0;
    }
}
