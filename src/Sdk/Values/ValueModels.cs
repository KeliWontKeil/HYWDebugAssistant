namespace HYWDebugAssistant.Sdk.Values;

/// <summary>值的编辑形态（由值声明给出，视图只按它选择编辑器）。</summary>
public enum ValueEditor
{
    /// <summary>单行/多行文本。</summary>
    Text = 0,

    /// <summary>数字文本。</summary>
    Number = 1,

    /// <summary>布尔值（<c>true</c> / <c>false</c>）。</summary>
    Boolean = 2,

    /// <summary>枚举值（从 <see cref="ValueChoice"/> 中选择）。</summary>
    Enum = 3,
}

/// <summary>一个可选值（稳定键 + 给人看的文本）。</summary>
/// <param name="Value">写回时的稳定字面量。</param>
/// <param name="Title">显示文本。</param>
public sealed record ValueChoice(string Value, string Title);

/// <summary>值错误的严重程度。</summary>
public enum ValueNoticeSeverity
{
    /// <summary>提示。</summary>
    Info = 0,

    /// <summary>警告。</summary>
    Warning = 1,

    /// <summary>错误。</summary>
    Error = 2,
}

/// <summary>某个值当前的那一句结论（错误 / 警告 / 提示）。</summary>
/// <param name="Severity">严重程度。</param>
/// <param name="Message">给人看的一句话。</param>
public sealed record ValueNotice(ValueNoticeSeverity Severity, string Message);

/// <summary>某个值当前的有效性结论：是否无效，以及（可选的）原因。</summary>
/// <param name="IsInvalid">是否无效（无效时控件画失败轮廓）。</param>
/// <param name="Notice">原因；<see langword="null"/> = 只有状态没有文字。</param>
/// <remarks>
/// 它与 <see cref="ValueNotice"/> 是组合关系而不是二选一：只描红不给原因时
/// <see cref="IsInvalid"/> 为真、<see cref="Notice"/> 为空；只有警告/提示时
/// <see cref="IsInvalid"/> 为假、<see cref="Notice"/> 有值。
/// </remarks>
public readonly record struct ValueValidity(bool IsInvalid, ValueNotice? Notice)
{
    /// <summary>有效且没有结论。</summary>
    public static readonly ValueValidity Valid = new(false, null);

    /// <summary>无效并给一句原因。</summary>
    /// <param name="message">原因。</param>
    public static ValueValidity Error(string message) =>
        new(true, new ValueNotice(ValueNoticeSeverity.Error, message));

    /// <summary>有效但给一句警告。</summary>
    /// <param name="message">警告。</param>
    public static ValueValidity Warning(string message) =>
        new(false, new ValueNotice(ValueNoticeSeverity.Warning, message));

    /// <summary>有效但给一句提示。</summary>
    /// <param name="message">提示。</param>
    public static ValueValidity Info(string message) =>
        new(false, new ValueNotice(ValueNoticeSeverity.Info, message));
}

/// <summary>值的声明：名字、编辑器、可选项、持久化与重置属性。</summary>
/// <remarks>
/// <para>
/// 这里**没有“配置值/输入值”的分类**：值就是值。是否持久化、是否参与模块还原，
/// 都是这个声明上的正交属性。视图可见值的初值由视图 XAML 声明，不在这里重复。
/// </para>
/// <para>
/// 视图控件只按名字绑定；它不知道值最终落在哪一份存储里。
/// </para>
/// </remarks>
/// <param name="Name">值名（模块内唯一）。</param>
public sealed record ValueDeclaration(string Name)
{
    /// <summary>编辑形态。</summary>
    public ValueEditor Editor { get; init; } = ValueEditor.Text;

    /// <summary>可选值（仅 <see cref="ValueEditor.Enum"/>）。</summary>
    public IReadOnlyList<ValueChoice>? Choices { get; init; }

    /// <summary>是否随组合件/工作区保存。</summary>
    public bool Persistent { get; init; } = true;

    /// <summary>是否参与模块窗口的“还原”。</summary>
    public bool ResetWithModule { get; init; } = true;
}

/// <summary>一个值此刻对视图呈现的全部事实。</summary>
/// <param name="Name">值名。</param>
/// <param name="Editor">编辑形态。</param>
/// <param name="Value">当前有效值。</param>
/// <param name="DeclaredDefault">声明初值（当前用于配置字段的当前值；视图值的初值由 XAML 播种）。</param>
/// <param name="Choices">可选值。</param>
/// <param name="IsReadOnly">是否只读。</param>
/// <param name="IsEnabled">是否可操作。</param>
/// <param name="Validity">当前有效性结论（是否无效 + 原因）。</param>
/// <param name="Revision">值状态变更号（用于“没变就不碰视觉”）。</param>
/// <param name="IsKnown">这个值是否有运行期事实；false = 设计期/无上下文，控件应保留 XAML 初值。</param>
public sealed record ValueState(
    string Name,
    ValueEditor Editor,
    string Value,
    string DeclaredDefault,
    IReadOnlyList<ValueChoice>? Choices,
    bool IsReadOnly,
    bool IsEnabled,
    ValueValidity Validity,
    long Revision,
    bool IsKnown = false)
{
    /// <summary>当前是否无效（<see cref="Validity"/> 的派生读法）。</summary>
    public bool IsInvalid => Validity.IsInvalid;

    /// <summary>当前结论（<see cref="Validity"/> 的派生读法；空 = 没有话说）。</summary>
    public ValueNotice? Notice => Validity.Notice;

    /// <summary>一个“这个值还没有事实”的空状态。</summary>
    /// <param name="name">值名。</param>
    public static ValueState Empty(string name) => new(
        name,
        ValueEditor.Text,
        string.Empty,
        string.Empty,
        null,
        false,
        true,
        ValueValidity.Valid,
        0);
}
