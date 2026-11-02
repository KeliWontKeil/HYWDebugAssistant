namespace HYWDebugAssistant.Tests;

/// <summary>
/// 模块来源发现测试用的**入口类型集合**（标识必须与 <c>module.json</c> 完全一致）。
/// </summary>
/// <remarks>
/// 每个类型对应一个"模块包身份"：同名的不同版本需要不同标识 → 用不同类型区分。
/// 这些类型不参与任何真实运行，只用于"能被加载器加载"。
/// 本程序集只引 Sdk（模块包实现件的真实形态），不得引 Access。
/// </remarks>
public static class FixtureModuleType
{
    /// <summary><c>hyw/alpha</c>（多根场景与重复包场景共用）。</summary>
    public static readonly Type Alpha = typeof(AlphaModule);

    /// <summary><c>hyw/beta</c>。</summary>
    public static readonly Type Beta = typeof(BetaModule);

    /// <summary><c>hyw/multi</c>。</summary>
    public static readonly Type MultiV1 = typeof(MultiModuleV1);

    /// <summary><c>hyw/multi</c>。</summary>
    public static readonly Type MultiV2 = typeof(MultiModuleV2);

    /// <summary><c>hyw/first</c>。</summary>
    public static readonly Type First = typeof(FirstModule);

    /// <summary><c>hyw/second</c>。</summary>
    public static readonly Type Second = typeof(SecondModule);

    /// <summary><c>hyw/from-settings</c>。</summary>
    public static readonly Type FromSettings = typeof(FromSettingsModule);

    /// <summary><c>hyw/alpha</c> 入口。</summary>
    [HYWDebugAssistant.Sdk.ModuleDefinition("hyw", "alpha", DisplayName = "夹具 alpha")]
    public sealed class AlphaModule : HYWDebugAssistant.Sdk.Lifecycle.IModule;

    /// <summary><c>hyw/beta</c> 入口。</summary>
    [HYWDebugAssistant.Sdk.ModuleDefinition("hyw", "beta", DisplayName = "夹具 beta")]
    public sealed class BetaModule : HYWDebugAssistant.Sdk.Lifecycle.IModule;

    /// <summary><c>hyw/multi</c> 入口。</summary>
    [HYWDebugAssistant.Sdk.ModuleDefinition("hyw", "multi", DisplayName = "夹具 multi v1")]
    public sealed class MultiModuleV1 : HYWDebugAssistant.Sdk.Lifecycle.IModule;

    /// <summary><c>hyw/multi</c> 入口。</summary>
    [HYWDebugAssistant.Sdk.ModuleDefinition("hyw", "multi", DisplayName = "夹具 multi（重复 ID）")]
    public sealed class MultiModuleV2 : HYWDebugAssistant.Sdk.Lifecycle.IModule;

    /// <summary><c>hyw/first</c> 入口。</summary>
    [HYWDebugAssistant.Sdk.ModuleDefinition("hyw", "first", DisplayName = "夹具 first")]
    public sealed class FirstModule : HYWDebugAssistant.Sdk.Lifecycle.IModule;

    /// <summary><c>hyw/second</c> 入口。</summary>
    [HYWDebugAssistant.Sdk.ModuleDefinition("hyw", "second", DisplayName = "夹具 second")]
    public sealed class SecondModule : HYWDebugAssistant.Sdk.Lifecycle.IModule;

    /// <summary><c>hyw/from-settings</c> 入口。</summary>
    [HYWDebugAssistant.Sdk.ModuleDefinition("hyw", "from-settings", DisplayName = "夹具 from-settings")]
    public sealed class FromSettingsModule : HYWDebugAssistant.Sdk.Lifecycle.IModule;
}
