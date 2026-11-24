# 接口参考

本页是模块能看到的框架接口的完整清单：每个类型、成员、默认值与失败方式。写法与步骤在其它页，这里只回答“有哪些、叫什么、什么时候能用”。

## 入口与生命周期

### 模块声明特性

```csharp
[ModuleDefinition("vendor", "my-module",
    DisplayNameKey = MyText.Title,
    DescriptionKey = MyText.Description,
    Tags = ["template"],
    Capabilities = ModuleCapability.File,
    ViewFactory = typeof(Views.MyViewFactory))]
public sealed class MyModule : IModule { }
```

| 成员 | 说明 |
|---|---|
| `Id`（构造参数） | `作者/模块名`：两段均为 `[a-z0-9-]`，不能为空、不能以 `-` 开头或结尾；必须与 `module.json` 的 `id` 一致 |
| `DisplayName` / `DisplayNameKey` | 目录展示名；键命中时优先于字面量，键在本模块文本表中 |
| `Description` / `DescriptionKey` | 目录描述；规则同上 |
| `Tags` | 仅供检索与模块清单的分类筛选，不构成框架业务分类。分类标签为 `port` / `source` / `terminal` / `converter` / `other`；旧标签（`sender`、`monitor`、`protocol` 等）仍兼容 |
| `Capabilities` | `Network` / `SerialPort` / `File` / `Process`，按位组合；加载时透明提示，不拦截（按实际访问的资源勾选） |
| `DependsOn` | 依赖的模块标识；缺失时该模块不进入可加入列表并报 Issue |
| `ParticipatesInDataFlow` | 是否参与数据流；`false` 用于纯界面/工具模块 |
| `ViewFactory` | 视图工厂类型（实现 `IModuleViewFactory`，公共无参构造）；不声明 = 无自绘界面 |
| `WindowWidth/Height`、`MinWidth/Height` | **已废弃**，框架不读；尺寸只写在视图根 XAML |

入口类型必须实现 `IModule`、是公开非抽象类、有公共无参构造函数。

### `IModule` 的六个钩子

| 钩子 | 线程 | 何时调用 | 该做什么 |
|---|---|---|---|
| `OnRegistered(IModuleContext)` | 创建方（管理线程；实例线程尚未启动） | 实例创建、成员实例化完成后，一次 | 全部 `Declare*`、创建定时器、设接收回调、记录上下文 |
| `OnActivated()` | 实例线程 | 进入 `Active` 后 | 发布初始状态、启动需要“运行中”才做的动作 |
| `OnDisabled()` | 实例线程 | 进入 `Disabled`（实例保留、数据流暂停） | 模块自己的暂停处置；定时器由框架暂停 |
| `OnEnabled()` | 实例线程 | `Disabled → Active` | 恢复自己的活动 |
| `OnConfigurationChanged()` | 实例线程 | 配置经变更路径写回后 | 重新读取配置、调整运行参数 |
| `OnRemoved()` | 实例线程停止前 | 移除前 | 释放模块自己的句柄/文件/缓冲 |

纪律：钩子里的异常不得逸出（框架会捕获并转 Issue，但状态已不可预期）；`OnRegistered` 里不得假定数据已经在流动；除 `OnRegistered` 外都在实例线程，与 `OnStep`、接收回调、定时器 Tick 同线程、不并发。

### 可选活动接口

```csharp
public interface IActivitySource
{
    ReceiveMode Receive => ReceiveMode.Push;
    void OnStep(IModuleContext context, StepReason reason);
}

[Flags] public enum StepReason
{
    Start = 1, Data = 2, Session = 4, Command = 8, Input = 16,
}
```

| 成员 | 说明 |
|---|---|
| `Receive` | `Push`（默认）：端口/接入数据由框架回调；`Pull`：数据留在队列，在 `OnStep` 自取。一个模块只能选一种 |
| `OnStep` | 每拍调用一次；`reason` 可多位合并（例如同一拍既有命令又有数据） |

不实现 `IActivitySource` 的模块**收不到命令与输入**；端口数据仍可由 `SetReceiveHandler` 投递。

## 模块上下文

`IModuleContext` 是模块访问框架的唯一入口，每个实例一份，生命期与实例相同。不持有其它模块实例、不反向查询其它模块。

