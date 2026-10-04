# 模块界面声明面 —— 顶层设计草案（待裁）

状态：草案 v1.1 · **未经裁决，尚未落地任何正文**（v1.1 增 §5.1/§5.2 取证、§14 覆盖验证、机器可读 schema 与四模块声明例）
依据：用户 m03520 的 Q1C / Q2 / Q3 / Q4 / Q5 / Q6 / Q7 / Q8
上游事实：19 个 `ViewElementKind` 目录 · 36 项框架固定项 · `view.json` 键全集 · 四模块声明现状（取证中）
取代范围：`decisions.md` 7.55 · 7.57 · 7.58，以及 7.56 落地的"`view.json` 只补几何"读法

---

## 0. 一句话

把"模块界面"从**两个事实源**（C# 里声明 + 包内 `view.json` 只补几何）改成**一个事实源**：
包内**一个**声明文件描述整棵控件树与全部可调属性；框架用一份 **schema** 给出全部默认值与约束（代码零默认）；外观只由全局主题给；默认共有元素不进声明面。
文件内分**两段**（用户 m04188 裁决）：**框架级段**（极少量、非解释型 —— 模块窗口尺寸与约束、网络标签的默认位置）+ **控件级段**（大量、解释型完备 —— 控件树 + 语义键 + 几何 + 各 kind 默认值）。仍是**一个文件**，理由见 §4.1。

---

## 1. 三条唯一性（Q2 / Q8）

| 对象 | 唯一事实源 | 今日状态 |
|---|---|---|
| 控件树与全部可调属性 | 模块包内声明文件 | 树在 C#、几何在 `view.json`、默认值散在三处 |
| 全部默认值与约束 | 框架 schema（随框架发布的数据文件） | 默认值散在 C# 常量、XAML 字面量、`view.json` 读取器 |
| 颜色 / 字体 / 字号 | 全局主题资源 | 部分在主题，部分写死在控件与 XAML 里 |

- **完全取代，不做兼容层**（Q8）：旧读法一律不认；坏声明 = 硬伤（该视图不加载 + 报 Issue），不做"尽量解析"。
- **机械护栏**（本设计必须自带的两条架构规则）：
  - `AR-2x 声明面默认值只能来自 schema`：读取器与控件代码里不得出现几何默认值字面量；命中即失败，例外须显式登记。
  - `AR-2x 外观只能来自主题`：`src/Ui`、`src/FrameworkUi`、`src/App` 里不得出现颜色字面量（`#RRGGBB` / `Colors.*` / `Brushes.*`）与字号字面量；主题资源文件除外，例外须显式登记。

---

## 2. 边界表（Q3 / Q4）—— 什么进声明面

| 类别 | 进声明面 | 落到哪里 |
|---|---|---|
| 控件种类与嵌套结构（整棵树） | **进** | 声明文件 |
| 语义属性：`title` / `dataSource` / 命令名 / 输入源 / `fileMode` / `filter` / `inputKind` / `textAlign`（原 `Alignment`，见 C-13）/ `stateKey` | **进** | 声明文件（今日在 `ViewElement`） |
| 几何与排布：`x` `y` `width` `height` `scaleWithWindow` · 网格 `column` `row` `columnSpan` `rowSpan` `auto` · 容器 `layout` `overflow` `padding` `spacing` `columns` `rows` `columnSpacing` `rowSpacing` | **进** | 声明文件（今日在 `view.json`） |
| 控件相关的内容与默认值：多行、日志容量、曲线点数、数值单位与精度、按钮与开关文案、表格列定义等 | **进** | 声明文件（Q4 明确） |
| 窗口：默认尺寸、最小/最大约束 | **进** | 声明文件 |
| 网络标签（端口）：**默认位置** —— 哪条边（`edge`）+ 边上位置（`offset`） | **进**（只这一条） | 声明文件的**框架级段**（用户 m04188 裁决，见 C-15）；正式名 = 网络标签 |
| 网络标签（端口）的其余：标记几何（宽 / 长 / 固定件 / 名字最大宽）、可拖动性、拖动与吸附阈值、标签文案来源 | **不进** | 框架内置固定项（§6.3）；用户拖动覆盖照旧 |
| 外观：颜色 / 画刷 / 字号 / 字体族 / 圆角 | **不进** | 全局主题（Q3）；框架级段里的 `theme` 只是**预留键**、本轮不实现 |
| 默认共有元素：关闭菜单栏、模块运行状态、外壳（无边框 / 不可缩放 / 透明）、端口标记本身 | **不进** | 框架内置固定项（Q4） |
| preset / include / 继承 / 变量 | **不进** | 不做（Q6，见 §7） |

---

## 3. 节点模型（Q1C：属性目录化 → 原子控件 + 嵌套）

两层，不含第三种：

- **容器节点**：可含子节点
- **原子控件节点**：不可再分，无子节点

### 3.1 容器（4 种）

| kind | 作用 | 关键属性 |
|---|---|---|
| `container` | 通用容器；排布由 `layout` 决定 | `layout` = `flow`（单列自上而下，默认） / `grid` / `absolute`；`overflow` = `scroll`（默认） / `clip`；`padding`；`spacing`；`columns` `rows` `columnSpacing` `rowSpacing` |
| `group` | 带标题分组框 | `title`；`padding`；`spacing` |
| `tabs` | 页签容器 | 子容器各带 `title` = 页签名；当前页状态键 |
| `collapsible` | 可折叠分组 | `title`；`expanded` 默认值 |

**收敛**：删掉 `Scroll=17` 这一 kind —— 滚动是容器的 `overflow` 属性，不是一种控件（一个概念只留一个位置）。
**待裁 C-1**：容器粒度用**显式 kind**（上表，推荐：可读、可校验、与"目录"一致）还是**单 `container` + 正交属性**（`title` / `collapsible` / `overflow`，类型更少但把三种行为挤进一个节点）。

