using System.Globalization;
using System.Text.Json;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using HYWDebugAssistant.Sdk.Text;
using HYWDebugAssistant.Sdk.Ui;
using HYWDebugAssistant.Sdk.Ui.Controls;

namespace HYWDebugAssistant.Modules.AttitudeDisplay.Views;

/// <summary>三轴姿态视图：组/通道摘要 + 三维姿态 + 独立设置面板。</summary>
[ModuleViewDesignData(typeof(AttitudeDesignData))]
public sealed partial class AttitudeDisplayView : ModuleView, IModuleViewElement
{
    private readonly AttitudeView? _view;
    private readonly ModuleLabel? _summary;
    private readonly ModulePopupPanel? _panel;

    private IModuleViewContext? _context;
    private long _metaRevision = -1;
    private long _statusRevision = -1;
    private string _summaryText = string.Empty;

    /// <summary>构造视图。</summary>
    public AttitudeDisplayView()
    {
        AvaloniaXamlLoader.Load(this);

        _view = this.FindControl<AttitudeView>("View");
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
        TextCatalog.Get(typeof(AttitudeDisplayView).Assembly, key);

    private static string Tr(string key, params object?[] args) =>
        TextCatalog.Get(typeof(AttitudeDisplayView).Assembly, key, args);

    /// <inheritdoc />
    public void Refresh(IModuleViewContext context)
    {
        _context = context;

        ReadMeta(context);

        if (_view is not null)
        {
            if (context.ReadLatest(AttitudeDisplayViewKeys.Roll) is { } roll)
            {
                _view.Roll = roll;
            }

            if (context.ReadLatest(AttitudeDisplayViewKeys.Pitch) is { } pitch)
            {
                _view.Pitch = pitch;
            }

            if (context.ReadLatest(AttitudeDisplayViewKeys.Yaw) is { } yaw)
            {
                _view.Yaw = yaw;
            }
        }

        UpdateStatusTip(context);
    }

    private void ReadMeta(IModuleViewContext context)
    {
        var snapshot = context.ReadText(AttitudeDisplayViewKeys.Meta);
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
                ReadInt(settings, "roll", 0),
                ReadInt(settings, "pitch", 1),
                ReadInt(settings, "yaw", 2));
        }
        catch (JsonException)
        {
            // 坏快照不打断刷新：保留上一次可用的设置。
        }
    }

    private void ApplySettings(JsonElement settings)
    {
        if (_view is null)
        {
            return;
        }

        _view.RenderStyle = ParseStyle(ReadString(settings, "style", "cube"), _view.RenderStyle);
        _view.Zoom = ReadDouble(settings, "zoom", _view.Zoom);
        _view.ShowGrid = ReadBool(settings, "grid", _view.ShowGrid);
        _view.ShowLabels = ReadBool(settings, "labels", _view.ShowLabels);
        _view.SmoothingEnabled = ReadBool(settings, "smooth", _view.SmoothingEnabled);
        _view.SmoothingTimeMs = ReadDouble(settings, "smoothMs", _view.SmoothingTimeMs);
    }

    /// <summary>组/通道摘要：值没变不重写标签。</summary>
    private void UpdateSummary(int group, int roll, int pitch, int yaw)
    {
        if (_summary is null)
        {
            return;
        }

        var groupText = group > 0
            ? group.ToString(CultureInfo.InvariantCulture)
            : Tr(AttitudeDisplayText.SummaryAuto);
        var text = Tr(AttitudeDisplayText.Summary, groupText, roll, pitch, yaw);
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
        var snapshot = context.ReadText(AttitudeDisplayViewKeys.Status);
        if (snapshot.Revision == _statusRevision || snapshot.Lines.Count == 0)
        {
            return;
        }

        _statusRevision = snapshot.Revision;
        ToolTip.SetTip(this, string.Concat(snapshot.Lines));
    }

    private static AttitudeRenderStyle ParseStyle(string text, AttitudeRenderStyle fallback)
    {
        if (string.Equals(text, "axes", StringComparison.OrdinalIgnoreCase))
        {
            return AttitudeRenderStyle.Axes;
        }

        if (string.Equals(text, "cube", StringComparison.OrdinalIgnoreCase))
        {
            return AttitudeRenderStyle.Cube;
        }

        if (string.Equals(text, "drone", StringComparison.OrdinalIgnoreCase))
        {
            return AttitudeRenderStyle.Drone;
        }

        return fallback;
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
