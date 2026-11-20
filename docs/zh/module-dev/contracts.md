# 声明面

声明是模块与框架的契约：模块说“我有哪些类型、口、界面通道”，框架据此建缓冲、校验连接、生成表单。**类型/角色/界面通道**在 `OnRegistered` 里声明一次；**端口**在视图 XAML 的 `ModuleView.Ports` 里声明。框架在加载期读取一次并转成不可变元数据，运行期不再重读。

成员签名与默认值见[接口参考](reference.md)；本页讲**怎么写才合法、写错会怎样**。

## 声明顺序

1. 数据类型（`OnRegistered`）
2. 端口（视图 XAML 的 `ModuleView.Ports`）
3. 角色与内部处理顺序（`OnRegistered`，可选）
4. 显示源 / 输入 / 命令（`OnRegistered`）
5. 外部接入（`OnRegistered`，可选）

类型先有，口才能按 `TypeId` 引用；口先有，角色才能按名字引用。顺序不是框架强制，但按这个顺序读代码最清楚。

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

端口是**视图声明**：写在模块视图 XAML 的 `ModuleView.Ports` 里；数据类型与角色仍在 `OnRegistered` 声明。

```xml
<ui:ModuleView ...>
  <ui:ModuleView.Ports>
    <ui:ModulePort Name="{x:Static local:MyModule.OutPort}"
                   Direction="Send"
                   TypeId="{x:Static local:MyModule.TypeId}"
                   Edge="Bottom" />
  </ui:ModuleView.Ports>

  <Canvas>...</Canvas>
</ui:ModuleView>
```

| 项 | 规则 |
|---|---|
| 名称 | 模块内唯一且稳定；连接锚定为 `实例ID.口名`。多同型口用 `rx1`/`rx2`；用 `{x:Static}` 指向模块常量 |
| 方向 | `Send` / `Receive`；双向 = 两个口 + 两条单向网络 |
| 类型引用 | `TypeId` 必须在本模块 `DeclareDataType` 声明过；形状与传递语义从该类型继承，XAML 不重复写 |
| 默认边 | `Auto`（发送右、接收左）、`Left/Right/Top/Bottom`；用户拖动优先 |
| 锚点 | `Offset` 0–1（默认 0.5）；`AnchorDraggable` 默认 `false`（框架固定项） |
| 可选语义覆盖 | `Semantics="Stream/Message/Event"`；不写则取类型语义 |

**几何惯例**：宽大于高的模块把**接收口放在上边（`Top`）、发送口放在下边（`Bottom`）**，与内置发送器 / 监视器 / 波形模块的分边一致。这样两个窗口上下叠放时端口正好贴合，松开即可自动建网；`Auto` 的"发送右、接收左"只在左右并排摆放时才有意义。分边只影响呈现与叠放吸附，不参与兼容判定；跨窗口仍可用命名网络连接。

**加载链**：构建把 `Views/**/*.axaml` 随包复制到 `views/`；Hosting 用 `PortSpecReader` 从同一份 XAML 源解析端口，再按 `TypeId` 解析成运行时 `PortDeclaration`。预览器直接读 `ModuleView.Ports`，两边同源。

**旧模块兼容**：没有视图端口声明的模块仍可调用 `DeclarePort(PortDeclaration)`；**不要**同时用 XAML 与 C# 声明（会报“双声明”警告，且以 XAML 为准）。

常见错误：

- 口引用了未在本模块声明的 `TypeId`：加载期报错并跳过该口；
- 同一模块两个口同名：后一个被拒并报 Issue；
- XAML 里写 `Name`/`TypeId` 字面量：可以加载，但架构约定要求用 `{x:Static}` 指向模块常量，避免两处漂移。

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

## 值

```csharp
context.Declaration.DeclareValue(new ValueDeclaration("header")
{
    Editor = ValueEditor.Text,
    Persistent = true,
    ResetWithModule = true,
});
```

- 值名模块内唯一；视图按这个名字绑定，模块按这个名字 `context.Values.ReadText` 读。
- 没有“配置值/输入值”分类；持久化、重置是值声明上的正交属性；视图可见值的初值只写在 XAML。
- 配置字段自动是同名的值；视图可以直接用 `Value` 绑定，模块也可以用 `Values` 按名字读。
- 详见[值](values.md)。

## 动作输入

```csharp
context.Declaration.DeclareInput("savePath");
```

- 动作/事件输入（文件选择结果、结构动作、一次性消息）用 `DeclareInput`；它们从收件箱取，不是可编辑的值。
- 有动作输入但没有界面元素是合法的：文件选择只把路径投回一个看不见的输入源。
- 未登记的输入名由框架拒收并报 Issue。

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
| 参数类型 | 框架按标准登记；模块不引用它，但视图可以用标准输入控件（文本/数字 `ModuleTextSurface`、枚举 `ModuleSelect`、布尔 `ModuleToggle`）绑定它的字段名 |
| 动态选项 | 本机串口、本机网卡地址等 `IConfigurationChoices` 字段用 `ModuleSelect`；没有可选项时下拉为空，不退回文本框 |
| 表单文案 | 参数表单的标题/说明/分组/枚举显示由**本模块**的文本表解析，需提供对应的 `config.*` 键 |

外部接入只搬原始字节：一次读到的是一块字节，不是完整报文；需要报文边界时由模块自己组帧。机制见[外部接入](../concepts/access.md)，用法见[实现](implementation.md)。

## 写错会怎样

| 检查 | 在哪报 | 后果 |
|---|---|---|
| 入口缺公共无参构造 / 未实现 `IModule` | 自检 `Problems` / 加载 | 模块不可用 |
| 口名重复、类型标识重复、显示源/输入/命令重名 | 自检 `Problems` / 加载 Issue | 后者被跳过 |
| XAML 端口引用了本模块未声明的 `TypeId` | 自检 `Problems` / 加载 Error | 跳过该口（引用它的角色随后报错） |
| 旧模块 C# 口的形状与类型形状不同 | 自检 `Problems` / 加载 Error | 该口不可用 |
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
