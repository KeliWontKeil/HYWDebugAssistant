using Avalonia.Controls;
using HYWDebugAssistant.Sdk.Ui;

namespace HYWDebugAssistant.Modules.AttitudeDisplay.Views;

/// <summary>三轴姿态视图工厂（无状态；每个实例造一次视图）。</summary>
public sealed class AttitudeDisplayViewFactory : IModuleViewFactory
{
    /// <inheritdoc />
    public Control Create(IModuleViewContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        return new AttitudeDisplayView();
    }
}