### 3.2 原子控件（15 种，含 6 种尚未渲染）

| # | kind | 中文 | 渲染 | 今日枚举 |
|---|---|---|---|---|
| 1 | `numeric` | 数值 | ✅ | 0 |
| 2 | `statusLight` | 状态灯 | ❌ | 1 |
| 3 | `curve` | 曲线 | ✅ | 2 |
| 4 | `table` | 表格 | ❌ | 3 |
| 5 | `textLog` | 文本日志 | ✅ | 4 |
| 6 | `hexView` | 十六进制视图 | ❌ | 5 |
| 7 | `gauge` | 仪表 | ❌ | 6 |
| 8 | `image` | 图像 | ❌ | 7 |
| 9 | `configurationEditor` | 配置编辑器 | ✅ | 8 |
| 10 | `commandButton` | 命令按钮 | ✅ | 9 |
| 11 | `toggleSlider` | 开关滑块 | ✅ | 10 |
| 12 | `enumSelector` | 枚举选择 | ❌ | 11 |
| 13 | `textInput` | 文本输入 | ✅ | 12 |
| 14 | `fileSelector` | 文件选择 | ✅ | 13 |
| 15 | `toggleSwitch` | 拨动开关 | ✅ | 18 |

**6 种未渲染控件的处置（推荐）**：目录**全部显式登记**（19 个原 kind 一个不丢，Q5 要求"究竟有哪些必须能显式理解"），schema 给每个控件一个显式 `rendered` 状态；**声明 `rendered: false` 的控件 = 硬伤**（拒绝加载并报 Issue），不允许出现今日那种"声明了却只画一句『尚未渲染』回退文案"的静默失败。
- 备选：从 SDK 枚举中删掉这 6 个（目录更纯，但丢掉了"框架打算支持什么"的显式记录）。
- 备选：本批一并实现这 6 个控件（扩大一次重构建的范围）。

**静态视图边界**：声明文件是**加载时读一次的静态树**，不提供条件、循环、按配置增删控件。需要"随配置改变界面"的，用页签 / 折叠 / 置空的既有能力表达；真正需要动态增删的情况见 §10 待裁 C-2。

---

## 4. schema 与默认值（Q2）

### 4.1 声明文件的内部结构（用户 m04188 裁决：两段）

一个文件、两个顶层段：

| 段 | 内容 | 体量 | 性质 |
|---|---|---|---|
| `frame` | 框架级个性化声明：`window`（默认尺寸与约束）· `ports[]`（网络标签默认位置：`name` / `edge` / `offset`）· `theme`（**预留、本轮不实现**） | 几行 | 非解释型：框架与模块共有，但模块有个性化需求 |
| `sources` + `views` | 控件级声明：数据源表 + 控件树（语义键、几何、各 kind 默认值） | 几十到几百行 | 解释型完备：能完整描述界面 |

**为什么仍是一个文件**：拆成两个文件会造出两个事实源、两条加载路径、两条错误通道（"哪个文件先读、哪个坏了算谁的"），而框架级那几行不值得这些代价；两段在同一份 JSON 里也有同一套校验与同一份 Issue 通道。

### 4.2 schema 的位置与形态

框架内一份 `schema/view.schema.json`（随框架发布），声明每一个节点 kind 的全部属性、类型、约束、默认值：

```
{
  "window": { "defaultWidth": {"type":"number","default":320,"min":1}, ... },
  "common": { "x": {...}, "width": {"type":"size", ...}, ... },
  "defaultGeometry": { "*": {"width":{"fill":true},"height":{"adaptive":true}}, "textLog": {"height":{"fill":true}}, ... },
  "nodes": {
    "textInput": {
      "kind": "atomic", "rendered": true,
      "properties": {
        "title":     {"type":"string","default":""},
        "multiline": {"type":"bool","default":false},
        "textAlign": {"type":"enum","values":["default","left","center","right"],"default":"default"}
      }
    }
  }
}
```

### 4.3 读取器职责（改后）

1. 按 schema 校验声明文件（两段一起校，`frame.ports[]` 的口名还要与数据面注册的名字对账）；
2. 按 schema **填默认值**（代码里不再有 per-kind 默认分支）；
3. 产出内部节点模型。

### 4.4 校验口径（沿用今日两级，硬伤即拒绝加载 + Issue）

- **硬伤**：`version` 缺失或 ≠ 1；未知 key；未知 kind；类型错；枚举越界；数值小于 `min` 或大于 `max`；同轴 `min > max`；同一个轴给了多条尺寸策略；`absolute` 元素缺任一个轴或该轴不是像素；容器没有子节点；容器 `layout: grid` 的位置信息缺失或混用；`frame.ports[]` 口名缺失/重名、`edge` 不认识、`offset` 越界。
- **警告**：省略了某个轴（按 schema 的 `defaultGeometry` 的 kind 默认口径填 —— C-14 建议 B）；**声明了 `rendered: false` 的 kind**（已登记、框架尚未渲染 ⇒ 界面会缺这一块，但**不拒绝加载** —— C-2 用户裁决 m04188）；`frame.ports[]` 里的口名在数据面不存在（须宿主加载时对账）；段缺失（按 schema 默认）；`max` 小于有效下限（该条忽略）。
- **边界**：schema 是数据文件而非代码 ⇒ "代码零默认"可被机械核查。

---

## 5. 外观：只走全局主题（Q3）

- 声明面**没有任何外观键**（不给、也不预留）。
- 控件渲染只从主题资源取值（`DynamicResource`），因此主题切换即时生效，观感整体一致。
- 今日写在 `ModuleContentView.axaml`、`ConfigurationFormView.axaml` 中的字面量要**分流**：
  - **属于外观**的（颜色、字号、字体族、圆角）→ 移入主题资源；
  - **属于测量基线与排布**的（标签列宽、下拉宽、内边距）→ 留在 §6.2 的固定项清单里显式登记。

