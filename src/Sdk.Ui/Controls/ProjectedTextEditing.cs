using System.Globalization;
using Avalonia.Controls;
using Avalonia.Input.Platform;
using HYWDebugAssistant.Sdk.Text;
using HYWDebugAssistant.Sdk.Values;

namespace HYWDebugAssistant.Sdk.Ui.Controls;

/// <summary>
/// **可编辑文本面的 Raw/投影桥**：编辑框里是显示投影，值始终是原始文本。
/// </summary>
/// <remarks>
/// 用户编辑先经文本簇对齐的差分换算成 Raw 修改，再重新投影；标记/方框是原子编辑单元。
/// 撤销栈保存 Raw 快照，保证撤销不会把显示标记恢复成字面文本。
/// </remarks>
internal sealed class ProjectedTextEditing
{
    private const int MaxUndoSteps = 50;
    private const long MaxUndoChars = 1_000_000;
    private const long CoalesceTicks = 700;

    private readonly TextBox _editor;
    private readonly List<EditSnapshot> _undo = [];
    private readonly List<EditSnapshot> _redo = [];
    private MenuItem? _undoItem;
    private MenuItem? _redoItem;
    private MenuItem? _cutItem;
    private MenuItem? _copyItem;
    private MenuItem? _pasteItem;

    private bool _suppress;
    private bool _markers = true;
    private ValueTextFormat _format = ValueTextFormat.Text;
    private string _raw = string.Empty;
    private TextProjection _projection = TextProjection.Create(string.Empty, markers: true);
    private string _lastDisplay = string.Empty;

    private long _lastUndoTicks;
    private EditKind _lastEditKind;
    private int _lastEditRawPosition;

    public ProjectedTextEditing(TextBox editor)
    {
        ArgumentNullException.ThrowIfNull(editor);

        _editor = editor;
        _editor.TextChanged += OnTextChanged;
        _editor.PropertyChanged += OnEditorPropertyChanged;
        InstallContextMenu();
    }

    public event EventHandler? UserEdited;

    public event EventHandler? ProjectionChanged;

    /// <summary>当前原始文本（提交、复制、撤销的权威值）。</summary>
    public string RawText => _raw;

    /// <summary>当前显示投影。</summary>
    public TextProjection Projection => _projection;

    /// <summary>当前需要警示色叠画的标记区间。</summary>
    public IReadOnlyList<TextMarkerSpan> MarkerSpans => _projection.GetMarkerSpans();

    /// <summary>当前用户输入的字符准入格式（程序化写入不经过它）。</summary>
    public void SetFormat(ValueTextFormat format) => _format = format;

    /// <summary>标记模式（true = 标记，false = 方框）。</summary>
    public void SetMarkerMode(bool markers)
    {
        if (_markers == markers)
        {
            return;
        }

        _markers = markers;
        RebuildProjection(preserveSelection: true);
    }

    /// <summary>设置原始文本并更新投影。</summary>
    /// <param name="text">原始文本。</param>
    /// <param name="clearHistory">是否清空撤销/重做历史。</param>
    public void SetRaw(string? text, bool clearHistory = true)
    {
        var value = text ?? string.Empty;

        if (string.Equals(_raw, value, StringComparison.Ordinal))
        {
            return;
        }

        if (clearHistory)
        {
            _undo.Clear();
            _redo.Clear();
            ResetCoalesce();
        }

        _raw = value;
        RebuildProjection(preserveSelection: false);
    }

    /// <summary>当前选区的原始文本。</summary>
    public string GetSelectedRawText()
    {
        var start = Math.Min(_editor.SelectionStart, _editor.SelectionEnd);
        var end = Math.Max(_editor.SelectionStart, _editor.SelectionEnd);

        if (end <= start)
        {
            return string.Empty;
        }

        var rawStart = _projection.DisplayToRaw(start, preferRight: false);
        var rawEnd = _projection.DisplayToRaw(end, preferRight: true);
        rawStart = Math.Clamp(rawStart, 0, _raw.Length);
        rawEnd = Math.Clamp(rawEnd, rawStart, _raw.Length);
        return _raw[rawStart..rawEnd];
    }

    /// <summary>复制 Raw 选区到剪贴板。</summary>
    public void CopySelection()
    {
        var text = GetSelectedRawText();

        if (text.Length == 0)
        {
            return;
        }

        if (TopLevel.GetTopLevel(_editor)?.Clipboard is { } clipboard)
        {
            _ = clipboard.SetTextAsync(text);
        }
    }

