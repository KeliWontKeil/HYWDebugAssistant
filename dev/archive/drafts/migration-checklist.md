# 迁移清单（已裁决 · 待执行）

> 裁决：用户 m04188（C-2 = 登记但不禁声明 · C-15 = 网络标签只声明默认位置 · `.json` 分"框架级段 + 控件级段"两段 · 其余 C-1/C-3…C-14 按草案建议）⇒ 本清单可直接执行。

> 配套：`.dsh/drafts/view-declaration-design.md`（设计）· `.dsh/drafts/view.schema.draft.json`（schema）· `.dsh/drafts/view-declaration-examples/*.view.json`（四模块目标声明）· `.dsh/drafts/validate-view-declarations.ps1`（校验器）。
> 口径：用户 Q7 = **一次重构建**（不是分批兼容演进）；Q8 = **完全取代 7.55/7.57/7.58**、不做兼容层。下面这 10 步是**同一次改动内部的施工顺序**，不是可以分别发布的批次。
> 分层验证沿用门禁：改 `src/` 先 T0 编译受影响工程 → T1 受影响测试类 → T2 全量 → T3 `build.ps1`（文档审计 + 包 + 许可 + 测试）。改过 `src/Ui`/`src/Modules` 后**不得**用 `-p:BuildProjectReferences=false`（测的是旧 DLL）。

## 0. 已经落地的半截（③ 批）与本设计的关系
`view.json` 的**几何那一半已经落地**：`src/Sdk/Views/ViewLayout.cs`（`ModuleViewLayout` / `ModuleViewLayoutEntry` / `AxisLayout` / `ViewLayoutMode` / `ViewLayoutOverflow`）· `src/Hosting/Packages/ModuleViewLayoutReader.cs`（532 行，校验口径权威）· `src/Ui/Controls/ModuleElementPanel.cs` 的 `LayoutModeProperty`/`OverflowProperty`/grid/absolute 排布 · `src/Ui/ViewModels/ViewElementViewModels.cs:138-141`（`LayoutMode`/`Overflow`）。
本设计要在这半截之上**再并进另一半**（控件树 + 语义键），然后把"两套声明"塌缩成一套。**端口那一半按用户 m04188 裁决缩成"只声明默认位置"**（见第 5 步与设计方案 §10 的 C-15）。

## 1. schema 落库（唯一事实源）
- 新增 `src/Hosting/Packages/schema/view.schema.json`（随框架资产发布，内容 = 草案 `view.schema.draft.json` 定稿版）。
- 新增只读加载器 `ViewSchema.Load()`（进程内缓存一次；**schema 缺失 = 框架资产坏，报启动 Issue**，不是模块错）。
- 守卫：schema 里每个 kind 都必须能产出"类型 + 约束 + 默认值"三元组；缺一项 = 启动自检失败。

## 2. 读取器切换（`ModuleViewLayoutReader` → `ViewDeclarationReader`）
- 按 schema 校验 → 填默认（`defaultGeometry` 与各键默认值）→ 产出**完整控件树**；错误/警告仍走 `src/Hosting/Instances/ModuleInstanceFactory.cs:90-101` 的 Issue 通道（今天 `ReadLayout` 就在那里）。
- 校验分级 = 草案 §4.4（硬伤整份拒绝 + Issue；警告该条忽略；`rendered:false` 的 kind 与 `frame.ports[]` 里对不上的口名都只报警告）。
- 删掉今天按路径键打补丁的那套：`ModuleViewLayoutEntry` 的路径表、`ModuleViewViewModel` 的 `PathKeys`/`CollectPathKeys`、`ModuleViewViewModel.Rebuild`(≈:509) → `Expand`(≈:586，`v/0` 路径键) → `CreateElement`(≈:731) 三段里的路径匹配。

