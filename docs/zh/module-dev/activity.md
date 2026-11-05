# 自发活动与定时器

模块需要"周期性做事"（定时发送、轮询采样、批处理）或"在数据/命令到达时推进"时：

- 需要数据/命令/会话驱动的单步，实现 `IActivitySource`；
- 需要周期，用 `IModuleContext.Timers` 创建 `IModuleTimer`。

框架拥有实例线程与调度能力；模块拥有定时器实例（间隔、启停、Tick 处理），但**不拥有线程、不写循环**。

## 契约

```csharp
public interface IActivitySource
{
    ReceiveMode Receive => ReceiveMode.Push;  // 端口/会话：Push 回调，Pull 自取
    void OnStep(IModuleContext context, StepReason reason);
}

[Flags] public enum StepReason
{
    Start = 1, Data = 2, Session = 4,
    Command = 8, Input = 16,
}

public interface IModuleTimer : IDisposable
{
    TimeSpan Interval { get; set; }   // > 0；运行中修改从修改时刻重新计
    bool IsEnabled { get; }
    event EventHandler? Tick;
    void Start();
    void Stop();
}
```

- **不要写循环**：没有自建循环、没有等待原语、没有 `Thread.Sleep`、没有自建 `System.Threading.Timer`。
- **不要保存线程**：`OnStep`、`Tick`、生命周期钩子都由框架在实例专属线程调用。
- **定时器可多个**：一个模块可以创建多个不同周期的定时器；不再有"一个模块一个 NextDelay"的限制。
- **Tick 在实例线程**：与 `OnStep`、生命周期钩子、接收回调同线程，不并发；Tick 内不得阻塞。
- **第一个 Tick 在一个 Interval 之后**：没有"开启立即发一条"的特殊分支。
- **Tick 可合并**：实例线程忙时最多保留一个待派发 Tick，不补发、不堆积。
- **禁用/移除**：框架暂停/释放全部定时器；模块显式 `Stop`/`Dispose` 是幂等且允许的。
- **异常**：Tick 异常由框架捕获、归到该实例并停掉该定时器，不每拍刷屏。

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

    public void OnRemoved() => _timer?.Dispose();

    public void OnStep(IModuleContext context, StepReason reason)
    {
        _context = context;

        // 输入/命令在这里处理；普通 step 不重置定时器。
    }

    private void OnTimerTick(object? sender, EventArgs e)
    {
        if (_context is { } context)
        {
            Send(context);
        }
    }
}
```

- 间隔变化时改 `timer.Interval`；运行中修改从修改时刻重新计。
- 停止周期用 `timer.Stop()`；重新开始用 `timer.Start()`。
- 需要"立即做一次"是模块自己的动作（直接调用 Send），不是定时器语义。

## Push / Pull

- `ReceiveMode.Push`（默认）：框架在实例线程把端口/会话数据交给接收回调；`OnStep` 里只处理命令/输入/定时器之外的唤醒。
- `ReceiveMode.Pull`：数据留在队列；`OnStep` 收到 `Data`/`Session` 后自己 `TryReceive`/`TryRead`。
- 一个模块只用一种消费方式，不能同时有回调与自取。

## 会话与设备探活

外部会话由框架推进：模块不调用 `Pump`。Pull 模式在 `OnStep(Session)` 里读；Push 模式由框架回调投递。设备探活由框架按会话节拍推进，不需要模块建定时器。

## 测试

行为测试用 `Sdk.Testing` 的 `TestModuleContext` 与 `TestActivityDriver.Run(module, context, cancellationToken)` 直接驱动：

- `TestActivityDriver` 推进测试时钟，触发 `context.Timers` 里已启动的定时器；
- 定时器 Tick 在测试线程执行，便于断言发送结果；
- 它只用于模块级测试，不代表框架的实例线程/计时服务实现。

## 相关

- [实现](implementation.md)
- [声明面](contracts.md)
- [SDK 与兼容](sdk-version.md)
