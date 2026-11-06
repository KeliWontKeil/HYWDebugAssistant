# SDK 版本与兼容

## 包有哪些

| 包 | 类别 | 用途 |
|---|---|---|
| `HYWDebugAssistant.Sdk` | 运行时 | 声明、生命周期、上下文、上报（模块作者的主要面） |
| `HYWDebugAssistant.Sdk.Ui` | 运行时（可选） | 视图契约与控件库，依赖 Avalonia |
| `HYWDebugAssistant.Sdk.AccessKit` | 运行时（可选） | 外部接入通道：请求、读写原始字节、状态呈现 |
| `HYWDebugAssistant.Sdk.Testing` | 作者包 | 契约自检与测试上下文，只给测试项目 |
| `HYWDebugAssistant.Module.Build` | 作者包 | 模块打包规则（`module.json` / `resources` 归拢） |

**运行时包随宿主发布**；**作者包不随宿主发布，也不得进模块包**——`Sdk.Testing` 被模块运行时引用时，框架拒绝加载。

## 版本

- 只有一个版本号：`主.小.修订.轮次`，产品、发布清单与全部作者包共用。
- 引用时把版本写死（例如 `0.0.0.10`），一次构建的五个包版本必须相同。
- 没有"Sdk 版本"这种第二个版本轴：包是产品的派生，不单独编号。

## 兼容

- **0.x 允许破坏性变更**：新增可选声明优先用 `init` 属性或新类型；删除/改签名必须提升包版本，并重编所有模块。
- **不比较版本区间**：加载器不校验模块编译时用的包版本；模块能加载的前提是程序集能加载、入口实现模块接口、依赖可解析。
- 运行时按**简单名**绑定：按旧包编译的模块可能绑到新宿主，但 API 变化会在类型加载/第一次调用时失败。
- 升级 SDK 后**重编模块**是唯一正确做法；`0.x` 期间不承诺旧包可加载。
- 本次 `0.0.0.8 → 0.0.0.9` 包含：`IActivitySource` 用 `OnStep` + 相对 `NextDelay`，删除 `IModuleClock`/`IModuleContext.Clock`、`IStopSignal`/`IModuleContext.StopSignal`、`IModuleInteractionSink`、`DisplayDelta`/`DisplayCursor`；`Sdk.Ui` 的 `TextBufferSnapshot.Sequence` 与 `ReadTextRevision` 的默认实现一并删除。旧模块必须重编。
- 本次 `0.0.0.9 → 0.0.0.10` 包含：删除 `IActivitySource.NextDelay` 与 `StepReason.Due`，新增 `IModuleContext.Timers` / `IModuleTimer`（模块自持定时器，Tick 在实例线程触发）；周期模块改用 `context.Timers.Create()`。旧模块必须重编。
- 本次 `0.0.0.10 → 0.0.0.11` 包含：发送器间隔 0 仍可输入但不再是连续发送周期；间隔填写错误不再阻断手动单次发送；UDP 忽略对端未绑定产生的异步 ICMP 接收错误；串口写泵不再在写路径上无界 Flush，串口端口目录支持按硬件变化刷新且失效的当前端口不再保留为可选项；外部接入状态按当前丢弃 episode / 当前积压呈现。`IConfigurationChoices` 新增带默认实现的 `GetChoicesRevision()` 与 `KeepCurrentChoiceWhenUnavailable`，`PortFlowState` 新增 `DropActive`，`ExternalAccessSnapshot` 新增 `SendDropActive`。旧模块必须重编。
- 本次 `0.0.0.11 → 0.0.0.12` 包含：状态行增加可配置的积压/丢弃滞回，`PortFlowState` 增加 `BacklogStatusActive`/`DropStatusActive`，`ExternalAccessSnapshot` 增加发送/接收状态字段；`FlowSettings` 增加 `StatusBacklogEnterDepth`/`StatusBacklogExitDepth`/`StatusHoldMs`/`StatusDropHoldMs`；文本显示上限改为设置项。旧模块必须重编。
- 加载失败时，错误文案会带上"编译时引用的 SDK 版本"，用于判断是否该重编。

## 什么时候必须重编

- 任何一次包版本提升（作者包与宿主版本对齐）；
- 宿主 API 面变化（新增抽象成员、改签名、删类型）；
- 目标框架变化。

详见[版本与兼容](../project/versioning.md)。

## 相关

- [发布与装载](publishing.md)
- [测试与验证](testing.md)
