> ⚠ 历史材料（模块方案评估稿，2026-10-13 归档）。它所描述的代码与文档结构**已被一致化重构取代**：文中的
> `docs/architecture/0X-*.md` 分册、`SampleKitModule`、`ModuleElementPanel.cs`、19 值 `ViewElementKind`、`src/Ui/Controls/`
> 等**均已不存在**，其中的缺口编号也不构成现行登记。现行结论见 `docs/architecture/CODEX.md` 与 `docs/TODO.md`；本文仅作追溯。

# 当前模块方案评估（起草中 · 待两路审计回收后补全）

> 状态：**起草中**。本文件是「暂停开发 → 详细评估」的交付物草稿，裁决权在用户。
> 触发：用户对「DataMonitor 声明 128 vs 96 红项」的答复，把议题从「某个键该不该留」抬到「模块实现方案本身是否正确」。

## 0. 用户原话（本次评估的题面）
> 「我注意到了几个与框架设计目标相冲突的点，以及描述不清的地方。1.『声明面』究竟指什么？是 .json 文件，还是什么地方『还有一个声明』？2. 这个问题本身暴露了框架设计目标的未达成：理想的框架完成后，模块中所有控件的排布 / 位置有 .json 文件唯一决定，而不是某处的『声明』会影响模块本身，C# 只写『有什么，触发后的行为是什么』，.json 决定『在什么地方，怎么排布（位置 / 大小 / 是否自适应 / 只读 / 形态 / 文本 / 容器层级等所有外观和操作感知项）』，所以也有了前面很多次的可实现性评估和方案选择，考虑使用脚本语言等。目前看来，我怀疑当前模块的实现方案本身就是错误的，它自定义了一种标准，但是 bug 频出，声明混乱。暂停开发，我需要对当前模块方案做详细的评估。」

---

## 1. 「声明面」到底指什么 —— 现状里有**五个**叫「声明」的地方

| # | 位置 | 载体 | 谁写 | 内容 | 框架怎么用 |
|---|---|---|---|---|---|
| **A** | **包内 `view.json`** | 模块包里的文件 | **模块作者** | 控件树、容器层级、绑定角色、页签、窗口尺寸、几何与排布 | `ViewDeclarationReader` → `ViewDocument` → `ModuleViewViewModel` 渲染 |
| **B** | **C# 模块类**（`IModule.OnRegistered` → `context.Declaration`） | 编译进程序集的代码 | **模块作者** | 数据类型 / 端口 / 数据角色 / `[ConfigurationField]` 配置项 / 命令处理器 | `ModuleContract` 自检、`ModuleRegistry` 注册、端口建网、配置表单 |
| **C** | **框架的 `view.schema.json`** | 随框架程序集嵌入 | **框架** | 「有哪些 kind、每个键叫什么、缺省值多少、min/max 多少」= 词汇表 + 缺省值 | `ViewSchema` 校验 `view.json`；缺省值由读取器补齐 |
| **D** | **渲染引擎里的硬编码缺省** | `src/Ui/Controls/ModuleElementPanel.cs` 等 | **框架** | 未声明宽度 = 满宽、未声明高度 = 随内容或填满、日志最小高 88、网格自动落位 | 直接决定渲染结果 |
| **E** | **全局主题 + 控件模板** | `src/Ui/Styles/Tokens.axaml` · `Controls.axaml` · `ModuleContentView.axaml` | **框架** | 颜色、字号、字体、边框、圆角、悬停反馈、控件高度 27 | 直接决定观感，模块**无法**覆盖 |

**对用户第 1 问的直接回答**：**两者都有，而且不止两处**。`view.json`（A）只是**其中一处**；C# 里确实「还有一个声明」（B）；**此外框架自己还有 C / D / E 三处在影响最终画面** —— 而这三处**不在模块包里、模块作者改不了**。

**「描述不清」的根因**：现行文档里「声明面」一词在不同段落分别指 A、B、C 三处中的一处或几处，**没有统一口径**。模块作者视角下，一个界面由「我写的 .json」+「我看不见的 schema 缺省」+「我改不了的引擎兜底」+「我改不了的主题」共同决定。

