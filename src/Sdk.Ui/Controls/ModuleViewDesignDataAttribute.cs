using Avalonia.Controls;
using HYWDebugAssistant.Sdk.Configuration;

namespace HYWDebugAssistant.Sdk.Ui.Controls;

/// <summary>
/// **模块视图的设计时配置类型（或标准标识）**：预览器没有运行时上下文，用它决定从哪个配置类型反射字段。
/// </summary>
/// <remarks>
/// 只影响设计环境（<see cref="Design.IsDesignMode"/> 或 <see cref="ModuleView.IsPreviewHost"/>）；
/// 运行时 <see cref="ModuleViewHost"/> 会覆盖 <c>DataContext</c>，真实上下文优先。
/// <para>
/// 两种写法：模块自带配置类型时给 <see cref="ConfigType"/>；配置类型在框架侧时只给
/// <see cref="StandardId"/>，由预览宿主把标准标识映射到配置类型（映射注册属于框架接线）。
/// </para>
/// </remarks>
[AttributeUsage(AttributeTargets.Class, Inherited = false, AllowMultiple = false)]
public sealed class ModuleViewDesignDataAttribute : Attribute
{
    /// <summary>按设计时配置类型指定（模块自带配置）。</summary>
    /// <param name="configType">带 <see cref="ConfigurationFieldAttribute"/> 的配置类型。</param>
    public ModuleViewDesignDataAttribute(Type configType)
    {
        ArgumentNullException.ThrowIfNull(configType);
        ConfigType = configType;
    }

    /// <summary>按外部标准标识指定（配置类型在框架侧）。</summary>
    /// <param name="standardId">外部标准标识（见 <see cref="HYWDebugAssistant.Sdk.Access.ExternalAccessStandardIds"/>）。</param>
    public ModuleViewDesignDataAttribute(string standardId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(standardId);
        StandardId = standardId;
    }

    /// <summary>设计时配置类型（按标准标识指定时为 <see langword="null"/>）。</summary>
    public Type? ConfigType { get; }

    /// <summary>外部标准标识（按配置类型指定时为 <see langword="null"/>）。</summary>
    public string? StandardId { get; }
}
