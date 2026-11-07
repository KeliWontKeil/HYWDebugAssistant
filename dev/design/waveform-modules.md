# 波形显示器与波形发生器模块设计

> 状态：**设计文档，待评审**。评审通过后再进入实现；本文不含实现代码。
>
> 数据帧约定已定稿：[`waveform-frame.md`](waveform-frame.md)（`hyw/waveform-frame`，280 B）。本文只设计两个模块、配套控件与它们之间的组合方式。

## 1. 定位与范围

**目标**

- `hyw/waveform-generator`：多组、多通道、波形参数、1–2 周期预览、连续发送、按周期一次性发送。
- `hyw/waveform-display`：组/通道发现、波形绘制、双游标读数、独立范围选择、跟踪、动态历史、CSV/PNG 保存。
- 在 `Sdk.Ui` 增加可复用的通用控件：`ChartSurface`（绘图 + 游标）、`RangeSelector`（线性范围选择）、`ModuleSelect`（选项下拉）。

**不做**

- 不改变数据帧约定；不新增框架级“波形数据投递/视图”契约。框架仍只负责把 280 B 帧投递到端口。
- 不引入第三方图表包、图标包。
- 不规定模块内部历史的内存上限；只提供计量、开关、可选上限和清空。
- 不实现数据帧的自动路由/过滤；组/通道匹配、消费、丢弃全部在模块内。

## 2. 现状约束

| 事实 | 设计后果 |
|---|---|
| 框架传输期不读 payload，只按端口投递 | 组/通道匹配、历史、投影全在模块内 |
| 视图只读已声明的显示源；`ReadSeries` 最多 256 点；环形缓冲默认 512 | 显示器用“64 个 F32 序列源 + 元数据/状态/索引源”送视图；模块自己抽稀到 256 点 |
| 显示源/输入/命令静态声明；`ModuleTabs` 静态；配置 Schema 只识别标量 | 动态组/通道 UI 模块自建；复杂配置用模块自有记录 + 自定义视图 |
| 模块定时器落后时跳过整拍，不补发 | 低频逐帧；高频按时间片批量发多帧并提示；实际时序不保证 |
| 模块只引 `Sdk`/`Sdk.Ui`/`Sdk.AccessKit`；无第三方控制依赖 | 图表/选择器自研；PNG 用 Avalonia `RenderTargetBitmap`；保存格式只有 CSV/PNG |
| 内存无统一硬上限 | 历史分块动态增长；默认全组保存；提供非活动组开关、可选上限、实时占用 |

## 3. 总体架构

```text
发生器配置（组/通道/周期数）
   ↓ 每组定时器（低频逐帧 / 高频时间片批量）
波形数学 → 280 B 帧写入器 → out 端口
   ↓ 数据面（只搬运）
in 端口 → 帧读取/校验 → 每组状态 / 动态历史
   ↓ 活动组、可见通道、抽稀
64 个 F32 序列源 + meta/status/latestIndex/stride/rate/generation
   ↓ 模块自己的视图 Refresh()
ChartSurface（绘图 + 游标）    RangeSelector（范围选择）
        ↘                    ↙
         显示视图：视口/跟踪/读数/保存（唯一组合根）
```

**模块标识**

| 项 | 发生器 | 显示器 |
|---|---|---|
| 模块 ID | `hyw/waveform-generator` | `hyw/waveform-display` |
| 包名 | `waveform-generator` | `waveform-display` |
| 端口 | `out`（Send，Bottom） | `in`（Receive，Top） |
| 角色 | `generate` | `display` |
| 窗口 | 约 800×420，Min 720×360 | 约 960×560，Min 760×460 |

## 4. 控件抽象与单一事实源

### 4.1 功能分解

