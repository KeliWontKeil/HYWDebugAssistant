> ⚠ 历史材料（过程记录，2026-09 归档）。**现行状态**见 `docs/roadmap.md`（状态唯一源）与 `docs/README.md`。

# 06 · 代码仓库引用修正报告

范围：只改 `D:\project\c# project\HYWDebugAssistant\` 下三个文件（`LESSONS.md` / `UI-ADJUST.md` / `DEVELOPMENT.md`）。
`src/`、`tests/`、`tools/` 下的代码**未改动**。
行号 = 改前 = 改后（全部为同行内替换，未增删行）。

约定：
- 旧 `architecture/NN` → 新分册名（`docs/architecture/0N-…`）；
- 旧 `ADR-00XX`（作为**现行结论**的依据）→ `docs/decisions.md`（现行决策表）；
- 仅当引用的是**被取代的历史原文**时，才指向 `docs/archive/adr/ADR-00XX-….md`；
- `docs/implementation/02` 的 `P##` 编号、`docs/implementation/04*` 路径：**保持不变**（P 表仍在 `implementation/02`，只是序号重排）；
- 主文档章节号：本次三个文件中无此类引用，无需处理。

---

## 1. LESSONS.md（6 行 / 7 处改写，另 1 处引用确认不变）

| # | 行号 | 原文引用 | 改后引用 |
|---|---|---|---|
| 1 | 5 | `docs/implementation/02`（P1–P59 工程坑位日志） | **不变**（P 表仍在 `implementation/02`） |
| 2 | 5 | `docs/architecture/14-ui-rework-m1-16.md`（设计与裁决记录） | `docs/architecture/06-ui-shell-and-windows`（设计与裁决记录） |
| 3 | 5 | `docs/adr/ADR-0052`（角色 = 模块声明的功能分块） | 与原第 4 项**合并**为 `docs/decisions.md`（现行决策表：角色 = 模块声明的功能分块 · 叠加层的两个信息维度） |
| 4 | 5 | `docs/adr/ADR-0053`（叠加层的两个信息维度） | 同上（两个括号内容均保留，只合并了同一目标的重复路径） |
| 5 | 91 | `architecture/15` §8.3 | `docs/architecture/07-overlay-and-visual-baseline` 的「拼图组、外壳跟随与平移落盘」（该小节标题已在新分册 07 中 grep 确认存在，且其「源」标注正是 `15 §8.3`） |
| 6 | 93 | （ADR-0045） | （`docs/decisions.md`） |
| 7 | 107 | ADR-0046 D6 说「…」 | `docs/decisions.md` 说「…」（该现行结论 = 决策表 类 4.5） |
| 8 | 128 | ADR-0045 早就定了 | `docs/decisions.md` 早就定了 |
| 9 | 138 | ADR-0048 让角色 =（方向 × 类型身份）**派生** | `docs/archive/adr/ADR-0048-module-data-role-model.md` 让角色 = **派生**（此处 ADR-0048 是**已被 ADR-0052 取代的历史口径**，指原文；其现行结论在决策表 类 4.13） |

## 2. UI-ADJUST.md（12 行 / 18 处）

| # | 行号 | 原文引用 | 改后引用 |
|---|---|---|---|
| 1 | 111 | `architecture/15` | `docs/architecture/07-overlay-and-visual-baseline` |
| 2 | 111 | + ADR-0042 | + `docs/decisions.md` |
| 3 | 115 | （ADR-0042） | （`docs/decisions.md`） |
| 4 | 123 | （**端口标记内容重排**；ADR-0043） | （**端口标记内容重排**；`docs/decisions.md`） |
| 5 | 134 | `implementation/02` P38 | **不变**（P 编号保持） |
| 6 | 136 | （**方向语义色与主题色解耦**；ADR-0044） | （**方向语义色与主题色解耦**；`docs/decisions.md`） |
| 7 | 148 | （**叠加信息层重设计**；ADR-0045） | （**叠加信息层重设计**；`docs/decisions.md`） |
| 8 | 159 | 已随 ADR-0052 删除 | 已按 `docs/decisions.md` 的现行结论删除 |
| 9 | 169 | `implementation/02` **P40** | **不变**（P 编号保持） |
| 10-11 | 180 | ADR-0048/0049（第十八轮） | 删除括号内的 ADR 编号；在标题末尾统一给出 `（以上各轮的现行结论均见 docs/decisions.md）` |
| 12-15 | 180 | ADR-0052 / ADR-0053 / ADR-0054（第三十三·三十四轮标签） | 同上（合并为标题末尾一处 `docs/decisions.md` 指针；四轮的轮次名与描述**一字未动**） |
| 16 | 182 | **ADR-0052 + ADR-0053（含 §5 二次修正）** | **`docs/decisions.md` 的现行结论（含二次修正）** |
| 17 | 201 | （侧边栏 交互 页底部 · **ADR-0054**） | （侧边栏 交互 页底部 · `docs/decisions.md`） |
| 18 | 202 | 设置页 4 处 · ADR-0054 §2.1 | 设置页 4 处 · `docs/decisions.md` |
| 19 | 203 | （ADR-0054 §2） | （`docs/decisions.md`） |

