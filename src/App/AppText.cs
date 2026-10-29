using HYWDebugAssistant.Sdk.Text;

namespace HYWDebugAssistant.App;

/// <summary>App 启动文案的**统一入口**（拥有者 = App 程序集）。</summary>
internal static class AppText
{
    /// <summary>取一条 App 文本（参数按 <c>{0}</c>/<c>{1}</c> 顺序传入）。</summary>
    public static string Get(string key, params object?[] args) =>
        TextCatalog.Get(typeof(AppText).Assembly, key, args);
}