| 抽象功能 | 控件/位置 | 负责 | 不负责 |
|---|---|---|---|
| 多序列绘图 | `ChartSurface` | 坐标轴、网格、曲线、游标线/标签、游标交互 | 范围选择、跟踪策略、保存、组/通道、数据历史 |
| 图形读数 | `ChartSurface`（内聚） | 游标屏幕位置、A/B/Δ 标签、与坐标映射同源 | 每通道 y 读数（视图算）、业务语义 |
| 线性范围选择 | `RangeSelector` | 域/视口窗口/选择块的显示与拖动 | 绘图、数据、刻度标签、跟踪 |
| 选项选择 | `ModuleSelect` | 固定选项的下拉选择 | 动态数据、业务命令 |
| 组页签/通道列表/图例/色板/参数表单 | 模块自建 | 波形业务的具体编排 | 进 `Sdk.Ui` 的通用能力 |
| 跟踪/视口/保存 | 显示视图 + 模块 | 策略、命令、文件 | 控件内部状态 |

**为什么游标留在图表**：游标需要与绘图区坐标映射、序列读取同步；拆出去要么复制视口/序列，要么让两个控件互相引用，耦合更高。

**为什么范围选择独立**：范围选择只需要一个线性域和选择状态，不需要序列/绘图；独立后可复用于任何线性范围，也不会让图表背上“选择/保存”的业务状态。

### 4.2 `ChartSurface`

| 成员 | 说明 |
|---|---|
| `Series: IReadOnlyList<ChartSeries>` | 视图创建一次并复用 |
| `DataVersion` | 数据内容失效号；`AffectsRender` |
| `XStart`、`XStep` | 首点 x、间距（视图换算好的 Index/Time 单位） |
| `XAxisKind: Index / Time` | 刻度/标签格式 |
| `XAxisTitle`、`YAxisTitle` | 模块本地化文本 |
| `YMin`、`YMax` | 视图算好的 y 范围 |
| `XTickStep`、`YTickStep` | 0/空 = 自动 nice tick |
| `ShowCursors`、`CursorAFraction`、`CursorBFraction` | 游标显示与屏幕比例位置 |
| `PlotArea: Rect`（只读） | 绘图区实际矩形；供视图对齐选择器 inset |
| `CapturePng()` | 当前图表 PNG 字节；失败 null |
| 事件 `CursorChanged` | 视图同步读数/按需持久化 |

- **没有**选择状态/选择条/选择事件；选择归 `RangeSelector`。
- `ChartSeries`：`Name`、`Color: Color?`、`Values: double[]`、`Count`、`Visible`；`Color=null` 按索引取主题曲线色板。
- 绘制：绘图区、网格、x/y 刻度、曲线（NaN/Inf 断线）、游标线/顶部标签；空数据只画轴。
- 指针：只处理游标线/顶部标签拖动（6 DIP 容差）；夹紧。
- 主题：轴/文字/网格/游标走主题 token；新增主题曲线色板 token（浅/深）；设计时补预览兜底。
- 性能：无 LINQ；复用几何/文本缓存；只按 `DataVersion` 重绘。
- 极小尺寸：隐藏刻度，只画轴/曲线，不抛异常。

### 4.3 `RangeSelector`

| 成员 | 说明 |
|---|---|
| `Minimum`、`Maximum` | 可选域；调用方给（显示器 = 活动组历史范围） |
| `ViewportStart`、`ViewportEnd` | 当前视口窗口（浅色显示） |
| `SelectionStart`、`SelectionEnd`、`HasSelection` | 独立选择范围 |
| `InsetStart`、`InsetEnd` | 轨道左右 inset（DIP）；与图表绘图区对齐，不引用图表 |
| 事件 `SelectionChanged` | 选择变化；调用方换算成业务范围 |
| `ClearSelection()` | 清除选择 |

- 绘制：轨道、视口窗口、选择块、两端手柄；**不画刻度/标签**（避免与图表 x 轴重复）。
- 指针：拖两端改范围；拖中间整体平移；夹紧到 `Minimum..Maximum`；域为空/零跨度时禁用。
- 不读数据集、不知道波形/组/通道；任何线性范围都能用。
- 对齐：显示视图在 `Refresh` 读 `ChartSurface.PlotArea`，设置 `RangeSelector.InsetStart/InsetEnd`；单向数据，不是控件互引。

### 4.4 `ModuleSelect`

- `ComboBox` 薄壳/主题：`Options`（key + 本地化 label）、`SelectedKey`、`SelectionChanged`。
- 视图代码设置选项/选中项；变化时提交模块输入；不绑定动态源名。
- 用于发生器波形类型、显示器 X 轴模式等固定枚举。