| 属性 | 类型 | 可用时机与要点 |
|---|---|---|
| `ModuleId` | `ModuleId` | 全程只读 |
| `Declaration` | `IModuleDeclaration` | **仅 `OnRegistered` 期间**使用；运行期不要再用 |
| `Ports` | `IPortAccess` | 收发端口、发布显示值、读口压力、上报模块内部丢弃 |
| `Access` | `IExternalAccess` | 外部接入；未接线时为空实现（请求被忽略、读无数据、写失败，不抛） |
| `Configuration` | `IConfigurationAccess` | 读写实例配置；`Read<T>` 首次调用即登记默认值；控件值请走 `Values` |
| `Values` | `IModuleValues` | 按值名读当前值、接受规范化值、报告字段结论；不区分配置/输入 |
| `Issues` | `IIssueReporter` | 需要用户处理的问题；不抛 |
| `Log` | `IModuleLogger` | 诊断日志；先 `IsEnabled` 再构造昂贵参数 |
| `State` | `IBusinessStateReporter` | 业务状态行；不是 Issue |
| `Appearance` | `IViewAppearanceAccess` | 元素边框/文字/字号/只读/可用/交互复原；不抛 |
| `Inbox` | `IModuleInbox` | 命令与输入；只在 `OnStep` 消费 |
| `Timers` | `IModuleTimers` | 创建实例定时器；未接线时能创建但永不触发 |
| `Display` | `IViewDisplayAccess` | 追加/替换/清空显示源、发布序列窗口快照 |
| `ViewState` | `IViewStateReader` | 只读视图事实（选中页、输入框文本、显示缓冲）；可写回输入值 |
| `Files` | `IFileAccess` | 请求系统文件/目录选择器；选定路径经输入源投回 |
| `Resources` | `IResourceAccess` | 读本模块 `resources/<kind>/` 下的自定义资源 |

## 声明面

全部声明只在 `OnRegistered` 中调用一次，框架随后只读一次并转成不可变元数据。

| 方法 | 声明什么 | 关键约束 |
|---|---|---|
| `DeclareDataType(DataTypeDeclaration)` | 类型：标识、形状、语义、计量块、解码器、语义标签 | 标识模块内唯一；口形状必须与类型形状完全一致 |
| `DeclarePort(PortDeclaration)` | **旧模块兼容**：端口：名称、方向、形状、语义、默认边、锚点 | 新模块改用视图 XAML 的 `ModuleView.Ports`；不要与 XAML 双声明 |
| `DeclareRole(RoleDeclaration)` | 功能分块：一种类型 + 输入口/输出口集合 | 一个口最多属于一个角色；两侧同型；未声明时只给建议 |
| `DeclareFlow(FlowDeclaration)` | 角色之间的内部处理顺序 | 两端角色必须已声明；不得自环 |
| `DeclareDisplaySource(DisplaySourceDeclaration)` | 界面可读的数据通道：名称、类别、值类型、单位 | 名称模块内唯一 |
| `DeclareValue(ValueDeclaration)` | 视图可编辑值：编辑器、可选项、持久化/重置属性；初值写在 XAML | 名称模块内唯一 |
| `DeclareInput(string)` / `DeclareInput(InputDeclaration)` | 动作/事件输入源名（文件结果、结构动作等） | 名称模块内唯一 |
| `DeclareCommand(string name, string title)` | 按钮命令 | 名称模块内唯一；`title` 只用于诊断文案 |
| `DeclareExternalAccess(ExternalAccessDeclaration)` | 外部接入：接入名 + 标准标识 + 单元上限 | 接入名模块内唯一；标准标识必须是框架认识的值 |

### 数据类型

`DataTypeDeclaration` 的构造参数是 `(typeId, shape, semantics = Stream)`，可选 `init` 属性：

| 成员 | 默认 | 说明 |
|---|---|---|
| `Metering` | 全默认 | 有效载荷字节数、有效长度字段名、每单元样本数、每通道样本数、标称速率；只用于观测 |
| `Decoder` | `null` | `SampleDecoder`：把一个定长样本解成一个 `double`；不提供则界面显示字节数 |
| `SemanticTags` | 空 | 单位/量程/精度等可读注记；**只呈现** |

形状原语：`I8/I16/I32/I64/U8/U16/U32/U64/F32/F64/Bool`，字节数分别为 1/2/4/8 的对应宽度，`Bool` 占 1 字节；另外支持定长字节块（1–4096 字节）、元素为原语且长度 1–256 的定长数组、结构组合（嵌套深度 ≤ 4、字段名唯一）。全部定长、小端、无填充。

### 端口

新模块在视图 XAML 里声明端口（数据类型/角色仍在 `OnRegistered`）：