### 1.1 「唯一事实源」现状：**一个观感要查三到五个地方**
`docs/architecture/02-module-sdk.md:183` 现行口径把「唯一事实源」拆成三条：
- ① 树与语义 = 包内 `view.json`（A）
- ② 默认值与约束 = 随框架嵌入的 `view.schema.json`（C）
- ③ 外观 = 全局主题（E）

**评价**：这条「三源分工」作为**框架内部职责划分**没有写错；但它**不是用户要的「唯一事实源」**。用户的诉求是「打开 `.json` 就知道会画成什么样」。现行口径下要回答「这个 `textInput` 会多高」需要同时读四处：`.json`（有没有写 height）→ schema（该 kind 的 `defaultGeometry`）→ `ModuleElementPanel.cs`（引擎里还有一份兜底）→ `Tokens.axaml`（`ControlHeight = 27`）。

**这一条并非新问题**：原始设计草案 `.dsh/drafts/view-declaration-design.md` §1 的标题就叫「三条唯一性」，其中明确写着今日状态是「树在 C#、几何在 `view.json`、**默认值散在三处**」。也就是说 **「唯一事实源未达成」在设计阶段就被识别为待解决问题**，草案给出的解法是 §1 的两条机械护栏（`AR-2x`）：
- `AR-2x 声明面默认值只能来自 schema`：读取器与控件代码里不得出现几何默认值字面量；命中即失败。
- `AR-2x 外观只能来自主题`：`src/Ui`/`src/FrameworkUi`/`src/App` 不得出现颜色与字号字面量。

**⇒ 现状判断**：这两条护栏**没有落地**（或未全覆盖）。证据：
1. `src/Ui/Controls/ModuleElementPanel.cs:35` `LogMinHeight = 88`、`:38` `DefaultSpacing = 2`、`:52` `DefaultLayoutMode = Flow`、`:69` `DefaultOverflow = Scroll`、`:139` `DefaultColumns`、`:858-880` `IsFill`（按元素类型判定，不看声明）—— 这些都是**几何/排布默认值字面量**，与 schema 的 `defaultGeometry`「同一件事的第二处定义」。
2. schema 自己的 `defaultGeometry` 里留着一句未定稿标记：**「容器那一档要与 `ArrangeOverride` 实测对齐后再定稿」** ⇒ 设计者本人也知道 schema 与引擎**尚未收敛为一份**。

---

## 2. 逐个可感知项：现在由谁决定

| 用户列举的项 | 现在由谁决定 | 模块作者能否在 `.json` 里表达 |
|---|---|---|
| **文本** | 模块（`title` / `unit` / 按钮文案 / 页签名） | ✅ 能 |
| **容器层级** | 模块（`children` 嵌套） | ✅ 能 |
| **位置** | 模块（`x`/`y`、grid 的 `column`/`row`、`align`/`verticalAlign`） | ✅ 能（`absolute` 需显式几何） |
| **大小** | 模块（`width`/`height` = `pixels`/`fill`/`adaptive`）+ **schema 缺省** + **引擎兜底** | ⚠️ 能写，但「不写会多大」不在 `.json` 里 |
| **是否自适应** | 模块（`adaptive` / `fill` / `scaleWithWindow`）+ **引擎按类型判定**（`IsFill`） | ⚠️ 同上 |
| **形态** | 部分：`textInput` 的 `multiline`/`inputKind`/`textAlign`、`fileSelector` 的 `fileMode` | ⚠️ 只有这几项；控件类型可选，但**外观形态不可选** |
| **只读** | **无人决定** —— 声明面没有这个键；`TextSurface.IsReadOnly` 是框架控件的样式属性（`src/Ui/Controls/TextSurface.cs:48`），**从未从声明面接线** | ❌ 不能 |
| **可见性 / 条件显示** | **无人决定** —— 无声明键；`IsVisible` 只在框架自己（滚动条、对话框）用 | ❌ 不能 |
| **禁用** | 只有**整窗级**：`ModuleViewViewModel.IsEnabled => StateText == "运行中"`（`:247`） | ❌ 不能按元素 |
| **校验规则 / 占位提示** | **无人决定** —— 无声明键；`Watermark`/`PlaceholderText` 全仓零命中 | ❌ 不能 |
| **颜色 / 字体 / 字号 / 边框 / 圆角 / 悬停反馈** | **全局主题**（E），且设计上**明确拒绝**模块覆盖（`decisions.md` 7.60 用户原话） | ❌ 不能（**设计如此，不是遗漏**） |
| **控件种类** | schema 的 19 项目录（4 容器 + 15 原子），其中 **6 项未渲染**只出占位 | ⚠️ 目录固定，**无自定义 kind 逃生口**（`architecture/02:343-347`） |
| **数据呈现格式**（小数位、时间格式、字节的 hex/ASCII 选择） | **无人决定** —— `numeric` 只有 `unit`；schema §12 记 `format` 为「不做」 | ❌ 不能 |

