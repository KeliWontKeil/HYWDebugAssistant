# 模块化工程上位机平台 · 设计文档

> 本目录是**设计文档的唯一入口**。**现行设计 = 主文档 + 11 份分册 + 1 张决策表**；其余全部是**历史材料** —— ADR 原文 / 审计报告进 `archive/`，**修订历史、逐轮反馈、已收口条目、已执行流水等过程材料进 `journal/`**（见 `decisions.md` 7.64）。**2026-10 起归档从宽清理**：已被取代的旧载体已删（旧分册 / 旧工程文档 / 选型论证 / 一次性过程流水），清单与 git 取回方式见 `archive/DELETED-2026-10.md`。

---

## 1. 文档树（现行部分只有这些）

```
上位机架构设计.md              ← ① 验收唯一事实源（冻结结论 + 边界与 Gate）
docs/
├─ README.md                   ← 你在这里：入口、权威层级、阅读顺序、维护规则指针
├─ DOC-WORKFLOW.md             ← ② **文档工作流**（编辑/同步对照表/归档/审计门禁/标记体系）——"文档自己怎么管"的唯一落点
├─ decisions.md                ← ③ 现行决策表（7 类；**编号有跳跃，现行 = 7.1–7.19 与 7.64**）+ 关键取证索引（R1–R15）+ 架构例外登记
├─ architecture/               ← ④ 现行分册 11 份（"具体怎么做"）
│   ├─ 00-architecture-index.md      分册索引：每份管什么、何时看、跨册边界（同步表见 DOC-WORKFLOW §2）
│   ├─ 01-layering-and-projects.md   七层与依赖链、项目结构、App 白名单
│   ├─ 02-module-sdk.md              SDK 公开面、声明式视图、线程与投递、不做清单
│   ├─ 03-data-protocol-and-typing.md 数据单元、形状=契约、类型注册、兼容规则
│   ├─ 04-runtime-mechanisms.md      生命周期、停止协议、ChangeSet、组合件、实测基准
│   ├─ 05-network-semantics.md       建网两机制、多对多、数据报语义、计量与丢弃
│   ├─ 06-ui-shell-and-windows.md    外壳、窗口与交互、**UI 开发方式与调参地图（§9）**、快捷键
│   ├─ 07-overlay-and-visual-baseline.md 叠加层、端口标记、角色方块、**取值纪律与视觉 token（§1）**、吸附
│   ├─ 08-application-settings.md    设置键清单（唯一来源）与生效规则
│   ├─ 09-test-and-gate.md           判据、测试分层、架构规则集、门禁与里程碑
│   └─ 10-delivery-and-ops.md        交付、构建、许可门禁、诊断
├─ implementation/             ← ⑤ 工程纪律：怎么写字（01）、工程基线 P 表（02）、开发流程（03）、手工验收清单（04）、双轨开发工作流（05）、模块标准与开发流程（06）、**开发内环（07）**、**经验总结（08）**
├─ modules/                    ← ⑥ **模块文档：一模块一页**（清单页 + `_模板.md`）；"这个模块是什么"看这里，"做到哪了"看 roadmap
├─ roadmap.md                  ← ⑦ 状态唯一源：当前阶段、未完成项、落地缺口、模块开发状态
├─ journal/                    ← ⑧ **过程材料**（修订历史 · 逐轮反馈 · 已收口条目 · 已执行流水）：**历史，不作为依据**；现行结论一律回到主文档 / `decisions.md` / 分册
└─ archive/                    ← ⑨ 历史材料（不作为依据；每份头部有"⚠ 历史材料 + 现行锚点"）；**2026-10 已从宽清理**，现存 3 项 + 清理清单
    ├─ adr/                    54 个 ADR 原文 + `README.md`（ADR 一览表；被 `decisions.md` 按编号引用）
    ├─ superseded.md           被取代 / 撤销表述与 P 系列去向表
    ├─ docs-audit-2026-10-04.md  文档结构审计报告
    ├─ DELETED-2026-10.md      **已清理载体清单**（旧分册 22 · 旧工程文档 4 · 选型论证 6 · 收敛前根载体 6 · 过程流水与技能草稿 29 · ADR 一览旧版 1）
    └─ modules-<年-月>/        退役 / 归档的模块文档（DOC-WORKFLOW §3.2；本仓尚未产生）

```

> **仓库根只有 `README.md` 与主文档 `上位机架构设计.md`**：原先的三份"长期指导"（`DEVELOPMENT.md` / `LESSONS.md` / `UI-ADJUST.md`）已收敛进 `implementation/07`、`implementation/08`、`architecture/06` §9 与 `architecture/07` §1（见 `decisions.md` 7.64）。

---

## 2. 权威层级（冲突时谁说了算）

