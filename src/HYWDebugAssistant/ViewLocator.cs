using System;
using System.Diagnostics.CodeAnalysis;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using HYWDebugAssistant.ViewModels;

namespace HYWDebugAssistant;

/// <summary>
/// 视图定位器：根据 ViewModel 返回对应的 View（可以返回 null）。
/// </summary>
[RequiresUnreferencedCode(
    "默认 ViewLocator 实现依赖反射，裁剪（trimming）时可能被移除。",
    Url = "https://docs.avaloniaui.net/docs/concepts/view-locator")]
public class ViewLocator : IDataTemplate
{
    public Control? Build(object? param)
    {
        if (param is null)
        {
            return null;
        }

        var name = param.GetType().FullName!.Replace("ViewModel", "View", StringComparison.Ordinal);
        var type = Type.GetType(name);

        if (type != null)
        {
            return (Control)Activator.CreateInstance(type)!;
        }

        return new TextBlock { Text = "Not Found: " + name };
    }

    public bool Match(object? data)
    {
        return data is ViewModelBase;
    }
}