**⇒ 与用户理想口径的差额是明确的三大块**：
- **① 布局细节的缺省不在 `.json`**（在 schema + 引擎两处）→ 用户说的「不是某处的『声明』会影响模块本身」。
- **② 一批「操作感知项」根本没有声明入口**：只读、可见性、按元素禁用、校验规则、占位提示、数值格式。
- **③ 外观被设计性排除**（颜色/字体/字号/边框/圆角/悬停）→ 用户说「.json 决定……所有外观和操作感知项」，与现行「外观只走全局主题」**正面冲突**。

---

## 3. 现行方案的具体缺陷与证据（外观归属审计已回收，`文件:行号` 齐全）

### 3.1 最严重：**schema 自称「唯一事实源」，但读取器不用它**
`src/Hosting/Packages/schema/view.schema.json:4` 自称「全部默认值与约束的唯一事实源」，`ViewSchema.cs:232,235,552-553` 也确实把 `validation.hard` / `validation.warning` 读进了 `HardRules` / `WarningRules`。
**但读取器 `ViewDeclarationReader.cs` 完全不用这两份规则**，实际判定是它自己硬编码的 `if` 链。⇒ **同一套校验规则有两份定义，其中一份是死的。** 这是「声明混乱」最直接的物证。

### 3.2 **登记了却没有消费者**的声明键（写了不生效，且不报错）
| 键 | 读取器 | 消费者 | 实情 |
|---|---|---|---|
| `commandButton.icon`（`view.schema.json:161`） | `ViewDeclarationReader.cs:646` **对每个节点无条件读** | **全仓零消费者** | schema 里**唯一**的外观类键，读了不用；仓内无任何 `view.json` 用过它 |
| `textLog.wrap`（`:123`） | 是 | 模板无 `TextWrapping` 绑定（`ModuleContentView.axaml:240-244`） | 写了不生效 |
| `textLog.showNewlineMark`（`:124`） | 是 | 模板硬写 `ShowNewlineMarks="True"`（`:243`） | 写了不生效 |
| `textLog.capacity`（`:122`） | 是 | 硬编码 `ViewElementViewModels.cs:728 MaxLines = DisplayBuffer.DefaultCapacity` | 写了不生效 |
| `curve.capacity`（`:108`） | 是 | 硬编码 `ViewElementViewModels.cs:653 MaxPoints = 256` | 写了不生效 |
| `numeric.unit`（`:94`） | 是 | 元素级 unit 无消费；实际单位来自 `sources[].unit`（`ViewElementViewModels.cs:232`） | 写了不生效 |
| `tabs.title`（`:77`） | 是 | `ModuleContentView.axaml:435-452` 不画容器标题 | 写了不生效 |
| `views[].title`（`:225`） | 是 | `ModuleViewViewModel.cs:657,674` 只用 `Root` | 写了不生效 |
| `ViewNode.Auto`（`:51`） | 是（`ViewDocument.cs:143`） | **零消费者** —— grid 自动放置由「未给 column/row」实现（`ModuleElementPanel.cs:384-434`），不看该键 | 写了不生效 |

**⇒ 这一组证明「`.json` 不是事实源」不只是「缺省在别处」，而是「写了的键也可能根本没人读」。**

