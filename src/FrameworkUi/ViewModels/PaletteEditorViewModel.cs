using Avalonia.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using HYWDebugAssistant.Core.Api;
using HYWDebugAssistant.Sdk.Text;
using HYWDebugAssistant.Ui.Theming;
using HYWDebugAssistant.Ui.ViewModels;

namespace HYWDebugAssistant.FrameworkUi.ViewModels;

/// <summary>本文件三类视图模型的文本表入口（拥有者 = FrameworkUi 程序集）。</summary>
internal static class PaletteText
{
    /// <summary>取一条文案。</summary>
    /// <param name="key">键。</param>
    public static string Get(string key) => TextCatalog.Get(typeof(PaletteText).Assembly, key);

    /// <summary>取一条带占位符的文案。</summary>
    /// <param name="key">键。</param>
    /// <param name="args">格式化参数。</param>
    public static string Get(string key, params object?[] args) =>
        TextCatalog.Get(typeof(PaletteText).Assembly, key, args);
}

/// <summary>
/// **色板里"某一套主题的那一格"**：一个色块 + 它跟随的是哪支。
/// </summary>
/// <remarks>
/// <list type="bullet">
///   <item><b>留空 = 跟随</b>（主题基类型的内置值 / 派生源）：跟随是默认状态，也是"恢复"的落点；</item>
///   <item><b>色块的显示值</b>：已改 ⇒ 改过的色；跟随 ⇒ 主题当前的有效值
///     （由 <see cref="ThemeApplier.PaletteBrush"/> 按草稿主题现算，不要求主题已经应用）；</item>
///   <item><b>带不透明度的那几支</b>（页签淡底、遮罩）：面板只改**色**，浓度仍取内置值。</item>
/// </list>
/// </remarks>
public sealed partial class PaletteCellViewModel : ViewModelBase
{
    private ThemeDescriptor? _theme;

    /// <summary>创建一格。</summary>
    /// <param name="token">色板 token 名。</param>
    /// <param name="deriveFrom">这一支**跟随**哪一支（契约里的 <c>derive.from</c>；null = 不派生）。</param>
    public PaletteCellViewModel(string token, string? deriveFrom)
    {
        Token = token;
        DeriveFrom = deriveFrom;
    }

    /// <summary>色板 token 名。</summary>
    public string Token { get; }

    /// <summary>这一支跟随哪一支（<see langword="null"/> = 不派生，未改就用内置值）。</summary>
    public string? DeriveFrom { get; }

    /// <summary>主题里这一支的显式值（<c>#RRGGBB</c>）；<see langword="null"/> / 空 = **跟随**。</summary>
    [ObservableProperty]
    public partial string? Hex { get; set; }

    /// <summary>是否已改。</summary>
    public bool IsOverridden => Hex is { Length: > 0 };

    /// <summary>色块画刷：已改 ⇒ 那个色；跟随 ⇒ 主题当前的有效值。</summary>
    public IBrush Brush =>
        _theme is { } theme
            ? ThemeApplier.PaletteBrush(Token, theme) ?? Brushes.Transparent
            : Brushes.Transparent;

    /// <summary>这一格**当前的有效色文本**（取色窗口初值 / 显示用）。</summary>
    public string? EffectiveHex => Brush is ISolidColorBrush brush
        ? DataTypeColorRowViewModel.ToHex(brush.Color)
        : null;

    /// <summary>这一格的状态文本（跟随 / 已改）。</summary>
    public string StateText => Hex is { Length: > 0 } hex
        ? hex
        : DeriveFrom is { Length: > 0 } source
            ? PaletteText.Get("palette.cell.state.derived", source)
            : PaletteText.Get("palette.cell.state.builtin");

    /// <summary>这一格的悬停提示（说清"跟随"跟的是谁）。</summary>
    public string ToolTip => DeriveFrom is { Length: > 0 } source
        ? PaletteText.Get("palette.cell.tooltip.derived", source)
        : PaletteText.Get("palette.cell.tooltip.builtin");