### 4.5 事实归属（单一事实源）

| 事实 | 所有者 | 说明 |
|---|---|---|
| 活动组/通道数据、历史、`DisplayIndex` | 显示模块 | 唯一数据源 |
| 视口（x 模式、x 起止、窗口、y 范围） | 显示视图 | 由模块设置/跟踪策略计算，只读喂给图表和选择器 |
| 绘图区几何、游标屏幕位置 | `ChartSurface` | 图表独占；不复制到选择器 |
| 范围选择（start/end/hasSelection） | `RangeSelector` | 选择控件独占；图表不知道选择 |
| 跟踪策略 | 显示视图 | 只改视口；不改游标、不改选择 |
| 通道名称/颜色/显隐 | 显示模块配置 | 视图读取；图表只拿最终序列 |
| 保存动作 | 显示模块 + 视图 | CSV 模块写；PNG 视图捕获图表 |

- 控件之间不互相引用；显示视图是唯一组合根。
- 视口只由显示视图计算一次，分别设置给图表和选择器；两者不各自维护“当前窗口”。
- 图表不画选择块；选择只在选择器上显示。若将来要在绘图区高亮选择，再加独立只读高亮层，不把选择状态塞回图表。

## 5. 数据帧使用约定

- 类型：`hyw/waveform-frame`；`Stream`；280 B；形状/golden string 严格按 [`waveform-frame.md`](waveform-frame.md)。
- 两模块各自有 `WaveformFrameContract` 常量、`WaveformFrameWriter`/`WaveformFrameReader`；不做共享程序集。测试项目同时引用两模块做写入→读取往返。
- 写入：`channelCount`（1–64）、`payloadBytes=4×channelCount`、`groupId`（非 0）、`sequence`（每组独立递增/回绕，仅完整性）、`rateHz`（该组请求帧率）、`validMask`（启用通道）、样本区（禁用/未用槽清零）。
- 读取：校验 `channelCount`/`payloadBytes`/`groupId`/`validMask`；失败丢整帧并计数；`rateHz` 非有限正数视为未知；不把 `sequence` 用于时间/样本位置。
- **组帧率**：每组共享一个帧率 = 所有启用通道 `resolution × frequency` 的最大值。低请求帧率的通道被过采样；若需要每通道独立帧率，应拆成多个组或未来新类型。
- 组名不传输：显示端默认“组 &lt;groupId&gt;”，本地改名；发生器 UI 显示组 ID 供对照。

## 6. 波形发生器设计

### 6.1 配置模型

```text
GeneratorConfig
  Groups: List<GeneratorGroupConfig>
  OneShotPeriods: int = 5
  ActiveGroupIndex, ActiveChannelIndex
GeneratorGroupConfig
  GroupId: uint (1..uint.Max，模块内唯一)
  Name: string
  Channels: List<GeneratorChannelConfig>
GeneratorChannelConfig
  Name: string
  Kind: Sine | Square | Triangle | Noise
  FrequencyHz: double (>0)
  PhaseDegrees: double
  Offset: double
  Amplitude: double
  Resolution: int (2..4096，每周期点数)
  DutyCycle: double (方波用，0.01..0.99)
  Enabled: bool
```

- 上限：8 组、64 通道/组；名称 ≤32 字符；组 ID 模块内唯一。
- copy-on-write：编辑构造新 record/新列表，`IConfigurationAccess.Write<T>` 整体写回；不就地改原对象。
- `Continuous` 不持久化；`OneShotPeriods` 持久化。
- 默认：1 组（ID 1，1 通道，正弦 10 Hz、Resolution 100、幅值 1、offset 0、启用），发送 5 周期。
- 复杂配置不走自动 Schema 表单；模块窗口是唯一编辑器。

### 6.2 一次性发送：按周期

用户输入 **N 个周期**。对每组 g：

