namespace HYWDebugAssistant.Sdk.Views;

/// <summary>
/// **颜色角色词表** —— 声明面与运行期**共用的一份词汇**：说"这一块是哪一支色"，
/// 具体色值由主题给（浅/深各一份）⇒ 深浅主题、用户改色都自动跟随。
/// </summary>
/// <remarks>
/// <para>
/// <b>与业务语义无关</b>：框架**不认识**"失败"，它只认识"配色表里的哪一支"。
/// 被否掉的仍然是"把成功/失败做成参数"（那等于替模块规定"失败必须长这样"）。
/// </para>
/// <para>
/// <b>可扩展</b>：加一个词 = 加一个主题键 + 在这里加一行常量与登记（<b>不改调用方</b>）。
/// 角色词**不是层级**（不是"主要/次要"两档）—— 需要多少支色就有多少支。
/// </para>
/// <para>
/// <b>写在哪</b>：声明面的 <c>textColor</c> / <c>titleColor</c>（与 <c>#RRGGBB</c> 二选一），
/// 未来运行期的角色版 setter 也收同一份词。**写 hex 表示"刻意固定"**（深浅主题都不变，代价自担）。
/// </para>
/// </remarks>
public static class ViewInkRoles
{
    /// <summary>缺省墨色（正文、值、读数、按钮字）。</summary>
    public const string Default = "default";

    /// <summary>弱化墨色（次要说明）。</summary>
    public const string Muted = "muted";

    /// <summary>主题色。</summary>
    public const string Accent = "accent";

    /// <summary>更重一档的主题色（指向 / 悬停）。</summary>
    public const string AccentStrong = "accentStrong";

    /// <summary>次色（与主题色邻近，避免"一片蓝"）。</summary>
    public const string AccentSecondary = "accentSecondary";

    /// <summary>状态：正常。</summary>
    public const string Success = "success";

    /// <summary>状态：注意。</summary>
    public const string Warning = "warning";

    /// <summary>状态：失败。</summary>
    public const string Danger = "danger";

    /// <summary>状态：未知 / 空闲。</summary>
    public const string Idle = "idle";

    /// <summary>方向：发送（稳定语义色，与主题色解耦）。</summary>
    public const string DirectionSend = "directionSend";

    /// <summary>方向：接收。</summary>
    public const string DirectionReceive = "directionReceive";

    private static readonly HashSet<string> Known = new(StringComparer.Ordinal)
    {
        Default, Muted, Accent, AccentStrong, AccentSecondary,
        Success, Warning, Danger, Idle, DirectionSend, DirectionReceive,
    };

    /// <summary>这个词是不是登记过的角色（非法词 ⇒ 声明读取时按硬伤报出）。</summary>
    /// <param name="word">声明里写的字面量。</param>
    /// <returns>登记过 ⇒ <see langword="true"/>。</returns>
    public static bool IsKnown(string? word) => !string.IsNullOrEmpty(word) && Known.Contains(word);
}
