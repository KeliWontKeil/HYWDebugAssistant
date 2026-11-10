# 波形显示器与波形发生器模块设计

> 状态：**XAML 单一尺寸源、响应式图表 + 固定控件 Canvas、组频率归属、游标/数据轴与发生器表单重构已完成**（待人工验收）。数据帧约定见 [`waveform-frame.md`](waveform-frame.md)（`hyw/waveform-frame`，280 B）。
>
> 本文是波形两模块、配套控件与组合方式的**唯一设计口径**。进度只在 [`../status.md`](../status.md) 维护；本文不重复现状读数。

## 1. 定位与范围

**目标**

- `hyw/waveform-generator`：多组、多通道、波形参数、2 个基波周期预览、连续发送、按 N 个基波周期一次性发送、SPWM/SVPWM 全参数。
- `hyw/waveform-display`：组/通道发现、波形绘制、X/Y 双范围、双游标读数、数据轴范围、跟踪、动态历史、CSV/PNG 保存。
- `Sdk.Ui` 通用控件：`ChartSurface`（全量点绘图 + 游标 + 参考线）、`RangeSelector`（单一线性范围 + 主/次刻度）、`ModuleSelect`、`ModuleSegmentedControl`、`IModuleColorPicker`（框架取色服务）。
- 新增通用显示源：`SeriesWindow`（不可变全量窗口快照），替代波形图表的环形缓冲/UI 降采样路径。

**不做**

- 不改变数据帧约定；不新增波形专用框架语义。
- 不引入第三方图表/图标包。
- 不规定模块内部历史的内存上限；只提供计量、开关、按样本数计的可选上限和清空。
- 不实现数据帧自动路由/过滤；组/通道匹配、消费、丢弃全部在模块内。
- 不把 Y 范围写进保存；局部保存只使用 X 范围。

## 2. 设计裁决（已确认）

1. **范围有两个**：
   - **X 范围**：当前活动组的样本区间 `[xStart, xEnd]`。它同时决定图表 x 轴窗口、数据轴范围带、x 起止输入、**局部保存**范围。
   - **Y 范围**：当前活动图表的值域 `[yMin, yMax]`。它只决定波形视图与 Y 游标，不参与保存。
2. **X 范围是唯一事实**：数据轴选择条只是可视化，x 起止输入是精确编辑器；图表、数据轴、x 输入、局部保存必须同一帧一致。
3. **没有固定 256 点窗口**：选择范围内有多少个点，就发布/绘制多少个点；背景网格按 X/Y 范围自适应。256/512 只是旧环形缓冲/UI 降采样的实现上限，不是需求。
4. **发生器按周期发送**：一次发送 N 个基波周期；预览固定 2 个基波周期；组“每周期点数”是组内共享的采样分辨率。
5. **SPWM/SVPWM 全参数**：载波频率、调制比、载波相位、组基波频率/分辨率、通道相位/幅值/偏移全部在界面可编辑；编译期只给初始值；禁止静默参数与静默降级。
6. **布局紧凑**：单层边框、紧内边距、组卡分隔线、小留白；具体数值由截图人眼复核。
7. **颜色不写死**：通道颜色走框架统一调色窗口；自动色只在通道首次出现数据时分配一次并持久化，重置后重新自动分配。
8. **Y 自适应只增不减**：自动 Y 按组锁存，数据变小不缩回；只有“适应当前曲线”、复位、清空、通道重建显式缩小。
9. **发生器逻辑树、固定坐标表单**：组行/通道行都用同一分段 chips + 同一加/垃圾桶；组段→组参数→通道段→波形选择常显；波形参数行允许运行期按类型隐藏。连续发送/发送一次/停止全部是按钮，没有减号；不恢复左侧树。结构控件集中在 `Canvas`，参数块用行列容器；代码不写结构坐标，也不把结构控件默认隐藏。
10. **尺寸两条事实**：视图根 `.axaml` 的 `InitialWidth/InitialHeight` 是实例化默认内容尺寸，`MinWidth/MinHeight` 是运行期最小内容尺寸；两者独立，chrome 由框架加。禁止 `[ModuleDefinition]` 尺寸与运行期读取 `d:`。

## 3. 总体架构

```text
发生器配置（组/通道/周期/调制参数）
   ↓ 每组定时器（低频逐帧 / 高频时间片批量）
波形数学 → 280 B 帧写入器 → out 端口
   ↓ 数据面（只搬运）
in 端口 → 帧读取/校验 → 每组历史
   ↓ 活动组 X 范围（唯一事实）
模块构建不可变 SeriesWindowSnapshot（范围内全部点 + 有效 Y）
   ↓ 框架快照槽（引用替换）
视图读取（不复制、不抽稀）
   ↓
ChartSurface（画全部点，XStart/XEnd，网格自适应）
RangeSelector（X 范围可视化） + x 输入 + y 输入 + 局部保存
```

**模块标识**

