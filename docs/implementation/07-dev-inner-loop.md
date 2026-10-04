# 开发内环：分层验证 · 命令速查 · 已知摩擦

> 状态：**现行**（本文件是**开发内环（改代码 → 验证）的唯一落点**；由仓库根 `DEVELOPMENT.md` 收敛而来，原文已删除）
> 定位：**"怎么改代码、怎么验、花多少代价验"** —— 四层验证 T0–T3、命令速查、本工程实测踩过的摩擦与规避、改代码的固定动作、提交与回滚纪律。
> 分工：**"做什么、验收什么、文档怎么改"** → `../README.md`（文档树）+ `03-development-workflow-and-coding-details.md`（流程与判据）+ `../DOC-WORKFLOW.md`（文档自己怎么管）；**验证层级在架构上的位置（门禁构成、触发时点、手工验收）** → `../architecture/09-test-and-gate.md` §5/§5.4；**框架与平台的具体坑** → `02-engineering-baseline.md` 的 `P##` 表。
> 命令速查的**权威实现**是同目录的 `tools/dev.ps1`（其头注释与各 `-Mode` 分支）——本文件与它冲突时以脚本为准。

---

## 1. 核心原则：**分层验证**（不要每改一行就跑全量）

| 层级 | 何时用 | 命令 | 典型耗时 |
|---|---|---|---|
| **T0 单项目编译** | **每次小改**（改完即编，最早发现编译/分析器错误） | `.\tools\dev.ps1 -Mode Build -Project Core` | 1–3 s |
| **T1 单类测试** | 改了某块逻辑 / 某个测试类 | `.\tools\dev.ps1 -Mode Test -Filter 端口标签` | 3–6 s |
| **T2 单测试项目** | **一个增量收尾**（如"修好闪烁"） | `.\tools\dev.ps1 -Mode Test` | 4–8 s |
| **T3 完整门禁** | **仅在 §2 的三种情况** | `.\tools\dev.ps1 -Mode Gate` | 25–40 s |
| **UI：只编 + 跑起来看** | **纯 XAML/样式/文案/间距改动** | `.\tools\dev.ps1 -Mode Build -Project FrameworkUi`（或 `Ui`）→ `-Mode Run` | 3–8 s |

**纪律**：

1. **批量改完再编译**（不要"改一行编一次"）；
2. **T3 不是"收尾仪式"**：只有 §2 的三种情况才跑；
3. **纯 UI 改动不做"测试一轮"**：XAML / 样式 / 文案 / 间距 → **T0 编译 + `-Mode Run` 眼睛看**即可，**不跑** T2/T3、**不跑**整套测试；只有**连带改了逻辑**（视图模型、命令、数据结构）时，才按 T1 跑**受影响的那个测试类**（如 `-Filter FrameworkUiTests` / `-Filter ModuleWindowVisual`）；
4. **报告结论时必须区分"我跑了哪一层"**，不得把 T0/T1 的通过说成"全量通过"。

---

## 2. 什么时候**必须**跑 T3 完整门禁

1. **要把东西交给用户手工验证之前**（用户会按验收清单点，不能带着未验证的构建）；
2. **改了验收事实源相关的东西**：`Sdk` 公开面 · `Core.Api` 契约 · 设置/组合件文件格式 · 架构规则与项目引用（AR-0~AR-16）· 依赖（`Directory.Packages.props`）；
3. **一个步骤（M1-xx）宣布完成时**（写状态回写之前）。

其余情况一律 T0/T1/T2。**里程碑级文档回写**通常与第 3 条同时发生。

> **【用户裁决 · 当前生效的例外】** **T3 完整门禁暂不跑**：用户明确"后续还有大量的 UI 调整和模块调整；**完成最小验收指标（所有基础功能和最小测试模块就绪）后，再跑完整门禁。完整门禁现在跑没有意义。**"
> ⇒ 在"最小验收指标就绪"之前，**UI / 交互 / 模块的改动一律只跑 T0 + 受影响的 T1/T2**；T3 与 `implementation/04` 的手工验收一起**推迟到那个时点**（路线与状态见 `../roadmap.md`；门禁构成的架构口径见 `../architecture/09-test-and-gate.md` §5.3）。

---

## 3. 已知摩擦与规避（都是本工程实测踩过的）

