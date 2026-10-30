namespace HYWDebugAssistant.Sdk.Access;

/// <summary>
/// **适配件可调参数的统一下发口**（可选实现）：框架把应用级通信设置交给支持它的适配件。
/// </summary>
/// <remarks>
/// <para>
/// 只有需要"关闭等待 / 接收暂存容量 / 设备丢失判定"的适配件才实现它；不实现的适配件保持自己的默认值。
/// 参数在会话创建时下发一次，运行期设置变更时再下发一次（见 <see cref="ExternalAccessTuning"/> 的生效说明）。
/// </para>
/// <para>
/// 实现必须**不抛**：一个适配件拒绝某项参数不应让会话创建失败。
/// </para>
/// </remarks>
public interface IExternalAccessTuning
{
    /// <summary>应用一组通信参数（实现方可忽略不支持的项）。</summary>
    /// <param name="tuning">参数（不为 <see langword="null"/>）。</param>
    void ApplyTuning(ExternalAccessTuning tuning);
}

/// <summary>
/// **通信参数**：关闭等待、接收暂存容量、设备丢失判定次数、探活节拍。
/// </summary>
/// <remarks>
/// <list type="bullet">
///   <item><b>探活节拍</b>由会话消费（决定多久调一次适配件探活），适配件不感知；</item>
///   <item><b>暂存容量</b>在会话创建（适配器尚未打开）时生效；既有会话保持自己的容量；</item>
///   <item><b>关闭等待</b>与<b>丢失判定次数</b>可立即生效（下一次关闭 / 下一次探活读到新值）。</item>
/// </list>
/// </remarks>
/// <param name="BufferBytes">适配件接收暂存的字节容量（大于 0）。</param>
/// <param name="CloseTimeout">关闭旧传输时的等待上限。</param>
/// <param name="DeviceLostMisses">连续探活缺失多少次判设备断开（≥ 1）。</param>
/// <param name="LivenessInterval">探活节拍（会话使用）。</param>
public sealed record ExternalAccessTuning(
    int BufferBytes,
    TimeSpan CloseTimeout,
    int DeviceLostMisses,
    TimeSpan LivenessInterval)
{
    /// <summary>缺省参数：64 KB 暂存、1 s 关闭等待、2 次丢失判定、250 ms 探活。</summary>
    public static ExternalAccessTuning Default { get; } =
        new(64 * 1024, TimeSpan.FromSeconds(1), 2, TimeSpan.FromMilliseconds(250));
}