| 项 | 发生器 | 显示器 |
|---|---|---|
| 模块 ID | `hyw/waveform-generator` | `hyw/waveform-display` |
| 包名 | `waveform-generator` | `waveform-display` |
| 端口 | `out`（Send，Bottom） | `in`（Receive，Top） |
| 角色 | `generate` | `display` |
| 内容尺寸（XAML Initial / Min） | 960×320 / 960×320 | 960×580 / 960×580 |
| 外框（内容 + chrome） | 框架加，其他任务统一 | 框架加，其他任务统一 |

## 4. 通用控件与显示源

### 4.1 `SeriesWindow` 快照源

- 来源：显示器模块、发生器预览。
- 形态：不可变对象，一次提供某个 X 范围的全部点；没有环形容量、没有 UI 降采样。
- 类型 `SeriesWindowSnapshot`：版本、X 起止、latest、有效速率、有效 Y 起止、YAuto、通道列表。
- 类型 `SeriesWindowChannel`：通道序号、显示名、颜色、`float[] Values`（长度 = X 范围宽度）。
- 写：模块 `IViewDisplayAccess.SetSeriesWindow`（实例线程，引用整体替换）。
- 读：视图 `IModuleViewContext.ReadSeriesWindow`（UI 线程，只读）。
- 发布后模块不得改数组；更新时构建新对象。
- 旧 `Sequence`/环形缓冲保留给其他模块；波形图表不使用。

### 4.2 `ChartSurface`

| 成员 | 说明 |
|---|---|
| `Series: IReadOnlyList<ChartSeries>` | 视图复用列表；每个序列的 `Values` 是 `ReadOnlyMemory<float>`，可直接引用快照数组 |
| `DataVersion` | 数据/范围真正变化时递增；不每帧无条件递增 |
| `XStart`、`XEnd`、`XStep` | x 域与点间距（Index/Time 单位）；`XEnd` 为空时按点数推导（兼容旧调用） |
| `XAxisKind` | Index / Time |
| `XAxisTitle`、`YAxisTitle` | 通用属性；波形视图不设置标题/单位 |
| `YMin`、`YMax` | 模块算好的有效 y 范围 |
| `XTickStep`、`YTickStep` | 0 = 自动 nice tick |
| `ShowCursors`、`CursorAFraction`、`CursorBFraction` | X 游标显示与屏幕比例 |
| `ShowYCursors`、`YCursorAFraction`、`YCursorBFraction`、`YCursorALabel`、`YCursorBLabel` | Y 游标与 m/n 标签 |
| `ReferenceLines` | 竖向参考线（周期刻度等），支持虚线 |
| `PlotInset` | 绘图区四边内缩（左/上/右/下，DIP）；**唯一边距来源**，刻度、游标标签、轴标题都算在内 |
| 绘图区 | 由 `PlotInset` 决定；显示器 `50,20,4,20`，发生器 `40,4,4,20`；不再按 `ShowCursors` 动态加顶部，也不再有固定 30 DIP 底部 |
| `CapturePng()` | 当前图表 PNG 字节 |
| 事件 `CursorChanged` / `YCursorChanged` | 拖动产生 |

- 绘制：绘图区、网格、x/y 刻度、全部曲线点（NaN/Inf 断线）、X 游标线/标签、Y 游标线/标签、参考线；空数据只画轴。
- 绘图区外框：左、下两条边线就是坐标轴，再补上、右两条边线；只框绘图区，不含刻度文字、游标标签或图例。
- 预览布局外框：两个波形视图在图表槽位叠一层 1 px 主题色外框，只在设计/预览环境可见；图表 `Margin=2` 作为图表与布局边界的距离。运行期隐藏，不参与布局、命中或 `CapturePng()`。
- 网格由 X/Y 范围与刻度步长计算；不按点数反推域。
- 指针：只处理 X/Y 游标线或标签拖动；夹紧；互不影响。
- 游标标签：Y 标签在绘图区外左侧，可覆盖 y 轴刻度；X 的 A/B/Δ 同顶对齐，Δ 位于 A/B 两游标中点；统一 `符号 + 空格 + 值`，不用冒号。Y 用 m/n。Δ 固定为第二游标减第一游标（`B−A`、`n−m`），可为负，不取绝对值。模块不再设置 Y 轴标题/单位。
- 曲线颜色由调用方显式给出；缺省用主题强调色，不按索引取内置七色。
- 性能：不抽稀；大范围由用户缩小范围或状态提示；只按 `DataVersion` 重绘。

### 4.3 `RangeSelector`

