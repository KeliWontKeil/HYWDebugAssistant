# 终端显示模块（仪表盘 / 进度条 / 三轴姿态）设计

> 本文是四个终端显示模块与配套控件的**唯一设计口径**。进度只在 [`../status.md`](../status.md) 维护；手工验收项在 [`../acceptance.md`](../acceptance.md)。数据帧约定见 [`waveform-frame.md`](waveform-frame.md)。

## 1. 定位与范围

**目标**

- `hyw/gauge-display`：方形窗口，把某一组的**一个通道**显示成指针表盘（红针）。
- `hyw/scale-bar-horizontal` / `hyw/scale-bar-vertical`：长条窗口，把**一个通道**显示成**矩形细条进度条**（Windows 进度条观感，默认绿）。
- `hyw/attitude-display`：把某一组的**三个通道**当作横滚 / 俯仰 / 偏航显示三维姿态。
- `Sdk.Ui` 通用控件：`GaugeDial`、`ScaleBar`、`AttitudeView`，以及**浮层设置容器 `ModulePopupPanel`**。

**不做**：不改波形帧契约；不给控件新增色板 token；不引第三方渲染库；不做"发现到的组/通道列表"（编号由用户配置）；不持久化发现到的组/通道对象本身；不把姿态视角写进模块设置、`meta` 或视图状态（视角不持久化）。

## 2. 模块拆分与共享边界

| 项 | 值 |
|---|---|
| 包名 / 模块 ID | `gauge-display`、`scale-bar-horizontal`、`scale-bar-vertical`、`attitude-display`（`hyw/…`） |
| 程序集 | `HYWDebugAssistant.Modules.<Name>`，目录 `src/Modules/Business/<Name>/` |
| 依赖 | 只引 `Sdk` + `Sdk.Ui`（AR-1）；模块之间不互引 |
| 窗口尺寸 | 只写在视图根 XAML（AR-18）：仪表 300×300、横向条 400×100、纵向条 100×400、姿态 400×400（运行期最小尺寸同在各视图根） |

**为什么横向/纵向是两个模块**：窗口形状不同，而窗口初始/最小尺寸只能由视图根声明一次；拆成两个模块比运行期切换布局更符合"尺寸只有一个事实"。

**为什么控件进 `Sdk.Ui`**：模块只能引用 `Sdk` / `Sdk.Ui`，四个模块要共用同一套绘制与平滑，唯一合法落点是控件库。帧解析与组/通道状态按仓库既有做法每模块一份（与 `WaveformDisplay` 同构），不为共享代码新增程序集。

## 3. 裁决

1. **目标值只有一条规则（唯一事实）**：样本新鲜 → 样本值；超过"回零延时"没有新样本 → 开"自动回零"给 0（姿态为单位姿态）、关则保持最后样本；没有样本（含通道不存在）→ 0。实现是 `ChannelFrameFeed.Resolve` 一处。
2. **控件没有"无数据"分支**：三个数据显示控件只画收到的目标值；"保持还是回零"由模块发布的目标值决定。
3. **平滑在控件、回零在模块**：只有模块知道最后一帧何时到；只有控件有每帧绘制节奏。控件按框架每帧 `Refresh` 的 dt 逼近，settle 后停止重绘，第一帧直接落到当前值。
4. **组/通道是用户输入的持久化值**：`input.group`（正整数，1 基，无“0 = 自动跟随”）、`input.channel`（正整数，1 基；帧槽位 = 通道号 - 1）；姿态为 `input.roll/pitch/yaw`（1 基，默认 1/2/3）。**不钳制**：通道不存在按“没有样本”处理（目标 0），因此用户能提前配置任意编号；0 非法，保留上一份有效值并标红，不自动改成 1。
5. **设置界面是统一的"浮层面板标准"**：面板正文是一个独立视图（`Views/*SettingsPanel.axaml`，继承 `ModuleView`、自己声明尺寸），因此预览器能单独打开；模块视图只声明一个 `ModulePopupPanel`（正文类型 + 标题 + 恢复默认命令名）；容器开**无边框不透明独立窗口**（标题行 + 刷新 + 关闭）并负责定位、生命周期与逐帧刷新。预览单独打开正文时**不套模块窗口预留带**（正文从 (0,0) 起，与运行期正文一致），但范围框照常显示。详见 [控件文档](../../docs/zh/module-dev/controls.md) 的"浮层面板"一节。
6. **一个值一个编辑器**：视图 XAML 里同名值出现两个输入控件会让包加载失败（`DuplicateValue`）；渲染物这类设置只放在浮层面板里。
7. **角度单位属于输入解释**：姿态的"度/弧度"只影响读到的通道值如何换算，读数始终显示度；三轴反相只翻符号。
8. **视角属于控件，不属于模块**：默认等轴测；左键拖动、左上角按钮/空格七视角、左下地面参考轴都在 `AttitudeView` 内。相机按世界坐标系（前视看世界 +Z 面，不跟随机头），与机体姿态相互独立；视角不写设置/`meta`/视图状态，不持久化。七视角面板是控件内 4 列 × 3 行浮层，等轴测在上视右侧，点一个立即应用并关闭；图标是固定等轴的透明线框立方体 + 单面高亮。底栏每个轴同时显示角度与弧度，并用“数据单位”徽标标明数据通道当前的角度单位。机体轴按相机深度拆前后两遍绘制，机体内部段不画，轴标签在机体之后绘制。

