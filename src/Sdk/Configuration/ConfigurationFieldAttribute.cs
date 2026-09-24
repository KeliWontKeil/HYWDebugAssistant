namespace HYWDebugAssistant.Sdk.Configuration;

/// <summary>
/// 配置字段的**可呈现元数据**（可选）——用于框架按配置 Schema 自动生成编辑表单（B1 元素）。
/// </summary>
/// <remarks>
/// <list type="bullet">
///   <item><b>可选</b>：不标注也能生成表单（字段名 → 标题、属性类型 → 编辑控件）；标注用于补充
///     **标题 / 分组 / 取值范围 / 步长 / 单位 / 说明**，让界面不出现代码化标识（主文档 §10.4）；</item>
///   <item><b>语义归模块</b>：框架只呈现，范围与步长仅是编辑界面的约束（框架**不据此裁剪**配置值）；</item>
///   <item>属**兼容扩展**（只增不改语义）：新增字段不影响既有模块。</item>
/// </list>
/// </remarks>
/// <example>
/// <code>
/// public sealed class SamplerConfig
/// {
///     [ConfigurationField("采样周期", Group = "采集", Minimum = 1, Maximum = 60000, Unit = "ms")]
///     public int PeriodMs { get; set; } = 100;
/// }
/// </code>
/// </example>
[AttributeUsage(AttributeTargets.Property, Inherited = true, AllowMultiple = false)]
public sealed class ConfigurationFieldAttribute : Attribute
{
    /// <summary>创建字段元数据。</summary>
    /// <param name="title">显示标题（缺省时由属性名生成）。</param>
    public ConfigurationFieldAttribute(string? title = null) => Title = title;

    /// <summary>显示标题（可为空 = 由属性名生成）。</summary>
    public string? Title { get; }

    /// <summary>分组标题（同类字段归一组；空 = 不分组）。</summary>
    public string? Group { get; init; }

    /// <summary>说明文本（界面上的辅助提示；空 = 不显示）。</summary>
    public string? Description { get; init; }

    /// <summary>数值下限（含；仅用于编辑界面约束与校验提示）。<see cref="double.NaN"/> = 不限制。</summary>
    public double Minimum { get; init; } = double.NaN;

    /// <summary>数值上限（含）。<see cref="double.NaN"/> = 不限制。</summary>
    public double Maximum { get; init; } = double.NaN;

    /// <summary>步长（数值调节的粒度）。<see cref="double.NaN"/> = 由界面决定。</summary>
    public double Step { get; init; } = double.NaN;

    /// <summary>单位文本（显示用；**不入契约**——单位属显示层）。</summary>
    public string? Unit { get; init; }
}
