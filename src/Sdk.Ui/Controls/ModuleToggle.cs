using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;

namespace HYWDebugAssistant.Sdk.Ui.Controls;

/// <summary>
/// **开关**（绑一条输入源）：勾上提交 <c>"true"</c>、取消提交 <c>"false"</c>。
/// </summary>
/// <remarks>
/// <para>
/// 显示值优先从**输入状态**读：模块把用户拨动后的值写回 `ViewState.SetInputValue`，
/// 这就是"用户拨了"与"模块认了"的同一份事实；没有输入状态时才回退到同名显示源。
/// 只读显示源会导致刷新时把开关弹回 false，而模块内部变量已经变了。
/// </para>
/// <para>
/// 视觉是模块自己的滑块（轨道 + 圆钮 + 滑动过渡），在 `ControlTheme` 的 `PART_Track` /
/// `PART_Knob` 里；不再使用 Fluent `CheckBox`。
/// </para>
/// </remarks>
public sealed class ModuleToggle : ModuleElement
{
    /// <summary>是否处于打开态。</summary>
    public static readonly StyledProperty<bool> IsCheckedProperty =
        AvaloniaProperty.Register<ModuleToggle, bool>(nameof(IsChecked));

    /// <summary>开关旁边的文字（空 = 不显示）。</summary>
    public static readonly StyledProperty<string> TextProperty =
        AvaloniaProperty.Register<ModuleToggle, string>(nameof(Text), defaultValue: string.Empty);

    private IModuleViewContext? _context;
    private bool _syncing;
    private bool _readOnly;

    /// <summary>造一个开关（视觉结构在 `ControlTheme` 的 `PART_Track` / `PART_Knob` 里）。</summary>
    public ModuleToggle()
    {
        Focusable = true;
        Cursor = new Cursor(StandardCursorType.Hand);
        PseudoClasses.Set(":checked", false);
    }

    /// <summary>用户拨动（或模块写回）后的勾选态。</summary>
    public bool IsChecked
    {
        get => GetValue(IsCheckedProperty);
        set => SetValue(IsCheckedProperty, value);
    }

    /// <summary>开关旁边的文字。</summary>
    public string Text
    {
        get => GetValue(TextProperty);
        set => SetValue(TextProperty, value);
    }

    /// <summary>勾选态变化（**程序化写回也会触发**；调用方用 `_syncing` 区分）。</summary>
    public event EventHandler? CheckedChanged;

    /// <inheritdoc />
    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);

        if (change.Property != IsCheckedProperty)
        {
            return;
        }

        var value = change.GetNewValue<bool>();
        PseudoClasses.Set(":checked", value);
        CheckedChanged?.Invoke(this, EventArgs.Empty);

        if (_syncing)
        {
            return;
        }

        // 用户拨动 ⇒ 提交意愿；模块写回（`_syncing`）不得再提交一次。
        if (_context is not null && Source.Length > 0)
        {
            _ = _context.SubmitInput(Source, value ? "true" : "false");
        }
    }

    /// <inheritdoc />
    public override void Refresh(IModuleViewContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        _context = context;

        if (Source.Length == 0)
        {
            return;
        }

        // **输入开关的事实源是输入状态，不是显示源**：模块把用户拨动后的值写回
        // `ViewState.SetInputValue`；只看 `ReadLatest` 会拿到 null，刷新时把开关弹回 false，
        // 而模块内部变量其实已经改了。显示源只作为没有输入状态时的兼容回退。
        var text = context.ReadInputText(Source);

        bool? wanted = text is not null
            ? string.Equals(text, "true", StringComparison.OrdinalIgnoreCase)
            : context.ReadLatest(Source) is { } number
                ? number != 0
                : null;

        if (wanted is null || IsChecked == wanted.Value)
        {
            return;
        }

        _syncing = true;
        IsChecked = wanted.Value;
        _syncing = false;
    }

    /// <inheritdoc />
    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);

        if (!_readOnly && e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
        {
            SetCurrentValue(IsCheckedProperty, !IsChecked);
            e.Handled = true;
            Focus();
        }
    }

    /// <inheritdoc />
    protected override void OnKeyDown(KeyEventArgs e)
    {
        if (!_readOnly && e.Key is Key.Space or Key.Enter)
        {
            SetCurrentValue(IsCheckedProperty, !IsChecked);
            e.Handled = true;
            return;
        }

        base.OnKeyDown(e);
    }

    /// <summary>运行期只读：用户不能拨动（显示值仍由模块写）。</summary>
    /// <param name="isReadOnly">是否只读。</param>
    protected override void ApplyReadOnly(bool isReadOnly)
    {
        _readOnly = isReadOnly;
        IsEnabled = !isReadOnly;
    }
}
