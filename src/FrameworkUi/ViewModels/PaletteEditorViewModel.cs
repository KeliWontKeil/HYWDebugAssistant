using Avalonia.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using HYWDebugAssistant.Core.Api;
using HYWDebugAssistant.Ui.Theming;
using HYWDebugAssistant.Ui.ViewModels;

namespace HYWDebugAssistant.FrameworkUi.ViewModels;

/// <summary>
/// **色板里"某一套主题的那一格"**（浅色或深色）：一个色块 + 它是"跟随"还是"已改"。
/// </summary>
/// <remarks>
/// <list type="bullet">
///   <item><b>留空 = 跟随</b>（该套主题的内置值）：跟随是默认状态，也是"恢复"的落点 ——
///     它不必写进设置文件，因此不产生死数据；</item>
///   <item><b>色块的显示值</b>：已改 ⇒ 改过的那个色；跟随 ⇒ 该套主题**当前的内置值**
///     （由 <see cref="ThemeApplier.PaletteBrush"/> 从主题字典里读，不必切主题去看）；</item>
///   <item><b>带不透明度的那几支</b>（页签淡底、遮罩）：面板只改**色**，浓度仍取内置值 ——
///     "淡"是观感设计，不该因为用户改了色就变成一块实色（取色窗口也不提供透明度）。</item>
/// </list>
/// </remarks>
public sealed partial class PaletteCellViewModel : ViewModelBase
{
    /// <summary>创建一格。</summary>
    /// <param name="token">色板 token 名。</param>
    /// <param name="theme">主题名（<see cref="ThemeIds"/>，如 <c>light</c> / <c>dark</c>）。</param>
    /// <param name="deriveFrom">
    /// 这一支**跟随**哪一支（契约里的 <c>derive.from</c>；<see langword="null"/> = 不派生）。
    /// </param>
    public PaletteCellViewModel(string token, string theme, string? deriveFrom)
    {
        Token = token;
        Theme = theme;
        DeriveFrom = deriveFrom;
    }

    /// <summary>色板 token 名。</summary>
    public string Token { get; }

    /// <summary>主题名（哪一套的格子）。</summary>
    public string Theme { get; }

    /// <summary>这一支跟随哪一支（<see langword="null"/> = 不派生，未改就用内置值）。</summary>
    public string? DeriveFrom { get; }

    /// <summary>用户改过的色（<c>#RRGGBB</c>）；<see langword="null"/> / 空 = **跟随**。</summary>
    [ObservableProperty]
    public partial string? Hex { get; set; }

    /// <summary>是否已改（决定"恢复"按钮出不出现）。</summary>
    public bool IsOverridden => Hex is { Length: > 0 };

    /// <summary>色块画刷：已改 ⇒ 那个色；跟随 ⇒ 该套主题当前的有效值（读不到 ⇒ 透明）。</summary>
    public IBrush Brush =>
        ThemeApplier.Parse(Hex) is { } configured
            ? new SolidColorBrush(configured)
            : ThemeApplier.PaletteBrush(Token, Theme) ?? Brushes.Transparent;

    /// <summary>状态文本（跟随 &lt;源&gt; / 跟随内置 / 改过的色值）。</summary>
    public string StateText => Hex is { Length: > 0 } hex
        ? hex
        : DeriveFrom is { Length: > 0 } source ? $"跟随 {source}" : "跟随内置";

    /// <summary>这一格的悬停提示（说清"跟随"跟的是谁）。</summary>
    public string ToolTip => DeriveFrom is { Length: > 0 } source
        ? $"没改过时跟着 {source}（色随它走，浓度按该套主题内置的值）；也可以直接改成你要的色"
        : "没改过时用该套主题的内置值";

    /// <summary>
    /// 这一格**当前的有效色文本**（已改 ⇒ 改过的值；跟随 ⇒ 该套主题当前的有效值）。
    /// </summary>
    /// <remarks>取色窗口的初值、以及「两套同色」的取值都用它 —— "当前看起来是什么色"只有这一个答案。</remarks>
    public string? EffectiveHex => Hex is { Length: > 0 } hex
        ? hex
        : ThemeApplier.PaletteBrush(Token, Theme) is ISolidColorBrush brush
            ? DataTypeColorRowViewModel.ToHex(brush.Color)
            : null;

    /// <summary>值变了 ⇒ 几个派生显示项一起通知。</summary>
    partial void OnHexChanged(string? value) => Refresh();

    /// <summary>让派生显示项重新求值（主题/内置值变了以后由面板统一调）。</summary>
    public void Refresh()
    {
        OnPropertyChanged(nameof(IsOverridden));
        OnPropertyChanged(nameof(Brush));
        OnPropertyChanged(nameof(StateText));
        OnPropertyChanged(nameof(EffectiveHex));
    }
}

