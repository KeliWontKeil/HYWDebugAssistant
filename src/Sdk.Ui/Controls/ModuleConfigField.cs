using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.VisualTree;
using HYWDebugAssistant.Sdk.Text;

namespace HYWDebugAssistant.Sdk.Ui.Controls;

/// <summary>
/// **配置字段编辑器**：绑一个配置字段名，按字段种类自己选编辑器，改一项写回一项。
/// </summary>
/// <remarks>
/// <para>
/// <b>只做编辑器，不带标题</b>：标题是视图里一个独立的 <see cref="ModuleLabel"/>（`Field` 取同一份配置元数据），
/// 摆在编辑器旁边。标题若长在控件里，它的文本宽度就会替视图决定编辑器还能剩多宽 ——
/// 那正是"窄字段的输入框被标题挤没"的来历。
/// </para>
/// <para>
/// <b>可选项来自配置类型</b>（<c>[ConfigurationField]</c> / <c>IConfigurationChoices</c>），视图只说"编哪一个字段"。
/// </para>
/// <para>
/// <b>改一项写一项</b>：没有"保存"按钮。文本 / 数字在**失焦或回车**时写回
/// （逐键写回会让 <c>1</c>→<c>11</c>→<c>115</c> 每个中间态各触发一次配置变更，而配置变更会重开会话）；
/// 布尔与枚举是"点一下就有明确结果"的交互，当场写回。
/// </para>
/// <para>
/// <b>编辑中的文本不被每帧刷新覆盖</b>：字段值来自事实源，但用户正在打的字是本地意图；
/// 两者混在一帧里更新会把"打到一半的 11"改回旧值。脏标记 + 编辑中跳过是本控件唯一的保护点。
/// </para>
/// <para>
/// <b>失败不回滚</b>：用户刚打的字留在框里，配一句框架给的具名原因 ——
/// 悄悄改回去会让人以为"自己没打进去"。
/// </para>
/// <para>
/// 视觉结构在 `ControlTheme` 的 `PART_EditorHost` / `PART_Notice` 里；
/// 这里只选编辑器种类、管写回时机与运行期外观。
/// </para>
/// </remarks>
public sealed class ModuleConfigField : ModuleElement
{
    private TextBlock? _notice;
    private ContentControl? _editorHost;
    private Border? _errorRing;

    private TemplatedControl? _editor;
    private ConfigFieldKind? _kind;
    private ConfigFieldView? _field;
    private IModuleViewContext? _context;
    private IReadOnlyList<ConfigChoice>? _choices;
    private string? _editorValue;
    private bool _dirty;
    private bool _syncing;

    /// <summary>造一个配置字段编辑器（视觉部件在模板应用时取得）。</summary>
    public ModuleConfigField()
    {
        // 宿主运行时走模块视图祖先；预览器没有宿主，上下文直接挂在视图 DataContext 上。
        DataContextChanged += (_, _) =>
        {
            if (DataContext is IModuleViewContext context)
            {
                _context = context;
                Load();
            }
        };

        // 挂树时读一次字段（此刻实例与配置都已就绪）。
        AttachedToVisualTree += (_, _) =>
        {
            ResolveContext();
            Load();
        };
    }

    /// <summary>本格编辑的配置字段名。</summary>
    public string Field
    {
        get => Source;
        set
        {
            if (string.Equals(Source, value, StringComparison.Ordinal))
            {
                return;
            }

            Source = value;
            _editorValue = null;
            _dirty = false;
            Load();
        }
    }

    /// <summary>就地那一句事实（失败原因 / "没有这个字段"；空 = 没有话说）。</summary>
    public string Notice => _notice?.Text ?? string.Empty;

