using HYWDebugAssistant.Modules.Serial;
using HYWDebugAssistant.Modules.TcpClient;
using HYWDebugAssistant.Modules.TcpServer;
using HYWDebugAssistant.Modules.Udp;
using HYWDebugAssistant.Sdk.Resources;
using Shouldly;
using Xunit;

namespace HYWDebugAssistant.Modules.Tests;

/// <summary>
/// 模块的**自有资源包**：清单与 SVG 必须真的落在模块输出旁的 `resources/` 下，且模块只查自己的包。
/// </summary>
/// <remarks>
/// 模块包不带宿主程序集，资源是模块自有数据；漏了 csproj 的 Content 项、或者把资源放到框架
/// `resources/` 下，界面上只会少一个图标，编译期完全合法 —— 所以这里按模块查一次清单 + 一次文件，
/// 再证明"框架根里的同名拥有者不会被模块读到"。
/// </remarks>
public sealed class ModuleIconTableTests
{
    /// <summary>外部接入四模块。</summary>
    public static TheoryData<Type> Modules => new()
    {
        typeof(SerialModule),
        typeof(TcpClientModule),
        typeof(TcpServerModule),
        typeof(UdpModule),
    };

    [Theory]
    [MemberData(nameof(Modules))]
    public void 模块图标表_应随输出命中清空计数图标(Type moduleType)
    {
        var assembly = moduleType.Assembly;

        IconCatalog.TryGet(assembly, "icon.refresh", out var asset).ShouldBeTrue(
            $"{moduleType.Name} 的图标清单没有随输出（包内 resources/icons/<程序集简名>/icons.json）");

        asset.ShouldNotBeNull();
        asset!.PathData.ShouldNotBeEmpty($"{moduleType.Name} 的 icon.refresh 必须真的解析出路径");
        asset.ViewBoxWidth.ShouldBeGreaterThan(0, $"{moduleType.Name} 的 icon.refresh 画布宽度必须是正的");
        asset.ViewBoxHeight.ShouldBeGreaterThan(0, $"{moduleType.Name} 的 icon.refresh 画布高度必须是正的");

        // 图形文件必须和模块 DLL 一起躺在**模块自己的** resources/ 里（清单只是索引，缺文件同样会让图标消失）。
        var ownerDirectory = Path.Combine(
            Path.GetDirectoryName(assembly.Location)!,
            ResourceLayout.ResourcesDirectoryName,
            ResourceKinds.Icons,
            assembly.GetName().Name!);

        Directory.Exists(ownerDirectory).ShouldBeTrue(
            $"{moduleType.Name} 的输出里没有 resources/icons/<程序集简名>/ 目录");
        Directory.GetFiles(ownerDirectory, "*.svg", SearchOption.AllDirectories)
            .ShouldNotBeEmpty($"{moduleType.Name} 的输出里没有随包的 SVG");
    }

    /// <summary>
    /// **两套根不串**：把同名拥有者放到"框架根"（显式根）里，模块仍然只读自己包里的那份。
    /// </summary>
    /// <remarks>
    /// 用"画布尺寸"区分来源：框架根里的探针 SVG 自己声明一个尺寸，模块包里的是另一份。
    /// 若模块读到了框架根，这里会看到那个不属于模块包的尺寸。
    /// </remarks>
    [Fact]
    public void 模块拥有者_不读框架根里的同名目录()
    {
        var assembly = typeof(SerialModule).Assembly;
        var ownerName = assembly.GetName().Name!;

        var fakeRoot = Path.Combine(Path.GetTempPath(), "hyw-framework-root-" + Guid.NewGuid().ToString("N"));
        var fakeOwner = Path.Combine(fakeRoot, ownerName);

        try
        {
            const double frameworkCanvas = 99;

            _ = Directory.CreateDirectory(fakeOwner);
            File.WriteAllText(Path.Combine(fakeOwner, "icons.json"), $$"""
                {
                  "schemaVersion": 1,
                  "entries": { "icon.refresh": "probe.svg" }
                }
                """);
            File.WriteAllText(
                Path.Combine(fakeOwner, "probe.svg"),
                $"""<svg viewBox="0 0 {frameworkCanvas} {frameworkCanvas}" xmlns="http://www.w3.org/2000/svg"><path d="M0 0H50V50H0Z" /></svg>""");

            IconCatalog.UseRoot(fakeRoot);

            try
            {
                IconCatalog.TryGet(assembly, "icon.refresh", out var asset).ShouldBeTrue();
                asset!.ViewBoxWidth.ShouldBeGreaterThan(0);
                asset.ViewBoxWidth.ShouldNotBe(frameworkCanvas, "模块拥有者只查模块包，框架根里的同名目录不算数");
            }
            finally
            {
                IconCatalog.UseRoot(null);
            }
        }
        finally
        {
            try
            {
                Directory.Delete(fakeRoot, recursive: true);
            }
            catch (IOException)
            {
            }
            catch (UnauthorizedAccessException)
            {
            }
        }
    }
}