### 3.3 **`frame.theme` 是陷阱**：预留键被硬伤分支抢先
`ViewDeclarationReader.cs:248-251` 有一条「预留键」警告分支，但 `:241` 的 `RejectUnknownKeys(..., _schema.FrameKeys, "frame 段")` **先执行**，而 `FrameKeys` 只来自 schema 的 `window`/`ports`。⇒ **声明 `frame.theme` 判硬伤、整份声明作废**（`:216-223`），而 schema 自己的 `doc` 把它描述成「预留、本轮不实现」。**预留键在实现上是致命错误。**

### 3.4 **`IsEnabled` 算出来没人用**
`ModuleViewViewModel.cs:247` `IsEnabled => StateText == "运行中"` 在 `src/Ui` 下只有 `:247`（定义）与 `:599`（通知）两处，**无任何视图消费** ⇒ **模块被停用/离线时，声明出来的控件不会变灰，用户从控件本身看不出模块已停用。**

### 3.5 只读 / 可见性 / 禁用 / 校验 / 占位 —— 声明面**整体缺类**
- 只读：`ModuleContentView.axaml:240` `IsReadOnly="True"` **写死**，且只给 `textLog` 用；
- 校验：红框 `IsInvalid` 只由运行时反馈写入（`ModuleViewViewModel.cs:419-500`），**模块不能用 `.json` 声明规则**；
- 占位：`Placeholder|Watermark` 在模块元素模板**零命中**（命中的全是外壳与网络标签重命名框）；
- 可见性：元素模板里所有 `IsVisible` 都是框架算出来的（`ModuleContentView.axaml:257,264,274,349,383,398,409,415,483`）。

### 3.6 元素种类**硬编码在四处**，模块无扩展点
① `src/Sdk/Views/ViewElementKind.cs:12-91`（19 值）② `:101-141`（声明名映射，`tabs` 是唯一例外）③ **schema 是框架程序集的嵌入资源**（`src/Hosting/Packages/ViewSchema.cs:338-348` 后缀匹配且**要求恰好 1 个** ⇒ **模块无法自带 schema**）④ `ModuleViewViewModel.cs:932-976`（工厂）+ `ModuleContentView.axaml:170-576`（每 kind 一个 DataTemplate）。
**⇒ 新增一种控件必须同时改这 4 处并重编框架。**

### 3.7 数据呈现格式在模块 C# 里，不在声明面
数值固定 `ViewElementViewModels.cs:637 DisplayFormat = "0.###"`；十六进制 / 时间格式在 `src/Modules/Business/DataMonitor/MonitorLineFormatter.cs:10,55,62`。⇒ **格式属「模块实现」而非「界面声明」**，与你要的「`.json` 决定呈现」不一致。

### 3.8 两套声明互不交叉校验
`src/Sdk.Testing/ModuleContract.cs:92-96` 的 5 项自检**全在 C# 侧内部**；唯一跨面对账是宿主加载时的 `src/Hosting/Packages/ViewDeclarationReader.cs:71 TryCheckLabels`，**且只有一个方向**（数据面的口必须在 `frame.ports[]` 里有条目，`edge` 必填无缺省）。**反向不查。**

---

## 4. 可选路线与代价对比

| 路线 | 内容 | 代价 | 与现有裁决的关系 |
|---|---|---|---|
| **① 声明面收口进 `.json`** | schema 缺省 + 引擎兜底合并进 `.json` 语义；补只读/可见/禁用/校验/占位/格式；**再开放外观键** | 与「外观只走全局主题、不做按模块覆盖」**正面冲突**，须重新裁决外观归属；`.json` 体量上升 | 改 `architecture/02`、`06`、`07`、`decisions.md` 7.60 |
| **② 维持三源分工，把护栏落地** | 补 `AR-2x` 机械护栏、删引擎缺省、schema 容器档定稿、术语统一 | 工作量小、不动架构裁决 | **不满足「`.json` 唯一决定一切」** |
| **③ 取消声明面，模块自带视图** | C# 里随便写 UI | 推翻 ADR-0026/0028/0019 与顶层 `:727`；框架失去校验/合成/主题统一 | 重大架构反转 |
| **④ 引入脚本语言** | 模块用脚本描述界面 | 顶层 `上位机架构设计.md:869` 现行口径 **v1 不引入脚本宿主**，须**新 ADR 并重估执行权** | 需新 ADR |
| **⑤ 分层自由**（我的倾向） | `.json` 管**排布 + 操作感知**（只读/可见/禁用/校验/占位/格式）；**外观仍走全局主题**，但主题键**可在 `.json` 里按语义引用**（不写死颜色） | 兼顾一致性与可表达性 | 需新裁决（主题引用 vs 覆盖） |

