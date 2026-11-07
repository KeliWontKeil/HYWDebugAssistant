# 实现

实现一个模块 = 实现接口，并在钩子里做该做的事。线程、队列与调度由框架负责；模块只写"这一步做什么"。

## 何时被调用

| 位置 | 线程 | 该做什么 |
|---|---|---|
| `OnRegistered` | 实例创建方（管理线程） | 声明类型、端口、角色、界面契约与接入 |
| `OnActivated` | 实例线程 | 启动定时器、发布初始状态 |
| `OnStep` | 实例线程 | 处理本拍合并后的原因（数据 / 会话 / 命令 / 输入） |
| 接收回调 | 实例线程 | 读载荷、发数据、更新显示 |
| 定时器 `Tick` | 实例线程 | 周期动作 |
| `OnDisabled` / `OnEnabled` | 实例线程 | 暂停或恢复自己的业务状态（定时器由框架暂停） |
| `OnConfigurationChanged` | 实例线程 | 读取新配置、调整运行参数 |
| `OnRemoved` | 实例线程停止前 | 释放句柄、文件、缓冲等模块内部资源 |

三条纪律：不假定注册期已经有数据在流动；任何回调都必须尽快返回（不得阻塞、不得等待）；不自建线程、不自建系统定时器。完整的线程与队列机制见[运行期](../concepts/runtime.md)。

## 发数据

```csharp
// 布局在类型旁边算一次，运行期只做偏移寻址。
private static readonly ContractLayout Layout = ContractLayout.FromShape(Shape);

Span<byte> unit = stackalloc byte[Shape.SizeInBytes];
var writer = new PayloadWriter(unit, Layout);
writer.WriteU32("value", value);
var accepted = context.Ports.TrySend("out", unit);
```

- 载荷必须是**定长**、长度等于发送口契约尺寸；短了被丢弃（记 `载荷非法`），长了会被截断。
- 返回 `false` 表示没有送达（队列满、口不存在或实例停止）；是否重试由模块决定，框架不做背压。
- 写字段用 `PayloadWriter`（按契约布局寻址）；单原语类型直接用 `BinaryPrimitives` 也可以。

## 收数据

有两种消费方式，**一个模块统一用一种**（由 `IActivitySource.Receive` 决定）：

| 方式 | 写法 | 适用 |
|---|---|---|
| Push（默认） | `context.Ports.SetReceiveHandler("in", handler)`，框架在每个单元到达时回调 | 简单、无周期 |
| Pull | `ReceiveMode.Pull` + 在 `OnStep` 里 `TryReceive` / `ReleaseBuffer` | 需要自己攒批、控节奏 |

```csharp
private void OnData(ReadOnlySpan<byte> payload)
{
    var value = BinaryPrimitives.ReadUInt32LittleEndian(payload);
    _total += value;
    PublishTotal(context);
}
```

回调里的载荷只在本次调用有效；需要留存时必须自己拷贝。Pull 模式取到的缓冲必须显式归还。

## 配置

配置是**实例**的属性，随组合件保存，读取得到不可变快照：

```csharp
public sealed class MyConfig
{
    [ConfigurationField(Title = "目标地址", Description = "主机名或 IP")]
    public string Address { get; set; } = "127.0.0.1";
}

var config = context.Configuration.Read<MyConfig>();
```

- 第一次 `Read<T>` 会把默认值登记为该实例的配置；只读不登记的模块保存组合件时会丢配置。
- 写回用 `context.Configuration.Write<T>(...)`；框架随后调用 `OnConfigurationChanged`。
- 字段标题、说明、分组、单位可用 `TitleKey` / `DescriptionKey` / `GroupKey` 指向模块文本表。
- 枚举选项用 `[ConfigurationChoice]` 标注；动态选项（例如可用端口列表）实现 `IConfigurationChoices`。

## 上报

三条通道边界不同，不要混用：

| 通道 | 用途 | 写法 |
|---|---|---|
| Issue | 需要用户处理的问题（契约错误、打开失败、配置非法） | `context.Issues.Report(new IssueReport(IssueCategory.RuntimeError, IssueSeverity.Error, "…"))` |
| 业务状态 | 模块自己的状态行（一句话） | `context.State.Update(new BusinessState("key", "value", "显示文本"))` |
| 日志 | 诊断记录，按级别过滤 | `context.Log.Log(ModuleLogLevel.Information, "…")` |

业务状态不是 Issue：不要把"设备没插"这类可预期状态报成错误。

## 显示

显示源在 `OnRegistered` 声明，写入后由界面按刷新节拍读取：

| 方法 | 作用 |
|---|---|
| `Display.AppendText(源名, 文本)` | 追加一段（不换行） |
| `Display.AppendLine(源名, 文本)` | 追加一行 |
| `Display.SetText(源名, 文本)` | 整体替换（适合一行状态） |
| `Display.Clear(源名)` | 清空 |
| `Display.IsFull(源名)` | 是否已到容量上限（满了会丢最旧） |

