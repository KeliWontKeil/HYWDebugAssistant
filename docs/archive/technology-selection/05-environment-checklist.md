> ⚠ 历史材料（过程记录，2026-09 归档）。**现行状态**见 `docs/roadmap.md`（状态唯一源）与 `docs/README.md`。

# 开发环境配置清单（05）——供你自行配置

> **状态：环境事实（部分已被取代）**——工程目录与项目命名以 `docs\architecture\02-project-structure.md` §2.1 为准（`HYWDebugAssistant.*`）；文中出现的 `Platform.Core` / `Platform.Modules.Sdk` / `Platform.UI` / `Platform.App` / `Reference.Assembly` 均为**早期草案命名，已作废**。
> 阶段：技术选型 → 环境准备。**本文件只给清单与方向，不代为执行配置。**
> 目标环境：Windows 10/11（首要平台）；保留跨平台能力（Avalonia + .NET）。

---

## 1. 运行时与 SDK

| 项 | 建议 | 说明 |
|---|---|---|
| **.NET SDK** | **已定为 .NET 10**（`net10.0`，SDK **10.0.401**；工程根 `global.json` 锁定，`rollForward: latestPatch`） | 不要用 STS 版本做大项目基线（生命周期短） |
| 目标框架（TFM） | **统一 `net10.0`** | 统一全部项目 |
| **不使用** Native AOT | — | 与 ADR-0020 模块动态加载、可选脚本冲突（ADR-0023 §1） |
| GC 配置（运行时） | 应用级 `runtimeconfig`：`System.GC.Server=true`、`System.GC.Concurrent=true`；必要时运行期切换 `GCSettings.LatencyMode = SustainedLowLatency` | 性能相关，需在原型中实测（验证项） |

## 2. 开发工具（**注意许可**：本项目禁止采购）

| 项 | 建议 | 许可提示 |
|---|---|---|
| **IDE** | **Visual Studio Community**（免费）或 **VS Code + C# Dev Kit**（免费层） | Rider 为付费产品 → 若坚持"零采购"，选免费 IDE |
| Avalonia 模板 | `dotnet new install Avalonia.Templates` | MIT |
| Avalonia 预览器 | Avalonia Previewer（VS/VS Code 扩展） | MIT |
| 调试/性能 | `dotnet-counters`、`dotnet-trace`、`dotnet-gcdump`、PerfView（可选） | MIT |
| 版本控制 | Git（含 `.gitignore` 的 `VisualStudio.gitignore` 模板） | 宽松 |

## 3. 工程骨架与依赖管理

| 项 | 建议 |
|---|---|
| 解决方案结构 | 单解决方案 + 分层项目（数据面 / UI / 模块 SDK / 参考模块 / 测试）——**分层与依赖方向在技术架构设计中敲定** |
| 依赖管理 | NuGet + **中央包管理**（`Directory.Packages.props`）+ `NuGet.config`（锁定源） |
| 版本锁定 | `packages.lock.json`（`RestorePackagesWithLockFile`）保证可复现构建 |
| 分析器 | `.editorconfig` + `EnableNETAnalyzers` + 可选 IDE 分析器（全部 MIT/内置） |
| 构建脚本 | `dotnet build/publish`（后续可加 CI，如 GitHub Actions 免费层） |

## 4. 许可审计（硬约束，ADR-0023 §6）

| 项 | 建议 |
|---|---|
| 依赖许可清单 | `dotnet list package --include-transitive` 导出 → 用工具/脚本核对许可 |
| 建议工具 | **自研 `tools/audit-licenses.ps1`**（从本地 `.nuspec` 解析许可 + 白名单门禁）；**不使用第三方许可工具**（ADR-0024 §6） |
| 门禁 | 构建期脚本：出现 GPL/LGPL/商业许可依赖即失败（CI 门禁） |
| **已知陷阱库（务必避开或核实）** | **FluentAssertions 8+**（转商业许可）、**MediatR / AutoMapper**（转商业许可）、**Moq 4.20+**（SponsorLink 争议）、QuestPDF/ImageSharp 等的许可变更史 → 选型时逐一核实"当前版本"许可，而非历史印象 |

## 5. 性能与验证准备（选型后、原型阶段使用）

| 项 | 用途 |
|---|---|
| BenchmarkDotNet（MIT） | 数据面吞吐/延迟基准 |
| 压力工具 | 自建"数据发生器"桩模块（参考组合，ADR-0021 T1） |
| 波形性能测试 | 10⁵–10⁶ 点刷新与 CPU/GPU 占用（Avalonia + ScottPlot/自绘对照） |
| 抖动观测 | `dotnet-counters` 观察 GC 次数/暂停；必要时 `dotnet-trace` 采样 |