| 成员 | 说明 |
|---|---|
| `Minimum`、`Maximum` | 可选域；调用方给（显示器 = 活动组 `[0, latest]`，按当前 x 单位） |
| `RangeStart`、`RangeEnd` | **唯一的当前范围**（图表窗口 / 数据轴范围带 / 局部保存） |
| `MinimumRangeSpan` | 最小范围跨度（与域同单位） |
| 轨道 | 占满自身控件宽度，仅 1 DIP 描边内缩；不接收图表绘图区 inset |
| `TickStep`、`ShowTicks` | 主刻度线 |
| `ShowTickLabels`、`MinorTickStep`、`XAxisKind`、`TickLabelBrush` | 主刻度数字、次刻度、Index/Time 格式与标签色 |
| `TickBrush` | 刻度线色；与轨道边框分开，浅色轨道也能看清 |
| `HandleWidth` | 只作边缘命中宽度，不再画粗手柄 |
| 事件 `RangeChanged`、`LastChangeKind` | 拖动产生；调用方按 `Start/End/Body/New` 区分左端改宽与移动范围 |
| 绘制 | 主题色轨道（浅色主题近白、深色主题随主题）、半透明选择块（1 px 边界）、深色主/次刻度、主刻度数字；**不再有独立 viewport/selection 两层状态** |
| 指针 | 左右边缘 `SizeWestEast`，选择块内部 `SizeAll`，外侧默认；`OnPointerMoved` 即使不拖动也更新 |

- 不读数据集、不知道波形/组/通道；任何线性范围都能用。
- 数字精确值由 x 起止输入承担；控件只做可视化与拖动。
- 指针：拖两端改范围；拖中间整体平移；夹紧到 `Minimum..Maximum`；域为空/零跨度时禁用。
- 对齐：数据轴与图表不共享像素几何。图表保留 `PlotInset.Left` 的 Y 轴预留；数据轴从自身控件边界到边界，预览器与运行期不再因刷新时机产生 40 DIP 空白。

### 4.4 选择类通用控件

- `ModuleSelect`：`ComboBox` 薄壳/主题；`Options`、`SelectedKey`、`OptionSelected`；用于 X 轴模式。
- `ModuleSegmentedControl`：互斥分段；`Options`（key/label/icon/enabled/tooltip）、`SelectedKey`、`ShowLabels`、`IconSize`；用于发生器波形类型。
- `IModuleColorPicker`：框架调色窗口服务，经 `IModuleViewContext.ColorPicker` 暴露；`SuggestedColors` 与推荐色同源；`PickAsync` 返回 `Confirmed/ColorArgb/Reset`。模块不内建色板。
- `ToggleButton.module-row-action`：行内切换按钮（隐藏曲线、适应 Y、组/通道启用）；选中态有明确底色/边框。

### 4.5 窗口尺寸事实

- **两个独立事实**：模块视图根 `.axaml` 的 `InitialWidth/InitialHeight`（实例化默认）与 `MinWidth/MinHeight`（运行期下限）。
- 实际外框 = 内容 + `ModuleSurfaceMetrics` chrome；chrome 由框架加一次，不在本轮统一。
- `InitialWidth/Height` 是唯一字面量；`ModuleView` 把同一值镜像给预览器的 `Design.Width/Height`，因此不再写 `d:DesignWidth/Height`。`[ModuleDefinition]` 的 Window/Min 尺寸已废弃，内置模块不得声明。
- 初始尺寸用于新建/还原；最小尺寸用于运行期夹紧。持久化布局优先，但夹到有效区间。
- 不允许 `Math.Max(ModuleDefinition, XAML)` 的双源合并。

### 4.6 事实归属

| 事实 | 所有者 | 说明 |
|---|---|---|
| 活动组/通道历史、`DisplayIndex`、latest | 显示模块 | 唯一数据源 |
| X 范围 `[start,end]` | 显示模块（活动组运行期） | 图表、数据轴、x 输入、局部保存共同读取 |
| Y 范围（手动 + 有效值） | 显示模块（活动组配置 + 自动计算） | 只影响图表/Y 游标，不参与保存 |
| X 模式、跟踪范围、X/Y 刻度、游标、跟随、自定义显示频率 | 显示模块设置（全局） | 视图逐键回读/提交；有效速率 = 自定义频率或活动组帧声明速率 |
| X 轴单位 | `AxisModeSelect` + `DisplayAxisUnits` | 样本是内部事实；Index 直接显示、Time 除以有效速率；模块与视图共用同一换算 |
| 实际可读刻度上限 | 图表/数据轴控件按实际尺寸与字体测量，视图上报模块 | 只决定红框与自动回退，不改变用户配置 |
| 活动组快照（全部点） | 显示模块构建，框架快照槽持有引用 | 视图只读 |
| 绘图区几何、游标屏幕比例 | `ChartSurface` | 图表独占；不复制 |
| 范围带拖动 | `RangeSelector` | 只发事件；不保存业务范围 |
| 组卡/通道行/文本格式 | 视图 | 只提交，不持有范围事实 |
| 通道颜色（自动/覆盖） | 显示模块配置 | 视图首次数据出现时提交一次自动色；重置后清空覆盖 |
| 发生器配置/发送状态/调制参数 | 发生器模块 | 视图只读/提交 |

