# 波形数据帧类型约定（WaveformFrame）

> 状态：已定稿并落地。函数发生器、波形显示器与相关控件已实现；本文是类型约定的设计记录。
>
> 公开契约口径见 [docs/zh/concepts/waveform-frame.md](../../docs/zh/concepts/waveform-frame.md)；线上字段、发送端义务与消费方校验以公开页为准。本文保留设计理由、决策与验证背景。
>
> 本文只定义模块间的数据帧约定与解析标准。框架只负责把数据帧投递到端口；帧进入模块后，由模块自己的应用逻辑处理、过滤或丢弃。框架不解析帧内容，不按组/通道过滤，也不为模块的视图/控件提供专用通路。

## 1. 定位与边界

这个类型解决的问题是：**让所有与波形数据相关的模块，对“一帧数据长什么样、字段怎么读、组和通道怎么区分”有一份共同约定**。

它不解决的问题：

- 不规定函数发生器如何生成波形、如何分组、如何调度发送。
- 不规定波形显示器如何缓冲、如何过滤、如何绘制、用什么控件、如何保存。
- 不规定框架如何按组/通道路由，不新增框架级数据视图契约。
- 不替代模块自己的配置、状态与界面设计。

一帧是**连续的波形数据流**中的一帧；类型本身没有“首帧/末帧”“运行开始/结束”的语义。模块如果要标记开始/结束、一次性发送的边界、自己的样本序号，那属于模块自己的逻辑。

## 2. 术语

| 术语 | 含义 |
|---|---|
| 数据帧 | 一个定长数据单元，`280 B`；一帧只属于一个通道组 |
| 通道组 | 由 `groupId` 标识的一组通道；一帧带一个组 |
| 通道 | 组内的一个波形通道，按组内序号 `0..n-1` 标识 |
| 样本 | 一个通道在一帧中的 `f32` 值；一帧每通道一个样本 |
| 有效位 | `validMask` 的第 i 位，表示第 i 通道在本帧是否有有效样本 |
| 声明帧率 | 帧头 `rateHz`，发送端声明的标称帧率；不代表实际到达时间 |
| 完整性序号 | 帧头 `sequence`，组内回绕序号；只用于丢帧/重复/乱序等完整性判断，不作时序判据 |

## 3. 类型身份与声明

- 类型标识：`WaveformFrame`
- 传递语义：`Stream`
- 定长尺寸：`280 B`
- 类型身份：`TypeId + Shape`；没有版本字段。字段、类型、顺序或尺寸发生变化，就是另一种类型，必须换新的类型标识。
- 标识风格：**PascalCase 短名**（不带作者前缀、不带版本）；`hyw/*` 是**模块 ID** 的格式，不要拿来命名类型。
- 类型不是共享程序集类型：两个模块各自在自己的 `OnRegistered` 里声明同一标识与同一形状；不要放进 `Sdk`，也不要引用另一个模块。

### 3.1 声明配方

下面的代码是**模块作者的声明配方**，不是要新增的共享类型。每个模块复制同样的字段、顺序与取值。

```csharp
public const string WaveformFrameTypeId = "WaveformFrame";
public const int WaveformFrameMaxChannels = 64;
public const int WaveformFrameSizeInBytes = 280;
public const int WaveformFrameHeaderSize = 24;

public static readonly ContractShape WaveformFrameShape = ContractShape.OfStruct(
    ("channelCount", ContractShape.OfPrimitive(ContractPrimitive.U16)),
    ("payloadBytes", ContractShape.OfPrimitive(ContractPrimitive.U16)),
    ("groupId", ContractShape.OfPrimitive(ContractPrimitive.U32)),
    ("sequence", ContractShape.OfPrimitive(ContractPrimitive.U32)),
    ("rateHz", ContractShape.OfPrimitive(ContractPrimitive.F32)),
    ("validMask", ContractShape.OfPrimitive(ContractPrimitive.U64)),
    ("samples", ContractShape.OfArray(ContractPrimitive.F32, WaveformFrameMaxChannels)));

public static readonly MeteringBlock WaveformFrameMetering = new()
{
    PayloadLengthField = "payloadBytes",
    SamplesPerUnit = 1,
    ChannelCount = 1,
};
```