**重要事实**：顶层 `上位机架构设计.md:1054` 把「模块视图排版细节（尺寸族参数/窗口形态）」列为 **C 类原型 Gate（UX）**，时点 = **M1 之后、M2 之前**，**至今未关闭** ⇒ **排版细节本来就是尚未定稿的设计项，不是已定稿的实现**。

---

### 3.9 【契约与扩展面审计】`ModuleContract` 自检**只在测试里跑**，生产路径零调用
`src/Sdk.Testing/ModuleContract.cs:57/114` 的 `Probe` / `CheckAll` 是 `Sdk.Testing` 里的工具。全仓调用点**只在 `tests/`**：`tests/HYWDebugAssistant.Modules.Tests/BusinessModulesContractTests.cs:39,65,81,102,117,143` 与 `tests/HYWDebugAssistant.Tests.Reference/ModuleContractTests.cs:50,68,97,105,114,126,135,146,160,170,179,182,192,201,213,237,250`。
**`src/Hosting`、`src/Core`、`src/App` 无任何生产调用点。**
⇒ C# 侧内部对不上（角色引用了不存在的口、`MeteringBlock.PayloadLengthField` 写错）**加载期不报，只有跑测试才知道**。

### 3.10 【契约与扩展面审计】`module.json` **没有未知键校验**
`ViewDeclarationReader.cs:876` 的 `RejectUnknownKeys` 只管 `view.json`；`module.json` 走 `ModuleManifestReader.TryRead` 的默认 `JsonSerializerOptions`（`src/Hosting/Packages/ModuleManifest.cs:41-49`）**无未知字段校验、无 schema**。
同文件 `:32` 的 `Resources` **全仓无消费点**；manifest 里写 `displayName`/`description`/`tags`/`capabilities`/`participatesInDataFlow`（这些**只有 C# 有**，`src/Hosting/Registry/ModuleDefinition.cs:82-93`）**静默忽略**。
⇒ **manifest 里写错键名，零报错、零效果。**

### 3.11 【契约与扩展面审计】同一件事被两处声明的 18 组，其中 **5 组零对账**
**有对账的（6 组）**
| # | 两处 | 对不上时 |
|---|---|---|
| 1 | `module.json` `id` ↔ `[ModuleDefinition]` | **拒绝加载** `src/Hosting/Packages/ModulePackageLoader.cs:84-85` |
| 2 | `entryAssembly`/`entryType` ↔ 真实类型 | 逐条拒绝（loader `:40-79`） |
| 3 | `frameworkCompatibility` | C# **非空即胜**、manifest 回落（`ModuleDefinition.cs:89`）；不满足 ⇒ Issue（`ModuleInstaller.cs:56-61`） |
| 4 | `dependsOn` | 同上（`ModuleDefinition.cs:88`） |
| 5 | 端口名 ↔ `frame.ports[].name` | JSON 多 ⇒ **警告**（reader `:90`）；数据面多 ⇒ **硬伤 + 空窗口**（`:100-101`） |
| 6 | 形状字段名 ↔ `MeteringBlock.PayloadLengthField` | **仅在测试期自检**（`ModuleContract.cs:256-276`） |

