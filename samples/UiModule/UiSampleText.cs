using HYWDebugAssistant.Sdk.Views;

namespace HYWDebugAssistant.Samples.UiModule;

/// <summary>本模块用到的文本键（表在 <c>resources/i18n/&lt;程序集简名&gt;/</c> 下）。</summary>
internal static class UiSampleText
{
    /// <summary>模块标题。</summary>
    public const string Title = "module.title";

    /// <summary>模块说明。</summary>
    public const string Description = "module.description";

    /// <summary>视图标题。</summary>
    public const string ViewTitle = "view.title";

    /// <summary>计数标签。</summary>
    public const string CountLabel = "view.count";

    /// <summary>复位按钮。</summary>
    public const string Reset = "view.reset";

    /// <summary>状态：就绪。</summary>
    public const string Ready = "state.ready";

    /// <summary>状态：已清零。</summary>
    public const string ResetDone = "state.reset";

    /// <summary>角色名。</summary>
    public const string RoleName = "role.count";

    /// <summary>按当前语言取一句话。</summary>
    /// <param name="key">文本键。</param>
    public static string Get(string key) => Sdk.Text.TextCatalog.Get(typeof(UiSampleText).Assembly, key);

    /// <summary>组合一个数值显示源的字节（小端 U64）。</summary>
    /// <param name="value">计数值。</param>
    /// <param name="destination">目标缓冲（至少 8 字节）。</param>
    public static void Write(ulong value, Span<byte> destination) =>
        System.Buffers.Binary.BinaryPrimitives.WriteUInt64LittleEndian(destination, value);
}
