using Avalonia.Controls;
using HYWDebugAssistant.Sdk.Configuration;

namespace HYWDebugAssistant.Sdk.Ui.Controls;

/// <summary>
/// **模块视图的设计时配置类型**：预览器没有运行时上下文，用它决定从哪个配置类型反射字段。
/// </summary>
/// <remarks>
/// 只影响设计环境（<see cref="Design.IsDesignMode"/> 或 <see cref="ModuleView.IsPreviewHost"/>）；
/// 运行时 <see cref="ModuleViewHost"/> 会覆盖 <see cref="ModuleView.DataContext"/>，真实上下文优先。
/// </remarks>
[AttributeUsage(AttributeTargets.Class, Inherited = false, AllowMultiple = false)]
public sealed class ModuleViewDesignDataAttribute : Attribute
{
    /// <summary>设计时配置类型。</summary>
    /// <param name="configType">带 <see cref="ConfigurationFieldAttribute"/> 的配置类型。</param>
    public ModuleViewDesignDataAttribute(Type configType) => ConfigType = configType;

    /// <summary>设计时配置类型。</summary>
    public Type ConfigType { get; }
}