## 6. 目录结构（方向性建议，最终以技术架构设计为准）

```
上位机开发/
├─ 上位机架构设计.md              ← v1.0 顶层基线（验收事实源）
├─ docs/                          ← ADR / roadmap / changelog / archive / 技术选型 / 方法论
├─ src/
│  ├─ Platform.Core/              ← 语义内核（Registry/生命周期/绑定/网络/持久化）
│  ├─ Platform.Modules.Sdk/       ← 模块 SDK（原语层契约 + 行为层接口 + Manifest）
│  ├─ Platform.UI/                ← Avalonia UI（模块视图宿主 + 叠加层 + 面板）
│  ├─ Platform.App/               ← 应用入口（组合 Core 与 UI）
│  └─ Modules.*/                  ← 内置模块（串口/TCP/解析/波形/日志…）
└─ tests/
   ├─ Platform.Tests.*/           ← 分层测试（T1–T7，ADR-0021）
   └─ Reference.Assembly/         ← 参考组合（桩模块：发生器/记录器/断言器）
```

> 注：以上为**建议结构**，最终以技术架构设计为准；文档组（`D:\project\上位机开发\docs`）**不进入正式工程**（用户裁决：正式工程只留必要描述）。

---

## 7. 环境实测发现（工具链核对，含许可审计首次拦截）

| # | 发现 | 处理与结论 |
|---|---|---|
| **E1** | **`Platform` 环境变量被污染**（值 = `C:\Users\MSI-NB\AppData\Roaming\Python\Python39\Scripts`）→ 解决方案构建报 **MSB4126**（配置 "Debug\|<Python路径>" 无效） | CLI 构建前清除该变量（`Remove-Item Env:Platform`）或显式 `-p:Platform="Any CPU"`；**建议在系统环境变量中移除该变量**（Python 安装遗留，会影响所有 MSBuild 命令行构建） |
| **E2** | Avalonia 模板**默认包含 `AvaloniaUI.DiagnosticsSupport`**（Avalonia Accelerate 商业线：nuspec **无许可声明** + 强制遥测） | **违反 ADR-0024 许可约束 → 已移除**（csproj 包引用 + `Program.cs` 的 `WithDeveloperTools()`）；开发期诊断改用自建可观测性（L1–L5）与 VS 调试 |
| **E3** | **`Avalonia.BuildServices`**（传递依赖）= 构建期**匿名遥测**（MIT；**仅构建期**，运行时不采集、不影响部署产物） | 许可合规 ✓；可用环境变量 **`AVALONIA_TELEMETRY_OPTOUT=1`** 关闭上报（官方方式） |
| **E4** | 构建输出打印 "Avalonia Accelerate Community requires telemetry. To opt out, please upgrade to a paid tier." | 系 `AvaloniaStatsTask`（`Avalonia.BuildServices.targets`）在**无 Accelerate 许可键**时打印的**提示文本**；**不影响构建**（0 错误 0 警告），**不代表项目依赖商业组件**；实测 opt-out 后文本仍出现（提示与上报逻辑独立） |
| **E5** | dotnet CLI **默认不含 Avalonia 模板** | 已安装 `Avalonia.Templates 12.1.2`（用户授权）；可用模板：`avalonia.app` / `avalonia.mvvm`（默认 CommunityToolkit + net10.0）/ `avalonia.xplat` |
| **E6** | SDK 版本 | 默认 `10.0.401`；已在工程根用 `global.json` 锁定（`rollForward: latestPatch`） |

**许可要点澄清（回应"是否需登录/授权"）**：

| 组件 | 许可 | 是否需要登录/授权 |
|---|---|---|
| Avalonia 12.1.2 核心包（Avalonia/Desktop/Themes/Fonts/Skia/Win32…） | **MIT**（nuspec `license type="expression"` 明确） | **不需要**（可闭源商用、自由分发） |
| `AvaloniaUI.DiagnosticsSupport`（Accelerate 商业线） | **未声明许可** + Community 版要求遥测 | 需账号/许可层级 → **本项目已移除** |
| `Avalonia.BuildServices`（构建遥测） | **MIT** | 不需要；可 opt-out |
| Visual Studio 本体 | 微软许可 | **需要登录**（属 VS 自身要求，与 Avalonia 无关） |

**工程骨架已就位**：`D:\project\c# project\HYWDebugAssistant\`（`HYWDebugAssistant.slnx` + `src/HYWDebugAssistant`）= Avalonia 12.1.2 + net10.0 + CommunityToolkit.Mvvm 8.4.2；**构建通过（0 错误 0 警告）**。