- `E` = 该组启用通道集合；`E` 为空则跳过该组。
- `f_min = min(f_i)`（最慢通道频率）。
- `groupRate = max(R_i × f_i)`（组帧率）。
- 所需总帧数 `frames_g = ceil(N × groupRate / f_min)`。
- 数据总长度 `bytes_g = frames_g × 280`。
- 所有启用通道至少覆盖 N 个周期；最慢通道至少 N 个周期（向上取整后可能略多于 N），更快通道覆盖更多周期。
- 若所有通道同频，`frames_g = N × max(R_i)`（最大分辨率通道恰好 N 周期）。
- 各组的 `frames_g` 独立；一次性发送启动后各按自己的帧数发完；全部组完成即结束。
- UI 显示：`发送周期数 N`、当前组预计帧数/字节、全部组总帧数/总字节。
- 示例：单通道 f=10 Hz、R=100 ⇒ groupRate=1000 Hz；N=5 ⇒ frames=500、bytes=140000；两通道 f=10/R=100 与 f=50/R=100 ⇒ groupRate=5000，f_min=10 ⇒ frames=500N，慢通道 N 周期、快通道 5N 周期。
- 一次性发送期间改参数：总帧数冻结；波形参数对后续帧生效；增删组/通道停止发送。

### 6.3 发送调度：低频逐帧，高频时间片批量

- 每组一个 `IModuleTimer`。
- **低频（组帧率 ≤1000 Hz）**：`Interval = 1/groupRate`，一次 Tick 发一帧；保证低频下相对稳定的节奏。
- **高频（>1000 Hz）**：`Interval = 1 ms`，一次 Tick 发多帧：
  - `due += groupRate × 0.001`；`count = floor(due)`；`due -= count`；
  - 单 Tick 上限（如 1024 帧）防失控；
  - 状态显式提示：“高频批量发送：1 ms/批，约 k 帧/批；不保证逐帧时序”。
- 这就是“波形发生器”而非“信号发生器”：保证波形形状与采样序列，不保证硬实时。
- 限制：单组 ≤10 kHz；模块总请求帧率 ≤40 kHz；超限不静默降频，状态提示减少组/通道或降低分辨率。
- 连续发送：启动各组定时器；`Stop` 停止全部。
- 一次性发送：启动时按 §6.2 计算每组总帧数；每 Tick 减剩余；发完停该组；全部完成停全部。
- 每帧 `sequence` 递增/回绕；`TrySend` 失败计数并写状态，序号仍递增（接收端看到完整性缺口）。
- 定时器由框架暂停/恢复；恢复后重新同步，不补发。

### 6.4 波形数学

- 时间 `t = sampleIndex / groupRate`；相位 `φ = 2π(f·t + phase/360)`。
- 正弦 `sin φ`；方波按 `frac(φ/2π) < duty ? +1 : -1`；三角波线性折线；噪声用每通道确定性伪随机（种子由组 ID/通道索引派生）。
- 输出 `offset + amplitude × shape`；f32 写入；允许任意有限值，不强制限幅。
- 预览：按选中通道参数算 1–2 个周期，点数 `min(2×resolution, 256)`；清空 `preview` 后发布；不经过端口。
- 参数校验：频率/周期 >0；resolution 2–4096；duty 0.01–0.99；组 ID 非 0 且唯一；非法编辑不生效，保持旧值并写状态/Issue。

### 6.5 UI 与契约

- 左：`ChartSurface` 预览（约 280×220）+ 当前通道标题。预览只画曲线/轴，不开游标、不挂范围选择器。
- 中：组 Tab 条（模块自建 `GroupTabs`，横向滚动、选中高亮）+ 通道列表（模块自建行）+ 参数表单（波形类型、频率、周期、相位、offset、幅值、分辨率、占空比、启用）。
- 右：发送控制（连续开关、发送周期数、预计帧数/字节、发送一次、停止）。
- 底部：状态行 + 组/通道声明帧率/批量提示。
- 显示源/输入/命令：
  - `preview`（F32 Sequence）、`snapshot`（Text JSON，组摘要 + 活动组通道）、`status`（Text）。
  - 显示/输入：`groupName`、`groupId`、`channelName`、`waveform`、`frequency`、`period`、`phase`、`offset`、`amplitude`、`resolution`、`duty`、`channelEnabled`、`oneShotPeriods`、`continuous`。
  - 输入：`action`（JSON：选择组/通道等带参操作）。
  - 命令：`addGroup`、`removeGroup`、`addChannel`、`removeChannel`、`sendOnce`、`stop`。
