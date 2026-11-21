using System.Globalization;
using System.Text.Json;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.VisualTree;
using HYWDebugAssistant.Sdk.Text;
using HYWDebugAssistant.Sdk.Values;

namespace HYWDebugAssistant.Sdk.Ui.Controls;

/// <summary>列表行：每行由六个**已声明的值名**驱动，控件不关心字段语义。</summary>
/// <param name="Id">行标识（模块用槽位号）。</param>
/// <param name="LabelValue">标签值名（单行文本）。</param>
/// <param name="ContentValue">内容值名（多行文本）。</param>
/// <param name="HexValue">HEX 勾选值名（布尔）。</param>
/// <param name="CrValue">CR 勾选值名（布尔）。</param>
/// <param name="LfValue">LF 勾选值名（布尔）。</param>
/// <param name="NewlineValue">换行档值名（文本，供内容面的 <c>EnterNewlineValue</c>）。</param>
public sealed record ModuleValueListRow(
    string Id,
    string LabelValue,
    string ContentValue,
    string HexValue,
    string CrValue,
    string LfValue,
    string NewlineValue);

/// <summary>行内动作：<c>send</c> 或 <c>delete</c>。</summary>
/// <param name="RowId">行标识。</param>
/// <param name="Action">动作键。</param>
public readonly record struct ModuleValueListRowAction(string RowId, string Action);

/// <summary>
/// **值名驱动的行容器**：把六个已声明的值名排成一行（标签 / 内容 / HEX / CR / LF / 换行档），
/// 并处理勾选顺序、过滤与行内发送/删除事件。
/// </summary>
/// <remarks>
/// <para>
/// 它只经 <see cref="IModuleViewContext"/> 的**已有**值契约取数/提交：
/// <c>ReadValue</c> / <c>CommitValue</c>；不新增上下文成员，也不读写配置或视图状态。
/// 行内编辑器是真实的已声明值，因此宿主用根上下文即可刷新，不需要作用域。
/// </para>
/// <para>
/// <b>勾选顺序显示在勾选标记里</b>：未选是空框；选中时整块换成强调底并显示白色 1 基序号。
/// 序号只来自控件本地的勾选顺序，写入 <see cref="SelectionValue"/> 供模块读取。
/// </para>
/// <para>
/// 行内发送按钮在该行内容值被判为无效时禁用；批量/连续的闸门由模块按勾选集合统一处理。
/// </para>
/// </remarks>
public sealed class ModuleValueList : ModuleElement
{
    private const string EmptyKey = "moduleList.empty";
    private const string NoMatchKey = "moduleList.noMatch";
    private const string DeleteKey = "moduleList.delete";
    private const string SelectKey = "moduleList.select";
    private const string PreviewLabelKey = "moduleList.previewLabel";
    private const string PreviewContentKey = "moduleList.previewContent";

    private readonly StackPanel _rowsPanel = new();
    private readonly ScrollViewer _scroll;
    private readonly ModuleLabel _placeholder;
    private readonly Panel _root;
    private readonly List<RowVisual> _visuals = [];
    private readonly List<string> _selection = [];
    private IReadOnlyList<ModuleValueListRow>? _syncedRows;
    private IModuleViewContext? _context;
    private string _filterText = string.Empty;
    private string _lastSelectionJson = string.Empty;
    private bool _selectionRestored;
    private bool _previewBuilt;
    private bool _rebuiltThisFrame;
    private int _visibleCount;

    /// <summary>造一个值列表容器。</summary>
    public ModuleValueList()
    {
        HorizontalContentAlignment = HorizontalAlignment.Stretch;
        VerticalContentAlignment = VerticalAlignment.Stretch;
        MinWidth = 0;

        _rowsPanel.HorizontalAlignment = HorizontalAlignment.Stretch;
        _scroll = new ScrollViewer
        {
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalContentAlignment = HorizontalAlignment.Stretch,
            VerticalContentAlignment = VerticalAlignment.Top,
            Content = _rowsPanel,
        };

        _placeholder = new ModuleLabel
        {
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            TextWrapping = TextWrapping.Wrap,
            IsVisible = false,
            IsHitTestVisible = false,
        };
        _placeholder.Classes.Add("hint");

        _root = new Panel();
        _root.Children.Add(_scroll);
        _root.Children.Add(_placeholder);
        Content = _root;

        if (ModuleView.IsDesignEnvironment)
        {
            BuildPreviewRows();
        }
    }

