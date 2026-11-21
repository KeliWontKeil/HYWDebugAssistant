using System.Globalization;
using System.Text.Json;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;
using HYWDebugAssistant.Sdk.Ui;
using HYWDebugAssistant.Sdk.Ui.Controls;

namespace HYWDebugAssistant.Modules.CommandSender.Views;

/// <summary>指令发送器视图（布局在 <c>CommandSenderView.axaml</c>）。</summary>
public sealed partial class CommandSenderView : ModuleView, IModuleViewElement
{
    private readonly ModuleValueList? _list;
    private IModuleViewContext? _context;
    private string? _lastOrder;
    private int _lastCount;
    private bool _loaded;

    /// <summary>造一个视图。</summary>
    public CommandSenderView()
    {
        AvaloniaXamlLoader.Load(this);
        _list = this.FindControl<ModuleValueList>("List");

        if (_list is not null)
        {
            _list.RowAction += OnRowAction;
        }
    }

    /// <inheritdoc />
    public void Refresh(IModuleViewContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        _context = context;

        if (_list is null)
        {
            return;
        }

        var orderText = context.ReadValue(CommandSenderViewKeys.Order).Value ?? string.Empty;

        if (string.Equals(orderText, _lastOrder, StringComparison.Ordinal))
        {
            return;
        }

        _lastOrder = orderText;
        var slots = ParseSlots(orderText);
        var rows = new List<ModuleValueListRow>(slots.Count);

        foreach (var slot in slots)
        {
            rows.Add(new ModuleValueListRow(
                slot.ToString(CultureInfo.InvariantCulture),
                CommandSenderModule.SlotValue(slot, SlotField.Label),
                CommandSenderModule.SlotValue(slot, SlotField.Content),
                CommandSenderModule.SlotValue(slot, SlotField.Hex),
                CommandSenderModule.SlotValue(slot, SlotField.Cr),
                CommandSenderModule.SlotValue(slot, SlotField.Lf),
                CommandSenderModule.SlotValue(slot, SlotField.Newline)));
        }

        _list.Rows = rows;

        if (slots.Count > _lastCount && slots.Count > 0 && _loaded)
        {
            var id = slots[^1].ToString(CultureInfo.InvariantCulture);
            Dispatcher.UIThread.Post(() => _list.FocusRow(id));
        }

        _lastCount = slots.Count;
        _loaded = true;
    }

    private void OnRowAction(object? sender, ModuleValueListRowAction action)
    {
        var context = _context;

        if (context is null
            || !int.TryParse(action.RowId, NumberStyles.Integer, CultureInfo.InvariantCulture, out var slot))
        {
            return;
        }

        var json = JsonSerializer.Serialize(new { op = action.Action, slot });
        _ = context.SubmitInput(CommandSenderModule.ActionSource, json);
    }

    private static List<int> ParseSlots(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return [];
        }

        try
        {
            var values = JsonSerializer.Deserialize<int[]>(text);
            return values is null ? [] : [.. values.Where(value => value >= 0)];
        }
        catch (JsonException)
        {
            return [];
        }
    }
}

/// <summary>指令发送器视图工厂。</summary>
public sealed class CommandSenderViewFactory : IModuleViewFactory
{
    /// <inheritdoc />
    public Control Create(IModuleViewContext context) => new CommandSenderView();
}