- 频率/周期联动：编辑任一，模块同步另一个并重发显示源。
- 所有 UI 文案走模块 i18n；动态行用 `Tr`。

## 7. 波形显示器设计

### 7.1 动态历史

```text
GroupRuntime
  GroupId, ChannelCount, DeclaredRateHz, ActualRateHz
  LastSequence, ReceivedFrames, LostFrames, LastSeen
  DisplayIndex: long（模块自己的计数，可清零）
  Channels[]: 动态 ChannelHistory（分块 f32）
```

- **动态增长，无固定样本上限**：每通道用分块数组（如 8192 样本/块）追加；O(1) 追加，块满才分配下一块；不做逐帧 `List<float>` 扩容。
- **默认保存所有组历史**（`KeepInactiveHistory = true`）：每个组都追加样本；切换组保留历史。
- **非活动组保存开关**（默认开）：
  - 关：非活动组只解析头部（组 ID/通道数/速率/序号/丢失统计），不提取/保存样本；切换组时释放前一个活动组的全部历史；新活动组从空历史开始，`DisplayIndex` 归零。
  - 开：所有组动态增长；切换保留；新活动组用已有历史播种投影。
- **内存上限（可选）**：`HistoryLimitMB` 默认 `0 = 不限`。大于 0 时达到阈值后停止追加新样本但保留已有，状态提示“历史达到上限，已停止保存；可清空或提高上限”。
- **内存读数**：每组样本数/估算字节、总占用，显示在状态/组卡片；`clear` 释放。
- **CSV 导出**：大数据量时不在一次 `OnStep` 内写完；采用分批流式写（每次写固定行数，跨 Tick 继续），状态显示进度。导出期间继续接收；同一时间只允许一个导出。
- **清空**：清空活动组历史并把 `DisplayIndex` 归零；这是“可清零计数”的落点。若只想重置计数而保留历史，v1 不提供（会导致索引与历史错位）。
- 活动组通道数变化：重置该组历史/投影/选中索引，状态提示；非活动组只更新元数据。

### 7.2 历史与投影

- 历史按组/通道独立；`DisplayIndex` 每收到一帧 +1；`sequence` 只更新丢帧/重复/乱序统计。
- 投影只取活动组、可见通道；抽稀到 ≤256 点；`stride` 由窗口、时基/索引模式计算。
- 活动组切换/清空/窗口/步长/速率变化：清空 64 个序列源，`generation++`，从历史尾部播种最多 `256×stride` 点，再按新步长增量发布。
- 跟踪开启：持续发布最新抽稀样本；跟踪关闭：停止发布，图表保持。跟踪与游标、范围选择互不控制。
- 非活动组保存关闭时，切换后新活动组无历史，投影从空开始。

### 7.3 显示投影源

| 源 | 类型 | 含义 |
|---|---|---|
| `ch0..ch63` | F32 Sequence | 活动组可见通道样本（抽稀后） |
| `meta` | Text JSON | 组摘要 + 活动组通道（名称/颜色/可见）+ 选中项 + 设置 |
| `status` | Text | 状态/丢帧/错误/保存/历史上限/批量提示 |
| `latestIndex` | Value U64 | 当前活动组 `DisplayIndex` |
| `stride` | Value U32 | 当前抽稀步长 |
| `rate` | Value F32 | 时间轴有效速率（用户时基或声明速率） |
| `generation` | Value U64 | 活动组/清空/投影重建代次 |
| `groupName`、`channelName`、`channelColor`、`channelVisible` | Text/Bool | 选中组/通道编辑器 |
| `window`、`yMin`、`yMax`、`yAuto`、`xAxisMode`、`timeBase`、`xTick`、`yTick`、`follow`、`cursors` | Text/Bool | 图表设置 |
| `imageRequest` | Text | PNG 保存路径/请求号（模块→视图） |

输入：同名编辑源；另有 `action`（JSON 结构操作/选择）、`savePath`（文件选择回投）、`imageResult`（PNG 写盘结果）。
命令：`clear`（清数据+索引）、`resetGroup`（恢复显示默认）、`saveSelection`、`saveAll`、`saveImage`。