```xml
<ui:ModuleView.Ports>
  <ui:ModulePort Name="{x:Static local:MyModule.InPort}"
                 Direction="Receive"
                 TypeId="{x:Static local:MyModule.TypeId}"
                 Edge="Left"
                 Offset="0.5"
                 AnchorDraggable="False" />
</ui:ModuleView.Ports>
```

| 成员 | 默认 | 说明 |
|---|---|---|
| `Name` | — | 模块内唯一、稳定；连接锚定为 `实例ID.口名`；用 `{x:Static}` 指向模块常量 |
| `Direction` | — | `Send` / `Receive`；双向 = 两个口 + 两条网络 |
| `TypeId` | — | 必须是本模块 `DeclareDataType` 声明过的标识；形状/语义从该类型继承 |
| `Semantics` | 类型语义 | 可选覆盖；`Stream` / `Message` / `Event`，跨语义不判兼容 |
| `Edge` | `Auto` | `Auto` 发送在右、接收在左；也可 `Left/Right/Top/Bottom`；用户拖动优先 |
| `Offset` | `0.5` | 在该边上的默认位置（0–1） |
| `AnchorDraggable` | `false` | 默认固定；`true` 允许用户拖动标记 |

**兼容入口**：没有视图端口声明的旧模块仍可用 `DeclarePort(PortDeclaration)`；内置模块与模板已全部迁到 XAML，架构门禁禁止在 `src/Modules` 再出现 `DeclarePort(`。

### 角色与内部顺序

```csharp
RoleDeclaration.Output(roleId, typeId, "out", "生成");
RoleDeclaration.Input(roleId, typeId, "in", "显示");
RoleDeclaration.Both(roleId, typeId, "in", "out", "转换");
new FlowDeclaration("convert", "output", "经过限幅");
```

角色只用于呈现：不参与建网、兼容、停流、投递与计量。角色两侧必须承载同一种类型；类型转换要拆成两个角色 + `FlowDeclaration`。口数不设上限。

### 显示源

```csharp
new DisplaySourceDeclaration("count", ViewDataSourceKind.Value, ContractPrimitive.U64);
```

| 类别 | 形态 | 写入方式 |
|---|---|---|
| `Value` | 最新值槽（容量 1） | `Ports.Publish(name, bytes)` |
| `Sequence` | 环形缓冲（512 个样本） | `Ports.Publish` |
| `RowSet` / `ByteBlock` / `Text` / `Frame` | 环形缓冲（512 个样本） | `Ports.Publish` 或文本面方法 |
| `SeriesWindow` | 不可变整窗快照，无容量上限 | `Display.SetSeriesWindow(name, snapshot)` |

`Text` 有两条写入路径：`Display.Append*`/`SetText` 走文本缓冲（默认 20000 行，应用设置可调）；`Ports.Publish` 走 512 样本环形缓冲。两者不是同一块缓冲。

用 `Value`/`Sequence` 时声明 `ValueType`（如 `U64`），模块按该原语的字节数写小端值。第一次写入决定槽宽；之后更长的样本会被丢弃并计数，空样本也会被丢弃。

### 输入、命令与外部接入

| 类型 | 要点 |
|---|---|
| `new ValueDeclaration(name) { ... }` | 视图可编辑值；`Editor` 选编辑器，`Persistent` / `ResetWithModule` 是正交属性，初值写在 XAML；文本准入格式写在控件 `TextFormat`；详见[值](values.md) |
| `new InputDeclaration(name) { Persistence = InputPersistence.Persistent }` | 动作/事件输入；持久化仅对这类事件保留旧语义 |
| `DeclareCommand(name, title)` | 未登记的命令名被框架拒收并报 Issue |
| `new ExternalAccessDeclaration(name, standardId) { MaxUnitBytes = 1472 }` | 适配器、参数类型与会话都在框架侧；上限超过框架校验值时该接入被拒 |

## 数据收发

### `IPortAccess`

| 成员 | 语义 |
|---|---|
| `TrySend(port, payload)` | 发送一个单元；返回 `false` = 已丢弃（队列满/停流/口不存在）。短于契约尺寸被丢弃并记“载荷非法”；长于契约尺寸被截断 |
| `SetReceiveHandler(port, handler)` | Push：每个单元到达时在实例线程回调；载荷只在本次调用有效 |
| `TryReceive(out PayloadBuffer, out string sourcePort)` | Pull：非阻塞取一个单元（每实例一条入站队列）；必须 `ReleaseBuffer` 归还 |
| `ReleaseBuffer(buffer)` | 归还主动接收的缓冲；归还后不得再访问 |
| `Publish(source, bytes)` | 写显示源；详见“显示源” |
| `TryGetFlowState(port, out PortFlowState)` | 读本实例接收口的队列深度、峰值、丢弃账本、饱和状态；无队列返回 `false` |
| `ReportLoss(port, DropReason.ModuleInternal, units, bytes)` | 上报模块自己的丢弃；只接受 `ModuleInternal`，其它原因静默忽略 |

