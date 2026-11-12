using HYWDebugAssistant.Sdk.Text;

namespace HYWDebugAssistant.Template.MyModule;

/// <summary>模板用到的文本键；文案在 <c>resources/i18n/&lt;程序集简名&gt;/zh-Hans.json</c>。</summary>
internal static class TemplateText
{
    public const string Title = "module.title";
    public const string Description = "module.description";
    public const string RoleName = "role.forward";
    public const string ViewTitle = "view.title";
    public const string Interval = "view.interval";
    public const string Count = "view.count";
    public const string State = "view.state";
    public const string Reset = "view.reset";
    public const string Ready = "state.ready";
    public const string Received = "state.received";
    public const string Tick = "state.tick";
    public const string ResetDone = "state.reset";

    public static string Get(string key) => TextCatalog.Get(typeof(TemplateText).Assembly, key);
}
