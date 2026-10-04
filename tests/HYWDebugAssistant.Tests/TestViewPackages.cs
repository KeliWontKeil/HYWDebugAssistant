namespace HYWDebugAssistant.Tests;

/// <summary>
/// 桩模块的包内声明：给数据面端口一个位置、声明它会写的数据源。
/// </summary>
/// <remarks>
/// 数据面端口**没有包内声明就没有位置**（框架不按惯例推断）⇒ 任何要跑通数据流的桩模块都得带一份声明。
/// 按入口类型的名字挑一份现成的，别在每个用例文件里重写一遍 JSON。
/// </remarks>
internal static class TestViewPackages
{
    /// <summary>取该桩模块应挂的包目录。</summary>
    public static string For(Type entryType)
    {
        var name = entryType.Name;

        if (name.Contains("Drain", StringComparison.Ordinal))
        {
            return Drain;
        }

        if (name.Contains("ViewSender", StringComparison.Ordinal))
        {
            return ViewSender;
        }

        if (name.Contains("Dual", StringComparison.Ordinal))
        {
            return DualReceiver;
        }

        return name.Contains("Receiver", StringComparison.Ordinal) ? Receiver : Sender;
    }

    private static readonly string Sender =
        TempViewPackage.Create("stub-sender", [("out", "right")], []);

    private static readonly string Receiver =
        TempViewPackage.Create("stub-receiver", [("in", "left")], []);

    private static readonly string DualReceiver =
        TempViewPackage.Create("stub-dual-receiver", [("in1", "left"), ("in2", "left")], []);

    /// <summary>带读数控件的发送桩：把发出去的量写进 <c>value</c>。</summary>
    private static readonly string ViewSender =
        TempViewPackage.Create("stub-view-sender", [("out", "right")], [("value", "F64")]);

    /// <summary>主动接收桩：把收到多少单元写进 <c>received</c>。</summary>
    private static readonly string Drain =
        TempViewPackage.Create("stub-drain", [("in", "left")], [("received", "U64")]);
}
