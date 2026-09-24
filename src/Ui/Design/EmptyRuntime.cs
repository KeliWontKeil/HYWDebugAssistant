using HYWDebugAssistant.Core.Api;
using HYWDebugAssistant.Sdk.Views;

namespace HYWDebugAssistant.Ui.Design;

/// <summary>
/// 空运行时（设计器预览与 UI 单元测试用）：所有查询返回空、所有命令返回成功且**无副作用**。
/// </summary>
/// <remarks>
/// 它不是产品实现——只用于让 XAML 预览器与视图层测试能构造视图模型（UI 只依赖 <c>Core.Api</c>，
/// 因此可以在此提供一个最小替身，而不必拉起整个框架）。
/// </remarks>
public sealed class EmptyRuntime : IRuntimeQuery, IRuntimeCommand
{
    /// <summary>单例（无状态）。</summary>
    public static readonly EmptyRuntime Instance = new();

    // ---------------------------------------------------------------- 查询面

    /// <inheritdoc />
    public IReadOnlyList<ModuleInstanceView> GetInstances() => [];

    /// <inheritdoc />
    public bool TryGetInstance(string instanceId, out ModuleInstanceView? view)
    {
        view = null;
        return false;
    }

    /// <inheritdoc />
    public IReadOnlyList<NetworkView> GetNetworks() => [];

    /// <inheritdoc />
    public IReadOnlyList<BindingDiagnosticView> GetDiagnostics() => [];

    /// <inheritdoc />
    public IReadOnlyList<IssueView> GetIssues(int maxCount) => [];

    /// <inheritdoc />
    public IReadOnlyList<ModuleCatalogEntryView> GetModuleCatalog() => [];

    /// <inheritdoc />
    public IReadOnlyList<ModuleSourceView> GetModuleSources() => [];

    /// <inheritdoc />
    public IReadOnlyList<ConnectionCandidateView> PreviewConnections(IReadOnlyList<ModuleLayoutView>? provisionalLayouts = null) => [];

    /// <inheritdoc />
    public ApplicationSettings GetSettings() => new();

    /// <inheritdoc />
    public IReadOnlyList<ModuleLayoutView> GetLayout() => [];

    /// <inheritdoc />
    public IReadOnlyList<SubscriptionSourceView> GetSubscriptionSources() => [];

    /// <inheritdoc />
    public bool TryReadLatest(string instanceId, string dataSourceName, Span<byte> destination, out int length)
    {
        length = 0;
        return false;
    }

    /// <inheritdoc />
    public int ReadSeries(string instanceId, string dataSourceName, Span<byte> destination, int maxSamples) => 0;

    /// <inheritdoc />
    public bool TryGetConfigurationJson(string instanceId, out string? json)
    {
        json = null;
        return false;
    }

    /// <inheritdoc />
    public bool TryGetConfigurationSchema(string instanceId, out ConfigurationSchemaView? schema)
    {
        schema = null;
        return false;
    }

    /// <inheritdoc />
    public CommandResult UpdateConfigurationField(string instanceId, string fieldName, string value) => CommandResult.Ok();

    // ---------------------------------------------------------------- 命令面

    /// <inheritdoc />
    public CommandResult InstallModulePackage(string packageDirectory) => CommandResult.Ok();

    /// <inheritdoc />
    public CommandResult RescanModuleSources() => CommandResult.Ok();

    /// <inheritdoc />
    public CommandResult AddModule(string instanceId, string moduleIdLiteral) => CommandResult.Ok();

    /// <inheritdoc />
    public CommandResult RemoveModule(string instanceId) => CommandResult.Ok();

    /// <inheritdoc />
    public CommandResult SetModuleEnabled(string instanceId, bool enabled) => CommandResult.Ok();

    /// <inheritdoc />
    public CommandResult Connect(PortRef source, IReadOnlyList<PortRef> targets, string? name = null) => CommandResult.Ok();

    /// <inheritdoc />
    public CommandResult Disconnect(string networkName) => CommandResult.Ok();

    /// <inheritdoc />
    public CommandResult UpdateConfiguration(string instanceId, object configuration) => CommandResult.Ok();

    /// <inheritdoc />
    public CommandResult UpdateSettings(ApplicationSettings settings) => CommandResult.Ok();

    /// <inheritdoc />
    public CommandResult UpdateModuleLayout(ModuleLayoutView layout) => CommandResult.Ok();

    /// <inheritdoc />
    public CommandResult UpdateModuleLayouts(IReadOnlyList<ModuleLayoutView> layouts) => CommandResult.Ok();

    /// <inheritdoc />
    public CommandResult UpdatePortGeometry(string instanceId, PortGeometryView geometry) => CommandResult.Ok();

    /// <inheritdoc />
    public CommandResult UpdatePortNetwork(string instanceId, string portName, string? networkName) => CommandResult.Ok();

    /// <inheritdoc />
    public CommandResult SendCommand(string instanceId, string commandName) => CommandResult.Ok();

    /// <inheritdoc />
    public CommandResult SubmitInput(string instanceId, string source, string text) => CommandResult.Ok();

    /// <inheritdoc />
    public CommandResult SaveComposite(string filePath, string? name = null, IReadOnlyList<string>? instanceIds = null) =>
        CommandResult.Ok();

    /// <inheritdoc />
    public CommandResult LoadComposite(string filePath) => CommandResult.Ok();

    /// <inheritdoc />
    public CommandResult ResolvePlaceholder(string instanceId, string moduleIdLiteral) => CommandResult.Ok();

    /// <inheritdoc />
    public CommandResult UpdateConfigurationJson(string instanceId, string json) => CommandResult.Ok();

    /// <summary>预览用的示例视图声明（设计器里能看到最小模块视图长什么样）。</summary>
    public static ViewDeclaration PreviewView => new(
        [
            new ViewElement(ViewElementKind.Numeric, "value", "当前值"),
            new ViewElement(ViewElementKind.Curve, "wave", "波形"),
        ],
        [
            new ViewDataSource("value", "V", "F2", ViewDataSourceKind.Value, Sdk.Contracts.ContractPrimitive.F32),
            new ViewDataSource("wave", null, null, ViewDataSourceKind.Sequence, Sdk.Contracts.ContractPrimitive.F32),
        ]);
}