| 看什么 | 去哪 |
|---|---|
| **这段内容属于哪一类、该放哪** | `DOC-WORKFLOW.md` §0（**A–G 分类协议**：先定类，再定落点） |
| **结论**（这个系统是什么、边界在哪） | **主文档 `上位机架构设计.md`** ← 验收唯一事实源 |
| **为什么这么定**（决定 + 取证 + 例外） | `decisions.md` |
| **具体怎么做**（分层/协议/机制/网络/UI/设置/测试/交付） | `architecture/` 各分册（索引见 `architecture/00-architecture-index.md`） |
| **怎么写代码/怎么跑验证** | `implementation/`（**开发内环 = `implementation/07`** · **经验总结 = `implementation/08`** · **双轨工作流见 `implementation/05`**；工程脚本 = 仓库根 `tools/dev.ps1`） |
| **方法（跨项目可复用）** | **`.dsh/skills/`**（置于此处以便 DSH 发现；**与本项目解耦**，可交给任何 AI 工具；由用户显式跟踪） |
| **这个模块是什么** | `modules/<模块标识>.md`（一模块一页；清单 `modules/README.md`） |
| **到哪了 / 还差什么** | `roadmap.md`（状态唯一源，含**落地缺口**与**模块开发状态**） |
| **过程材料**（当时是怎么改的、哪一轮反馈） | `journal/`（**历史，不作为依据**） |
| **历史与被否决方案** | `archive/`（**不作为依据**） |

**一句话**：**结论看主文档 · 为什么看 decisions · 怎么做看分册 · 怎么写看 implementation · 到哪了看 roadmap · 过程看 journal · 历史看 archive。**

---

## 3. 按角色/任务的阅读顺序

| 我是谁 / 我要做什么 | 顺序 |
|---|---|
| **第一次接手** | 主文档 §1–§3、§18 → `architecture/00` → `01` → `03` → `04` → `09` → `roadmap.md` |
| **写一个新模块** | `implementation/06`（**模块标准与七阶段流程**）→ `implementation/05`（走哪条轨）→ `architecture/02` → `03` → `architecture/05` → `modules/_模板.md` → **开发参考 = 第一批四个正式模块**（`src/Modules/Business/`；**不要**照抄已删除的 6 个旧参考模块 —— 见 `decisions.md` 7.36） |
| **改界面观感** | `architecture/06` §9（**改哪儿 → 改哪个文件**）→ `architecture/07` §1（**取值纪律**：token 与旋钮现值）→ `.dsh/skills/ui-rendering-and-third-party-control-discipline` |
| **动建网/数据面** | `architecture/05` → `03` → `04` → `09`（这类改动必须跑完整门禁） |
| **加设置项** | `architecture/06` §9 → `architecture/08`（**键清单唯一源**）→ `decisions.md`（**仅当它是"决定"**） |
| **修 bug** | `roadmap.md` 落地缺口 → 相关分册 → `implementation/02` 的 P 踩坑表 → `implementation/08`（通用纪律） |
| **做验收** | `implementation/04`（手工验收清单）→ `architecture/09`（自动化覆盖到哪层）→ 主文档 §14 |

---

## 4. 维护规则（**全文在 `DOC-WORKFLOW.md`**）

> **文档自己怎么管** —— **A–G 内容分类协议（一段内容只有一个家）** · 单一事实源 · 失效信息搬家 · **改动同步对照表** · 归档规程 · 审计门禁 · 标记体系（`【待裁】`/`【待评估】`）· 提案→裁决→回写循环 —— **唯一落点是 `DOC-WORKFLOW.md`**。

| 我要做的事 | 去哪 |
|---|---|
| 判断**这段内容属于哪一类 / 该放哪**（结论 / 决策 / 数值 / 进度 / 过程 / 方法 / 历史） | `DOC-WORKFLOW.md` §0（**A–G 分类协议**） |
| 改了东西，要知道**该同步哪些文件** | `DOC-WORKFLOW.md` §2（**唯一对照表**） |
| 要**归档 / 搬迁**内容（含退役模块、过程材料进 `journal/`） | `DOC-WORKFLOW.md` §3 |
| 跑**审计**、处理 FAIL | `DOC-WORKFLOW.md` §4 |
| 用 `【待裁】` / `【待评估】` / `【待细化】` 标记 | `DOC-WORKFLOW.md` §5（**必须登记到 `roadmap.md` §3**，不能只躺在正文里） |
| 走"**提案 → 裁决 → 回写**" | `DOC-WORKFLOW.md` §6 |
| 判断"这属于哪一类问题"（A 类裁决 / B 类实现） | `DOC-WORKFLOW.md` §5 末的判据 |

**三条最容易忘的**：① **改设计 = 两处落点**（`decisions.md` 一条 + 受影响的**唯一一个**正文；对照表见 `DOC-WORKFLOW.md` §2）；② 文档与代码**同一次改动**，回写**每批次一次**（不逐条写、不写进注释）；③ **模块**改动要**三处一致**（模块页 / `modules/README.md` §2.1 / `roadmap.md` §5）。
