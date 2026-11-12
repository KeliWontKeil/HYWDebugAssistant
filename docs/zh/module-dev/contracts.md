# 声明面

声明是模块与框架的契约：模块说“我有哪些类型、口、界面通道”，框架据此建缓冲、校验连接、生成表单。声明只在 `OnRegistered` 里写一次，框架随后只读一次并转成不可变元数据；运行期不再重读，也不要再调用 `Declare*`。

成员签名与默认值见[接口参考](reference.md)；本页讲**怎么写才合法、写错会怎样**。

## 声明顺序

1. 数据类型
2. 端口
3. 角色与内部处理顺序（可选）
4. 显示源 / 输入 / 命令（界面契约）
5. 外部接入（可选）

顺序不是框架强制，但按这个顺序读代码最清楚：类型先有，口才能引用；口先有，角色才能引用；界面键与接入名最后登记。

## 数据类型

```csharp
public const string TypeId = "Pulse";

public static readonly ContractShape Shape = ContractShape.OfStruct(
    ("sequence", ContractShape.OfPrimitive(ContractPrimitive.U64)),
    ("value", ContractShape.OfPrimitive(ContractPrimitive.F32)));

context.Declaration.DeclareDataType(new DataTypeDeclaration(TypeId, Shape)
{
    Metering = new MeteringBlock { PayloadSizeInBytes = sizeof(ulong) + sizeof(float) },
});
```

| 项 | 规则 |
|---|---|
| 标识 | 非空；模块内唯一；用 **PascalCase 短名**（如 `RawBytes`、`WaveformFrame`），不带前缀、不带版本。跨模块同名即同一类型；改形状就换标识 |
| 形状 | 全部定长：原语、字节块（1–4096 字节）、定长数组（1–256 元素）、结构（嵌套 ≤ 4，字段名唯一） |
| 尺寸 | 由形状决定；端口的形状必须与它**完全一致**（字段名、原语、长度、顺序全同） |
| 语义 | `Stream` / `Message` / `Event`；只在兼容判定里比较，跨语义不判兼容 |
| 计量块 | 可选；字段全部可选。有效长度字段必须是**顶层整型**字段 |
| 解码器 | 可选；把一个定长样本解成一个 `double`。多通道/多字段结构不要给解码器 |
| 语义标签 | 可选的可读注记（单位、量程等），只用于展示，不参与判定 |

设计一条新约定（粒度、布局、标识、演进、指纹）见[数据面](../concepts/data-plane.md)的“设计一条约定”；本页不重复设计规则。参与两个模块时，两边各自声明同一标识与同一形状，不共享代码。

## 端口

```csharp
context.Declaration.DeclarePort(new PortDeclaration(OutPort, PortDirection.Send, Shape)
{
    TypeId = TypeId,
    Edge = PortEdge.Bottom,
});
```

| 项 | 规则 |
|---|---|
| 名称 | 模块内唯一且稳定；连接锚定为 `实例ID.口名`。多同型口用 `rx1`/`rx2` |
| 方向 | `Send` / `Receive`；双向 = 两个口 + 两条单向网络 |
| 形状 | 引用了类型时，必须与类型形状完全相同 |
| 类型引用 | 不声明 `TypeId` = 只按形状判定；声明后按标识 + 形状判定；引用的标识必须在本模块声明过 |
| 默认边 | `Auto`（发送右、接收左）、`Left/Right/Top/Bottom`；用户拖动优先 |
| 锚点 | `AnchorOffset` 0–1；`AnchorDraggable` 默认 `false` |

常见错误：

- 口引用了未在本模块声明的标识：框架把该口退回“未声明类型”，只按形状判定，自检会报问题；
- 口形状与类型形状写法不同（字段名大小写、顺序、长度）：加载期拒连，计数/呈现按错的那一份算；
- 同一模块两个口同名：后一个被拒并报 Issue。

## 角色与内部顺序

角色是模块内部的功能分块，只用于叠加层呈现：一个角色 = 一种数据类型 + 输入口集合 + 输出口集合。

```csharp
context.Declaration.DeclareRole(RoleDeclaration.Both(RoleId, TypeId, InPort, OutPort, "转换"));
context.Declaration.DeclareFlow(new FlowDeclaration("convert", "limit"));
```

| 规则 | 违反后果 |
|---|---|
| 角色标识模块内唯一 | 自检 `Problems`，加载期忽略重复项 |
| 引用的类型必须在本模块声明 | 自检 `Problems` |
| 输入口必须是接收口、输出口必须是发送口 | 自检 `Problems` |
| 一个口最多属于一个角色 | 自检 `Problems` |
| 角色的输入与输出必须承载同一类型 | 自检 `Problems`；类型转换请拆成两个角色 + `DeclareFlow` |
| 有数据口却没有角色 | 只给建议（`Advisories`），不影响合法性；叠加层不画方块 |
| 口数 > 2 | 只给建议；多入一出是合法形态，不必拆 |
| 内部顺序两端角色必须存在、不得自环、不得重复 | 自检 `Problems` |

角色不参与建网、兼容、停流、投递与计量，也不进组合件。

## 显示源

```csharp
context.Declaration.DeclareDisplaySource(new DisplaySourceDeclaration(
    CountSource, ViewDataSourceKind.Value, ContractPrimitive.U64));
```

