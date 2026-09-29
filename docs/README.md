# 模块化工程上位机平台 · 设计文档

> 本目录是**设计文档的唯一入口**。2026-09 完成一轮收敛：**现行设计 = 主文档 + 11 份分册 + 1 张决策表**；其余（54 个 ADR 原文、旧分册、过程流水、选型论证）全部归档为**历史材料**，原文一字未删。

---

## 1. 文档树（现行部分只有这些）

```
上位机架构设计.md              ← ① 验收唯一事实源（冻结结论 + 边界与 Gate）
docs/
├─ README.md                   ← 你在这里：入口、权威层级、阅读顺序、维护规则指针
├─ DOC-WORKFLOW.md             ← ② **文档工作流**（编辑/同步对照表/归档/审计门禁/标记体系）——"文档自己怎么管"的唯一落点
├─ decisions.md                ← ③ 现行决策表（7 类约 55 条）+ 关键取证索引（R1–R15）+ 架构例外登记
├─ architecture/               ← ④ 现行分册 11 份（"具体怎么做"）
│   ├─ 00-architecture-index.md      分册索引：每份管什么、何时看、跨册边界（同步表已收进 DOC-WORKFLOW §2）
│   ├─ 01-layering-and-projects.md   七层与依赖链、项目结构、App 白名单
│   ├─ 02-module-sdk.md              SDK 公开面、声明式视图、线程与投递、不做清单
│   ├─ 03-data-protocol-and-typing.md 数据单元、形状=契约、类型注册、兼容规则
│   ├─ 04-runtime-mechanisms.md      生命周期、停止协议、ChangeSet、组合件、实测基准
│   ├─ 05-network-semantics.md       建网两机制、多对多、数据报语义、计量与丢弃
│   ├─ 06-ui-shell-and-windows.md    外壳、窗口与交互、人机操作通道、快捷键
│   ├─ 07-overlay-and-visual-baseline.md 叠加层、端口标记、角色方块、视觉 token、吸附
│   ├─ 08-application-settings.md    设置键清单（唯一来源）与生效规则
│   ├─ 09-test-and-gate.md           判据、测试分层、架构规则集、门禁与里程碑
│   └─ 10-delivery-and-ops.md        交付、构建、许可门禁、诊断
├─ implementation/             ← ⑤ 工程纪律：怎么写字（01）、工程基线 P 表（02）、开发流程（03）、手工验收清单（04）、**双轨开发工作流（05）**、**模块标准与开发流程（06）**
├─ modules/                    ← ⑥ **模块文档：一模块一页**（清单页 + `_模板.md`）；"这个模块是什么"看这里，"做到哪了"看 roadmap
├─ roadmap.md                  ← ⑦ 状态唯一源：当前阶段、未完成项、落地缺口、模块开发状态
└─ archive/                    ← ⑧ 历史材料（不作为依据；每份头部有"⚠ 历史材料 + 现行锚点"）
    ├─ adr/                    54 个 ADR 原文 + ADR 历史索引
    ├─ architecture/           收敛前的旧分册
    ├─ implementation/         重写前的旧工程文档
    ├─ technology-selection/   选型论证
    ├─ modules-<年-月>/        退役/归档的模块文档（DOC-WORKFLOW §3.2）
    └─ roadmap.md / changelog.md / decisions.md（旧） / reorg-2026-09（本轮审计报告）
```

---

## 2. 权威层级（冲突时谁说了算）

| 看什么 | 去哪 |
|---|---|
| **结论**（这个系统是什么、边界在哪） | **主文档 `上位机架构设计.md`** ← 验收唯一事实源 |
| **为什么这么定**（决定 + 取证 + 例外） | `decisions.md` |
| **具体怎么做**（分层/协议/机制/网络/UI/设置/测试/交付） | `architecture/` 各分册（索引见 `architecture/00-architecture-index.md`） |
| **怎么写代码/怎么跑验证** | `implementation/`（**双轨工作流见 `implementation/05`**）+ 代码仓库的 `DEVELOPMENT.md`、`LESSONS.md`、`UI-ADJUST.md` |
| **方法（跨项目可复用）** | **`.dsh/skills/`**（原 `docs/skills/`，2026-09 移至此处以便 DSH 发现；**与本项目解耦**，可交给任何 AI 工具；由用户显式跟踪） |
| **这个模块是什么** | `modules/<模块标识>.md`（一模块一页；清单 `modules/README.md`） |
| **到哪了 / 还差什么** | `roadmap.md`（状态唯一源，含**落地缺口**与**模块开发状态**） |
| **历史与被否决方案** | `archive/`（**不作为依据**） |

