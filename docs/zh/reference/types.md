# 数据类型约定

数据类型是**模块之间的通信约定**：由开发者与文档定义，模块各自在自己的代码里重现；框架不定义、不预设任何类型，只比较两侧声明是否一致。

## 在用的约定

| 约定 | 标识 | 形状 | 尺寸 |
|---|---|---|---|
| 原始字节 | `RawBytes` | `{ len: u16, data: bytes[1472] }` | 1474 B |

`RawBytes` 的语义：

- `len` 是**有效字节数**（0–1472）；`data` 尾部补零只是定长填充，`0x00` 是合法数据、不是终止符；
- 接收方按 `len` 取有效内容，**不得按 NUL 截断**；
- 计量块声明 `PayloadLengthField = "len"` ⇒ 有效字节速率按真实有效数据计算；
- 使用者：文本发送器、HEX 发送器、数据监视器、四个外部接入模块——它们**各自声明**同一条约定，才能开箱即连。

## 声明一个新类型

类型是**模块自己的代码**，不是框架配置：

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

## 兼容判定

逐（源，宿）对判定，顺序固定：

1. 传递语义一致（流 / 消息 / 事件）；
2. 形状完全一致（字段名、原语、长度、顺序、数量全同）；
3. 两侧都声明类型时，标识相同；
4. 任一侧未声明类型时只按第 2 条判定。

没有强制连接的放行路径；框架不做自动转换。

## 一致性判据

- **端口形状必须等于所引用类型的形状**：传输按口形状算布局、计量块按类型形状算偏移，不一致会在加载期报错。
- **同一标识必须是同一种形状**：两个模块声明了同标识不同形状时，加载期报一条冲突（标识 + 两种形状 + 实例）。
- 改形状 = 破坏性变更：类型没有版本，两侧必须同步改。

## 命名

- 建议加作者前缀（如 `vendor/…`），跨模块同名即视为同一类型。
- 未加前缀的名字（如 `RawBytes`）是历史约定，新类型不要照抄这种命名。
