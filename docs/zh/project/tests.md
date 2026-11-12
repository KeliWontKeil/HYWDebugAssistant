# 测试体系

| 项 | 内容 |
|---|---|
| 读者 | 要加测试、删测试或跑测试的维护者 |
| 回答 | 哪个测试项目负责什么、什么不进门禁、怎么只跑一层、加用例的准入 |
| 不写 | 模块作者自己的测试写法 → [模块开发/测试与验证](../module-dev/testing.md) |
| 事实来源 | `tests/`、`dev/workflow.md`、`build.ps1`、`tools/dev.ps1` |

## 一条判据

测试只做两件事：**能看见处的自动化**（行为链路：装入→连上→投递、标签→建网、发送→显示、退出协调）
与**看不见处的补充**（数据面、并发、生命周期、持久化与格式、契约与声明、路径、架构规则）。

- **呈现不写断言**：布局、尺寸、颜色、文案、控件树形状、窗口位置一律交给人眼（截图见下）。
- **历史缺陷不保留为常驻用例**：事故已修好就删；根因是结构问题的，登记为 `dev/status.md` 的**结构待办**。
- **测试不是交付物**：不接受“判据文档式测试”“用法示范式测试”“留 PNG 给人看式断言”。

一条用例留下必须同时满足：① 拦得住一类已经发生过的缺陷；② 不与既有用例重复同一事实（同一事实只留最靠外一处）；
③ 断言的是来源/一致性/结构/边界（不钉字面值、尺寸、文案）；④ 失败时人能说出一句“用户会看到什么坏结果”。

## 项目划分

| 项目 | 负责的“看不见的问题” | 何时跑 |
|---|---|---|
| `HYWDebugAssistant.Tests` | 数据面与队列账本、运行时变更原子性、持久化与格式边界、设置与路径、文本表与图标降级、包加载与来源发现、行为链路 | 常规（默认排除压力/性能） |
| `HYWDebugAssistant.Modules.Tests` | 八个业务模块的契约自检、组帧/编码/长度、外部接入适配件与暂存 | 常规 |
| `HYWDebugAssistant.Tests.Reference` | 人机操作链路判据、多源链路、`Sdk.Testing` 自检“能抓出坏声明” | 常规 |
| `HYWDebugAssistant.ArchitectureTests` | 七层依赖方向、命名空间、排除组件、路径与尺寸唯一来源、注释纪律 | 常规 |
| `HYWDebugAssistant.Benchmarks` | 数值实测（吞吐/延迟/分配），不设阈值 | 按需 |
| `HYWDebugAssistant.VisualSnapshots` | **截图工具，不是测试**：产出 PNG 供人眼 | 按需（不进任何自动化门禁） |

## 三类不进门禁

| 类别 | 标记 / 位置 | 打开方式 |
|---|---|---|
| 压力测试（串口回环、过载响应性） | `[Trait("Category", "Pressure")]` | `.\tools\dev.ps1 -Mode Test -Pressure` |
| 性能测量（分配/内存） | `[Trait("Category", "Perf")]` | `.\tools\dev.ps1 -Mode Test -IncludePressure` |
| 截图工具 | `tests/HYWDebugAssistant.VisualSnapshots` | `.\tools\dev.ps1 -Mode Snapshots` |

压力测试是**功能开发期的临时测试**：改数据面、外部接入、串口、投递循环时跑；相关功能收尾后登记“可删”。

## 命令

```powershell
.\tools\dev.ps1 -Mode Build -Project Core      # T0 单项目编译
.\tools\dev.ps1 -Mode Test -Filter 端口标签     # T1 单类测试（显式过滤词可点到压力用例）
.\tools\dev.ps1 -Mode Test                     # T2 主测试项目（不含压力/性能）
.\tools\dev.ps1 -Mode Test -AllTests           # 全部测试项目（不含压力/性能/截图）
.\tools\dev.ps1 -Mode Snapshots                # 生成截图（不算通过/失败）
.\tools\dev.ps1 -Mode Gate                     # T3 完整门禁（= build.ps1）
```

## 截图（替代 UI 断言）

```powershell
.\tools\dev.ps1 -Mode Snapshots
```

输出在 `artifacts\snapshots\`：`overlay\`（叠加层三窗 + 契约冲突红边 `conflict-*.png` + 建网预览粗边 `dock-highlight.png`）、
`color-picker*.png`、`modules\<模块>.png`（每个页签各一张；含窗口外框与端口占位标记）。
打开看，判断“能不能看、挤不挤、字有没有被裁、外框与端口位置对不对”——**这里没有自动化判据**。

## 预算与准入

| 项目 | 用例预算（含 Theory 行） |
|---|---:|
| `Tests` | 350（其中 Pressure 12 / Perf 2 不进门禁） |
| `Modules.Tests` | 135 |
| `Tests.Reference` | 16 |
| `ArchitectureTests` | 21 |

超预算新增一条 ⇒ 先删一条或写明“它替掉了哪一条的覆盖”。架构规则、包校验、资源审计这类“规则型检查”不受此限（它们不是用例）。

## 手工验收

界面与观感的正确性由人看（清单见仓库内 `dev/acceptance.md`）；测试通过不等于功能可用。

## 相关

- [代码结构](code-structure.md)
- [构建与门禁](build.md)
- [模块测试与验证](../module-dev/testing.md)