`PortFlowState` 字段：`Depth`、`Capacity`、`PeakDepth`、`Enqueued`、`DroppedUnits`、`DroppedBytes`、`DroppedWhileSaturatedUnits`、`Saturated`、`SaturatedFor`、`LastDropReason`、`SinceLastDrop`、`Fill`。每口队列容量默认 1024 个单元，溢出按“丢最旧”处理。

### 布局与字节读写

```csharp
private static readonly ContractLayout Layout = ContractLayout.FromShape(Shape);
Span<byte> unit = stackalloc byte[Shape.SizeInBytes];
var writer = new PayloadWriter(unit, Layout);
writer.Clear();
writer.WriteU16("len", (ushort)text.Length);
...
var reader = new PayloadReader(payload, Layout);
var length = reader.ReadU16("len");

// 长度不确定时先安全创建：
if (!PayloadReader.TryCreate(payload, Layout, out var safe)) { return; }
var value = safe.ReadF32("value");
```

| 类型 | 要点 |
|---|---|
| `ContractLayout` | 顶层字段名 → 偏移；`SizeInBytes`；嵌套结构由模块自己解析 |
| `PayloadWriter` | `ref struct`；`Clear`、`WriteU8/I8/U16/I16/U32/I32/U64/I64/F32/F64/Bool/Bytes`；字段名拼错在开发期抛异常 |
| `PayloadReader` | `ref struct`；构造时长度不足会抛，长度不确定时用 `PayloadReader.TryCreate`；读方法 `ReadU8/I8/U16/I16/U32/I32/U64/I64/F32/F64/Bool/Bytes` |
| `PayloadBuffer` | `Length`、`Layout`、`Span`；生命周期仅限回调/主动接收 |

## 配置

配置是**实例**属性，随组合件保存。配置类型必须是公开、非抽象类，有公共无参构造函数；框架按 `class + new()` 约束读写。

```csharp
public sealed class MyConfig
{
    [ConfigurationField("目标地址", Group = "连接", Description = "主机名或 IP")]
    public string Host { get; set; } = "127.0.0.1";

    [ConfigurationField("端口", Minimum = 1, Maximum = 65535)]
    public int Port { get; set; } = 60721;

    [ConfigurationField("模式")]
    public MyMode Mode { get; set; } = MyMode.Fast;
}

var config = context.Configuration.Read<MyConfig>();
context.Configuration.Write(new MyConfig { Host = "10.0.0.2" });
```

| 项 | 规则 |
|---|---|
| 可编辑属性 | `bool`、`string`、整型、`float`/`double`/`decimal`、枚举、以上类型的 `Nullable`；复杂对象/集合不生成字段 |
| 字段名 | 同一实例内必须唯一（标准接入的参数类型也在同一张表单里）；跨两个配置类型重名会写回失败 |
| 标注 | `Title`/`TitleKey`、`Group`/`GroupKey`、`Description`/`DescriptionKey`、`Minimum`/`Maximum`/`Step`、`Unit` |
| 枚举显示 | `[ConfigurationChoice("9600")]` 只决定显示字；写回用成员名 |
| 动态选项 | 配置类型实现 `IConfigurationChoices`；`GetChoices` 返回 `null` = 不是动态字段，空表 = 现在没有选项 |
| 读取即登记 | 第一次 `Read<T>` 把默认值登记为该实例配置；不读不登记，保存组合件时该配置丢失 |
| 写回 | `Write<T>` 用传入的新实例替换实例配置；界面字段写回走 JSON 中转并调用 `OnConfigurationChanged`，模块自己的 `Write` 不会回投该钩子。不要就地修改运行中的对象 |
| 不落盘的类型 | 不要把运行时状态放进配置类型；配置只放用户可编辑的参数 |

声明外部接入后，框架把该标准的参数类型登记为实例配置，模块视图可直接用标准输入控件绑定这些字段名：文本/数字 `ModuleTextSurface`，枚举 `ModuleSelect`，布尔 `ModuleToggle`（见下文“外部接入”）。

## 上报

