# 实现

实现一个模块 = 实现 `IModule`，在钩子里做该做的事。线程、队列、调度与生命周期由框架负责；模块只写“这一步做什么”。成员签名见[接口参考](reference.md)，本页讲怎么用。

## 生命周期与线程

| 位置 | 线程 | 该做什么、不该做什么 |
|---|---|---|
| `OnRegistered` | 管理线程（实例线程尚未启动） | 声明全部成员、设接收回调、创建定时器、保存上下文。此时数据还没流动，不能假定已经收到过数据 |
| `OnActivated` | 实例线程 | 发布初始状态、启动需要“运行中”才做的动作 |
| `OnStep` | 实例线程 | 处理本拍合并后的原因（数据 / 会话 / 命令 / 输入） |
| 接收回调 / 定时器 Tick | 实例线程 | 处理一个单元 / 一次周期动作 |
| `OnDisabled` / `OnEnabled` | 实例线程 | 暂停或恢复自己的业务状态；定时器由框架暂停 |
| `OnConfigurationChanged` | 实例线程 | 重新读取配置、调整运行参数 |
| `OnRemoved` | 实例线程停止前 | 释放句柄、文件、缓冲 |

三条纪律：

- 不假定注册期已经有数据在流动；
- 任何回调都必须尽快返回：不阻塞、不等待、不 `Thread.Sleep`、不自建线程或系统定时器；
- 钩子与回调里的异常不得逸出。框架会捕获并转 Issue，但行为已经不可预期，应该自己处理可预期的失败。

## 收数据

两种消费方式，一个模块统一用一种：

| 方式 | 写法 | 适用 |
|---|---|---|
| Push（默认） | `context.Ports.SetReceiveHandler(InPort, OnData)` | 简单、无周期 |
| Pull | `Receive => ReceiveMode.Pull`；在 `OnStep(Data)` 里 `TryReceive` / `ReleaseBuffer` | 需要攒批、控节奏、多口公平 |

```csharp
private void OnData(ReadOnlySpan<byte> payload)
{
    if (payload.Length < MyModule.Shape.SizeInBytes)
    {
        return;
    }

    var sequence = BinaryPrimitives.ReadUInt64LittleEndian(payload);
    var value = BinaryPrimitives.ReadSingleLittleEndian(payload[8..]);
    // 处理；需要留存时自行拷贝
}
```

- 回调里的载荷只在本次调用有效；回调返回后缓冲可能立即被复用。
- Pull 取到的 `PayloadBuffer` 必须 `ReleaseBuffer` 归还；每实例一条入站队列，`TryReceive` 返回的是“任意一个单元 + 目标口名”，只关心单口的模块自行比对 `sourcePort`。
- 到达长度不足契约尺寸的单元不会进回调（框架按“载荷非法”丢弃）。

## 发数据

```csharp
private static readonly ContractLayout Layout = ContractLayout.FromShape(Shape);

Span<byte> unit = stackalloc byte[Shape.SizeInBytes];
var writer = new PayloadWriter(unit, Layout);
writer.Clear();
writer.WriteU64("sequence", sequence);
writer.WriteF32("value", value);
var accepted = context.Ports.TrySend(OutPort, unit);
```

- 长度必须等于发送口契约尺寸；短了被丢弃（记“载荷非法”），长了被截断。
- `TrySend` 返回 `false` 表示没有送达（队列满、口不存在或已停流）；是否重试由模块决定，框架不做背压。
- 写字段用 `PayloadWriter`（按布局寻址）；单原语类型直接用 `BinaryPrimitives` 也可以。
- 热路径不分配：布局在类型旁边算一次，缓冲用字段或 `stackalloc`。

## 值

控件编辑的都是值：视图只按名字绑定，模块只按名字读取、接受、报告结论。值没有“配置/输入”分类；完整规则见[值](values.md)。

```csharp
context.Declaration.DeclareValue(new ValueDeclaration("header")
{
    Editor = ValueEditor.Text,
    Persistent = true,
});

var header = context.Values.ReadText("header") ?? string.Empty;
context.Values.Report("header", header.Length == 0
    ? new ValueNotice(ValueNoticeSeverity.Warning, "帧头不能为空")
    : null);
```

配置字段自动是同名的值；模块也可以继续用强类型配置对象处理自己的领域状态。控件不直接读写配置或视图状态。

## 配置（模块私有、强类型）

配置是**实例**属性，随组合件保存，读取得到不可变快照。它适合模块自己的领域对象；视图控件值请优先用上面的值接口。

