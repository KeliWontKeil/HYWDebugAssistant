# 契约声明

声明写在模块的 `OnRegistered` 里，**只写一次**：框架在实例化时读一次，运行期不重读。

## 声明 API 一览

| 方法 | 声明什么 | 必填 | 关键约束 |
|---|---|---|---|
| `DeclareDataType(DataTypeDeclaration)` | 数据类型：标识、形状、传递语义、计量块、解码器 | 用类型时必填 | 标识模块内唯一；口形状必须与类型形状一致 |
| `DeclarePort(PortDeclaration)` | 端口：名称、方向、形状、语义、默认边 | 有数据流时必填 | 名称模块内唯一且稳定；默认边只影响初始位置 |
| `DeclareRole(RoleDeclaration)` | 功能分块：一种类型 + 输入口/输出口集合 | 有数据口时建议 | 一个口最多属于一个角色；两侧同型 |
| `DeclareFlow(FlowDeclaration)` | 角色之间的处理先后 | 可选 | 只呈现，不参与建网与投递 |
| `DeclareDisplaySource(DisplaySourceDeclaration)` | 界面可读的数据通道：名称、类别、值类型 | 有界面时按需 | 名称模块内唯一；类别决定缓冲形态 |
| `DeclareInput(InputDeclaration)` | 用户提交值的通道：名称、是否持久化 | 有输入时必填 | 名称模块内唯一；无界面元素的输入也要登记 |
| `DeclareCommand(name, title)` | 按钮触发的动作名 | 有按钮时必填 | 名称模块内唯一；未登记的名字由框架拒收 |
| `DeclareExternalAccess(ExternalAccessDeclaration)` | 设备接入：接入名 + 标准标识 | 设备接入时必填 | 标准标识必须是框架已提供的值 |

每一层在整体结构中的位置见[模块结构](anatomy.md)。

## 顺序

1. 数据类型
2. 端口
3. 角色与内部处理顺序（可选）
4. 显示源 / 输入 / 命令（界面契约）
5. 外部接入（可选）

## 数据类型

| 项 | 说明 |
|---|---|
| 标识 | 模块自己的约定名，建议带作者前缀；跨模块同名即同一类型 |
| 形状 | 定长：原语、定长字节块、定长数组、结构；尺寸由形状决定 |
| 传递语义 | 流 / 消息 / 事件；跨语义不判兼容 |
| 计量块 | 可选：有效载荷字节数、有效长度字段、每单元样本数、通道数、标称速率；只用于观测 |
| 解码器 | 可选：呈现期把样本解成数值；不提供则界面显示字节数 |

约束：

- 标识在模块内唯一；口引用的标识必须在本模块声明过。
- **端口形状必须与所引用类型的形状完全相同**（传输按口形状、计量按类型形状）。
- 同一标识在两个模块里必须是同一形状，否则加载期报冲突。

## 声明一个新类型

类型是**模块自己的代码**，不是框架配置。字段怎么排、长度怎么定、怎么演进、指纹怎么留，见[数据面](../concepts/data-plane.md)的「设计一条约定」；本页只给写法。

```csharp
public const string TypeId = "vendor/pulse";
public static readonly ContractShape Shape = ContractShape.OfStruct(
    ("seq", ContractShape.OfPrimitive(ContractPrimitive.U64)),
    ("value", ContractShape.OfPrimitive(ContractPrimitive.F32)));

context.Declaration.DeclareDataType(new DataTypeDeclaration(TypeId, Shape)
{
    Metering = new MeteringBlock { SamplesPerUnit = 1, ChannelCount = 1 },
});
```

然后把端口指向它，并让口形状**与它完全相同**：

```csharp
context.Declaration.DeclarePort(new PortDeclaration("out", PortDirection.Send, Shape)
{
    TypeId = TypeId,
});
```

角色引用同一个标识（一个角色只有一种类型，输入/输出两侧同型）。

## 端口

| 项 | 说明 |
|---|---|
| 名称 | 模块内唯一、稳定；连接锚定为 `实例ID.口名` |
| 方向 | 发送 / 接收两态；双向 = 两个口 + 两条单向网络 |
| 形状 | 与所引用类型一致；不声明类型时只按形状判定兼容 |
| 默认边 | 发送口默认右、接收口默认左；可声明其它边，用户拖动优先 |

## 角色与内部处理顺序

- 一个角色 = 一种数据类型 + 输入口集合 + 输出口集合；输入输出**两侧同型**。
- 一个口最多属于一个角色；口数不设上限（多入一出是合法形态）。
- 内部处理顺序只表达"角色 → 角色"的先后，**只呈现**，不参与建网/兼容/投递。
- 有数据口但零角色只给**建议**（不进问题清单）。

## 显示源 / 输入 / 命令

| 项 | 说明 |
|---|---|
| 显示源 | 模块愿意给界面读的数据通道；名字是唯一键，重复即拒绝后者 |
| 输入源 | 用户把值提交给模块的通道名；未登记的名字由框架拒收并报 Issue |
| 命令 | 按钮触发的动作名；未登记的命令名由框架拒收并报 Issue |

显示源的类别决定缓冲形态：`Value` 是最新值槽，其余（序列 / 行集 / 字节块 / 文本 / 帧）是环形缓冲。数值型源声明值类型（契约原语）。

## 外部接入

只声明两件事：**接入名**（模块内唯一）与**标准标识**（串口 / TCP / UDP）。可选单元上限。

**不声明数据类型、不组帧**：接入面只交原始字节。适配器、参数类型与会话都在框架侧；模块只发开/关请求并读写字节。

## 命名

- 建议加作者前缀（如 `vendor/…`），跨模块同名即视为同一类型。
- 未加前缀的名字（如 `RawBytes`）是历史约定，新类型不要照抄这种命名。

## 自检

`Sdk.Testing` 的 `ModuleContract.Probe` 会跑同样的规则：

- `Problems`：必须为空（空 = 合法）。
- `Advisories`：建议（例如"有口零角色"），逐条确认即可，不影响合法性。

详见[测试与验证](testing.md)。

## 相关

- [模块结构](anatomy.md)：声明在整体结构中的位置
- [数据面](../concepts/data-plane.md)
- [外部接入](../concepts/access.md)
- [测试与验证](testing.md)