| 通道 | 类型 | 用途 |
|---|---|---|
| Issue | `Issues.Report(new IssueReport(category, severity, message, objectPath))` | 需要用户处理的问题；`ObjectPath` 可写口名/字段名 |
| 业务状态 | `State.Update(new BusinessState(key, value, displayText))` | 模块状态行；不是 Issue |
| 日志 | `Log.Log(ModuleLogLevel.Information, "…")` | 诊断记录；`Trace`/`Debug` 只在诊断开关打开时写入 |

`IssueCategory`：`ConfigurationError`、`CompositionError`、`InterfaceError`、`ModuleError`、`CommunicationError`、`RuntimeError`、`PresentationError`。`IssueSeverity`：`Info`、`Warning`、`Error`——可预期的外部状态（设备未插、对端关闭）用 `Warning`，不弹窗、不阻断。`ModuleLogLevel`：`Trace`、`Debug`、`Information`、`Warning`、`Error`。

`DropReason` 是唯一的丢弃词表；模块只能经 `ReportLoss` 报 `ModuleInternal`，其它原因由框架记账。

## 显示、视图与外观

### 显示内容

| 成员 | 语义 |
|---|---|
| `Display.AppendText(source, text)` | 只增、不换行；文本里的换行字符原样显示 |
| `Display.AppendLine(source, text)` | 只增、由通道换行（不画换行标记） |
| `Display.SetText(source, text)` | 整体替换；空串等价清空 |
| `Display.Clear(source)` | 清空 |
| `Display.IsFull(source)` | 自上次清空/替换以来是否因容量丢过内容 |
| `Display.SetSeriesWindow(source, snapshot)` | 发布一个不可变整窗快照；发布后不得再改动其中的数组 |

`SeriesWindowSnapshot(Version, XRangeStart, XRangeEnd, LatestIndex, RateHz, YMin, YMax, YAuto, Channels)`，通道为 `SeriesWindowChannel(Index, Name, ColorArgb, Values)`；`Values` 长度就是窗口宽度。`WindowMin/WindowMax` 是窗口内有限样本的最小/最大值，无有效样本时为 `NaN`；它们来自通道自身，不依赖游标。

### 视图状态

| 成员 | 语义 |
|---|---|
| `ViewState.GetSelectedPage(stateKey)` | 页签组当前页（0 基）；未知 ⇒ `null` |
| `Values.ReadText(name)` | 当前有效值（持久化值，或激活前播种的 XAML 初值） |
| `Values.AcceptText(name, text)` | 模块接受/规范化后的值；控件下一帧读到 |
| `Values.ReportValidity(name, validity)` | 报告该值当前的有效性（`ValueValidity`：是否无效 + 原因；`Valid` 收起） |
| `Values.ReadValidity(name)` | 读回该值当前的有效性，模块可据此分类状态 |
| `ViewState.GetDisplayLines(source)` | 文本显示缓冲的全部行（调用时复制） |
| `InputValidity.Set(context, name, invalid, message)` | 输入有效性的唯一记账：写/清 `Danger` 轮廓并写 `ValueValidity`；重复调用不重复写外观 |

### 外观

| 成员 | 语义 |
|---|---|
| `Appearance.SetOutlineColor(source, argb)` / `SetOutlineRole(source, role)` | 边框色；绝对色与角色词互斥，后写生效 |
| `Appearance.SetTextColor(source, argb)` / `SetTextRole(source, role)` | 文字色；规则同上 |
| `Appearance.SetTextSize(source, dips)` | 字号 |
| `Appearance.SetReadOnly(source, bool)` | 运行期只读（能看、能选中、不能改） |
| `Appearance.SetEnabled(source, bool)` | 整格不可操作 |
| `Appearance.ResetInteraction(source)` | 把用户刚做的交互复原（勾选拨回等） |

角色词：`default`、`muted`、`accent`、`accentStrong`、`accentSecondary`、`success`、`warning`、`danger`、`idle`、`directionSend`、`directionReceive`。未知词视同没设。

### 视图工厂与视图上下文

```csharp
public sealed class MyViewFactory : IModuleViewFactory
{
    public Control Create(IModuleViewContext context) => new Views.MyView();
}
```

`Create` 每个实例调用一次；工厂必须无状态、公共无参构造。返回的控件由框架包进 `ModuleViewHost`，并在每帧调用 `Refresh()`（只在窗口可见时）。

视图根建议继承 `ModuleView`（主题与预览支持）。视图的 `DataContext` 是 `IModuleViewContext`（XAML 根写 `x:DataType="contracts:IModuleViewContext"`）。