**零对账的（5 组，全部静默）**
| # | 两处 | 对不上时 |
|---|---|---|
| ① | **`stateKey`**：`view.json` ↔ C# 常量（`HexSenderModule.cs:48`、`TextSenderModule.cs:49`、`HexStringConverterModule.cs:43,46`、`SampleKitModule.cs:54`） | 只拿到 `null` → 各自 `?? 0` 回落第 0 页，**零 Issue/警告** |
| ② | **显示源名**：`IViewDisplayAccess` 写错源名 | **完全静默**（`src/Core/Services/ViewStateStore.cs:199-212` 空名/未声明即忽略） |
| ③ | **默认保存文件名**：C# `DataMonitorModule.cs:74 DefaultSaveFileName = "savetext1.txt"` ↔ `view.json:89 suggestedFileName` | **两侧零对账**；且 C# 那个常量**在 C# 内也无消费点** |
| ④ | **显示容量**：C# `SampleKitModule.cs:78 HistoryCapacity = 256` / `:81 LogCapacity = 500` ↔ `view.json` `curve.capacity` / `textLog.capacity` | 零对账；`SampleKitModule.cs:479-485` 注释**自陈**「真实容量写在同一份 view.json 上」 |
| ⑤ | **数据类型标识**：`"RawBytes"@1` 在 `DataMonitorModule.cs:86/89`、`HexSenderModule.cs:75/78`、`TextSenderModule.cs:85/88`、`SampleKitModule.cs:87/90` **逐字重写** | 拼错 ⇒ **无任何加载期报错**，只是默认不兼容（`DataTypeDeclaration.cs:91-92`） |

### 3.12 【契约与扩展面审计】解码器通道**完全未接线**
`src/Core/Views/SubscriptionBuffer.cs:102/105` 的 `Decoder` / `DataType` 是 `init` 属性，**全仓没有任何赋值点** ⇒ `SubscriptionSourceView.Decoder` **恒为 `null`**（`RuntimeServices.cs:880-881` 只是读取）。
⇒ UI 必然回落到 `sources[].valueType` 走 `src/Ui/Runtime/SampleCodec.cs`（`ModuleWindowsViewModel.cs:2218-2226`）。
**⇒ 这解释了 G20 的来源**：`sources[].valueType` 的 enum 只有 `Bool/I32/U32/U64/F64`（`view.schema.json:216-221`），而 `ContractPrimitive` 有 11 项 —— 缺 `I8/U8/I16/U16/I64/F32`。**因为回退路径是唯一路径，「声明值必须等于真实字节宽度」就不是可选项而是硬约束**；4 字节 float 声明成 `F64` ⇒ 读方按 8 字节切片 ⇒ 越界/空白。

### 3.13 模块**没有任何界面扩展 API**
`src/Sdk/` 全部接口：`IModule`（`Lifecycle/IModule.cs:23`）、`IActivitySource`（`:75`）、`IModuleDeclaration`（`Context/IModuleContext.cs:20`）、`IPortAccess`（`:68`）、`IConfigurationAccess`（`:103`）、`IStopSignal`（`:117`）、`IModuleInbox`（`:141`）、`IInputFeedbackReporter`（`:170`）、`IModuleContext`（`:186`）、`IViewStateReader`（`Context/IViewStateReader.cs:36`）、`IViewDisplayAccess`（`Context/IViewDisplayAccess.cs:28`）、`IIssueReporter`/`IModuleLogger`/`IBusinessStateReporter`（`Reporting/ReportingModels.cs:106/113/123`）；特性只有 `ModuleDefinitionAttribute` 与 `ConfigurationFieldAttribute`。
**唯一「模块自带实现被框架回调」的扩展点 = 样本解码器**（`DataTypeDeclaration.cs:16` + `:143`）——**而这个通道没接线**（§3.12）。
⇒ **SDK 里没有任何「注册新界面元素种类 / 新呈现方式 / 自己的外观」的 API。**

### 3.14 配置面也是闭集，且只读属性被静默跳过
`ConfigurationFieldAttribute`（`src/Sdk/Configuration/ConfigurationFieldAttribute.cs:24`）全部成员**只有 7 个**：`Title`/`Group`/`Description`/`Minimum`/`Maximum`/`Step`/`Unit` —— **没有** `Values`/`Options`/`ReadOnly`/`Multiline`/`Password`。
控件种类由**属性类型**判定（`src/Core/Services/ConfigurationSchemaBuilder.cs:181-220`，枚举的显示名 = 成员名、**无别名机制**）；**其它类型 ⇒ 不生成字段**（`:155-159`）；**只读/不可写属性被静默跳过**（`:47-50`）。
⇒ 模块想「配置项可见但不可改」「枚举带中文名」都做不到。

