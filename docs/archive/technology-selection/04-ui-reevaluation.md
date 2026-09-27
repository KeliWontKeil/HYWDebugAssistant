> ⚠ 历史材料（过程记录，2026-09 归档）。**现行状态**见 `docs/roadmap.md`（状态唯一源）与 `docs/README.md`。

# 技术选型：UI 层重评估（WPF / Avalonia / Web）——04

> **状态：历史论证材料**——本文件的 UI 框架候选比较结论**已由 ADR-0023 关闭**（选定 Avalonia 12.1.2）；文中"WPF 更快落地""天平接近"等均为当时评估，不再作为决策依据。
> 阶段：技术选型。触发：用户补充 UI 预期（**Windows 风格、工程化专业、效率优先、不追求视觉效果、不排斥现成组件/库、连线作为叠加图层**），使"成熟控件生态"权重上升，故将 **WPF** 拉回候选并重评。
> 前提：数据面已定 **C#**（含"热点可下沉原生库"路径）。

---

## 1. 更新后的 UI 需求

| 编号 | 需求 |
|---|---|
| U1 | 模块视图**自由排列**（无容器、拼图式），Windows 原生窗口行为 |
| U2 | 几何对齐/吸附（纯排版） |
| U3 | 口上**网络标记**（小标签：名称弱化 + 方向 + 状态） |
| U4 | **连线作为叠加图层**（默认隐藏，热键/按钮切换，有向流动样式）——**不深度耦合模块 UI** |
| U5 | **实时波形自绘**（10⁵–10⁶ 点、增量刷新）——本项目的核心自绘需求 |
| U6 | 复杂控件：表格/树/属性网格/停靠面板/日志视图（**优先用现成组件**） |
| U7 | 模块清单/管理视图（无 UI 模块兜底） |
| U8 | **风格**：Windows 风格、工程化专业、简洁；**仅基础主题色/背景切换**；无花哨动画与复杂视觉 |
| U9 | 主题切换、高 DPI、多显示器 |

---

## 2. 候选对比

| 维度 | **WPF**（.NET 9，Windows-only） | **Avalonia**（.NET 9，跨平台，MIT） | **Web/TS**（+C# 宿主） |
|---|---|---|---|
| 组件生态（U6） | **最强**：开源 MahApps.Metro、MaterialDesignInXAML、**AvalonDock（停靠）**、Extended.Wpf.Toolkit、OxyPlot/LiveCharts；商业 DevExpress/Telerik/Actipro/Syncfusion 全覆盖 | 中等偏上：内置 DataGrid；开源 Dock/Behaviors；商业 **Actipro Avalonia Controls**（已发布）；可用 **XPF**（商业）复用部分 WPF 生态 | **最强**：Ant Design/Element Plus/AG Grid/DevExtreme 等 + 图表库海量 |
| 波形（U5） | ScottPlot 5 / OxyPlot / 自绘（DrawingVisual+WriteableBitmap） | **ScottPlot.Avalonia 已存在**（`AvaPlot`）；或 SkiaSharp 自绘 | uPlot/ECharts/WebGL 自绘 |
| 连线叠加层（U4） | Adorner / Canvas 叠加，成熟 | Canvas / 自绘叠加，成熟 | 绝对定位 / Canvas 叠加，灵活 |
| 自由排列与多窗口（U1） | **最自然**（原生窗口 + 无边框 + 自定义布局） | **自然**（原生窗口，跨平台一致） | 受宿主控制（Tauri 多窗口 API 或伪窗口，体验打折） |
| 开发难度 | **最低**（资料海量、MVVM 成熟、调试一体） | 中（XAML 与 WPF 近似，知识可迁移；资料量次之） | 前端熟练则低；**跨栈集成与调试成本高** |
| 与数据面结合 | **同进程单栈**（零拷贝共享缓冲易） | **同进程单栈**（同上） | 跨进程桥接（需共享内存/环形缓冲设计） |
| 跨平台（未来可能） | **无**（重写 UI 层代价大） | 好（Win/macOS/Linux 同一 UI 代码） | 好（WebView 差异需处理） |
| 许可 | MIT（WPF/.NET 开源） | MIT（Avalonia）+ 商业控件可选购 | MIT/Apache（前端库） |
| 长期维护 | 中：微软维护模式（仍随 .NET 演进，社区活跃、生态沉淀深） | 好：活跃开发、商业化公司支持、持续迭代 | 好（前端生态）+ 中（跨栈） |
| 风险 | **Windows-only + 迁移成本未来可能一次性偿付** | 生态广度与资料量少于 WPF（部分控件需自研/采购） | 双栈复杂度、上下文切换成本、原生感弱 |

**证据**：ScottPlot 官方仓库含 `ScottPlot.Avalonia` 控件实现（[ScottPlot/ScottPlot](https://github.com/ScottPlot/ScottPlot/blob/9fbb2db8/src/ScottPlot5/ScottPlot5%20Controls/ScottPlot.Avalonia/AvaPlot.cs)）；Actipro 发布 Avalonia 控件（[Actipro/Avalonia-Controls v25.2.1](https://github.com/Actipro/Avalonia-Controls/releases/tag/v25.2.1)）；Avalonia XPF 用于"把 WPF 应用带到 macOS/Linux 而无需重写"（[Avalonia XPF](https://avaloniaui.net/xpf)）与第三方兼容性说明（[XPF 兼容性](https://deepwiki.com/AvaloniaUI/avalonia-docs/5.4-xpf-third-party-compatibility-and-version-info)）。

---

## 3. 三种风格的典型实现路径（U4/U5/U8）

- **U5 波形**：三种方案都能自绘；Avalonia 与 WPF 均可先用 ScottPlot 起步、性能不足时下沉到自绘（Skia / DrawingVisual）；Web 可用 uPlot 起步。
- **U4 连线叠加层**：**统一设计**——把"模块区"实现为一个**叠加两层**：底层 = 模块视图层；上层 = 透明叠加层（连线 + 网络标记 + 对齐辅助线）。该设计与"连线不耦合模块 UI"的预期一致，且三种 UI 技术都能实现。
- **U8 风格**：WPF 天然 Windows 风格；Avalonia 需选主题（Fluent/Simple/Material），可用 Windows 风格主题包；Web 需自行选 Windows 风格组件库（如 Fluent UI Web / Ant Design 定制）。

---

## 4. 权衡与推荐

| 若你最看重… | 选择 |
|---|---|
| **最快落地 + 生态最强 + 天然 Windows 风格**，且**接受"未来若跨平台需重写 UI"** | **WPF** |
| **跨平台可能 + 单栈 + 生态够用 + MIT**，愿意为个别控件自研/采购 | **Avalonia**（推荐） |
| **UI 生态压倒一切 + 前端效率**，接受双栈与跨进程桥接 | Web/TS |

**我的判断（基于你的新预期）**：你的 UI 预期是"**不追求视觉创新、优先现成组件、效率优先**"——这**削弱了 Avalonia 相对 WPF 的唯一强项（自定义渲染自由度）**，同时**加大了 WPF 的生态/资料优势**。因此天平由"Avalonia 明显领先"变为 **WPF 与 Avalonia 接近**，关键分水岭只剩一个：**"未来跨平台"是否值得为此付出代价**。

- 若"跨平台"只是"想想而已、很可能永不发生" → **WPF** 更省力、生态更强、更贴合 Windows 专业软件观感；
- 若"跨平台"有一定概率成真（哪怕 30%）→ **Avalonia** 可避免未来 UI 层重写（且 WPF 的 XAML 知识可迁移，学习成本相对低）。