预览宿主约定：`ModuleView.IsPreviewHost` 由预览宿主设置，模块与产品不设置；`[ModuleViewDesignData]` 只在设计环境生效。预览边界见[界面](ui.md)。

| `IModuleViewContext` 成员 | 用途 |
|---|---|
| `InstanceId` | 实例 ID |
| `ReadLatest(source)` | 最新数值（`Value` 源） |
| `ReadSeries(source)` | 等间隔降采样序列 |
| `ReadSeriesWindow(source)` | 整窗快照（`SeriesWindow` 源） |
| `ReadText(source)` / `ReadTextRevision(source)` | 文本缓冲快照/变更号 |
| `TryReadTextWindow(...)` / `TryReadTextTail(...)` | 增量文本窗口（高级；返回 `false` 表示主应用未实现） |
| `SubmitInput(source, text)` | 提交输入值（等价用户编辑后提交） |
| `InvokeCommand(command)` | 触发命令，返回 `CommandOutcome(IsError, Message)` |
| `ReadField(field)` / `UpdateField(field, value)` | 配置字段呈现/写回 |
| `Appearance(source)` | 当前外观 |
| `ReadValue(name)` / `CommitValue(name, text)` | 读值状态 / 按控件提交时机交值 |
| `RegisterViewDefault(name, value)` | 登记 XAML 初值；正常由加载期播种，设计期/自定义上下文作后备 |
| `SelectedPage(stateKey)` / `SetSelectedPage(stateKey, page)` | 页签选中页 |
| `MultilineSubmitKey` | 当前多行提交键档位（`CtrlEnter`/`Enter`/`None`） |
| `WheelZoomStep` | 波形曲线区每格滚轮缩放系数（应用设置，默认 1.2） |
| `ColorPicker` | 框架调色窗口（可空）；`SuggestedColors` + `PickAsync` |
| `ImagePicker` | 框架图片选择/裁切窗口（可空）；`PickAsync(owner, aspect, title)` 返回原始字节与源图裁切矩形 |
| `ViewImageStore` | 当前视图控件的持久化图片（可空）；`Revision` + `Read` / `Write` 字节与裁切，随 `viewState` 保存 |

自定义元件：继承 `ModuleElement`（或实现 `IModuleViewElement`），框架每帧调用 `Refresh(context)`；值没变就不要碰视觉，刷新不要重建控件树。`ModuleViewHost` 先落外观、再调刷新。

### 取色

`IModuleColorPicker.SuggestedColors` 给出推荐色，`PickAsync(owner, initialArgb, title, resetText)` 打开框架调色窗口，返回 `Confirmed`、`ColorArgb`、`Reset`。设计期 `ColorPicker` 可能为 `null`，不要弹窗。

### 选择图片

`IModuleImagePicker.PickAsync(owner, aspectRatio, title)` 打开框架统一的文件选择 + 裁切窗口，返回 `Confirmed`、`Bytes`、`SourceSize`、`CropRect`（源图像素坐标）。`aspectRatio` 传图片控件内容区的宽高比；`ImagePicker` 可能为 `null`，设计期/测试不要弹窗。视图把原始字节和归一化裁切交给控件图片状态。

### 控件图片状态

`IModuleViewImageStore` 读写**当前视图控件自己的持久化图片**：

```csharp
public readonly record struct ModuleViewImage(byte[] Bytes, Rect? Crop);

long Revision { get; }
ModuleViewImage? Read();
bool Write(byte[]? bytes, Rect? crop);
```

- 图片保存在 `viewState.image`，字节在组合件/工作区的 `assets/<hash>.<真实扩展名>`；与模块壁纸共用同一个实例资源槽机制与命名规则，但用途（`ViewImage` / `Wallpaper`）不同、互不影响。
- `Revision` 用于避免每帧复制字节；`ViewImageStore` 可能为 `null`。
- 清除图片只清 `ViewImage` 槽；模块壁纸仍由模块窗口自己的壁纸通道管理。

## 收件箱

| 成员 | 语义 |
|---|---|
| `Inbox.TryDequeueCommand(out string)` | 取一条待执行命令；非阻塞 |
| `Inbox.TryDequeueInput(out string source, out string text)` | 取一个输入值；非阻塞 |
| `Inbox.HasPending` | 是否还有待处理项（step 模型据此唤醒） |

收件箱容量 64，满时丢最旧并计数；重复点击会重复入队，需要幂等自己做。只有声明过的命令/输入名才会被投递。

## 定时器

