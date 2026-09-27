> ⚠ 历史材料（2026-09 文档收敛前的旧版架构分册）。**现行结论**见 `docs/architecture/` 下的 11 份现行分册（索引 `00-architecture-index.md`）；本文件仅作追溯用，其中的数值/口径可能已被后续修订取代。

# 第 2 章 项目与解决方案结构（全文）

> 状态：**已冻结**（议程 ③ 第 2 章；Q2-1~Q2-5 与 C2-1~C2-5 已由用户逐条裁决；裁决记录见 ADR-0027）
> 上位约束：第 1 章（已冻结，ADR-0026 七层与依赖禁令）+ 顶层基线 v1.0 §11（模块供给）+ ADR-0023/0024（栈与库）+ ADR-0025（工程约束）+ 工程基线（`Directory.Build.props` / `Directory.Packages.props` / `build.ps1`）
> 已裁决前提（议程 ③ 第 2 章）：**Q2-1 层即程序集** · **Q2-2 单一 SDK 程序集** · **Q2-3 参考模块放 `src/`** · **Q2-4 框架 UI 独立项目** · **Q2-5 建示例业务模块并随产品发布（内容属 Gate E）**
> 本章只定**物理落地**（项目、目录、引用、重组）；模块包的内部形态见第 3 章，测试项目细化见第 6 章。

---

## 2.1 项目清单（层 → 项目一一对应）

| 层 | 项目名（**已定**，C2-1） | 类型 | 引用 |
|---|---|---|---|
| L1 契约 | `HYWDebugAssistant.Sdk` | 类库 | 无（**不得引 L0**） |
| L1 契约·测试套件 | `HYWDebugAssistant.Sdk.Testing` | 类库 | `Sdk`（**T6 契约测试套件**：供模块作者在自己的测试项目里自检声明与行为，见第 6 章） |
| L0 横切支撑 | `HYWDebugAssistant.Infrastructure` | 类库 | 无 |
| L2 模块宿主 | `HYWDebugAssistant.Hosting` | 类库 | `Sdk`、`Infrastructure` |
| L3 数据面 | `HYWDebugAssistant.DataPlane` | 类库 | `Sdk`、`Infrastructure` |
| L4 核心管理 | `HYWDebugAssistant.Core` | 类库 | `Sdk`、`Core.Api`、`Hosting`、`DataPlane`、`Infrastructure` |
| L4 公开面 | `HYWDebugAssistant.Core.Api` | 类库 | `Sdk` |
| L5 UI 构成 | `HYWDebugAssistant.Ui` | 类库（含 XAML） | `Sdk`、`Core.Api` |
| L5 框架 UI | `HYWDebugAssistant.FrameworkUi` | 类库（含 XAML） | `Sdk`、`Core.Api`、`Ui` |
| 入口 | `HYWDebugAssistant.App` | **WinExe** | `Ui`、`FrameworkUi`、`Core`、`Core.Api`（组合根，AR-11 唯一白名单） |
| L6 模块 | `HYWDebugAssistant.Modules.*` | 类库（模块包） | **仅 `Sdk`** |

**入口项目的定位说明**：`App` 是薄组合根（`Program`/`Main`、DI 组装、Core 与 UI 的装配序 §3.4），不含机制实现与 UI 布局；`Ui` 承载产品 UI 构成（Shell），`FrameworkUi` 承载框架自身 UI（Module Registry 清单/Issue 面板/诊断面板等，§3.2「框架自身 UI 不作为普通 Module」）。`App` 引用 `Core` 属于"组合根特权"（它是唯一允许同时看到 L4 内部与 L5 的地方），此例外在 §2.5 登记。

> **修订（M1-6 实现暴露）**：`Core` **需要引用 `Core.Api`**——`Core.Api` 是 L4 的公开面程序集，其接口（查询/命令）必须由 L4 实现；`Core.Api` 自身仍**只引 `Sdk`**（AR-6）。该依赖为"L4 实现 → L4 自身公开面"，不构成跨层反向依赖。