### 5.1 取证：今日外观现状（`src/Ui` + `src/FrameworkUi` + `src/App`，99 个 axaml/cs）

唯一主题词典 = `src/Ui/Styles/Tokens.axaml`（169 行）：`Light`（:13-60）/ `Dark`（:62-91）各 23 个 `SolidColorBrush`，另有字体族 3（`FontUi` :96 · `FontData` :97 · `FontIcons` :153）、圆角 1（`ModuleCornerRadius`=2 :105）、尺寸与字号 9（:116-141）、图标码位 13（:156-168）；**间距类没有任何键**。
引用与写死对比：`DynamicResource` **279** · `StaticResource` **0** · 写死外观 **238**（`#RRGGBB` 72 · `FontSize` 字面 105 · `CornerRadius` 字面 22 · `Opacity` 15 · 具名色 9 · `Colors.*` 5 · `Brushes.*` 8 · `FontFamily` 2）。
写死集中处：`ModuleWindow.axaml` 21（具名色 5 · 字号 4 · 圆角 5 · 不透明度 7）· `SettingsView.axaml` 24（字号 20 · 圆角 4）· `ModuleContentView.axaml` 25（字号 20 · 圆角 4）· `Controls.axaml` 8 · `TopologyView.axaml` 11 · `InteractionView.axaml` 11 ·
字号语义档位（缺键的档位即"需要新增主题键"的清单）：12 = 窗口正文（已有键 `ControlFontSize`，:117）· 11 = 分区标题/次要文本 ≈45 处（**无键**）· 10 = 弱化文本 ≈40 处（**无键**）· 9 / 13 / 14 / 15 / 16 / 18 各自零散（**无键**）。
同语义不一致（外观"观感一致性"要消掉的对象）：① 图标码位字号 5 个值（`Controls.axaml:171`=13 · `ModuleCatalogView.axaml:43`=13 · `:72`=14 · `:159`=11 · `Controls.axaml:179`=16）；② 速率文本两值（`Tokens.axaml:141` `PortMarkerRateFontSize`=10 vs `RoleTextMetrics.cs:28`=15）；③ 次要文本两值（`Controls.axaml:116`=11 vs `ModuleContentView.axaml:531`/`:579`=10）；④ `Tokens.axaml:120` 键值 11 而同文件 :124 注释写"箭头字号 11 → 16"、实际 :130 是 **18**（注释与值不符）。
圆角：字面 22 处但键只有 1 个；同为"小方块/色块"取 **2**（`ModuleContentView.axaml:199/:234/:504/:553` 等）与 **3**（`ModuleWindow.axaml:486` · `SettingsView.axaml:237` · `InteractionView.axaml:122` 等）两值，另有 0 / 7 / 8。
同一默认色多份字面量：`#2563EB` ×6（`Tokens.axaml:22` · `ThemeApplier.cs:59` · `SettingsView.axaml.cs:86`/`:89` · `SettingsPanelViewModel.cs:246` · `ApplicationSettings.cs:77`）· `#F3F4F6` ×5 · `#0891B2` ×4 · `#EA580C` ×3 · 占位色 `#9AA4B2` ×2（`PacketTypePalette.cs:40` · `DataTypeColorTableViewModel.cs:112`）。
主题开关的执行面很窄：唯一写入者是 `src/Ui/Theming/ThemeApplier.cs`（`ApplyCore` 设 `app.RequestedThemeVariant` :110-115，覆盖 AccentPrimary/AccentSecondary/OverlaySelection/OverlayHighlight `:260-265`、BgWorkspace/BgModule/BgChrome `:270-272`、DirectionSend `:279`、DirectionReceive `:284`、FontUi `:145`、FontData `:150`）；调用点只有 `SettingsPanelViewModel.cs:525`（装载）与 `:898`（应用/确认）⇒ **启动路径不调用**（`src/App/App.axaml.cs`、`CompositionRoot.cs` 零命中），且全仓无 `ActualThemeVariantChanged` 监听；`src/App/App.axaml:5` 把 `RequestedThemeVariant="Light"` 写死。
字体族有 5 个来源且两处各写一遍：`Tokens.axaml:96` `FontUi` = `Segoe UI, Microsoft YaHei UI, sans-serif` vs `SettingsPanelViewModel.cs:286` / `ApplicationSettings.cs:116` = `Segoe UI, Microsoft YaHei UI`（**少了 sans-serif 回退**）；`Tokens.axaml:97` `FontData` 是**等宽** `Cascadia Mono, Consolas, Courier New, monospace`，而设置的默认数据字体是 `Times New Roman, 宋体`（衬线），且 `ThemeApplier.ApplyFonts` 只要非空就覆盖（`ThemeApplier.cs:148-151`）⇒ **主题里的等宽声明默认永不生效**。死资源（定义零引用）：`ModuleBandThickness`(:119) · `GlyphRun`(:162) · `GlyphStop`(:163)。
**不进主题的两类有色值（应在护栏里显式登记为例外）**：① 数据类型自动配色 `src/Ui/ViewModels/PacketTypePalette.cs`（`For()` 三级回落：用户覆盖 → FNV-1a 稳定哈希（`AutoSaturation`=0.52 :43 · `AutoLightness`=0.63 :46 · `AutoColor` :88-99）→ 占位色）；② `ThemeApplier` 的预设 8 档与背景档 `:59-66`/`:294-295` 与 `Secondary()`/`Lighten()` `:307-316`（用户可配置色，属设置数据不属观感主题）。
模块侧已合规：`src/Modules/**` 与 `src/Sdk/**` 对 `FontSize|FontFamily|#hex|Brushes\.|Colors\.|CornerRadius` 零命中（唯一命中是 `System.Globalization.NumberStyles`）⇒ **今日模块声明本身不含外观键**，Q3 只需在框架侧收口。

### 5.2 需要新增的主题键（按取证推出的最小集）

