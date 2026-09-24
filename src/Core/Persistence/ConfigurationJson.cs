using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace HYWDebugAssistant.Core.Persistence;
/// <summary>
/// 模块配置对象与 JSON 之间的转换参数（**反射式**：配置类型由模块定义、框架编译期不可知）。
/// </summary>
/// <remarks>
/// <list type="bullet">
///   <item>组合件**信封**不依赖反射（手工 JsonNode 映射）；只有模块自有的配置对象走反射式序列化
///     （v1 明确取舍：SDK 未要求模块提供源生成上下文）。保存、加载与配置表单三条路径**共用同一参数**，
///     避免"写进去和读出来不一致"；</item>
///   <item><see cref="JsonNumberHandling.AllowNamedFloatingPointLiterals"/>：模块配置里合法的
///     <c>±Infinity</c>/<c>NaN</c>（如"值域上下限未设"用 ±∞ 表示）必须能往返，
///     否则保存组合件会直接失败——它们是**模块自有的语义**，框架不做裁剪；</item>
///   <item><see cref="JsonStringEnumConverter"/>：枚举按**名字**读写（组合件是给人看的文本；
///     表单写回的也是名字）；转换器默认允许整数，故 M1 早期写下的数字枚举仍能加载。</item>
/// </list>
/// </remarks>
internal static class ConfigurationJson
{
    /// <summary>共用序列化参数。</summary>
    internal static readonly JsonSerializerOptions Options = new()
    {
        PropertyNameCaseInsensitive = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
        NumberHandling = JsonNumberHandling.AllowNamedFloatingPointLiterals,
        Converters = { new JsonStringEnumConverter() },
    };
}
