namespace HYWDebugAssistant.Core.Api;

/// <summary>配置字段的**编辑控件类型**（由属性类型判定；框架据此选控件，不猜语义）。</summary>
public enum ConfigurationFieldKind
{
    /// <summary>布尔（开关）。</summary>
    Boolean = 0,

    /// <summary>整数（整型属性）。</summary>
    WholeNumber = 1,

    /// <summary>实数。</summary>
    Number = 2,

    /// <summary>文本（字符串 / 路径等）。</summary>
    Text = 3,

    /// <summary>枚举（可选项来自枚举定义）。</summary>
    Enum = 4,
}

/// <summary>枚举字段的一个可选项。</summary>
/// <param name="Value">写回时使用的字面量（枚举成员名）。</param>
/// <param name="Title">显示标题（成员名，或模块标注的显示名）。</param>
public sealed record ConfigurationFieldChoice(string Value, string Title);

/// <summary>
/// 配置**字段**的视图（Q5-5：按配置 Schema 自动生成表单）——字段名/类型/范围/可选项/分组。
/// </summary>
/// <param name="Name">字段名（**写回时的键**）。</param>
/// <param name="Title">显示标题（模块标注优先；缺省由字段名生成，界面不出现代码化标识）。</param>
/// <param name="Group">分组标题（空 = 不分组）。</param>
/// <param name="Kind">编辑控件类型。</param>
/// <param name="Value">当前值的**文本形态**（显示与就地编辑都用它；回写时按 <paramref name="Kind"/> 解析）。</param>
/// <param name="Minimum">数值下限（空 = 不限）。</param>
/// <param name="Maximum">数值上限（空 = 不限）。</param>
/// <param name="Step">步长（空 = 由控件决定）。</param>
/// <param name="Unit">单位文本（空 = 无）。</param>
/// <param name="Description">说明文本（空 = 无）。</param>
/// <param name="Choices">可选项（仅 <see cref="ConfigurationFieldKind.Enum"/>）。</param>
public sealed record ConfigurationFieldView(
    string Name,
    string Title,
    string? Group,
    ConfigurationFieldKind Kind,
    string Value,
    double? Minimum = null,
    double? Maximum = null,
    double? Step = null,
    string? Unit = null,
    string? Description = null,
    IReadOnlyList<ConfigurationFieldChoice>? Choices = null);

/// <summary>
/// 实例配置的 **Schema + 当前值**（Q5-5 / `11` §4.2）——模块视图内的配置编辑与
/// 无 UI 模块的管理兜底**共用**同一份表单定义。
/// </summary>
/// <param name="InstanceId">实例 ID。</param>
/// <param name="Fields">字段（按声明顺序；分组由 <see cref="ConfigurationFieldView.Group"/> 表达）。</param>
/// <param name="Notice">
/// 无法编辑时的原因（无配置类型 / 占位实例 / 属性不可写）——空 = 可编辑。
/// </param>
public sealed record ConfigurationSchemaView(
    string InstanceId,
    IReadOnlyList<ConfigurationFieldView> Fields,
    string? Notice = null)
{
    /// <summary>是否可编辑（有字段且无阻断原因）。</summary>
    public bool IsEditable => Fields.Count > 0 && string.IsNullOrEmpty(Notice);
}