    /// <inheritdoc />
    protected override void OnApplyTemplate(TemplateAppliedEventArgs e)
    {
        base.OnApplyTemplate(e);

        _notice = e.NameScope.Find<TextBlock>("PART_Notice");
        _editorHost = e.NameScope.Find<ContentControl>("PART_EditorHost");
        _errorRing = e.NameScope.Find<Border>("PART_ErrorRing");

        if (_editorHost is not null)
        {
            _editorHost.Content = _editor;
        }

        Load();
    }

    /// <inheritdoc />
    public override void Refresh(IModuleViewContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        _context = context;
        Load();
    }

    /// <summary>找上下文：运行时的宿主祖先优先；预览器/测试可由 DataContext 直接提供。</summary>
    private void ResolveContext()
    {
        if (_context is not null)
        {
            return;
        }

        if (this.FindAncestorOfType<ModuleViewHost>() is { } host)
        {
            _context = host.Context;
            return;
        }

        if (DataContext is IModuleViewContext context)
        {
            _context = context;
        }
    }

    /// <summary>读一次字段并按种类建（或复用）编辑器。</summary>
    private void Load()
    {
        if (_context is not { } context)
        {
            return;
        }

        if (Field.Length == 0)
        {
            Reject(TextCatalog.Get(typeof(ModuleConfigField).Assembly, "moduleField.reject.missingFieldName"));
            return;
        }

        var field = context.ReadField(Field);

        if (field is null)
        {
            Reject(TextCatalog.Get(typeof(ModuleConfigField).Assembly, "moduleField.reject.unknownField", Field));
            return;
        }

        _field = field;

        // 标题是另一个控件；编辑器自己仍要对屏读器说得出"这是哪一项"。
        AutomationProperties.SetName(this, field.Title);

        if (_editor is null || _kind != field.Kind)
        {
            BuildEditor(field);
            ShowValue(field.Value);
            return;
        }

        // 用户正在编辑（或刚写回失败）⇒ 本地文本优先，等失焦/回车再决定。
        if (_dirty)
        {
            return;
        }

        // 值没变、但可选项变了（插拔设备）也要刷新下拉；用户正在编辑时仍让路。
        var choicesChanged = field.Choices is { } choices && !SameChoices(_choices, choices);

        if (!string.Equals(field.Value, _editorValue, StringComparison.Ordinal) || choicesChanged)
        {
            ShowValue(field.Value);
        }
    }

    /// <summary>这一格画不出来：撤掉编辑器，只留一句事实。</summary>
    /// <param name="reason">要显示的那句话。</param>
    private void Reject(string reason)
    {
        if (_editorHost is not null)
        {
            _editorHost.Content = null;
        }

        _editor = null;
        _kind = null;
        _field = null;
        _dirty = false;
        _editorValue = null;

        SetNotice(reason);
    }

