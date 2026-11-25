using HYWDebugAssistant.Core.Api;
using HYWDebugAssistant.Core.Runtime;
using HYWDebugAssistant.Core.Services;
using HYWDebugAssistant.Hosting.Registry;
using HYWDebugAssistant.Sdk;
using HYWDebugAssistant.Sdk.Context;
using HYWDebugAssistant.Sdk.Lifecycle;
using HYWDebugAssistant.Ui.Geometry;
using HYWDebugAssistant.Ui.ViewModels;
using Shouldly;
using Xunit;

namespace HYWDebugAssistant.Tests;

/// <summary>
/// 层 / 拼接判定的**集成测试夹具**：真模块服务 + 真 <see cref="ModuleWindowsViewModel"/>，只有窗口技术是假的。
/// </summary>
/// <remarks>
/// 几何全部是**物理像素**（与分组/层判定的口径一致）：布局给什么，判定就用什么，不做缩放投影。
/// </remarks>
internal sealed class TilingHarness : IDisposable
{
    /// <summary>建一批模块并摆到指定位子上。</summary>
    /// <param name="layouts">(实例 id, X, Y, 宽, 高)。</param>
    /// <param name="shell">外壳物理矩形（null = 没有外壳）。</param>
    /// <param name="zOrder">叠放序（null = 全部同序，退化为 id 序）。</param>
    public TilingHarness(
        IReadOnlyList<(string Id, double X, double Y, double Width, double Height)> layouts,
        CanvasRect? shell = null,
        Func<string, int>? zOrder = null)
    {
        Runtime = new ModuleRuntime(issues: null, carrierOptions: null);
        Services = new RuntimeServices(Runtime);

        var definition = new ModuleDefinition(
            ModuleId.Create("test", "layer"),
            nameof(LayerModule),
            string.Empty,
            [],
            ModuleCapability.None,
            [], true,
            typeof(LayerModule),
            string.Empty,
            string.Empty);

        Services.Registry.TryRegister(definition, out var failure).ShouldBeTrue(failure);

        foreach (var (id, x, y, width, height) in layouts)
        {
            Services.AddModule(id, "test/layer").Succeeded.ShouldBeTrue();
            Services.UpdateModuleLayout(new ModuleLayoutView(id, x, y, width, height)).Succeeded.ShouldBeTrue();
        }

        Windows = new ModuleWindowsViewModel(Services, Services);
        Windows.Refresh();

        if (shell is { } shellRect)
        {
            Windows.SetShellFrame(shellRect, shellRect);
            Windows.SetShellWindowPosition(new Avalonia.PixelPoint((int)shellRect.X, (int)shellRect.Y));
        }

        Windows.ZOrderProvider = zOrder ?? (_ => 0);
        Windows.RefreshLayers();
    }

    public ModuleRuntime Runtime { get; }

    public RuntimeServices Services { get; }

    public ModuleWindowsViewModel Windows { get; }

    public ModuleViewViewModel Module(string id) => Windows.Modules.Single(module => module.InstanceId == id);

    public void Dispose() => Runtime.Dispose();

    /// <summary>只为满足模块定义的宿主类型（没有任何行为）。</summary>
    [ModuleDefinition("test", "layer")]
    public sealed class LayerModule : IModule
    {
        public void OnRegistered(IModuleContext context)
        {
        }
    }
}
