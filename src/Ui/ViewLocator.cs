using System.Collections.Concurrent;
using System.Diagnostics.CodeAnalysis;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using HYWDebugAssistant.Ui.ViewModels;

namespace HYWDebugAssistant.Ui;

/// <summary>
/// 视图定位器：按命名约定为视图模型找到对应视图（找不到时**不匹配**，由调用方给出显式模板）。
/// </summary>
/// <remarks>
/// 本项目里几乎所有视图都是**显式设置**的（主窗口、模块卡片、元素模板），本定位器只是兜底：
/// 因此 <see cref="Match"/> 仅在"约定名对应的视图类型确实存在"时才为真——
/// 否则会拦下元素视图模型的显式模板，显示成 "Not Found"。
/// </remarks>
[RequiresUnreferencedCode(
    "ViewLocator 依赖反射按命名约定查找视图类型；裁剪（trimming）时可能被移除。",
    Url = "https://docs.avaloniaui.net/docs/concepts/view-locator")]
public class ViewLocator : IDataTemplate
{
    private static readonly ConcurrentDictionary<Type, Type?> Cache = new();

    /// <inheritdoc />
    public Control? Build(object? param)
    {
        if (param is null)
        {
            return null;
        }

        return Resolve(param.GetType()) is { } type
            ? (Control)Activator.CreateInstance(type)!
            : new TextBlock { Text = "未找到视图：" + param.GetType().FullName };
    }

    /// <inheritdoc />
    public bool Match(object? data) =>
        data is ViewModelBase viewModel && Resolve(viewModel.GetType()) is not null;

    private static Type? Resolve(Type viewModelType) =>
        Cache.GetOrAdd(
            viewModelType,
            static type =>
            {
                var name = type.FullName!.Replace("ViewModel", "View", StringComparison.Ordinal);
                var resolved = Type.GetType(name);
                return resolved is not null && typeof(Control).IsAssignableFrom(resolved) ? resolved : null;
            });
}