## 4. 契约

### 4.1 显示源与输入

| 名称 | 类型 | 方向 | 说明 |
|---|---|---|---|
| `value` | F32 单值 | 模块 → 视图 | 仪表 / 进度条的目标值（永远是有限实数） |
| `roll` / `pitch` / `yaw` | F32 单值 | 模块 → 视图 | 姿态模块的三个目标角度（度） |
| `meta` | 文本 | 模块 → 视图 | 设置摘要 JSON；视图按变更号解析 |
| `status` | 文本 | 模块 → 视图 | 一句话状态；界面作为**悬停提示**，不占版面 |

没有 `action` 输入：结构选择已由值承担。

### 4.2 `meta` schema

```json
{
  "revision": 12,
  "settings": {
    "group": 1, "channel": 1,
    "min": 0, "max": 100, "autoZero": true, "zeroDelayMs": 800,
    "title": "", "unit": "", "smooth": true, "smoothMs": 120,
    "decimals": 2, "fillFromZero": true, "color": ""
  }
}
```

- `group`/`channel`（姿态 `roll/pitch/yaw`）供模块外面那行**摘要**用；进度条多 `fillFromZero` / `color`；姿态另含 `style/unit/zoom/grid/labels`。
- 视图只在变更号变化时解析并应用。

### 4.3 值

| 值名 | 编辑器 | 默认 | 说明 |
|---|---|---|---|
| `input.group` | Number | `1` | 1 基组号；0 非法，不自动跟随 |
| `input.channel` | Number | `1` | 1 基通道号（帧槽位 = 通道号 - 1） |
| `input.roll` / `input.pitch` / `input.yaw` | Number | `1/2/3` | 姿态三通道（1 基） |
| `settings.minimum` / `settings.maximum` | Number | `0` / `100` | 量程 |
| `settings.autoZero` / `settings.zeroDelay` | Boolean / Number | `true` / `800` | 统一回零规则 |
| `settings.title` / `settings.unit` | Text | 空 | 标题 / 单位 |
| `settings.smoothing` / `settings.smoothingMs` | Boolean / Number | `true` / `120` | 平滑 |
| `settings.decimals` | Number | `2` | 小数位 |
| `settings.fillFromZero` | Boolean | `true` | 仅进度条 |
| `settings.color` | Text | 空 | 仅进度条：`AARRGGBB`，空 = 主题默认；旧五档键解析失败回落默认 |
| 姿态：`settings.style` / `unit` / `zoom` / `grid` / `labels` / `invertRoll|Pitch|Yaw` / `lostZero` / `lostDelay` | 枚举/布尔/数字 | 见表 | 渲染物只在浮层面板里 |

- 命令 `resetSettings` 只重置 `settings.*`，不动 `input.*`；值被清空（模块"还原"）时按默认重新解析。
- 上下界互逆或数字非法：不应用该设置，`Values.Report` 一条结论，保留上一份有效设置；可归一化项夹紧后生效。用户输入空串按非法处理；只有框架清空值（`ReadText` 为 `null`，还原/移除值）才回默认。

### 4.4 值编辑器职责拆分（本轮修正）

`ModuleConfigField` 曾同时承担“自动选编辑器 + 文本/数字 + 布尔 + 枚举”，与已有的 `ModuleTextSurface`、`ModuleToggle`、`ModuleSelect` 重叠。现在按值的种类拆开：