| 类别 | 现状 | 建议 |
|---|---|---|
| 字号 | 只 9 个专用键，正文/次要/弱化 3 档全靠字面 | 建**字号阶梯**（如 `FontSizeBody`=12 / `FontSizeSecondary`=11 / `FontSizeCaption`=10）+ 保留现有专用键；图标字号并入阶梯或单独键 |
| 圆角 | 只有 `ModuleCornerRadius`=2 | 建 2–3 档（控件 2 / 面板边 3 / 圆形按钮由半宽推出） |
| 字重 | 无键（`SemiBold`/`Bold` 散在 XAML） | `FontWeightTitle` / `FontWeightBody` |
| 不透明度 | 无键（0.20 / 0.12 / 0.6 / 0.85 已入 Tokens，其余散在 XAML） | 全部入 Tokens（含过渡时长） |
| 间距 | 无键 | 建 `SpacingControl`=2 等；**但排布相关间距属 §6.2 固定项**，只把"观感用边距"入主题 |

**落地判据**：`src/Ui`/`src/FrameworkUi`/`src/App` 里"外观字面量"归零（唯一例外 = §5.2 表格里显式登记的例外），由 §1 的第二条机械护栏核查。

---

## 6. 框架内置固定项（Q5 的显式清单）

分三类，全部显式登记 + 一句话理由。**不进声明面**不等于"没有事实源"——事实源就是这张表 + 代码里的单一定义处。

### 6.1 进声明面（唯一例外：窗口）

| 项 | 默认 | 约束 |
|---|---|---|
| 窗口默认尺寸 | 320 × 220 | ≥ 1 |
| 窗口最小尺寸 | 180 × 160 | ≥ 1；有效下限 = max(框架下限, 声明值) |
| 窗口最大尺寸 | 无上限 | 声明值 < 有效下限 ⇒ 该条忽略 |

### 6.2 框架固定、声明面不暴露（需显式登记）

（取自 36 项固定项取证，逐条附理由；此处列组，落地时写成表格）

- **外壳**：无边框、不可缩放、不进任务栏、透明底 → 模块窗口不是普通窗口，是受控面板。
- **内容区留白** 20 / 22 / 20 / 24（必须用 Margin）→ 保证常驻滚动条不占位。
- **容器外观与标题**：外框内边距 6,4,6,6、圆角 2、标题字号 10。
- **容器内容边距**：`ContentMargin = (padding, padding + 4, padding, padding)`。
- **元素间距**：schema 默认 2（声明可覆盖；今天写死在 `ModuleElementPanel` 的 `DefaultSpacing`，按 Q2 移进 schema）。
- **填满 / 自适应**：**不再是框架固定项**——今天由代码里的类型判定（`IsFill`：`textLog` 与多行 `textInput`）决定，改为 schema 的 per-kind 默认口径（`defaultGeometry`：`*` = 宽 `fill` / 高 `adaptive`；`textLog` 高 `fill`；多行 `textInput` 高 `fill`；见 §10 的 C-14，建议 B）。
- **兜底与夹取**：尺寸下限 1；`overflow: clip` 且可用空间有限时才取 min；`absolute` 未声明几何的元素按流式接在声明区之下（**待裁 C-4**：是否改为硬伤）。
- **文本框基线**：最小高 88 / 60；分页窗口 20–200 行；行高 14；刷新 50 ms；自动换行；`↵` 标记 gap 2.0 / 缩放 0.85；滚轮 3 行/格。
- **日志容量**：20000 行丢最旧（**待裁 C-5**：是否把容量做成声明键）。
- **曲线点数**：256（同 C-5）。
- **元素表重建**：切页 / 改设置时整表重建，故折叠态与输入态必须借 `stateKey` 存储。
- **拖动与编辑期纪律**：拖动 / 编辑期间不回写。
- **主壳基线**：基准短边 396、最小 360 × 480。
- **遮罩**：叠加遮罩不透明度由主题给（唯一值 0.60）。

### 6.3 网络标签（正式名）：只声明**默认位置**，其余进固定项

端口标记几何（宽 20 / 长 150 / 固定件 49 / 名字最大宽 101 / 窗口最小外沿 160）、锚点定位（`available × clamp(offset) − length/2` 再夹取）、拖动阈值 4 px、吸附与对齐阈值（4 / 8 / 24 / 4 / 1 / 12）、「固定」按钮几何、可拖动性 —— 全部属**通信与排版设施**，由框架统一决定；模块只能通过数据面声明"有哪种口"。

**用户 m04188 裁决**：网络标签"既属于框架又属于模块自身，存在多种约束"，所以**只声明默认位置**——在哪条边（`edge`）与边上的哪个位置（`offset`），放在声明文件的**框架级段**。它是数据面口名的一个薄附属，不是控件。

**与惯例的关系（C-15 · 2026-09 复裁）**：**没有兜底惯例** —— 用户原话「『发送/接收』直接由 .json 文件声明，不规定默认值，.json 写什么就是什么 ⇒ 唯一事实源统一口径」。`edge` **必填**，数据面登记的口**必须**在 `frame.ports[]` 里各有一条（缺 ⇒ 硬伤、整份忽略）；优先级：**用户拖动覆盖（`WorkspaceLayout._ports`）> 声明值**；`PortEdgeResolution` 的「发送右 / 接收左」回退分支删除（`decisions.md` 7.61）。本段原主张的「模块窗口 = 接收顶 / 发送底、主壳 = 发送右 / 接收左」**作废**（它曾被写成"必须补的框架固定项"）。

**需要落地的改动**：③ 批把它写成了 `ports[].default.edge/offset` + `label` + `constraints.draggable`（落点 `src/Sdk/Views/ViewLayout.cs:573`/`:598` · `ModuleViewLayoutReader.cs:396-431`）⇒ 保留 `edge` / `offset`，**删掉 `label` 与 `constraints.draggable`**（前者由数据面口名给，后者是框架固定项），并按上面三层优先级重接 `src/Core/Composition/PortGeometryResolution.cs`。