- 控件之间不互相引用；显示视图是唯一组合根。
- 视图不得保存第二份 X/Y 范围；只做格式化与瞬时读数。
- 模块在夹紧/回退后必须把接受值写回输入状态，避免控件显示被拒值。

## 5. 数据帧使用

- 类型：`hyw/waveform-frame`；Stream；280 B。
- 两模块各自有 `WaveformFrameContract` 常量与 reader/writer；不做共享程序集。
- 写入：`channelCount`（1–64）、`payloadBytes=4×channelCount`、`groupId`（非 0）、`sequence`（每组独立递增/回绕，仅完整性）、`rateHz`、`validMask`、样本区。
- 读取：校验 `channelCount`/`payloadBytes`/`groupId`/`validMask`；失败丢整帧并计数；`rateHz` 非有限正数视为未知；不用 sequence 做时间/样本位置。
- 组帧率 = 组 `resolution × frequency`；组内所有通道共享同一分辨率；没有启用通道时为 0。
- 组名不传输；显示端默认“组 <id>”，本地改名。

## 6. 波形发生器

### 6.1 配置模型

```text
GeneratorConfig
  Groups: List<GeneratorGroupConfig>
  OneShotPeriods: int = 5
  ActiveGroupIndex, ActiveChannelIndex   // -1 = 组选中

GeneratorGroupConfig
  GroupId: uint (1..uint.Max，模块内唯一)
  Name: string
  Enabled: bool = true
  FrequencyHz: double = 10       // 组基波频率，所有通道共享
  Resolution: int = 100          // 每周期点数，组内所有通道共享
  Channels: List<GeneratorChannelConfig>

GeneratorChannelConfig
  Name: string
  Kind: Sine | Square | Triangle | Noise | Spwm | Svpwm
  PhaseDegrees / Offset / Amplitude
  DutyCycle: double (0.01..0.99，仅方波)
  CarrierFrequencyHz: double (>0，SPWM/SVPWM)
  ModulationIndex: double (SPWM 0..1；SVPWM 0..1.15)
  CarrierPhaseDegrees: double
  Enabled: bool = true
  LegacyFrequencyHz: double?      // 只读旧配置迁移；归一化后清空、不落盘
  LegacyResolution: int?          // 只读旧配置迁移；归一化后清空、不落盘
```

- 上限：8 组、64 通道/组；名称 ≤32 字符；组 ID 模块内唯一。
- copy-on-write；`IConfigurationAccess.Write<T>` 整体写回。
- `Continuous` 不持久化；`OneShotPeriods` 持久化。
- 默认：1 组（ID 1、组频率 10 Hz、1 通道、正弦、Resolution 100、幅值 1、offset 0、启用），发送 5 周期。
- 旧 workspace 若通道上带 `FrequencyHz`，归一化时迁移到组频率（取首个启用通道的正有限旧值，否则 10 Hz），随后清空迁移字段；新配置不再写通道频率。
- 旧 workspace 若通道上带 `Resolution`，归一化时迁移到组分辨率（取所有旧通道的最大合法值；没有则用组合法值，再否则 100），随后清空迁移字段；新配置不再写通道分辨率。
- 复杂配置不走自动 Schema 表单；模块窗口是唯一编辑器。
- 所有影响波形的参数都必须在界面可见可改；compile-time 只是初始值。

### 6.2 一次性发送：按基波周期

- 用户输入 **N 个组基波周期**。对每组 g：
  - `E` = 该组启用通道；为空或组停用则跳过。
  - `f_g` = 组频率（唯一基波频率）。
  - `groupRate = R_g × f_g`（`R_g` = 组每周期点数）。
  - `frames_g = ceil(N × groupRate / f_g)`。
  - 所有启用通道共享组基波频率和组每周期点数，不再有通道间过采样差异。
- 启动时冻结每组计划帧数；发送期间改参数对后续帧生效，但剩余帧数不变。
- 各组的 frames 独立；全部组完成即结束。
- 启动时重置 `SentFrames/Due/Random`；**`Sequence` 不重置**（否则显示端丢帧统计会爆）。重复发送从相位 0 开始，序号继续递增。
- 增删组/通道时先停止发送并写状态；结构性变化不静默继续。

### 6.3 发送调度

- 每组一个 `IModuleTimer`。
- 组帧率 ≤1000 Hz：`Interval=1/groupRate`，一次 Tick 发一帧。
- >1000 Hz：1 ms 时间片批量发多帧；小数欠账 `Due`；单 Tick 上限 1024 帧；状态提示“高频批量发送，不保证逐帧时序”。
- 上限：单组 ≤10 kHz；模块总请求帧率 ≤40 kHz；超限不静默降频，状态提示。
- 组停用：不发送、不计入预计；连续发送下立即停该组；一次性期间禁用则移出本次。
- `TrySend` 失败计数并写状态；序号仍递增。
- 添加/删除组/通道：先停止发送并写状态。

