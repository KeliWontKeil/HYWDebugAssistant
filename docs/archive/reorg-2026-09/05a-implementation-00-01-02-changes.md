> ⚠ 历史材料（过程记录，2026-09 归档）。**现行状态**见 `docs/roadmap.md`（状态唯一源）与 `docs/README.md`。

# 变更记录：`implementation/00` · `01` · `02`（本轮文档收敛）

> 范围：**只改这三份文件**（`docs/implementation/00-pre-implementation-constraints.md` · `01-coding-style-and-dev-conventions.md` · `02-engineering-baseline.md`）。
> 依据：本目录 `01-architecture-assessment.md` · `03-process-and-lessons-assessment.md`（§1.2 / §1.3 / §1.4 / §2.1–§2.4）· 用户本轮指令（压缩去重、P 序号升序重排、引用改指现行分册、状态行统一）。
> 现行引用目标：`docs/architecture/00–10`（新分册名）· `docs/decisions.md`（条目号 / R 索引 / §3 例外登记）· `docs/archive/adr/`（ADR 原文）· 工程仓 `DEVELOPMENT.md` / `LESSONS.md`。
> 逐条记号：**移出** = 该轮被删的具体事实在现**行文档**中仍有承载（并给出落点）；**保留** = 只此一处，按"绝不丢信息"留在本表。

---

## A. `00-pre-implementation-constraints.md`（压缩重写）

| # | 位置 | 处理 | 依据 | 信息移出 |
|---|---|---|---|---|
| A1 | 标题 / 头部 | 标题改为"实现前置约束：三条验证边界纪律（00）"；头部状态行换成统一状态行，并加"本文件只保留 C1–C3"的适用范围声明 | 任务要求的状态行；审计 §1.2 处置 1 | 无（原头部"关联：ADR-0023/0024、`05-environment-checklist.md`、`06-library-selection.md`、主文档 §14"为索引，改由 §依据 + 各纪律"现行落点"行承载，且这些文件本轮已在归档/精简范围内） |
| A2 | 原 §1 C1/C2/C3 行 | **保留为文件主体**，每条展开为「是什么 / 为什么 / 违反的后果 / 怎么自查 / 现行落点」五格，内容与原表逐条等价 | 审计 §1.2："C1–C3 是唯一以'用户裁决'形式固定的验证边界纪律，价值高" | 无 |
| A3 | 原 §1 C4（代码风格约束 + 开发约束） | **删除该行** | 审计 §1.2：C4 已兑现（ADR-0025 + `implementation/01`） | **已移出**：落点 = `implementation/01` 全文 + `docs/decisions.md` 7.2（`01` 头部已声明其来源） |
| A4 | 原 §1 末注（C1+C2 与 T1/T6 一致） | 并入 C1/C2 的"现行落点"格，改指 `09-test-and-gate.md` §1–§3 | 引用改写要求 | 无（"T1 = 判据测试、T6 = 模块契约测试套件"两处编号在 `09` §3 逐条保留） |
| A5 | 原 §2（环境就绪后的议程 1–5） | **整节删除** | 审计 §1.2：§2 议程 1–3 已完成、4–5 由 `09` 的 M1 步骤接管 | **已移出**：议程产物落点 = `implementation/01`（风格与约束）、`docs/decisions.md` 类 2/3/5/6（分层/SDK/机制/UI）、`09-test-and-gate.md` §10（M1-0…M1-17 交付边界与验收判据）、`01-layering-and-projects.md`·`02-module-sdk.md`·`04-runtime-mechanisms.md` 等分册 |
| A6 | 原 §3（风格与开发约束议题清单 8 项） | **整节删除** | 审计 §1.2：8 项议题已逐条由 `01` §1–§8 与 ADR-0025/0026/0032 兑现 | **已移出**：落点 = `implementation/01` §1–§8（逐项对应）+ `docs/decisions.md` 7.2/7.3/7.4；原 §3 的具体编号引用（开关 S1/S3/S4、P12、`build.ps1 -RunBenchmarks`）在 `01` 与 `09` 中均有 |
| A7 | 原 §4（当前状态） | **整节删除**（含"下一步：进入 M1 实现"） | 审计 §1.2：状态只应在 `roadmap.md` 与 `docs/README.md` 维护，且该段早已过时 | **已移出**：落点 = `docs/roadmap.md`（状态唯一源） |
| A8 | 新增「与其它文档的关系」 | 加 3 行指向 `09-test-and-gate.md` / `decisions.md` / `01` + 工程仓 `DEVELOPMENT.md` | 引用改写要求 | 无 |