---

## 7. Q6：preset / include 的决定（我定）

**都不做。** 理由：

- **preset（预设）**会造出第二层继承：默认值来自 schema，预设又给一套值，声明再覆盖 ⇒ 三个事实源，"代码零默认"退化为"看情况"；用户排查界面问题时要追三层。
- **include（片段包含）**引入路径解析、包含次序、循环包含与重复定义问题，而它能表达的"复用"在单文件静态树 + 页签/折叠下收益有限；且会让"一份声明 = 一个界面"这条心智模型失效。
- 若将来出现真实的重复（多个模块声明几乎相同的子树），优先做法是**框架提供新的原子控件或容器**（一处定义、一处实现），而不是文本层面的包含。

---

## 8. 命名统一：端口 / 网络标签

- **正式名称 = 网络标签**（用户明确：这是很早确认的名称）。
- "端口"仅保留在**数据面 / 架构层**语义（连接点、口名、边、偏移）与技术标识符（`PortMarkerMetrics`、`PortEdgeResolution`、`ports[]`）里；一切**面向用户的文案、模块文档、界面提示**统一说"网络标签"。
- 落地动作：文档全仓统一（`docs/modules/**`、`architecture/**`）、UI 文案检查、术语表登记一处（`architecture/02` 术语行）。

---

## 9. 迁移与批次（Q7 一次重构建 / Q8 完全取代）

**施工顺序见 `.dsh/drafts/migration-checklist.md`（10 步，含每步的目标 file:line 与验证层）。** 概要：

1. schema 落库为框架资产（唯一事实源）+ 启动自检；
2. 读取器按 schema 校验并填默认，产出完整控件树；
3. 四个模块的 `OnRegistered` 建树代码移出，改为包内声明文件；
4. 代码里的声明面默认值归零（测量基线另登记）；
5. 网络标签归位（C-15）：框架级段只留 `edge` / `offset`（`edge` **必填、无默认、无惯例**），删 `label` 与 `constraints.draggable`，写死「用户拖动 > 声明值」的优先级（2026-09 复裁）；
6. 窗口段留声明面、默认值移进 schema；
7. 控件目录合一（4 容器 + 15 原子，删 `Scroll`）；
8. 外观归位（238 处字面量 + 主题接线）；
9. 测试改造（删路径键守门、加顺序与对账用例）；
10. 文档回写（`decisions.md` 新条取代 7.55/7.57/7.58 · `architecture/02` 正文 · `roadmap.md` 收口）。

（本节原有一版"迁移五步"，其中第 1 步写的是"schema 先行、旧 C# 声明仍可读、不改变模块行为"—— 那是一条**兼容过渡**路径，已被 Q7"一次重构建"与 Q8"不做兼容性修改"否掉，故删除。原第 5 步"端口部分作废"保留，升为 C-15 与第 5 步。）

---

## 10. 待裁问题（请逐条裁决）

| # | 问题 | 我的建议 |
|---|---|---|
| C-1 | 容器粒度：显式 4 种 kind vs 单 `container` + 正交属性 | 显式 4 种 |
| C-2 | 6 个未渲染控件：目录登记 + `rendered:false` 禁声明 / 从枚举删除 / 本批实现 | **用户裁决（m04188）：登记 + 不禁声明** —— 现在没人会声明它，现在禁了以后实现时又要改回去。落地口径：允许声明、不拒绝加载，解析时按**警告**报出来（"已登记、框架尚未渲染 ⇒ 界面会缺这一块"）——既不拒载，也不静默。 |
| C-3 | 动态视图 | **已取证：静态即可**（三处循环生成只由编译期常量 `PageCount = 5` 驱动，无按配置/运行时分支 ⇒ 5 页在声明文件里直接展开） |
| C-4 | `absolute` 未声明几何的元素：按流式接在下方 vs 硬伤 | 硬伤（一处不写就报错，好排查） |
| C-5 | 日志容量 20000 行、曲线点数 256：保持框架固定 vs 做成声明键 | 做成声明键（Q4：用户可调且有默认值） |
| C-6 | 声明文件名 `view.json` 保留 vs 改名（它现在是全量声明，不再是几何补丁） | 保留 `view.json` |
| C-7 | 声明文件与模块包的关系：仍在包内 `module.json` 旁 vs 其他 | 同目录并列 `view.json` |
| C-8 | preset / include | 都不做（§7） |
| C-9 | 命令声明归属：模块级 `commands[]` 表 + 元素按名引用 / 命令直接声明在按钮元素上（声明即入口） | 声明在按钮元素上（今天 `DataMonitor` 的 `save` 命令无按钮 ⇒ 不可达，这是"两处各写一遍"的产物） |
| C-10 | 数据源的绑定键名：沿用单一 `dataSource`（三种角色混用一个键） / 按角色分成 `data` `input` `command` 三个键 | 按角色分键（今天同一个名字既是 Inbox 路由键又是数据源条目名，声明层无类型区分） |
| C-11 | 外观归位的范围：只把模块界面（`src/Ui`）的字面量入主题 vs **整仓**（含 `src/FrameworkUi` 的 238 处写死外观，§5.1）一并归零 | 整仓一并归零（Q3 说的是"整体观感一致性"；只改模块界面会把同类不一致留在主壳与模块之间）；数据类型自动配色与用户配色属**设置数据**，显式登记为主题护栏的例外 |
| C-12 | 启动时应用主题：组合根补一次 `ThemeApplier.Apply` + 监听主题变更 vs 维持"只有设置面板装载后生效" | 补（否则 `App.axaml:5` 写死的 `Light` 会盖过用户设置里的 Dark，直到打开设置页） |
| C-13 | 两个"对齐"键重名问题（§14）：grid 格子对齐 `align` 与 `textInput` 文本对齐 `alignment` | 把文本那个改名 `textAlign`（格子对齐沿用 `align`/`verticalAlign`） |
| C-14 | 尺寸轴缺省：**A** 每个轴必须显式写一条（7.58 已落地的口径，读取器把 `Undeclared` 当硬伤） vs **B** 省略即用 schema 的 `defaultGeometry`（每个 kind 的缺省口径写在 schema 里，代码零默认） | **B**：Q2 明确"默认值由 schema 显式给出、代码零默认"；A 会让 schema 的默认值永远用不上，且流式树里每个节点都要抄两行样板（text-sender 一页 30 个节点 = 60 行）；B 的"具体档位"仍只有一处（schema），今天"清空/保存一大一小"那类不一致恰好由"按 kind 统一默认"消除 |
| C-15 | 网络标签的**默认位置**归属（Q4 与 ③ 批的 `ports[]` 冲突） | **用户裁决（m04188）：只声明默认位置** —— 在哪条边（`edge`）+ 边上的哪个位置（`offset`），放在声明文件的**框架级段**；它不是控件，但确有模块自己的个性化需求。标记几何 / 可拖动性 / 拖动与吸附阈值 / 标签文案来源 → 框架固定项（§6.3）；**不声明 = 硬伤**（`edge` 必填、无框架惯例，2026-09 复裁）；优先级 = **用户拖动覆盖 > 声明值**。 |

