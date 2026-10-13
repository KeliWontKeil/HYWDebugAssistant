> ⚠ 历史材料（ADR 原文，2026-09 文档收敛起归档）。**现行决策见 `docs/architecture/CODEX.md`，架构例外见 `docs/architecture/EXCEPTIONS.md`。正文为当时的决策与论证；其中的"待决/待办/计划"一律以现行文档为准。

# ADR-0024 库级选型与许可策略（技术选型 阶段一收尾）

> 状态：**Accepted**（用户确认，技术选型阶段）
> 关联阶段：技术选型（Gate A）
> 关联文档：`docs/technology-selection/06-library-selection.md`、`05-environment-checklist.md`；ADR-0023（栈方向）
> 前置裁决（用户）：许可仅 MIT/Apache-2.0/BSD；**禁止 GPL/LGPL 与任何采购**
> 取代：无 · 被取代：无（§7 脚本宿主、§4 停靠面板评估位被收窄/不采用，见下）
> **收窄/修订说明**：§7 脚本宿主在 v1 不实现（**ADR-0028** §9）；§4 停靠面板评估位不采用（**ADR-0031** Q5-1：停靠 = 几何吸附，不做容器式窗口停靠）。

## 决策

1. **库级选型清单确认**（详见 06 文档 A–E 部分；**版本为技术选型时经 nuget.org 核实的最新稳定版**）：**Avalonia 12.1.2**（MIT）· `Avalonia.Controls.DataGrid` 12.1.2（MIT）· **ScottPlot 5.1.59** / `ScottPlot.Avalonia` 5.1.59（MIT）· SkiaSharp（MIT）· 内置 `System.IO.Ports` / `System.Net.Sockets` / `Channels` / `Buffers` / `Pipelines` / `Intrinsics` · `Microsoft.Extensions.{DependencyInjection,Configuration,Logging}`（MIT，随 .NET 10）· `System.Text.Json`（MIT，含 Source Generator）· `CommunityToolkit.Mvvm` 8.4.2（MIT）· **xUnit**（2.9.3 或 xunit.v3 4.0.1；Apache-2.0）+ **Shouldly** 4.3.0（BSD-3）+ **NSubstitute** 6.2.0（BSD-3）+ **BenchmarkDotNet** 0.15.8（MIT）+ **Avalonia.Headless** 12.1.2（MIT）；模块加载用 `AssemblyLoadContext`（内置）。
2. **日志后端**：**极简自研** —— `Microsoft.Extensions.Logging` 抽象 + 自研文件 Sink（不引入 Serilog）。
3. **FFT / 数值**：**引入 FFT 库**（`FftSharp` 或 `MathNet.Numerics`，均 MIT；实现阶段按实测择一）。
4. **停靠 / 面板**：**预留评估位** —— v1 自研简化方案；后续若需求上升再评估开源 Dock（须先核许可）。
5. **明确排除**（不得引入）：MediatR、AutoMapper、FluentAssertions 8+、Moq（争议版本）、Qt（LGPL）、DevExpress/Telerik/Actipro/Syncfusion 等商业控件、Newtonsoft.Json（非必要）、Electron/WebView 方案。
6. **许可审计门禁（工程要求）**：构建期脚本对**直接 + 传递依赖**核验许可，出现 GPL/LGPL/商业许可即失败；依赖锁文件（`packages.lock.json`）保证可复现。
7. **脚本宿主**（可选行为载体）：语言选择留待后续 ADR，候选均须满足第 6 条（MoonSharp BSD-3 / NLua MIT / Jint BSD-2 / ClearScript MIT）。

## 影响

- 技术选型（Gate A）中的"运行时/UI 框架/DI/序列化/日志/配置/测试框架"等项**全部落定**；剩余 Gate 项（进程/隔离模型、数值指标、UI 原型细节等）转由技术架构设计与原型阶段处理。
- 下一步：**技术架构设计**（分层与依赖方向、项目结构细化、模块 SDK 形态、参考组合与判据落地、关键机制的技术方案）。

## 修订记录

| 版本 | 日期 | 内容 | 状态 |
|---|---|---|---|
| v1.0 | 技术选型 | 用户确认库级打包；日志极简自研；引入 FFT 库；停靠预留评估位 | **Accepted** |
| v1.1 | 环境核对（阶段二前） | **许可审计首次实际拦截**：Avalonia 模板默认引入的 `AvaloniaUI.DiagnosticsSupport`（Avalonia Accelerate 商业线：nuspec 无许可声明、Community 版强制遥测）**已从工程移除**（csproj + `Program.cs` 的 `WithDeveloperTools()`）。核实结论：Avalonia 12.1.2 核心包为 **MIT、无需登录/授权**；`Avalonia.BuildServices` 为**构建期匿名遥测（MIT、运行时不采集）**，可经 `AVALONIA_TELEMETRY_OPTOUT=1` 关闭；构建输出中的 "Accelerate Community requires telemetry" 仅为提示文本，不影响构建。详见 `technology-selection/05` §7 | **Accepted** |