```csharp
context.Declaration.DeclareDataType(new DataTypeDeclaration(
    WaveformFrameTypeId,
    WaveformFrameShape,
    TransferSemantics.Stream)
{
    Metering = WaveformFrameMetering,
    // 不声明 Decoder：一个 double 表达不了 n 个通道；由模块自己解析样本区。
});
```

端口侧（发送与接收同型）：

```csharp
context.Declaration.DeclarePort(new PortDeclaration(
    "out", PortDirection.Send, WaveformFrameShape, TransferSemantics.Stream)
{
    TypeId = WaveformFrameTypeId,
});
```

### 3.2 形状指纹

模块的契约自检应逐字比对下面的描述：

```text
{channelCount:u16, payloadBytes:u16, groupId:u32, sequence:u32, rateHz:f32, validMask:u64, samples:f32[64]}
```

同时校验 `SizeInBytes == 280`。如果任一模块的字段名、顺序、类型或数组长度不同，框架会把它们判为不同形状；同一标识下出现两种形状时，`DataTypeLedger` 会报冲突。

## 4. 字段布局

所有整数与浮点均为**小端**；字段从帧首字节起按下列偏移排列，没有额外填充。

| 偏移 | 字段 | 形状 | 字节 | 语义 |
|---:|---|---|---:|---|
| 0 | `channelCount` | u16 | 2 | 本帧通道数，1–64；逐帧有效，允许变化 |
| 2 | `payloadBytes` | u16 | 2 | 已用样本字节数 = 4 × `channelCount`，4–256；用于计量与格式校验 |
| 4 | `groupId` | u32 | 4 | 通道组标识；0 保留为“未分配”，发送端不得发送 0 |
| 8 | `sequence` | u32 | 4 | 组内回绕序号；只作完整性判据，不作时序判据；模块可忽略 |
| 12 | `rateHz` | f32 | 4 | 声明标称帧率（Hz）；0/NaN/Inf 表示未知 |
| 16 | `validMask` | u64 | 8 | bit i = 1 表示 `samples[i]` 有效；i ≥ `channelCount` 的位必须为 0 |
| 24 | `samples` | f32[64] | 256 | 第 i 通道的样本；i ≥ `channelCount` 的槽必须为 0；无效通道槽必须为 0 |
| **合计** |  |  | **280** | 头部 24 B + 样本区 256 B |

规则：

- `channelCount` 是**逐帧**字段；连续流中允许变化。类型不定义“首帧/末帧”，也不要求变化必须伴随任何边界标记；模块自行处理曲线增减或状态重置。
- `payloadBytes` 只表达这一帧用了多少样本字节（`4 × channelCount`）。它不区分有效/无效通道；有效通道由 `validMask` 表达。无效通道的 4 B 槽仍计入已用样本区。
- `groupId` 是**唯一**的组区分键；`sequence` 不用来区分组。
- `rateHz` 是声明值，不是实际测量值；它不表示实际到达时间，也不得由 `sequence` 推导时间。
- `sequence` 是组内序号；发送端可按组每帧 +1，允许按 2³² 回绕。接收端可以用差值判断丢帧、重复或乱序，但**不得**用它计算时间、样本位置或任何时序判据。模块可以完全不使用它。
- `validMask` 逐帧有效；0 表示该通道本帧无有效样本，模块应把折线断开，不得把该槽当数据。全 0 mask 的帧合法：它不产生点，但仍是一帧。
- `samples` 的前 `channelCount` 个槽是本帧样本；其余槽与无效通道槽必须写 0，接收端忽略。

## 5. 计量块

```csharp
new MeteringBlock
{
    PayloadLengthField = "payloadBytes", // 每帧 4 × channelCount
    SamplesPerUnit = 1,                  // 每单元每通道 1 个样本
    ChannelCount = 1,                    // 框架按“每通道”口径给样本速率
    // NominalRateHz 留空：一个端口可混多个组，组速率在帧头
}
```

框架四档速率的含义：

| 档 | 本类型的含义 |
|---|---|
| 单元速率 | 每源每秒帧数（端口级） |
| 搬运字节速率 | 280 B × 帧数（按宿计） |
| 有效载荷字节速率 | `payloadBytes` 累计（4 × 通道数；含无效槽的占位字节） |
| 样本 / 通道样本速率 | 单元速率 × 1 样本 × 1 通道 = 每通道样本速率；总通道样本速率由模块自己按 `channelCount`/`validMask` 计算 |

说明：