### 6.4 波形数学

- 时间 `t = frameIndex / groupRate`；相位 `φ = 2π(f_g·t + phase/360)`（`f_g` = 组频率）。
- 正弦/方波/三角/噪声：现有公式。
- **马鞍波**（连续调制波，不是开关波形）：
  ```text
  θ = 2π(f·t + phase/360)
  s = (2/√3) * (sinθ + sin(3θ)/6)
  sample = offset + amplitude * s
  ```
  三次谐波注入系数固定 1/6；归一化后复合波峰值为 ±1，界面的「幅值」就是峰值（基波为 `2/√3` 倍）。
- **SPWM**：
  ```text
  ref = m * sin(φ)
  tri = 2 * |2 * frac(fc·t + φc/360) - 1| - 1
  out = ref >= tri ? +1 : -1
  sample = offset + amplitude * out
  ```
- **SVPWM**：
  ```text
  θi = 2π(f·t + phase/360 + phaseOffset_i)  // 0 / ±120°
  va,vb,vc = sin(θa), sin(θb), sin(θc)
  vcm = -(max+min)/2
  refi = m*(sin(θi) + vcm)
  out = refi >= tri ? +1 : -1
  sample = offset + amplitude * out
  ```
- 载波固定对称三角波；不模拟死区；2π、120°、三角波是定义常量，不是隐藏参数。
- SVPWM 按通道计算：该通道相位为基准，另两相 ±120° 派生；同组多个 SVPWM 通道载波/调制比不一致时状态提示。
- **Nyquist 校验**：SPWM/SVPWM 的 `2 × carrier > groupRate` 时拒绝发送，状态说明原因；不静默降频/降载波。
- 预览：2 个基波周期，包含全部 `2 × 组 resolution` 个点（不截 256）；载波细节受预览范围限制时提示。
- 参数校验：频率/周期 >0；组 resolution 2–4096；duty 0.01–0.99；carrier >0；mod 范围；非法编辑不生效，保持旧值并写状态。

### 6.5 UI 与契约

- 三列（内容 960×320）：左 `*` `ChartSurface` 预览（无标题、无轴标题、1 周期参考虚线、不开游标）；中 560 固定表单（内部 540×288 `Canvas`）；右 150 发送 + 底部状态 14。
- 组行 → 组参数 → 通道行 → 通道名/启用 → 波形类型使用固定坐标；组参数行包含频率、周期、**每周期点数**、采样率。波形参数块为 3 列 `StackPanel + Grid`（每组一行）：基波行（偏移/幅值/相位）、方波行（占空比）、载波行（载波频率/调制比/载波相位）。每格 180×24，内部子列 72/64/44：标签右对齐，输入等宽 64，单位 44。发送区同样绝对定位。窗口变宽只让左列图表吸收，不推动按钮/输入框。
- 旧 UI 外壳：组行 = `通道组` 标签 + `GroupChips` + 加组 + 删组；通道行 = `通道` 标签 + `ChannelChips` + 加通道 + 删通道。两行同一 `ModuleSegmentedControl`、同一行高、同一加/垃圾桶样式；无减号。
- 逻辑树：点组 chip → `selectGroup`，`activeChannelIndex=-1`；点通道 chip → `selectChannel`；组是父、通道是子；不恢复左侧树。
- 组段常显：组名/组编号/组启用/**组频率/组周期/组每周期点数**/采样率；组频率和分辨率是唯一来源，所有通道共享。
- 通道结构常显：通道名、通道启用、7 个波形类型图标+文字；无选中通道时这些结构控件禁用，参数块整体隐藏。
- 波形参数按类型隐藏：正弦/三角/马鞍波显示基波行；方波增加占空比行；SPWM/SVPWM 增加载波行；噪声隐藏相位单元。隐藏行折叠，不留空行；XAML 默认全部可见，保证预览器可定位。
- 发送区：发送周期数、活动组/全部组预计帧数与字节、`连续发送`/`发送一次`/`停止` 三个按钮；无连续开关、无标题。发送中「连续发送/发送一次」置灰、「停止」可用；空闲或发送结束/结构停止后恢复；闸门由模块经 `Appearance.SetEnabled` 发布，按钮只读这一事实。
- 预览：选组时预览该组全部启用通道（统一点数），选通道时单通道；2 个组基波周期、全部点。
- 显示源：`preview`（SeriesWindow）、`snapshot`（Text JSON，单棵树 `groups[].channels[]`，组带 frequency/rate）、`status`（Text）。
- 输入：组/通道/波形/频率/周期/相位/偏移/幅值/分辨率/占空比/载波频率/调制比/载波相位/周期数/action；频率/周期/分辨率写入组。
- 命令：`addGroup`、`startContinuous`、`sendOnce`、`stop`；树形动作：`selectGroup`、`selectChannel`、`addChannel`、`removeChannel`、`removeGroup`、`setGroupEnabled`、`setChannelEnabled`。
- `ActiveChannelIndex=-1` 表示选中组（通道段禁用但常显），`>=0` 表示选中通道；删除通道后自动选中同位置/前一个残余通道。
- 无“波形发生器/2 周期预览/发送控制”等标题；所有文案走模块 i18n。固定结构控件使用 `Canvas` 相对坐标；只有波形参数块使用行列容器，以便隐藏行时塌缩。

