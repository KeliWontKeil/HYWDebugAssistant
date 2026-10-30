using System.Globalization;
using System.Reflection;
using HYWDebugAssistant.Modules.Serial;
using HYWDebugAssistant.Modules.TcpClient;
using HYWDebugAssistant.Modules.TcpServer;
using HYWDebugAssistant.Modules.Udp;
using HYWDebugAssistant.Sdk.Access;
using HYWDebugAssistant.Sdk.Text;
using Shouldly;
using Xunit;

namespace HYWDebugAssistant.Modules.Tests;

/// <summary>
/// 四个外部接入模块的**失败文案表**：每个失败种类都必须由模块自己的表给出句子（未覆盖的种类走回退）。
/// </summary>
/// <remarks>
/// 逐种类驱动 <c>Describe</c>：缺键会以 <c>⟦key⟧</c> 露出，句子为空或格式化失败都会让用例变红，
/// 因此这张矩阵同时钉住"回退存在"与"每种失败都有落点"。
/// </remarks>
public sealed class ExternalAccessFailureTextTests
{
    /// <summary>四个外部接入模块（sealed，用反射取受保护的 <c>Describe</c>）。</summary>
    private static readonly Type[] ModuleTypes =
    [
        typeof(SerialModule),
        typeof(TcpClientModule),
        typeof(TcpServerModule),
        typeof(UdpModule),
    ];

    /// <summary>模块 × 失败种类的全部组合。</summary>
    public static TheoryData<Type, ExternalAccessFailureKind> Cases
    {
        get
        {
            var data = new TheoryData<Type, ExternalAccessFailureKind>();

            foreach (var moduleType in ModuleTypes)
            {
                foreach (var kind in Enum.GetValues<ExternalAccessFailureKind>())
                {
                    data.Add(moduleType, kind);
                }
            }

            return data;
        }
    }

    /// <summary>四个模块（被拒发送后缀用例用）。</summary>
    public static TheoryData<Type> ModuleTypeCases
    {
        get
        {
            var data = new TheoryData<Type>();

            foreach (var moduleType in ModuleTypes)
            {
                data.Add(moduleType);
            }

            return data;
        }
    }

    [Theory]
    [MemberData(nameof(Cases))]
    public void 每个失败种类都应有本模块句子(Type moduleType, ExternalAccessFailureKind kind)
    {
        var snapshot = Snapshot(
            ExternalAccessState.Faulted,
            failure: new ExternalAccessFailure(
                kind,
                new ExternalAccessEndpoint("127.0.0.1", 1234),
                Value: "1234",
                Detail: nameof(ExternalAccessFailureKind)));

        var text = Describe(moduleType, snapshot);

        text.ShouldNotBeNullOrWhiteSpace();
        TextCatalog.IsMissing(text).ShouldBeFalse($"失败种类 {kind} 的句子必须命中模块表（当前 {text}）");
    }

    [Theory]
    [MemberData(nameof(ModuleTypeCases))]
    public void 有被拒发送且会话未打开时_状态句追加发送已丢弃后缀(Type moduleType)
    {
        var suffix = string.Format(
            CultureInfo.CurrentCulture,
            TextCatalog.Get(moduleType.Assembly, RejectedSuffixKey(moduleType)),
            3);

        var closed = Describe(moduleType, Snapshot(ExternalAccessState.Closed, rejectedUnits: 3));
        closed.ShouldContain(suffix);

        var open = Describe(moduleType, Snapshot(ExternalAccessState.Open, rejectedUnits: 3));
        open.ShouldNotContain(suffix);
    }

    /// <summary>模块自己的"发送已丢弃"后缀键（文案只在模块表里，用例不抄句子）。</summary>
    private static string RejectedSuffixKey(Type moduleType) => moduleType.Name switch
    {
        nameof(SerialModule) => SerialText.StatusRejectedSuffix,
        nameof(TcpClientModule) => TcpClientText.StatusRejectedSuffix,
        nameof(TcpServerModule) => TcpServerText.StatusRejectedSuffix,
        nameof(UdpModule) => UdpText.StatusRejectedSuffix,
        _ => throw new ArgumentOutOfRangeException(nameof(moduleType)),
    };

    private static ExternalAccessSnapshot Snapshot(
        ExternalAccessState state,
        long rejectedUnits = 0,
        ExternalAccessFailure? failure = null) =>
        new(
            state,
            Suspended: false,
            Endpoint: new ExternalAccessEndpoint("127.0.0.1", 1234),
            Failure: failure,
            ReceivedUnits: 0,
            ReceivedBytes: 0,
            SentUnits: 0,
            SentBytes: 0,
            DroppedUnits: 0,
            DroppedBytes: 0,
            RejectedUnits: rejectedUnits,
            ActiveConnections: 0,
            LastReceivedAt: null);

    private static string Describe(Type moduleType, ExternalAccessSnapshot snapshot)
    {
        var module = Activator.CreateInstance(moduleType);
        var method = moduleType.GetMethod("Describe", BindingFlags.Instance | BindingFlags.NonPublic)
            ?? throw new InvalidOperationException($"{moduleType.Name} 没有 Describe 方法");

        return (string)method.Invoke(module, [snapshot])!;
    }
}
