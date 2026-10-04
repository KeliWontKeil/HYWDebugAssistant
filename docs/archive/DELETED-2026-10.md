# 已清理的归档载体（2026-10）

> ⚠ 历史材料（记录一次清理动作本身）。**不作为依据** · 现行结论看 `docs/README.md` 与主文档。

## 为什么清理

用户 2026-10 裁定：**归档从宽删**。理由 —— 归档体量（130 份 / 2.0 MB）已超过代码阅读成本，
其中大量是「已被取代的旧载体」「一次性过程流水」「选型论证草稿」，**留着只造成记忆污染与冲突点**，
而真正需要追溯的情形极少。

## 保留了什么

| 保留 | 理由 |
| --- | --- |
| `adr/ADR-0000`–`ADR-0054`（54 份）+ `adr/README.md` | **每一条都被现行文档按编号引用**（`docs/decisions.md`、主文档、各分册）；`docs/decisions.md` 明写"原文（含当时的论证、被否决方案、实测取证）在 `docs/archive/adr/`" |
| `superseded.md` | 被取代/撤销表述与 P 系列去向表，被主文档与 `docs/decisions.md` 引用 |
| `docs-audit-2026-10-04.md` | 被 `docs/README.md` 引用（文档结构的评估依据） |
| `DELETED-2026-10.md` | 本文件（清理清单） |

## 清理了什么（73 份）

路径按 `archive/` 相对写法记录；**删除前全部已入 git 索引**，需要时用
`git checkout <删除前的提交> -- docs/archive/<路径>` 取回。

### 收敛前的根载体（6）

| 文件 | 内容 |
| --- | --- |
| `architecture-v0.30-full.md` | 收敛前的完整旧原文快照（1144 行） |
| `README.md` | 收敛前的文档组总索引（162 行） |
| `roadmap.md` | 收敛前的 roadmap（259 行；现行状态见 `docs/roadmap.md`） |
| `decisions.md` | 收敛前的决策表（62 行；现行见 `docs/decisions.md`） |
| `changelog.md` | 主文档 v0.0–v0.30 版本序列（46 行） |
| `pq-closed.md` | 已关闭待决问题的原始索引（28 行；现行索引见 `docs/journal/main-doc-closed-questions.md`） |

### ADR 目录（1）

| 文件 | 内容 |
| --- | --- |
| `adr/README-收敛前（含每份一句话摘要）.md` | 收敛前的 ADR 一览（与现行 `adr/README.md` 重复） |

### 收敛前的旧分册（22，旧编号世代）

`architecture/00-architecture-outline.md` · `01-layering-and-dependencies.md` · `02-project-structure.md` ·
`03-module-sdk.md` · `03a-view-rendering-options.md` · `04-key-mechanisms.md` · `04a-execution-and-data-unit-analysis.md` ·
`04b-application-settings.md` · `05-ui-architecture.md` · `06-test-architecture.md` · `07-gate-and-rollout.md` ·
`08-implementation-roadmap.md` · `09-visual-design-baseline.md` · `10-delivery-and-ops.md` · `11-presentation-revision.md` ·
`12-module-discovery-and-window-behavior.md` · `13-ui-usability-remediation-plan.md` · `14-ui-rework-m1-16.md` ·
`15-snap-and-anchor-remediation.md` · `16-data-protocol.md` · `17-layering-and-responsibilities.md` · `18-network-semantics.md`

> 注意：**旧编号与现行 `docs/architecture/01`–`10` 不是同一份**，编号不可互推。现行分册见 `docs/architecture/00-architecture-index.md`。

### 重写前的旧工程文档（4）

`implementation/00-pre-implementation-constraints.md` · `02-engineering-baseline.md` ·
`03-development-workflow-and-coding-details.md` · `04-m1-manual-acceptance.md`

### 方法论（1）

`methodology/top-down-design-with-human-expert-review.md` —— 已收敛为技能
`.dsh/skills/human-ai-collaborative-design-review/`（权威源）。

### 2026-09 收敛过程（22）

审计与逐份改动记录：`reorg-2026-09/01-architecture-assessment.md` · `02-adr-assessment.md` ·
`03-process-and-lessons-assessment.md` · `04a-main-doc-changes.md` · `04b-adr-metadata-changes.md` ·
`04c-main-doc-final-changes.md` · `04d-volumes-1-2-3-5-6-changes.md` · `04e-volumes-4-7-8-9-10-changes.md` ·
`05a-implementation-00-01-02-changes.md` · `05b-implementation-03-04-changes.md` · `05c-roadmap-compression.md` ·
`06-code-repo-refs.md` · `conflicts.md` · `phase-b-contract.md`；
技能草稿 `skills-draft/`（3 个技能 × 各自附带清单，已被 `.dsh/skills/` 取代）；
一次性工具 `tools/archive-docs.ps1` · `tools/swap-architecture.ps1`。

### 2026-10 文档重构过程（11）

一次性执行脚本 `reorg-2026-10/reorg-move-span.ps1` · `reorg-p2-maindoc.ps1` · `reorg-p2-module-pages-fix.ps1` ·
`reorg-p2-module-pages-trim.ps1` · `reorg-p2-module-pages.ps1` · `reorg-p2-roadmap.ps1` · `reorg-p3-root-docs.ps1` ·
`reorg-p4-06-gsections.ps1` · `reorg-p4-06-refs.ps1` · `reorg-p5-ui-adjust.ps1` · `reorg-p6-lessons.ps1`。

### 技术选型论证（6）

`technology-selection/01-criteria-and-matrix.md` · `02-csharp-performance-and-scripting.md` ·
`03-ui-avalonia-vs-web.md` · `04-ui-reevaluation.md` · `05-environment-checklist.md` · `06-library-selection.md`
—— 结论仍在 ADR-0023/0024 与现行 `docs/architecture/10-delivery-and-ops.md`。

## 结果

| | 清理前 | 清理后 |
| --- | --- | --- |
| 文件数 | 130 | 57 |
| 体积 | 2020 KB | 508 KB |
