# 路线图与状态唯一源（Roadmap）

> 状态：**现行**（2026-09 文档收敛：过程流水已归档，本文件只保留"当前状态 + 未完成项 + 落地缺口"）
> **本文件回答"到哪了 / 还差什么"**：怎么做看 `architecture/00-architecture-index.md` 与 `01`–`10`；为什么这么定看 `decisions.md`；历史（批次过程、逐轮实现台账 v0.1–v1.66）看 `archive/`，**不作为依据**。

**一句话现状**：顶层设计与技术架构设计已冻结（批次 0–10 走完）；**M1 框架最小闭环已实现到 M1-17**——分层与 AR 规则 · SDK 与契约 · Hosting 生命周期 · 数据面与多源网络 · Core/ChangeSet · 持久化 · 应用级设置 · 外壳与拼图式多窗口 · 六个参考模块 · T1–T6 自动化（含无头 UI 与渲染快照）；**当前处于 M1-16「UI 专项」的调整期**（用户多轮实测反馈 → 逐轮交付 → 待复验），**M1-14 手工验收顺延到 M1-16 之后、在最终形态上一次性执行**；M2 及以后未开始。

---

## 1. 推进原则（长期有效的纪律；过程记录已归档）

| # | 纪律 | 出处 / 指针 |
|---|---|---|
| 1 | **垂直切片**：先打通一条最小完整链路（模块包 → 实例 → 网络 → 投递 → 界面显示），不按层横向做完 | `architecture/09-test-and-gate` §10.2 |
| 2 | **设计—实现阈值**：设计阶段只消除 A 类不确定性（"应该是什么"）；仅当剩余未知以 B 类（"怎样实现"）为主才进入实现。**整版原理图未完成不 layout**，不得以"用代码验证设计"为由提前进入实现 | `<仓库根>/.dsh/skills/human-ai-collaborative-design-review`（权威源）· `methodology/top-down-design-with-human-expert-review.md` |
| 3 | **域内四步协议**：图景描述 → 概念与支点 → 机制/结构 → 细节 Gate，前一步未冻结不进下一步；配三条守护规则（层级定位检查 · 转写校验门 · 逆例自检） | 同上 |
| 4 | **逐条裁决、不打包默认**；正式裁决落 `decisions.md`；AC 判据给纸面路径，实证在实现期完成并回写 | 同上 · `decisions.md` |
| 5 | **后一阶段不得推翻前一阶段已冻结结论**；确需推翻必须走新 ADR（Superseded/Deprecated）+ 人工决策 | `decisions.md` |
| 6 | **残余 A 类问题必须回写**：实现中发现"应该是什么"未定 → 停止实现 → Architecture Issue → 人工决策 → 回写主文档/分册 | `implementation/03` §2.1 |
| 7 | **例外必须显式登记且带复查条件**：`// ARCH-EXCEPTION: <原因> · <复查条件>` + 登记 `decisions.md` §3；缺登记视为门禁失败；每个里程碑收尾复查一次 | `decisions.md` §3 |
| 8 | **质量门禁与拒收条件**：7 条通过条件 + 7 条拒收条件（"静默吞异常/静默降级/静默丢数据"即打回） | `implementation/03` §3 |
| 9 | **分层验证**：T0 编译 / T1 单类 / T2 单项目 / T3 完整门禁；报告必须说明"跑了哪一层"，不得把 T0/T1 说成全量通过 | `architecture/09-test-and-gate` §5–§6 |
| 10 | **参数先入设置模型**（不在代码里写魔法数字）· **热路径先想分配** · **一次改动一个主题** | `implementation/03` §2.2 |

---

## 2. 里程碑状态

> 判据来自 `architecture/09-test-and-gate` §10（里程碑交付边界与验收判据）+ `implementation/04-m1-manual-acceptance.md`（手工验收清单，现行版）。**本表只记状态，不记规模与计划性描述。**