| 类别 | 缓冲 | 典型用途 | 写入 |
|---|---|---|---|
| `Value` | 最新值槽（1） | 计数、状态数值 | `Ports.Publish` |
| `Sequence` | 环形 512 样本 | 滚动曲线 | `Ports.Publish` |
| `RowSet` | 环形 512 | 表格行 | `Ports.Publish` |
| `ByteBlock` | 环形 512 | 十六进制视图 | `Ports.Publish` |
| `Text` | 环形 512 | 日志/文本 | `Display.Append*` / `SetText` 或 `Ports.Publish` |
| `Frame` | 环形 512 | 图像/位图 | `Ports.Publish` |
| `SeriesWindow` | 无容量上限 | 任意范围的整窗曲线 | `Display.SetSeriesWindow` |

- 名称模块内唯一；重复声明后者被拒并报 Issue。
- `Value`/`Sequence` 要声明 `ValueType`，按该原语的字节数写小端值。
- 第一次写入决定槽宽；之后更长的样本被丢弃并计数；空样本也被丢弃。
- `Text` 有两条写入路径：`Display.Append*`/`SetText` 走框架的文本缓冲（行数上限由应用设置，默认 20000 行）；`Ports.Publish` 走 512 样本环形缓冲。两者不是同一块缓冲，按用途选一条。
- 未声明的名字被写入时，框架丢弃并只提示一次。
- 文本缓冲的总行数由应用设置控制（默认 20000 行），不是模块自己配的。

## 输入

```csharp
context.Declaration.DeclareInput("savePath");                       // 临时：关闭即丢
context.Declaration.DeclareInput(new InputDeclaration("interval")
{
    Persistence = InputPersistence.Persistent,                      // 随组合件保存
});
```

- 输入名模块内唯一；未登记的输入名由框架拒收并报 Issue。
- 用户提交、文件选择结果、开关拨动都经输入源投递，模块在 `OnStep` 里从收件箱取。
- 有副作用的输入（路径、间隔、发送内容）用 `Transient`；需要下次打开还在的用 `Persistent`。
- 有输入但没有界面元素是合法的：例如文件选择只把路径投回一个看不见的输入源。

## 命令

```csharp
context.Declaration.DeclareCommand(ResetCommand, "复位");
```

- 命令名模块内唯一；未登记的命令名由框架拒收并报 Issue。
- 第二个参数只用于诊断文案（“执行「复位」失败”），不参与任何判定。
- 命令只是“用户点了一下”，不保证被执行；模块从收件箱取出后自行决定幂等与节奏。

## 外部接入

```csharp
context.Declaration.DeclareExternalAccess(new ExternalAccessDeclaration(
    "my-serial", ExternalAccessStandardIds.Serial)
{
    MaxUnitBytes = 1472,
});
```

| 项 | 规则 |
|---|---|
| 接入名 | 模块内唯一；界面与诊断按这个名字寻址 |
| 标准标识 | 必须是框架已提供的值（`serial` / `tcp-client` / `tcp-server` / `udp`）；未知标识 ⇒ 该接入不接线并报 Issue |
| 单元上限 | 默认 1472；必须 > 0 且不超过框架上限，否则该接入被拒绝 |
| 参数类型 | 框架按标准登记；模块不引用它，但视图可以用 `ModuleConfigField` 绑定它的字段名 |
| 表单文案 | 参数表单的标题/说明/分组/枚举显示由**本模块**的文本表解析，需提供对应的 `config.*` 键 |

外部接入只搬原始字节：一次读到的是一块字节，不是完整报文；需要报文边界时由模块自己组帧。机制见[外部接入](../concepts/access.md)，用法见[实现](implementation.md)。

## 写错会怎样

| 检查 | 在哪报 | 后果 |
|---|---|---|
| 入口缺公共无参构造 / 未实现 `IModule` | 自检 `Problems` / 加载 | 模块不可用 |
| 口名重复、类型标识重复、显示源/输入/命令重名 | 自检 `Problems` / 加载 Issue | 后者被跳过 |
| 口引用了本模块未声明的类型 | 自检 `Problems` / 加载 Warning | 该口回退为只按形状判定 |
| 口形状与类型形状不同 | 自检 `Problems` / 加载 Error | 该口不可用 |
| 同一标识在两个模块里形状不同 | 全场对账 | 一条类型冲突 Issue，连接失败 |
| 角色引用不存在的口/类型、口属于多个角色、两侧不同型 | 自检 `Problems` / 加载忽略 | 该角色不生效 |
| 内部顺序引用不存在的角色、自环、重复 | 自检 `Problems` | 该条被忽略 |
| 外部接入名重复 / 标准未知 / 单元上限非法 | 自检 `Problems` / 加载 Issue | 该接入不接线 |
| 配置类型缺无参构造（`*Config` / `*Configuration`） | 自检 `Problems` | 配置读写失败 |

## 自检

```csharp
var probe = ModuleContract.Probe(typeof(MyModule));

probe.Problems.ShouldBeEmpty();
probe.Advisories.ShouldBeEmpty();   // 逐条确认后可以保留
```

声明外部接入时，`Probe` 默认只做结构校验（接入名、单元上限、标准标识非空）；标准标识是否被框架认识，由产品加载时校验。`catalog` 参数只对框架自己的组合级测试有意义，外部作者不需要传。

`Problems` 空 = 合法；`Advisories` 不影响合法性。**自检只证明本模块自洽，不证明与内置对端兼容**；与既有约定交换数据时还要按[完成判据](index.md)与内置模块建网实测。完整测试面见[测试与验证](testing.md)。

## 相关

- [接口参考](reference.md)：成员签名与默认值
- [模块结构](anatomy.md)：声明面在七层中的位置
- [数据面](../concepts/data-plane.md)：类型约定的设计与校验机制
- [波形帧约定](../concepts/waveform-frame.md)：`WaveformFrame` 的公开契约与字段级约束归属
- [实现](implementation.md)：声明之后怎么写收发与状态