**一句话**：**结论看主文档 · 为什么看 decisions · 怎么做看分册 · 怎么写看 implementation · 到哪了看 roadmap · 历史看 archive。**

---

## 3. 按角色/任务的阅读顺序

| 我是谁 / 我要做什么 | 顺序 |
|---|---|
| **第一次接手** | 主文档 §1–§3、§18 → `architecture/00` → `01` → `03` → `04` → `09` → `roadmap.md` |
| **写一个新模块** | `implementation/06`（**模块标准与七阶段流程**）→ `implementation/05`（走哪条轨）→ `architecture/02` → `03` → `architecture/05` → `modules/_模板.md` → **开发参考 = 第一批四个正式模块**（`src/Modules/Business/`；**不要**照抄已于 2026-09 删除的 6 个旧参考模块 —— 见 `decisions.md` 7.36） |
| **改界面观感** | `architecture/07` → `06` → `.dsh/skills/ui-rendering-and-third-party-control-discipline`（**仓库根**） |
| **动建网/数据面** | `architecture/05` → `03` → `04` → `09`（这类改动必须跑完整门禁） |
| **加设置项** | `architecture/08` → 主文档 §9.5 → `decisions.md` |
| **修 bug** | `roadmap.md` 落地缺口 → 相关分册 → `implementation/02` 的 P 踩坑表 |
| **做验收** | `implementation/04`（手工验收清单）→ `architecture/09`（自动化覆盖到哪层）→ 主文档 §14 |

---

## 4. 维护规则（**全文在 `DOC-WORKFLOW.md`**）

> **文档自己怎么管** —— 单一事实源 · 失效信息搬家 · **改动同步对照表** · 归档规程 · 审计门禁 · 标记体系（`【待裁】`/`【待评估】`）· 提案→裁决→回写循环 —— **唯一落点是 `DOC-WORKFLOW.md`**。
> 本节原先的 11 条规则已**整体收敛进该文件**（内容一条未丢），此处只留最短指针，避免两处各写一遍。

| 我要做的事 | 去哪 |
|---|---|
| 改了东西，要知道**该同步哪些文件** | `DOC-WORKFLOW.md` §2（**唯一对照表**；`architecture/00` §4 与 `implementation/03` §4 的旧表已并入） |
| 要**归档 / 搬迁**内容（含退役模块） | `DOC-WORKFLOW.md` §3（退役模块 → `docs/archive/modules-<年-月>/`） |
| 跑**审计**、处理 FAIL | `DOC-WORKFLOW.md` §4 —— 命令 `pwsh -NoProfile -File .\docs\tools\audit-docs.ps1`；**已挂进 `build.ps1` 第 1/5 步** |
| 用 `【待裁】` / `【待评估】` / `【待细化】` 标记 | `DOC-WORKFLOW.md` §5（**必须登记到 `roadmap.md` §3**，不能只躺在正文里） |
| 走"**提案 → 裁决 → 回写**" | `DOC-WORKFLOW.md` §6 |
| 判断"这属于哪一类问题"（A 类裁决 / B 类实现） | `DOC-WORKFLOW.md` §5 末的判据 |

**三条最容易忘的**：① **改设计 = 三处同步**（`decisions.md` + 回写主文档 + 相关分册）；② 文档与代码**同一次改动**；③ **模块**改动要**三处一致**（模块页 / `modules/README.md` §2.1 / `roadmap.md` §5）。

**一条失效路径更正**：代码仓库的 `DEVELOPMENT.md` §6 仍写"设计/验收事实源在 `D:\project\上位机开发\docs\`" —— 那是**已失效的旧路径**（文档已并入本仓库 `docs/`），冲突时以本仓库为准。