    /// <summary>行表（由视图代码后置在 <c>order</c> 变化时整体替换）。</summary>
    /// <remarks>只在引用变化时重建行；值编辑不会重建，避免打断正在编辑的控件。</remarks>
    public IReadOnlyList<ModuleValueListRow> Rows { get; set; } = [];

    /// <summary>勾选顺序写入的值名（JSON 整数数组；空 = 只保存在控件内）。</summary>
    public string SelectionValue { get; set; } = string.Empty;

    /// <summary>过滤词所在的值名（空 = 不过滤）。</summary>
    public string FilterValue { get; set; } = string.Empty;

    /// <summary>行内删除按钮的图标键；空 = 用文字「×」。</summary>
    public string DeleteIcon
    {
        get => _deleteIcon;
        set
        {
            _deleteIcon = value ?? string.Empty;

            foreach (var visual in _visuals)
            {
                ApplyDeleteIcon(visual.Delete);
            }
        }
    }

    private string _deleteIcon = string.Empty;

    /// <summary>行内发送按钮文案。</summary>
    public string SendText
    {
        get => _sendText;
        set
        {
            _sendText = value ?? string.Empty;

            foreach (var visual in _visuals)
            {
                visual.Send.Content = _sendText;
            }
        }
    }

    private string _sendText = "Send";

    /// <summary>内容编辑框最大高度（DIP）。</summary>
    public double ContentMaxHeight { get; set; } = 96;

    /// <summary>设计期占位行数（运行期忽略）。</summary>
    public int PreviewRowCount { get; set; } = 2;

    /// <summary>行内按钮动作（发送 / 删除）。</summary>
    public event EventHandler<ModuleValueListRowAction>? RowAction;

    /// <inheritdoc />
    public override void Refresh(IModuleViewContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        _context = context;

        if (ModuleView.IsDesignEnvironment && Rows.Count == 0)
        {
            BuildPreviewRows();
            return;
        }

        if (!ReferenceEquals(_syncedRows, Rows))
        {
            RebuildRows();
            _rebuiltThisFrame = true;
        }

        if (!_selectionRestored && _visuals.Count > 0)
        {
            RestoreSelection();
        }

        SyncSelectionFromValue();
        SanitizeSelection();

        foreach (var visual in _visuals)
        {
            UpdateRow(context, visual);
        }

        if (_rebuiltThisFrame)
        {
            _rebuiltThisFrame = false;

            foreach (var visual in _visuals)
            {
                RefreshEditors(visual);
            }
        }

        ApplyFilter(context);
        UpdatePlaceholder();
    }

    /// <summary>把焦点放到某一行的标签编辑器（新建行后使用）。</summary>
    /// <param name="id">行标识。</param>
    public void FocusRow(string id)
    {
        var visual = _visuals.Find(item => string.Equals(item.Row.Id, id, StringComparison.Ordinal));
        var editor = visual?.Label.GetVisualDescendants().OfType<TextBox>().FirstOrDefault();
        _ = editor?.Focus();
    }

    // ---------------------------------------------------------------- 行构建

    private void RebuildRows()
    {
        _rowsPanel.Children.Clear();
        _visuals.Clear();
        _previewBuilt = false;
        _syncedRows = Rows;

        foreach (var row in _syncedRows)
        {
            var visual = CreateRow(row, preview: false);
            _visuals.Add(visual);
            _rowsPanel.Children.Add(visual.Root);
        }

        UpdateBadges();
        _visibleCount = _visuals.Count;
    }

