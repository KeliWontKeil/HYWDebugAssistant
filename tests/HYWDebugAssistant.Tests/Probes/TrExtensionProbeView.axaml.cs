using Avalonia.Controls;

namespace HYWDebugAssistant.Tests.Probes;

/// <summary>只给 `{loc:Tr}` 探针用的最小视图（两条文本，键都在本程序集的表里）。</summary>
public partial class TrExtensionProbeView : UserControl
{
    /// <summary>创建探针视图。</summary>
    public TrExtensionProbeView() => InitializeComponent();
}
