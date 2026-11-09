using Avalonia.Controls;

namespace HYWDebugAssistant.Sdk.Ui.Controls;

/// <summary>下拉选项（键 + 本地化显示文本）。</summary>
/// <param name="Key">提交给模块的稳定键。</param>
/// <param name="Label">界面显示文本（模块 i18n）。</param>
public sealed record ModuleSelectOption(string Key, string Label);

/// <summary>
/// **模块通用下拉**：固定选项 + 稳定键，不绑定动态源名。
/// </summary>
/// <remarks>
/// 视图代码设置 <see cref="Options"/> 与 <see cref="SelectedKey"/>；用户切换时抛
/// <see cref="OptionSelected"/>，由视图决定提交到哪个输入/命令。控件不读数据源、不理解业务。
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

    private void Rebuild()
    {
        _syncing = true;
        var labels = new string[_options.Count];
        for (var index = 0; index < _options.Count; index++)
        {
            labels[index] = _options[index].Label;
        }

        ItemsSource = labels;
        _syncing = false;
        ApplySelectedKey();
    }

    private void ApplySelectedKey()
    {
        var index = -1;
        for (var candidate = 0; candidate < _options.Count; candidate++)
        {
            if (string.Equals(_options[candidate].Key, _selectedKey, StringComparison.Ordinal))
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

        if (_syncing || SelectedIndex < 0 || SelectedIndex >= _options.Count)
        {
            return;
        }

        _selectedKey = _options[SelectedIndex].Key;
        OptionSelected?.Invoke(this, EventArgs.Empty);
    }
}