| 步骤 | 状态 | 判据 / 说明 |
|---|---|---|
| **M1**（首个里程碑） | **进行中** | 框架最小闭环已跑通；**M1-14 验收顺延至 M1-16 之后**（用户裁决：对即将重做的 UI 先做正式验收无意义，中间以自动化覆盖 + 用户抽查为事实依据） |
| M1-0 前置准备 | ✅ 完成 | 代码工程根确认 · `decisions.md` 建立 · 门禁基线可跑 |
| M1-1 项目骨架 | ✅ 完成 | 9 个框架/应用项目 + 模块项目 + 4 个测试项目；旧骨架拆分并移除 |
| M1-2 架构规则集 | ✅ 完成 | AR-0~AR-16 落地（程序集元数据 + csproj 项目引用检查 + 源码扫描）；故意违规可被拦下 |
| M1-3 `Sdk` 最小面 | ✅ 完成 | 零框架依赖；声明面 / 六钩子 / 契约形状 / 配置 / 视图 / 口声明；面向作者的 XML 注释齐全 |
| M1-4 `Hosting` | ✅ 完成 | 包加载与校验 · 注册表多版本共存 · 实例化 · 生命周期状态机与六钩子 · 缺依赖报 Issue 不静默 |
| M1-5 `DataPlane` | ✅ 完成 | 每实例专属线程 + 每 (实例, 口) 有界队列 + 池化定长缓冲 + 投递原语 + 停止协议与孤儿处置 |
| M1-6 `Core` + `Core.Api` | ✅ 完成 | ChangeSet 两阶段（Prepare 零副作用）· 绑定不可变快照 · 增量重建 · Issue 中心 · 查询/命令公开面 |
| M1-7 应用级设置 | ✅ 完成 | 独立用户级 JSON + `schemaVersion` + 原子写 + 即时生效（键清单见 `architecture/08-application-settings`） |
| M1-8 持久化 | ✅ 完成 | 组合件 round-trip + 版本迁移 + 缺失/损坏/不可解析处置 + 按 `实例ID.口名` 重锚 |
| M1-9 `Ui` 最小 | ✅ 完成（形态已被 M1-15 取代） | 现行形态见 `architecture/06-ui-shell-and-windows`、`07-overlay-and-visual-baseline` |
| M1-10 `FrameworkUi` 最小 | ✅ 完成 | 模块加载入口 / 模块清单 / Issue 面板 / 设置入口（现行形态见 `06`） |
| M1-11 `App` 组合根 | ✅ 完成 | 应用可启动；`App` 是 AR-11 唯一白名单，不承载业务与机制逻辑 |
| M1-12 桩模块 + 测试 | ✅ 完成 | 参考模块**全集 6 个**；AC-1~AC-6 逐条自动化通过；T6 契约套件提供且推荐 |
| M1-13 `Benchmarks` 首轮 | ✅ 完成 | 基准可跑、数值已回写（**只记录、不设阈值**；不进常规门禁） |
| M1-15 界面澄清与修正 | ✅ 完成 · **待验收** | 拼图式多窗口 · 外壳侧边栏 · 建网唯一化（标签同名即连）· 配置 Schema 表单 · 两个新参考模块 |
| M1-17 发现 · 层级 · 设置分类 | ✅ 完成 · **待验收** | 构建即归拢 · 声明根发现与重扫 · 窗口层级统一 · 设置七类 · 快捷键可编辑 |
| M1-16 UI 专项 | 🔄 **进行中（当前步骤）** | 用户出要点 → 草案 → 定稿 → 实现 + 文档回写；已多轮交付、**待用户复验**；是 M1-14 的直接前置 |
| M1-14 验收 + 回写 | ⏳ **未开始（顺延）** | 在 M1-16 之后的**最终形态**上一次性执行；清单 = `implementation/04`（**113 条，其中 39 条仅人工可判**；**不在旧稿上反复改**）；**验收前须跑一次 T3** |
| M2 / M3 / M4 | ⏳ 未开始 | M2 = Gate C UX 原型 + 完整 UI（18 元素 + 4 容器）· M3 = 首批真实业务模块 + 产品验收场景 · M4 = 打磨与稳定性（`09` §10.6） |

---

## 3. 未完成项（后续开发工作清单）