---

## 11. 取证结论（已回，作为本设计的依据）

- **声明入口**：四个模块都在 `OnRegistered` 里内联建树并调 `context.Declaration.DeclareView(...)`（全仓 4 处：`DataMonitorModule.cs:180` · `TextSenderModule.cs:199` · `HexSenderModule.cs:167` · `HexStringConverterModule.cs:156`）；`ModuleContext.DeclaredView` 只取**首个**视图（`ModuleContext.cs:143`）。
- **`ViewElement` 属性全集 = 11 个**（`src/Sdk/Views/ViewDeclaration.cs:275-380`）：`Kind` `DataSource` `Title` `Children` `StateKey` `Multiline` `FileMode` `FileFilter` `SuggestedFileName` `InputKind` `Alignment` —— **零几何、零外观**。
- **真正按 kind 专有的声明键只有 5 个**：`TextInput` 的 `Multiline`(false) / `InputKind`(Text) / `Alignment`(Default)、`FileSelector` 的 `FileMode`(Open) / `FileFilter`(null) / `SuggestedFileName`(null)，加容器共用的 `StateKey`(null)。其余 15 个 kind 只吃 `Kind` / `DataSource` / `Title` / `Children`。
- **绑定角色由 kind 决定**（`src/Sdk.Testing/ModuleContract.cs:190-195`）：`CommandButton` ⇒ 命令；`IsInteractive` 的 7 种（`ConfigurationEditor` `CommandButton` `ToggleSlider` `ToggleSwitch` `EnumSelector` `TextInput` `FileSelector`）⇒ 输入源；其余 ⇒ 数据源。**同一个字符串今天既是 Inbox 路由键又是 `dataSources` 条目名**（如 `paused` / `interval`）。
- **无动态视图**：三处 `Enumerable.Range(0, PageCount)` 循环（`TextSenderModule.cs:157-168` · `HexSenderModule.cs:124-137` · `HexStringConverterModule.cs:97-107`、`:125-136`）只由编译期常量 `PageCount = 5` 驱动；无 `if`/`switch`/读设置包住声明。
- **无配置使用实例**：四模块均无配置类型、无 `OnConfigurationChanged`、无 `ConfigurationEditor` 元素；配置面只有以 `typeof(T).FullName` 为键的 `Read<T>/Write<T>` 与 `[ConfigurationField]`（键 = 属性名；`ConfigurationFieldKind{Boolean,WholeNumber,Number,Text,Enum}`）。
- **被业务模块实际用到的 kind 只有 8 个**：`TextLog` `ToggleSwitch` `CommandButton` `FileSelector` `TextInput` `Numeric` `TabPages` `Group`（其余 11 个无使用实例）。
- **今天真正的四模块绑定键**：`DataMonitor`——`log`(数据源) / `paused` `hexView` `wrap` `timestamp` `savePath`(输入源) / `clear` `save`(命令，`save` 无按钮 ⇒ 不可达)；`TextSender`——`text1`–`text5` `continuous` `interval`(输入源) / `sent`(数据源) / `send`；`HexSender`——`hex1`–`hex5` `continuous` `interval` / `sent` / `send`；`HexStringConverter`——`char1`–`char5` `hex1`–`hex5` / `hexResult` `charResult` / `to-hex` `to-text`。
- **一件事实缺陷**：`src/Modules/Business/DataMonitor/view.json:23` 的注释引用 `DataMonitorModule.DeclareViewCore`，源码里不存在该方法。

## 12. 逐 kind 属性 schema（第一版）

通用键（所有节点）：`kind`（必填）· `title`（除 `commandButton` 外，默认 ""）· `stateKey`（默认 null，仅容器与 `textInput` 消费）· 几何与排布键（§2 表）。

绑定键按角色命名（C-10）：`data` / `input` / `command`；每个 kind 只允许其中**一个**，写错 key = 硬伤。

| kind | 角色键 | 专有键（默认值） | 渲染 |
|---|---|---|---|
| `numeric` | `data` | `unit`("") · `format`(不做) | ✅ |
| `statusLight` | `data` | `states`(待定) | ❌ |
| `curve` | `data` | `capacity`(256) | ✅ |
| `table` | `data` | `columns`(待定) | ❌ |
| `textLog` | `data` | `capacity`(20000) · `wrap`(true) · `showNewlineMark`(true) · `showInlineResult`(true) · `feedbackSource`(null) | ✅ |
| `hexView` | `data` | `bytesPerRow`(待定) | ❌ |
| `gauge` | `data` | `min`(0) · `max`(100) · `unit`("") | ❌ |
| `image` | `data` | `stretch`(待定) | ❌ |
| `configurationEditor` | `data` | （今天无使用实例，字段由属性上的特性推出） | ✅ |
| `commandButton` | `command` | `title`(命令名) · `icon`(null) | ✅ |
| `toggleSlider` | `input` | — | ✅ |
| `enumSelector` | `input` | `options`(待定) | ❌ |
| `textInput` | `input` | `multiline`(false) · `inputKind`("text") · `textAlign`("default") · 尺寸按 `defaultGeometry`（多行高 fill / 单行高 adaptive） | ✅ |
| `fileSelector` | `input` | `fileMode`("open") · `filter`(null) · `suggestedFileName`(null) | ✅ |
| `toggleSwitch` | `input` | — | ✅ |

