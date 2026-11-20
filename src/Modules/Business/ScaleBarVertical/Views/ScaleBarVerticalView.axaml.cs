using System.Globalization;
using System.Text.Json;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using Avalonia.Media;
using HYWDebugAssistant.Sdk.Text;
using HYWDebugAssistant.Sdk.Ui;
using HYWDebugAssistant.Sdk.Ui.Controls;

namespace HYWDebugAssistant.Modules.ScaleBarVertical.Views;

/// <summary>纵向刻度条视图：组/通道摘要 + 矩形刻度条 + 独立设置面板。</summary>
[ModuleViewDesignData(typeof(ScaleBarDesignData))]
public sealed partial class ScaleBarVerticalView : ModuleView, IModuleViewElement
{
    private readonly ScaleBar? _bar;
    private readonly ModuleLabel? _summary;
    private readonly ModuleLabel? _title;
    private readonly ModuleLabel? _unit;
    private readonly ModuleNumeric? _value;
    private readonly ModulePopupPanel? _panel;

    private IModuleViewContext? _context;
    private long _metaRevision = -1;
    private long _statusRevision = -1;
    private string _summaryText = string.Empty;

    /// <summary>构造视图。</summary>
    public ScaleBarVerticalView()
    {
        AvaloniaXamlLoader.Load(this);

        _bar = this.FindControl<ScaleBar>("ScaleBar");
        _summary = this.FindControl<ModuleLabel>("Summary");
        _title = this.FindControl<ModuleLabel>("Title");
        _unit = this.FindControl<ModuleLabel>("Unit");
        _value = this.FindControl<ModuleNumeric>("Value");
        _panel = this.FindControl<ModulePopupPanel>("SettingsPanel");

        if (this.FindControl<ModuleButton>("SettingsButton") is { } settingsButton)
        {
            settingsButton.Click += OnOpenSettings;
            if (_panel is not null)
            {
                _panel.Anchor = settingsButton;
            }
        }
    }

    private static string Tr(string key) =>
        TextCatalog.Get(typeof(ScaleBarVerticalView).Assembly, key);

    private static string Tr(string key, params object?[] args) =>
        TextCatalog.Get(typeof(ScaleBarVerticalView).Assembly, key, args);

    /// <inheritdoc />
    public void Refresh(IModuleViewContext context)
    {
        _context = context;

        ReadMeta(context);

        if (_bar is not null && context.ReadLatest(ScaleBarVerticalViewKeys.Value) is { } target)
        {
            _bar.Value = target;
        }

        UpdateStatusTip(context);
    }

    private void ReadMeta(IModuleViewContext context)
    {
        var snapshot = context.ReadText(ScaleBarVerticalViewKeys.Meta);
        if (snapshot.Revision == _metaRevision || snapshot.Lines.Count == 0)
        {
            return;
        }

        _metaRevision = snapshot.Revision;

        try
        {
            using var document = JsonDocument.Parse(string.Concat(snapshot.Lines));
            if (!document.RootElement.TryGetProperty("settings", out var settings)
                || settings.ValueKind != JsonValueKind.Object)
            {
                return;
            }

            ApplySettings(settings);
            UpdateSummary(
                ReadInt(settings, "group", 0),
                ReadInt(settings, "channel", 0));
        }
        catch (JsonException)
        {
            // 坏快照不打断刷新：保留上一次可用的设置。
        }
    }

    private void ApplySettings(JsonElement settings)
    {
        if (_bar is null)
        {
            return;
        }

        _bar.Minimum = ReadDouble(settings, "min", _bar.Minimum);
        _bar.Maximum = ReadDouble(settings, "max", _bar.Maximum);
        _bar.SmoothingEnabled = ReadBool(settings, "smooth", _bar.SmoothingEnabled);
        _bar.SmoothingTimeMs = ReadDouble(settings, "smoothMs", _bar.SmoothingTimeMs);

        if (_title is not null)
        {
            _title.Text = ReadString(settings, "title", string.Empty);
        }

        if (_unit is not null)
        {
            _unit.Text = ReadString(settings, "unit", string.Empty);
        }
        _bar.FillOrigin = ReadBool(settings, "fillFromZero", _bar.FillOrigin == ScaleFillOrigin.Zero)
            ? ScaleFillOrigin.Zero
            : ScaleFillOrigin.Minimum;
        ApplyFill(ReadString(settings, "color", string.Empty));
    }

    /// <summary>颜色覆盖：有 ARGB 就写本地画刷，空/非法则交还主题默认。</summary>
    private void ApplyFill(string text)
    {
        if (_bar is null)
        {
            return;
        }

        if (ColorText.TryParseArgb(text, out var argb))
        {
            _bar.FillBrush = new SolidColorBrush(Color.FromUInt32(argb));
            return;
        }

        _bar.ClearValue(ScaleBar.FillBrushProperty);
    }

    /// <summary>组/通道摘要：值没变不重写标签。</summary>
    private void UpdateSummary(int group, int channel)
    {
        if (_summary is null)
        {
            return;
        }

        var groupText = group > 0
            ? group.ToString(CultureInfo.InvariantCulture)
            : Tr(ScaleBarVerticalText.SummaryAuto);
        var text = Tr(ScaleBarVerticalText.Summary, groupText, channel);
        if (string.Equals(text, _summaryText, StringComparison.Ordinal))
        {
            return;
        }

        _summaryText = text;
        _summary.Text = text;
    }

    /// <summary>状态句不占版面：整块视图的悬停提示（文本源变了才更新）。</summary>
    private void UpdateStatusTip(IModuleViewContext context)
    {
        var snapshot = context.ReadText(ScaleBarVerticalViewKeys.Status);
        if (snapshot.Revision == _statusRevision || snapshot.Lines.Count == 0)
        {
            return;
        }

        _statusRevision = snapshot.Revision;
        ToolTip.SetTip(this, string.Concat(snapshot.Lines));
    }

    private static double ReadDouble(JsonElement element, string name, double fallback) =>
        element.TryGetProperty(name, out var value) && value.TryGetDouble(out var parsed) ? parsed : fallback;

    private static int ReadInt(JsonElement element, string name, int fallback) =>
        element.TryGetProperty(name, out var value) && value.TryGetInt32(out var parsed) ? parsed : fallback;

    private static bool ReadBool(JsonElement element, string name, bool fallback) =>
        element.TryGetProperty(name, out var value)
        && value.ValueKind is JsonValueKind.True or JsonValueKind.False
            ? value.GetBoolean()
            : fallback;

    private static string ReadString(JsonElement element, string name, string fallback) =>
        element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString() ?? fallback
            : fallback;

    private void OnOpenSettings(object? sender, RoutedEventArgs e)
    {
        _ = sender;
        _ = e;
        _panel?.Toggle();
    }
}
