using Avalonia.Controls;
using HYWDebugAssistant.Ui.ModuleSurface.Hosting;
using HYWDebugAssistant.Ui.ViewModels;

namespace HYWDebugAssistant.Ui.Views;

/// <summary>
/// **模块窗口的内容区** —— 把「声明面 → 控件」的那块表面挂进窗口。
/// </summary>
/// <remarks>
/// <para>
/// 它自己<b>不含任何控件知识</b>：内容全部由 <see cref="ModuleSurfaceHost"/> 产出（一整块
/// <see cref="ModuleSurfacePanel"/>）。这个文件存在的唯一理由是"视图模型不直接造控件树"，
/// 而窗口的 XAML 需要一个可以 <c>DataContext="{Binding}"</c> 的元素。
/// </para>
/// <para>
/// 表面**在视图模型第一次被渲染时构造**（见 <see cref="Apply"/>），而不是构造视图模型时构造：
/// 声明树 → 排布树 → 控件树是一条不便宜的链，隐藏的窗口不该为它付钱。
/// </para>
/// </remarks>
public sealed class ModuleSurfaceView : UserControl
{
    private ModuleViewViewModel? _module;

    /// <summary>构造内容区。</summary>
    public ModuleSurfaceView()
    {
        HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Stretch;
        VerticalAlignment = Avalonia.Layout.VerticalAlignment.Stretch;
        DataContextChanged += (_, _) => Attach(DataContext as ModuleViewViewModel);
    }

    /// <summary>当前渲染的表面（没有声明视图 ⇒ <see langword="null"/>）。</summary>
    public ModuleSurfaceHost? Surface => _module?.Surface;

    private void Attach(ModuleViewViewModel? module)
    {
        if (_module is { } previous)
        {
            previous.PropertyChanged -= OnModulePropertyChanged;
        }

        _module = module;

        if (module is not null)
        {
            module.PropertyChanged += OnModulePropertyChanged;
        }

        Apply();
    }

    private void OnModulePropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        // 只有"换了一份声明"才需要换内容；每帧的数据更新走 RefreshSurface()，不经过属性通知。
        if (string.Equals(e.PropertyName, nameof(ModuleViewViewModel.Surface), System.StringComparison.Ordinal))
        {
            Apply();
        }
    }

    private void Apply()
    {
        Content = _module?.EnsureSurface()?.View;
    }
}