| 摩擦 | 症状 | 规避 |
|---|---|---|
| **VS 的 Avalonia 设计器宿主锁定输出 DLL** | 构建刷一堆 `MSB3026`，每条等 1 秒重试，最后失败 | `dev.ps1` **默认先停掉它**；手工构建时用 `.\build.ps1 -StopDesignerHost` |
| **`Platform` 环境变量被污染** | `MSB4126`（把 `Platform` 当目标平台） | `dev.ps1` / `build.ps1` 都会先清空它 |
| **`PLATFORM` 环境变量被污染** | 直接 `dotnet build` 报 `MSB3191` / `MSB4018`，中间产物被写到 `obj\<某个无关路径>\` | **别手工跑裸 `dotnet build`**：用 `dev.ps1` / `build.ps1`（它们会清 `Platform`）。必须手工跑时先 `Remove-Item Env:Platform -ErrorAction SilentlyContinue`（本机实测该变量被 Python 安装器写成了 `…\Python39\Scripts`） |
| **PowerShell 对源码做字符串替换** | 正则 / 数组语法写错 → **文件被改坏** | **禁止**用 PowerShell 改源码；一律用编辑器 / `edit` 工具（工程坑位见 `02-engineering-baseline.md` P14） |
| **应用正在运行**（手工验证时） | 构建要写 App 输出 → 被运行中的进程锁住，刷 `MSB3026 ×10` 才失败 | `dev.ps1` **快速失败并提示**（不会白等）；先 T0 编好改动项目，再用 `-Mode Test -SkipAppRebuild`（只重编测试项目，不碰 App 输出）；需要时可 `-StopApp`（**会丢失应用的运行现场**） |
| **全量测试输出很长** | 刷屏、难以定位 | `dev.ps1` 用 `-v q`；失败时再加 `--filter` 定位单个测试 |
| **每帧重建 UI 列表** | 悬停乱闪、CPU 空转 | 列表一律走**增量同步**（`CollectionSync`）；查询面不做文件系统访问（见 `02-engineering-baseline.md` P17–P19） |
| **`VisualSnapshots` 并发假失败** | `-Mode Test -AllTests` 下 `ColorPickerDialogSnapshotTests` 报红，单独跑却 4/4 全绿 | **已在脚本里解决**：`dev.ps1 -Mode Test -AllTests` 与 `build.ps1` 改为**逐个串行**跑测试项目、**快照项目最后单独进程**跑。**手工跑时也要一个一个来** —— `AvaloniaHeadlessPlatform` 是进程级配置，与其它测试项目并发会互相破坏真实 Skia 渲染 |

---

## 4. 改代码的固定动作

1. **先读**要改的文件（不要凭记忆改）；涉及契约 / 格式的先看 `../decisions.md` 里对应决定；
2. 一次改动 = 一个主题（不夹带无关重构）；
3. **T0** 编译 → **T1/T2** 测试 → 需要时补一个能**复现原缺陷**的回归测试；
4. 改了会影响用户可见行为的东西 → 在 `04-m1-manual-acceptance.md` 增 / 改验收条目；
5. 交付 / 回写时跑 **T3**，并在回复里给出**可复现的验证步骤**；
6. 代码注释用中文；异常不逸出边界；热路径零分配；**模块只引 `Sdk`**（AR-1/AR-2）。

---

## 5. 命令速查

```powershell
.\tools\dev.ps1 -Mode Build -Project Ui        # 只编译 Ui（含其下游被引用项目）
.\tools\dev.ps1 -Mode Build                    # 编译整个解决方案
.\tools\dev.ps1 -Mode Test -Filter 自动建网     # 只跑名字含"自动建网"的测试
.\tools\dev.ps1 -Mode Test                     # 跑主测试项目（T2）
.\tools\dev.ps1 -Mode Test -AllTests           # 跑全部测试项目（不含许可审计）
.\tools\dev.ps1 -Mode Test -SkipAppRebuild     # 应用正在跑时的应急：只重编测试项目（先 T0 编好改动项目）
.\tools\dev.ps1 -Mode Gate                     # 完整门禁（= build.ps1，含许可审计）
.\tools\dev.ps1 -Mode Tree                     # 列出解决方案里的项目（挑 T0 目标用）
.\tools\dev.ps1 -Mode Stop                     # 只停 Avalonia 设计器宿主
.\tools\dev.ps1 -Mode Run                      # 构建并启动应用（手工验证用）
```

**参数语义**（完整定义以 `tools/dev.ps1` 为准）：

- `-Project <名片段或 csproj 路径>`：T0 的目标项目；不传则编译整个解决方案；
- `-TestProject <名>`：T1/T2 换测试项目；
- `-Filter <词>`：按测试名过滤；**过滤词没匹配到任何测试不算失败**（脚本会提示换词）；
- `-AllTests`：跑全部测试项目，**不含**许可审计；**串行**执行，快照项目单独进程；
- `-SkipAppRebuild`：应用正在运行时的应急路径 —— 只重编测试项目，前提是改动过的项目已 T0 编译过；
- `-StopApp`：直接停掉正在运行的应用，**会丢失运行现场**（不要为了跑门禁擅自杀掉用户正在看的实例，先问）；
- `-KeepDesignerHost`：保留设计器宿主不停；
- `-Mode Tree|Stop|Run`：列项目 / 只停设计器宿主 / 构建并启动。

---

## 6. 提交与文档纪律（简）

- **提交信息**：Conventional Commits（`feat:` / `fix:` / `docs:` / `test:` / `refactor:` …）+ 中文描述；
- **分支**：单人直提 `main`（开关 S4，见 `01-coding-style-and-dev-conventions.md` §4）；
- **文档**：设计 / 验收 / 决策事实源 = 本仓库 `docs/`（入口见 `../README.md`）；文档自己怎么管见 `../DOC-WORKFLOW.md`；文档与代码**同一次改动**，回写**每批次一次**；
- **构建产物**（`artifacts/`、`bin/`、`obj/`）**不入库**；模块包由构建自动归拢（见 `src/Modules/Directory.Build.targets`）。
