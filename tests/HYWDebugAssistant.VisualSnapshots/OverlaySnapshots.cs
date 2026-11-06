using System;
using System.IO;
using HYWDebugAssistant.Core.Api;
using HYWDebugAssistant.Core.Runtime;
using HYWDebugAssistant.Core.Services;
using HYWDebugAssistant.Hosting.Registry;
using HYWDebugAssistant.Sdk;
using HYWDebugAssistant.Sdk.Context;
using HYWDebugAssistant.Sdk.Contracts;
using HYWDebugAssistant.Sdk.Lifecycle;
using HYWDebugAssistant.Sdk.Ports;
using HYWDebugAssistant.Sdk.Views;
using HYWDebugAssistant.Ui.ViewModels;
using HYWDebugAssistant.Ui.Views;
using Xunit;

namespace HYWDebugAssistant.VisualSnapshots;

/// <summary>
/// **叠加层截图**：三窗布局（发送器在上、两个接收器在下、一个未连接）→ 每个窗口一张 PNG。
/// </summary>
/// <remarks>
/// 只负责“把它画出来”，不判断画得对不对；对错由人眼看 <c>artifacts\snapshots\overlay\</c> 下的图。
/// </remarks>
[Collection("visual-snapshots")]
public class OverlaySnapshots
{
    /// <summary>生成叠加层截图（<c>overlay\*.png</c>）。</summary>
    [Fact]
    public async Task 生成快照_叠加层()
    {
        var session = SnapshotSession.Shared;

        await session.Dispatch(
            () =>
            {
                // 三窗布局覆盖：多对端分叉、未连接残段、信息区三档字号
                using var harness = new SnapshotHarness();

                harness.AddSender("sender", x: 260, y: 40);
                harness.AddSink("m1", x: 40, y: 380);
                harness.AddSink("m2", x: 480, y: 380);
                harness.AddSender("lonely", x: 700, y: 40);   // 未连接 ⇒ 方块照画，`m/n` 显示 0/--

                UpdateNetwork(harness, "sender", "out", "bus");
                UpdateNetwork(harness, "m1", "in", "bus");
                UpdateNetwork(harness, "m2", "in", "bus");

                harness.Windows.ShowInfoOverlay = true;
                harness.Windows.Refresh();

                foreach (var module in harness.Windows.Modules)
                {
                    var window = harness.OpenWindow(module);
                    SnapshotTool.Pump();

                    SnapshotTool.Save(window, Path.Combine("overlay", module.InstanceId + ".png"));
                    SnapshotTool.DumpTree(window, Path.Combine("overlay", module.InstanceId + "-tree.txt"));
                    window.Close();
                }
            },
            CancellationToken.None);
    }

    /// <summary>把某实例的某口挂到命名网络上（失败即抛——截图现场必须是真的）。</summary>
    private static void UpdateNetwork(SnapshotHarness harness, string instanceId, string port, string network)
    {
        var update = harness.Services.UpdatePortNetwork(instanceId, port, network);

        if (!update.Succeeded)
        {
            throw new InvalidOperationException($"挂网络失败：{instanceId}/{port} → {network}");
        }
    }

    /// <summary>快照现场（复用生产 VM 与真实模块窗口）。</summary>
    internal sealed class SnapshotHarness : IDisposable
    {
        public SnapshotHarness()
        {
            ViewState = new ViewStateStore();
            Runtime = new ModuleRuntime(viewState: ViewState);
            Services = new RuntimeServices(Runtime);
            Windows = new ModuleWindowsViewModel(Services, Services, ViewState);

            // 视图由模块自己的 XAML 提供；截图夹具只需要端口与角色（叠加层几何）。
            Register("snapshot-sender", typeof(SnapshotSenderModule));
            Register("snapshot-sink", typeof(SnapshotSinkModule));
        }

        public ModuleRuntime Runtime { get; }

        public RuntimeServices Services { get; }