**结果**：53 行 → 46 行（≤60 行目标达成）。**唯一"只此一处"的事实**：C3 的"原型必须**独立搭建**（独立目录/独立方案，不参与主构建、不被工程引用）"——`09-test-and-gate.md` §6 只写"不做一次性原型"，未写独立搭建条件，故**保留在本文件**并按纪律陈述。

---

## B. `01-coding-style-and-dev-conventions.md`（基本保留 + 修引用）

| # | 位置 | 处理 | 依据 | 信息移出 |
|---|---|---|---|---|
| B1 | 头部 4 行 | 状态行换成统一状态行（保留"用户裁决 · 议程 ② · 实现阶段强制约束"）；依据改为 `decisions.md` 7.2 / 2.1·2.2 / 7.3·7.4 + `01-layering-and-projects.md` + `09-test-and-gate.md`；效力行加入"与 `decisions.md` 冲突以后者为准"；加一行 ADR 原文归档锚点 | 任务要求；旧编号 `docs/architecture/01`/`06` 必须改写 | 无（ADR-0025/0026/0032 编号保留在新依据 + 归档锚点中） |
| B2 | §2 引言的"唯一来源"两行 | `architecture/06-test-architecture.md` §6.3 → `09-test-and-gate.md` §4；`architecture/01-layering-and-dependencies.md` → `01-layering-and-projects.md` §1/§2 + `decisions.md` 2.1/2.2 | 引用改写要求 | 无 |
| B3 | §2 执行方式表 | **新增一行 AR-0**（`AR0_产品项目清单应与七层结构一致`，不在 AR-1~AR-16 清单内，登记在 `decisions.md`） | 现行规则集是 **AR-0~AR-16**（`09` §4），原文只提 AR-1~AR-16，属"与现行分层/规则集口径不一致"的失效点 | 无（新增信息，非移出） |
| B4 | §2 例外机制行 | "登记到 `docs/decisions.md`（§8）" → "登记到 `docs/decisions.md` **§3**（流程见本文件 §8）" | `decisions.md` §3 = 架构例外登记（唯一登记处），原文的"§8"易被读成 decisions.md 的小节，属**失效指向** | 无 |
| B5 | §5 模块 Manifest 行 | ADR-0020/0028 → `decisions.md` 3.3/3.7 + `02-module-sdk.md`「模块包与发现」；"模块 Manifest"改称"模块包 Manifest" | 引用改写要求；新分册口径为"模块包" | 无（必填字段清单逐项保留） |
| B6 | §5 必须写 ADR 的情形行 | 加出处 `decisions.md` 7.2 | 引用改写要求 | 无（六种情形逐项保留） |
| B7 | §6 引言 + 表内 4 处 | `architecture/06-test-architecture.md` → `09-test-and-gate.md`（§4 AR-0~AR-16 + §1/§2 AC + §3 T1–T8）；删去 ADR-0032 的 Q6-4/Q6-3 编号（改为"用户裁决，见 `09` §3"，结论"T6 提供且推荐不强制""T8 v1 不做"逐字保留）；"纪律 C1"加指向 `implementation/00`；UI 手工验证补现行载体 `implementation/04`；"原则"行加出处 `09` §6 | 引用改写要求；Q6-x 属归档 ADR 内部编号 | 无 |
| B8 | §7 依赖引入纪律 | 保留 4 条硬约束与禁止清单；**新增 1 行指针**指向 `10-delivery-and-ops.md` §3 + `tools/license-allowlist.json` | 引用改写要求（门禁细节的现行落点在 `10` §3） | 无（"首例已拦截 `AvaloniaUI.DiagnosticsSupport`"事实保留） |
| B9 | §8 例外与豁免 | 登记载体改为 `docs/decisions.md` **§3**（唯一登记处、追加式、不删行）；字段清单补齐为现行 7 列（日期·约束·位置·原因·复查条件·处置人·状态）；复查行加 `decisions.md` §3.3/§3.4 指针 | `decisions.md` §3.1 的现行格式；原写法"单一文件，追加式"已不足以定位登记处 | 无（原字段"日期·约束编号·原因·复查条件·处置人"全覆盖，且补上"位置/状态"） |
| B10 | §9 开关 S3 行 | 规则集 `AR-1~AR-16` → `AR-0~AR-16`（+ 指针 `09` §4） | 同 B3 | 无 |
| B11 | §1 / §3 / §4 | **未改动**（无失效规则、无旧编号引用） | 审计 §1.3：本文件是"把清单指出去"的防重复范本，保留原样 | 无 |