## 7. 波形显示器

### 7.1 双范围

**X 范围**

- 规范值：样本索引 `[start,end]`；每活动组运行期一份。
- 宽度 = `end-start+1`；没有固定窗口；用户拖动/输入决定宽度。
- 跟随开启：`end=latest`，`start=end-width+1`，宽度取跟踪范围；关闭冻结。
- 范围操作按意图统一处理：
  - **宽度意图**：拖左端、输入 X 起、修改跟踪范围输入；保持当前跟踪状态，只更新宽度；
  - **移动意图**：整体平移、拖右端、重新框选、输入 X 止；关闭跟踪并写入本次范围；
  - 任何**实际改变范围**的操作都把本次跨度写回跟踪宽度；重新打开跟踪沿用最后跨度。
  - 按下范围带但没有实际移动不算操作；拖动过程中只要实际改过范围，就按该手势的意图处理（不做“回到原值就撤销意图”的额外判定）。
- 跟踪开关是唯一显式设置 `Follow` 的操作；数据自动推进不改变状态。跟随推进刷新 X 起止输入框只是显示回显，不进入 `ViewState`，不构成用户操作。
- 跟踪范围内部以**样本数**为唯一事实（默认 256，2–1,000,000）；时间模式显示为秒，输入秒后换算成样本。
- 默认初始宽度 256（无数据时也以此画 X 轴）；用户可改。
- 切组：恢复该组运行期范围；无则默认；跟随开启时滑到最新。
- 清空：历史与范围清空；复位：恢复默认并保留历史。

**Y 范围**

- 每活动组配置 `YAuto/YMin/YMax/YFitChannel`；自动时由模块在当前 X 范围内的全部可见点计算有效值；手动时用配置。
- 自动 Y 在组运行期**锁存**：只扩不缩；X 范围变化、通道显隐、跟随刷新都不自动缩小。`fitY`（显式适应当前曲线/全部）、`Y 自动` 关切到开、复位、清空、通道重建才清除锁存。
- Y 只影响图表与 Y 游标，不参与 CSV 保存内容（PNG 只是捕获当前图表）。
- Y 自动时输入仍可编辑；编辑最小/最大值会关闭自动并以当前有效值为另一端起算。
- 切换组恢复该组 Y 配置与锁存状态。

### 7.2 快照与投影

- 活动组、X 范围内每个可见通道的全部样本；`Values.Length = width`；不抽稀、不截断。
- 有效 Y 从同一范围计算；快照携带 Y 起止/YAuto。
- 触发重建：X 范围、切组、清空/复位、通道显隐/数量、历史、Y 设置、跟踪范围。
- 跟随：小范围逐帧重建；大范围按约 20 Hz 重建（每次仍是完整范围），状态显示点数/更新率。
- 活动组未被发现时，其他组到达不得抢占配置的 ActiveGroupId；只有 ActiveGroupId==0 才自动激活第一组。
- 通道数变化：重置该组历史/X 范围/快照。
- 历史上限：只限制非活动组保留的样本帧合计；活动组始终保留。达到上限停止追加并保留已有，状态提示。

### 7.3 显示源与输入

| 源 | 类型 | 含义 |
|---|---|---|
| `series` | SeriesWindow | 活动组 X 范围内全部点的不可变快照 |
| `meta` | Text JSON | 组摘要（id/name/channels/dataCount/actual/declared/lost/enabled/y 配置）+ 活动组通道 + 设置 |
| `status` | Text | 状态/错误/保存/历史/警告 |
| `latestIndex` | Value U64 | 数据轴域末端（每帧更新） |
| `rate` | Value F32 | 时间轴有效速率（模块全局自定义频率优先；关闭时用活动组帧声明速率） |
| `historyUsed` | Value U64 | 非活动组当前保留的样本帧合计 |
| `imageRequest` | Text | PNG 保存路径/请求号 |

输入：`xStart`、`xEnd`、`yMin`、`yMax`、`yAuto`、`follow`、`followRange`、`cursors`、`xAxisMode`、`useCustomRate`、`customRate`、`xTick`、`yTick`、`keepInactive`、`historyLimit`、`readableTickLimits`、`action`、`savePath`、`imageResult`。