- 端口级不声明类型级标称速率；每组的“声明速率”取帧头 `rateHz`，“实际速率”由模块按到达帧数自行测量。两者由模块自己的界面分列，不能混成一个数。
- 框架的有效载荷速率按 `payloadBytes` 计算，因此无效通道的占位槽会被算入“已用样本区”。模块若要统计“有效通道样本”，按 `validMask` 自己算，两者口径不同，不能互相替代。

## 6. 写入、读取与校验

### 6.1 写入（发送模块）

1. 取 280 B 的复用缓冲并清零。
2. 写 `channelCount`、`groupId`、`sequence`、`rateHz`。
3. 对每个 `i < channelCount`：写 `samples[i]`；通道有效则置 `validMask` 第 i 位，否则样本写 0。
4. 写 `validMask`；写 `payloadBytes = 4 * channelCount`。
5. 调用 `TrySend`。全程小端，不使用 LINQ，不逐帧分配。

发送端按组维护 `sequence` 即可；它只是完整性提示，不参与发送节奏、不定义首末帧。发送端重启后序号如何处理，由模块自己决定；类型不定义重置语义。

### 6.2 读取（接收模块）

1. 长度至少 280 B（框架已校验；模块侧仍建议保留防御）。
2. 读头部，按 6.3 的顺序校验。
3. 用固定偏移 24 取样本区，只消费 `i < channelCount` 的槽。
4. 按 `validMask` 更新模块自己维护的组/通道状态；是否断线、是否丢弃、是否显示，全部由模块决定。

### 6.3 校验顺序与失败处置

| 顺序 | 检查 | 失败处置 |
|---:|---|---|
| 1 | `channelCount` 在 1–64 | 丢整帧，不部分更新；经 `IPortAccess.ReportLoss(口名, DropReason.ModuleInternal, 1, length)` 计数 |
| 2 | `payloadBytes == 4 * channelCount` | 同上 |
| 3 | `groupId != 0` | 同上 |
| 4 | `validMask` 只取 `[0, channelCount)` 内的位；额外位忽略（可记一次诊断） | 继续 |
| 5 | `rateHz` 是有限正数；否则视为未知 | 继续；时间轴由模块自行降级或改用用户设定 |
| 6 | 无效通道的样本值、非有限样本 | 忽略或按断点处理；不因绘图问题丢帧 |

任何“丢整帧”都不得留下半帧状态。高频路径不抛异常、不逐帧刷 Issue，只计数并按需提示一次。

## 7. 组、通道与模块消费

- **一帧只有一个组**：`groupId` 是唯一组标识；一帧不能同时表达两个组。
- **一个端口可以混多个组**：接收模块按 `groupId` 自行分流。每个组的通道数、序号、显示状态都由模块自己维护。
- **一个模块可以有多个组**：生成器按自己的分组逻辑分别发帧；显示模块动态发现组，不需要生成器预先声明组清单。
- **匹配、过滤、丢弃在模块内**：框架把端口上的每个帧都交给模块；模块可以只看某些组、只看某些通道、丢弃不关心的帧。类型字段只是让这件事在全应用里用同一套键（`groupId` + 通道序号）来做。
- **多播**：端口 fan-out 天然支持；帧是只读字节，复制安全；类型不需要额外字段。
- **多源**：数据面不暴露来源；同一端口上的不同发送者必须使用不同 `groupId`。同组 ID 的两个发送者视为配置错误，不保证可分离；若将来必须支持，新增带来源字段的类型，不在此类型上加字段。
- **通道身份**：组内序号 `0..n-1`；`channelCount` 逐帧有效，允许变化。模块按序号维护自己的曲线名称/颜色；序号变化不代表任何“运行边界”，模块自行决定如何延续或重置。
- **模块自己的视图/控件**：不在本文约定内。模块拿到帧后如何缓冲、如何绘制、用什么控件、是否新开窗口，全部由模块自己的实现决定。

## 8. 序号、声明速率与时间

### 8.1 序号只作完整性判据

- `sequence` 按组独立，发送端每帧加 1，按 2³² 回绕。
- 接收端可以用差值判断丢帧、重复或乱序：差值为 1 表示连续；差值为 0 或明显回退表示重复/过期；其余缺口表示有帧未到达。
- **不得**用 `sequence` 计算时间、样本位置、x 轴坐标或任何时序判据。它只回答“这一组的数据是否完整”，不回答“这一帧发生在什么时候”。
- 模块可以完全忽略 `sequence`；类型不强制消费它。
- 投递队列按（源，宿）保证顺序；跨源只按到达顺序。序号不用于跨源排序，也不改变队列的投递顺序。

