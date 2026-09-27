> ⚠ 历史材料（过程记录，2026-09 归档）。**现行状态**见 `docs/roadmap.md`（状态唯一源）与 `docs/README.md`。

# 库级选型打包推荐（06）——一次性审核

> 阶段：技术选型。约束：**仅 MIT / Apache-2.0 / BSD 等宽松许可；禁止 GPL/LGPL 与任何采购**（ADR-0023 §6）。
> 说明：标注「⚠待核实」者为**需要在下单前核对当前版本许可**的项（许可会变更，历史印象不可靠）；核实在实现阶段以依赖审计脚本把关。

## A. 运行时 / UI / 图形

| 用途 | 推荐 | 许可 | 备注 |
|---|---|---|---|
| 运行时 | .NET 最新 LTS | MIT | 见 05 清单 |
| UI 框架 | **Avalonia 12.1.2** | MIT | ADR-0023；版本经 nuget.org 核实 |
| 数据网格 | `Avalonia.Controls.DataGrid` 12.1.2（官方） | MIT | 与 Avalonia 保持同版本 |
| 主题 | Avalonia 内置 Fluent / Simple 主题 | MIT | 起步够用；⚠待核实额外主题包（Semi/Material 类） |
| 波形/曲线 | **ScottPlot 5.1.59**（含 `ScottPlot.Avalonia` 5.1.59） | MIT | 起步用；性能不足时下沉自绘（Skia） |
| 2D 自绘 | SkiaSharp（Avalonia 内置渲染栈） | MIT | 自定义控件/叠加层绘制 |
| 停靠面板 | v1：**不采用**（原候选 = 自研简化方案；我们的形态是自由排列窗口，停靠需求低） | — | **经评估不采用**——ADR-0031 Q5-1：v1 的"停靠"仅指**几何吸附**，不做容器式窗口停靠；若后续确有需求再评估开源 Dock（⚠待核实许可） |

## B. 数据面与通信

| 用途 | 推荐 | 许可 | 备注 |
|---|---|---|---|
| 串口 | `System.IO.Ports` | MIT | Windows 可靠；Linux 支持有限（跨平台时评估） |
| TCP/UDP | `System.Net.Sockets` | MIT（内置） | — |
| 进程内管道 | `System.Threading.Channels` | MIT（内置） | 有界缓冲 + 丢帧策略的落点 |
| 缓冲区/零分配 | `System.Buffers`（`ArrayPool`）、`Span<T>`、`Memory<T>` | MIT（内置） | 数据面零分配设计基础 |
| 流式管道 | `System.IO.Pipelines` | MIT（内置） | 解析器（帧同步）适用 |
| SIMD | `System.Runtime.Intrinsics` | MIT（内置） | 滤波/数值处理 |
| FFT | `FftSharp` 与 `MathNet.Numerics`（**二者在实现时按功能与性能实测择一**） | MIT | 未定其一（ADR-0024 §3）；当前版本许可由许可审计门禁把关 |
| 脚本宿主（可选，后续 ADR） | 候选：MoonSharp / NLua / Jint / ClearScript | BSD-3 / MIT / BSD-2 / MIT | 语言决策留后续 ADR；许可均宽松 |

## C. 架构支撑

| 用途 | 推荐 | 许可 | 备注 |
|---|---|---|---|
| 依赖注入 | `Microsoft.Extensions.DependencyInjection` | MIT | — |
| 配置 | `Microsoft.Extensions.Configuration`（+ JSON 提供程序） | MIT | — |
| 日志 | `Microsoft.Extensions.Logging`（内置抽象）+ **自研文件 Sink**（**不引入 Serilog**） | MIT | ADR-0024 §2：**日志极简自研**（依赖最少、全 MIT）；不用 Serilog |
| 序列化 | **System.Text.Json**（含 Source Generator） | MIT | 组合件文本格式；未知字段保真需配置 |
| MVVM | `CommunityToolkit.Mvvm` | MIT | 源生成器、免样板 |
| 模块加载 | `AssemblyLoadContext`（内置） | MIT（内置） | 卸载/多版本共存（ADR-0020） |
| 进程间（未来） | `System.IO.Pipes` / `MemoryMappedFile` | MIT（内置） | 仅在未来需要进程隔离时 |

## D. 测试与质量

| 用途 | 推荐 | 许可 | 备注 |
|---|---|---|---|
| 测试框架 | **xUnit** | Apache-2.0 | — |
| 断言 | **Shouldly** 或 xUnit 内置断言 | BSD-3 | ⚠**避开 FluentAssertions 8+（已转商业许可）** |
| 测试替身 | **NSubstitute** | BSD-3 | ⚠避开 Moq 争议版本（SponsorLink） |
| UI 无头测试 | **Avalonia.Headless** | MIT | 支撑 T8（UI 自动化）落地 |
| 基准测试 | **BenchmarkDotNet** | MIT | 数据面吞吐/延迟基准 |
| 许可审计 | **自研 `tools/audit-licenses.ps1`**：从本地 `.nuspec` 解析许可（SPDX 表达式 / 许可文件文本 / 常见许可 URL）+ 白名单门禁；**不使用第三方许可工具** | —（工程内自研脚本） | 构建期门禁：禁止 GPL/LGPL/商业（ADR-0024 §6） |

## E. 明确不选（含理由）

| 项 | 理由 |
|---|---|
| MediatR / AutoMapper | **已转商业许可**（不合"零采购"约束） |
| FluentAssertions 8+ | 已转商业许可 |
| Moq（争议版本） | SponsorLink 争议，改用 NSubstitute |
| DevExpress / Telerik / Actipro / Syncfusion | 商业控件（用户明确禁止采购） |
| Qt（含 QML） | LGPL 合规成本 / 商业授权（ADR-0023 §6） |
| Newtonsoft.Json | 非必要（System.Text.Json 足够），减少依赖面 |
| Electron / WebView 方案 | 与"单栈 + 性能优先"不符（技术选型 04 已评估） |

## F. 开放点已定案（用户裁决）

1. **日志后端**：**极简自研**——用 `Microsoft.Extensions.Logging` 抽象 + 自研文件 Sink（依赖最少、全 MIT）；不引入 Serilog。
2. **FFT / 数值**：**引入 FFT 库**（`FftSharp` 或 `MathNet.Numerics`，均 MIT；具体择一在实现时按功能与性能实测决定）。
3. **停靠 / 面板**：**预留评估位**——v1 优先自研简化方案（本项目形态为自由排列模块视图，停靠需求低）；若后续需求上升，再评估开源 Dock 方案（**必须先核许可**且满足 ADR-0023 §6）。**加注（经评估不采用）**：ADR-0031 Q5-1 已明确 v1 的"停靠"仅指**几何吸附**，不做容器式窗口停靠。