    private RowVisual CreateRow(ModuleValueListRow row, bool preview)
    {
        var root = new Border
        {
            BorderThickness = new Thickness(0, 0, 0, 1),
            Padding = new Thickness(4, 2, 4, 2),
            Background = Brushes.Transparent,
        };
        SetTokenBrush(root, Border.BorderBrushProperty, "BorderDefault");

        var layout = new DockPanel();

        // 选择标记：未选=空框；选中=强调底 + 白色 1 基序号。
        var badge = new Border
        {
            Width = 16,
            Height = 16,
            CornerRadius = new CornerRadius(3),
            BorderThickness = new Thickness(1),
            Focusable = true,
            Cursor = new Cursor(StandardCursorType.Hand),
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Top,
            Margin = new Thickness(2, 2, 6, 0),
        };
        SetTokenBrush(badge, Border.BackgroundProperty, "SurfaceControlBrush");
        SetTokenBrush(badge, Border.BorderBrushProperty, "BorderStrong");
        var badgeText = new ModuleLabel
        {
            FontSize = 10,
            FontWeight = FontWeight.Bold,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            IsHitTestVisible = false,
        };
        SetTokenBrush(badgeText, TextBlock.ForegroundProperty, "TextPrimary");
        badge.Child = badgeText;
        ToolTip.SetTip(badge, TextCatalog.Get(typeof(ModuleValueList).Assembly, SelectKey));
        badge.PointerPressed += (_, e) =>
        {
            if (!e.GetCurrentPoint(badge).Properties.IsLeftButtonPressed)
            {
                return;
            }

            ToggleSelection(row.Id);
            e.Handled = true;
            badge.Focus();
        };
        badge.KeyDown += (_, e) =>
        {
            if (e.Key is Key.Space or Key.Enter)
            {
                ToggleSelection(row.Id);
                e.Handled = true;
            }
        };
        DockPanel.SetDock(badge, Dock.Left);
        layout.Children.Add(badge);

        var main = new DockPanel
        {
            LastChildFill = true,
            Margin = new Thickness(0, 0, 4, 0),
        };
        var label = new ModuleTextSurface
        {
            Value = preview ? string.Empty : row.LabelValue,
            Mode = TextSurfaceMode.Input,
            TextFormat = ValueTextFormat.Text,
            Multiline = false,
            CommitMode = ValueCommitMode.OnChange,
            Height = 22,
            Margin = new Thickness(0, 0, 0, 2),
            ShowInputHint = true,
            Text = preview ? TextCatalog.Get(typeof(ModuleValueList).Assembly, PreviewLabelKey) : string.Empty,
        };
        var content = new ModuleTextSurface
        {
            Value = preview ? string.Empty : row.ContentValue,
            Mode = TextSurfaceMode.Input,
            TextFormat = ValueTextFormat.Text,
            Multiline = true,
            CommitMode = ValueCommitMode.OnChange,
            EscapeInput = true,
            MinEditorHeight = 22,
            MaxHeight = ContentMaxHeight,
            ShowInputHint = true,
            Text = preview ? TextCatalog.Get(typeof(ModuleValueList).Assembly, PreviewContentKey) : string.Empty,
        };
        DockPanel.SetDock(label, Dock.Top);
        main.Children.Add(label);
        main.Children.Add(content);
        var side = new StackPanel
        {
            Spacing = 1,
            Width = 62,
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Top,
        };

        var firstLine = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 1,
            VerticalAlignment = VerticalAlignment.Center,
        };
        var hex = new ModuleCheckBox
        {
            Value = preview ? string.Empty : row.HexValue,
            Content = "HEX",
            CommitMode = ValueCommitMode.OnChange,
            IsChecked = preview,
            Width = 38,
            Height = 22,
            MinHeight = 0,
        };
        var delete = new ModuleButton
        {
            Content = "×",
            Appearance = ModuleButtonAppearance.Danger,
            CommandName = string.Empty,
            Width = 18,
            Height = 18,
            MinWidth = 0,
            MinHeight = 0,
            FontSize = 12,
            Padding = new Thickness(0),
            HorizontalContentAlignment = HorizontalAlignment.Center,
            VerticalContentAlignment = VerticalAlignment.Center,
        };
        delete.Classes.Add("icon");
        ApplyDeleteIcon(delete);
        ToolTip.SetTip(delete, TextCatalog.Get(typeof(ModuleValueList).Assembly, DeleteKey));
        delete.Click += (_, _) => RaiseAction(row.Id, "delete");
        firstLine.Children.Add(hex);
        firstLine.Children.Add(delete);