### 8.2 时间轴

- `rateHz` 是发送端声明的标称帧率，不是实际测量值；实际到达时间受调度、合并、延迟影响，不保证与声明值一致。
- 模块的时间轴由模块自己决定：可以用自己的接收计数换算，可以用用户设定的时间基，也可以只显示数据个数。类型只提供 `rateHz`，不规定换算方式。
- “数据个数”轴使用模块自己的接收计数或自己的样本索引，**不是** wire 的 `sequence`。
- 丢帧时，模块可以用 `sequence` 缺口知道数据不完整，但如何表现缺口（断线、标记、忽略）由模块自己决定；不得把缺口当成连续时间。

### 8.3 帧率与通道数的变化

- `rateHz` 与 `channelCount` 都是逐帧字段，允许变化。类型不定义“变化必须伴随开始/结束”的规则。
- 模块发现声明帧率或通道数变化时，可以按自己的策略继续、重置或丢弃；这是模块自己的逻辑。
- 类型不定义首帧、末帧、运行开始、运行结束、一次性发送的边界；这些全部属于模块自己的状态机。

## 9. 明确不承载

以下内容一律不进帧，属于模块配置、模块内部状态或呈现：

- 首帧/末帧标记、运行开始/结束、一次性发送边界。
- 任何以序号表达时间或样本位置的语义。
- 组名、通道名、颜色、可见性、曲线顺序、y 轴缩放。
- 单位、量程、精度、光标位置、跟踪开关、保存格式。
- 波形类型、周期、相位、offset、幅值、频率、分辨率。
- 来源实例身份、实际时间戳、抖动、发送时刻。
- 传输包分帧头、校验和、压缩、加密。
- 模块如何过滤、如何缓冲、如何送视图、用什么控件。
- 超过 64 通道、一帧多点、每通道 ID、变长字符串。
- 类型版本字段；类型身份就是 `TypeId + Shape`。

## 10. 兼容与演进

- 两个模块必须逐字声明同一 `TypeId` 与同一形状；端口形状必须等于类型形状；传递语义都必须是 `Stream`。
- 同一标识出现两种形状时，框架会报冲突；连接也会因形状不一致而失败。模块自检应先用 golden string 与 `SizeInBytes` 兜住。
- 类型没有版本。任何字段、类型、顺序、尺寸变化都是破坏性变更，必须使用新的类型标识。
- 未来变体各自使用新标识，例如：
  - `WaveformFrame128`：超过 64 通道。
  - `WaveformFrameBlock`：一帧多采样点/批量样本。
  - 带来源字段的变体：需要区分同组多源时。
- 不要通过复用同一标识、只改字段含义来“升级”；框架没有版本协商，也不会自动转换。

## 11. 与现有框架机制的适配

| 机制 | 本类型的用法 | 结论 |
|---|---|---|
| 类型声明/兼容 | 两模块声明同一标识/形状；golden string 自检 | 直接支持 |
| 固定尺寸投递 | 280 B 一帧，一帧一数据单元 | 直接支持 |
| 计量块 | `payloadBytes = 4 × channelCount`，框架读取该字段 | 直接支持 |
| 多源 | `groupId` 是逻辑身份；多源须用不同组 ID | 有条件支持；同组多源不支持 |
| 外部接入 | 多个 280 B 帧可由模块封进一个传输包 | 直接支持；分帧在模块侧 |
| 持久化 | 组/通道名称、颜色、显示配置存模块配置 | 直接支持；线上只有 ID |
| 热路径 | 固定偏移、Span、无分配纪律；样本区可直接 `MemoryMarshal.Cast` | 直接支持 |
| 模块消费/视图/控件 | 模块自己持有应用逻辑与视图 | 不涉及；框架不需要扩展 |

**结论**：类型与现有数据面、契约、计量机制适配；不需要框架级改动，也不需要框架感知组/通道/视图。框架只负责投递到端口，之后全部由模块处理。

## 12. 后续模块与验证

### 12.1 后续模块（不在本类型定义内）