| 成员 | 语义 |
|---|---|
| `Timers.Create()` | 创建未启动的定时器，默认间隔 100 ms |
| `IModuleTimer.Interval` | 必须 > 0；运行中修改从修改时刻重新计 |
| `IModuleTimer.Start()` / `Stop()` | 幂等；第一个 Tick 在一个间隔之后 |
| `IModuleTimer.IsEnabled` | 当前是否启用（暂停/停止后保留启用意图） |
| `IModuleTimer.Tick` | 只在实例线程触发，与 `OnStep` 串行 |
| `Dispose()` | 幂等；移除时框架统一释放 |

实例停用时框架暂停全部定时器，恢复后重新计一个间隔；Tick 异常由框架捕获并停掉该定时器。模块不得自建线程或系统定时器。

## 文件与资源

| 成员 | 语义 |
|---|---|
| `Files.Request(new FilePickRequest(sourceName, mode, filter, suggestedName))` | 立即返回；同一输入源的未完成请求被后一次替换 |
| `FilePickMode` | `Open` / `Save` / `Folder` |
| 结果 | 用户选定后，路径经 `sourceName` 这个**输入源**投回；必须已声明该输入源 |

| 成员 | 语义 |
|---|---|
| `Resources.TryOpen(kind, relativePath, out Stream?)` | 只读打开 `resources/<kind>/<相对路径>`；越界/不存在返回 `false`，不抛 |
| `Resources.ReadAllBytes(kind, path)` | 扩展方法；失败返回 `null` |

`i18n` 与 `icons` 是框架认识的类别，由框架按拥有者读取；`IResourceAccess` 只读**其它**自定义类别。

## 外部接入

模块只声明“接入名 + 标准标识”，适配器、参数类型与会话都在框架侧。

| 成员 | 语义 |
|---|---|
| `Access.RequestOpen(name)` / `RequestClose(name)` | 只发请求；实际打开/关闭由框架在实例循环执行；返回名字是否已声明 |
| `Access.SetReceiveHandler(name, handler)` | Push：原始字节块由框架回调；与 `TryRead` 互斥 |
| `Access.TryRead(name, Span<byte>, out int length)` | Pull：非阻塞取一块字节；目标缓冲不足 ⇒ 丢弃并计数 |
| `Access.Write(name, payload)` | 写一块字节；有界等待，失败返回 `false` 并计入丢弃 |
| `Access.GetSnapshot(name)` | 唯一事实源：状态、端点、失败、收发/丢弃计数；没有事件面 |
| `Access.ClearCounters(name)` | 只清计数，不动队列、连接数与故障态 |
| `Access.GetParametersType(name)` | 该接入参数类型（框架登记；模块骨架用它反射字段名） |

`ExternalAccessSnapshot` 关键字段：`State`（`Closed`/`Opening`/`Open`/`Closing`/`Faulted`）、`Suspended`、`Endpoint`、`Failure`、`ReceivedUnits/Bytes`、`SentUnits/Bytes`、`DroppedUnits/Bytes`、`RejectedUnits`、`ActiveConnections`、`LastReceivedAt`；分原因丢弃字段与饱和/滞回字段见快照类型。计数在实例生命周期内累计，只有 `ClearCounters` 归零。失败用 `ExternalAccessFailure(Kind, Endpoint, Value, Detail)` 表达：`Kind` 是机器可读种类（地址为空/端口占用/超时/对端关闭/设备丢失/读写失败等），其余字段只搬运事实，句子由模块自己的表生成。框架不自动重试，失败后需用户再次请求打开。

四个标准与配置字段（模块视图用标准输入控件绑定字段名：文本/数字 `ModuleTextSurface`、枚举 `ModuleSelect`、布尔 `ModuleToggle`；用 `ExternalAccessFieldNames` 常量）：

| 标准 | 字段 |
|---|---|
| `serial` | `PortName`、`BaudRate`、`DataBits`、`StopBits`、`Parity` |
| `tcp-client` | `RemoteHost`、`RemotePort`、`ConnectTimeout`、`NoDelay` |
| `tcp-server` | `LocalAddress`、`LocalPort`、`MaxConnections`、`NoDelay` |
| `udp` | `LocalAddress`、`LocalPort`、`RemoteAddress`、`RemotePort`、`Broadcast` |

这些参数类型在框架侧，模块不引用；但参数表单的标题/说明/分组/枚举显示由**声明该接入的模块**的文本表解析，模块需要提供 `config.serial.*`、`config.tcpClient.*`、`config.tcpServer.*`、`config.udp.*` 这些键，否则界面显示键名。

