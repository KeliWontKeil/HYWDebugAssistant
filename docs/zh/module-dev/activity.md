# 自发活动（step）

模块需要"周期性做事"（定时发送、轮询采样、批处理）或"在数据/命令到达时推进"时实现 `IActivitySource`。框架拥有实例线程与计时服务，模块只提供**一步**和**下一次延迟**。

## 契约

```csharp
public interface IActivitySource
{
    ReceiveMode Receive => ReceiveMode.Push;  // 端口/会话：Push 回调，Pull 自取
    TimeSpan? NextDelay => null;              // 相对延迟；null = 无周期
    void OnStep(IModuleContext context, StepReason reason);
}

[Flags] public enum StepReason
{
    Start = 1, Data = 2, Session = 4,
    Command = 8, Input = 16, Due = 32,
}
```

- **不要写循环**：没有自建循环、没有等待原语、没有 `Thread.Sleep`、没有自建 `Timer`。
- **不要保存线程**：`OnStep` 由框架在实例专属线程调用。
- **`NextDelay` 是相对延迟**：返回"从现在起多久后需要下一次 Due"，不需要也不存在模块可见的绝对时钟。
- **同一个到期不要每次 step 重排**：普通 `Input`/`Command`/`Data` 不改变已排好的到期；只有 `Start`、`Due` 或延迟值变化时框架才重排。框架内部会从上一到期推进，避免每拍处理耗时累积成漂移。
- **一步要短小**：`OnStep` 内不做长时间阻塞；停止后框架不再调用。

## 周期发送示例

```csharp
public sealed class TickSender : IModule, IActivitySource
{
    private TimeSpan _period = TimeSpan.FromMilliseconds(10);
    private bool _running;
    private bool _fireImmediately;

    public ReceiveMode Receive => ReceiveMode.Push;

    public TimeSpan? NextDelay =>
        _running
            ? _fireImmediately ? TimeSpan.Zero : _period
            : null;

    public void OnRegistered(IModuleContext context)
    {
        context.Declaration.DeclarePort(new PortDeclaration("out", PortDirection.Send, Shape));
        context.Declaration.DeclareInput(new InputDeclaration("interval"));
    }

    public void OnStep(IModuleContext context, StepReason reason)
    {
        var wasRunning = _running;

        // 输入不重置已有的到期；只更新是否运行/间隔。
        while (context.Inbox.TryDequeueInput(out var source, out var text))
        {
            if (source == "interval" && int.TryParse(text, out var ms) && ms >= 0)
            {
                _running = true;
                _period = TimeSpan.FromMilliseconds(Math.Max(1, ms));
            }
        }

        if (reason.HasFlag(StepReason.Due))
        {
            _fireImmediately = false;
            Send(context);
        }

        if (_running && !wasRunning)
        {
            _fireImmediately = true;
        }

        if (!_running)
        {
            _fireImmediately = false;
        }
    }
}
```

- 刚打开周期时用 `TimeSpan.Zero` 让下一拍立即到期；首条发出后回到正常间隔。
- 间隔变化时 `NextDelay` 的值变了，框架会从现在按新延迟重排；普通输入/命令不改变已排好的到期。
- 停止周期时返回 `null`，框架撤销到期。

## Push / Pull

- `ReceiveMode.Push`（默认）：框架在实例线程把端口/会话数据交给接收回调；`OnStep` 里只处理命令/输入/到期。
- `ReceiveMode.Pull`：数据留在队列；`OnStep` 收到 `Data`/`Session` 后自己 `TryReceive`/`TryRead`。
- 一个模块只用一种消费方式，不能同时有回调与自取。

## 会话与设备探活

外部会话由框架推进：模块不调用 `Pump`。Pull 模式在 `OnStep(Session)` 里读；Push 模式由框架回调投递。设备探活的到期也由框架计时服务产生 `Due`。

## 测试

行为测试用 `Sdk.Testing` 的 `TestModuleContext` 与 `TestActivityDriver.Run(module, context, cancellationToken)` 直接驱动；`TestActivityDriver` 用测试侧 `Stopwatch` 按 `NextDelay` 模拟到期，只用于测试，不代表框架实现。

## 相关

- [实现](implementation.md)
- [声明面](contracts.md)
- [SDK 与兼容](sdk-version.md)