- 函数发生器：组/通道配置、定时器、封帧、连续/一次性发送、预览。
- 波形显示器：收帧、组/通道状态、曲线缓冲、游标/跟踪、清空/参数复位、CSV/JPG/PNG 保存。
- 模块自己的视图/控件：由模块自行决定如何呈现；本类型不规定框架级视图/控件通路。

### 12.2 本阶段验证

本文是设计文档，没有代码改动，不需要构建或测试。完成标准：

- 字段偏移相加为 280 B；golden string 与声明配方逐字一致。
- 文档只定义帧与解析标准，不出现“框架按组/通道路由”“框架感知帧内容”“框架级视图/控件通路”的设计。
- 两个未来模块能只凭本文各自声明同一类型，并通过契约自检。

### 12.3 未来模块阶段的验证

| 层 | 内容 |
|---|---|
| T0 | 两个模块项目分别编译 |
| T1 | 契约自检（golden string、`SizeInBytes == 280`、字段偏移）；写入→读取往返（1 通道/64 通道/全无效 mask/rate=0）；畸形帧拒绝（`groupId=0`、`channelCount=0/65`、`payloadBytes` 不匹配）；`sequence` 缺口只用于完整性判断，不写时序断言 |
| T2 | 模块测试项目全量 |
| T3 | 交给用户手工验收前跑完整门禁（模块包与契约面改动） |
| 组合验证 | 生成器→显示器：两组×四通道、通道停止、组交织、一次性发送；全部由模块自己的逻辑验证 |
| 人眼验收 | 坐标轴、游标、图例、颜色、动态组页签、保存结果；不写像素/布局断言 |

测试只覆盖“看不见的机制”（形状、往返、拒绝、完整性、缓冲）；呈现走截图 + 人眼。未来新增用例遵守模块测试预算，同一事实只断言一处。

## 13. 决策记录

| ID | 决定 |
|---|---|
| D1 | 一个数据单元 = 一个通道组的一帧 = 每通道 1 个 f32 样本。 |
| D2 | 通道身份 = 组内序号 `0..n-1`；停止通道用 `validMask` 表达，不删除。 |
| D3 | 每帧最多 64 通道；总 280 B；超过则换新类型。 |
| D4 | 组身份只用 `groupId`；不带来源、时间戳；同一端口多发送者必须用不同组 ID。 |
| D5 | 样本 f32、小端、无单位；单位/量程属模块配置。 |
| D6 | `rateHz` 是声明标称值；不用到达时间；`sequence` 不参与时间轴。 |
| D7 | 用 `validMask` 表达每通道有效性；类型没有首帧/末帧、运行开始/结束、`Start`/`Stop` 标志。 |
| D8 | 名称、颜色、单位、波形类型、频率等不上线；属模块配置/呈现。 |
| D9 | 类型没有版本字段；任何形状变化 = 新类型标识。 |
| D10 | 计量块用 `payloadBytes`、`SamplesPerUnit=1`、`ChannelCount=1`、`NominalRateHz` 空。 |
| D11 | 类型标识用 **PascalCase 短名**（`WaveformFrame`；与 `RawBytes` 同风格），不带前缀、不带版本；`hyw/*` 是模块 ID 格式，不是类型标识格式。 |
| B1 | 类型只定义帧与解析标准；框架只投递到端口，不按组/通道过滤，不感知帧内容。 |
| B2 | 模块收到帧后自行处理/过滤/丢弃；模块如何缓冲、如何送自己的视图/控件，是模块自己的实现。 |
| B3 | 本阶段无框架/SDK/控件改动；类型是模块间约定，不是共享代码类型。 |
| B4 | `sequence` 只作数据完整性判据；模块可忽略；不得作为任何时序判据。 |

## 14. 参考

- [数据面](../../docs/zh/concepts/data-plane.md)（类型身份、定长数据单元、计量块）
- [契约声明](../../docs/zh/module-dev/contracts.md)（`DeclareDataType` / `DeclarePort`）
- `src/Sdk/Contracts/DataTypeDeclaration.cs`（类型声明、计量块、解码器）
- `src/Sdk/Contracts/ContractShape.cs`（形状与结构相等）
- `src/Core/Composition/ContractCompatibility.cs`（兼容判定）
- `src/Core/Diagnostics/DataTypeLedger.cs`（同标识异形状冲突）
- `src/Core/Binding/Binding.cs`（`PayloadLengthField` 的逐单元读取）