| # | 事项 | 依据 | 阻塞与否 |
|---|---|---|---|
| 1 | **M1-16「UI 专项」收口**（当前步骤）：按用户方案要点打磨视觉体系 / 模块窗口 / 端口标记 / 弹窗信息层级 / 状态反馈 / 克制动效 | `architecture/09-test-and-gate` §10.3 · 用户逐轮反馈 | 不阻塞（正在做） |
| 2 | **M1-14 手工验收**：在 M1-16 之后的最终形态上**一次性执行**；清单 `implementation/04`（现行版：113 条，其中 39 条仅人工可判） | 同上 · 用户裁决（顺延） | **阻塞 M1 收尾** |
| 3 | **验收前跑一次 T3 完整门禁**（`build.ps1`，**需先关闭应用**） | `implementation/04` 前置 · `09` §5.3 触发条件① | 阻塞（M1-14 前置） |
| 4 | **`WaitForData` 主动接收原语**（让主动接收也微秒级且不占核；SDK 兼容扩展项，**不是 v1 交付**） | `architecture/02-module-sdk` · `04-runtime-mechanisms`（R 项一览） | 不阻塞 M1（后续里程碑） |
| 5 | **池化包装对象**：投递路径当前 **40 B/单元** → 目标 0（L3 内部空闲链复用 wrapper，不动 SDK 面） | `architecture/04-runtime-mechanisms`（实测偏差项）· `03-data-protocol-and-typing` | 不阻塞 M1（**列为后续里程碑首项**；在此之前文档承诺以实测 40 B 为准） |
| 6 | **叠加层虚线穿过角色信息文字是否加极淡底衬**（用户观察项，一句话即可裁定） | 旧 roadmap 登记项 · `architecture/07-overlay-and-visual-baseline` 叠加层节 | 不阻塞（纯观感项） |
| 7 | **M2 范围**：Gate C UX 原型（M1 后、M2 前做一次）→ 完整渲染（元素目录全集）· 三层信息类层 · 设置界面 · 诊断面板；**M2 后跑第二轮数值实测**（含 UI 节拍下丢弃率、GC、内存） | `architecture/09-test-and-gate` §9（Gate C）· §10.6 | 不阻塞（M1 收尾后启动） |
| 8 | **Gate D 预留项尚未实现**：**会话快照**（崩溃/退出恢复现场）· **模块版本迁移路径**（旧版本实例 → 新 Definition） | `architecture/09-test-and-gate` §9（Gate D） | 不阻塞（接口位已预留） |
| 9 | **首批正式模块**：用户 2026-09 已给出**三批清单（15 个）** —— 清单唯一落点 = `modules/README.md` §2.1；**顶层开发路线** = `implementation/06` §H.5（框架缺口 → 第一批 4 个基础模块 → 归档旧 6 个 → 纯框架修复 7 项 → 验收）；**顺序与拆分待细化** | 用户直接给出 · `implementation/06` §H.5/§J.0 | **进行中**（待细化后开工） |
| 10 | **波形 / FFT 相关依赖未引入**：ScottPlot 与 FftSharp（或 MathNet.Numerics）在 `Directory.Packages.props` **尚无条目** | `architecture/10-delivery-and-ops` §8 · ADR-0024 | 不阻塞（波形能力未开工） |
| 11 | **模块标准与开发流程**（`implementation/06`）**已生效**：**B1 三层判据 · α 两侧同型 · B2/B3 层①命名与多角色 · B4 `Capabilities` 检查项 · B5 角色归属建议 · C3 测试落点 · D 大小数值 · E 外部 IO 强制写法 · F 快照并发** **均已裁并落地**；仍【待评估】= 模块族分类维度（B2′）· 空模板形态（F1）——**评估时点 = 第一批基础模块完成后** | `implementation/06` §I | 不阻塞第一批开发（**标准已可照做**） |
| 12 | **框架缺口修复（顶层路线第 ① 步）—— 用户 2026-09 裁定「框架全做完再出模块页」**，按序执行五项：**G1** `TabPages` 渲染 + 页签交互（**唯一硬阻塞**）—— ✅ **完成**（代码：`TabStripElementViewModel` + `ModuleViewViewModel.Expand` 取代 `Flatten` 的"只取首页" + `ModuleContentView.axaml` 模板与选中态；**三层回归网齐**：VM 用例 4 条 + **真实窗口无头 2 条**（含**真指针点击**切页）+ **真出像素快照 1 条**（切页两帧必须不同；PNG 在 `%TEMP%\hyw-tab-snapshot\`，**已人工看图**）；T0 `Ui` **0 警告**；全项目串行回归 **375 通过 / 0 失败**；文档回写 `02`（渲染子集 + C2 声明结构）/`06` §7（交互）/`07`（视觉取值））· **G2** `TextInput` 加 `Multiline` —— ✅ **完成**（`ViewElement.Multiline`（`init` 只增）+ 模板 `AcceptsReturn`/最小高度 + **提交键做成应用级设置 `multilineSubmitKey`**（用户裁定"换行极其常见、习惯不一"）：`CtrlEnter` 默认 / `Enter` / `None`，**即时生效**（每刷新节拍下发并重建输入元素）；**单行行为不变**；回归网：VM 用例 **8 条** + 真实窗口 **2 条**（`AcceptsReturn` / 最小高度 / 走 `UpdateSettings` 真实路径改档位）；文档回写 `08` §2（键清单 20→**21**）/`02`/`06`/`decisions` 7.15。**「不自动折行」登记为特性而非限制**（用户 2026-09 裁决：画面换行 ⟺ 文本里真有换行符 ⇒ **所见即所发**、发送内容完整、用户不必猜"这是折行还是换行符"；长行横向滚动；**框架不规范化文本**；行尾 `CR/LF/CRLF` 由模块定））· **G3** 输入元素"校验失败"态（红框）—— ✅ **完成**（SDK 只增 `IInputFeedbackReporter` + `IModuleContext.InputFeedback`（模块回报"某次输入的处理结果"）；`InputFeedbackStore`（Hosting）按 (实例, 源名) 存；`Core.Api` 只增 `GetInputFeedback` + `InputFeedbackView`；`ModuleRuntime` 在**投递入队时清旧结果** ⇒ **"下一次提交即刷新"**；`Ui` 侧 `TextInputElementViewModel.IsInvalid` → `Classes.invalid` → `TextBox.invalid`（**`SemanticError` + 线宽 2**）。**回归网**：2 条**整链集成**用例（模块活动体回报 → 查询面 → VM → **渲染真的变红**：断言边框 2px + 颜色显著偏红；以及"再提交合法值 ⇒ 红框消失"）。**T3 豁免**见 `decisions.md` 7.17；**取值范围**：只做输入，命令侧留待需要时再只增） · **G4** B6 文件选择渲染 —— ✅ **完成**（SDK 只增 `FilePickerMode` + `ViewElement.FileMode`/`FileFilter`（`init`）；`FileSelectorElementViewModel` + 模板（标签 + **只读**路径框 + 「浏览…」+ 就地结果）；**弹窗在视图层**（`OnBrowseClick` 用 `TopLevel.StorageProvider`；`async void` 事件处理 + 异常就地提示不逸出）；**路径走既有输入通道**（`SubmitInput` → 模块 `TryDequeueInput`）⇒ **模块不碰文件对话框 API**。**回归网**：渲染用例（2 个选择器 → 标签/只读框/浏览按钮**都真的画出来**）+ 提交用例（框架接受路径；未声明输入被拒）。**顺带修掉一个真缺陷**：`ModuleRuntime.IsInputElement` **原先漏了 `FileSelector`** ⇒ 声明 B6 的模块收路径会被判"**模块未声明该输入**"而拒绝（由本用例抓出）。**只能人工验**：点「浏览」真的弹系统对话框。文档回写 `02`（渲染子集 + B6 声明语义）/`06`（输入通道 + 渲染子集）） · **G5** **C5-7 端口锚点一次做完**：`PortDeclaration.Edge` + `AnchorDraggable`（默认 **false**）+ **可拖到任意边**（⇒ 组合件**新增"边"字段** + 走 ADR-0017 迁移）+ **全局**「固定/解锁端口锚点」快捷键 + `Core.Api` 锚点变更操作 ⇒ **必须跑 T3 + 重编全部模块** —— ✅ **完成**（**SDK 只增**：`PortDeclaration.Edge`（`Auto` 默认 = 惯例）+ `AnchorDraggable`（默认 `false` = 固定）；**组合件新增 `edge` 字段**（`Auto` 缺省不写）与 `locked` **三态**（`null` = 用户没设过 ⇒ 按模块声明，见 `decisions.md` 7.19）；`Core` 侧 `ResolveEdge`（用户覆盖 > 声明 > 惯例）唯一解析；**拖动**：标记按下 → 越过 **4px 阈值**才启动（阈值内留给**双击改网名**，有真双击用例钉住）→ **最近边 + 沿边偏移**就地预览 → 松手经 `UpdatePortGeometry` **一次写全**（`SetPort` 是整条替换 ⇒ 少写字段会抹掉标签/边/固定态）、失败**就地回滚**；拖动期间 `IsDraggingAnchor` **冻结节拍同步**（否则预览被框架权威值拽回，有专门用例）；**入口**：标记**带内、标记长度之外**（悬停该标记时淡入）的**图标按钮**（点 = 固定/解锁该口）+ **全局 `Ctrl+P`**（批量）；**用户两次否掉视觉实现**（汉字「固」徽标槽位 + 悬停放大标记 ⇒ "太破坏观感""过于复杂"；二版落"标记内侧" ⇒ "实际落在了内侧遮挡 UI，**而不是边框留白处**"——实测预留带与内容区内边距**是同一块地方**）后按裁定重做，见 `decisions.md` 7.18。**落地中抓出并修掉两个真缺陷**（`decisions.md` 7.20）：同边拖动的抓取偏移算错（⇒ **恒等于没拖**）+ 面板没订阅模型（⇒ **改偏移屏幕不重排**）——两条都补了"去掉修复即红"的回归用例。**T3 已跑**（`build.ps1` 五步全绿：文档审计 ✓ · 构建 **0 警告 0 错误**（**6 个参考模块一并重编** ⇒ 二进制兼容已验证）· 模块包 **6/6** ✓ · 许可审计 ✓ · 测试 **400 通过 / 0 失败**（Tests 341 · Reference 43 · 架构 10 · 快照 6 · Modules.Tests 0））。**回归网**：VM/持久化 **2 条**（三态按声明解析、`locked:false` 也落盘、`locked` 缺省不写）+ **真指针** 6 条（拖动换边并落盘、默认固定拖不动且给解锁提示、纯点击/阈值内抖动不提交、**真双击仍打开网名编辑**、拖动期间刷新不拽回、全局固定/解锁）+ **真出像素快照 1 条**（悬停淡入的按钮**真的出现**（隐藏帧 vs 淡入帧像素差 > 40）、**真指针点击**命中并写进运行时、两态字形互斥且切换、光标 `SizeAll`→`Arrow`、按钮**不与标记/指示灯重叠**且在窗口内）；快捷键目录用例 +1（默认键唯一、术语用「固定」不用「锁定」） | 用户 2026-09 裁定 · `implementation/06` §H.1/§H.5 · `roadmap.md` §4 **G16** · `07` 端口标记表 | **✅ 完成（G1 ✅ · G2 ✅ · G3 ✅ · G4 ✅ · G5 ✅ —— 框架缺口已清零，下一步 = 4 份模块页）** |
| 13 | ~~模块自检的"口 → 角色归属完整性"未落地~~ → **已收口**（`implementation/06` §I 的 **B5** 已裁 = a 且已实施：两条规则进 `ModuleContract` 的 `Advisories`、运行期不报；`IModuleContext` / `RoleDeclaration` / `Core.Api` 三处"运行期会提示"的旧承诺已校正） | `architecture/02`「口 → 角色归属的完整性（B5）」 | — |
| 14 | **`MultiSourceChainTests.多源链路_两路输入应按策略融合并转发给下游` 偶发失败**：断言 `fuserRole.RateBytesPerSecond > 0` 偶得 `0`（**代码未变、同一命令重复跑即复现/不复现**；实测 6 次里 3 次红）。根因指向已知口径 —— `RateMeter.MinIntervalSeconds = 0.1`（1.0.29 为修"一次刷新多消费者除第一个外全读 0"而加）⇒ **距上次读取不足 100 ms 的读取返回 0**，而该测试在活动结束后**立刻单次读**。**不是本次改动引入**（改前 41/41 通过是"并发跑"的时序，改后走串行 ⇒ 时序变了）。**用户 2026-09 裁定：先不修，只留登记**（按测试环境噪声处理） | `tests/HYWDebugAssistant.Tests.Reference/MultiSourceChainTests.cs` L115–121 · 主文档 1.0.29 · `Core/Services/RateMeter.cs` | **不阻塞**（已登记为**已知偶发**）。**红时的正确判断**：只单跑该项目一次 —— `.\tools\dev.ps1 -Mode Test -Filter 多源链路` **第二次通过即为本条**，不是回归；若**连续两次都红**才按真实缺陷处理。**将来若要根治**：① 测试改为轮询等速率（保留"必须 > 0"的原意）② `RateMeter` 最小间隔改可配/可重置（需回写主文档 1.0.29 口径 + `decisions.md`） |
| 15 | **输入元素「处理结果」反馈通道**（**契约扩展请求 · 已裁 = 新增通道（只增）**，见 `decisions.md` 7.16）：模块可把"某次输入的处理结果（成功/失败 + 原因）"回报到**对应输入元素**，框架渲染**失败态（红框）**；**失败态在"下一次提交"时刷新**（框架在投递入队时清旧结果 ⇒ 无回报 = 无失败态、零回归）。**取证（为何现有面做不到）**：`SubmitInput` 的 `CommandResult` 只反映投递是否入队（`src/Core/Runtime/ModuleRuntime.cs` `case SubmitInputOperation` 只校验实例存在 + 该输入已声明，然后 `plan.InputDeliveries.Add(...)`）；模块无"把结果落到某元素"的通道（Issue 进 Issue 中心、业务状态进状态徽标，都不按元素定位）；模块**不持 UI** ⇒ 满足判据"本质只能由框架做"。**影响** = #4 转换器（转换失败标红）· #2 HEX 发送器（非法 HEX）· 后续逐输入校验类模块。**范围：只做输入**（命令侧留待需要时再只增）。**T3 豁免**见 `decisions.md` 7.17（**重编全部模块照常发生**，只豁免额外那次完整门禁） | `implementation/05` §2 · `src/Core/Runtime/ModuleRuntime.cs` · `src/Core/Services/RuntimeServices.cs` | **G3 的实现依据**（已裁，无阻塞） |
| 16 | **快照项目在 `-AllTests` 负载下曾偶发一条红（未复现）**：`VisualSnapshots.OverlaySnapshotTests.叠加层_应在真实渲染栈下画出方块与连线_并留下快照` 在 G2 收尾的那次全量跑中红过一次；此后**单独跑 6 次 + 全量 `-AllTests` 2 次全绿**，**失败断言原文未捕获** | `tests/HYWDebugAssistant.VisualSnapshots/OverlaySnapshotTests.cs` · `TabStripSnapshotTests.cs`（G1 新增，使同进程**第三个**各自 `StartNew` 无头会话的快照类） | **不阻塞 · 归因未定**（按纪律**只报证据支持的结论**：该用例不碰标签页/多行输入的新代码路径；疑似"多无头会话 + 负载时序"，但**未证实**）。**暂按与第 14 项同一口径处理**（先不修、只留登记）：**红时单跑一次，通过即非回归**；若再复现**且捕获到断言原文**，再定是否要给"多会话串行"加保护 |

**已收口、不再作为未完成项（登记备查，勿重复开单）**：
① **B2/B5 命令与输入通道** → 已由收件箱拉取式（`IModuleContext.Inbox`）落地，推送式 `IModule.OnCommand`/`OnInput` 作废；
② **ADR-0050 的 R6（UI 投影）** → 已由 P3/P4（数据角色模型 → 声明式角色投影，ADR-0048/0049/0052/0053）落地；
③ **异常隔离政策 Z**（投递回调异常是否改为丢弃继续）→ 维持现状（报 Issue + 停止该实例活动）；
④ **UDP 语义规范化与结构级 Q1–Q6**（含队列粒度、薄旁路头）→ ADR-0050；**数据协议 Q1–Q14** → ADR-0046；**吸附 D-1~D-5** → ADR-0042；
⑤ **`Disabled` 是否保留线程** → 裁定"**保留线程与资源**"（仅 `Removed` 回收）；
⑥ **文档编辑 / 归档工作流** → 2026-09 已落地为 **`DOC-WORKFLOW.md`**（唯一对照表 + 归档规程 + 审计挂进 `build.ps1` 第 1/5 步），`README` §4 / `architecture/00` §4 / `implementation/03` §4 的三处重复规则**已收敛为一处**。

**明确不做（v1）**：跨窗口长连线 · 模块热重载 · 任务栏相关设置项 · UI 自动化（T8）· 进程级沙箱 · 崩溃转储 · 安装包/自动更新 · 首次运行引导 · i18n 与无障碍（详见主文档 §18.4 与 `architecture/10-delivery-and-ops` §7）。

---

## 4. 落地缺口（设计这么定，代码还没做到 / 做反了）

> 口径：**"应有处置"≠ 已裁决** —— 本轮不拍板设计，只如实登记，供后续 bug 修复轮取用；分册在相关位置只留指针。

| # | 缺口 | 现状（以代码为准） | 应有处置 | 来源 |
|---|---|---|---|---|
| G1 | `appTopmost` **属性存在但无读写方** | `ApplicationSettings.AppTopmost`（默认 `false`）存在，界面可切换且即时生效；`SettingsService` 的 JSON 映射**既不读也不写** ⇒ 不落盘、重启回 `false` | **移除该属性，或改为运行期状态**（纪律：运行期状态切换不进设置项） | `architecture/08-application-settings` §6 G1 |
| G2 | **日志能力未落地** | 代码只有 `logLevel`；**无"日志文件位置"设置键**，全仓无日志依赖、无文件 sink、无 `logs\` 目录 | 键清单**不含**该条；日志能力（sink + 文件位置 + 滚动 + 未处理异常兜底）按 `10` §4.2 在 M1-10 / M2 落地 | `08` §6 G2 · `10` §4.3/§8 |
| G3 | `diagnosticHintThreshold` 与 `logLevel` **键真实存在但无消费者** | 两项的定义 / 校验 / 读写 / 界面齐全，`src/` 内**检索不到消费者** ⇒ 声明的"即时生效"未实现 | **保留键**并标注"当前无消费者"；接入消费者后回写状态 | `08` §6 G3 |
| G4 | **设置变更未走 ChangeSet 同一入口** | `RuntimeServices.UpdateSettings` **直连** `SettingsService.Update`，不经 `ModuleRuntime.Apply(ChangeSet…)` ⇒ 与"**状态修改只有一条串行路径**"的约束**不符** | 接入统一入口，或由 ADR 明确"设置走独立入口"及其并发保证（**本轮不拍板**） | `08` §6 G4 |
| G5 | 设置文件 `schemaVersion` **无迁移分支 / 无版本门禁**，且**映射为手工 `JsonObject`/`JsonNode`**（非源生成） | 只有 `schemaVersion` 的读入与写回（`CurrentSchemaVersion = 1`）；对比组合件侧已有迁移逻辑 | 迁移 / 门禁 / 源生成是否补做**待定**（决定后续新增设置项是否要动代码结构） | `08` §6 G5 |
| G6 | **`CarrierHost.cs` 类注释与现行行为不符** | 注释写"`Disabled` 的实例不占线程"，实际行为 = **禁用保留线程与资源**（只 `Removed` 回收） | **改注释**（以代码行为为准）；`04a`/主文档的"按活跃实例计"同句一并作废 | `architecture/04-runtime-mechanisms`（落地缺口节）· `05` §7 |
| G7 | SDK `PortDeclaration.ParticipatesInAutoNetwork` **存在但框架从不读它** | 构造参数 + 属性（默认 `true`）都在；**仅 1 处测试断言默认值**，无行为消费者。其注释引用的"**R0–R6 快速建网**"是**失效引用**（该机制已由 ADR-0037 整体作废） | **实现（按标志过滤建网）或删除（从 SDK 移除该参数，走 `init` 属性兼容路径）待定**；失效引用注释须清 | `architecture/02-module-sdk` 口声明节 |
| G8 | **交付与运维整体未落地**：便携包打包 · 两种发布形态 · 日志文件 · 未处理异常兜底 · `THIRD_PARTY_NOTICES.md` 生成 | 目录布局与 zip 打包未做（导出脚本已有）；工程无 RID / self-contained 配置；无日志 sink；`Program.cs`/`App.axaml.cs` 无 `UnhandledException`/`UnobservedTaskException` 处理；审计脚本只打印控制台、仓库无 NOTICE 文件 | 打包与日志按 `10` §1/§4.2 在 M1-10 / M2 落地；**两种发布形态在发布阶段处理**；NOTICE 由许可审计同一数据源导出 | `architecture/10-delivery-and-ops` §8 |
| G9 | **波形 / FFT 依赖无条目** | ADR-0024 定了 ScottPlot 与 FftSharp（或 MathNet.Numerics），CPM 中**尚无对应包** | 引入波形能力时一并加入 CPM 并过许可门禁 | `10` §8 · ADR-0024 |
| G10 | **"命令合并"在实现侧尚未落地** | `ModuleContext.BoundedInbox`：容量 64、满则丢最旧并计数、`EnqueueCommand` **不做去重** ⇒ 现行为"重复点击 = 重复入队" | **实现待补齐**（设计语义"同命令已在待执行队列则不追加"不改写） | `architecture/02-module-sdk` 代码核对补充 |
| G11 | **`EnumSelector`（B4）未接渲染分支** | 代码元素工厂无该分支，落入"未渲染"占位 ⇒ 与 ADR-0040 D4 把"枚举/下拉选择"计入渲染子集的写法**不一致** | **以代码为准：v1 实际渲染子集不含 B4**；补渲染或改 ADR 措辞二选一 | `architecture/02-module-sdk` · `06-ui-shell-and-windows` §7 |
| G12 | **`ShellLayout.cs` 的尺寸常量无引用且与外框不一致** | 声明 `MinWidth=360` / `MinHeight=480`，**全仓无任何引用**；有效最小值只来自 `MainWindow.axaml` 的 **360×360** | 两处常量**二选一收口**，避免后续漂移 | `architecture/06-ui-shell-and-windows` §2 附带发现 |
| G13 | **`CarrierOptions.IdleWait = 1 ms` 缺分册条目** | 主文档已补齐该承载参数（投递循环空闲等待粒度，代码取证 `CarrierOptions.cs`），但 `04-runtime-mechanisms` 尚无该参数条目 | 分册补一行（**文档补齐类，非代码缺陷**） | 主文档回写记录 · 代码 `DataPlane/Carriers/CarrierOptions.cs` |
| G14 | **组合件的保存 / 打开入口在界面上未接线** | `MainWindow.axaml.cs` 有 `OnOpenComposite` / `OnSaveComposite` 处理函数，但**没有任何控件绑定它们**（顶部菜单已撤）⇒ 用户**无法保存/打开组合件**；`implementation/04` 的 #56/#57/#71/#72 因此手工不可执行（清单内已标注） | **补界面入口**（活动栏或侧边栏）；自动化侧 `CompositePersistenceTests` 不受影响 | `implementation/04` 遗留问题 · 代码 `FrameworkUi/Views/MainWindow.axaml(.cs)` |
| G15 | **代码内帮助文本与现行 UI 不符** | 设置页「关于/帮助」文本仍写"右下角按钮：**停**（运行/停用）…"，而现行模块窗口是**右上角四钮**（还原/锁定/隐藏/关闭）且**无"停"**（ADR-0041 D1 已删） | **改帮助文案**（属 UI 文案遗留，随下一轮 UI 微调一并处理） | 代码 `FrameworkUi/Views/SettingsView.axaml` 帮助文本 · `06-ui-shell-and-windows` §5 |

**已核对通过、不列入缺口**：组合件网络记录的可选字段 **`allowUnverifiedTypeMatch`**（仅在该值为 `true` 时写出、缺省 `false`）已在 `03-data-protocol-and-typing` 与 `05-network-semantics` 写明，无需补；`CarrierOptions` 的队列容量 **1024**（按 (实例, 口) 计）与"丢最旧"默认值已在分册与主文档一致。

| G16 | **✅ 已闭合（G5 · 2026-09）** —— 原登记"C5-7 端口锚点：多数子项已实现，缺'口的默认边'与'拖动 / 锁定入口'"：三处缺口**已全部补齐**（① **口的默认边**：`PortDeclaration.Edge`，`Auto` = 惯例；② **拖动**：标记拖到任意边 + 沿边改偏移，松手经 `UpdatePortGeometry` 落盘；③ **入口**：标记内侧悬停淡入的图标按钮（单口）+ `Ctrl+P`（全局））。**原登记内容保留在下方作为沿革**：✅ **已实现** = 沿边偏移的数据链（`Core.Api.PortView.AnchorOffset` → `PortGeometryView.Offset` → `ModuleViewViewModel` L428/L434 消费）· **随组合件往返保存**（`CompositeSerializer`；`CompositePersistenceTests` L61/L590/L600 断言 0.25 / 0.75）· 锚点锁定标志（`PortView.IsAnchorLocked` / `PortGeometryView.IsLocked`，含序列化）· **建网判定双轴泛化**（`DockRule.TryGetContact` 左右 + 上下、`MarkerCenter` 按接触面取轴）· **吸附轴按边取**（`PortAnchorPair.AlignsX = !边竖直性`）· **横排标记渲染**（`MarkerWidth/Height => IsVertical ? 20 : 114`）。❌ **缺** = ① 口的**默认边**（`PortMarkerViewModel.Side => IsSend ? Right : Left` **写死**；模块无法声明，也没有 Top/Bottom）② **拖动锚点**（全仓 `AnchorOffset` **没有任何写入方**，只有测试在写 ⇒ 用户改不了）③ **锁定 / 解锁的操作入口**（C5-7 原文要求"可通过**快捷键/操作**锁定与解锁"） | 证据见左列各文件：`src/Ui/ViewModels/PortMarkerViewModel.cs` L202 · `src/Ui/ViewModels/ModuleViewViewModel.cs` L428/434 · `src/Core.Api/RuntimeViews.cs` L11–27/L233–241 · `src/Core/Composition/DockRule.cs` · `src/Core/Services/CompositeSerializer.cs` L307/613 | **按 C5-7 补齐**（`ADR-0031 §6③` + 旧分册 `05 §C5-7` + `07` 端口标记表"锚点是否可拖动是模块属性"）；旧分册 `13` 的"本批只泛化布局规则、暂不开放上下边端口"是**有意的暂缓**，**不是**废止 C5-7。⚠ **另登记两处（用户 2026-09 已判）**：① **语义冲突**：`07` L83 写"**固定**锚点在模块拉伸时**按比例重排**"，而 `RuntimeViews.cs` L233 的 `IsLocked` 注释写"锁定后**不随**模块拉伸重排，也不可拖动" —— 用户裁定"**锁定时即不允许移动也不允许拉伸**"（**已核实实现正确**：`ModuleWindowsViewModel` 的 `BeginDrag` L410 与 `BeginResize` L867 **都**要求 `Is Locked: false`）⇒ **模块锁定后不会被拉伸，锚点重排问题不会发生**；锚点那个标志只应表达**不可拖动**（= C5-7 的「固定」）⇒ **改 `RuntimeViews.cs` 注释**（代码行为无需改）。② **术语撞词**：「锁定」现同时指**模块窗口锁定**（ADR-0041 按钮簇）与**锚点固定**（C5-7）⇒ **锚点侧换词为「固定 / `AnchorDraggable`」**，`07` 措辞一并收敛 | 用户 2026-09 指出 · `ADR-0031` §6③ · `07` 端口标记表 · `implementation/06` §H.1 |

---

## 5. 模块开发状态（状态唯一源的一部分）

> 规定见 `implementation/05-two-track-development-workflow.md` §4：**模块页与本节必须一致**。
> **清单的唯一落点 = `modules/README.md` §2.1**（15 个正式模块的标识与一句话职责在那里）；**本节只做状态汇总**，不重列清单。
> 参考模块（判据载体）不在此列 —— 其处置（**验收后归档**）见 `implementation/06` §G 与 `modules/README.md` §2.2。

| 批次 | 数量 | 状态 | 说明 |
|---|---|---|---|
| **第一批**（基础 · 纯文本处理） | 4 | **未开始** | 顶层路线的第 ② 步；**待**：框架缺口修复（`implementation/06` §H.1）与标准裁决 |
| **第二批**（外部接口：串口 / TCP / UDP） | 3 | 未开始 | 串口会触发新依赖（许可门禁 + ADR） |
| **第三批**（高级功能：预设发送 / 波形 / 协议解析 / 示波器 / 仪表盘 / 姿态 / 分流） | 7 | 未开始 | 多数依赖框架缺口（波形依赖、A7/A8 未渲染） |
| **第四批**（工具 / 美化：图片显示器） | 1 | 未开始 | 依赖 B6 文件选择渲染 |
| **已交付** | 0 | — | — |

**当前有效状态**：**第一批正式模块尚未开工**；开工前须先定 `implementation/06` 的【待裁】项与框架缺口修复范围（`implementation/06` §H.5）。

## 6. 门禁节奏与当前生效例外

| 项 | 内容 |
|---|---|
| **当前节奏（用户裁决）** | 在**"最小验收指标"就绪**（① 所有基础功能可用 ② 最小测试模块就绪 ③ 跑过一次 T3）之前，**UI / 交互 / 模块的改动一律只跑 T0 编译 + 受影响的 T1/T2**，**不跑完整门禁 T3**；理由 = 后续还有大量 UI 与模块调整，此时跑 T3 没有意义 |
| **恢复条件（写死）** | ① **最小验收指标就绪时**恢复执行完整门禁；② **M1-14 手工验收前必须跑一次 T3**（`build.ps1`，需先关闭应用）；③ 此后 `09` §5.3 的三种触发情况照常生效——交用户手工验证前 · 改动 `Sdk` 公开面/`Core.Api` 契约/文件格式/AR 规则/依赖 · 某个 M1-xx 宣布完成时 |
| **当前事实记录** | 最近一次**完整门禁**全绿 = 构建 0 警告 · 模块包 6/6 · 许可审计 75 包 · **测试 366 通过 / 0 失败**；此后各轮 UI 调整只跑 T1 类（最近一次**分层回归** 358 全绿）。**两者是不同时点的快照，当前实数一律以 `build.ps1` 实跑输出为准** |
| **例外登记** | `decisions.md` §3（`ARCH-EXCEPTION` 唯一登记处，追加式：约束 / 原因 / 复查条件 / 处置人）；**当前无生效例外** |
| **纯 UI 改动口径** | XAML / 样式 / 文案 / 间距 → **T0 编译 + 起应用用眼睛看**即可，不跑 T2/T3；只有**连带改了逻辑**（视图模型 / 命令 / 数据结构）时，才按 T1 跑**受影响的那个测试类**（`09` §6） |