    /// <summary>剪切 Raw 选区到剪贴板。</summary>
    public void CutSelection()
    {
        var text = GetSelectedRawText();

        if (text.Length == 0)
        {
            return;
        }

        CopySelection();
        ReplaceSelectionRaw(string.Empty);
    }

    /// <summary>用一段字面 Raw 文本替换当前选区（Enter 换行等）。</summary>
    public void ReplaceSelectionRaw(string text)
    {
        ArgumentNullException.ThrowIfNull(text);

        var filtered = ValueTextFormats.Filter(_format, text);

        if (filtered.Length == 0 && text.Length > 0)
        {
            return;
        }

        var start = Math.Min(_editor.SelectionStart, _editor.SelectionEnd);
        var end = Math.Max(_editor.SelectionStart, _editor.SelectionEnd);
        var (rawStart, rawEnd) = _projection.DisplayRangeToRaw(start, end);

        PushUndo(rawStart, EditKind.Replace);
        _raw = string.Concat(_raw.AsSpan(0, rawStart), filtered, _raw.AsSpan(rawEnd));
        _redo.Clear();

        var rawCaret = rawStart + filtered.Length;
        _projection = TextProjection.Create(_raw, _markers);
        _lastDisplay = _projection.Display;

        _suppress = true;
        try
        {
            _editor.Text = _projection.Display;
            SetCaretFromRaw(rawCaret);
        }
        finally
        {
            _suppress = false;
        }

        RaiseProjectionChanged();
        RaiseUserEdited();
    }

    /// <summary>按目标换行档归一 Raw 里的真实换行；C 转义记法不受影响。</summary>
    /// <returns>是否改动。</returns>
    public bool NormalizeNewlines(NewlineMode mode)
    {
        var normalized = TextNewlines.Normalize(_raw, mode);

        if (string.Equals(normalized, _raw, StringComparison.Ordinal))
        {
            return false;
        }

        var rawCaret = CurrentRawCaret();
        PushUndo(rawCaret, EditKind.Replace);
        _raw = normalized;
        _redo.Clear();
        RebuildProjection(preserveSelection: true);
        RaiseProjectionChanged();
        return true;
    }

    /// <summary>撤销一步。</summary>
    public void Undo()
    {
        if (_undo.Count == 0)
        {
            return;
        }

        var current = new EditSnapshot(_raw, CurrentRawCaret());
        var snapshot = _undo[^1];
        _undo.RemoveAt(_undo.Count - 1);
        _redo.Add(current);

        ApplySnapshot(snapshot);
        ResetCoalesce();
        RaiseUserEdited();
    }

    /// <summary>重做一步。</summary>
    public void Redo()
    {
        if (_redo.Count == 0)
        {
            return;
        }

        var current = new EditSnapshot(_raw, CurrentRawCaret());
        var snapshot = _redo[^1];
        _redo.RemoveAt(_redo.Count - 1);
        _undo.Add(current);
        TrimUndo();

        ApplySnapshot(snapshot);
        ResetCoalesce();
        RaiseUserEdited();
    }

    /// <summary>从编辑器上摘除事件订阅。</summary>
    public void Detach()
    {
        _editor.TextChanged -= OnTextChanged;
        _editor.PropertyChanged -= OnEditorPropertyChanged;
    }

    private void InstallContextMenu()
    {
        _undoItem = new MenuItem { Header = Loc("moduleText.contextMenu.undo") };
        _undoItem.Click += (_, _) => Undo();

        _redoItem = new MenuItem { Header = Loc("moduleText.contextMenu.redo") };
        _redoItem.Click += (_, _) => Redo();

        _cutItem = new MenuItem { Header = Loc("moduleText.contextMenu.cut") };
        _cutItem.Click += (_, _) => CutSelection();

        _copyItem = new MenuItem { Header = Loc("moduleText.contextMenu.copy") };
        _copyItem.Click += (_, _) => CopySelection();

        _pasteItem = new MenuItem { Header = Loc("moduleText.contextMenu.paste") };
        _pasteItem.Click += (_, _) => _editor.Paste();

        var selectAll = new MenuItem { Header = Loc("moduleText.contextMenu.selectAll") };
        selectAll.Click += (_, _) => _editor.SelectAll();

        var menu = new ContextMenu
        {
            Items =
            {
                _undoItem,
                _redoItem,
                new Separator(),
                _cutItem,
                _copyItem,
                _pasteItem,
                selectAll,
            },
        };

        menu.Opening += (_, _) => UpdateContextMenu();
        _editor.ContextMenu = menu;
    }