**结论**：无内容删除；共 10 处处理，全部为引用改写 + 2 处口径修正（AR-0 补齐、例外登记处 §3）+ 1 处格式对齐。

---

## C. `02-engineering-baseline.md`（压缩 + P 表重整）

| # | 位置 | 处理 | 依据 | 信息移出 |
|---|---|---|---|---|
| C1 | 头部 | 状态行换成统一状态行；依据改为 `decisions.md` 7.1/7.2/7.3 + `10-delivery-and-ops.md` + `09-test-and-gate.md` §4/§5；加"工程内事实唯一事实源 = 工程仓 `DEVELOPMENT.md`"声明 | 任务要求 | 无 |
| C2 | 原 §1（9 行组件表） | **压缩为"当前生效的工程基线（清单）"10 行表**：项 / 现行基线 / **唯一事实源**（工程仓文件或现行分册）。保留的具体事实：SDK 10.0.401 + `rollForward/allowPrerelease`、`net10.0`、`TreatWarningsAsErrors`/`AnalysisLevel`/`EnforceCodeStyleInBuild`、CPM、Avalonia 12.1.2 / CommunityToolkit.Mvvm 8.4.2、`.editorconfig`、`build.ps1` 六步、许可审计机制、豁免 1 条（`xunit.abstractions` 2.0.3）、七层=程序集/`App` 唯一白名单/`.slnx`、架构测试自研 + 缺失即失败、基准不进常规门禁 | 任务："与 `DEVELOPMENT.md` 重复的部分改成指针"；字段按"现行值 + 去哪看"重构 | **已移出（有承载）**：门禁步骤细节 → `DEVELOPMENT.md` §1–§5 + `10` §2.1；许可门禁实现 → `10` §3.1–§3.3；规则集 → `09` §4；项目结构 → `01` §4–§6；测试资产/计数 → `09` §3/§7；模板默认值清理与 `A1–A6` 旧规则 → 本文件 P3/P5/P13 + `09` §4（"取代 A1–A6"）。**保留**：Avalonia 12.1.2 / CommunityToolkit.Mvvm 8.4.2 / SDK 10.0.401 / 豁免 1 条等"具体值"以"以工程文件为准"的措辞留在表内 |
| C3 | 原 §2（使用方式 3 条命令） | 改为 3 行指针（唯一事实源 = `DEVELOPMENT.md` §1/§2/§5 + `dev.ps1`）：三条命令名保留，另加"T3 触发时点见 `09` §5.3"与"Accelerate 遥测提示不影响构建"两行 | 任务："与 `DEVELOPMENT.md` 重复的部分改成指针" | **已移出**：命令的完整语义与开关 → `DEVELOPMENT.md` §5；T3 三种触发情况 → `09` §5.3。**保留**：`build.ps1` / `-SkipTests` / `-StopDesignerHost` 三个命令名 |
| C4 | 原 §3 引言（来源说明） | 来源说明**补全到 P65**：P1–P15 环境与骨架 · P16–P19 机制落地 · P20–P40 M1-16 十六轮 UI/交互返工 · P41–P44 ADR-0050 R2-a/R3 · P45–P65 多源链路/角色投影/类型颜色/调色板；ADR-0050 改指 `05-network-semantics.md` §3/§6 | 审计 §2.1 事实 B（来源说明只 cover 到 P44，已脱节） | 无 |
| C5 | 原 §3 表 | **P1–P65 全部保留、序号升序重排（1→65 严格递增、无缺号无重号）**，含义逐条不变；列名沿用原表 4 列（问题 / 现象 / 处理），**新增"分类"列** | 任务："序号重排成升序；不得改变任何条目的含义"；"保持每条四列语义（原列名不同就保留原列名，只补分类标签）" | 无（65 条一条不少） |
| C6 | 原 §3 表 · 45 条通用纪律 | 标 `[通用工程纪律]` 并**压成"一句话 + 指针（见 `LESSONS.md` 第 N 条）"**；指针号取自审计 §2.4 的 44 对对照表（P17/P18 共指 UI 刷新纪律，故 45 条对 44 对） | 任务："通用工程纪律只在 `LESSONS.md` 保留完整叙述，本表该条压成一句话 + 指针" | **已移出（有承载）**：完整现象/根因/修法与"通用规则" → 工程仓 `LESSONS.md` 第 10–12、16–17、19–25、36、38–44、49、54、56–59、61–67、69–74 条（逐条对应见各行的指针）；其中已进现行决策表的重要取证 → `decisions.md` R2/R5/R10/R12（已在行内一并标出）。**保留在本表**：被压条的**关键锚点**（如 `CollectionSync.Sync`/`PanelRefreshStabilityTests`、`ScreenPixels.Round`、`DwmGetWindowAttribute`、`x:Double`、`DashStyle.Offset`、`MinIntervalSeconds`、`VisualSnapshots`、`ColorView`、`Problems` 空=合法）与一句话纪律本身 |
| C7 | 原 §3 表 · 20 条项目特例 | 标 `[本项目特例]` 并**保留完整信息**（现象/根因/修法逐字保留） | 任务："本项目特例必须在本表保留完整信息（那些数值/文件名是只此一份的）" | 无（这 20 条是 P1–P15、P19、P27、P28、P29、P35） |
| C8 | 原 §4（验证结果快照） | **删除数字快照**（还原/构建许可审计"45 个包"/架构测试 5+冒烟 3），改为"**以 `.\build.ps1` 的实跑输出为准**"，并指向 `09-test-and-gate.md` §5.2（最近一次 T3 事实记录）与 §7（测试资产清单） | 任务："§4 验证结果已失效（写着 45 包，实际已 75 包）：删掉具体数字" | **已移出**：现行数字 → `09` §5.2（75 包 / 测试计数）与 §7；命令数量说明 → `DEVELOPMENT.md` |
| C9 | 原 §4 末注（Accelerate 遥测提示、`technology-selection/05` §7） | 保留该事实，出处改指 `10-delivery-and-ops.md` §3.2 | 引用改写（`technology-selection/*` 已归档） | **已移出**：环境实测 E1–E6 明细 → `10` §3.2/§1（`05-environment-checklist.md` 的现行落点） |