`Sdk.AccessKit` 的 `ExternalAccessChannel` 是可选骨架：`Attach(onRawData)`、`TryRead`、`Write`、`HandleCommand(open/close/clear)`、`PublishStatus()`，并在状态变化时统一发布状态句、灯色、计数与按钮/字段闸门。构造参数 `ExternalAccessChannelSpec` 只含名字：接入名、五个显示源（连接、状态、收、发、丢弃，可选连接数）、三个命令名；状态句由 `Func<ExternalAccessSnapshot, ExternalAccessStatus>` 给出（`Text` + 可选 `Severity` 角色词），压力句可选（`ExternalAccessPressure`：`ReceiveLost` / `SendLost` / `SendBacklogged` / `ReceiveSaturated`，返回 `null` 表示不说）。不需要它时直接用 `IModuleContext.Access` 亦可。

## 测试替身

`HYWDebugAssistant.Sdk.Testing` 提供不起框架的测试面。

| 类型 | 用途 |
|---|---|
| `ModuleContract.Probe(type, context?, catalog?, portSpecs?)` | 跑声明面检查；`portSpecs` 传视图 XAML 的 `PortSpecReader` 结果；`Problems` 空 = 合法，`Advisories` 是建议 |
| `ModuleContract.CheckAll(types…)` | 只汇总所有模块的 `Problems` |
| `TestModuleContext` | 驱动 `OnRegistered`、投递、收件箱、配置、定时器、外部接入、视图状态 |
| `TestActivityDriver.Run(source, context, token)` | 在测试线程推进定时器并周期调 `OnStep` |
| `TestExternalAccess` | `QueueReceived`、`Deliver`、`SetSnapshot`、`Written`、`WriteSucceeds`、请求记录 |
| `TestModuleTimers` | `AdvanceTo`、`NextDue`、`FireNow`（单个定时器） |
| `TestViewState` | 预置选中页/输入文本/显示文本/序列窗口 |

`TestModuleContext` 主要成员：

| 成员 | 说明 |
|---|---|
| `DeclaredPorts` / `DeclaredDataTypes` / `DeclaredRoles` / `DeclaredFlows` / `DeclaredDisplaySources` / `DeclaredInputs` / `DeclaredCommands` / `DeclaredExternalAccesses` | 声明结果，可直接断言 |
| `Deliver(port, payload, layout?)` | 有接收回调 ⇒ 直接回调；否则入 Pull 队列。省略 `layout` 时按字节块推断（仅 ≤256 字节，更大请显式传入） |
| `Sent` / `Published` | 发出的单元 / 写入的显示样本 |
| `EnqueueCommand(name)` / `EnqueueInput(source, text)` | 投递命令/输入；`EnqueueInput` 同时写视图状态 |
| `SetConfiguration<T>(config)` | 预置配置 |
| `ReportedIssues` / `Logs` / `States` / `AppearanceReports` | 上报道记录 |
| `ReportedLosses` / `PortFlowStates` | 模块上报的丢弃 / 预置的口流动状态 |
| `ClearedDisplays` / `SetTexts` / `FullDisplays` | 显示请求记录与预置 |
| `FilePickRequests` | 文件选择请求记录（不弹窗） |
| `Access` / `ViewState` / `Timers` | 测试替身 |

## 线程与边界

| 位置 | 线程 |
|---|---|
| `OnRegistered` | 管理线程（实例线程尚未启动） |
| 其余五个生命周期钩子、`OnStep`、接收回调、定时器 Tick | 该实例的专属线程，彼此不并发 |
| 视图 `Refresh`、`IModuleViewContext` 的读取 | 界面线程（框架每帧调用） |
| 配置 Schema 生成、`IConfigurationChoices.GetChoices` | 界面线程 |

纪律：

- 模块不得自建线程、不得 `Thread.Sleep`、不得自建系统定时器。
- 框架调用模块的入口之外的跨线程访问都不安全；共享状态只在实例线程内读写，界面只经视图上下文取数。
- 钩子与回调里的异常不得逸出；框架会捕获并转 Issue，但行为已不可预期。
- 模块不得引用其它模块，不得反向查询其它模块是否存在；模块间只经端口与数据类型通信。
- 框架对模块提供的能力只有 `IModuleContext` 这一份；不要保存框架对象引用。

## 版本与兼容

- 产品只有一个四段版本；五个作者包同版本发布，不单独编号。
- 加载器**不比较**模块编译时的 SDK 版本；API 变化会在类型加载或首次调用时失败，失败文案会带上编译时引用的 SDK 版本。
- 作者包升级后重编模块是唯一正确做法；0.x 期间不承诺旧包可加载。