### 3.15 【契约与扩展面审计】`view.json` 各失败面的**用户可感知程度**
| 失败 | 用户能否感知 |
|---|---|
| 硬伤（未知 kind / 未知键 / 类型错…） | ✅ 1 条 `InterfaceError`/`Warning` + **退化为只有框架地板的空窗口**（`ModuleInstanceFactory.cs:120-175`、`FallbackView :102-110`）—— **模块本身照常运行，只是界面空** |
| `frame.ports[].edge` 缺失 / 数据面口缺条目 | ✅ `InterfaceError`/**Error** + 空窗口 |
| schema 缺失/损坏 | ✅ `InterfaceError`/**Error** |
| **显示源名写错** | ❌ **完全静默** |
| **`stateKey` 写错** | ❌ 静默（回落第 0 页） |
| **样本长度与声明的 `valueType` 不符** | ❌ 数值空白，**无 Issue** |
| **目录里没有 `module.json`** | ❌ 只是 **skip 计数、零提示**（`RuntimeServices.cs:146-150`） |
| **manifest 键名写错** | ❌ 静默忽略 |

---

## 4. 现有架构裁决与「可表达性」的落差

| 现有裁决 | 出处 | 与用户理想口径的关系 |
|---|---|---|
| 模块**不提供 View 类型**、不依赖 UI 框架 | `src/Sdk/Views/ViewElementKind.cs:7`、`上位机架构设计.md:727` | 支持「C# 只写行为」；但**也就此关闭了模块自定义外观** |
| **外观只走全局主题**，声明面**没有任何外观键** | `docs/architecture/02-module-sdk.md:181`、`view.schema.json:199` | **与「`.json` 决定所有外观」正面冲突** |
| 视图种类是**闭集**，模块无扩展点 | `ViewElementKind.cs:12-91` + 内嵌 schema + `ModuleViewViewModel.cs:940-975` | **与「`.json` 唯一决定」在「能表达什么」上冲突** |
| 「模块视图排版细节（尺寸族参数/窗口形态）」= **C 类原型 Gate（UX）**，M1 之后、M2 之前，**至今未关闭** | `上位机架构设计.md:1054` | ⇒ **排版细节本来就是尚未定稿的设计项** |
| v1 **不引入脚本宿主**，引入须**新 ADR 并重估执行权** | `上位机架构设计.md:869` | 约束「脚本语言」路线 |
| 阶段 5 现场确认（含四模块窗口下限观感）**未开始** | `docs/roadmap.md` | 本轮暂停落在阶段 5 之前 |

---

## 5. 未决问题与建议


**必须由用户裁决的四件事**
1. **外观归属**：你要的「`.json` 决定外观」与「只走全局主题、不做按模块覆盖」（`decisions.md` 7.60）直接冲突。改不改？改的话走路线 ⑤（主题键引用）还是路线 ①（直接写颜色）？
2. **C# / `.json` 职责边界**：端口名、数据类型标识、配置字段名算「有什么」还是算「声明面」？它们今天在 C# 侧声明，且与 `.json` 有两处重名。
3. **6 个未渲染的控件**（`statusLight`/`table`/`hexView`/`gauge`/`image`/`enumSelector`）：保留登记 + 占位（现状），还是从目录里删掉？
4. **样板模块 SampleKit 的去向**：它的「排布演示」页专门演示 grid / absolute / auto 排布，**其存在前提正是「`.json` 写排布」** ⇒ 若采纳 ① 或 ⑤ 这一页要重写。

**无论走哪条都建议先做的三件小事**（低成本、立刻消除「声明混乱」的体感）
- **把死规则处理掉**：`validation.hard/warning` 要么让读取器真的按它判定，要么从 schema 删掉（§3.1）。
- **给「登记但无消费者」的 9 个键定性**：要么实现，要么从 schema 删掉，要么在 schema 里显式写「预留、暂不生效」（§3.2）。
- **修 `frame.theme` 的抢先硬伤**，让「预留键」真的是预留（§3.3）；顺带把 `IsEnabled` 无消费者的断线接上或删掉（§3.4）。

**建议**：先只裁第 1 条（外观归属），因为它决定后面所有事 —— 若「外观仍走主题」，则路线 ②/⑤ 的差别只在「操作感知项补多少」；若「外观也进 `.json`」，则整个 `architecture/02` 的「三件唯一事实源」要重写。

