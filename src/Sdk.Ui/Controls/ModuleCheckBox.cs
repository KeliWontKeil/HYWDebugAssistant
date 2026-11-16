using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using HYWDebugAssistant.Sdk.Values;

namespace HYWDebugAssistant.Sdk.Ui.Controls;

/// <summary>
/// **勾选框**（绑一个布尔值）：勾上提交 <c>"true"</c>、取消提交 <c>"false"</c>。
/// </summary>
/// <remarks>
/// <para>
/// 与 <see cref="ModuleToggle"/> 同一套值语义（<see cref="Value"/> / <see cref="CommitMode"/> /
/// <see cref="Refresh"/> / 只读与提示），区别只在视觉：方框 + 勾形，文字作为 <see cref="ContentControl.Content"/> 放在右侧。
/// </para>
/// <para>
/// 视觉结构住在 `ControlTheme` 的 `PART_Box` / `PART_Check` / `PART_ContentPresenter` 里；
/// 控件代码不创建任何图形（AR-21）。
/// </para>
/// </remarks>
public sealed class ModuleCheckBox : ModuleElement
{
    /// <summary>是否处于勾选态。</summary>
    public static readonly StyledProperty<bool> IsCheckedProperty =
        AvaloniaProperty.Register<ModuleCheckBox, bool>(nameof(IsChecked));

    private IModuleViewContext? _context;
    private bool _syncing;
    private bool _readOnly;
    private bool _valueRegistered;
    private object? _baseToolTip;
    private bool _noticeToolTipActive;

    /// <summary>造一个勾选框（方框与勾形在 `ControlTheme` 里）。</summary>
    public ModuleCheckBox()
    {
        Focusable = true;
        Cursor = new Cursor(StandardCursorType.Hand);
        PseudoClasses.Set(":checked", false);
    }

    /// <summary>用户点选（或模块写回）后的勾选态。</summary>
    public bool IsChecked
    {
        get => GetValue(IsCheckedProperty);
        set => SetValue(IsCheckedProperty, value);
    }

    /// <summary>绑定的**值名**；空 = 未绑定。</summary>
    public string Value { get; set; } = string.Empty;

    /// <summary>用户点选交出去的时机（勾选框通常即时）。</summary>
    public ValueCommitMode CommitMode { get; set; } = ValueCommitMode.OnChange;

    /// <summary>值通道用值名寻址外观。</summary>
    protected override string AppearanceKey => Value.Length > 0 ? Value : base.AppearanceKey;

    /// <summary>勾选态变化（**程序化写回也会触发**；调用方用 <c>_syncing</c> 区分）。</summary>
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

        if (_syncing || _context is null)
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

        var wanted = string.Equals(state.Value, "true", StringComparison.OrdinalIgnoreCase);
        if (IsChecked != wanted)
        {
            _syncing = true;
            IsChecked = wanted;
            _syncing = false;
        }

        ApplyNoticeToolTip(state.Notice);
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

    /// <inheritdoc />
    protected override void ApplyReadOnly(bool isReadOnly)
    {
        _readOnly = isReadOnly;
        IsEnabled = !isReadOnly;
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
}