```csharp
public sealed class MyConfig
{
    [ConfigurationField("目标地址", Group = "连接", Description = "主机名或 IP")]
    public string Host { get; set; } = "127.0.0.1";

    [ConfigurationField("超时(秒)", Minimum = 1, Maximum = 60)]
    public int TimeoutSeconds { get; set; } = 5;
}

public void OnRegistered(IModuleContext context)
{
    var config = context.Configuration.Read<MyConfig>();   // 首次读取即登记默认配置
    _timeout = TimeSpan.FromSeconds(config.TimeoutSeconds);
}

public void OnConfigurationChanged()
{
    var config = _context!.Configuration.Read<MyConfig>();
    _timeout = TimeSpan.FromSeconds(config.TimeoutSeconds);  // 热生效
}
```

规则：

- 配置类型必须是**公开、非抽象类**，有公共无参构造；框架按 `class + new()` 约束读写。
- 只有 `bool`、`string`、整型、浮点、枚举和它们的 `Nullable` 会生成表单字段；复杂对象与集合不参与。
- 字段名在**同一实例的所有配置类型里**必须唯一（标准接入的参数类型也在同一张表单里）；跨类型重名会让写回拒绝。
- 第一次 `Read<T>` 会把默认值登记为该实例配置；**只声明不读取**的配置不会出现在表单里，保存组合件时也不落盘。
- 写回用 `Configuration.Write<T>`：传入一个新的配置实例替换该实例的配置，不要就地修改运行中的对象。**界面上的字段写回**走“序列化 → 覆盖一个字段 → 反序列化为新实例”的变更路径，原子应用后在实例线程调用一次 `OnConfigurationChanged`；模块自己调用 `Write` 只更新实例配置与配置版本，不会把通知回投给模块——写完直接更新自己的运行参数。
- 字段标题、说明、分组、枚举显示用 `TitleKey` / `DescriptionKey` / `GroupKey` / `[ConfigurationChoice(TitleKey = …)]` 指向本模块文本表；不给键时回退为字段名（如 `PeriodMs` → “Period Ms”）。
- 动态选项（可用串口、本机地址）实现 `IConfigurationChoices`：返回 `null` = 不是动态字段，空表 = 现在没有可选项。它运行在界面线程，必须廉价、不抛、不阻塞。
- 单位写在 `Unit` 或字段标题里，**不入契约**；改单位不改变数据字节。
- **配置字段不会自动出现在窗口里**：需要在视图中用 `ModuleConfigField Value="字段名"` 画出（见[控件](controls.md)）。不声明视图工厂的模块，窗口只显示身份与状态；配置仍可随组合件保存，但用户改不了。

## 收件箱：命令与动作输入

用户点按钮、文件选择结果、结构动作等**事件**投进收件箱，由模块在 `OnStep` 里排空：

```csharp
public void OnStep(IModuleContext context, StepReason reason)
{
    while (context.Inbox.TryDequeueCommand(out var command))
    {
        if (command == ResetCommand)
        {
            Reset(context);
        }
    }

    while (context.Inbox.TryDequeueInput(out var source, out var text))
    {
        ApplyInput(context, source, text);
    }

    if ((reason & StepReason.Data) != 0 && Receive == ReceiveMode.Pull)
    {
        Drain(context);
    }
}
```

- 只有声明过的命令名与输入源名才会被投递；未登记的名字由框架拒收并报 Issue。
- 收件箱容量 64，满时丢最旧并计数；同一命令重复点击会重复入队，需要幂等就自己做。
- 不实现 `IActivitySource` 的模块**收不到**命令与输入：没有单步就没有消费时机。
- 控件值用 `context.Values.ReadText(名字)` 读取；它已经是当前有效值（持久化值，或框架在激活前播种的 XAML 初值）。
- 需要“用户正在编辑但尚未提交”的内容时，把控件的 `CommitMode` 设为 `OnChange`，而不是依赖回车。
- 模块接受/规范化的值用 `context.Values.AcceptText`；字段结论用 `context.Values.Report`。
- **提交时机是控件属性**：`OnChange` 逐键交值，`OnExplicit` 在回车/动作/失焦时交值。模块不判断控件属于哪种。
- **持久化归值声明，初值归 XAML**：`Persistent` / `ResetWithModule` 写在 `ValueDeclaration` 上；视图可见值的默认值只写在控件 XAML 初值（`Text` / `IsChecked` / `SelectedKey` / `InitialValue`）。控件不持有持久化语义。

## 上报

三条通道边界不同，不要混用：

| 通道 | 用途 | 写法 |
|---|---|---|
| Issue | 需要用户处理的问题（打开失败、配置非法、声明冲突） | `Issues.Report(new IssueReport(IssueCategory.CommunicationError, IssueSeverity.Warning, "…", "my-serial"))` |
| 业务状态 | 模块自己的状态行（一句话） | `Display.SetText(StateSource, "已连接")` 或 `State.Update(new BusinessState("link", "online", "已连接"))` |
| 日志 | 诊断记录，按级别过滤 | `Log.Log(ModuleLogLevel.Information, "…")` |