        var secondLine = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 1,
            VerticalAlignment = VerticalAlignment.Center,
        };
        var cr = new ModuleCheckBox
        {
            Value = preview ? string.Empty : row.CrValue,
            Content = "\\r",
            CommitMode = ValueCommitMode.OnChange,
            IsChecked = preview,
            Width = 28,
            Height = 22,
            MinHeight = 0,
        };
        var lf = new ModuleCheckBox
        {
            Value = preview ? string.Empty : row.LfValue,
            Content = "\\n",
            CommitMode = ValueCommitMode.OnChange,
            IsChecked = preview,
            Width = 28,
            Height = 22,
            MinHeight = 0,
        };
        secondLine.Children.Add(cr);
        secondLine.Children.Add(lf);

        var send = new ModuleButton
        {
            Content = SendText,
            Appearance = ModuleButtonAppearance.Normal,
            CommandName = string.Empty,
            Width = 56,
            Height = 20,
            MinWidth = 0,
            MinHeight = 0,
            Padding = new Thickness(0),
            HorizontalContentAlignment = HorizontalAlignment.Center,
            VerticalContentAlignment = VerticalAlignment.Center,
        };
        send.Click += (_, _) => RaiseAction(row.Id, "send");

        side.Children.Add(firstLine);
        side.Children.Add(secondLine);
        side.Children.Add(send);
        DockPanel.SetDock(side, Dock.Right);
        layout.Children.Add(side);
        layout.Children.Add(main);
        root.Child = layout;

        var visual = new RowVisual(row, root, badge, badgeText, label, content, hex, cr, lf, send, delete);
        label.Loaded += (_, _) => RefreshEditors(visual);
        content.Loaded += (_, _) => RefreshEditors(visual);
        hex.Loaded += (_, _) => RefreshEditors(visual);
        cr.Loaded += (_, _) => RefreshEditors(visual);
        lf.Loaded += (_, _) => RefreshEditors(visual);
        return visual;
    }

    private void ApplyDeleteIcon(ModuleButton button)
    {
        if (_deleteIcon.Length > 0)
        {
            button.Icon = _deleteIcon;
            return;
        }

        button.Icon = string.Empty;
        button.Content = "×";
    }

    private void RefreshEditors(RowVisual visual)
    {
        if (_context is not { } context)
        {
            return;
        }

        visual.Label.Refresh(context);
        visual.Content.Refresh(context);
        visual.Hex.Refresh(context);
        visual.Cr.Refresh(context);
        visual.Lf.Refresh(context);
    }

    private static void UpdateRow(IModuleViewContext context, RowVisual visual)
    {
        var hex = ReadBool(context, visual.Row.HexValue, fallback: false);

        if (hex != visual.LastHex)
        {
            visual.LastHex = hex;
            visual.Content.TextFormat = hex ? ValueTextFormat.Hex : ValueTextFormat.Text;
            visual.Content.EscapeInput = !hex;
        }

        var newline = hex ? string.Empty : visual.Row.NewlineValue;
        if (!string.Equals(visual.LastNewline, newline, StringComparison.Ordinal))
        {
            visual.LastNewline = newline;
            visual.Content.EnterNewlineValue = newline;
        }

        var contentState = context.ReadValue(visual.Row.ContentValue);
        visual.Send.IsEnabled = !contentState.IsKnown || !contentState.IsInvalid;
    }

    // ---------------------------------------------------------------- 选择

    private void RestoreSelection()
    {
        _selectionRestored = true;

        if (SelectionValue.Length == 0 || _context is null)
        {
            return;
        }

        var state = _context.ReadValue(SelectionValue);
        if (!state.IsKnown)
        {
            return;
        }

        _lastSelectionJson = state.Value;
        ApplySelectionJson(state.Value);
    }

    private void SyncSelectionFromValue()
    {
        if (SelectionValue.Length == 0 || _context is null || !_selectionRestored)
        {
            return;
        }

        var state = _context.ReadValue(SelectionValue);
        if (!state.IsKnown || string.Equals(state.Value, _lastSelectionJson, StringComparison.Ordinal))
        {
            return;
        }

        _lastSelectionJson = state.Value;
        ApplySelectionJson(state.Value);
    }

    private void ApplySelectionJson(string? json)
    {
        var slots = ParseSlots(json);
        _selection.Clear();

        foreach (var slot in slots)
        {
            var id = slot.ToString(CultureInfo.InvariantCulture);
            if (_visuals.Exists(visual => string.Equals(visual.Row.Id, id, StringComparison.Ordinal)))
            {
                _selection.Add(id);
            }
        }

        UpdateBadges();
    }

    private void SanitizeSelection()
    {
        var removed = _selection.RemoveAll(id =>
            !_visuals.Exists(visual => string.Equals(visual.Row.Id, id, StringComparison.Ordinal)));

        if (removed > 0)
        {
            UpdateBadges();
            WriteSelection();
        }
    }

    private void ToggleSelection(string id)
    {
        var index = _selection.IndexOf(id);

        if (index >= 0)
        {
            _selection.RemoveAt(index);
        }
        else
        {
            _selection.Add(id);
        }

        UpdateBadges();
        WriteSelection();
    }

    private void WriteSelection()
    {
        if (SelectionValue.Length == 0 || _context is null)
        {
            return;
        }

        var json = SerializeSlots(_selection);
        _lastSelectionJson = json;
        _context.CommitValue(SelectionValue, json);
    }

    private void UpdateBadges()
    {
        foreach (var visual in _visuals)
        {
            var index = _selection.IndexOf(visual.Row.Id);

            if (index >= 0)
            {
                SetTokenBrush(visual.Badge, Border.BackgroundProperty, "AccentPrimary");
                SetTokenBrush(visual.Badge, Border.BorderBrushProperty, "AccentPrimary");
                SetTokenBrush(visual.BadgeText, TextBlock.ForegroundProperty, "TextOnAccent");
                visual.BadgeText.Text = (index + 1).ToString(CultureInfo.InvariantCulture);
            }
            else
            {
                SetTokenBrush(visual.Badge, Border.BackgroundProperty, "SurfaceControlBrush");
                SetTokenBrush(visual.Badge, Border.BorderBrushProperty, "BorderStrong");
                SetTokenBrush(visual.BadgeText, TextBlock.ForegroundProperty, "TextPrimary");
                visual.BadgeText.Text = string.Empty;
            }
        }
    }

    // ---------------------------------------------------------------- 过滤

    private void ApplyFilter(IModuleViewContext context)
    {
        var filter = FilterValue.Length == 0
            ? string.Empty
            : context.ReadValue(FilterValue).Value ?? string.Empty;

        if (filter.Length == 0 && _filterText.Length == 0)
        {
            return;
        }

        _filterText = filter;
        _visibleCount = 0;

        foreach (var visual in _visuals)
        {
            var visible = filter.Length == 0
                || Contains(context, visual.Row.LabelValue, filter)
                || Contains(context, visual.Row.ContentValue, filter);

            visual.Root.IsVisible = visible;

            if (visible)
            {
                _visibleCount++;
            }
        }
    }

    private static bool Contains(IModuleViewContext context, string valueName, string filter)
    {
        if (valueName.Length == 0)
        {
            return false;
        }

        var text = context.ReadValue(valueName).Value;
        return !string.IsNullOrEmpty(text)
            && text.Contains(filter, StringComparison.OrdinalIgnoreCase);
    }

    private void UpdatePlaceholder()
    {
        if (ModuleView.IsDesignEnvironment && _previewBuilt)
        {
            _placeholder.IsVisible = false;
            return;
        }

        if (_visuals.Count == 0)
        {
            _placeholder.Text = TextCatalog.Get(typeof(ModuleValueList).Assembly, EmptyKey);
            _placeholder.IsVisible = true;
            return;
        }

        if (_visibleCount == 0)
        {
            _placeholder.Text = TextCatalog.Get(typeof(ModuleValueList).Assembly, NoMatchKey);
            _placeholder.IsVisible = true;
            return;
        }

        _placeholder.IsVisible = false;
    }

    // ---------------------------------------------------------------- 动作

    private void RaiseAction(string id, string action) =>
        RowAction?.Invoke(this, new ModuleValueListRowAction(id, action));

    // ---------------------------------------------------------------- 设计期

    private void BuildPreviewRows()
    {
        if (_previewBuilt || Rows.Count > 0)
        {
            return;
        }

        _previewBuilt = true;
        _selectionRestored = true;
        _rowsPanel.Children.Clear();
        _visuals.Clear();

        for (var index = 0; index < Math.Clamp(PreviewRowCount, 1, 8); index++)
        {
            var row = new ModuleValueListRow(
                index.ToString(CultureInfo.InvariantCulture),
                string.Empty,
                string.Empty,
                string.Empty,
                string.Empty,
                string.Empty,
                string.Empty);
            var visual = CreateRow(row, preview: true);
            visual.BadgeText.Text = (index + 1).ToString(CultureInfo.InvariantCulture);
            SetTokenBrush(visual.Badge, Border.BackgroundProperty, "AccentPrimary");
            SetTokenBrush(visual.Badge, Border.BorderBrushProperty, "AccentPrimary");
            SetTokenBrush(visual.BadgeText, TextBlock.ForegroundProperty, "TextOnAccent");
            _visuals.Add(visual);
            _rowsPanel.Children.Add(visual.Root);
        }

        _visibleCount = _visuals.Count;
        _placeholder.IsVisible = false;
    }

    // ---------------------------------------------------------------- 工具

    private static bool ReadBool(IModuleViewContext context, string name, bool fallback)
    {
        if (name.Length == 0)
        {
            return fallback;
        }

        var state = context.ReadValue(name);
        return state.IsKnown && bool.TryParse(state.Value, out var value) ? value : fallback;
    }

    private static int[] ParseSlots(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return [];
        }

        try
        {
            return JsonSerializer.Deserialize<int[]>(json) ?? [];
        }
        catch (JsonException)
        {
            return [];
        }
    }

    private static string SerializeSlots(List<string> ids)
    {
        var slots = new List<int>(ids.Count);

        foreach (var id in ids)
        {
            if (int.TryParse(id, NumberStyles.Integer, CultureInfo.InvariantCulture, out var slot))
            {
                slots.Add(slot);
            }
        }

        return JsonSerializer.Serialize(slots);
    }

    private sealed class RowVisual(
        ModuleValueListRow row,
        Border root,
        Border badge,
        ModuleLabel badgeText,
        ModuleTextSurface label,
        ModuleTextSurface content,
        ModuleCheckBox hex,
        ModuleCheckBox cr,
        ModuleCheckBox lf,
        ModuleButton send,
        ModuleButton delete)
    {
        public ModuleValueListRow Row { get; } = row;
        public Border Root { get; } = root;
        public Border Badge { get; } = badge;
        public ModuleLabel BadgeText { get; } = badgeText;
        public ModuleTextSurface Label { get; } = label;
        public ModuleTextSurface Content { get; } = content;
        public ModuleCheckBox Hex { get; } = hex;
        public ModuleCheckBox Cr { get; } = cr;
        public ModuleCheckBox Lf { get; } = lf;
        public ModuleButton Send { get; } = send;
        public ModuleButton Delete { get; } = delete;
        public bool LastHex { get; set; }
        public string? LastNewline { get; set; }
    }
}
