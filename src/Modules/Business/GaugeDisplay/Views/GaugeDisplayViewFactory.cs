using Avalonia.Controls;
using HYWDebugAssistant.Sdk.Ui;

namespace HYWDebugAssistant.Modules.GaugeDisplay.Views;

/// <summary>仪表盘视图工厂（无状态；每个实例造一次视图）。</summary>
public sealed class GaugeDisplayViewFactory : IModuleViewFactory
{
    /// <inheritdoc />
    public Control Create(IModuleViewContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        return new GaugeDisplayView();
    }
}
