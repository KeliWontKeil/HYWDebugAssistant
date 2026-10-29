using HYWDebugAssistant.Sdk.Text;

namespace HYWDebugAssistant.Hosting;

/// <summary>Hosting 诊断文本的**统一入口**（拥有者 = Hosting 程序集）。</summary>
internal static class HostingText
{
    /// <summary>取一条 Hosting 文本（参数按 <c>{0}</c>/<c>{1}</c> 顺序传入）。</summary>
    public static string Get(string key, params object?[] args) =>
        TextCatalog.Get(typeof(HostingText).Assembly, key, args);
}
