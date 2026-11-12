using HYWDebugAssistant.Sdk.Configuration;

namespace HYWDebugAssistant.Template.MyModule;

/// <summary>模板配置：一个用户可编辑的实例参数。</summary>
/// <remarks>
/// 配置类型必须是公开、非抽象类并有公共无参构造；字段标题与范围只影响表单呈现。
/// 模块读取一次即登记默认值；配置随组合件保存。
/// </remarks>
public sealed class TemplateConfig
{
    /// <summary>定时器间隔（毫秒）。</summary>
    [ConfigurationField("间隔(ms)", Minimum = 10, Maximum = 60_000, Unit = "ms")]
    public int IntervalMs { get; set; } = 500;
}
