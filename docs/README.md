# 模块化工程上位机平台 · 设计文档

> 本目录是**设计文档的唯一入口**。2026-09 完成一轮收敛：**现行设计 = 主文档 + 11 份分册 + 1 张决策表**；其余（54 个 ADR 原文、旧分册、过程流水、选型论证）全部归档为**历史材料**，原文一字未删。

---

## 1. 文档树（现行部分只有这些）

```
上位机架构设计.md              ← ① 验收唯一事实源（冻结结论 + 边界与 Gate）
docs/
├─ README.md                   ← 你在这里：入口、权威层级、维护规则
├─ decisions.md                ← ② 现行决策表（7 类约 55 条）+ 关键取证索引（R1–R15）+ 架构例外登记
├─ architecture/               ← ③ 现行分册 11 份（"具体怎么做"）
│   ├─ 00-architecture-index.md      分册索引：每份管什么、何时看、跨册边界、改动同步表
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
├─ implementation/             ← ④ 工程纪律：怎么写字、工程基线（P 踩坑表）、开发流程、手工验收清单
├─ skills/                     ← ⑤ 可迁移的方法（与本项目解耦，可交给任何 AI 工具）
├─ roadmap.md                  ← ⑥ 状态唯一源：当前阶段、未完成项、落地缺口
└─ archive/                    ← ⑦ 历史材料（不作为依据；每份头部有"⚠ 历史材料 + 现行锚点"）
    ├─ adr/                    54 个 ADR 原文 + ADR 历史索引
    ├─ architecture/           收敛前的旧分册
    ├─ implementation/         重写前的旧工程文档
    ├─ technology-selection/   选型论证
    └─ roadmap.md / changelog.md / decisions.md（旧） / reorg-2026-09（本轮审计报告）
```

---

## 2. 权威层级（冲突时谁说了算）

| 看什么 | 去哪 |
|---|---|
| **结论**（这个系统是什么、边界在哪） | **主文档 `上位机架构设计.md`** ← 验收唯一事实源 |
| **为什么这么定**（决定 + 取证 + 例外） | `decisions.md` |
| **具体怎么做**（分层/协议/机制/网络/UI/设置/测试/交付） | `architecture/` 各分册（索引见 `architecture/00-architecture-index.md`） |
| **怎么写代码/怎么跑验证** | `implementation/`（+ 代码仓库的 `DEVELOPMENT.md`、`LESSONS.md`、`UI-ADJUST.md`） |
| **方法（跨项目可复用）** | `skills/` |
| **到哪了 / 还差什么** | `roadmap.md`（状态唯一源，含**落地缺口**清单） |
| **历史与被否决方案** | `archive/`（**不作为依据**） |

**一句话**：**结论看主文档 · 为什么看 decisions · 怎么做看分册 · 怎么写看 implementation · 到哪了看 roadmap · 历史看 archive。**

---

## 3. 按角色/任务的阅读顺序

| 我是谁 / 我要做什么 | 顺序 |
|---|---|
| **第一次接手** | 主文档 §1–§3、§18 → `architecture/00` → `01` → `03` → `04` → `09` → `roadmap.md` |
| **写一个新模块** | `architecture/02` → `03` → `05` → `implementation/01` → 参考模块代码 |
| **改界面观感** | `architecture/07` → `06` → `skills/ui-rendering-and-third-party-control-discipline` |
| **动建网/数据面** | `architecture/05` → `03` → `04` → `09`（这类改动必须跑完整门禁） |
| **加设置项** | `architecture/08` → 主文档 §9.5 → `decisions.md` |
| **修 bug** | `roadmap.md` 落地缺口 → 相关分册 → `implementation/02` 的 P 踩坑表 |
| **做验收** | `implementation/04`（手工验收清单）→ `architecture/09`（自动化覆盖到哪层）→ 主文档 §14 |

---

## 4. 维护规则（改文档前先读这段）

1. **单一事实源**：一处事实只在一个文件里写全，其它地方给**指针**。跨册边界见 `architecture/00-architecture-index.md` §1。
2. **失效信息搬家，不与生效结论共存**：被推翻的写法**移出**现行路径，进 `archive/` 并加"⚠ 历史材料 + 现行锚点"标记。
3. **绝不丢失有效信息**：删/合并前确认内容在别处仍有承载；"只此一处且有指导意义"的（实测数值、边界条件、被否决方案的理由）**必须留下**。
4. **改设计 = 三处同步**：`decisions.md`（决定级）+ **回写主文档**（验收事实源 + 修订记录 +1）+ 相关分册。
5. **例外必须登记**：代码注释 `// ARCH-EXCEPTION: <原因> · <复查条件>` **并**登记到 `decisions.md` §3。
6. **数值唯一**：字号/尺寸/容差/阈值在现行文档里只允许一个取值；两代值并存必须按显式取代标记取新值，否则标 `【待裁】`。
7. **状态只在一处**：进度与缺口只写在 `roadmap.md`；分册不写进度。
8. **手工验收清单有生命周期**：里程碑收尾即冻结归档，下一个里程碑另起文件（不在同一文件上叠修订版）。

完整的"改什么 → 必须更新哪些文件"对照表在 `architecture/00-architecture-index.md` §4。

**改完文档跑一次机械审计**（只读，不修改任何文件）：

```powershell
pwsh -NoProfile -File .\docs\tools\audit-docs.ps1
```

它检查五件事：① **失效术语黑名单**（`快速建网`/`R0–R6`/`不建立数据流`/`形状子集`/`标题栏 24 px` 等，带"作废/历史/取代"标记的行允许）；② **引用可达性**（`docs/**.md` 与 `ADR-00XX` 是否都能落到实际文件；ADR-0047 按"从未产出"豁免）；③ **数值唯一性抽查**（如遮罩不透明度）；④ **结构完整性**（11 份分册齐备、旧文件不在现行路径、`docs/adr` 已移除）；⑤ **skills 去事实**（SKILL.md 正文不得含本项目专名）。任一未过会输出具体 `文件:行号`。

