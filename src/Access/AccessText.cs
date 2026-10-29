using HYWDebugAssistant.Sdk.Text;

namespace HYWDebugAssistant.Access;

/// <summary>Access 诊断文本的**统一入口**（拥有者 = Access 程序集）。</summary>
internal static class AccessText
{
    /// <summary>取一条 Access 文本（参数按 <c>{0}</c>/<c>{1}</c> 顺序传入）。</summary>
    public static string Get(string key, params object?[] args) =>
        TextCatalog.Get(typeof(AccessText).Assembly, key, args);
}
