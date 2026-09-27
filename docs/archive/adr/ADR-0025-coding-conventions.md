> ⚠ 历史材料（ADR 原文，2026-09 文档收敛起归档）。**现行决策**见 `docs/decisions.md`；实现层现行值见 `docs/architecture/` 各分册。正文为当时的决策与论证；其中的"待决/待办/计划"一律以现行文档为准。

# ADR-0025 代码风格与开发约束（议程 ② 冻结）

> 状态：**Accepted**（用户裁决，实现前置议程 ②）
> 关联阶段：实现前置（环境就绪后）
> 关联文档：`docs/implementation/01-coding-style-and-dev-conventions.md`（细则全文）、`implementation/00`（纪律 C1–C4）
> 依据：`上位机架构设计.md` v1.0 · ADR-0021（测试架构）· ADR-0023/0024（栈与许可）
> 取代：无 · 被取代：§2 的 A1–A5 架构规则集（部分）由 **ADR-0032** 重写为 AR-1~AR-16
> **收窄/修订说明**：§2 的 A1–A5 架构规则集已由 **ADR-0032** 重写为 AR-1~AR-16（依赖类走程序集元数据、源码类走源码扫描；A3 口径变为 `Ui` 只引 `Sdk` + `Core.Api`）；其余 7 项纪律与 5 项开关不变。

## 决策

确立 8 项工程纪律（细则见 `implementation/01`）：

1. **编码规范**：`.editorconfig` 随仓；`Nullable/ImplicitUsings/LangVersion=latest`；命名与文件组织约定；禁用 `#region`/`async void`/吞异常/魔法数字。
2. **架构约束自动化**（**自研反射检查**，违反即测试失败）：A1 Core 不引用 UI/Avalonia/具体模块；**A2 模块之间不得相互引用**（顶层 P12）；A3 UI 仅依赖 SDK 契约；A4 命名空间与目录一致；A5 Core 内禁止具体业务词汇（P1/P2/P13、ADR-0003）。
3. **数据面性能纪律**：热路径禁 LINQ/装箱/闭包/每单元分配/异常控制流；改用 `Span`/`ArrayPool`/`ValueTask`；关键路径建 BenchmarkDotNet 基准，热路径改动须给出分配数据。
4. **提交与审查**：Conventional Commits；提交前 `build.ps1` 门禁（清理 `Platform` 环境变量 → 依赖许可审计 → 构建 → 测试）；5 条变更自查清单；**单人直提 `main`**。
5. **文档要求**：SDK 公共 API 必须有 XML 注释；模块 Manifest 必填字段校验；**6 种情形必须写 ADR**（影响 v1.0 结论 / 新核心概念 / 新依赖 / 改模块契约 / 性能策略变更 / 例外）。
6. **测试要求**：**模块验证 + 组合验证**（对齐纪律 C1）；持久化 round-trip、故障注入、`Avalonia.Headless` UI 测试；**不设覆盖率百分比**，以"机制改动必须有组合级验证"为准。
7. **依赖纪律**：许可 ∈ {MIT, Apache-2.0, BSD}（构建期门禁含传递依赖）；禁止 GPL/LGPL、需采购/登录授权的组件、**无许可声明的包**（首例 `AvaloniaUI.DiagnosticsSupport` 已被拦截移除）。
8. **例外机制**：`// ARCH-EXCEPTION: <原因> · <复查条件>` + 决策日志；禁止静默绕过；例外清单每批次收尾复查。

**开关裁决**：S1 仅内置分析器 · S2 警告即错误（开启） · S3 自研架构测试 · S4 直提 main · S5 注释与文档中文（标识符英文）。

## 影响

- 实现阶段的代码与提交必须遵守本 ADR 及其细则；`build.ps1`（含许可门禁）与 `.editorconfig`、中央包管理为**必须交付的工程化基线**（稍后一并落地）。
- 架构测试项目（`tests/*.ArchitectureTests`）为测试骨架的**必备项**。

## 修订记录

| 版本 | 日期 | 内容 | 状态 |
|---|---|---|---|
| v1.0 | 议程 ② | 用户确认 8 项纪律与 5 项开关 | **Accepted** |
