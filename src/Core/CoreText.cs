using HYWDebugAssistant.Sdk.Text;

namespace HYWDebugAssistant.Core;

/// <summary>Core 诊断文本的**统一入口**：拥有者 = Core 程序集，键在 Core 的文本表里。</summary>
/// <remarks>
/// 只做"取表 + 格式化"这一件事：诊断/状态文案不再写在代码里，换语言只换表。
/// 键按"谁在说"分区（<c>core.runtime.*</c> 等），调用方直接给键与参数。
/// </remarks>
internal static class CoreText
{
    /// <summary>取一条 Core 文本（参数按 <c>{0}</c>/<c>{1}</c> 顺序传入）。</summary>
    /// <param name="key">文本表键。</param>
    /// <param name="args">格式化参数（数字/路径/异常消息先格式化再传）。</param>
    public static string Get(string key, params object?[] args) =>
        TextCatalog.Get(typeof(CoreText).Assembly, key, args);
}