模块级：`sources[]`（`name` 必填 · `kind`（Value/Sequence/RowSet/ByteBlock/Text/Frame，默认 Text）· `valueType`（契约原始类型，可选）· `unit`（可选））+ `window`（§6.1）+ `views`（容器树）。
校验：元素引用的名字必须在 `sources[]` 里存在；`sources[]` 里未被任何元素引用的条目 = 警告（防止"声明了却接不上"的死条目）。

**待补**：6 个未渲染 kind 的专有键（今天无使用实例，schema 留 `rendered: false` 占位，实现时再定）；`ConfigurationEditor` 的字段目录（今天无使用实例）。

**机器可读版（本轮新增）**：`.dsh/drafts/view.schema.draft.json` —— 把本节与 §4 的口径写成可直接校验的 schema（window / axes / tracks / commonProperties / containers / atoms / document / validation.hard / validation.warning）。
**四模块的完整声明例（本轮新增）**：`.dsh/drafts/view-declaration-examples/*.view.json`（`data-monitor` · `text-sender` · `hex-sender` · `hex-string-converter`）—— 把 C# 里的树 + 语义键 + 今天 `view.json` 里的几何**合并成一份声明**，用以证明"新声明面能完整表达今天的四个模块"。校验器：`.dsh/drafts/validate-view-declarations.ps1`（只读，按上面的 schema 校验四份例）。
**落地施工顺序（本轮新增）**：`.dsh/drafts/migration-checklist.md` —— 10 步（schema 落库 → 读取器切换 → 四模块改声明 → 默认值归零 → 端口退出声明面 → 窗口段 → 控件目录合一 → 外观归位 → 测试改造 → 文档回写），每步附目标 file:line 与它踩在哪一段已落地的半截上。
**校验器的反证样本（本轮新增）**：`.dsh/drafts/negative-tests/*.view.json`（5 份故意写坏的声明，见 §14）。

## 13. 仍待补的取证 / 未决

- **主题切换的接线**：今日 `ThemeApplier.Apply` 只在设置面板装载与应用时调用、启动路径不调用、无主题变更监听。若要求"改设置即时全局一致"，需在组合根补一次启动应用（属本设计的配套项，**待裁 C-12**）。
- **两处口径必须随本设计消掉**（今日已知不一致，非本设计引入）：`Tokens.axaml:120` 注释与 `:130` 键值不符；`Tokens.axaml:97` 的等宽 `FontData` 与设置默认 `Times New Roman, 宋体` 冲突。
- **`ConfigurationEditor` 的字段目录**：今日无使用实例，字段仍由 `[ConfigurationField]` 特性推出（§12 待补）。
- **6 个未渲染 kind 的专有键**：留 `rendered: false` 占位，实现时再定（§12 待补）。
- **容器那一档的缺省尺寸口径**：`defaultGeometry` 里 `*` = 宽 `fill` / 高 `adaptive` 已覆盖原子控件，但**容器**（`group` / `tabs` / `collapsible`）今天的实际占位要对着 `src/Ui/Controls/ModuleElementPanel.cs` 的 `ArrangeOverride` 实测一遍再定稿（否则会把今天的默认行为改掉）。
- **外观分流清单**：238 处字面量逐条归入"主题键"还是"§6.2 固定项"（本轮只给了分档与集中处的量级，逐条清单要到落地时随改造一起出）。

## 14. 覆盖验证（本轮实测）

以草案 schema 校验四份示例声明（`pwsh -NoProfile -File .dsh/drafts/validate-view-declarations.ps1`）：**4 份全部通过，硬伤 0 / 警告 0**。结论与过程中暴露的问题：

