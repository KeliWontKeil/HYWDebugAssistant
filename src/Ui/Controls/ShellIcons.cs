namespace HYWDebugAssistant.Ui.Controls;

/// <summary>
/// **外壳与模块清单的图标键**：界面只经这些常量取图标，图形在 `resources/icons/HYWDebugAssistant.Ui/`
/// 的 `icons.json` 指向的 `.svg` 里（显式文件，不是字体码位）。
/// </summary>
/// <remarks>
/// <para>
/// 键名在这里，图形在数据里：换一个图标只改 `.svg` 文件，不改界面代码。缺键时
/// <c>IconCatalog</c> 返回键名本身，界面上直接显示缺的是哪个键。
/// </para>
/// <para>
/// **分类图标与外壳图标复用同一个键**：同一枚图标在清单里只登记一次，
/// 否则"同一个图标两处各存一份"迟早会漂移。
/// </para>
/// </remarks>
public static class ShellIcons
{
    /// <summary>模块清单（外壳活动栏）。</summary>
    public const string Modules = "icon.shell.modules";

    /// <summary>组合。</summary>
    public const string Composite = "icon.shell.composite";

    /// <summary>交互。</summary>
    public const string Interaction = "icon.shell.interaction";

    /// <summary>拓扑诊断。</summary>
    public const string Topology = "icon.shell.topology";

    /// <summary>Issue。</summary>
    public const string Issue = "icon.shell.issue";

    /// <summary>设置。</summary>
    public const string Settings = "icon.shell.settings";

    /// <summary>帮助与关于。</summary>
    public const string Help = "icon.shell.help";

    /// <summary>运行。</summary>
    public const string Run = "icon.shell.run";

    /// <summary>停止。</summary>
    public const string Stop = "icon.shell.stop";

    /// <summary>锁定。</summary>
    public const string Lock = "icon.shell.lock";

    /// <summary>解锁。</summary>
    public const string Unlock = "icon.shell.unlock";

    /// <summary>隐藏。</summary>
    public const string Hide = "icon.shell.hide";

    /// <summary>关闭。</summary>
    public const string Close = "icon.shell.close";

    /// <summary>还原。</summary>
    public const string Reset = "icon.shell.reset";

    /// <summary>图片。</summary>
    public const string Image = "icon.shell.image";

    /// <summary>模块清单分类：校验类。</summary>
    public const string CatalogVerify = "icon.catalog.verify";

    /// <summary>模块清单分类：其他。</summary>
    public const string CatalogOther = "icon.catalog.other";

    /// <summary>模块清单分类：全部（与 <see cref="Modules"/> 同一个键）。</summary>
    public const string CatalogAll = Modules;

    /// <summary>模块清单分类：源（与 <see cref="Run"/> 同一个键）。</summary>
    public const string CatalogSource = Run;

    /// <summary>模块清单分类：宿（与 <see cref="Hide"/> 同一个键）。</summary>
    public const string CatalogSink = Hide;
}