    /// <summary>按字段种类造编辑器（种类变了才重建 —— 重建会把正在编辑的那一格换成新对象）。</summary>
    /// <param name="field">字段。</param>
    private void BuildEditor(ConfigFieldView field)
    {
        _editor = field.Kind switch
        {
            // **布尔一律用模块自己的滑块开关**：同一个语义只能对应一套外观；
            // 配置表单与视图都走 `ModuleToggle`，视觉在 `ControlTheme` 里。
            ConfigFieldKind.Boolean => new ModuleToggle { Text = string.Empty, VerticalAlignment = VerticalAlignment.Center },
            ConfigFieldKind.Enum => new ComboBox
            {
                VerticalAlignment = VerticalAlignment.Center,
                // 模块配置格很窄：箭头用紧凑变体，仍复用同一个 ComboBox 模板。
                Classes = { "compact" },
            },
            _ => new TextBox { VerticalContentAlignment = VerticalAlignment.Center },
        };

        // 勾选框不拉伸：拉伸会把方框与文字拉成一条横线（视觉上像"没有控件"）。
        _editor.HorizontalAlignment = field.Kind == ConfigFieldKind.Boolean
            ? HorizontalAlignment.Left
            : HorizontalAlignment.Stretch;
        _editor.VerticalAlignment = VerticalAlignment.Center;

        if (_editorHost is not null)
        {
            _editorHost.Content = _editor;
        }

        // 编辑器三档各自的写回时机与外观
        switch (_editor)
        {
            case ModuleToggle toggle:
                toggle.CheckedChanged += (_, _) =>
                {
                    if (_syncing || _field is null)
                    {
                        return;
                    }

                    var value = toggle.IsChecked ? "true" : "false";

                    if (!string.Equals(value, _editorValue, StringComparison.Ordinal))
                    {
                        _dirty = true;
                        Commit(value);
                    }
                };
                break;

            case ComboBox combo:
                combo.SelectionChanged += (_, _) =>
                {
                    if (_syncing || _field?.Choices is not { } choices)
                    {
                        return;
                    }

                    if (combo.SelectedIndex < 0 || combo.SelectedIndex >= choices.Count)
                    {
                        return;
                    }

                    var value = choices[combo.SelectedIndex].Value;

                    if (!string.Equals(value, _editorValue, StringComparison.Ordinal))
                    {
                        _dirty = true;
                        Commit(value);
                    }
                };
                break;

            case TextBox text:
                text.TextChanged += (_, _) =>
                {
                    if (!_syncing)
                    {
                        _dirty = true;
                    }
                };
                text.LostFocus += (_, _) => CommitText();
                text.KeyDown += (_, args) =>
                {
                    if (args.Key == Key.Enter)
                    {
                        args.Handled = true;
                        CommitText();
                    }
                };

                break;

            default:
                break;
        }

        _kind = field.Kind;
        _choices = null;
        _editorValue = null;
        _dirty = false;
    }

    /// <summary>把一份值画到当前编辑器上（<b>不写回</b>）。</summary>
    /// <param name="value">事实源给的文本形态。</param>
    private void ShowValue(string value)
    {
        if (_editor is null)
        {
            return;
        }

        _syncing = true;

        try
        {
            switch (_editor)
            {
                case ModuleToggle toggle:
                    var wanted = string.Equals(value, "true", StringComparison.OrdinalIgnoreCase);

                    if (toggle.IsChecked != wanted)
                    {
                        toggle.IsChecked = wanted;
                    }

                    break;

                case ComboBox combo when _field?.Choices is { } choices:
                    if (!SameChoices(_choices, choices))
                    {
                        combo.Items.Clear();

                        foreach (var choice in choices)
                        {
                            combo.Items.Add(choice.Title);
                        }

                        _choices = choices;
                    }

                    combo.SelectedIndex = IndexOf(choices, value);
                    combo.PlaceholderText = combo.SelectedIndex < 0 && value.Length > 0
                        ? TextCatalog.Get(typeof(ModuleConfigField).Assembly, "moduleField.currentChoiceUnavailable")
                        : null;
                    break;

                case TextBox text:
                    if (!string.Equals(text.Text, value, StringComparison.Ordinal))
                    {
                        text.Text = value;
                    }

                    break;

                default:
                    break;
            }
        }
        finally
        {
            _syncing = false;
        }

        _editorValue = value;
        _dirty = false;
    }

    /// <summary>文本框里改了字就交出去（失焦 / 回车）。</summary>
    private void CommitText()
    {
        if (_editor is not TextBox text)
        {
            return;
        }

        var value = text.Text ?? string.Empty;

        if (string.Equals(value, _editorValue, StringComparison.Ordinal))
        {
            _dirty = false;
            return;
        }

        Commit(value);
    }

    /// <summary>写回一项并就地显示结论。</summary>
    /// <param name="value">新值的文本形态。</param>
    private void Commit(string value)
    {
        if (_context is not { } context)
        {
            return;
        }

        var outcome = context.UpdateField(Field, value);

        if (outcome.IsError)
        {
            _dirty = true;
            SetNotice(outcome.Message);
            return;
        }

        // 成功 ⇒ 事实源就是新值；把"上次交出去的字"跟着前进一格。
        _editorValue = value;
        _dirty = false;
        SetNotice(null);
    }