- **可表达性**：四个模块今天用到的 8 个 kind（`textLog` `toggleSwitch` `commandButton` `fileSelector` `textInput` `numeric` `tabs` `group`）全部覆盖；今天由 `Enumerable.Range(0, PageCount)` 生成的分页结构（三个模块各 5 页）在声明文件里**直接展开成 5 个 `group`** ⇒ 与 §11 的"静态即可"结论一致，不需要循环能力。
- **路径键消失（结构性收益）**：几何不再是"按 `v/0/2` 路径打的补丁表"，而是**节点自己的键**。今天"改了模块声明顺序、忘了同步 `view.json` 也照样通过（路径键仍然合法）"这一整类缺陷随之消失，为它写的守门用例（`数据显示器_声明顺序应等于视觉顺序`）在切换后即可删除。
- **命名冲突（须裁决或改名）**：grid 的**格子对齐**键 `align` 与 `textInput` 的**文本对齐**键 `alignment` 只差一个后缀，语义完全不同 ⇒ 建议把文本那个改名 `textAlign`（列在 §10 待裁追加）。
- **通用键的范围修正**：`showInlineResult` 与 `feedbackSource` 今天对**任何元素**都有效（`ModuleViewLayoutReader` 逐 child 读），不是 `textLog` 专有 ⇒ 已从 §12 的 `textLog` 行移入 schema 的 `commonProperties`（`fileSelector` 关掉就地结果、把反馈接到 `textLog` 左下角就是这个机制）。
- **命令的校验边界**：命令名不在 `sources[]` 里，声明文件只能校验"声明了、写法合法"；**"声明了命令但模块没注册处理器"只能由宿主在加载时对账**（报 Issue）。反向的"模块注册了处理器但没有按钮"今天是 `DataMonitor.save`（有处理器、无按钮 ⇒ 不可达）⇒ 按 C-9 建议，声明文件里它直接消失，模块可保留处理器供测试/热键，此情形按**警告**报出。
- **按钮文案的归属**：今天按钮的显示名来自模块级的 `ViewCommand(name, "清空")` 表（`clearButton` 本身**没有** `Title`）⇒ 按 C-9 建议改为按钮自带 `title`，示例里已写成 `"title": "清空"`。
- **页签名**：`tabs` 的页签名取子容器 `title`（转换器今天给的是「字符」「十六进制」，另两个模块的 `tabs` 没有标题）⇒ 与 `group.title`/`tabs.title` 是同一套键，不需要新键。
- **自相矛盾被抓住（本轮）**：schema 文字写"每个轴必须显式給一条（不给 = 硬伤）"，而校验器对**缺失**的轴是放行的（`if ($null -eq $axis) { return }`）⇒ 同一份草案里两条口径打架。原因是"显式"这条来自 7.58（几何单独成文件时，沉默=歧义），而 Q2 又说"默认值由 schema 给出"——两者在合并成单文件后**必须先选一条**。已提为 **C-14**（建议 B：省略即用 schema 的 per-kind 默认），并把 schema、校验器、示例一并改成 B 的口径（示例无需再为 30 个流式节点抄 60 行样板）。
- **默认口径表**（B 方案下写进 schema 的 `defaultGeometry`）：`*` = 宽 `fill` / 高 `adaptive`；`textLog` 高 `fill`；多行 `textInput` 高 `fill`（今天这档是代码里的 `IsFill` 类型判定，改后**代码零默认**）。容器那一档待与 `ModuleElementPanel.ArrangeOverride` 实测对齐（§13）。
- **校验器反证（本轮新增）**：`.dsh/drafts/negative-tests/` 放 **6 份**故意写坏的声明（未知键 `colour` · `absolute` 容器里缺 `height` · `commandButton` 写成 `data` · grid 里混用 `column/row` 与 `auto` · `frame.ports[]` 的 `edge: "above"` 与 `offset: 1.5`）⇒ 校验器报 **8 处硬伤、exit 1**；另有 1 份只触发**警告**的 `warn-unrendered-kind.view.json`（声明 `rendered:false` 的 `table` —— C-2 用户裁决后它不再算硬伤）⇒ 校验器确实会把坏声明判红、把"能加载但会缺一块"的声明判黄，不是恒绿的空壳。

---

## 15. 用户裁决（m04188）与我的评估

用户原话（裁决）：**C-2**「登记，不禁声明（目前没人会声明它，后面需要的时候再做实现，现在禁了后面又要改回去）」；**C-15**「网络标签很特殊，他既属于框架又属于模块自身，存在多种约束，因此只需要声明默认位置（在哪条边和在边的哪个地方）……它不是控件，但是又有一定的个性化需求，因此 `.json` 似乎可以分为两部分：1. 框架级个性化声明（极少量，如模块本身大小、网络标签位置、特殊的主题需求等不完全解释型声明，只有几行）2. 控件级声明（大量，解释型极强，完备）」；**其余按草案建议**。并要求我评估，认可就照做、不认可必须说出来。

| 项 | 我的评估 | 落地口径 |
|---|---|---|
| C-2 登记 + 不禁声明 | **认可**。"禁了以后还要改回去"是对的：这一条的价值不在"拦住谁"，而在"登记清楚有哪些 kind 存在"。但原方案（禁声明）会被我按你这条改成**警告**——不是硬伤、不拒绝加载，却也不静默（模块作者会在日志里看到"你声明的控件框架还没渲染"）。 | `rendered:false` 的 kind：声明合法 ⇒ 解析成功 + 报一条警告。schema、校验器、§4.4 已同步。 |
| C-15 只声明默认位置 | **认可**。网络标签确实两头都沾：它是通信设施（框架定几何），又带模块自己的排布意图（模块知道自己的收发口在哪条边更顺眼）。"只声明默认位置"正好卡在这条界线上，而且它只有 2 个键（`edge` / `offset`），完全符合你说的"只有几行"。 | `frame.ports[] = [{ name, edge, offset }]`；`label` 与 `constraints.draggable` 删除；优先级 = 用户拖动 > 声明值；**不设框架惯例**（`edge` 必填，2026-09 复裁）。 |
| `.json` 分两部分 | **认可，但按"一个文件、两个顶层段"实现**（`frame` + `sources`/`views`），不是两个文件：两个文件会造出两个事实源、两条加载路径、两条错误通道（"哪个先读、哪个坏了算谁的"），而框架级那几行不值得这个代价。若你要的就是两个物理文件，请说一声——改动量不大，但上述代价要一起接受。 | 见 §4.1；schema/校验器/四份示例已按两段落盘。 |
| 框架级段里的"特殊主题需求" | **这一条我暂不认可，请你定**。Q3 定的是"只使用全局主题，保证颜色/字体的整体观感一致性"；一旦允许模块在自己的声明里覆盖外观，就等于在主壳与模块之间重新开一条外观通道，也与"238 处写死外观归零"属同一类问题（且"整体一致性"这个验收点会失去机械判据）。所以我在 schema 里把 `theme` 写成**预留键**（结构上允许，本轮不实现、声明里不出现；出现即警告），门留着、先不开。如果你要现在开，请顺带定它的范围（允许覆盖颜色？字号？只允许"强调色"？）——那会变成一条独立裁决，我也会同时把它登记进护栏例外清单。 | `frame.theme` = 预留、默认 `null`；出现即警告。 |

**当前状态**：C-1 / C-2 / C-3 … C-15 全部已达裁决（C-2、C-15 按上面两条改写，其余按草案建议）⇒ 草案自洽，可以进入 `.dsh/drafts/migration-checklist.md` 的 10 步落地。
