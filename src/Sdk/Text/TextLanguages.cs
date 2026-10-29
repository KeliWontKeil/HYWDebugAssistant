namespace HYWDebugAssistant.Sdk.Text;

/// <summary>
/// **界面文本的语言标识**：既是设置里存的值，也是文本表的文件名主干。
/// </summary>
/// <remarks>
/// 取值用 BCP-47 文化名（<c>zh-Hans</c>），而不是自造的短码 —— 这样 <see cref="TextCatalog"/>
/// 可以直接拿当前 UI 文化去查表，不必维护一张"我们的语言名 ↔ 文化名"的对照表。
/// </remarks>
public static class TextLanguages
{
    /// <summary>**跟随系统**（用系统的 UI 文化，落到受支持语言，否则中文简体）。</summary>
    public const string System = "system";

    /// <summary>**中文（简体）**——回退链的最后一站，也是内置兜底语言。</summary>
    public const string ZhHans = "zh-Hans";
}
