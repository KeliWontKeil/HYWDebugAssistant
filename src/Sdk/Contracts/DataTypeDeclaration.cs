namespace HYWDebugAssistant.Sdk.Contracts;

/// <summary>
/// **样本解码器**：把一段定长样本字节解成可显示的数值（呈现期解码）。
/// </summary>
/// <remarks>
/// <list type="bullet">
///   <item><b>由模块提供、随类型注册</b>：框架**只回调**，不内建、不理解结构（"呈现期解码由模块负责"）；</item>
///   <item><b>失败即失败</b>：返回 <see langword="false"/> 表示"我解不了这个样本"，框架**不猜**、不按长度/原语推断；</item>
///   <item>实现必须是**纯函数**（同字节 → 同结果、不抛异常、不分配）；样本生命周期仅限本次调用。</item>
/// </list>
/// </remarks>
/// <param name="sample">样本字节（长度 = 该类型的尺寸）。</param>
/// <param name="value">解出的数值（可显示量；非数值类型可返回任意有意义的标量）。</param>
/// <returns>是否解码成功。</returns>
public delegate bool SampleDecoder(ReadOnlySpan<byte> sample, out double value);

/// <summary>
/// **计量块**（随类型声明，全字段可选）：框架据此**机械**给出四档速率，零解析。
/// </summary>
/// <remarks>
/// <list type="bullet">
///   <item><b>四档速率</b>：单元速率（按源计）· 搬运字节速率（按宿计，含自描述与对齐填充）·
///         有效载荷字节速率（业务数据）· 样本 / 通道样本速率；</item>
///   <item><b>全可选且有明确缺省</b>：<see cref="PayloadSizeInBytes"/> 缺省 = 契约尺寸 ·
///         <see cref="PayloadLengthField"/> 缺省 = 不逐单元读（按静态尺寸算） ·
///         <see cref="SamplesPerUnit"/> 缺省 1 · <see cref="ChannelCount"/> 缺省 1 · <see cref="NominalRateHz"/> 缺省"未声明"；</item>
///   <item><b>纪律</b>：这些字段**只用于计量与呈现**，**禁止参与**建网/兼容等任何框架判定（"可声明 ≠ 可判定"）；</item>
///   <item>未声明计量块的类型（或旧模块）⇒ 四档速率**只显示有定义的那几档**，绝不猜。</item>
/// </list>
/// </remarks>
public sealed record MeteringBlock
{
    /// <summary>缺省计量块（= 契约尺寸为有效载荷、每单元 1 样本 1 通道、未声明标称速率）。</summary>
    public static MeteringBlock Default { get; } = new();

    /// <summary>
    /// **有效载荷字节数**（业务数据字节，不含自描述/对齐填充）；缺省 = 契约尺寸。
    /// </summary>
    public int? PayloadSizeInBytes { get; init; }

    /// <summary>**每个数据单元包含多少个样本**（批量/多采样）；缺省 1。必须 &gt; 0。</summary>
    public double SamplesPerUnit { get; init; } = 1;

    /// <summary>**每样本的通道数**（多通道采集）；缺省 1。必须 &gt; 0。</summary>
    public int ChannelCount { get; init; } = 1;

    /// <summary>
    /// **有效长度字段名**（业务数据"到哪结束"的那个顶层字段；缺省 = 有效载荷按静态尺寸算）。
    /// </summary>
    /// <remarks>
    /// <list type="bullet">
    ///   <item><b>解决什么</b>（速率只统计有效数据，被截断的部分不应算进有效数据）：
    ///     定长契约里的**变长载荷**（典型：<c>{ len: u16, data: bytes[1472] }</c>，尾部补零）用一个静态尺寸
    ///     表达不出"这一个单元真正有多少业务字节" ⇒ 声明**哪个字段写着有效长度**，框架逐单元读它、
    ///     夹到 <c>[0, 契约尺寸]</c> 后累计 ⇒ 第 3 档（有效载荷字节速率）给出**真实**有效速率；</item>
    ///   <item><b>怎么读</b>：字段名按**顶层字段**在 <see cref="ContractLayout"/> 里定位偏移（加载期算好，运行期零反射），
    ///     按该字段的**整型原语**、**小端**读取（与数据面一致）；字段不存在 / 不是整型 /
    ///     不是结构形状 ⇒ 由 <c>ModuleContract</c> 的契约自检**明确报错**（不静默）；</item>
    ///   <item><b>与 <see cref="PayloadSizeInBytes"/> 的关系</b>：声明了本字段 ⇒ **动态值优先**
    ///     （逐单元真实长度）；没声明才回落到静态尺寸（旧模块/定长载荷行为不变）；</item>
    ///   <item>**纪律不变**：计量块**只用于计量与呈现**，禁止参与建网/兼容等任何框架判定。</item>
    /// </list>
    /// 用法：<c>Metering = new MeteringBlock { PayloadLengthField = "len" }</c>
    /// </remarks>
    public string? PayloadLengthField { get; init; }

