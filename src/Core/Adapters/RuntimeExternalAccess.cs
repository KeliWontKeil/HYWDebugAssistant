using HYWDebugAssistant.DataPlane.Access;
using HYWDebugAssistant.Hosting.Diagnostics;
using HYWDebugAssistant.Sdk.Access;
using HYWDebugAssistant.Sdk.Context;
using HYWDebugAssistant.Sdk.Reporting;

namespace HYWDebugAssistant.Core.Adapters;

/// <summary>
/// L4 对模块暴露的外部接入面：把数据面的会话与失败记录接到 <see cref="IExternalAccess"/>。
/// </summary>
/// <remarks>
/// <list type="bullet">
///   <item>数据面只返回 <see cref="AccessFailure"/>，**不报 Issue**；本类把失败转成 Issue 并归属到模块；</item>
///   <item>无活动体模块：承载者的投递循环调 <see cref="Pump"/>（控制 + 取数 + 回调投递）；</item>
///   <item>有活动体模块：模块在自己的循环里调 <see cref="TryRead"/> / <see cref="Write"/>，本类先推进会话再取。</item>
/// </list>
/// </remarks>
public sealed class RuntimeExternalAccess : IExternalAccess
{
    private readonly ExternalAccessHost _host;
    private readonly string _instanceId;
    private readonly IIssueSink _issues;
    private readonly string _objectPath;

    /// <summary>创建存取面。</summary>
    /// <param name="host">数据面接入宿主。</param>
    /// <param name="instanceId">实例 ID。</param>
    /// <param name="issues">Issue 上报口。</param>
    public RuntimeExternalAccess(ExternalAccessHost host, string instanceId, IIssueSink issues)
    {
        ArgumentNullException.ThrowIfNull(host);
        ArgumentException.ThrowIfNullOrWhiteSpace(instanceId);
        ArgumentNullException.ThrowIfNull(issues);

        _host = host;
        _instanceId = instanceId;
        _issues = issues;
        _objectPath = $"module:{instanceId}";
    }

    /// <summary>
    /// 推进本实例的全部会话：应用开关/重启/停用，把适配件就绪的数据取进队列，并按需投递给回调。
    /// </summary>
    /// <returns>这一拍是否做了工作。</returns>
    public bool Pump()
    {
        var didWork = _host.Pump(_instanceId, out var failures);
        Report(failures);

        if (_host.DrainToHandlers(_instanceId))
        {
            didWork = true;
        }

        return didWork;
    }

    /// <summary>参数变更：标记重启（下一拍关闭并用新参数重开）。</summary>
    public void NotifyConfigurationChanged() => _host.NotifyConfigurationChanged(_instanceId);

    /// <summary>置实例暂停（Disabled）：会话关闭但保留"想要打开"的意图。</summary>
    /// <param name="suspended">是否暂停。</param>
    public void SetSuspended(bool suspended) => _host.SetSuspended(_instanceId, suspended);

    /// <inheritdoc />
    public bool RequestOpen(string accessName)
    {
        var requested = _host.RequestOpen(_instanceId, accessName);
        Report(_host.DrainFailures(_instanceId));
        return requested;
    }

    /// <inheritdoc />
    public bool RequestClose(string accessName)
    {
        var requested = _host.RequestClose(_instanceId, accessName);
        Report(_host.DrainFailures(_instanceId));
        return requested;
    }

    /// <inheritdoc />
    public bool TryRead(string accessName, Span<byte> destination, out int length)
    {
        _ = Pump();
        var read = _host.TryRead(_instanceId, accessName, destination, out length);
        Report(_host.DrainFailures(_instanceId));
        return read;
    }

    /// <inheritdoc />
    public void SetReceiveHandler(string accessName, ReceiveHandler handler) =>
        _host.SetReceiveHandler(_instanceId, accessName, handler);

    /// <inheritdoc />
    public bool Write(string accessName, ReadOnlySpan<byte> payload)
    {
        var sent = _host.Write(_instanceId, accessName, payload);
        Report(_host.DrainFailures(_instanceId));
        return sent;
    }

    /// <inheritdoc />
    public ExternalAccessSnapshot GetSnapshot(string accessName) =>
        _host.GetSnapshot(_instanceId, accessName);

    /// <inheritdoc />
    public bool ClearCounters(string accessName) => _host.ClearCounters(_instanceId, accessName);

    /// <inheritdoc />
    public Type? GetParametersType(string accessName) =>
        _host.GetParametersType(_instanceId, accessName);

    private void Report(IReadOnlyList<AccessFailure> failures)
    {
        foreach (var failure in failures)
        {
            _issues.Report(_objectPath, new IssueReport(
                ExternalAccessIssue.CategoryOf(failure),
                ExternalAccessIssue.Severity,
                CoreText.Get(
                    "core.runtime.externalAccessFailure",
                    failure.AccessName,
                    failure.Message)));
        }
    }
}
