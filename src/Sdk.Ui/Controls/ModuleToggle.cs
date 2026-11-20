using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using HYWDebugAssistant.Sdk.Values;

namespace HYWDebugAssistant.Sdk.Ui.Controls;

/// <summary>
/// **开关**（绑一个值）：勾上提交 <c>"true"</c>、取消提交 <c>"false"</c>。
/// </summary>
/// <remarks>
/// <para>
/// 控件只读 <see cref="IModuleViewContext.ReadValue"/>、按 <see cref="CommitMode"/> 提交；
/// 它不知道值最终落在配置还是视图状态里，也不判断错误。
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

    private IModuleViewContext? _context;
    private bool _syncing;
    private bool _readOnly;
    private bool _valueRegistered;
    private object? _baseToolTip;
    private bool _noticeToolTipActive;

    /// <summary>造一个开关（视觉结构在 `ControlTheme` 的 `PART_Track` / `PART_Knob` 里）。</summary>
    public ModuleToggle()
    {
        Focusable = true;
        Cursor = new Cursor(StandardCursorType.Hand);
        PseudoClasses.Set(":checked", false);

        // 预览器没有 ModuleViewHost：设计上下文挂在视图 DataContext 上，挂树时读一次。
        DataContextChanged += (_, _) => ResolveDesignContext();
        AttachedToVisualTree += (_, _) => ResolveDesignContext();
    }

    private void ResolveDesignContext()
    {
        if (_context is null && DataContext is IModuleViewContext context)
        {
            Refresh(context);
        }
    }

    /// <summary>用户拨动（或模块写回）后的勾选态。</summary>
    public bool IsChecked
    {
        get => GetValue(IsCheckedProperty);
        set => SetValue(IsCheckedProperty, value);
    }

    /// <summary>绑定的**值名**；空 = 未绑定。</summary>
    public string Value { get; set; } = string.Empty;

    /// <summary>用户拨动交出去的时机（开关通常即时）。</summary>
    public ValueCommitMode CommitMode { get; set; } = ValueCommitMode.OnChange;

    /// <summary>值通道用值名寻址外观。</summary>
    protected override string AppearanceKey => Value.Length > 0 ? Value : base.AppearanceKey;

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
        if (_context is null || _syncing)
        {
            return;
        }

        if (Value.Length > 0 && CommitMode == ValueCommitMode.OnChange)
        {
            _context.CommitValue(Value, value ? "true" : "false");
        }
    }

    /// <inheritdoc />
    public override void Refresh(IModuleViewContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        _context = context;

        if (Value.Length == 0)
        {
            return;
        }

        if (!_valueRegistered)
        {
            _valueRegistered = true;
            context.RegisterViewDefault(Value, IsChecked ? "true" : "false");
        }

        var state = context.ReadValue(Value);

        if (!state.IsKnown)
        {
            ApplyNoticeToolTip(null);
            return;
        }

        var wantedValue = string.Equals(state.Value, "true", StringComparison.OrdinalIgnoreCase);

        if (IsChecked != wantedValue)
        {
            _syncing = true;
            IsChecked = wantedValue;
            _syncing = false;
        }

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
