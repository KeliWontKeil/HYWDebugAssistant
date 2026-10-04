# 代码风格与开发约束

> 状态：**现行** —— 实现阶段的强制约束（用户裁决）
> 依据：`上位机架构设计.md` v1.0（顶层基线，验收唯一事实源）· `docs/decisions.md` 7.2（工程纪律 8 项 + 已裁决开关 5 项，本文件来源）· 2.1/2.2（分层与依赖方向）· 7.3/7.4（架构规则集与测试分层）· `docs/architecture/01-layering-and-projects.md`（七层 L0–L6）· `docs/architecture/09-test-and-gate.md`（AR-0~AR-16 / AC-1~6 / T1–T8）
> 效力：与 `docs/architecture/*`、`docs/decisions.md` 或主文档冲突时，**以后者为准**；本文件只规定"怎么写"，不重复规定"依赖怎么走"（见 §2）。
> 追溯：原 ADR-0025/0026/0032 正文见 `docs/archive/adr/`。
> 说明：本文件为**工程纪律**，违反需按 §8 申报，不得静默绕过。

---

## 1. 编码规范

| 项 | 约定 |
|---|---|
| `.editorconfig` | 随解决方案提供（不依赖全局设置）；启用 .NET 官方推荐规则集 |
| 命名 | 类型/成员 `PascalCase`；私有字段 `_camelCase`；接口 `I` 前缀；异步方法 `Async` 后缀；常量 `PascalCase`；**禁止滥用缩写**（`Id`/`Ui`/`Fft` 等公认者除外） |
| 文件组织 | 一文件一公共类型；文件名 = 类型名；文件作用域命名空间（`namespace X;`） |
| 语言特性 | `<Nullable>enable</Nullable>` · `<ImplicitUsings>enable</ImplicitUsings>` · `<LangVersion>latest</LangVersion>` · 允许 `record`/模式匹配/集合表达式等现代特性 |
| 警告 | **`<TreatWarningsAsErrors>true</TreatWarningsAsErrors>`**（可局部 `NoWarn` 但须注明原因）；`<AnalysisLevel>latest-Recommended</AnalysisLevel>` |
| 分析器 | **仅内置 .NET 分析器**（开关 S1）；不引入 StyleCop/Roslynator |
| 禁止项 | `#region`（可用文件分区替代）；`async void`（除事件处理）；`catch` 吞异常；`Thread.Sleep` 于热路径；魔法数字（用具名常量） |
| 代码注释语言 | **中文**（团队语言）；标识符与 API 名保持英文（C# 惯例） |

## 2. 架构约束的自动化

> **规则集不在本文件定义**——唯一来源是 `docs/architecture/09-test-and-gate.md` §4 的 **AR-0~AR-16**（本文件只规定执行方式，避免双份维护导致漂移）。
> **层级与依赖方向**唯一来源：`docs/architecture/01-layering-and-projects.md` §1/§2（七层 L0–L6 + 唯一依赖链）+ `docs/decisions.md` 2.1/2.2。

**执行方式**：

| 项 | 规定 |
|---|---|
| 落地项目 | `tests/HYWDebugAssistant.ArchitectureTests`（**自研，零第三方依赖**，开关 S3；不引入 ArchUnitNET 等外部框架） |
| 依赖类规则（AR-1~AR-12） | **程序集元数据检查**：读已编译程序集的引用表（`MetadataLoadContext` / `System.Reflection.Metadata`）——能覆盖间接依赖 |
| 源码类规则（AR-13~AR-16） | **源码扫描**（业务词汇 / 命名空间与目录一致 / 排除清单 / `Core.Api` 公开签名） |
| 清单类规则（AR-0） | `AR0_产品项目清单应与七层结构一致`——**不在 AR-1~AR-16 清单内**，登记在 `docs/decisions.md`（见 `09-test-and-gate.md` §4） |
| 失败语义 | 目标程序集缺失即**失败**（**禁止**"无对象则跳过"——规则静默失效比误报危险得多） |
| 例外机制 | `// ARCH-EXCEPTION: <原因> · <复查条件>` + 登记到 `docs/decisions.md` **§3**（流程见本文件 §8）；架构测试对已登记的例外按显式白名单放行 |
| 跑动时机 | `build.ps1` 的测试步骤（本地门禁，开关 S4） |

## 3. 数据面性能纪律（热路径）

> 适用"热路径"= 数据单元产生/传递/解析/缓冲/绑定的每单元代码路径。

| 禁止 | 替代 |
|---|---|
| LINQ（`Where/Select/ToList` 等） | `for`/`foreach` + `Span<T>` |
| 装箱（值类型转 `object`）、`params object[]` | 泛型 / 结构体 / 重载 |
| 闭包捕获与委托分配（每单元） | 缓存委托 / 结构体回调 / 静态方法 |
| 字符串拼接与插值（热路径） | `Span`/`StringBuilder`（非热路径才用插值） |
| 每单元 `new`/`byte[]` 分配 | `ArrayPool<T>.Shared` 租还 + 池化缓冲 |
| 异常作控制流 | 返回值 / Try 模式 |
| `Task` 分配（每单元） | `ValueTask` |

**度量与回归**：`HYWDebugAssistant.Benchmarks`（BenchmarkDotNet，**不进常规门禁**，`build.ps1 -RunBenchmarks` 按需运行）建立关键路径基准（吞吐/延迟/分配）；**热路径改动需给出分配数据**。

## 4. 提交与审查