| 值种类 | 控件 |
|---|---|
| 文本 / 数字 | `ModuleTextSurface`（`CommitMode` 由场景给） |
| 布尔 | `ModuleToggle` / `ModuleCheckBox` |
| 枚举 | `ModuleSelect`（下拉）/ `ModuleSegmentedControl`（少量选项） |
| 颜色 | `ModuleColorField` |

- 视图显式选择控件；不再按 `ValueState.Editor` 自动建编辑器。
- `ModuleSelect` 补齐外观通道（文字色/字号/只读/`:error` 红框），因此配置字段的枚举不再需要宿主代管。
- 设置面板的文本/数字用 `ModuleTextSurface CommitMode="OnChange"`；配置表单保持 `OnExplicit`（配置变更重开会话）。

## 5. 浮层面板（标准）

**动机**：模块窗口会裁切视图，视图内浮层出不了模块窗口、还会挡住可视化；平台 `Popup` 的落位/翻转/是否落 overlay 层由平台决定，出现过贴在模块上的残留，透明弹窗还让设置项发灰。

**标准（模块只做前两件，其余归控件库）**

| 件 | 谁做 | 内容 |
|---|---|---|
| 面板正文 | 模块 | `Views/<Name>SettingsPanel.axaml`，继承 `ModuleView`，声明 `InitialWidth/InitialHeight`（表单区尺寸），带 `[ModuleViewDesignData]` ⇒ 预览器可单独打开；预览只渲染正文并保留范围框，不套模块窗口预留带 |
| 容器声明 | 模块 | 模块视图里一个 `ModulePopupPanel`：`PanelType` / `Title` / `ResetCommandName`；代码后置设置 `Anchor` |
| 外壳与窗口 | `Sdk.Ui` | `ModulePanelSurface`（标题 + 刷新 + 关闭）+ 无边框不透明独立窗口；以齿轮 `Anchor` 为基准右侧优先、放不下换左/下/上、屏幕内夹紧，打开期间锚点加 `panel-anchor` 高亮并随模块窗口逐帧跟随；Esc/关闭图标/模块窗口关闭或隐藏时关闭；内容包进内部 `ModuleViewHost` 按帧刷新。外壳不在正文宿主祖先链上，容器创建时把视图上下文显式绑定给刷新按钮 |

- 面板图标固定为 `icon.refresh` / `icon.close`，属主由容器按正文程序集给（正文在模块视图之外）。
- 关闭只是隐藏窗口，正文状态保留；再次打开复用同一窗口。
- 容器铺在模块视图最上层但不参与命中，不吃齿轮与控件的点击。
- 预览：模块视图里只有齿轮；面板正文单独预览，另有 `<模块>-settings.png` 单独截图。
- 不采用：视图内浮层（会被裁切）、`Popup`（平台行为不可控、透明弹窗发灰）。

## 5.1 仪表盘几何（口径修正）

- **圆心 = 控件中心**；半径由短边决定：`(min(宽, 高) − 2×padding − 环厚)/2`，两轮迭代（半径↔环厚）后按最终环厚回算，保证外沿精确内接。标题/读数/单位在圆内绘制，**不参与尺寸计算**。
- 端点数字与主刻度重合时只画一次，避免 `0..100` 的 `100` 重影。
- 不变量：圆心在控件中心、半径只由短边决定、环外沿在控件内；有单测钉住（不钉具体半径）。

## 5.2 跨实例自动分配组/通道（评估 = 不做）

需求：新建同类模块时沿用上一个实例的组、通道 +1。

- 模块之间不能互查（P12），每实例的值在创建时由 `ModuleRuntime.SeedValueDefaults` 从本模块包 XAML 初值播种，没有"按模块类型共享"的存储 ⇒ 只能在框架层加"播种策略"（`ValueDeclaration` 新策略 + `ModuleRuntime` 找同模块最近实例并末位自增）。
- 代价与语义面：SDK 公开面新增、创建路径改动、顺序/删除/还原/跨工作区/解析失败语义、文档与用例 —— 属于新的框架能力，超出本轮精修。
- 结论：不做；本轮用"面板内可编辑 + 外面常显摘要"缓解。
## 5.3 控件留白与窄模块（本轮扩展）

