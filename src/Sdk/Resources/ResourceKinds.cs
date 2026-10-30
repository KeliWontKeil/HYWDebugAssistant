namespace HYWDebugAssistant.Sdk.Resources;

/// <summary>
/// **资源类别的名字**：每个类别都是 `resources/&lt;kind&gt;/&lt;拥有者&gt;/…` 下的一棵树。
/// </summary>
/// <remarks>
/// 类别名同时是目录名，出现在三层：仓库布局、程序输出投递、模块包。改名等于搬家，
/// 审计按这里的字面量核对另外两处（`StoragePaths` 与模块 csproj 的 Content 项）。
/// </remarks>
public static class ResourceKinds
{
    /// <summary>界面文本表：`<拥有者>/<culture>[.<area>].json`。</summary>
    public const string Text = "i18n";

    /// <summary>界面图标：`<拥有者>/icons.json`（键 → 相对路径的 `.svg`）。</summary>
    public const string Icons = "icons";
}