> **修订（M1-12 实现暴露）**：新增 `Sdk.Testing`（T6 契约测试套件，`08` §M1-12 已列入交付物）——它是 **`Sdk` 的配套测试支撑**，与 `Sdk` 同源：**只引 `Sdk`、不引任何测试框架**（断言由作者用自己的测试框架做，接口暴露问题清单即可）。AR-0 项目清单由 9 项增至 **10 项**，架构规则集中其允许引用集为 `[Sdk]`。

---

## 2.2 目录结构

```text
HYWDebugAssistant.slnx
├─ src/
│  ├─ Sdk/                     HYWDebugAssistant.Sdk            （L1 契约 · 模块作者唯一引用面）
│  ├─ Infrastructure/          HYWDebugAssistant.Infrastructure （L0 横切支撑）
│  ├─ Hosting/                 HYWDebugAssistant.Hosting        （L2 模块宿主）
│  ├─ DataPlane/               HYWDebugAssistant.DataPlane      （L3 数据面 + 调度承载）
│  ├─ Core/                    HYWDebugAssistant.Core           （L4 核心管理面）
│  ├─ Core.Api/                HYWDebugAssistant.Core.Api       （L4 公开查询/命令面）
│  ├─ Ui/                      HYWDebugAssistant.Ui             （L5 产品 UI 构成）
│  ├─ FrameworkUi/             HYWDebugAssistant.FrameworkUi    （L5 框架自身 UI）
│  ├─ App/                     HYWDebugAssistant.App            （WinExe 组合根）
│  └─ Modules/
│     ├─ Reference/
│     │  ├─ DataGenerator/     HYWDebugAssistant.Modules.Reference.DataGenerator
│     │  ├─ Recorder/          HYWDebugAssistant.Modules.Reference.Recorder
│     │  ├─ Asserter/          HYWDebugAssistant.Modules.Reference.Asserter
│     │  ├─ ManualSender/      HYWDebugAssistant.Modules.Reference.ManualSender  （M1-15：手动数据发送器）
│     │  └─ Monitor/           HYWDebugAssistant.Modules.Reference.Monitor       （M1-15：信息显示模块）
│     └─ <业务模块>/            HYWDebugAssistant.Modules.<名称>   （Q2-5；具体清单属 Gate E）
├─ tests/
│  ├─ HYWDebugAssistant.Tests               （T2–T5 机制与单元测试）
│  ├─ HYWDebugAssistant.Tests.Reference     （T1 判据测试：参考组合 + AC-1~6）
│  ├─ HYWDebugAssistant.ArchitectureTests   （架构规则 AR-1~AR-16）
│  └─ HYWDebugAssistant.Benchmarks          （Gate B 数值实测；不进常规门禁）
├─ tools/                       （audit-licenses.ps1 / license-allowlist.json）
├─ docs/                        （文档组：不在产品项目内）
├─ Directory.Build.props · Directory.Packages.props · .editorconfig · global.json · build.ps1
```

**约定**：项目目录名 = 项目名（A4 规则要求命名空间前缀与目录一致）；模块一律置于 `src/Modules/` 之下并分为 `Reference/`（参考桩模块）与业务模块两类。

**【新增 · M1-17 / ADR-0036 D2】模块包的产出目录约定（构建期，不入库）**

