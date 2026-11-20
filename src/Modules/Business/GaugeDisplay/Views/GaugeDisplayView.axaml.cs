using System.Globalization;
using System.Text.Json;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using HYWDebugAssistant.Sdk.Text;
using HYWDebugAssistant.Sdk.Ui;
using HYWDebugAssistant.Sdk.Ui.Controls;

namespace HYWDebugAssistant.Modules.GaugeDisplay.Views;

/// <summary>指针仪表盘视图：组/通道摘要 + 表盘 + 独立设置面板。</summary>
[ModuleViewDesignData(typeof(GaugeDesignData))]
public sealed partial class GaugeDisplayView : ModuleView, IModuleViewElement
{
    private readonly GaugeDial? _gauge;
    private readonly ModuleLabel? _summary;
    private readonly ModulePopupPanel? _panel;

    private IModuleViewContext? _context;
    private long _metaRevision = -1;
    private long _statusRevision = -1;
    private string _summaryText = string.Empty;

    /// <summary>构造视图。</summary>
    public GaugeDisplayView()
    {
        AvaloniaXamlLoader.Load(this);

        _gauge = this.FindControl<GaugeDial>("Gauge");
        _summary = this.FindControl<ModuleLabel>("Summary");
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
        TextCatalog.Get(typeof(GaugeDisplayView).Assembly, key);

    private static string Tr(string key, params object?[] args) =>
        TextCatalog.Get(typeof(GaugeDisplayView).Assembly, key, args);

    /// <inheritdoc />
    public void Refresh(IModuleViewContext context)
    {
        _context = context;

        ReadMeta(context);

        if (_gauge is not null && context.ReadLatest(GaugeDisplayViewKeys.Value) is { } target)
        {
            _gauge.Value = target;
        }

        UpdateStatusTip(context);
    }

    private void ReadMeta(IModuleViewContext context)
    {
        var snapshot = context.ReadText(GaugeDisplayViewKeys.Meta);
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

            var group = ReadInt(settings, "group", 0);
            var channel = ReadInt(settings, "channel", 0);
            UpdateSummary(group, channel);
        }
        catch (JsonException)
        {
            // 坏快照不打断刷新：保留上一次可用的设置。
        }
    }

    private void ApplySettings(JsonElement settings)
    {
        if (_gauge is null)
        {
            return;
        }

        _gauge.Minimum = ReadDouble(settings, "min", _gauge.Minimum);
        _gauge.Maximum = ReadDouble(settings, "max", _gauge.Maximum);
        _gauge.FillOrigin = ReadBool(settings, "fillFromZero", _gauge.FillOrigin == ScaleFillOrigin.Zero)
            ? ScaleFillOrigin.Zero
            : ScaleFillOrigin.Minimum;
        _gauge.Title = ReadString(settings, "title", _gauge.Title);
        _gauge.Unit = ReadString(settings, "unit", _gauge.Unit);
        _gauge.SmoothingEnabled = ReadBool(settings, "smooth", _gauge.SmoothingEnabled);
        _gauge.SmoothingTimeMs = ReadDouble(settings, "smoothMs", _gauge.SmoothingTimeMs);
        _gauge.ValueDecimals = ReadInt(settings, "decimals", _gauge.ValueDecimals);
    }

    /// <summary>组/通道摘要：值没变不重写标签。</summary>
    private void UpdateSummary(int group, int channel)
    {
        if (_summary is null)
        {
            return;
        }

        var groupText = group > 0 ? group.ToString(CultureInfo.InvariantCulture) : Tr(GaugeDisplayText.SummaryAuto);
        var text = Tr(GaugeDisplayText.Summary, groupText, channel);
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
        var snapshot = context.ReadText(GaugeDisplayViewKeys.Status);
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