## 3. 模块侧改声明文件（四个示例即目标内容）
- `.dsh/drafts/view-declaration-examples/{data-monitor,text-sender,hex-sender,hex-string-converter}.view.json` → 覆盖各自包内 `src/Modules/Business/*/view.json`。
- 删掉四个模块 `OnRegistered` 里的建树代码：`DataMonitorModule.cs:120-204` · `TextSenderModule.cs:140-219` · `HexSenderModule.cs:105-194` · `HexStringConverterModule.cs:85-174`（保留数据面注册：`DeclareOutput`/`Inbox` 等）。
- `ModuleContext.DeclareView`（`src/Hosting/Context/ModuleContext.cs:143`，只取首个视图）与 `ViewDeclaration`/`ViewElement` 的"模块输入"角色一并删除；`ViewElement` 若要保留，只能作为**读取器的输出模型**。
- 包内 `view.json` 已由 `src/Modules/Directory.Build.targets` 拷进包 ⇒ 拷贝规则不用改，但**改了包内声明必须重打包**再跑测试。

## 4. 默认值归零（Q2）——严格区分两类
- **声明面默认值**（必须进 schema，代码零字面量）：`ModuleElementPanel.DefaultSpacing = 2`(:38) · 缺省 `layout`/`overflow`（`:52`/`:69`）· 各 kind 的属性默认（§12 表）· `AxisMode.Undeclared` 口径（`:458-462`，按 C-14 改为"省略即取默认"）。
- **框架测量基线**（留代码常量，但**必须登记**进设计方案 §6.2）：`LogMinHeight = 88`(:35) · 文本框最小高 60/88 · 行高 14 · 刷新 50 ms · 分页窗口 20–200 行 · 曲线 256 点 · 日志 20000 行（C-5 若裁定做成声明键，则 20000 进 schema，代码只留兜底）。
- `IsFill` 的**类型判定**（`:857-884`）改为读声明/默认口径：`AxisMode.Fill`(:741) 仍是引擎侧概念，但"谁默认填满"由 schema 的 `defaultGeometry` 决定。
- 同步引用点：`src/Ui/ViewModels/ViewElementViewModels.cs:147` · `src/Ui/ViewModels/ModuleViewViewModel.cs:613`（两处都在读 `ModuleElementPanel.DefaultSpacing`）。

## 5. 网络标签归位（用户 m04188 裁决 C-15）
这是 ③ 批里**唯一与 Q4 直接冲突**的部分，但**不是全删**：裁决是"只声明默认位置"。今天 `view.json` 带 `ports` 段（`src/Modules/Business/DataMonitor/view.json` 的 `in` edge=top；两个发送器的 `out` edge=bottom；转换器无口）。
- 改：`ports[]` 从"几何段"缩成 `[{ name, edge, offset }]`，**移到声明文件的框架级段**（`frame.ports[]`，与 `frame.window` 并列）。
- 删：`ports[].label`（标签文案由数据面口名给）与 `constraints.draggable`（可拖动性是框架固定项）· `ViewLayoutPort` 里除 `edge`/`offset` 之外的字段（`src/Sdk/Views/ViewLayout.cs:573`、`:598`）· `TryValidatePorts` 里与之对应的校验（`ModuleViewLayoutReader.cs:396-431`，改成新口径）· `PortGeometryResolution` 的 `declared` 入参语义（`src/Core/Composition/PortGeometryResolution.cs:31`、`:50`）——它现在只解析"用户拖动覆盖 > 声明值"两层（2026-09 复裁：**无框架惯例**，见 `decisions.md` 7.61）。
- 保留：模块在**数据面**声明"有哪种口"；用户拖动覆盖（`WorkspaceLayout._ports`）不动，且**优先级最高**。
- **~~必须补的框架固定项~~（已作废 · 2026-09 复裁）**：原计划把「模块窗口 = 接收顶 / 发送底、主壳 = 发送右 / 接收左」按窗口类别写成框架固定项；复裁改为**不设任何惯例** —— `frame.ports[].edge` 去 `auto`/去 `default` 改**必填**，数据面登记的口缺声明即硬伤，`PortEdgeResolution` 的回退分支删除（`decisions.md` 7.61）。