- 文本输入逐键写入视图状态；模块在 Input step 回读，变化即应用（HEX 转换器模型），不依赖 Enter/失焦。
- `readableTickLimits` 是视图按实际控件尺寸、字号和格式化文本测量出的可读刻度上限（图表 X/Y、数据轴 X 各自一份）；模块据此判断红框，视图据此自动回退。
- `action` 支持 `selectGroup`、`selectChannel`、`setRange`、`renameGroup`、`renameChannel`、`setColor`、`resetColor`、`setVisible`、`fitY`。
- 模块夹紧/交换/回退后写回输入状态；值未变不写，避免回读回声。
- 时间基：`rate = useCustomRate && customRate > 0 ? customRate : 活动组声明速率`；自定义频率是模块全局设置，不按组保存。
- X 轴单位只由 `xAxisMode` 决定：Index 为样本个数，Time 为秒。X 起/止/刻度/跟踪范围都按此单位显示和解析；历史上限仍是样本个数。
- 时间模式：`time = sample / rate`；速率未知且关闭自定义频率时强制回数据个数并写状态。
- X 刻度以当前轴单位保存；切换 Index/Time 时按有效速率换算一次，0（自动）不换算。

### 7.4 保存

- **局部保存**：当前 X 范围；CSV 宽表 `sample_index,time_seconds,<channel names>`；选择/全部都按绝对样本索引；NaN 写空。
- **全部保存**：活动组 `[0,latest]`。
- **PNG**：模块请求路径 → 视图 `CapturePng()` → 写盘 → `imageResult` 回投；只捕获图表本身。
- 保存格式只有 CSV/PNG；JPG 不作为本轮范围。
- 大数据量分批流式写；状态显示进度；导出期间继续接收。

### 7.5 布局与交互

- 内容尺寸 960×580（XAML Initial/Min，两个独立事实；外框 chrome 另加）；无顶行标题；主区：图表 `*` + 右栏组标题列表（200）；图表下方数据轴（32）；底部功能栏固定 86 高 `Canvas`（3 行）；状态行 14。
- 右栏：组标题栏（编号/名称粗体/数据个数/实际声明帧率/展开箭头）；活动组 U 凸起，同时只展开活动组；组名可编辑；通道行 20（颜色、可编辑名称、A/B 读数、隐藏 `ToggleButton`、Y 自适应 `ToggleButton`）；内部滚动。
- 数据轴：`RangeSelector` 单 X 范围；轨道占满自身控件宽度（仅 1 DIP 描边内缩），不复制图表 `PlotInset.Left` 的 Y 轴预留；1 px 选择边界 + 半透明填充 + 主/次刻度 + 主刻度数字；不再画粗手柄。轨道/刻度/数字全用主题 token（浅色主题近白底、深色主题随主题），不写死白/黑。
- 功能栏 86、固定坐标 `Canvas`、3 行：
  - X 行：X 轴模式、X 刻度、X 起、X 止、跟踪、跟踪范围、自定义频率开关+输入。
  - Y 行：Y 刻度、Y 最小/最大、Y 自动；右侧保存按钮。
  - 第三行：复位、清空、游标、保留非活动组历史、历史上限、已用数量。
- X 轴相关输入不再有独立单位标签；`AxisModeSelect` 的“数据个数/时间(s)”就是单位提示。历史上限仍按样本个数，去掉单独“个”标签。
- 自定义频率是**模块全局**开关/输入，不按组保存；开启后忽略帧头声明速率。
- 状态行 14：模块状态 + “N 点 · 显示 a–b”。
- 所有范围相关控件同源；通道颜色/改名/显隐就地更新，不重建整卡。
- 颜色：首次数据出现时从 `ColorPicker.SuggestedColors` 自动分配一次并持久化；色块打开框架调色窗口；恢复自动色清空覆盖并重新分配。删除自建 `ChartPalette`/`ModuleColorPalette`/`ChartSeriesBrushN`。
- 紧凑：单层边框、内边距 2–4、组卡分隔线、图表小留白；图表 `Margin=2`，底部只留 x 刻度文字最小带（`PlotInset.Bottom=20`）；以截图人眼复核。
- 指针：选择块内部 `SizeAll`，左右边缘 `SizeWestEast`，其他区域默认。

## 8. 图表与图标选型

- 自研 `ChartSurface`/`RangeSelector`；不引入 ScottPlot/OxyPlot/LiveCharts；无 SkiaSharp 依赖。
- 图标用显式 SVG + `IconCatalog`；跟随主题；不引图标包。波形图标统一 20×20、`fill="none"` 单线条；`Icon` 对这类资产只描边、不填充，线宽由控件统一（1.8），不读 SVG 的 `stroke-width`。
- 曲线颜色不再使用内置七色；自动色来自框架推荐色（`IModuleColorPicker.SuggestedColors`，与调色窗口同源），用户覆盖颜色优先。
- 图表无颜色时的兜底是主题强调色，不是数据身份色板。

## 9. 实施顺序与验证