> 说明：第 10–15 处共 6 个 ADR 编号都指向同一文件，逐处替换会让标题出现 4 次 `docs/decisions.md`，故合并为标题末尾一处指针；四个轮次标签（第十八 / 三十三 / 三十四 / 三十四二次）与全部做法描述保持原样。

## 3. DEVELOPMENT.md（2 行 / 2 处；另有 3 行确认无需改）

| # | 行号 | 原文引用 | 改后引用 |
|---|---|---|---|
| 1 | 7 | （任务分解、**ADR**、验收清单以那边为准） | （任务分解、**现行决策表 `docs/decisions.md`**、验收清单以那边为准）—— ADR 层已收敛为决策表，此处是文档层名而非编号引用，属**判断项**，见 §5 |
| 2 | 62 | 涉及契约/格式的先看 `docs/adr/` 里对应决定 | 涉及契约/格式的先看 `docs/decisions.md` 里对应决定 |
| — | 43 | `implementation/04` 的手工验收 | **不变**（`docs/implementation/04-*` 仍存在） |
| — | 53 | `docs/implementation/02` P14 | **不变**（P 编号保持） |
| — | 65 | `docs/implementation/04-m1-manual-acceptance.md` | **不变**（文件实际仍为此名） |
| — | 34 | 架构规则与项目引用（AR-1~AR-16） | **不变**（映射表未涉及；AR 编号未在本次收敛范围内） |
| — | 67 | 模块只引 `Sdk`（AR-1/AR-2） | **不变**（同上） |

---

## 4. 自查（旧编号残留）

对三个文件 `grep` 正则：`architecture/\d`、`docs/adr`、`ADR-00`、`§\d+\.\d+`

- 残留的 `architecture/NN` 命中**只有新名**：`docs/architecture/06-ui-shell-and-windows`（LESSONS 5）、`docs/architecture/07-overlay-and-visual-baseline`（LESSONS 91、UI-ADJUST 111）。
- `architecture/03a`、`04a`、`04b`、`05`、`09`、`14`、`15`、`16`、`17`、`18` 及带小节号的旧式引用（如 `architecture/02 §2.1`）：**均无残留**。
- `docs/adr/`（旧目录路径）：**无残留**（`docs/archive/adr/` 例外，见 LESSONS 138，为有意保留的原文指针）。
- `ADR-00` 命中**只有一处**：LESSONS 138 的 `docs/archive/adr/ADR-0048-module-data-role-model.md`（有意保留）。
- 三个文件都没有引用主文档章节号，故无需"保持不变"校验。

## 5. 未能判定 / 需主 agent 裁决的引用

| 文件 / 行号 | 原文 | 问题 |
|---|---|---|
| `LESSONS.md` 84 | 「28. **文档先行**：ADR（设计冻结）→ 主文档回写 → 实现；」 | 这里的 "ADR" 是**流程阶段的层名**（"文档先行"这条纪律本身），不是路径也不是编号引用。按"只改引用、不改流程内容"的约束**未改**。若收敛后设计冻结层已改称「决策表/主文档」，此处需由主 agent 决定是否改写 —— 改写会动到流程表述本身。 |
| `DEVELOPMENT.md` 7 | 「（任务分解、ADR、验收清单以那边为准）」 | 同上的层名，但我判断它描述的是**另一个仓库的文档结构**（结构已变），故已改为「现行决策表 `docs/decisions.md`」。若判定越界，回滚此一处即可（仅此一行、一处）。 |
| `LESSONS.md` 5 | 「`docs/implementation/02`（P1–P59 工程坑位日志）」 | P 编号按要求保持不变，但**区间上界 P1–P59** 是本次收敛前 `implementation/02` 的条目数；若序号重排后条数变化，此处区间可能失真。属编号以外的描述性事实，未改。 |
| `DEVELOPMENT.md` 34 / 67 | 「AR-1~AR-16」「AR-1/AR-2」 | 架构规则编号未在映射表中给出；若 AR 编号也随本轮收敛重排，需补一条映射。**未改**。 |
| `LESSONS.md` 5 | `docs/architecture/06-ui-shell-and-windows`（设计与裁决记录） | 旧 14 被映射到新 06；但原括号「设计与裁决记录」是**旧 14 的分册性质**，新 06 是「外壳与窗口」分册。若希望更贴切，可改为「`docs/architecture/06-ui-shell-and-windows` 的「UI 开发方式约定」」（该小节标题已确认存在）。 |
