namespace HYWDebugAssistant.Sdk.Configuration;

/// <summary>
/// **配置项的动态可选项**（可选接口）：实现它的配置类型可以在表单生成时给出"现在有哪些可选值"。
/// </summary>
/// <remarks>
/// <para>
/// 用于**选项随环境变化**的字段 —— 典型是"可用串口列表"：值不能写死在枚举里，而人又不该手打
/// <c>COM3</c>（打错了要到打开失败才知道）。
/// </para>
/// <para>
/// 框架**只把它当数据**：某字段返回了表（**哪怕是空表**）⇒ 该字段按**下拉**呈现（值即显示文本）；
/// 返回 <see langword="null"/> ⇒ 该字段按它的**声明类型**呈现（文本）。
/// <b>空表与"不是动态字段"是两件事</b>：前者是"现在一个都没有"（设备没插上），
/// 后者是"这个字段**原本**就没有可选项" —— 混同会让"插拔设备"看起来像"字段换了一种控件"。
/// </para>
/// <list type="bullet">
///   <item>会被**反复调用**（生成表单时、每次展开下拉时）⇒ 实现应当廉价，必要时自己缓存；</item>
///   <item>实现**不得抛异常、不得阻塞** —— 它跑在界面线程上；失败就返回空表；</item>
///   <item>当前值由框架保证留在选项里（否则表单会把已存的值显示成列表里的第一项，等于骗人）。</item>
/// </list>
/// </remarks>
public interface IConfigurationChoices
{
    /// <summary>该字段现在有哪些可选值。</summary>
    /// <param name="fieldName">字段名（配置类型的属性名）。</param>
    /// <returns>
    /// 可选值的字面量（同时用作显示文本）；**空表 = 是动态字段但现在没有可选项**；
    /// <see langword="null"/> = 该字段不是动态字段。
    /// </returns>
    IReadOnlyList<string>? GetChoices(string fieldName);

    /// <summary>
    /// 动态选项集合的版本号；用于让 Schema 缓存在"设备插拔"后重建。
    /// </summary>
    /// <remarks>
    /// 默认 0 = 不参与版本失效（旧实现行为不变）。实现应保持廉价；只有选项集合变化时才递增。
    /// </remarks>
    long GetChoicesRevision() => 0;

    /// <summary>
    /// 当前值不在动态集合里时，是否仍把当前值留在选项表中。
    /// </summary>
    /// <remarks>
    /// 默认 true：值随环境变化但已保存的当前值仍有意义（例如手打地址）。
    /// 串口这类“当前端口已经不存在”的字段返回 false，避免消失的设备继续出现在下拉里。
    /// </remarks>
    bool KeepCurrentChoiceWhenUnavailable => true;
}