## 6. 窗口段留在声明面（§6.1）
`window`（默认 320×220 / 下限 180×160 / 无上限，`TryValidateWindow` `ModuleViewLayoutReader.cs:342-386`）保留，但默认值移进 schema；`src/Ui/Views/ModuleWindow.axaml:18-19` 的写死尺寸与 `ModuleWindowsViewModel.NextPlacement` 按 320×220 估算这两处要改成读声明/单点来源（**今天是同一事实的第二、第三个副本**）。

## 7. 控件目录合一
`ViewElementKind`（19 项）→ 4 容器 + 15 原子（删 `Scroll=17`）；与 `ModuleViewViewModel.CreateElement`(≈:731) 的 switch、`src/Ui/Views/ModuleContentView.axaml` 的 12 个 DataTemplate 逐项对齐；6 个未渲染 kind **允许声明但报警告**（C-2 用户裁决 m04188，不是硬伤）。

## 8. 外观归位（Q3 + C-11/C-12）
- 238 处字面量按 §5.2 的新主题键归零（字号阶梯 12/11/10 · 圆角 2–3 档 · 字重 · 不透明度）；例外显式登记（数据类型自动配色 `PacketTypePalette`、用户配色）。
- `ThemeApplier.Apply` 在组合根启动调用一次 + 监听主题变更（今天只在 `SettingsPanelViewModel.cs:525`/`:898` 调用，`src/App/App.axaml:5` 写死 `Light`）。
- 两处口径冲突随之一并消掉：`src/Ui/Styles/Tokens.axaml:120` 注释 vs `:130` 值；`Tokens.axaml:97` 等宽 `FontData` vs 设置默认 `Times New Roman, 宋体`（后者今天必胜，主题声明形同虚设）。

## 9. 测试改造
- 删：按路径键的守门用例（路径键消失）· `tests/HYWDebugAssistant.Tests/ModuleViewLayoutTests.cs` 里依赖旧 `ports`/路径形状的用例 · `ModuleWindowVisualTests.cs:2406`/`:2453` 的 `RootLayoutMode` 断言按新模型改写。
- 留并改：读取器的**校验口径**用例（硬伤/警告逐条）改到新 schema 上跑。
- 加：`声明顺序 = 渲染顺序`（今天靠 `BusinessModulesContractTests` 的 `数据显示器_声明顺序应等于视觉顺序` 钉死；切换后顺序由声明文件本身就是唯一来源，用例改为断言"渲染顺序 = 文件里的顺序"）· "命令声明 vs 模块注册处理器"的宿主对账（§14 第 5 条：声明了没处理器 ⇒ Issue；有处理器没按钮 ⇒ 警告）。
- 四个模块的包内声明文件加一条 **schema 校验用例**（复用 `.dsh/drafts/validate-view-declarations.ps1` 的口径，落成 C# 用例）。

## 10. 文档回写（裁决后与代码同批落盘）
- `docs/decisions.md`：新增一条（记 Q1C/Q2–Q8 裁决原文 + 本条取代 7.55/7.57/7.58 的哪几条），状态 ✅。
- `docs/architecture/02-module-sdk.md`：声明面正文整体重写（今天描述的是"`ViewElement` + `view.json` 两处"）。
- `docs/architecture/07`（外观基线，删"以主文档为准"那句矛盾）· `08`（设置：默认值来源）· `10`（`data\` 目录）· `docs/implementation/06` §C.1′（键表 7 行随 architecture/02 的覆盖范围同步）。
- `docs/roadmap.md`：第 53/55/56 项收口（布局机制 · 试点网格 · ①②④ 收口），新开一项记本次重构建；§3.1/§3.2 草案表按裁决结果改写。
- 审计 `& .\docs\tools\audit-docs.ps1` 必须 `[OK]`；T3 `.\build.ps1` 全绿。