    private void UpdateContextMenu()
    {
        var hasSelection = _editor.SelectionStart != _editor.SelectionEnd;
        var readOnly = _editor.IsReadOnly;

        if (_undoItem is not null)
        {
            _undoItem.IsEnabled = _undo.Count > 0 && !readOnly;
        }

        if (_redoItem is not null)
        {
            _redoItem.IsEnabled = _redo.Count > 0 && !readOnly;
        }

        if (_cutItem is not null)
        {
            _cutItem.IsEnabled = hasSelection && !readOnly;
        }

        if (_copyItem is not null)
        {
            _copyItem.IsEnabled = hasSelection;
        }

        if (_pasteItem is not null)
        {
            _pasteItem.IsEnabled = !readOnly;
        }
    }

    private static string Loc(string key) =>
        TextCatalog.Get(typeof(ProjectedTextEditing).Assembly, key);

    private void OnTextChanged(object? sender, TextChangedEventArgs e)
    {
        if (_suppress)
        {
            return;
        }

        var display = _editor.Text ?? string.Empty;

        if (string.Equals(display, _lastDisplay, StringComparison.Ordinal))
        {
            return;
        }

        ApplyUserEdit(display);
    }

    private void OnEditorPropertyChanged(object? sender, Avalonia.AvaloniaPropertyChangedEventArgs e)
    {
        if (_suppress)
        {
            return;
        }

        if (e.Property == TextBox.CaretIndexProperty
            || e.Property == TextBox.SelectionStartProperty
            || e.Property == TextBox.SelectionEndProperty)
        {
            SnapSelectionToMarkerBoundaries();
        }
    }

    private void ApplyUserEdit(string display)
    {
        var oldDisplay = _lastDisplay;
        var oldProjection = _projection;
        var oldRaw = _raw;

        var prefix = CommonPrefixLength(oldDisplay, display);
        prefix = BoundaryAtOrBefore(oldDisplay, prefix);
        prefix = Math.Min(prefix, BoundaryAtOrBefore(display, prefix));

        var suffix = CommonSuffixLength(oldDisplay, display, prefix);
        suffix = BoundaryAtOrBefore(oldDisplay, oldDisplay.Length - suffix) is var oldStart
            ? oldDisplay.Length - oldStart
            : suffix;
        suffix = Math.Min(
            suffix,
            display.Length - BoundaryAtOrBefore(display, display.Length - suffix));

        var oldChangedStart = prefix;
        var oldChangedEnd = oldDisplay.Length - suffix;
        var newChangedEnd = display.Length - suffix;

        if (oldChangedEnd < oldChangedStart)
        {
            oldChangedEnd = oldChangedStart;
        }

        if (newChangedEnd < oldChangedStart)
        {
            newChangedEnd = oldChangedStart;
        }

        var inserted = display[oldChangedStart..newChangedEnd];
        var filtered = ValueTextFormats.Filter(_format, inserted);

        if (filtered.Length == 0 && inserted.Length > 0)
        {
            RestoreRejectedEdit(oldChangedStart, oldChangedEnd);
            return;
        }

        inserted = filtered;

        var (rawStart, rawEnd) = oldProjection.DisplayRangeToRaw(oldChangedStart, oldChangedEnd);
        rawStart = Math.Clamp(rawStart, 0, oldRaw.Length);
        rawEnd = Math.Clamp(rawEnd, rawStart, oldRaw.Length);

        var newRaw = string.Concat(oldRaw.AsSpan(0, rawStart), inserted, oldRaw.AsSpan(rawEnd));

        if (string.Equals(newRaw, oldRaw, StringComparison.Ordinal))
        {
            // 编辑只动了显示层（例如在标记内部改字），恢复规范投影并保留选区。
            RebuildProjection(preserveSelection: true);
            return;
        }

        var kind = inserted.Length == 0
            ? EditKind.Delete
            : oldChangedEnd == oldChangedStart
                ? EditKind.Insert
                : EditKind.Replace;

        PushUndo(rawStart, kind);
        _raw = newRaw;
        _redo.Clear();

        _projection = TextProjection.Create(_raw, _markers);
        _lastDisplay = _projection.Display;

        var rawCaret = Math.Clamp(rawStart + inserted.Length, 0, _raw.Length);
        var identityDisplay = string.Equals(_projection.Display, _raw, StringComparison.Ordinal);
        var editorAlreadyShowsDisplay = string.Equals(_editor.Text, _lastDisplay, StringComparison.Ordinal);

        _suppress = true;
        try
        {
            if (!editorAlreadyShowsDisplay)
            {
                _editor.Text = _lastDisplay;
            }

            // 普通文本（无标记/转义投影）时编辑器的文本已经等于显示文本，原生 TextBox
            // 已把光标放在插入点；再写一次 CaretIndex 只会制造一次多余的位置回退。
            // 只有过滤/替换后文本真的变了，或投影不是 identity 时才需要重算光标。
            if (!identityDisplay || !editorAlreadyShowsDisplay)
            {
                SetCaretFromRaw(rawCaret);
            }
        }
        finally
        {
            _suppress = false;
        }

        RaiseProjectionChanged();
        RaiseUserEdited();
    }