    /// <summary>就地显示一句事实（空 ⇒ 收起来，不占位）。</summary>
    /// <param name="message">要显示的那句话。</param>
    private void SetNotice(string? message)
    {
        if (_notice is null)
        {
            return;
        }

        _notice.Text = message ?? string.Empty;
        _notice.IsVisible = !string.IsNullOrWhiteSpace(message);
    }

    /// <summary>运行期外观落到编辑器上（标题/提示不跟着变色）。</summary>
    /// <param name="appearance">外观。</param>
    protected internal override void ApplyAppearance(ViewAppearance appearance)
    {
        ArgumentNullException.ThrowIfNull(appearance);

        if (_editor is null)
        {
            return;
        }

        if (appearance.Text is { } text)
        {
            _editor.Foreground = new SolidColorBrush(Color.FromUInt32(text));
        }
        else
        {
            SetTokenBrush(_editor, TemplatedControl.ForegroundProperty, appearance.TextToken);
        }

        if (appearance.TextSize is { } size)
        {
            _editor.FontSize = size;
        }
        else
        {
            _editor.ClearValue(TemplatedControl.FontSizeProperty);
        }

        ApplyErrorRing(appearance);
        ApplyReadOnly(appearance.ReadOnly is true);
        ApplyEnabled(appearance.Enabled);
    }

    /// <summary>错误圈画在编辑器之上：焦点/选择边框不会把它盖掉。</summary>
    private void ApplyErrorRing(ViewAppearance appearance)
    {
        if (_errorRing is null)
        {
            return;
        }

        if (appearance.Outline is { } outline)
        {
            _errorRing.BorderBrush = new SolidColorBrush(Color.FromUInt32(outline));
            _errorRing.IsVisible = true;
        }
        else if (appearance.OutlineToken is { Length: > 0 } token)
        {
            SetTokenBrush(_errorRing, Border.BorderBrushProperty, token);
            _errorRing.IsVisible = true;
        }
        else
        {
            _errorRing.ClearValue(Border.BorderBrushProperty);
            _errorRing.IsVisible = false;
        }
    }

    /// <summary>运行期只读：文本框只改编辑状态（仍可聚焦选中复制），其余编辑器禁用。</summary>
    /// <param name="isReadOnly">是否只读。</param>
    protected override void ApplyReadOnly(bool isReadOnly)
    {
        switch (_editor)
        {
            case TextBox text:
                text.IsReadOnly = isReadOnly;
                text.IsTabStop = !isReadOnly;
                text.Focusable = true;

                if (isReadOnly)
                {
                    text.CaretBrush = Brushes.Transparent;
                }
                else
                {
                    text.ClearValue(TextBox.CaretBrushProperty);
                }

                break;

            case ModuleToggle toggle:
                toggle.IsEnabled = !isReadOnly;
                break;

            case Control control:
                control.IsEnabled = !isReadOnly;
                break;

            default:
                break;
        }
    }
    /// <summary>两份选项表是否逐项相同（避免每帧清空重建下拉项）。</summary>
    private static bool SameChoices(IReadOnlyList<ConfigChoice>? left, IReadOnlyList<ConfigChoice> right)
    {
        if (left is null || left.Count != right.Count)
        {
            return false;
        }

        for (var i = 0; i < right.Count; i++)
        {
            if (!string.Equals(left[i].Value, right[i].Value, StringComparison.Ordinal)
                || !string.Equals(left[i].Title, right[i].Title, StringComparison.Ordinal))
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>某个值在选项表里排第几（不在表里 ⇒ -1）。</summary>
    private static int IndexOf(IReadOnlyList<ConfigChoice> choices, string value)
    {
        for (var i = 0; i < choices.Count; i++)
        {
            if (string.Equals(choices[i].Value, value, StringComparison.Ordinal))
            {
                return i;
            }
        }

        return -1;
    }
}