### 7.4 布局

- 主区：`Grid` 两行：`ChartSurface`（`*`）+ `RangeSelector`（固定高度约 18–22）。
- `RangeSelector` 的轨道左右 inset 由显示视图按 `ChartSurface.PlotArea` 设置；选择器不画 x 刻度/标签，x 刻度只由图表画。
- 右栏约 240：组卡片列表；卡片标题 = `编号/名称/通道数/实际/声明速率`；点击激活，`⌄` 展开通道列表，同时只展开活动组；活动卡片凸起/高亮。
- 通道行：颜色块、可编辑名称、可见按钮、功能按钮；行高固定，内部滚动。
- 底部功能栏：
  - 第 1 行：窗口长度 + 单位、y 自动/手动 + y 范围、x 刻度/y 刻度、跟踪开关、游标开关、X 轴 `索引/时基` 切换、时基输入。
  - 第 2 行：清空、参数复位、保存选择、保存全部、历史占用/状态。
- 顶部/图例：游标 A/B/Δ 与每通道读数。
- 颜色：默认取主题曲线色板；点击色块弹 12 色高对比色板；写回配置。
- 组名/通道名：输入框失焦/回车提交；非法/空名不生效并提示。

### 7.5 索引/时基、游标、范围选择、跟踪

- **X 轴模式** `Index` / `Time`：
  - `Index`：x = 模块自己的 `DisplayIndex`，从 0 开始，`clear` 归零；不是 `sequence`。
  - `Time`：x = `DisplayIndex / timeBase`；`timeBase` 用户显式设置；为空/0 时回落该组声明 `rateHz`；仍未知则退化为 `Index` 并提示。
  - 切换只改标签/映射，不改数据；设置持久化。
- **游标**：
  - 两个游标 A/B 是屏幕比例位置；不暂停、不关闭、不改变跟踪；只负责“读当前图”。
  - 顶部显示 A/B/Δ 的 x 值（按当前模式）；图例/读数区显示各可见通道在 A/B 处的 y 值；跟踪滚动时读数随图更新。
  - 游标开关只控制显示；拖动只改位置。
- **范围选择（`RangeSelector`）**：
  - 域 = 活动组当前历史范围（`0..latestIndex`）；视口窗口 = 当前图表窗口；选择 = 独立 start/end。
  - 轨道显示域；浅色窗口显示当前视口；选择块可拖两端/整体；选择事件给显示视图，换算成样本索引，供“保存选择”。
  - 选择与跟踪、游标互不控制：跟踪继续、游标继续；用户需要看某段时自行关跟踪。
  - `RangeSelector` 不画刻度标签；不读数据集；只接收 `Minimum/Maximum/ViewportStart/ViewportEnd/SelectionStart/SelectionEnd`。
- **跟踪**：开启时窗口跟随最新；关闭时冻结；只改视口。游标和选择不受影响。
- **y 轴**：自动取可见通道窗口极值 + 5% 边距；手动用输入；全等值时加最小跨度。
- **刻度**：空 = 自动 nice tick；用户填写则使用；标签过密时按像素减少。
- 空数据/无可见通道：画轴 + 状态提示，不画假曲线。

### 7.6 保存

- **CSV**：`saveSelection` / `saveAll` → `IFileAccess.Request`（只列 `CSV (*.csv)|*.csv` 和 `PNG (*.png)|*.png`，按扩展名决定格式）。CSV 宽表 `sample_index,time_seconds,<channel names>`；选择范围来自 `RangeSelector`，按绝对样本索引；NaN 写空。大数据分批流式写，状态显示进度。
- **PNG**：模块请求 PNG 路径 → 写 `imageRequest`（请求号/路径）→ 视图调用 `ChartSurface.CapturePng()` → `File.WriteAllBytes` 写用户选定路径 → `imageResult` 回投 → 模块写状态。
- PNG 只捕获图表本身；不含范围选择器/底部栏。若将来要“连数据轴一起导出”，由视图捕获一个组合面板，另开需求。
- 保存格式只有 CSV 与 PNG。

