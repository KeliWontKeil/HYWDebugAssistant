> ⚠ 历史材料（ADR 原文，2026-09 文档收敛起归档）。**现行决策见 `docs/architecture/CODEX.md`，架构例外见 `docs/architecture/EXCEPTIONS.md`。正文为当时的决策与论证；其中的"待决/待办/计划"一律以现行文档为准。

# ADR-0001 领域概念重构与装配链

> 状态：Accepted（用户裁决，批次 1）
> 关联批次：批次 1
> 关联主文档章节：§3、§4.1、§4.2、§9.1、§10.1、术语表
> 取代：无（早期基线"Application/Shell/Core/Workspace 平级对象"表述作废，由本文档取代）
> 被取代：部分条目被 ADR-0016（持久化模型重构：抽象工作区 + 组合件载体）取代

## 问题

早期基线把 Application Shell、Core Runtime、Workspace 当作三个平级顶层对象，且 Workspace 被暗示为一个可感知容器。用户评审指出：

1. **Workspace 只是抽象概念**，不是用户可感知的"容器/框"；由 model 与 Core 搭起的整个可交互区域构成抽象的 Workspace。具体容器感会让软件显得"过于专业、缺少即插即用既视感"。但数据层仍需一个聚合单元（拓扑/路由/布局）用于保存与恢复。
2. **Shell 与 Core 不是两个独立对象**：二者同生命周期、双向引用、不可分离，平级建模是人为分割（PQ-DM-05 / C1）。

## 背景

- 已冻结：P10（用户操作组合而非数据流）、P1/P2（Core=Host、不做中央交换机）、"Workspace ≠ UI Canvas"、全在线变更/即插即用（§7.1）。
- 用户心智：模块直接"拼接"，无 Workspace 框；"即插即用既视感"是产品体验目标。

## 候选方案

### 方案 A：Application 整体 + 两逻辑构成面；Workspace 去容器化（采纳）

- Application = 平台整体：**UI 构成（术语"Shell"）**（主窗口/全局 UI/命令/模块视图宿主）与 **Core 构成（语义内核）**（Registry/模块宿主/运行时/Composition→Runtime/Workspace 文档管理）。
- Shell/Core 不再作为平级顶层领域对象；"Shell"降为 UI 构成面的实现术语。
- Workspace = **抽象概念 + 文档聚合单元**：无任何 UI 容器语义；文档内容 = Module Instance 集合 + Composition + Layout（+版本等）；由 Core 创建与管理。
- 模块视图（Presentation）直接挂载于 Application 的模块区（自由拼接），由 Instance 派生，可独立开关/重排。
- 装配：Application 初始化 → 创建 Core 构成 → 创建 UI 构成（引用 Core）。

### 方案 B：保留 Application/Shell/Core 三对象但重定义职责

Shell=UI 面、Core=语义面，二者同生命周期但仍是独立领域对象。缺点：概念表多一个平级对象，与"用户不可感知 Shell 存在"的心智不符；领域对象粒度偏实现化。

### 方案 C：维持早期基线（Shell 承载 Core 与 Workspace 容器）

与用户心智冲突（Workspace 容器感、Shell/Core 平级化），否决。

## 最终选择

方案 A。

## 选择理由

1. Workspace 去容器化符合用户心智与"即插即用"体验目标；数据层聚合单元仍存在（文档），满足保存/恢复与拓扑/布局存储需要。
2. Shell/Core 同生命周期、不可分离 → 建模为 Application 的两个构成面，消除人为平级对象；职责边界仍清晰（表现/语义），可由不同工程组织承载，但领域概念上不割裂。
3. 消除早期图中"Workspace 在 Shell 之下"的歧义（C1 闭合）。

## 放弃方案

- B：领域对象粒度偏实现化、概念表冗增。
- C：与用户心智冲突、保留"容器"歧义。

## 影响

- 主文档 §3（系统级结构重写）、§4.1 概念表（Application/UI 构成/Core/Workspace 行重定义，增 Module Registry 行）、§4.2 对象图重绘、§9.1（Workspace 原则）、§10.1（组织单元表述）、术语表同步。
- PQ-DM-05（装配链）关闭。
- Presentation 专题（批次 7）不再论证"Workspace 容器"形态，只论证模块视图在模块区内的排列形态。

## 修订记录

| 版本 | 日期 | 内容 | 状态 |
|---|---|---|---|
| v1.0 | 批次 1 | 用户裁决采纳方案 A | Accepted |
