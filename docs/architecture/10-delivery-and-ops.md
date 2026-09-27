# 交付与运维

> 状态：**现行**（本轮收敛重写；来源见各节标记）
> 适用范围：交付形态与运行前置条件、构建门禁与模块包产出、依赖许可门禁（MIT-only）、诊断信息的产生与去向、错误呈现规范、多实例与残留、v1 明确不做清单
> 与主文档的关系：主文档 `上位机架构设计.md` 是验收唯一事实源；本文件是其实现层分册，冲突时以主文档为准。

> 边界：**测试分层 T0–T3 / 架构判据 / 里程碑处置**见 `09-test-and-gate.md`；**设置键清单与默认值**见 `08-application-settings.md`（本文件不重复维护键表，只给承载与去向）。

## 1. 交付形态与运行前置 〔源: architecture/10 §1/§6/§8 + ADR-0023 §8 + code: global.json / Directory.Build.props / src/App/app.manifest〕

| 项 | 约定 |
|---|---|
| 形态 | **便携包（zip）**：解压即用；**不做 MSI、不做安装程序、不做卸载程序** |
| 目标平台 | **Windows（x64）**；其他平台不作为 v1 交付目标〔源: ADR-0023 §8「Windows 为首要目标，跨平台能力作为保留选项」〕 |
| 便携语义 | 不落注册表 / 不落 AppData / 卸载 = 删除目录 —— 唯一落点见 **§6** |
| 首次运行 | **无向导**（明确不做首次引导）：直接进主界面；`settings.json` 与 `logs\` 按默认值生成（`settings.json` 的实际写盘时机见下方布局表注） |
| 版本标识 | 程序集版本 + 「关于」界面；模块包**各自携带版本三元组**；现行工程版本 = `0.1.0`（`Directory.Build.props`） |

**包根目录布局**（便携包）：

| 路径 | 内容 |
|---|---|
| `App\` | `HYWDebugAssistant.App.exe` 与依赖程序集 |
| `modules\` | 随包内置模块包（1 包 = 1 目录） |
| `logs\` | 运行时生成 |
| `settings.json` | 首次运行生成（现行实现：**首次变更才落盘**，见 `08` §1） |
| `THIRD_PARTY_NOTICES.md` | 第三方许可声明（见 §3） |

**运行前置条件**：

| 场景 | 要求 | 依据 |
|---|---|---|
| 运行（精简包） | 目标机器需 **.NET 10 桌面运行时**；缺失时启动给出**明确提示与下载指引** | `architecture/10` §1 |
| 构建 | **.NET SDK 10.0.401**（`global.json`：`rollForward=latestPatch`、`allowPrerelease=false`）+ **PowerShell 7+**（`build.ps1` 与全部 `tools\*.ps1` 均 `#Requires -Version 7.0`） | `global.json` / `build.ps1` |
| 目标框架 | **`net10.0`**（全部项目，`Directory.Build.props` 统一） | `Directory.Build.props` |
| Windows 版本 | `app.manifest` 声明支持 **Windows 10** 及以上 | `src/App/app.manifest` |

**发布形态** 〔源: architecture/10 §8 C10-1（**用户裁决：两者都发**）〕：

- **framework-dependent**（精简包，需目标机器有 .NET 10 桌面运行时）+ **self-contained**（自包含，开箱即用）—— **两者都发布**。
- **关键事实**：两者**共用同一份代码与工程文件**，差异仅在发布参数（`--self-contained true/false` + RID），**不影响开发**——因此在**发布阶段一次性处理**。
- **旧句已不适用**：`architecture/10 §1` 表里"依赖 .NET 10 桌面运行时（**不发布 self-contained**，避免包体过大）"的表述**已被上述用户裁决取代**，不再作为约束；§1 的目录布局与启动提示需按"两种形态各自处理"。

**发布打包动作** 〔源: code: tools/stage-reference-modules.ps1〕：