## 8. 图表与图标选型

### 8.1 第三方图表 vs 自研

候选：

- **ScottPlot 5.1.59+**：MIT；2026-06-22 的 5.1.59 已加入 Avalonia 12 支持；信号图、坐标轴、交互 plottable、PNG 导出。需要 ScottPlot + SkiaSharp 3 依赖，主题/模板/游标/选择仍需二次集成。
- **OxyPlot.Avalonia12 2.1.2**：MIT；社区适配包，目标 .NET 10 / Avalonia ≥12.0.2；成熟绘图、标注、导出。维护者单一、包较新；仍要 SkiaSharp 导出和自定义交互。
- **LiveCharts2（Avalonia）**：MIT；2.0 RC；偏动画/通用图表，游标/选择/批量流式不是强项。
- **自研**：无依赖；现有自绘/主题/模块视图机制直接复用；需求窄（多序列、轴、两游标、读数；不做缩放/平移/大数据绑定），约 600–800 行可控。

参考：[ScottPlot changelog](https://scottplot.net/changelog/)（Avalonia 12 支持）、[Oxyplot.Avalonia12 NuGet](https://www.nuget.org/packages/Oxyplot.Avalonia12/2.1.2)。

**结论：自研**。理由：

1. 候选都带来新的图表/SkiaSharp 依赖链、包体与版本对齐成本；当前仓库无图表依赖，Avalonia 12 支持新近加入/社区适配。
2. 需要的是模块内嵌小型示波器交互；第三方库仍需自定义游标/范围选择/主题/导出，收益有限。
3. 自研保证主题 token、模块打包、PNG 捕获、无第三方模板挂载问题；用成熟思路实现 nice tick、裁剪、命中测试。
4. 若将来需要缩放/大数据/复杂统计，再评估 ScottPlot；当前不引。

### 8.2 图标

- 显式 SVG + 现有 `IconCatalog`；只需少量图标；跟随主题；不引图标包。
- 采用 Fluent UI System Icons（MIT）或自绘路径，按需更新 `THIRD_PARTY_NOTICES.md`。

## 9. 实施顺序与验证

| 阶段 | 内容 | 验证 |
|---|---|---|
| 0 设计评审 | 本文评审；通过后进入实现 | 文档评审 |
| 1 通用控件 | `ChartSurface` + `RangeSelector` + `ModuleSelect` + 主题色板 + 数学测试 + 合成截图；更新 controls 文档 | T0；T1 数学；T2；T3（`Sdk.Ui` 公开面）；截图人眼 |
| 2 模块骨架 | 两项目/module.json/i18n/视图工厂；两模块各自声明类型；契约 + 跨模块帧往返测试 | T0；T1 契约/帧往返 |
| 3 发生器核心 | 配置、波形数学、定时器（低频逐帧/高频批量）、按周期发送、帧写入、最小视图 | T0；T1 数学/调度/一次性 |
| 4 显示器核心 | 帧读取、组发现、动态历史、投影、最小视图（单组多通道 + 图表 + 选择器） | T0；T1 状态/历史/投影/丢帧 |
| 5 端到端切片 | 发生器→显示器；一组四通道；跟踪/游标/索引/时基/选择/保存；修桥接 | T0；受影响 T1/T2；起应用人眼 |
| 6 完整 UI/保存 | 多组/多通道/图例/颜色/参数联动/历史开关/CSV 流式/PNG/空态 | T0；T1/T2；截图人眼 |
| 7 收口 | 公共类型页/模块页/状态/验收；完整 T3；人工验收 | T3 + 人工验收 |

## 10. 测试与验收要点

- **契约**：两模块同类型/同形状/golden/280 B/Stream；`ModuleContract.Probe` 无 Problems/Advisories。
- **帧往返**：发生器写入 → 显示器读取；1/64 通道、mask、rate=0、sequence 回绕；畸形帧拒绝。
- **波形数学**：正弦/方波占空比/三角/噪声、相位/offset/幅值、频率×分辨率→组帧率、禁用通道。
- **一次性按周期**：`frames = ceil(N×groupRate/f_min)`、单通道 N×R、多通道慢者 N/快者更多、字节数。
- **调度**：低频一 Tick 一帧；高频 1 ms 批量 k 帧；连续/一次性停止；`TrySend` 失败计数。
- **显示历史**：组发现、活动切换、动态增长、非活动保存开关、`validMask` 断点、通道数变化重置、序号缺口只加丢帧、实际速率。
- **投影**：`stride`/`latestIndex`/`generation`、隐藏通道不发布、窗口变化重播种、索引清零。
- **保存**：CSV 选择/全部、表头、NaN 空、长历史分批写、进度/错误。
- **控件数学**：图表 nice tick/像素映射/游标命中；选择器值映射/拖动/夹紧。
- **组合事实**：图表不知道选择；选择器不知道图表；同一窗口只由视图设置一次。
- **呈现**：截图 + 人眼；不写像素断言。截图覆盖图表/选择器/发生器/显示器。
- **手工验收**：互连、参数、连续/周期发送、高频提示、组/通道、游标、范围选择、CSV/PNG、索引/时基、历史开关、清空、错误、最小窗口。

## 11. 风险与待评审项

**待评审确认**

1. **一次性发送的总帧数**按“最慢通道 N 周期”公式；若要求每个通道各自恰好 N 周期后分别停，当前单帧多通道模型无法同时表达，需要拆组或改类型。
2. 组帧率 = 启用通道 `resolution × frequency` 最大值；同组共享帧率。
3. 发生器上限：8 组、64 通道/组、单组 10 kHz、总请求 40 kHz。
4. 低频逐帧；高频 1 ms 时间片批量发多帧，显式提示，不保证逐帧时序。
5. 显示器默认保存所有组历史；动态增长无固定样本上限；非活动组保存开关默认开；可选内存上限默认 0=不限。
6. 历史“关闭非活动保存”时，非活动组只解析头部，不保存样本；切换时删除前一个活动组数据，新活动从空开始。
7. 索引是模块自己的 `DisplayIndex`，不是 `sequence`；清空同时清数据与索引；v1 不做“只重置索引保留数据”。
8. X 轴 `Index/Time` 切换；时间 = `DisplayIndex / timeBase`，时基为空/0 时回落声明速率；仍未知则退化 Index。
9. 游标归图表，范围选择归 `RangeSelector`，跟踪归视图；三者互不控制。选择条不内置到图表，图表不画选择块。
10. `RangeSelector` 是通用线性范围选择控件，不叫“波形数据轴”；任何线性范围都可复用。
11. 图表和选择器不互相引用；视口由显示视图计算一次并分别设置；选择器 inset 由视图按 `ChartSurface.PlotArea` 单向传入。
12. 保存只支持 CSV/PNG；PNG 只捕获图表本身。
13. 图表自研；图标用显式 SVG；不引第三方图表/图标包。
14. 复杂配置用模块自定义视图 + 自定义记录；自动 Config Schema 表单不覆盖动态组/通道。

**风险**

- 高频批量发送导致接收端突发：显示端按声明/实际速率提示；不承诺逐帧时序。
- 动态历史无限增长可能 OOM：提供占用显示、可选上限、非活动开关、清空；默认不限，由用户管理。
- 大 CSV 导出阻塞接收：分批流式写，状态进度；导出期间继续接收。
- 动态 UI 重建导致焦点/展开丢失：快照 revision 变化只重建结构，值更新不重建；编辑中的控件让路。
- 64 序列源 × 256 点视图读取成本：只读可见通道；抽稀；必要时关非活动保存/减通道。
- 图表/选择器对齐：用 `ChartSurface.PlotArea` 单向设置 inset，不复制边距；若布局变化，下一帧同步。
- 组 ID 冲突/同组多源：类型不携带来源；同组 ID 多发送者会混合，文档明确“必须不同 ID”。
- `Sdk.Ui` 公开面扩大：T3、文档、包校验；不破坏既有控件。

## 12. 相关

- 数据帧约定：[`waveform-frame.md`](waveform-frame.md)
- 数据面：`docs/zh/concepts/data-plane.md`
- 契约声明：`docs/zh/module-dev/contracts.md`
- 控件：`docs/zh/module-dev/controls.md`（实现阶段补充新控件）
