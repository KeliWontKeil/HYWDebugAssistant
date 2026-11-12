# 自发活动与定时器

模块需要“周期性做事”（定时发送、轮询采样、批处理）或“在数据/命令到达时推进”时：

- 需要数据/命令/会话驱动的单步，实现 `IActivitySource`；
- 需要周期，用 `IModuleContext.Timers` 创建 `IModuleTimer`。

框架拥有实例线程与调度能力；模块拥有定时器实例（间隔、启停、Tick 处理），但**不拥有线程、不写循环**。

## 怎么选

| 需要什么 | 用什么 | 不要做什么 |
|---|---|---|
| 有数据就处理 | `ReceiveMode.Push` + 接收回调 | 不要开定时器轮询数据 |
| 需要自己攒批、控节奏 | `ReceiveMode.Pull` + `OnStep(Data)` 里取 | 不要同时设回调（两个消费者） |
| 固定节拍（发送、采样） | `IModuleTimer` | 不要 `Thread.Sleep` / 自建 `Timer` |
| 用户点按钮、填输入 | `OnStep` 里从收件箱取 | 不要在回调线程外直接读界面 |
| 设备状态变化 | `OnStep(Session)` 或接入回调 | 不要自己探测端口 |

## 契约

```csharp
public interface IActivitySource
{
    ReceiveMode Receive => ReceiveMode.Push;  // 端口/会话：Push 回调，Pull 自取
    void OnStep(IModuleContext context, StepReason reason);
}

[Flags] public enum StepReason
{
    Start = 1, Data = 2, Session = 4, Command = 8, Input = 16,
}
```

| 规则 | 说明 |
|---|---|
| `Start` | 进入活动态后的第一拍，只调一次；在这里做上下文相关初始化 |
| 原因可合并 | 同一拍既有命令又有数据时只调一次 `OnStep`，`reason` 是位组合；用 `(reason & StepReason.Data) != 0` 判断 |
| 不保证每拍都有原因 | 没有工作时不调用；不要把 `OnStep` 当周期 |
| 线程 | 与生命周期钩子、接收回调、定时器 Tick 同实例线程，不并发 |
| 异常 | 不得逸出；框架会捕获并转 Issue |

不实现 `IActivitySource` 的模块：端口数据仍可由 `SetReceiveHandler` 投递，但**收不到命令与输入**。

## 周期发送示例

```csharp
public sealed class TickSender : IModule, IActivitySource
{
    private IModuleContext? _context;
    private IModuleTimer? _timer;

    public ReceiveMode Receive => ReceiveMode.Push;

    public void OnRegistered(IModuleContext context)
    {
        _context = context;
        context.Declaration.DeclarePort(new PortDeclaration("out", PortDirection.Send, Shape));

        _timer = context.Timers.Create();
        _timer.Interval = TimeSpan.FromMilliseconds(10);
        _timer.Tick += OnTimerTick;
        _timer.Start();
    }

    public void OnStep(IModuleContext context, StepReason reason)
    {
        _context = context;
        // 命令/输入在这里消费；普通 step 不重置定时器。
    }

    public void OnRemoved() => _timer?.Dispose();

    private void OnTimerTick(object? sender, EventArgs e)
    {
        if (_context is { } context)
        {
            Send(context);
        }
    }
}
```

- 定时器在 `OnRegistered` 创建、启动都可以；Tick 只在实例线程触发。
- 第一个 Tick 在一个 `Interval` 之后；没有“开启立即发一条”的特殊分支。
- 间隔变化时改 `timer.Interval`；运行中修改从修改时刻重新计。
- 停止周期用 `timer.Stop()`；重新开始用 `timer.Start()`。
- 需要“立即做一次”是模块自己的动作（直接调用 `Send`），不是定时器语义。
- 实例禁用时框架暂停全部定时器，恢复后从现在重新计一个间隔；移除时统一释放。模块显式 `Stop`/`Dispose` 是幂等且允许的。
- Tick 异常由框架捕获、归到该实例并停掉该定时器，不每拍刷屏。
- 多个定时器可以有不同周期；不要用一个短定时器模拟多个节拍。

## Push / Pull

- `ReceiveMode.Push`（默认）：框架在实例线程把端口/会话数据交给接收回调；`OnStep` 只处理命令/输入/Start 等唤醒。
- `ReceiveMode.Pull`：数据留在队列；收到 `OnStep(Data)` 后在自己的循环里 `TryReceive`：

```csharp
if ((reason & StepReason.Data) != 0)
{
    while (context.Ports.TryReceive(out var buffer, out var port) && buffer is not null)
    {
        try
        {
            if (port == InPort)
            {
                Handle(buffer.Span);
            }
        }
        finally
        {
            context.Ports.ReleaseBuffer(buffer);
        }
    }
}
```

- 一个模块只用一种消费方式，不能同时有回调与自取。
- `OnStep(Data)` 只表示“有数据可处理”，不保证每拍一个；一次 `TryReceive` 循环取完。
- 主动接收的缓冲必须归还；回调的载荷只在本次调用有效。

## 会话与设备探活

外部会话由框架推进：模块不调用 `Pump`。Pull 模式在 `OnStep(Session)` 里读；Push 模式由框架回调投递。设备探活由框架按会话节拍推进，不需要模块建定时器。

## 测试

定时器与单步的测试用 `Sdk.Testing`：

- `TestActivityDriver.Run(module, context, token)`：先 `OnStep(Start)`，随后推进测试时钟并周期调 `OnStep(Command | Input)`；不投递 Data/Session；
- `context.Timers.AdvanceTo(now)`：精确推进虚拟时钟，触发到期 Tick；
- `TestModuleTimer.FireNow()`：立即触发一次，不改变下次到期。

它只用于模块级测试，不代表框架的实例线程与计时服务。写法见[测试与验证](testing.md)。

## 相关

- [实现](implementation.md)：上下文各面的用法
- [接口参考](reference.md)：`IModuleTimer` 成员
- [模块结构](anatomy.md)：活动层在整体结构中的位置