    /// <summary>把一格同步到"当前主题（草稿身份）+ 当前色板值"。</summary>
    /// <param name="theme">当前主题身份（含草稿色板）。</param>
    /// <param name="overrideHex">该 token 在主题色板里的值（null = 跟随）。</param>
    public void Sync(ThemeDescriptor theme, string? overrideHex)
    {
        _theme = theme;
        Hex = overrideHex;
        Refresh();
    }

    /// <summary>值/主题变了 ⇒ 几个派生显示项一起通知。</summary>
    partial void OnHexChanged(string? value) => Refresh();

    /// <summary>让派生显示项重新求值（主题/内置值/草稿变后由面板统一调）。</summary>
    public void Refresh()
    {
        OnPropertyChanged(nameof(IsOverridden));
        OnPropertyChanged(nameof(Brush));
        OnPropertyChanged(nameof(EffectiveHex));
        OnPropertyChanged(nameof(StateText));
    }
}

/// <summary>
/// **色板里的一行**（一支色 × 当前主题的一格）。
/// </summary>
/// <remarks>
/// 名字与用途来自**色板契约**（`Styles/palette.json`，唯一事实源），不在这里另写一份说明文字。
/// </remarks>
public sealed class PaletteRowViewModel
{
    /// <summary>由契约里的一支创建一行。</summary>
    /// <param name="token">契约里的一支。</param>
    public PaletteRowViewModel(PaletteToken token)
    {
        ArgumentNullException.ThrowIfNull(token);

        Key = token.Key;
        Title = token.Title;
        Purpose = token.Purpose;
        DeriveFrom = token.Derive?.Source;
        Cell = new PaletteCellViewModel(token.Key, token.Derive?.Source);
    }

    /// <summary>token 名（与色板 XAML 的 <c>x:Key</c> 逐字相同；只在悬停提示里出现）。</summary>
    public string Key { get; }

    /// <summary>显示名（中文友好名；来自色板契约）。</summary>
    public string Title { get; }

    /// <summary>用途（契约里的一行说明；界面以悬停提示给出）。</summary>
    public string Purpose { get; }

    /// <summary>这一支跟随哪一支（<see langword="null"/> = 不派生）。</summary>
    public string? DeriveFrom { get; }

    /// <summary>当前主题的那一格。</summary>
    public PaletteCellViewModel Cell { get; }

    /// <summary>当前格是否已改。</summary>
    public bool IsOverridden => Cell.IsOverridden;

    /// <summary>把这一行同步到"当前主题 + 当前色板值"。</summary>
    /// <param name="theme">当前主题身份。</param>
    /// <param name="overrideHex">该 token 在主题色板里的值（null = 跟随）。</param>
    public void Sync(ThemeDescriptor theme, string? overrideHex) =>
        Cell.Sync(theme, overrideHex);
}

/// <summary>
/// **色板里的一个分区**（背景 / 强调 / 方向 …），可折叠。
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
    public int OverriddenCount => Rows.Count(row => row.IsOverridden);

    /// <summary>标题上的计数文本（0 改 ⇒ 不显示）。</summary>
    public string CountText => PaletteText.Get("palette.override.count", OverriddenCount);

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

/// <summary>
/// **主题卡**（"跟随系统"或某一套具体主题）：显示名 + 基类型 + 三枚预览色点 + 选中态。
/// </summary>
/// <remarks>
/// 它只负责"设置页里选哪套主题 / 哪份色板"的呈现；真正的主题切换仍由「应用 / 确认」触发。
/// </remarks>
public sealed partial class ThemeCardViewModel : ViewModelBase
{
    private IBrush _preview1 = Brushes.Transparent;
    private IBrush _preview2 = Brushes.Transparent;
    private IBrush _preview3 = Brushes.Transparent;
    private string _title = string.Empty;
    private string _baseLabel = string.Empty;
    private bool _isSelected;

    /// <summary>创建一张卡。</summary>
    /// <param name="id">主题 id（<see cref="ThemeIds.System"/> 或某套主题名）。</param>
    /// <param name="isSystem">是不是"跟随系统"。</param>
    /// <param name="isBuiltIn">是不是内置主题。</param>
    public ThemeCardViewModel(string id, bool isSystem, bool isBuiltIn)
    {
        Id = id;
        IsSystem = isSystem;
        IsBuiltIn = isBuiltIn;
    }