| 目录 | 产出者 | 用途 |
|---|---|---|
| `artifacts\modules\<包名>\` | `src/Modules/Directory.Build.targets`（模块项目**构建即归拢**：`module.json` + 实现程序集 + 依赖） | 统一包库：应用扫描的"工程默认根"、发布打包源 |
| `<应用输出>\modules\<包名>\` | `App` 项目的构建后目标（把 `artifacts\modules\*` 镜像过去） | **内置模块包目录**：应用启动默认扫描，开箱可用 |

- 规则：**先清空该包目录再复制**（避免新旧 DLL 混放）；包名由项目属性推导（默认取项目名后缀），**新增模块项目无需改任何脚本/targets**；
- `artifacts/` 已在 `.gitignore`（构建产物不入库）；
- `tools/stage-reference-modules.ps1` 降级为**发布打包用**（遍历 `artifacts\modules`，不再硬编码模块清单）；`build.ps1` 的"归拢"步骤改为**校验**（每个包目录是否齐全）。

---

## 2.3 模块项目的形态（与第 3 章的接口）

- 开发期：模块 = 普通类库项目，**仅引用 `Sdk`**（Q1-2），目标框架与其他项目一致（`net10.0`，由 `Directory.Build.props` 统一）。
- 交付期：模块项目产出**模块包**（`manifest + 实现绑定 + 可选资源`，§11.2/ADR-0020）。包的物理形式（目录/压缩包/程序集+清单）与 manifest 载体属**第 3 章与 Gate B**，本章只固定"模块项目是包的来源"。
- **参考模块与业务模块同构**：Q2-3 选择把参考桩模块放在 `src/`，即它们与第三方模块享受**完全相同的引用面与加载路径**（不享受测试程序集特权）——这是 AC-1~AC-6 实证保真度的前提。**【M1-15 追加】**参考桩模块现为**五个**独立项目（各只引 `Sdk`）：`DataGenerator` / `Recorder` / `Asserter` / `ManualSender`（手动数据发送器）/ `Monitor`（信息显示模块）。
- 模块项目**不得**引用其它模块项目（P12/A2，第 1 章禁令），**不得**引用 `Core.Api`/`Hosting`/`DataPlane`/`Core`/`Ui`/`FrameworkUi`/`Infrastructure`。

---

## 2.4 现有骨架的重组

现有工程（工程基线阶段建立）与本方案的差异与处理：

| 现有 | 处理 | 说明 |
|---|---|---|
| `src/HYWDebugAssistant`（WinExe + Avalonia + `ViewLocator` + `Program`） | **重组**为 `src/Ui`（产品 UI 构成）与 `src/App`（WinExe 组合根）；旧项目移除 | `Program.BuildAvaloniaApp()` 属组合根，移至 `App`；`ViewLocator` 属 UI 构成，移至 `Ui` |
| `tests/HYWDebugAssistant.Tests` | **保留**，继续承载 L2–L4 机制与单元测试 | 现有 3 项冒烟测试（程序集可加载 / Avalonia 主版本 / AppBuilder 可构建）随重组迁移 |
| `tests/HYWDebugAssistant.ArchitectureTests` | **保留**，第 6 章按七层重写规则集 | 现有 A1–A4/A6 基于 `.Core`/`.Modules.Sdk`/`.UI`/`.App` 后缀推断，需按新命名与 L0/L3/`Core.Api` 角色扩展 |
| `Directory.Packages.props`（CPM） | **保留**，Avalonia 相关包引用集中到 `Ui`/`FrameworkUi`/`App` | 框架与数据面项目不得引用 UI 包（A1 禁令） |
| `build.ps1` / `tools/audit-licenses.ps1` | **保留**；构建范围随项目数量自然扩大 | 许可门禁继续覆盖全部依赖（含传递依赖） |

**重组顺序**（避免中间态不可构建）：① 建立 `Sdk`/`Infrastructure` → ② 建立 `Hosting`/`DataPlane`/`Core`/`Core.Api` 空壳 → ③ 拆分出现有 `Ui`/`App` → ④ 更新架构测试规则集 → ⑤ 加入 `Modules/Reference/*` 与 `Tests.Reference`。工程纪律 C3（不建一次性原型）不因此受影响：上述为空壳骨架，非原型。

---

## 2.5 与第 1 章禁令的一致性检查

| 第 1 章禁令 | 本章落地方式 |
|---|---|
| L6 仅引 L1 | 模块项目只引 `Sdk`；架构测试 A2 覆盖模块互引，A-new 覆盖模块引框架其他层 |
| L3 不依赖 L2/L4/L5 | `DataPlane` 项目引用仅 `Sdk` + `Infrastructure` |
| L5 只引 L1 + `Core.Api` | `Ui`/`FrameworkUi` 引用表限定；`FrameworkUi` 额外引 `Ui`（复用视觉层组件，属 L5 内部） |
| L1 零框架依赖 | `Sdk` 无 ProjectReference、无 PackageReference（仅 BCL） |
| L0 仅 L2/L3/L4 可见 | `Infrastructure` 仅被 `Hosting`/`DataPlane`/`Core` 引用 |
| `Core.Api` 只依赖 L1 | 引用表限定；"L4 内部类型不得出现在其公开签名"由第 6 章规则检查 |
| 结构级保障"Core 不中转、UI 不阻塞数据面" | `DataPlane` 项目无对 `Core`/`Ui` 的引用，物理上无法反向调用 |

**登记例外（唯一）**：`App` 作为组合根同时引用 `Core`（L4 内部）与 `Ui`/`FrameworkUi`（L5）。理由：装配序（§3.4）要求先建 Core 再建 UI 并让 UI 引用 Core，此装配动作必须发生在**两层的共同上层**；`App` 不含机制实现，仅为组装与启动。该例外需在第 6 章的规则集里显式白名单化，并**禁止** `App` 承载任何业务/机制逻辑。

---

## 2.6 本章裁决记录（已确认 · 冻结依据）

| 编号 | 问题 | 裁决结果 | 连带影响（后续章节必须遵守） |
|---|---|---|---|
| **C2-1** | 项目命名方案 | **按本章 §2.1 方案**：`Sdk` / `Infrastructure` / `Hosting` / `DataPlane` / `Core` / `Core.Api` / `Ui` / `FrameworkUi` / `App` / `Modules.*` | 命名进入命名空间、A4 目录一致规则、第 6 章角色推断与后续全部文档引用；此后改名须走 ADR |
| **C2-2** | 应用入口项目 | **独立 `App`（WinExe）项目** | §2.5 的组合根例外成立；**第 6 章须把 `App` 白名单化**，并加规则禁止 `App` 承载业务/机制逻辑 |
| **C2-3** | 框架 UI 组织 | **`src/Ui` 与 `src/FrameworkUi` 两个平级项目** | 目录与程序集一一对应；`FrameworkUi` 可引 `Ui`（复用视觉层组件），反向不可 |
| **C2-4** | 参考桩模块粒度 | **三个独立项目**（`Modules.Reference.DataGenerator` / `.Recorder` / `.Asserter`） | 隐含"**一模块一包**"为默认形态；第 3 章须确认"一个模块包是否可含多个模块"，若合法则需说明何时使用 |
| **C2-5** | 现有骨架重组 | **按 §2.4 拆分并移除旧项目** | 执行顺序：建 `Sdk`/`Infrastructure` → 建其余空壳 → 拆分 `Ui`/`App` → 更新架构规则 → 加参考模块与 `Tests.Reference`；旧 `src/HYWDebugAssistant` 移除 |

> 记录方式：本章五项裁决作为 ADR-0027《项目与解决方案结构》的正文来源。

---

## 2.7 状态

- 本章 **已冻结**（项目清单 + 目录结构 + 模块项目形态 + 重组方案 + 五项裁决记录）。
- 冻结后推进：**开发实施（建项目骨架）不在本章立即执行**——按 Q0-3 逐章推进，先完成第 3–7 章设计，再统一落地工程（避免设计反复导致项目结构返工）。
- 下一章：第 3 章（模块 SDK 形态）提纲 → 全文（**C1-3 已收窄 Q3-7：模块不得提供 View 类型**；C2-4 要求确认模块包与模块的基数关系）。
