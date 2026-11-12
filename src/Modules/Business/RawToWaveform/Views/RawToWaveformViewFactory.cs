using Avalonia.Controls;
using HYWDebugAssistant.Sdk.Ui;

namespace HYWDebugAssistant.Modules.RawToWaveform.Views;

/// <summary>视图工厂：框架按每个实例调用一次，必须无状态。</summary>
public sealed class RawToWaveformViewFactory : IModuleViewFactory
{
    /// <inheritdoc />
    public Control Create(IModuleViewContext context) => new RawToWaveformView();
}
