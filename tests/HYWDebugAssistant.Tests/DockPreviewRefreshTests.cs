using HYWDebugAssistant.Core.Api;
using HYWDebugAssistant.Core.Runtime;
using HYWDebugAssistant.Core.Services;
using HYWDebugAssistant.Hosting.Registry;
using HYWDebugAssistant.Sdk;
using HYWDebugAssistant.Sdk.Context;
using HYWDebugAssistant.Sdk.Contracts;
using HYWDebugAssistant.Sdk.Lifecycle;
using HYWDebugAssistant.Sdk.Ports;
using HYWDebugAssistant.Ui.ViewModels;
using Shouldly;
using Xunit;

namespace HYWDebugAssistant.Tests;

/// <summary>
/// **建网预览的刷新时序**：两轴都被吸附锁住时，端口高亮仍必须出现。
/// </summary>
/// <remarks>
/// 复现路径：先有一次非接触移动占用 50ms 节流窗口，再进入贴合；此后指针继续沿吸附线移动，
/// 整组整数位移不变。若拖动死区把 `UpdateDockPreview` 一起跳过，接触那一帧被节流吞掉后
/// 就再也不会重算 —— 正是“x 轴相等上下拼接时端口不加粗、斜着拼正常”的成因。
/// </remarks>
public class DockPreviewRefreshTests
{
    [Fact]
    public void 吸附锁住两轴后端口高亮仍应出现()
    {
        using var harness = new PreviewHarness();
        var windows = harness.Windows;

        var sender = windows.Modules.Single(module => module.InstanceId == "s");
        var receiver = windows.Modules.Single(module => module.InstanceId == "r");

        sender.Ports.ShouldHaveSingleItem();
        receiver.Ports.ShouldHaveSingleItem();

        windows.BeginDrag("s", pointerX: 100, pointerY: 100, independent: true).ShouldBeTrue();

        // ① 接近但未贴合（gap 10 > 贴合容差 8）：占用一次预览节流窗口。
        windows.DragTo(pointerX: 100, pointerY: 140);
        sender.Ports.Single().IsDockCandidate.ShouldBeFalse();

        // ② 进入贴合：该帧恰好落在节流窗口内，预览被推迟。
        windows.DragTo(pointerX: 100, pointerY: 150);

        // ③ 指针沿吸附线继续移动，整组位移不变：旧实现会在这里提前 return。
        windows.DragTo(pointerX: 100, pointerY: 151);

        // ④ 等过节流窗口再给一帧：补算/预览必须把两端点亮。
        Thread.Sleep(70);
        windows.DragTo(pointerX: 100, pointerY: 152);

        sender.Ports.Single().IsDockCandidate.ShouldBeTrue("x 轴相等上下拼接时，发送口应在贴合后高亮");
        receiver.Ports.Single().IsDockCandidate.ShouldBeTrue("接收口同样应高亮");

        windows.EndDrag();
    }

    private sealed class PreviewHarness : IDisposable
    {
        public PreviewHarness()
        {
            Runtime = new ModuleRuntime(issues: null, carrierOptions: null);
            Services = new RuntimeServices(Runtime);

            Register("preview-sender", typeof(PreviewSenderModule));
            Register("preview-receiver", typeof(PreviewReceiverModule));

            Services.AddModule("s", "test/preview-sender").Succeeded.ShouldBeTrue();
            Services.AddModule("r", "test/preview-receiver").Succeeded.ShouldBeTrue();

            // x 相等、上下留 50px 间隙（大于拼接容差 8 ⇒ 开始时不是一组）。
            Services.UpdateModuleLayout(new ModuleLayoutView("s", 0, 0, 300, 200)).Succeeded.ShouldBeTrue();
            Services.UpdateModuleLayout(new ModuleLayoutView("r", 0, 250, 300, 200)).Succeeded.ShouldBeTrue();

            Windows = new ModuleWindowsViewModel(Services, Services);
            Windows.Refresh();
        }

        public ModuleRuntime Runtime { get; }

        public RuntimeServices Services { get; }

        public ModuleWindowsViewModel Windows { get; }

        public void Dispose() => Runtime.Dispose();

        private void Register(string name, Type entryType)
        {
            var definition = new ModuleDefinition(
                ModuleId.Create("test", name),
                entryType.Name,
                string.Empty,
                [],
                ModuleCapability.None,
                [], true,
                entryType,
                string.Empty,
                string.Empty);

            Services.Registry.TryRegister(definition, out var failure).ShouldBeTrue(failure);
        }
    }

    [ModuleDefinition("test", "preview-sender")]
    public sealed class PreviewSenderModule : IModule
    {
        public const string OutPort = "out";

        public static readonly ContractShape Shape = ContractShape.OfPrimitive(ContractPrimitive.F32);

        public void OnRegistered(IModuleContext context)
        {
            context.Declaration.DeclareDataType(new DataTypeDeclaration("PreviewFrame", Shape));
            context.Declaration.DeclarePort(new PortDeclaration(OutPort, PortDirection.Send, Shape)
            {
                TypeId = "PreviewFrame",
                Edge = PortEdge.Bottom,
            });
        }
    }

    [ModuleDefinition("test", "preview-receiver")]
    public sealed class PreviewReceiverModule : IModule
    {
        public const string InPort = "in";

        public static readonly ContractShape Shape = ContractShape.OfPrimitive(ContractPrimitive.F32);

        public void OnRegistered(IModuleContext context)
        {
            context.Declaration.DeclareDataType(new DataTypeDeclaration("PreviewFrame", Shape));
            context.Declaration.DeclarePort(new PortDeclaration(InPort, PortDirection.Receive, Shape)
            {
                TypeId = "PreviewFrame",
                Edge = PortEdge.Top,
            });
        }
    }
}