- 可预期的外部状态（设备没插、对端关闭、连接超时）用 `Warning`，不要用 `Error`；`Error` 留给框架故障、契约错误、持久化损坏。
- 不要把业务状态报成 Issue：状态行是给用户看“现在怎么样”，Issue 是“需要你处理”。
- 热路径不逐单元打日志；计数后用定时器汇总。
- `ObjectPath` 写口名、接入名或配置字段名，用户在 Issue 面板里能定位。

## 显示

先声明显示源，再写入。声明与缓冲规则见[声明面](contracts.md)。

```csharp
context.Display.SetText(StateSource, "就绪");
context.Display.AppendLine(LogSource, line);       // 一行一条，通道负责换行
context.Display.AppendText(LogSource, chunk);      // 只增、不换行
context.Display.Clear(LogSource);

Span<byte> counter = stackalloc byte[sizeof(ulong)];
BinaryPrimitives.WriteUInt64LittleEndian(counter, _total);
context.Ports.Publish(CountSource, counter);        // Value/Sequence 等数值源
```

- `Value` 是最新值槽；其余类别是环形缓冲，**只有 512 个样本**，溢出丢最旧。
- 文本缓冲由框架持有，默认上限 20000 行（应用设置）；`Display.IsFull(source)` 回答“自上次清空/替换以来有没有丢过内容”，不回答丢了几行。
- `SetText` 是替换，适合状态/消息；`AppendText` / `AppendLine` 是只增，适合日志。
- 需要“任意范围的全部点”时用 `SeriesWindow` 源 + `Display.SetSeriesWindow`：快照不可变，发布后不得再改其中的数组。

```csharp
context.Declaration.DeclareDisplaySource(new DisplaySourceDeclaration("curve", ViewDataSourceKind.SeriesWindow));

context.Display.SetSeriesWindow("curve", new SeriesWindowSnapshot(
    Version: ++_version,
    XRangeStart: start,
    XRangeEnd: end,
    LatestIndex: latest,
    RateHz: rate,
    YMin: yMin,
    YMax: yMax,
    YAuto: true,
    Channels: [new SeriesWindowChannel(0, "ch0", colorArgb: null, values)]));
```

## 外观

外观是基础能力，不含语义：框架不认识“红 = 失败”。

```csharp
context.Appearance.SetOutlineRole(StateSource, ViewInkRoles.Danger);
context.Appearance.SetTextRole(StateSource, ViewInkRoles.Warning);
context.Appearance.SetReadOnly("threshold", true);
context.Appearance.SetEnabled(StartCommand, false);
context.Appearance.ResetInteraction(ContinuousSource);
```

- 优先用角色词（`ViewInkRoles.Danger` 等），它随主题与用户改色；绝对色只在确有需要时使用。
- 只读 ≠ 禁用：只读让用户能看能选中但不能改；禁用让整格不可操作。
- `ResetInteraction` 表达“这次动作不成立，把控件拨回”；要表达“这一格接受后的值”，用 `context.Values.AcceptText`，并可用 `context.Values.Report` 给出结论。

## 视图状态

模块可以读用户此刻在看什么，但不能反向操作界面：

```csharp
var page = context.ViewState.GetSelectedPage(PagesKey);
var lines = context.ViewState.GetDisplayLines(LogSource);   // 保存显示内容
```

- 控件值用 `context.Values.ReadText`，不用 `ViewState.GetInputText`。
- 只读快照：不保证与用户眼睛同步（通常差一个刷新节拍）；未知名字返回 `null` / 空列表，不抛。
- 要改用户看到的内容，走值/显示/外观通道，不要试图直接操作控件。

## 文件

模块只负责“请用户指一个路径”，读写由模块自己做：

```csharp
context.Files.Request(new FilePickRequest(
    "savePath", FilePickMode.Save, "文本文件 (*.txt)|*.txt", "log.txt"));
```

- `Request` 立即返回；同一输入源的未完成请求会被后一次替换。
- 选定路径经**输入源** `savePath` 投回——它必须在 `OnRegistered` 里登记过，可以没有界面元素。
- 弹出对话框由框架在界面节拍执行；模块不要在请求后忙等结果。
- 读写异常就地反馈（状态或 Issue），框架不替模块做 IO。

## 资源

```csharp
if (context.Resources.TryOpen("templates", "default.txt", out var stream))
{
    using (stream) { /* 只读、相对、不越界 */ }
}

var bytes = context.Resources.ReadAllBytes("dictionaries", "terms.bin");
```