    /// <summary>主题 id（选择值）。</summary>
    public string Id { get; }

    /// <summary>是不是“跟随系统”（选择项，不是主题）。</summary>
    public bool IsSystem { get; }

    /// <summary>是不是内置主题。</summary>
    public bool IsBuiltIn { get; }

    /// <summary>是不是自定义主题（草稿或磁盘上的主题文件夹）。</summary>
    public bool IsCustom => !IsSystem && !IsBuiltIn;

    /// <summary>显示名。</summary>
    public string Title
    {
        get => _title;
        private set => SetProperty(ref _title, value);
    }

    /// <summary>基类型标签（系统 / 内置 / 自定义 / 浅色底 / 深色底）。</summary>
    public string BaseLabel
    {
        get => _baseLabel;
        private set => SetProperty(ref _baseLabel, value);
    }

    /// <summary>是否当前选中（选中态视觉）。</summary>
    public bool IsSelected
    {
        get => _isSelected;
        private set => SetProperty(ref _isSelected, value);
    }

    /// <summary>预览色 1（外壳底 / 系统卡：浅色模块底）。</summary>
    public IBrush Preview1
    {
        get => _preview1;
        private set => SetProperty(ref _preview1, value);
    }

    /// <summary>预览色 2（模块底 / 系统卡：深色模块底）。</summary>
    public IBrush Preview2
    {
        get => _preview2;
        private set => SetProperty(ref _preview2, value);
    }

    /// <summary>预览色 3（主题色）。</summary>
    public IBrush Preview3
    {
        get => _preview3;
        private set => SetProperty(ref _preview3, value);
    }

    /// <summary>悬停提示。</summary>
    public string ToolTip => IsSystem
        ? PaletteText.Get("palette.card.tooltip.system")
        : IsBuiltIn
            ? PaletteText.Get("palette.card.tooltip.builtin")
            : PaletteText.Get("palette.card.tooltip.custom");

    /// <summary>把卡刷新到当前草稿（标题 / 基类型 / 预览色 / 选中态）。</summary>
    /// <param name="theme">具体主题（系统卡为 null）。</param>
    /// <param name="systemLight">跟随系统在浅色模式下使用的那套（草稿身份）。</param>
    /// <param name="systemDark">跟随系统在深色模式下使用的那套（草稿身份）。</param>
    /// <param name="selected">是否当前选中。</param>
    public void Refresh(
        ThemeDescriptor? theme,
        ThemeDescriptor? systemLight,
        ThemeDescriptor? systemDark,
        bool selected)
    {
        if (IsSystem)
        {
            Title = PaletteText.Get("palette.card.system.title");
            BaseLabel = PaletteText.Get("palette.card.system.base");
            Preview1 = Brush(systemLight, "BgModule");
            Preview2 = Brush(systemDark, "BgModule");
            Preview3 = Brush(systemLight, "AccentPrimary");
        }
        else if (theme is { } concrete)
        {
            Title = concrete.Title;
            BaseLabel = IsBuiltIn
                ? concrete.Kind == ThemeKind.Dark
                    ? PaletteText.Get("palette.card.base.builtinDark")
                    : PaletteText.Get("palette.card.base.builtinLight")
                : concrete.Kind == ThemeKind.Dark
                    ? PaletteText.Get("palette.card.base.customDark")
                    : PaletteText.Get("palette.card.base.customLight");
            Preview1 = Brush(concrete, "BgChrome");
            Preview2 = Brush(concrete, "BgModule");
            Preview3 = Brush(concrete, "AccentPrimary");
        }

        IsSelected = selected;
    }

    private static IBrush Brush(ThemeDescriptor? theme, string token) =>
        theme is { } concrete
            ? ThemeApplier.PaletteBrush(token, concrete) ?? Brushes.Transparent
            : Brushes.Transparent;
}
