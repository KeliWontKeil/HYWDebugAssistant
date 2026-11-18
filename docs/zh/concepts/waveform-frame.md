# 波形帧约定 WaveformFrame

本页是 `WaveformFrame` 的公开契约：两个模块各写一份声明，不共享程序集类型。字段、顺序或尺寸变化就是另一种类型，必须换新标识。

## 身份

| 项 | 值 |
|---|---|
| 类型标识 | `WaveformFrame`（PascalCase 短名，无作者前缀、无版本） |
| 传递语义 | `Stream` |
| 定长尺寸 | `280 B` |
| 形状 | `{channelCount:u16, payloadBytes:u16, groupId:u32, sequence:u32, rateHz:f32, validMask:u64, samples:f32[64]}` |
| 类型身份 | `TypeId + Shape`；没有版本字段 |

两个模块各自在自己的 `OnRegistered` 里声明同一标识与同一形状；端口形状必须等于类型形状。不要把它放进 `Sdk`，也不要引用另一个模块。

## 布局

所有整数与浮点均为**小端**；字段从帧首字节起连续排列，没有额外填充。

| 偏移 | 字段 | 形状 | 字节 | 语义 |
|---:|---|---|---:|---|
| 0 | `channelCount` | u16 | 2 | 本帧通道数，1–64；逐帧有效，允许变化 |
| 2 | `payloadBytes` | u16 | 2 | 已用样本字节数 = `4 × channelCount`，4–256 |
| 4 | `groupId` | u32 | 4 | 通道组标识；**0 保留为未分配，发送端不得发送 0** |
| 8 | `sequence` | u32 | 4 | 组内回绕序号；只作完整性判据，不作时序判据 |
| 12 | `rateHz` | f32 | 4 | 声明标称帧率；0/NaN/Inf 表示未知 |
| 16 | `validMask` | u64 | 8 | bit i = 1 表示 `samples[i]` 有效；`i ≥ channelCount` 的位必须为 0 |
| 24 | `samples` | f32[64] | 256 | 第 i 通道样本；`i ≥ channelCount` 的槽与无效通道槽必须写 0 |
| **合计** |  |  | **280** | 头部 24 B + 样本区 256 B |

## 字段级约束由谁负责

这是本页最重要的一条：**字段级约束的归属必须写清楚，不能只藏在消费方代码里。**

| 约束 | 归属 | 不满足时 |
|---|---|---|
| `channelCount ∈ [1, 64]` | **发送端必须保证** | 消费方可以丢整帧，并计入模块内部丢弃 |
| `payloadBytes == 4 × channelCount` | **发送端必须保证** | 消费方可以丢整帧 |
| `groupId != 0` | **发送端必须保证**；属于线上契约，不是显示器的局部偏好 | 消费方可以丢整帧 |
| `validMask` 只置 `[0, channelCount)` 内的位 | **发送端必须保证**；额外位接收端忽略 | 消费方忽略额外位 |
| 无效通道槽、未用槽写 0 | **发送端必须保证** | 消费方不得把这些槽当数据 |
| `rateHz` 有限且 > 0 | 发送端声明；不满足视为未知 | 消费方降级时间轴，不丢帧 |
| `sequence` 按组递增、可回绕 | 发送端自行维护 | 消费方只用于完整性判断，可完全忽略 |

如果某条字段约束会让消费方**整帧丢弃**，它必须：

- 写进本契约页的“发送端必须保证”列；
- 被生产方在发送前校验，而不是等消费方静默丢；
- 在消费方以明确的 `DropReason`/计数暴露，不能只表现为“窗口没反应”。

显示器的业务过滤（例如只看某些组、某些通道）不属于线上契约；这类过滤不应让**生产方**无从得知。当前显示器对 `channelCount`、`payloadBytes`、`groupId` 的检查是**防御性契约校验**，不是显示器私有的格式偏好。

## 计量

```csharp
new MeteringBlock
{
    PayloadLengthField = "payloadBytes", // 4 × channelCount
    SamplesPerUnit = 1,                  // 每单元每通道 1 个样本
    ChannelCount = 1,                    // 按“每通道”口径给样本速率
    // NominalRateHz 留空：一个端口可混多个组，组速率在帧头
}
```

| 速率档 | 本类型的含义 |
|---|---|
| 单元速率 | 每源每秒帧数（端口级） |
| 搬运字节速率 | `280 B × 帧数`（按宿计） |
| 有效载荷字节速率 | `payloadBytes` 累计（含无效槽的 4 B 占位） |
| 样本/通道样本速率 | 单元速率 × 1 样本 × 1 通道；总通道样本速率由模块按 `channelCount`/`validMask` 自行计算 |

`rateHz` 是声明值，不是实际测量值；实际速率由模块按到达帧数自行测量。两者不要混成一个数。

## 组、通道与完整性

- 一帧只属于一个通道组，`groupId` 是唯一组键；一个端口可以混多个组，接收方自行分流。
- 组内通道按 `0..channelCount-1` 标识；`channelCount` 逐帧有效，允许变化。
- `sequence` 只回答“这一组是否完整”，不得用于时间、样本位置或任何时序判据；模块可完全忽略。
- 类型没有首帧/末帧、运行开始/结束、一次性发送边界；这些属于模块自己的状态机。
- 名称、颜色、单位、量程、波形类型、周期、来源身份、时间戳等**不进帧**，属于模块配置/呈现。

## 声明与校验清单

- [ ] `TypeId == "WaveformFrame"`，传递语义为 `Stream`；
- [ ] `SizeInBytes == 280`，字段顺序与 golden string 逐字一致；
- [ ] 端口形状等于类型形状，端口引用同一 `TypeId`；
- [ ] 发送端校验 `channelCount`、`payloadBytes`、`groupId != 0`，并写零未用槽；
- [ ] 接收端对畸形帧整帧丢弃，不留半帧状态，并记录丢弃原因；
- [ ] 与至少一个内置对端（波形发生器或波形显示器）建网成功并看到数据；
- [ ] 只与自己做的新模块互连**不算**兼容性证据。

## 演进

- 类型没有版本；任何字段、顺序、类型或尺寸变化都是破坏性变更，必须使用新的类型标识（例如 `WaveformFrame128`、`WaveformFrameBlock`）。
- 不要通过同一标识改字段含义来“升级”；框架没有版本协商，也不会自动转换。
- 不要保留“兼容读取旧形状”的分支；所有声明同一标识的模块必须同批更新。

## 相关

- [数据面](data-plane.md)：类型身份、兼容判定、计量机制与 `RawBytes` 对照
- [连接与组网](../guides/connect.md)：建网操作与失败查看位置
- [波形显示器](../guides/modules/waveform-display.md) / [波形发生器](../guides/modules/waveform-generator.md)：两个内置消费/生产方
- [原始 → 波形](../guides/modules/raw-to-waveform.md) / [波形 → 原始](../guides/modules/waveform-to-raw.md)：两个内置转换模块（原始字节流 ↔ 波形帧）
- [指针仪表盘](../guides/modules/gauge-display.md) / [横向刻度条](../guides/modules/scale-bar-horizontal.md) / [纵向刻度条](../guides/modules/scale-bar-vertical.md) / [三轴姿态仪](../guides/modules/attitude-display.md)：四个内置显示模块（单通道仪表 / 单通道量条 / 三通道姿态）
- [发布与装载](../module-dev/publishing.md)：模块装载与重启规则