    /// <summary>一次用户插入全被过滤：恢复原显示与选区，不产生编辑/撤销/提交。</summary>
    private void RestoreRejectedEdit(int oldChangedStart, int oldChangedEnd)
    {
        var start = Math.Clamp(oldChangedStart, 0, _lastDisplay.Length);
        var end = Math.Clamp(oldChangedEnd, start, _lastDisplay.Length);

        _suppress = true;
        try
        {
            if (!string.Equals(_editor.Text, _lastDisplay, StringComparison.Ordinal))
            {
                _editor.Text = _lastDisplay;
            }

            _editor.SelectionStart = start;
            _editor.SelectionEnd = end;
            _editor.CaretIndex = start;
        }
        finally
        {
            _suppress = false;
        }
    }

    private void RebuildProjection(bool preserveSelection)
    {
        var rawSelectionStart = 0;
        var rawSelectionEnd = 0;
        var rawCaret = 0;

        if (preserveSelection)
        {
            var start = Math.Min(_editor.SelectionStart, _editor.SelectionEnd);
            var end = Math.Max(_editor.SelectionStart, _editor.SelectionEnd);
            rawSelectionStart = _projection.DisplayToRaw(start, preferRight: false);
            rawSelectionEnd = _projection.DisplayToRaw(end, preferRight: true);
            rawCaret = _projection.DisplayToRaw(_editor.CaretIndex, preferRight: false);
        }

        _projection = TextProjection.Create(_raw, _markers);
        _lastDisplay = _projection.Display;

        _suppress = true;
        try
        {
            if (!string.Equals(_editor.Text, _lastDisplay, StringComparison.Ordinal))
            {
                _editor.Text = _lastDisplay;
            }

            if (preserveSelection)
            {
                var start = _projection.RawToDisplay(Math.Clamp(rawSelectionStart, 0, _raw.Length), preferRight: false);
                var end = _projection.RawToDisplay(Math.Clamp(rawSelectionEnd, 0, _raw.Length), preferRight: true);
                _editor.SelectionStart = Math.Min(start, end);
                _editor.SelectionEnd = Math.Max(start, end);
                _editor.CaretIndex = _projection.RawToDisplay(
                    Math.Clamp(rawCaret, 0, _raw.Length),
                    preferRight: false);
            }
            else
            {
                _editor.SelectionStart = 0;
                _editor.SelectionEnd = 0;
                _editor.CaretIndex = 0;
            }
        }
        finally
        {
            _suppress = false;
        }

        RaiseProjectionChanged();
    }

    private void ApplySnapshot(EditSnapshot snapshot)
    {
        _raw = snapshot.Raw;
        _projection = TextProjection.Create(_raw, _markers);
        _lastDisplay = _projection.Display;

        _suppress = true;
        try
        {
            if (!string.Equals(_editor.Text, _lastDisplay, StringComparison.Ordinal))
            {
                _editor.Text = _lastDisplay;
            }

            SetCaretFromRaw(snapshot.Caret);
        }
        finally
        {
            _suppress = false;
        }

        RaiseProjectionChanged();
    }

    private void SetCaretFromRaw(int rawCaret)
    {
        rawCaret = Math.Clamp(rawCaret, 0, _raw.Length);
        var display = _projection.RawToDisplay(rawCaret, preferRight: false);
        _editor.SelectionStart = display;
        _editor.SelectionEnd = display;
        _editor.CaretIndex = display;
    }