| 阶段 | 内容 | 验证 |
|---|---|---|
| 0 | 本设计冻结；状态回写 | 文档评审 |
| 1 | `SeriesWindow` 快照源（Sdk/Core/Ui/Testing） | T0；T1 快照读写 |
| 2 | `ChartSurface` 全量点/XEnd/参考线/紧凑；`RangeSelector` 单范围 | T0；T1 数学；截图人眼 |
| 3 | 显示器双范围/全量快照/保存/活动组修复 | T0；T1 范围/快照/CSV |
| 4 | 显示器视图：组标题/通道行/数据轴/X/Y 输入/紧凑 | T0；截图人眼 |
| 5 | 发生器：SPWM/SVPWM/组启用/发送状态/预览快照 | T0；T1 数学/发送 |
| 6 | 发生器视图：调制行/启用摘要/预览/紧凑 | T0；截图人眼 |
| 7 | 游标外置/统一格式、数据轴刻度、框架取色、显示器隐藏/Y 锁存 | T0；T1；截图 |
| 8 | 发生器旧 UI chips、组频率/分辨率归属、三列参数块（按波形隐藏）、按钮化发送、固定坐标 Canvas；显示器单位/跟踪/自定义频率 | T0；T1；截图 |
| 9 | 端到端联调、T3、手工验收、文档回写 | T3 + 人工验收 |

## 10. 测试与验收要点

- **契约**：两模块同类型/同形状/golden/280 B；`ModuleContract.Probe` 无 Problems/Advisories。
- **帧往返**：写入→读取；1/64 通道、mask、rate=0、sequence 回绕；畸形帧拒绝。
- **快照**：X 范围宽度 = `Values.Length`；首/中/末值对应历史索引；无 256/抽稀；Y 有效值与范围一致；局部保存只含 X 范围；全部保存含历史；Y 不参与。
- **范围**：夹紧/交换/最小跨度；宽度意图保持跟踪，移动意图关闭跟踪；跟踪开关是唯一显式状态设置；切组/清空/复位同步；活动组不抢占。
- **单位/速率**：Index/Time 切换时 X 起止/刻度/跟踪范围协同换算；自定义频率全局覆盖帧声明速率，关闭后回落；CSV 使用同一有效速率。
- **发生器**：组帧率/一次性帧数；重复一次性从相位 0；序号继续；结构变化停止；禁用组不发送；Nyquist 拒绝并说明；SPWM/SVPWM 每个参数改变输出；连续按钮启动/停止；组选中 `activeChannelIndex=-1`；组/通道 chips 加删。
- **显示器交互**：双通道默认全部可见、隐藏/恢复；Y 自动只增不减，`fitY` 才缩；颜色首次自动分配、重置后重新分配；隐藏按钮读取当前状态而非旧闭包。
- **窗口尺寸**：XAML 根 `InitialWidth/InitialHeight` 是实例化默认尺寸，`MinWidth/MinHeight` 是运行期下限；外框 = 内容 + chrome；ModuleDefinition 尺寸不参与；摆放/还原/默认尺寸各有独立口径。
- **数据轴**：选择块 1 px 边界；主/次刻度与主刻度数字；`XAxisKind` 标签格式；轨道/刻度/数字全部主题 token，不写死白/黑；内部/边缘指针形状不同。
- **控件数学**：nice tick/像素映射/游标命中；选择器值映射/拖动/夹紧。
- **组合事实**：图表不知道选择；选择器不知道图表；同一范围只由模块写。
- **呈现**：截图 + 人眼；不写像素断言。截图覆盖图表/选择器/发生器/显示器/SPWM。
- **手工验收**：互连、参数、连续/周期发送、高频提示、组/通道、组分辨率、X/Y 范围、X/Y 输入、游标、数据轴、CSV/PNG、索引/时间、单位协同、跟踪语义、自定义频率、历史开关、清空、错误、最小窗口、紧凑布局。

## 11. 风险与边界

- 全量快照/全量绘制在超大范围下内存与 CPU 成本高；状态显示点数/更新率，用户可缩小范围；不静默抽稀。
- 跟随重建大范围按约 20 Hz；不是丢点，是更新节奏。
- 框架新增 `SeriesWindow` 是通用序列窗口契约，不是波形专用；旧 `Sequence` 路径保留。
- 载波形状固定对称三角波；死区/锯齿载波若需要，需另立参数与控件。
- 组名/组 ID/组频率/组周期的生成器编辑在组段；显示器组名可编辑。
- 框架取色服务注入链（Sdk.Ui → Ui → FrameworkUi → App）是新的模块视图接口面；设计期/测试必须允许 `ColorPicker=null`。
- JPG、FFT、跨重启 X 范围持久化不在本轮。

## 12. 相关

- 数据帧约定：[`waveform-frame.md`](waveform-frame.md)
- 数据面：`docs/zh/concepts/data-plane.md`
- 契约声明：`docs/zh/module-dev/contracts.md`
- 控件：`docs/zh/module-dev/controls.md`
