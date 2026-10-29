namespace HYWDebugAssistant.Sdk.Configuration;

/// <summary>
/// 枚举成员在配置表单里的**显示名**（可选）。
/// </summary>
/// <remarks>
/// <para>
/// <b>为什么需要它</b>：配置表单的选项标题缺省就是**成员名**，而成员名必须能当标识符写回
/// （<c>Baud115200</c>、<c>Eight</c>），给人看却应该是 <c>115200</c>、<c>8</c> ——
/// 把"写回用的标识"与"给人看的字"合成一个，界面里就只能看到标识符。
/// </para>
/// <para>
/// <b>只影响显示</b>：写回（<c>UpdateConfigurationField</c>）用的始终是**成员名**，
/// 与主题、观感无关，也不参与任何框架判定。
/// </para>
/// </remarks>
/// <example>
/// <code>
/// public enum SerialBaudRate
/// {
///     [ConfigurationChoice("9600")] Baud9600 = 9600,
///     [ConfigurationChoice("115200")] Baud115200 = 115200,
/// }
/// </code>
/// </example>
[AttributeUsage(AttributeTargets.Field, AllowMultiple = false)]
public sealed class ConfigurationChoiceAttribute : Attribute
{
    /// <summary>创建显示名标注。</summary>
    /// <param name="title">给人看的字（如 <c>115200</c>）；空 ⇒ 退回成员名。</param>
    public ConfigurationChoiceAttribute(string? title = null) => Title = title;

    /// <summary>显示名（空 ⇒ 由成员名回退）。</summary>
    public string? Title { get; }

    /// <summary>
    /// 显示名的**文本表键**（拥有者 = 枚举所在程序集；命中优先于 <see cref="Title"/>）。
    /// </summary>
    /// <remarks>空 = 不用表，回退到 <see cref="Title"/> / 成员名。</remarks>
    public string? TitleKey { get; init; }
}