    private int CurrentRawCaret()
    {
        var caret = Math.Clamp(_editor.CaretIndex, 0, _projection.DisplayLength);
        return Math.Clamp(
            _projection.DisplayToRaw(caret, preferRight: false),
            0,
            _raw.Length);
    }

    private void SnapSelectionToMarkerBoundaries()
    {
        var start = Math.Clamp(_editor.SelectionStart, 0, _projection.DisplayLength);
        var end = Math.Clamp(_editor.SelectionEnd, 0, _projection.DisplayLength);
        var caret = Math.Clamp(_editor.CaretIndex, 0, _projection.DisplayLength);

        if (!_projection.IsInsideMarker(start) && !_projection.IsInsideMarker(end))
        {
            return;
        }

        _suppress = true;
        try
        {
            if (start == end)
            {
                _editor.CaretIndex = _projection.RawToDisplay(
                    _projection.DisplayToRaw(caret, preferRight: false),
                    preferRight: false);
                _editor.SelectionStart = _editor.CaretIndex;
                _editor.SelectionEnd = _editor.CaretIndex;
                return;
            }

            var snappedStart = _projection.RawToDisplay(
                _projection.DisplayToRaw(start, preferRight: false),
                preferRight: false);
            var snappedEnd = _projection.RawToDisplay(
                _projection.DisplayToRaw(end, preferRight: true),
                preferRight: true);

            _editor.SelectionStart = Math.Min(snappedStart, snappedEnd);
            _editor.SelectionEnd = Math.Max(snappedStart, snappedEnd);
            _editor.CaretIndex = _editor.SelectionEnd;
        }
        finally
        {
            _suppress = false;
        }
    }

    private void PushUndo(int rawPosition, EditKind kind)
    {
        var now = Environment.TickCount64;

        var canCoalesce = _undo.Count > 0
            && kind is EditKind.Insert or EditKind.Delete
            && _lastEditKind == kind
            && now - _lastUndoTicks <= CoalesceTicks
            && Math.Abs(rawPosition - _lastEditRawPosition) <= 1;

        if (!canCoalesce)
        {
            _undo.Add(new EditSnapshot(_raw, Math.Clamp(rawPosition, 0, _raw.Length)));
            TrimUndo();
        }

        _lastUndoTicks = now;
        _lastEditKind = kind;
        _lastEditRawPosition = rawPosition;
    }

    private void TrimUndo()
    {
        while (_undo.Count > MaxUndoSteps)
        {
            _undo.RemoveAt(0);
        }

        long chars = 0;

        for (var i = _undo.Count - 1; i >= 0; i--)
        {
            chars += _undo[i].Raw.Length;

            if (chars > MaxUndoChars)
            {
                _undo.RemoveRange(0, i + 1);
                break;
            }
        }
    }

    private void ResetCoalesce()
    {
        _lastUndoTicks = 0;
        _lastEditKind = EditKind.None;
        _lastEditRawPosition = 0;
    }

    private void RaiseUserEdited()
    {
        UserEdited?.Invoke(this, EventArgs.Empty);
    }

    private void RaiseProjectionChanged() => ProjectionChanged?.Invoke(this, EventArgs.Empty);

    private static int CommonPrefixLength(string left, string right)
    {
        var length = Math.Min(left.Length, right.Length);
        var index = 0;

        while (index < length && left[index] == right[index])
        {
            index++;
        }

        return index;
    }

    private static int CommonSuffixLength(string left, string right, int prefix)
    {
        var max = Math.Min(left.Length, right.Length) - prefix;
        var index = 0;

        while (index < max
            && left[left.Length - 1 - index] == right[right.Length - 1 - index])
        {
            index++;
        }

        return index;
    }

    private static int BoundaryAtOrBefore(string text, int index)
    {
        index = Math.Clamp(index, 0, text.Length);

        if (index == 0 || index == text.Length)
        {
            return index;
        }

        var starts = StringInfo.ParseCombiningCharacters(text);
        var low = 0;
        var high = starts.Length - 1;
        var found = 0;

        while (low <= high)
        {
            var middle = low + ((high - low) / 2);

            if (starts[middle] <= index)
            {
                found = starts[middle];
                low = middle + 1;
            }
            else
            {
                high = middle - 1;
            }
        }

        return found;
    }

    private enum EditKind
    {
        None = 0,
        Insert = 1,
        Delete = 2,
        Replace = 3,
    }

    private readonly record struct EditSnapshot(string Raw, int Caret);
}
