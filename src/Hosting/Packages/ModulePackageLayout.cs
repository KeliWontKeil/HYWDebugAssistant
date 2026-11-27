namespace HYWDebugAssistant.Hosting.Packages;

/// <summary>
/// **模块包目录的布局常量**：包内约定的文件名，只在这里写一次。
/// </summary>
/// <remarks>
/// <list type="bullet">
///   <item><b>为什么不散在各处</b>：这些名字同时被"加载器读"与"打包规则写"，
///     两处各写一份字面量就会漂移（改了打包侧而忘了读取侧 ⇒ 文件在包里、界面就是不显示）。</item>
///   <item><b>与打包侧的关系</b>：MSBuild 里无法引用 C# 常量，`build/module-package.targets`
///     用的是 `$(ModuleCoverFileName)`（缺省值在 `build/module-package.props`）；
///     两处必须保持一致，任一侧改名都要同步另一侧（`tools/verify-module-packages.ps1` 会核对）。</item>
/// </list>
/// </remarks>
public static class ModulePackageLayout
{
    /// <summary>
    /// 模块**封面图**的文件名（住在包根，与 <c>module.json</c> 同级）。
    /// </summary>
    /// <remarks>
    /// 封面是**可选**的展示资源：缺它只是清单里显示占位字形，不影响加载；
    /// 它在包根而不是 <c>resources/</c> 下，因此**不进** `module.json.resources`
    /// （那些目录是框架按拥有者读取的资源类别，封面不由资源层解释）。
    /// </remarks>
    public const string CoverFileName = "preview.png";
}