- `tools\stage-reference-modules.ps1 -ExportDirectory <目录>` ⇒ 把 `artifacts\modules\` 下**全部**包目录复制到目标目录（先清空目标目录）。
- **不再维护模块清单**——打包只遍历 `artifacts\modules`，新增模块项目自动进入发布包。

## 2. 构建门禁与产物归拢 〔源: code: build.ps1 / tools/stage-reference-modules.ps1 / src/Modules/Directory.Build.targets / src/App/HYWDebugAssistant.App.csproj / Directory.Build.props + ADR-0036 D2/D3〕

### 2.1 门禁实际步骤（`build.ps1`）

| # | 步骤 | 实际动作 | 失败处置 |
|---|---|---|---|
| 0 | 设计器宿主检测 | 检测 `Avalonia.Designer.HostApp`（VS XAML 预览器）是否占用本工程输出；加 `-StopDesignerHost` 则强制停止它 | 不失败，仅警告（潜在 `MSB3021`/`MSB3027`） |
| 1 | 环境清理 | 清除被污染的 `Platform` / `Platform(x86)` 环境变量 | 不失败（否则 MSBuild 报 `MSB4126`） |
| 2 | 还原 | `dotnet restore HYWDebugAssistant.slnx` | **中止** |
| 3 | 构建 | `dotnet build HYWDebugAssistant.slnx -c <Configuration> --no-restore` | **中止** |
| 4 | 模块包校验 | `tools\stage-reference-modules.ps1 -Configuration <Configuration>` | **中止** |
| 5 | 依赖许可审计 | `tools\audit-licenses.ps1 -Root <仓库根>` | **中止**（非零退出） |
| 6 | 测试 | `dotnet test HYWDebugAssistant.slnx -c <Configuration> --no-build` | **中止** |
| 7 | 基准（默认不跑） | `-RunBenchmarks` ⇒ `dotnet run -c Release --project tests\HYWDebugAssistant.Benchmarks` | 仅该步失败时中止 |

- **构建成功 = 0 警告**：`TreatWarningsAsErrors=true`（`Directory.Build.props`，ADR-0025 S2）⇒ 任一路径的编译警告都会让第 3 步失败。
- **基准不进常规门禁**（耗时、数值随机器波动）；数值**只记录、不设阈值**，按 ADR-0033 回写主文档 §13.2 与 `04a` §5。

| 开关 | 默认 | 作用 |
|---|---|---|
| `-Configuration` | `Debug` | `Debug` / `Release` |
| `-SkipTests` | 关 | 跳过第 6 步（只构建 + 许可审计） |
| `-SkipAudit` | 关 | 跳过第 5 步 |
| `-RunBenchmarks` | 关 | 追加第 7 步（Release） |
| `-StopDesignerHost` | 关 | 构建前停止 Avalonia 设计器宿主 |

### 2.2 模块包产出 = 构建即归拢（无脚本清单）

| 环节 | 机制 |
|---|---|
| 判定"是模块项目" | 项目目录下存在 **`module.json`**（**不引入额外开关**） |
| 包目录名 | `$(ModulePackageName)`；缺省取项目名**最后一个点段**，可在模块项目显式指定（参考模块均显式指定为 kebab 名：`data-generator` / `manual-sender` / `monitor` / `recorder` / `asserter` / `fuser`） |
| 模块侧产出 | 模块项目构建后（`AfterTargets="Build"`）⇒ `artifacts\modules\<包名>\`：**先清空该包目录再复制** `$(TargetDir)**\*`（避免新旧程序集混放造成"改了代码没生效"的假象） |
| 应用侧镜像 | `App` 项目构建后 ⇒ `$(OutDir)modules\<包名>\`（镜像 `artifacts\modules\**\*` 全部内容） |
| 新增模块 | **零改动**：新建模块项目（含 `module.json`）即可，不需要改任何脚本 / targets |
| 仓库卫生 | `artifacts/` 已在 `.gitignore`（构建产物不入库）；包物理形式 = **1 包 = 1 目录**（不变） |

**模块包内容与校验规则** 〔源: code: tools/stage-reference-modules.ps1〕：

- 每个包目录必须同时含 **`module.json`** 与清单 `entryAssembly` 指名的**实现程序集**（不齐即校验失败）。参考包实际内容：实现程序集 + `HYWDebugAssistant.Sdk.dll` + `*.deps.json` + `*.pdb` + `module.json`。
- 校验两处**包数必须一致**：`artifacts\modules\`（构建产出）vs `src\App\bin\<Configuration>\net10.0\modules\`（应用输出）；不一致 ⇒ 报错并提示"请重新构建 App 项目"。`artifacts\modules\` 不存在或为空 ⇒ 直接失败（提示先运行 `build.ps1`）。
- 应用输出 `modules\` 是**构建产物**（每次构建重建）——**不要手工往里放东西**。

### 2.3 应用如何找到内置模块 〔源: code: src/App/CompositionRoot.cs + ADR-0036 D1/D3/D4〕

| 项 | 现行 |
|---|---|
| 内置包目录 | `<应用目录>\modules`（`AppContext.BaseDirectory\modules`），**始终位于来源根列表首位、不可移除**，在设置页**不作为可编辑项显示** |
| 用户根列表 | `settings.moduleSourceDirectories`（有序），追加在内置目录之后（见 `08`） |
| 扫描结果 | 每次启动扫描后记一条 Issue（`app:startup`）：装入 N 个 / 失败 M 个 / 根路径列表；失败较多时级别升为 Warning |
| 用户入口 | 工具栏「模块清单」→「可用模块」→「加入现场」；「重扫来源」可发现**本次启动后新增**的包 |
| 改代码后 | **必须重启应用**（热重载明确不做）；包目录写入时间晚于本进程启动时间时给出"需重启才生效"提示 |

**发现边界（明确不做）**：不做磁盘递归 / 全盘搜索；不做隐式仓库探测；不扫 `obj/`、`bin/` 任意深度；**不引入第二套包形式**（zip / 单文件）〔源: ADR-0036 D3〕。

## 3. 依赖与许可门禁（MIT-only） 〔源: ADR-0023 §6 + ADR-0024 §5–§7 + ADR-0025 §7 + code: tools/audit-licenses.ps1 / tools/license-allowlist.json / Directory.Packages.props / Directory.Build.props〕

### 3.1 硬约束

| 约束 | 内容 |
|---|---|
| 允许 | **MIT / Apache-2.0 / BSD 等宽松许可**（具体白名单见下） |
| 禁止 | **GPL / LGPL**、任何**需采购**或**需登录授权**的商业组件、**无许可声明**的包 |
| 范围 | 校验**直接 + 传递依赖**（不是只看顶层 `PackageReference`） |
| 时机 | **构建期门禁**（`build.ps1` 第 5 步）；出现违规**即失败**（非零退出码） |

**明确排除（不得引入）** 〔源: ADR-0024 §5〕：MediatR · AutoMapper · FluentAssertions 8+ · Moq（争议版本）· Qt（LGPL）· DevExpress / Telerik / Actipro / Syncfusion 等商业控件 · Newtonsoft.Json（非必要）· Electron / WebView 方案。

**实际白名单**（`tools\audit-licenses.ps1` 参数 `$Allow`，代码为准）：

```text
MIT · Apache-2.0 · BSD-2-Clause · BSD-3-Clause · ISC · Zlib · MS-PL · Unlicense · 0BSD · CC0-1.0
```

- **SPDX 表达式判定**：按 `AND` / `OR` / `WITH` 拆分为 token，**每个 token 都必须落在白名单内**，整条表达式才算通过（例：`MIT OR GPL-2.0` **不通过**）。
- **许可解析顺序**（逐级回退）：
  1. `<license type="expression">` 的 SPDX 文本；
  2. `<license type="file">` ⇒ 读取该许可文件文本并识别许可族（Apache-2.0 / MIT / BSD-3-Clause / BSD-2-Clause / ISC / Zlib）；
  3. `<licenseUrl>`（`licenses.nuget.org/<SPDX>`、apache.org、opensource.org 的 MIT/BSD-3/BSD-2/ISC、mit-license.org）；
  4. `tools\license-allowlist.json` 中的**显式豁免**。
- **数据来源**：本机 NuGet 缓存 `~\.nuget\packages\<id>\<version>\<id>.nuspec`（**离线，无需网络**）；包清单由 `dotnet list <csproj> package --include-transitive --format json` 逐项目获取，遍历仓库内全部 `*.csproj`（排除 `bin`/`obj`）。
- 审计输出：控制台表格（包名 / 版本 / 许可 / 是否通过）；未通过时附违规清单与 `Note` 列。

### 3.2 白名单例外如何登记

- 文件：`tools\license-allowlist.json`（JSON 数组），每项字段 = **`package` + `version` + `license` + `reason`**。
- 匹配规则：**`package` 与 `version` 同时相等**才豁免（升级版本即自动失效，需重新核对）；脚本**只在该包原本判定为不通过时**才查豁免表。
- `reason` 必须写明**核对证据**（为什么可判为宽松许可、出现在哪条依赖链）。
- 现有登记（全文 1 条）：`xunit.abstractions` 2.0.3 → `Apache-2.0`（旧版 xunit 依赖，nuspec 仅提供 `licenseUrl` 无法自动识别；xunit 系列整体为 Apache-2.0，与 `xunit` 2.9.3 / `xunit.core` / `xunit.assert` 一致；**仅在测试依赖链中出现**）。

**既有拦截案例** 〔源: ADR-0024 修订 v1.1 + code: src/App/Program.cs〕：`AvaloniaUI.DiagnosticsSupport`（Avalonia Accelerate 商业线：nuspec **无许可声明**、Community 版强制遥测）已**从工程移除**（csproj 与 `Program.cs` 的 `WithDeveloperTools()`）。相关事实：Avalonia 12.1.2 核心包为 **MIT、无需登录/授权**；`Avalonia.BuildServices` 为**构建期匿名遥测（MIT、运行时不采集）**，可经 `AVALONIA_TELEMETRY_OPTOUT=1` 关闭；构建输出中的 "Accelerate Community requires telemetry" 仅为提示文本，不影响构建。

### 3.3 版本锁定

- **中央包管理（CPM）**：`Directory.Packages.props`（`ManagePackageVersionsCentrally=true`）是**全部包版本的唯一来源**；项目内 `PackageReference` 不写版本。
- **不生成 lock 文件**：`Directory.Build.props` 显式 `RestorePackagesWithLockFile=false`，全仓**无任何 `packages.lock.json`** ⇒ **依赖可复现性由中央包管理（CPM）+ 版本锁定承担**〔源: 代码 `Directory.Build.props` + 全仓检索无 lock 文件〕。**ADR-0024 §6 关于"依赖锁文件保证可复现"的要求已不适用**。

### 3.4 第三方声明（NOTICE）

| 项 | 约定 |
|---|---|
| 产物 | **`THIRD_PARTY_NOTICES.md`**（随包根目录）——**这是 MIT / Apache-2.0 / BSD 的合规要求，不是可选项**（与"不做隐私声明"不同：隐私声明是产品承诺，NOTICE 是法律要求） |
| 生成 | 由 `tools\audit-licenses.ps1` 的**同一数据源**导出（包名 / 版本 / 许可 / 版权行）；**不手工维护**（避免漂移） |
| 界面 | 「关于」入口显示同名**精简列表**（名称 + 许可 + 链接） |
| 时机 | 每次依赖变更后由门禁重新生成；**发版前校验非空** |

> **落地状态**：**尚未落地**——现行审计脚本只把表格打印到控制台，**没有导出文件的步骤**，仓库内也不存在 `THIRD_PARTY_NOTICES.md`（全仓检索无命中）。见 §8。

## 4. 诊断与可观测 〔源: ADR-0021 §三 + architecture/10 §2/§8 + code: src/Core/Issues/IssueCenter.cs / src/Ui/Diagnostics/DragDiagnostics.cs〕

### 4.1 观测分层 L1–L5

| 层 | 内容 | 呈现 |
|---|---|---|
| **L1 框架诊断** | 加载 / 注册 / 建网 / 绑定 / 增量重建 / 变更事件（结构化日志） | 诊断面板（可选开） |
| **L2 Issue 中心** | 对象标注 + 汇总列表 + 过滤 | 轻量计数 + 面板 |
| **L3 模块日志/状态** | 模块自产日志经框架汇集（分级 / 来源 / 过滤）；业务状态呈现 | 模块视图 + 汇总视图 |
| **L4 数据流诊断** | 网络/口流量速率、**丢弃计数**、最后数据时间 | 口上 / 网络详情 |
| **L5 性能监视** | CPU / 内存 / 模块活动统计 | Gate（v1 留待） |

**原则**：默认轻量不打扰；**L1 / L4 为排障主力**；录波 / 回放属未来能力（Gate）。

### 4.2 日志文件策略

| 项 | 约定 |
|---|---|
| 位置 | 应用目录下 `logs\`（便携语义，**与设置同目录**） |
| 文件名 | `app-YYYYMMDD.log`（**按日期滚动**） |
| 保留 | 最多保留**最近 7 个文件**；超出自动删除最旧〔源: architecture/10 §8 C10-2（设计方推荐；实现期可调）〕 |
| 格式 | 文本单行：`时间 \| 级别 \| 层 \| 对象 \| 事件 \| 详情`（便于人工 tail 与 grep） |
| 写入 | 框架**内置极简 sink**（ADR-0024：`Microsoft.Extensions.Logging` 抽象 + 自研文件 sink，**不引入 Serilog**）；追加写；单文件超 **32 MB** 立即滚动 |
| 级别 | 由应用级设置控制（默认 `Info`）；`Trace`/`Debug` **仅诊断开关打开时**写入 |
| 热路径 | **不逐单元写日志**：用计数 + 定时汇总 |
| **未处理异常兜底** | AppDomain 未处理异常 / 任务未观察异常 → ①写日志（含堆栈）②转 Issue（归属"框架"）③**不静默崩溃**：给出提示并保留现场日志 |
| 崩溃转储 | **不做**（v1 明确不做 dump） |
| 多实例 | 多实例同时运行各自写同一日志文件会交错——**允许**（单行原子追加，不影响可读性）；文件名**不做 PID 区分** |

### 4.3 诊断信息去向（现状）

| 去向 | 现状 |
|---|---|
| **Issue 中心**（已落地） | 内存无锁并发队列：条目 = 自增序号 + 归属对象路径（如 `module:gen-1/out`、`network:net-1`）+ 类别 + 严重度 + 描述 + UTC 时间；取用 = 快照 / 最近 N 条 / 清空。**不落盘**；Issue 面板按**类别**与**严重度**过滤 |
| **日志文件**（未落地） | 全仓**无 `Microsoft.Extensions.Logging` 引用、无文件 sink、无 `logs\` 目录**；`08` 的 `logLevel`/`diagnosticHintThreshold` 亦无消费者（见 `08` §6 落地缺口 G2 / G3） |
| 唯一落盘文件（临时） | `%TEMP%\hyw-drag-diag.log`——`Ui/Diagnostics/DragDiagnostics.cs` 的**临时**拖动诊断（逐帧落盘，单文件 512 KB 上限，定位完成后**整体删除**）。不属交付物 |

> ⇒ **运维含义**：§4.2 的日志文件是设计中**唯一的持久化诊断渠道**；在它落地之前，用户侧"保留现场日志"无处可取，只能靠 Issue 面板（内存）与临时诊断文件。

## 5. 错误呈现与文案规范 〔源: architecture/10 §3〕

### 5.1 呈现渠道

| 渠道 | 内容 |
|---|---|
| **对象标注** | 出问题的对象（实例 / 口 / 网络 / 配置项）上显示角标或状态色 |
| **Issue 面板**（`FrameworkUi`） | 汇总列表 + 过滤（按归属对象 / 类别 / 来源）；默认**轻量不打扰** |
| **轻量提示** | "该缓冲持续为满""处理速度跟不上输入"等**事实陈述**：低饱和、可忽略 |
| **日志** | 全量细节（**不弹窗**） |

### 5.2 文案规范

**结构 = 事实 + 影响 + 建议动作**：

```text
✗ 差：Error 0x1F
✓ 好：模块「串口 #1」未在期限内停止（已超时 5 s）· 该模块线程已放弃等待，其资源已释放
      建议：检查该模块是否有阻塞的硬件调用；可在设置中调大「停止超时」
