using Avalonia;
using Avalonia.Collections;
using Avalonia.Controls;
using Avalonia.Markup.Xaml.MarkupExtensions;
using Avalonia.Media;
using HYWDebugAssistant.Sdk.Ui;
using HYWDebugAssistant.Sdk.Values;
using HYWDebugAssistant.Sdk.Views;

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
public sealed class ModuleSelect : ComboBox, IModuleViewElement, IModuleElementInternal
{
    private bool _syncing;
    private bool _valueRegistered;
    private object? _baseToolTip;
    private bool _noticeToolTipActive;
    private IModuleViewContext? _context;
    private IReadOnlyList<ModuleSelectOption> _options = [];
    private string _selectedKey = string.Empty;
    private bool _appearanceInvalid;

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

        // 预览器没有 ModuleViewHost：设计上下文挂在视图 DataContext 上，挂树时读一次。
        DataContextChanged += (_, _) => ResolveDesignContext();
        AttachedToVisualTree += (_, _) => ResolveDesignContext();
    }

    private void ResolveDesignContext()
    {
        if (_context is null && DataContext is IModuleViewContext context)
        {
            _context = context;
            Refresh(context);
        }
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

    /// <summary>绑定的**值名**；非空时走统一值通道，空时保持旧的“控件自己抛事件”通道。</summary>
    public string Value { get; set; } = string.Empty;

    /// <summary>用户选择交出去的时机（下拉通常即时）。</summary>
    public ValueCommitMode CommitMode { get; set; } = ValueCommitMode.OnChange;

    /// <summary>用户切换选项（程序设置 <see cref="SelectedKey"/> 不触发）。</summary>
    public event EventHandler? OptionSelected;

    /// <inheritdoc />
    string IModuleElementInternal.Source => Value.Length > 0 ? Value : string.Empty;

    /// <inheritdoc />
    void IModuleElementInternal.ApplyModuleAppearance(ViewAppearance appearance) =>
        ApplyAppearance(appearance);

    /// <summary>
    /// 把模块设的外观落到下拉上：文字色/字号、只读禁用、失败红框。
    /// </summary>
    /// <param name="appearance">外观（全空 ⇒ 交还主题）。</param>
    private void ApplyAppearance(ViewAppearance appearance)
    {
        ArgumentNullException.ThrowIfNull(appearance);

        if (appearance.Text is { } text)
        {
            Foreground = new SolidColorBrush(Color.FromUInt32(text));
        }
        else if (appearance.TextToken is { Length: > 0 } textToken)
        {
            this[!ForegroundProperty] = new DynamicResourceExtension(textToken);
        }
        else
        {
            ClearValue(ForegroundProperty);
        }

        if (appearance.TextSize is { } size)
        {
            FontSize = size;
        }
        else
        {
            ClearValue(FontSizeProperty);
        }

        _appearanceInvalid = appearance.Outline is not null
            || appearance.OutlineToken is { Length: > 0 };
        UpdateErrorRing();

        // 下拉没有独立的只读叶子：只读就是不让展开/选择；显示值仍由值层刷新。
        IsEnabled = appearance.ReadOnly is not true && appearance.Enabled is not false;
    }

    /// <summary>失败红框只由模块设的外观轮廓驱动；Notice 只负责原因提示。</summary>
    private void UpdateErrorRing() => PseudoClasses.Set(":error", _appearanceInvalid);

    /// <inheritdoc />
    public void Refresh(IModuleViewContext context)
    {
        _context = context;

        if (Value.Length == 0)
        {
            return;
        }

        if (!_valueRegistered)
        {
            _valueRegistered = true;
            context.RegisterViewDefault(Value, SelectedKey);
        }

        var state = context.ReadValue(Value);

        if (!state.IsKnown)
        {
            UpdateErrorRing();
            ApplyNoticeToolTip(null);
            return;
        }

        if (state.Choices is { Count: > 0 } choices)
        {
            var options = choices
                .Select(choice => new ModuleSelectOption(choice.Value, choice.Title))
                .ToArray();

            if (!SameOptions(_options, options))
            {
                Options = options;
            }
        }

        if (!string.Equals(SelectedKey, state.Value, StringComparison.Ordinal))
        {
            SelectedKey = state.Value;
        }

        UpdateErrorRing();
        ApplyNoticeToolTip(state.Notice);
    }

    /// <summary>值结论的悬停提示；不覆盖 XAML 声明的提示。</summary>
    private void ApplyNoticeToolTip(ValueNotice? notice)
    {
        if (notice is { Message.Length: > 0 })
        {
            if (!_noticeToolTipActive)
            {
                _baseToolTip = ToolTip.GetTip(this);
                _noticeToolTipActive = true;
            }

            ToolTip.SetTip(this, notice.Message);
        }
        else if (_noticeToolTipActive)
        {
            ToolTip.SetTip(this, _baseToolTip);
            _noticeToolTipActive = false;
        }
    }

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

        if (Value.Length > 0 && _context is not null && CommitMode == ValueCommitMode.OnChange)
        {
            _context.CommitValue(Value, _selectedKey);
        }

        OptionSelected?.Invoke(this, EventArgs.Empty);
    }

    private static bool SameOptions(IReadOnlyList<ModuleSelectOption> left, ModuleSelectOption[] right)
    {
        if (left.Count != right.Length)
        {
            return false;
        }

        for (var index = 0; index < right.Length; index++)
        {
            if (!string.Equals(left[index].Key, right[index].Key, StringComparison.Ordinal)
                || !string.Equals(left[index].Label, right[index].Label, StringComparison.Ordinal))
            {
                return false;
            }
        }

        return true;
    }
}
