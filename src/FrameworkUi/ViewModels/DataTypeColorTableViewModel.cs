using System.Collections.ObjectModel;
using System.Globalization;
using Avalonia.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using HYWDebugAssistant.Ui.Theming;
using HYWDebugAssistant.Ui.ViewModels;

namespace HYWDebugAssistant.FrameworkUi.ViewModels;

/// <summary>
/// **数据结构（类型身份）→ 颜色** 映射表的一行【ADR-0054 · 用户第三十四轮】。
/// </summary>
/// <remarks>
/// <list type="bullet">
///   <item><b>一行 = 一种数据类型</b>（键 = 类型身份 `标识@v版本`；未声明类型的旧模块 = 结构描述）——
///     与端口标记的窄色条、叠加层角色方块的填充**同源**（都走 <see cref="PacketTypePalette"/>）；</item>
///   <item><b>颜色可改</b>：点色块打开取色弹窗（预设色片 + 十六进制），或"恢复自动"回落**稳定哈希配色**
///     （同一身份跨模块/跨重启同色 ⇒ 不改也能分辨类型）；</item>
///   <item><b>只呈现</b>：改色不参与建网/兼容/判定，也不进组合件。</item>
/// </list>
/// </remarks>
public sealed partial class DataTypeColorRowViewModel : ViewModelBase
{
    private readonly Action<string, string?> _apply;

    /// <summary>创建一行。</summary>
    /// <param name="typeKey">类型身份（`标识@v版本`）。</param>
    /// <param name="shapeText">形状呈现文本（给人看）。</param>
    /// <param name="usageText">用在（如 `4 个口 · 2 个角色`）。</param>
    /// <param name="overrideHex">用户覆盖色（null/空 = 自动配色）。</param>
    /// <param name="apply">应用回调（`typeKey, hex`；hex = null ⇒ 恢复自动）。</param>
    public DataTypeColorRowViewModel(
        string typeKey,
        string shapeText,
        string usageText,
        string? overrideHex,
        Action<string, string?> apply)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(typeKey);
        ArgumentNullException.ThrowIfNull(apply);

        TypeKey = typeKey;
        ShapeText = string.IsNullOrWhiteSpace(shapeText) ? "—" : shapeText;
        UsageText = usageText ?? string.Empty;
        OverrideHex = string.IsNullOrWhiteSpace(overrideHex) ? null : overrideHex;
        _apply = apply;
    }

    /// <summary>类型身份（色板键；跨模块一致）。</summary>
    public string TypeKey { get; }

    /// <summary>形状呈现文本（`{seq:u64, value:f32}` 之类；悬停可见全文）。</summary>
    [ObservableProperty]
    public partial string ShapeText { get; set; }

    /// <summary>用在（该身份在当前现场被多少口/角色使用）。</summary>
    [ObservableProperty]
    public partial string UsageText { get; set; }

    /// <summary>用户覆盖色（null = 该类型走**自动配色**）。</summary>
    [ObservableProperty]
    public partial string? OverrideHex { get; set; }

    /// <summary>是否有用户覆盖色（决定"恢复自动"是否可用）。</summary>
    public bool HasOverride => OverrideHex is { Length: > 0 };

    /// <summary>当前生效颜色（覆盖优先，否则自动配色）——色块绑定用。</summary>
    public IBrush Brush => PacketTypePalette.For(TypeKey);

    /// <summary>当前生效颜色（调色板窗口的初值）。</summary>
    public Color CurrentColor => Brush is ISolidColorBrush solid ? solid.Color : Colors.Gray;

    /// <summary>覆盖色变化 ⇒ 两个派生显示项一起通知。</summary>
    partial void OnOverrideHexChanged(string? value)
    {
        OnPropertyChanged(nameof(HasOverride));
        OnPropertyChanged(nameof(Brush));
        OnPropertyChanged(nameof(CurrentColor));
    }

    /// <summary>
    /// **就地刷新**一行（同一类型的形状/用量/覆盖色变化时调用）。
    /// </summary>
    /// <param name="shapeText">形状文本。</param>
    /// <param name="usageText">用量文本。</param>
    /// <param name="overrideHex">用户覆盖色（null = 自动）。</param>
    /// <remarks>
    /// 【用户第三十四轮实测："鼠标指到色块上时**不停的闪烁**"】根因 = 刷新时**重建整张表**
    /// （半个刷新周期换一批控件，正在打开的取色弹窗目标被销毁 ⇒ 反复开关 = 闪烁）。
    /// ⇒ 行对象**只在类型集合变化时增删**，其余情况一律**就地改值**（集合稳定 ⇒ 弹窗不会被拆掉）。
    /// </remarks>
    internal void Update(string shapeText, string usageText, string? overrideHex)
    {
        ShapeText = string.IsNullOrWhiteSpace(shapeText) ? "—" : shapeText;
        UsageText = usageText ?? string.Empty;
        OverrideHex = string.IsNullOrWhiteSpace(overrideHex) ? null : overrideHex;
    }

    /// <summary>用户在**调色板窗口**里选了颜色 ⇒ 落成覆盖色。</summary>
    /// <param name="color">选中的颜色。</param>
    public void ApplyPick(Color color) => _apply(TypeKey, ToHex(color));

    /// <summary>"恢复自动色" ⇒ 移除覆盖，回落稳定哈希配色。</summary>
    public void ResetToAuto() => _apply(TypeKey, null);

    /// <summary>画刷 → `#RRGGBB`（**写入设置的就是这个文本**）。</summary>
    /// <param name="brush">画刷（非纯色画刷返回占位色文本）。</param>
    /// <returns>`#RRGGBB` 形式。</returns>
    public static string ToHex(IBrush? brush) => brush is ISolidColorBrush solid
        ? ToHex(solid.Color)
        : "#9AA4B2";

    /// <summary>颜色 → `#RRGGBB`。</summary>
    /// <param name="color">颜色。</param>
    /// <returns>`#RRGGBB` 形式。</returns>
    public static string ToHex(Color color) =>
        string.Create(CultureInfo.InvariantCulture, $"#{color.R:X2}{color.G:X2}{color.B:X2}");
}

/// <summary>
/// **数据结构 → 颜色** 表格的容器（行集合 + 空态文案）【ADR-0054】。
/// </summary>
public sealed partial class DataTypeColorTableViewModel : ViewModelBase
{
    /// <summary>表格行（按类型身份排序）。</summary>
    public ObservableCollection<DataTypeColorRowViewModel> Rows { get; } = [];

    /// <summary>是否为空（现场还没有声明任何数据类型）。</summary>
    [ObservableProperty]
    public partial bool IsEmpty { get; set; } = true;

    /// <summary>空态文案（一句事实，不喊口号）。</summary>
    [ObservableProperty]
    public partial string EmptyText { get; set; } = "现场还没有数据类型";
}