- **`Padding`（两个数据显示控件）= 唯一的内部留白来源**：`GaugeDial.Padding`（默认 6）、`ScaleBar.Padding`（默认 2）；`0` = 环/轨道外沿紧贴控件边框。仪表几何只由 `GaugeGeometry.Compute(..., padding, ...)` 算，视图不再用 `Margin` 去凑留白。
- **纵向进度条文字列在条左侧**（沿条的长轴），与横向条"文字在条上方"同构：文字仍由视图标签拼，条只画轨道/填充/刻度；宽度方向只多一列文字。**这是待确认观感**：回滚只需把 `ScaleBarVerticalView.axaml` 的 `Row1` 换回"三行文字在上、条在下"，控件与框架不动。
- **框架级窄模块能力**（不在模块里写特判）：
  - 尺寸地板拆成宽/高：`PortMarkerMetrics.MinWindowWidth` 96、`MinWindowHeight` 160；
  - 端口标记尺寸固定；当前边放不下时由 `PortMarkerLayout.FitEdge` 把**边直接改掉**（一次坐标式变更，不记原边、不自动挪回；忽略锚点固定）；
  - 窄窗 chrome：`ModuleSurfaceMetrics.NarrowWidth` 200 是唯一断点，`ModuleViewViewModel.IsNarrow` 由 `Width` 派生（宽度本来就是唯一尺寸事实），窗口 XAML 用 `Classes.narrow` 切换"按钮簇竖排 / 选中标题竖排"，不另起事件与布尔。
## 5.4 窄窗 chrome / 标记尺寸 / 预览框（本轮修正）

- **窄窗判定改为"窗口实际 Bounds.Width"**（`ModuleWindow.ApplyChromeMode`）：上一轮用视图模型的 `Width` 派生 `IsNarrow`，既在预览器里失效（预览器改的是 Bounds），又因为**内联本地值压过样式**导致样式根本没生效。现在：`Orientation` / `MaxWidth` / `TextTrimming` 全部移入样式（基础样式在前、`Window.narrow ...` 在后），状态就是窗口自己的 `narrow` 类，没有第二份事实。
- **标记尺寸契约**：模板不再自带 `Width/Height`；`PortMarkerPanel` / `PreviewPortPanel` 在排布前把标记的**固定尺寸**显式写进容器的 Width/Height，再 Arrange。只靠 Arrange 矩形会让容器按内容尺寸摆根控件（表现为标记忽宽忽窄/错位）。
- **预览虚线框**：新增 `IModulePreviewBoundsTarget`，`GaugeDial` / `ScaleBar` / `AttitudeView` 实现；`ModuleView.AddPreviewTarget` 命中即收 —— 自绘控件终于有排版框可对齐。
- **进度条紧凑化**：刻度线与数字间距 3 → 2；纵向数字旋转 −90°（绘制点按 −90° 反算，与 `ChartSurface` 画 Y 轴标题同一手法），标签带 34/22 → 14；纵向模块 130×420 / 最小 96×320；横向模块 `MinHeight` 与高度地板对齐为 160。
## 5.5 端口换边 / 控件自量（本轮修正）

- **端口标记尺寸固定**（150×20），放不下就**直接换边**：`PortMarkerLayout.FitEdge(declared, direction, width, height)`（声明边 → 同轴镜像 → 左右 → 上下）。运行期由 `ModuleViewViewModel.FitPortEdges` 把结果**写回 `PortMarkerViewModel.Edge`**（宽高变化与 `SyncPorts` 之后各一次），预览外壳在创建时对 `ModulePort.Edge` 做同样一次写入。**没有"声明边/有效边"两份状态，也没有"空间回来再挪回去"的记忆**——换过去的边自己就放得下，再算一次仍是它。**忽略锚点固定**（固定只约束拖动）。
- **锚点按钮贴标记**：沿边优先放**标记之前**（贴近标签端），其次之后，都放不下时压住标记的色条端；取"面板端点"会让按钮落到窗口尽头（实测踩过）。
- **窄窗按钮顺序**：`Cluster` 由 `StackPanel` 改为 5×5 `Grid`；横排按列、窄窗按行且**关闭在 0 行**（反序）。样式驱动，无代码重排。
- **自绘控件按内容报尺寸**：`GaugeDial` 报方形（圆的直径，圆心即控件中心；标题/读数/单位不参与），`ScaleBar` 报“条 + 刻度带 / 条 + 标签带”；视图用非拉伸对齐（Center/Left）⇒ 控件 `Bounds` = 绘制范围，预览虚线框紧贴内容；`Margin` 是框外布局间隙、`Padding` 是框内留白。
- **预览范围框分两种角色**（`PreviewBoundsRole`）：`Control`（虚线，控件最终边界）给标签/读数/勾选框 + `IModulePreviewBoundsTarget`；`Container`（点线 + 半透明，槽位）给背景不可见的 Panel/Grid/Border（含模块内容区根元素）。上一轮"干脆不框容器"是过度修正：容器/布局区确实需要框，混淆来自**同一种线型**；现在用线型区分。需要可见槽位边框时仍可用视图里的 `PreviewOnly` 边框（`ChartLayoutFrame` 先例）。
## 5.6 本轮缺陷修正（小数端点 / 校验 / 从零填充 / 取色器 / 动态端口）