    /// <summary>**标称速率**（Hz；仅用于"实测 vs 标称"的掉速呈现）；缺省 = 未声明。</summary>
    public double? NominalRateHz { get; init; }
}

/// <summary>
/// **数据类型声明**（模块声明并注册）：标识 + 尺寸（定长）+ 传输语义 + 计量块 + 解码器。
/// </summary>
/// <remarks>
/// <list type="bullet">
///   <item><b>类型身份归模块</b>：框架只持**机械镜像**（标识/尺寸/语义/计量/解码器句柄），
///         不理解"这是什么量"（"可声明 ≠ 可判定"）；</item>
///   <item><b>标识就是身份，没有版本</b>：类型只能改形状，不能"升一个版本再兼容"——改形状就是破坏性变更；</item>
///   <item><b>尺寸属于传输</b>（定长，v1 不变长）、**语义属于模块**；<see cref="Shape"/> 是尺寸的唯一来源；</item>
///   <item><b>兼容判定</b>：同标识 + 形状**完全一致** = 兼容；**不同标识 = 不兼容**
///         （形状相同也不自动认，框架无资格判定"是不是同一种量"）；</item>
///   <item><b>解码器可选</b>：不提供 ⇒ 呈现层显示"未声明解码器 + 字节数"，**不猜**；</item>
///   <item>任一方未声明类型 ⇒ 只按形状**完全一致**判定，不因缺少声明而放宽。</item>
/// </list>
/// </remarks>
public sealed record DataTypeDeclaration
{
    /// <summary>创建数据类型声明。</summary>
    /// <param name="typeId">
    /// 类型标识（非空）。内置约定用 **PascalCase 短名**（如 <c>RawBytes</c>、<c>WaveformFrame</c>），
    /// 不带作者前缀、不带版本；**跨模块同名 = 同一类型**，所以名字要稳定、尽量不易撞名。
    /// </param>
    /// <param name="shape">定长形状（尺寸由它决定）。</param>
    /// <param name="semantics">传输语义（流/消息/事件；跨语义不判兼容）。</param>
    public DataTypeDeclaration(
        string typeId,
        ContractShape shape,
        TransferSemantics semantics = TransferSemantics.Stream)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(typeId);
        ArgumentNullException.ThrowIfNull(shape);

        TypeId = typeId;
        Shape = shape;
        Semantics = semantics;
    }

    /// <summary>类型标识（身份；同一标识只许声明一次）。</summary>
    public string TypeId { get; }

    /// <summary>定长形状（尺寸 = <see cref="ContractShape.SizeInBytes"/>）。</summary>
    public ContractShape Shape { get; }

    /// <summary>传输语义（跨语义不判兼容）。</summary>
    public TransferSemantics Semantics { get; }

    /// <summary>定长尺寸（字节）——**机械镜像**，供长度校验与"搬运字节"计量用。</summary>
    public int SizeInBytes => Shape.SizeInBytes;

    /// <summary>计量块（可声明；字段全可选）。</summary>
    public MeteringBlock Metering { get; init; } = MeteringBlock.Default;

    /// <summary>
    /// **样本解码器**（呈现期解码由模块提供；框架只回调）——<see langword="null"/> = 不提供，UI 显示"未声明解码器"。
    /// </summary>
    public SampleDecoder? Decoder { get; init; }

    /// <summary>
    /// **语义元数据的可读描述**（单位/量程/精度等，仅供**展示**）。
    /// </summary>
    /// <remarks>**禁止参与任何框架判定**（与"契约不含单位/量纲语义"一致：可声明 ≠ 可判定）。</remarks>
    public IReadOnlyList<string> SemanticTags { get; init; } = [];

    /// <inheritdoc />
    public override string ToString() => $"{TypeId} ({Shape.Describe()}, {SizeInBytes}B)";
}