        public ViewStateStore ViewState { get; }

        public ModuleWindowsViewModel Windows { get; }

        public void AddSender(string instanceId, double x, double y) => Add(instanceId, "test/snapshot-sender", x, y);

        public void AddSink(string instanceId, double x, double y) => Add(instanceId, "test/snapshot-sink", x, y);

        public ModuleWindow OpenWindow(ModuleViewViewModel module)
        {
            var window = new ModuleWindow { DataContext = module, Controller = Windows };
            window.Show();
            return window;
        }

        public void Dispose() => Runtime.Dispose();

        private const double InitialWindowWidth = 320;

        private const double InitialWindowHeight = 220;

        private void Add(string instanceId, string moduleIdLiteral, double x, double y)
        {
            if (!Services.AddModule(instanceId, moduleIdLiteral).Succeeded)
            {
                throw new InvalidOperationException($"加入模块失败：{instanceId} / {moduleIdLiteral}");
            }

            Windows.Refresh();

            if (!Services.UpdateModuleLayout(new ModuleLayoutView(
                    instanceId, x, y, InitialWindowWidth, InitialWindowHeight)).Succeeded)
            {
                throw new InvalidOperationException($"设置布局失败：{instanceId}");
            }

            Windows.Refresh();
        }

        internal void Register(string name, Type entryType)
        {
            var definition = new ModuleDefinition(
                ModuleId.Create("test", name),
                entryType.Name,
                string.Empty,
                [],
                ModuleCapability.None,
                [], true,
                entryType,
                packageDirectory: string.Empty,
                assemblyPath: string.Empty,
                viewFactory: null,
                windowWidth: 0,
                windowHeight: 0);

            if (!Services.Registry.TryRegister(definition, out var failure))
            {
                throw new InvalidOperationException($"登记快照模块失败：{failure}");
            }
        }
    }

    /// <summary>截图用发送模块（一个发送口）：声明一个“发送”角色（纯源分块，无输入口）。</summary>
    [ModuleDefinition("test", "snapshot-sender")]
    public sealed class SnapshotSenderModule : IModule
    {
        /// <summary>截图用数据类型标识。</summary>
        public const string TypeId = "test/snapshot-sample";

        /// <summary>截图用数据类型（`{seq:u64, value:f32}` = 12B）。</summary>
        public static readonly ContractShape Shape = ContractShape.OfStruct(
            ("seq", ContractShape.OfPrimitive(ContractPrimitive.U64)),
            ("value", ContractShape.OfPrimitive(ContractPrimitive.F32)));

        /// <inheritdoc />
        public void OnRegistered(IModuleContext context)
        {
            context.Declaration.DeclareDataType(new DataTypeDeclaration(TypeId, Shape));
            context.Declaration.DeclarePort(new PortDeclaration("out", PortDirection.Send, Shape)
            {
                TypeId = TypeId,
            });

            context.Declaration.DeclareRole(RoleDeclaration.Output(
                "send", TypeId, "out", "发送"));
        }
    }

    /// <summary>截图用接收模块（一个接收口）：声明一个“显示”角色（纯汇分块，无输出口）。</summary>
    [ModuleDefinition("test", "snapshot-sink")]
    public sealed class SnapshotSinkModule : IModule
    {
        /// <inheritdoc />
        public void OnRegistered(IModuleContext context)
        {
            context.Declaration.DeclareDataType(
                new DataTypeDeclaration(SnapshotSenderModule.TypeId, SnapshotSenderModule.Shape));
            context.Declaration.DeclarePort(new PortDeclaration(
                "in", PortDirection.Receive, SnapshotSenderModule.Shape)
            {
                TypeId = SnapshotSenderModule.TypeId,
            });

            context.Declaration.DeclareRole(RoleDeclaration.Input(
                "show", SnapshotSenderModule.TypeId, "in", "显示"));

            context.Ports.SetReceiveHandler("in", _ => { });
        }
    }
}