**结果**：65 条 P 记录 = **通用工程纪律 45 条（压成指针）** + **本项目特例 20 条（完整保留）**；文件 45,925 字节 / 123 行 → **25,979 字节 / 113 行**（体积 −43%；行数微增来自新增"分类"列与升序重排后的可读性）。

> **改动前版本的存放状态（供归档核对）**：改动前的工作区版本（P1–P65 / 45,925 字节）**未提交**——`git show HEAD:docs/implementation/02-engineering-baseline.md` 是更早的 62 行 / 8 条 P 版本，`git show :docs/implementation/02-engineering-baseline.md`（索引区）是 105 行 / 47 条 P 版本。**若归档直接从 git 取原文，会取到旧版**；三份文件改动前的完整原文只会来自本轮改动前的快照（或本记录所指的读取内容）。已在本轮按"65 条一条不少"重建，故现行文档无信息缺口。
> **保真核对**：20 条特例行已逐条与索引区原文做过字符串比对，除"分类标签""ADR 编号改写为 `decisions.md` 条目号/归档路径""补指针"三类改动外**逐字一致**。

### 保留在本表、别处不能再找到的具体事实（按类）

1. **环境变量名与其被污染值**：`Platform`（Python 安装遗留，值 = Scripts 路径）→ MSB4126（P1）。
2. **进程/宿主的精确名称**：`Avalonia.Designer.HostApp`、MSB3021/MSB3027（P2）。
3. **编译器/分析器规则编号与其处置**：IDE0005/`EnableGenerateDocumentationFile`（P4）、IDE0040/IDE0011（P5）、CA1707/CA1859/CS1591（P6）、CS0122 + `InternalsVisibleTo`（P8）、CS8209 + `IPointer.Capture` 返回 void（P10）、AVLN2000 + `x:CompileBindings="False"`（P9）、AVLN5001 + `TextBox.Watermark`→`PlaceholderText`（P13）。
4. **许可门禁的实测误判样本**：`Avalonia.Angle.Windows.Natives`（`<license type="file">`）与 `xunit.abstractions`（仅 GitHub raw licenseUrl）（P7）。
5. **本项目代码级标识**：`ViewLocator.Match` 的缓存条件（P11）、`ConfigurationJson.Options` 的两个 `JsonSerializer` 选项 + "±∞ 表示未设上下限"（P15）、`ModulePackageOrigin.NeedsRestart`（P19）、`showPortMarkers`→`showInfoOverlay`（P27）、层级顺序与对比度 ≈1.02（P28）、`PublishActualSize` 与"元素类型→高度表"（P29）。
6. **Win32 互操作与实测窗框差**：`DwmGetWindowAttribute(DWMWA_EXTENDED_FRAME_BOUNDS)` 三级回退 + `DragSelfTest.MeasureShellEdge` + 可见边框宽 ≈1px（P35）。
7. **测试噪声的实测数值**：JIT 首调固定 **40 字节** 与"翻倍不新增分配"的判据改写（P12）。
8. **一次工具事故的完整形状**：PowerShell `@(('a','b'))` 数组陷阱 + `.Replace('n','e')` 把源码写坏（P14）。

> 说明：以上各类在 `LESSONS.md`（1–74 条）中**均无对应条目**；在 `decisions.md` 的 R 索引中也只有部分旁证（如 R12 对 P15-family 的"读即登记"），故按"绝不丢信息"留在本表。

### 遗留问题

1. `docs/archive/adr/` 与 `docs/architecture/00–10` 的最终落位由本轮收敛的其它步骤完成；本文件已按新分册名写引用，**若分册名或小节名再调整，需同步本文件 §参照**（已在各表以"分册 + 小节名"形式写明，便于批量核对）。
2. `LESSONS.md` 条目 **52 与 55 仍逐字重复**（审计 §1.10 处置 1）——属工程仓文件，不在本轮授权范围；被压成指针的 P56/P57 等条目不受影响。
3. `09-test-and-gate.md` §11【待裁·09-2】记录了"测试分层 T1–T8 与验证层级 T0–T3 同名不同义"，本文件 §1/§2 同时引用两套编号，待该裁决后如改名需同步。
4. P 表分类判据（"`LESSONS.md` 是否完整覆盖"）若将来 `LESSONS.md` 增删条目，需回来复核 45 条指针的条号。