| 项 | 约定 |
|---|---|
| 分支 | **单人直提 `main`**（开关 S4）；`main` 始终保持可构建、可测试 |
| 提交信息 | **Conventional Commits**：`feat:` `fix:` `docs:` `refactor:` `test:` `chore:` `perf:`（正文说明"为什么"） |
| 提交前门禁 | 运行 `build.ps1` 全绿 |
| 变更自查清单 | ① 是否违反 v1.0 冻结结论？② 是否新增依赖（过许可门禁 + 记录）？③ 是否触碰热路径（分配检查）？④ 是否需要 ADR（§5 六种情形）？⑤ 文档/注释是否同步？ |

## 5. 文档要求

| 项 | 约定 |
|---|---|
| XML 文档注释 | **SDK 面向模块作者的公共 API 必须写**；Core/UI 内部公共成员按需 |
| 模块包 Manifest | 必填字段校验：身份/版本/接口声明/配置 Schema/生命周期/视图声明 + 实现入口/依赖/框架兼容范围（`docs/decisions.md` 3.3/3.7；细节见 `docs/architecture/02-module-sdk.md`「模块包与发现」）；缺失即拒绝加载 |
| **必须写 ADR 的情形** | ① 影响 v1.0 冻结结论；② 新增核心概念；③ 引入新依赖；④ 改变模块契约（SDK）；⑤ 性能策略重大变更；⑥ 例外豁免（`docs/decisions.md` 7.2） |
| 不需要 ADR | 纯实现细节、重构、样式、局部 bugfix |
| 文档语言 | 中文；`docs/` 与代码注释一致 |

## 6. 测试要求

> 规则集与映射的唯一来源：`docs/architecture/09-test-and-gate.md`（§4 AR-0~AR-16 + §1/§2 AC 判定 + §3 T1–T8 与承载项目）。

| 项 | 规定 |
|---|---|
| **模块验证（T6）** | 契约测试套件**提供且推荐（不强制）**（用户裁决，见 `09-test-and-gate.md` §3）：声明完整性/原语合法性/钩子实现/不跨模块引用 |
| **组合验证（T1）** | Core 机制改动必须有**参考组合**级验证（**现行载体 = 四个正式模块 + 判据项目内测试桩**：数据源 → 变换 → 记录/判据汇）——纪律 C1（见 `implementation/00`；载体表见 `09-test-and-gate.md` §2） |
| 持久化（T4） | round-trip 无损 + 未知字段保真 + 缺失/损坏路径 + **应用级设置**（缺失/损坏不阻断启动、未知字段保留、原子写、在线生效） |
| 故障注入（T5） | 异常不逸出边界、慢消费者丢帧、**停止超时路径**（孤儿线程 + Issue）、队列满丢旧 |
| UI 验证 | **T8 自动化 v1 不做**（用户裁决，见 `09-test-and-gate.md` §3）；**UI 应用层面由用户手工验证**，实现期须交付可执行手工验收清单（现行载体：`implementation/04-m1-manual-acceptance.md`） |
| 覆盖率 | **不设硬性百分比**；以"机制改动必须有组合级验证"为准 |
| 命名 | `Method_Scenario_ExpectedResult` |
| 原则 | **简单快捷**：自动化只覆盖主要问题，不追求全覆盖（见 `09-test-and-gate.md` §6） |

## 7. 依赖引入纪律

新增依赖必须同时满足：① **许可** ∈ {MIT, Apache-2.0, BSD}（构建期门禁自动检查，含传递依赖）；② 说明**不可替代性**（自研成本 vs 收益）；③ 记录到 ADR 或 `docs/decisions.md`；④ 在 `Directory.Packages.props` **锁定版本**。
**禁止**：GPL/LGPL、任何需采购/登录授权的组件、**无许可声明**的包（首例已拦截：`AvaloniaUI.DiagnosticsSupport`）。
> 门禁实现与白名单例外登记方式见 `docs/architecture/10-delivery-and-ops.md` §3。

## 8. 例外与豁免

违反任一约束须显式申报：代码注释 `// ARCH-EXCEPTION: <原因> · <复查条件>` + 登记到 `docs/decisions.md` **§3（架构例外登记）**；**禁止静默绕过**。

| 项 | 规定 |
|---|---|
| 登记载体 | `docs/decisions.md` **§3**（**唯一登记处，追加式**：不删行，状态改为「已关闭」并注明日期；代码注释只是索引，不构成登记）。每条含：日期 · 约束编号 · 位置 · 原因 · 复查条件 · 处置人 · 状态 |
| 审批 | 单人项目：由开发者自行登记，但**必须在提交前完成登记**（缺登记视为门禁失败） |
| 复查 | 每个里程碑收尾（M1/M2/M3…）复查一次；过期例外须关闭或续期（历史复查记录见 `docs/decisions.md` §3.3/§3.4） |

## 9. 已裁决的开关

| # | 开关 | 裁决 |
|---|---|---|
| S1 | 第三方分析器 | **仅内置 .NET 分析器**（不引入 StyleCop/Roslynator） |
| S2 | 警告即错误 | **开启** `<TreatWarningsAsErrors>true</TreatWarningsAsErrors>`（局部 `NoWarn` 须注明原因） |
| S3 | 架构测试实现 | **自研**（程序集元数据 + 源码扫描，零第三方依赖；规则集 = AR-0~AR-16，见 `09-test-and-gate.md` §4） |
| S4 | 分支策略 | **单人直提 `main`** + 提交前门禁（`build.ps1` 全绿） |
| S5 | 注释语言 | **中文**（代码注释与 XML 文档；标识符与 API 名英文） |