```

| 规则 | 说明 |
|---|---|
| **不出现错误码** | v1 **不做错误码体系**——用可读描述 |
| 说清归属 | 指出是哪个模块 / 口 / 网络 / 配置项 |
| 说清影响 | 是否影响其他部分（大多数情况：**不影响**） |
| 给可执行建议 | 能做什么（重试 / 重建实例 / 改配置 / 查看日志） |
| 不判断用户意图 | 描述事实，不替用户决定 |

### 5.3 分级与打扰度

| 级别 | 打扰度 | 示例 |
|---|---|---|
| 信息 | 无（仅日志 / 诊断层） | 模块加载成功、网络建立 |
| 注意 | 轻提示（诊断层 / 角标） | 缓冲持续为满、上游无数据 |
| 错误 | Issue 面板 + 对象标注（**不弹模态框**） | 模块异常停机、组合件加载失败 |

**禁止**：模态弹窗打断数据监视；把"上游无输入"报成错误（那是**合法状态**）。

## 6. 多实例与残留 〔源: architecture/10 §6〕

| 项 | 约定 |
|---|---|
| 多实例 | **允许**（不做单实例限制）；各实例共享同一 `settings.json` → **直接覆写，最后写入获胜**（可能丢失另一实例的修改，已接受） |
| 设置冲突提示 | **不做**（用户裁决：直接覆写） |
| 注册表 | **不写**（便携语义） |
| 用户目录 | **不写 AppData**（除 .NET 运行时自身行为） |
| 卸载 | 删除应用目录即完成；**无残留**（无注册表项、无 AppData 目录、无服务） |
| 内置模块被删 | 用户删除某模块目录 → 该模块进入**缺失态** + Issue（**不崩**） |

## 7. 明确不做（与主文档 §18.4 的对应） 〔源: 主文档 §18.4 + architecture/10 §7/§8〕

本文件**不新增"做"的范围**，只把已裁决的"不做"与必要的合规 / 可诊断能力落成约定：

| 主文档 §18.4「明确不做」 | 本文件对应 |
|---|---|
| 安装包（MSI）与卸载程序 | §1（便携包）、§6（删除即卸载） |
| 自动更新通道 | §1（**手动替换便携包**） |
| 诊断包导出 | §4（**日志文件即可，用户自行提供**） |
| 隐私 / 遥测声明 | 无（产品自身不上报任何数据；**但 §3.4 的 NOTICE 仍必须做**，属第三方许可合规） |
| 单实例限制 | §6（允许多开） |
| 首次运行引导 | §1（无向导） |
| i18n / 无障碍 | 无（v1 不做） |
| 崩溃转储（dump） | §4.2（v1 明确不做） |
| 模块热重载 | §2.3（**语义固定为"改代码 → 重建 → 重启应用"**，不留待办） |
| 进程级沙箱 / 权限拦截 / 强制签名 | 保留 Gate；本文件不含相关交付项 |

## 8. 落地状态与缺口（设计 ↔ 代码） 〔源: code 现状（以代码为准）〕

| 项 | 设计口径 | 代码现状 |
|---|---|---|
| 便携包打包 | §1 布局 + `-ExportDirectory` 导出 | 目录布局与 zip 打包**未落地**；导出脚本已有（`stage-reference-modules.ps1 -ExportDirectory`） |
| 两种发布形态（framework-dependent + self-contained） | §1（**两者都发**，用户裁决） | **未落地**（工程无 RID / self-contained 发布配置） |
| 日志文件（`logs\` + sink + 7 文件 / 32 MB 滚动） | §4.2 | **未落地**（无日志依赖、无 sink、无 `logs\`） |
| 未处理异常兜底（日志 + 转 Issue + 不静默崩溃） | §4.2 | **未落地**（`Program.cs` / `App.axaml.cs` 无 `UnhandledException` / `UnobservedTaskException` 处理） |
| NOTICE 生成与随包 | §3.4 | **未落地**（审计脚本只打印控制台；仓库无 `THIRD_PARTY_NOTICES.md`） |
| 依赖可复现（**不生成 lock 文件**） | §3.3：`RestorePackagesWithLockFile=false`，由 **CPM + 版本锁定**承担 | **已生效**（`Directory.Build.props`；全仓无 lock 文件）；ADR-0024 §6 的 lock 要求**已不适用** |
| 许可门禁（白名单 + 传递依赖 + 例外表） | §3 | **已落地并生效**（`build.ps1` 第 5 步） |
| 构建即归拢 + 应用镜像 + 门禁校验 | §2.2 | **已落地**（6 个参考模块包齐全，应用输出与 `artifacts\modules` 数量校验通过） |
| 内置模块目录默认扫描 + 来源根诊断 | §2.3 | **已落地**（`CompositionRoot` + 启动 Issue + 「模块清单」诊断区） |
| Issue 中心（收集 / 过滤 / 标注） | §4.1 L2 / §5.1 | **已落地**（内存，不落盘） |
| 波形 / FFT 相关依赖 | ADR-0024 §3（`FftSharp` 或 `MathNet.Numerics`；`ScottPlot.Avalonia`） | CPM 中**尚无**对应包条目（波形能力未引入依赖） |

**落地时点** 〔源: architecture/10 §9〕：打包与日志实现计划在 **M1-10（框架 UI 最小）/ M2** 落地；**两种发布形态在发布阶段处理**（不在 M1 范围内）。
