# implementation/06 §G.3–§G.4 原文（过程日志 · 不作为依据）

> ⚠ **历史材料**（2026-09 迁移）：本文是 `docs/implementation/06-module-standard-and-development-process.md` 里**原 §G.3（归档时要动的地方）与 §G.4（执行记录）**的原文，因属"已执行的过程流水"已从该文件搬出。
> **现行状态**：旧 6 个参考模块**已删除**（现行包清单 = `src/Modules/Business/` 的 4 个）；现行判据载体 = 四个基础模块 + 判据项目内测试桩。
> **现行锚点**：处置裁决 = `docs/decisions.md` 7.36 · 进度 = `docs/roadmap.md` §3 第 32 项 / `docs/journal/roadmap-completed-items.md` 第 32 项 · 模块清单 = `docs/modules/README.md`。
> 下文中的相对链接已按本文件位置（`docs/journal/`）重写；括注里的行号/节号指原 §G.3/§G.4 当时的位置。

---

### G.3 归档时要动的地方（**清单 —— 2026-09 已逐条执行，见 §G.4**）

| # | 动作 | 执行结果 |
|---|---|---|
| 1 | `src/Modules/Reference/` 6 个包移出 `src/`（**搬家不删除**，进归档区并加"⚠ 已归档 + 现行锚点"标记），从 `HYWDebugAssistant.slnx` 移除 | **口径变更**：用户 2026-09 裁定"**完全删除旧的模块和数据结构**" ⇒ **直接删除**（不再是"搬家不删除"）；`slnx` 参考分组已移除 |
| 2 | `tests/HYWDebugAssistant.Tests.Reference/` 的 AC-1~AC-6 判据**必须换载体**（承载 AC 的公开面判据不能随参考模块一起消失）—— 届时应已由**第一批正式模块**接管；这是归档前的**硬前置** | ✅ **已换载体**：AC-1~6 + 契约/链路判据改由**四个基础模块** + 判据项目内测试桩承载（§G.4） |
| 3 | `build.ps1` 的"模块包 6/6"校验改为按实际包清单校验 | ✅ 校验脚本**本就按目录遍历**（不写死清单）⇒ 只把脚本名从 `stage-reference-modules.ps1` 改为 `verify-module-packages.ps1`（功能不变），现校验 **4 个包** |
| 4 | 同步 `implementation/04`（手工验收清单里引用它们的条目）· `docs/modules/README.md` §2.2 · `05` §3 · `09` 测试资产清单 · `roadmap.md` §2/§5 | ✅ 已同步（另加：`architecture/01`·`03`·`09`·`10`、`src/Ui/Design/*`、`src/App/DragSelfTest.cs`、主测试项目三处包路径与包数断言） |

> **前置检查（归档前必答）**：AC-1~AC-6 与手工验收的判据**是否已由新模块完全接管**？答不上就不归档。 ⇒ **已答"是"**：AC-1~6 逐条在四个基础模块 + 测试桩上跑通（读数见 `docs/roadmap.md` §6）。

### G.4 执行记录（2026-09 · 用户裁定"完全删除旧的模块和数据结构"）

**用户原话**："**完全删除旧的模块和数据结构**，新测试全部转入新的四个模块，新的模块完成开发后将可以作为某种**开发参考**（**不删任何的框架能力和接口**）。"

| 项 | 事实 |
|---|---|
| 已删除 | `src/Modules/Reference/**`（6 包）· `src/Sdk/Contracts/CommonDataTypes.cs`（`hyw/*` 旧类型库）· `slnx` 参考分组与全部项目引用 · `artifacts\modules\` 与应用输出里的 6 个旧包目录 |
| 已改名 | `tools/stage-reference-modules.ps1` → **`tools/verify-module-packages.ps1`**（只校验/导出，功能不变） |
| 现判据载体 | **四个基础模块**（`text-sender` / `hex-sender` / `data-monitor` / `hex-string-converter`）+ **判据项目内测试桩**（`tests/HYWDebugAssistant.Tests.Reference/Stubs/`：`FusionStubModule` 多源融合 · `RateSourceStubModule` 定时定值数据源 · `JudgementSinkStubModule` 判据接收与限流上报）—— 桩**不进产品目录、不打包** |
| 开发参考 | **四份模块页**（`docs/modules/hyw-*.md`）+ 模块自身代码；**模板来源仍是第一批正式模块**（§G.1 ④ 未变） |
| 框架能力 | **一个都没删**（`Sdk`/`Core.Api` 公开面 · `ViewElementKind` 目录 · B1/B6/A5/A6 全部保留）——§G.3 附表列出的引用点全部只做"换载体"（**该批如此**；`Sdk` 的**视图声明面**后来按 `decisions.md` **7.60** 整体换掉 —— `ViewElement`/`ViewDeclaration`/`ModuleViewLayout` 已删，`ViewElementKind` 目录保留） |
| 计数 | 判据项目 43 → **41** 条 · 包 **6 → 4**（读数见 `docs/roadmap.md` §6） |
| 决策记录 | `docs/decisions.md` **7.36** · `docs/roadmap.md` §3 第 32 项 |