- 只读本模块包内 `resources/<kind>/`；绝对路径、`..`、盘符一律拒绝，返回 `false` 不抛。
- `i18n` 与 `icons` 由框架按拥有者读取，不要用 `IResourceAccess` 去读它们。
- 自定义 kind 的内容由模块解释；框架不校验格式。

## 外部接入

设备接入走框架托管的会话：模块只声明标准、发开/关请求、读写原始字节。

```csharp
// 声明（OnRegistered）
context.Declaration.DeclareExternalAccess(new ExternalAccessDeclaration(
    AccessName, ExternalAccessStandardIds.Serial)
{
    MaxUnitBytes = 1472,
});

// 请求打开/关闭（命令处理里）
context.Access.RequestOpen(AccessName);
context.Access.RequestClose(AccessName);

// Push：接收原始字节
context.Access.SetReceiveHandler(AccessName, OnRawBytes);

// 发送
context.Access.Write(AccessName, payload);

// 状态
var snapshot = context.Access.GetSnapshot(AccessName);
```

- 声明只写“接入名 + 标准标识”；适配器、参数类型与会话都在框架侧，模块不引用它们。
- Push 与 Pull 二选一：Push 设回调；Pull 在 `OnStep(Session)` 里 `TryRead`。
- 一次读到的是“一块字节”，不是完整报文；需要报文语义时自己组帧。同一次读取的块边界不承诺。
- 状态与计数以 `GetSnapshot` 为唯一事实源；`ClearCounters` 只清计数，不清队列、连接数与故障态。
- 框架**不自动重试**：打开/读写失败后状态是 `Faulted`，需要用户再次请求打开；模块的状态句按 `snapshot.Failure.Kind` 从自己的表选文案。
- 设备探活由框架按会话节拍推进，模块不建轮询定时器。
- 标准接入的参数类型由框架登记为实例配置；模块视图用 `ModuleConfigField` 绑定字段名（`ExternalAccessFieldNames` 常量），并需要在本模块文本表里提供对应的 `config.*` 文案。参数值不需要模块自己读取（框架用它建会话）；要在状态句里显示端点，用 `snapshot.Endpoint`。字段名见[接口参考](reference.md)。
- `Sdk.AccessKit` 的 `ExternalAccessChannel` 是可选骨架：统一处理打开/关闭/清空计数命令、状态句、灯色、计数与按钮/字段闸门；不用它也可以。

## 异常与停止

- 钩子与回调里的异常不得逸出；框架会捕获并转 Issue，但行为已经不可预期。
- 定时器 Tick 抛异常时，框架停掉该定时器并报一次 Issue，不会每拍刷屏。
- 停止是“置标志 + 有界等待”：模块必须周期性检查停止请求并尽快返回；不自建线程就没有强杀问题。
- 关闭/移除时框架先清连接与绑定，再调 `OnRemoved`；模块只释放自己的资源。

## 常见错误

| 现象 | 原因 | 处理 |
|---|---|---|
| 命令不执行、输入收不到 | 没实现 `IActivitySource`，或没在 `OnStep` 里排空收件箱 | 实现接口并在 `OnStep` 里 `TryDequeue*` |
| 状态行一直是初始值 | 显示源没声明，或只写了 `Text` 没写源 | 先 `DeclareDisplaySource`，再 `Display.SetText` |
| 数值读数不更新 | 写显示源的字节数与声明的 `ValueType` 不一致，或空样本 | 按声明原语写小端；不要发布空数组 |
| 开关自己弹回去 | 控件绑了值，但模块没有接受/回写 | 用 `context.Values.AcceptText` 宣告接受值；用 `Values.Report` 说明原因 |
| 配置改了不生效 | 没有重写 `OnConfigurationChanged`，或读的是旧对象 | 在钩子里重新 `Read<T>`；写回走 `Configuration.Write` |
| 配置字段不出现 | 配置类型不是公开类、缺无参构造，或属性不可读写 | 按“配置”一节的约束改 |
| 视图不刷新 | 视图根不是 `ModuleView`、没被 `ModuleViewHost` 驱动，或窗口不可见 | 用工厂 + `ModuleView`；可见时框架每帧调用 `Refresh` |
| 接入参数显示成键名 | 本模块文本表缺少 `config.*` 键 | 补文本表 |
| 接入打开后收不到数据 | 用了 Pull 却没在 `OnStep(Session)` 里读，或同时设了回调 | 二选一，按 `ReceiveMode` 消费 |

## 相关

- [接口参考](reference.md)：成员、默认值、线程与失败方式
- [自发活动与定时器](activity.md)：`OnStep` 与定时器的完整规则
- [测试与验证](testing.md)：上述各面怎么写测试