源类别决定缓冲形态：`Value` 是最新值槽，`Sequence` / `RowSet` / `ByteBlock` / `Text` / `Frame` 是环形缓冲。数值源用 `Ports.Publish` 写小端字节；文本源用上面的方法。

```csharp
context.Display.SetText("state", "就绪");
Span<byte> counter = stackalloc byte[sizeof(ulong)];
BinaryPrimitives.WriteUInt64LittleEndian(counter, _total);
context.Ports.Publish("count", counter);
```

## 外观

外观是基础能力，不含语义：框架不认识"红 = 失败"。

```csharp
context.Appearance.SetOutlineRole("state", "danger");   // 相对色，随主题与用户改色
context.Appearance.SetOutlineColor("state", 0xFFE53935); // 绝对色，刻意固定
context.Appearance.SetReadOnly("threshold", true);
context.Appearance.ResetInteraction("continuous");
```

优先用**角色词**（`danger` / `warning` / `muted` 等），它跟随主题；绝对色只在确有需要时使用。

## 收件箱

用户点按钮、填输入框，都会被投进收件箱，由模块在实例线程消费：

```csharp
while (context.Inbox.TryDequeueCommand(out var command))
{
    if (command == ResetCommand) { Reset(context); }
}

while (context.Inbox.TryDequeueInput(out var source, out var text))
{
    ApplyInput(context, source, text);
}
```

- 只有 `OnRegistered` 里声明过的命令名与输入源名才会被投递；未登记的名字由框架拒收并报 Issue。
- 收件箱有界，满时丢最旧并计数；同一命令重复点击会重复入队，需要幂等就自己做。
- 不实现 `IActivitySource` 的模块**收不到**命令与输入：没有单步就没有消费时机。

## 定时器

```csharp
_timer = context.Timers.Create();
_timer.Interval = TimeSpan.FromMilliseconds(100);
_timer.Tick += (_, _) => Publish(context);
_timer.Start();
```

第一个 Tick 在一个间隔之后；Tick 在实例线程触发，与 `OnStep` 串行；实例禁用时框架暂停定时器，移除时统一释放。模块自建定时器是错的——周期只有这一个入口。完整规则见[自发活动与定时器](activity.md)。

## 视图状态

模块可以读用户此刻在看什么，但不能反向操作界面：

```csharp
var page = context.ViewState.GetSelectedPage(PagesKey);
var text = context.ViewState.GetInputText(TextSource);
```

回写只改变"视图状态"（例如用户正在编辑但尚未提交的文本），不会触发模块逻辑。

## 文件

模块只负责"请用户指一个路径"，读写由模块自己做（异常就地反馈）：

```csharp
context.Files.Request(new FilePickRequest("savePath", FilePickMode.Save, "文本文件 (*.txt)|*.txt"));
```

选定的路径会经**输入源** `savePath` 投回——所以它必须在 `OnRegistered` 里登记为输入源，且该输入可以没有界面元素。

## 资源

```csharp
if (context.Resources.TryOpen("templates", "default.txt", out var stream))
{
    using (stream) { /* 只读、相对、不越界 */ }
}
```

`i18n` 与 `icons` 是框架认识的两种类别，由框架按拥有者读取；其它类别由模块自己读。见[资源与文本](resources.md)。

## 外部接入

设备接入走框架托管的会话：模块只声明标准、发开/关请求、读写字节。

```csharp
// 声明（OnRegistered）
context.Declaration.DeclareExternalAccess(
    new ExternalAccessDeclaration("my-serial", ExternalAccessStandardIds.Serial) { MaxUnitBytes = 1472 });

// 请求（命令里）
context.Access.RequestOpen("my-serial");

// 收发（Push）
context.Access.SetReceiveHandler("my-serial", OnRawBytes);
context.Access.Write("my-serial", payload);
```

- 声明只写"接入名 + 标准标识"；适配器、参数类型与会话都在框架侧，模块不引用它们。
- 两种消费方式二选一：Push 设回调；Pull 在 `OnStep(Session)` 里 `TryRead`。
- 一次读到的是"一块字节"，不是完整报文；需要报文语义时自己组帧。见[外部接入](../concepts/access.md)。
- 状态与计数以 `context.Access.GetSnapshot(name)` 为唯一事实源；`ClearCounters` 只清计数。

## 异常与停止

- 钩子与回调里的异常不得逸出；框架会捕获并转成 Issue，但行为已经不可预期。
- 定时器 Tick 抛异常时，框架停掉该定时器并报一次 Issue，不会每拍刷屏。
- 停止是"置标志 + 有界等待"：模块必须周期性检查停止请求并尽快返回；不自建线程就没有强杀问题。

## 相关

- [声明面](contracts.md)：每个 `Declare*` 的参数与约束
- [自发活动与定时器](activity.md)：调度选择的完整规则
- [测试与验证](testing.md)：这些面怎么写测试