- **小数端点标签**：端点用 `FormatEndpoint`（最多 6 位小数去尾零）而不是主刻度步长精度；端点标签先建槽，主刻度标签按文字宽度与弧长避让，端点优先。
- **校验记账**：`InputValidity` 在有效/无效切换时写/清 `Danger` 外观；`ValueNotice` 只给原因。控件不每帧擦红圈，`ApplyAppearance` 早退保留。范围校验逐帧读当前文本（不是被回退的候选），所以互逆时不红会“消失”的旧 bug 消失；修正一侧后未改的另一侧也会按当前文本重新生效。
- **仪表从零填充**：`GaugeDial.FillOrigin` 与 `ScaleBar` 同口径；模块值 `settings.fillFromZero`，默认打开。
- **取色器初值**：空值时把 `DefaultBrushToken` 解析成实际颜色再传给取色窗，不再从灰色开始。
- **动态端口/地址**：`SerialConfig.PortName`、`TcpServerConfig.LocalAddress`、`UdpConfig.LocalAddress` 是动态枚举，视图用 `ModuleSelect`；没有可选项时下拉为空，不退回文本框。数字字段仍是文本提交校验（旧数字档也没有键入过滤）。
- **提交默认**：`ModuleTextSurface` 默认 `OnChange`；配置表单显式 `OnExplicit`（变更会重开会话），并在注释说明。

## 6. 控件职责

| 控件 | 只画什么 | 不做 |
|---|---|---|
| `GaugeDial` | `Value` → 弧线、刻度、端点标签、红色指针、读数；`FillOrigin` 决定填充起点；超量程钉边界、读数转警示色 | 不读显示源、不解析设置、不判断无数据、不起定时器 |
| `ScaleBar` | `Value` → 矩形轨道 + 填充（`FillOrigin`）+ 刻度与数字；`BarThickness` 控制细条厚度；`FillBrush` 未覆盖时用主题 `DefaultFillToken` | 同上；**不画标题/读数/单位**（由视图标签拼） |
| `AttitudeView` | 三个角度 → 世界固定、相机可交互的透视投影（默认等轴测）；`RenderStyle` 切绘制物；拖动/七视角/地面参考轴在控件内 | 同上；不做单位换算、不持久化视角、不读模块设置 |

## 7. 边界

| 情形 | 处理 |
|---|---|
| 从未收帧 | 目标 0（姿态为单位姿态） |
| 数据停止 + 自动回零开 | 超过延时目标变 0；悬停提示"已回零（N ms 无数据）" |
| 数据停止 + 自动回零关 | 目标保持最后有效样本 |
| 坏帧 | 丢弃、不改目标，悬停提示"帧格式不正确，已丢弃" |
| 无效掩码位 | 保持最后有效样本、不刷新最后有效时刻；悬停提示"通道 N 无效" |
| 通道号超出本组通道数 | 按"没有样本"给 0；悬停提示"通道 N 不在本组内（共 M 个通道）" |
| 组号未出现 | 等待；悬停提示"等待组 N 的数据" |
| 上下界互逆 / 非法数字 | 不应用，红框 + 悬停原因，保留上一份有效设置；非法/互逆状态持续到当前文本合法，不因下一帧“候选回退”消失 |
| 超量程 | 指针/填充钉边界、读数警示色，数值不裁剪 |
| 极小窗口 | 控件退化为只画轴/轨道，不越界、不抛 |
| 窗口隐藏 | 框架不调 `Refresh`，动画停；再现时 dt 上限防止跳变 |

## 8. 待人工验收要点

见 [`../acceptance.md`](../acceptance.md) 终端显示一组条目（A121–A139）：加载不再报错、圆盘不被裁且无重影、摘要与面板内组/通道、面板独立窗口不透明且可放在模块外、预览不被面板挡、刷新图标、进度条颜色与 Windows 观感、红针、单位生效、姿态渲染物切换。