/// <summary>
/// **色板里的一行**（一支色 × 面板的两栏）。
/// </summary>
/// <remarks>
/// 一行 = 一支 token：名字与用途来自**色板契约**（`Styles/palette.json`，唯一事实源），
/// 不在这里另写一份说明文字。
/// <para>
/// **面板今天按两栏（浅 / 深）呈现两套内置主题**：模型与资源机制都按"N 套主题"设计
/// （主题目录 + 按主题分层的覆盖），但这一页的两栏布局是给"今天只有两套"用的 ——
/// 主题多起来时，这一页要改成"先选主题、再列该主题的色板"。
/// </para>
/// </remarks>
public sealed class PaletteRowViewModel
{
    /// <summary>由契约里的一支创建一行。</summary>
    /// <param name="token">契约里的一支。</param>
    public PaletteRowViewModel(PaletteToken token)
    {
        ArgumentNullException.ThrowIfNull(token);

        Key = token.Key;
        Purpose = token.Purpose;
        DeriveFrom = token.Derive?.Source;
        Light = new PaletteCellViewModel(token.Key, LeftTheme.Id, token.Derive?.Source);
        Dark = new PaletteCellViewModel(token.Key, RightTheme.Id, token.Derive?.Source);
    }

    /// <summary>面板左栏的那套主题（目录里的第一套；今天 = 浅色）。</summary>
    private static ThemeDescriptor LeftTheme => ThemeCatalog.Themes[0];

    /// <summary>面板右栏的那套主题（目录里的第二套；今天 = 深色）。</summary>
    private static ThemeDescriptor RightTheme => ThemeCatalog.Themes[1];

    /// <summary>token 名（与色板 XAML 的 <c>x:Key</c> 逐字相同；也是这一行的显示名）。</summary>
    public string Key { get; }

    /// <summary>用途（契约里的一行说明；界面以悬停提示给出）。</summary>
    public string Purpose { get; }

    /// <summary>这一支跟随哪一支（<see langword="null"/> = 不派生）。</summary>
    public string? DeriveFrom { get; }

    /// <summary>浅色那一格。</summary>
    public PaletteCellViewModel Light { get; }

    /// <summary>深色那一格。</summary>
    public PaletteCellViewModel Dark { get; }

    /// <summary>按主题名取一格。</summary>
    /// <param name="theme">主题名。</param>
    public PaletteCellViewModel Cell(string theme) =>
        string.Equals(theme, RightTheme.Id, StringComparison.OrdinalIgnoreCase) ? Dark : Light;

    /// <summary>按"是不是右栏那一格"取一格（界面的两个取色入口共用同一条路径）。</summary>
    /// <param name="dark">true = 右栏（深色）那一格。</param>
    public PaletteCellViewModel Cell(bool dark) => dark ? Dark : Light;

    /// <summary>这一行改过几套（0 = 两套都在跟随）。</summary>
    public int OverriddenCount => (Light.IsOverridden ? 1 : 0) + (Dark.IsOverridden ? 1 : 0);
}

/// <summary>
/// **色板里的一个分区**（背景 / 边框 / 墨色 …），可折叠。
/// </summary>
/// <remarks>默认展开哪些区由契约的 <c>groups[].expanded</c> 说 —— 代码不挑。</remarks>
public sealed partial class PaletteGroupViewModel : ViewModelBase
{
    /// <summary>建一个分区。</summary>
    /// <param name="group">清单里的分组。</param>
    /// <param name="rows">这一区的行。</param>
    /// <param name="expanded">默认是否展开。</param>
    public PaletteGroupViewModel(PaletteGroup group, IEnumerable<PaletteRowViewModel> rows, bool expanded)
    {
        ArgumentNullException.ThrowIfNull(group);
        ArgumentNullException.ThrowIfNull(rows);

        Id = group.Id;
        Title = group.Title;
        IsExpanded = expanded;

        foreach (var row in rows)
        {
            Rows.Add(row);
        }
    }

    /// <summary>分组 id。</summary>
    public string Id { get; }

    /// <summary>分区标题。</summary>
    public string Title { get; }

    /// <summary>这一区的行。</summary>
    public System.Collections.ObjectModel.ObservableCollection<PaletteRowViewModel> Rows { get; } = [];

    /// <summary>是否展开（默认展开常用区）。</summary>
    [ObservableProperty]
    public partial bool IsExpanded { get; set; }

    /// <summary>这一区有几行已改（标题上给个计数，收起时也看得见）。</summary>
    public int OverriddenCount => Rows.Sum(row => row.OverriddenCount);

    /// <summary>标题上的计数文本（0 改 ⇒ 不显示）。</summary>
    public string CountText => $"{OverriddenCount} 项已改";

    /// <summary>有没有改过的项（决定计数文本出不出现）。</summary>
    public bool HasOverrides => OverriddenCount > 0;

    /// <summary>让计数文本重新求值（行改动后由面板调）。</summary>
    public void Refresh()
    {
        OnPropertyChanged(nameof(OverriddenCount));
        OnPropertyChanged(nameof(CountText));
        OnPropertyChanged(nameof(HasOverrides));
    }
}
