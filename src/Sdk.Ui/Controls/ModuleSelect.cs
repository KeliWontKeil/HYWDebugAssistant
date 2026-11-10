using Avalonia.Collections;
using Avalonia.Controls;

namespace HYWDebugAssistant.Sdk.Ui.Controls;

/// <summary>下拉选项（键 + 本地化显示文本）。</summary>
/// <remarks>
/// 记录提供无参构造与 `init` 属性，让视图可以在 XAML 的 `PreviewOptions` 里直接声明预览选项；
/// 运行期仍可继续用位置构造。
/// </remarks>
public sealed record ModuleSelectOption
{
    /// <summary>造一个空选项（XAML 用）。</summary>
    public ModuleSelectOption()
    {
    }

    /// <summary>造一个下拉选项。</summary>
    /// <param name="Key">提交给模块的稳定键。</param>
    /// <param name="Label">界面显示文本（模块 i18n）。</param>
    public ModuleSelectOption(string Key, string Label)
    {
        this.Key = Key;
        this.Label = Label;
    }

    /// <summary>提交给模块的稳定键。</summary>
    public string Key { get; init; } = string.Empty;

    /// <summary>界面显示文本（模块 i18n）。</summary>
    public string Label { get; init; } = string.Empty;
}

/// <summary>
/// **模块通用下拉**：固定选项 + 稳定键，不绑定动态源名。
/// </summary>
/// <remarks>
/// <para>
/// 视图代码设置 <see cref="Options"/> 与 <see cref="SelectedKey"/>；用户切换时抛
/// <see cref="OptionSelected"/>，由视图决定提交到哪个输入/命令。控件不读数据源、不理解业务。
/// </para>
/// <para>
/// <see cref="PreviewOptions"/> 只服务预览：当运行期 <see cref="Options"/> 为空时，
/// 设计环境用这里的样例选项把下拉画出来；运行期永远不用它。
/// </para>
/// </remarks>
public sealed class ModuleSelect : ComboBox
{
    private bool _syncing;
    private IReadOnlyList<ModuleSelectOption> _options = [];
    private string _selectedKey = string.Empty;

    /// <summary>造一个通用下拉。</summary>
    public ModuleSelect()
    {
        SelectionChanged += OnSelectionChanged;
        Classes.Add("module-select");
        PreviewOptions.CollectionChanged += (_, _) =>
        {
            if (ModuleView.IsDesignEnvironment)
            {
                Rebuild();
            }
        };
    }

    /// <summary>选项集合（键 + 显示文本）。</summary>
    public IReadOnlyList<ModuleSelectOption> Options
    {
        get => _options;
        set
        {
            _options = value ?? [];
            Rebuild();
        }
    }

    /// <summary>
    /// **预览选项**：只在设计环境、且 <see cref="Options"/> 为空时使用；运行期视图不读它。
    /// </summary>
    public AvaloniaList<ModuleSelectOption> PreviewOptions { get; } = [];

    /// <summary>当前选中项的键（不存在时保持原选中，不静默选第一项）。</summary>
    public string SelectedKey
    {
        get => _selectedKey;
        set
        {
            _selectedKey = value ?? string.Empty;
            ApplySelectedKey();
        }
    }

    /// <summary>用户切换选项（程序设置 <see cref="SelectedKey"/> 不触发）。</summary>
    public event EventHandler? OptionSelected;

    /// <inheritdoc />
    protected override Type StyleKeyOverride => typeof(ComboBox);

    /// <summary>当前实际参与显示的选项：运行期优先，预览期才回退到 <see cref="PreviewOptions"/>。</summary>
    private IReadOnlyList<ModuleSelectOption> EffectiveOptions =>
        _options.Count > 0 || !ModuleView.IsDesignEnvironment || PreviewOptions.Count == 0
            ? _options
            : PreviewOptions;

    private void Rebuild()
    {
        _syncing = true;
        var options = EffectiveOptions;
        var labels = new string[options.Count];
        for (var index = 0; index < options.Count; index++)
        {
            labels[index] = options[index].Label;
        }

        ItemsSource = labels;
        _syncing = false;
        ApplySelectedKey();
    }

    private void ApplySelectedKey()
    {
        var options = EffectiveOptions;
        var index = -1;
        for (var candidate = 0; candidate < options.Count; candidate++)
        {
            if (string.Equals(options[candidate].Key, _selectedKey, StringComparison.Ordinal))
            {
                index = candidate;
                break;
            }
        }

        if (index < 0 || SelectedIndex == index)
        {
            return;
        }

        _syncing = true;
        SelectedIndex = index;
        _syncing = false;
    }

    private void OnSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        _ = sender;
        _ = e;

        var options = EffectiveOptions;
        if (_syncing || SelectedIndex < 0 || SelectedIndex >= options.Count)
        {
            return;
        }

        _selectedKey = options[SelectedIndex].Key;
        OptionSelected?.Invoke(this, EventArgs.Empty);
    }
}
