# 终端显示模块（仪表盘 / 刻度条 / 三轴姿态）设计

> 本文是四个终端显示模块与配套控件的**唯一设计口径**。进度只在 [`../status.md`](../status.md) 维护；手工验收项在 [`../acceptance.md`](../acceptance.md)。数据帧约定见 [`waveform-frame.md`](waveform-frame.md)。

## 1. 定位与范围

**目标**

- `hyw/gauge-display`：方形窗口，把某一组的**一个通道**显示成指针表盘（红针）。
- `hyw/scale-bar-horizontal` / `hyw/scale-bar-vertical`：长条窗口，把**一个通道**显示成**矩形细条刻度条**（Windows 进度条观感，默认绿）。
- `hyw/attitude-display`：把某一组的**三个通道**当作横滚 / 俯仰 / 偏航显示三维姿态。
- `Sdk.Ui` 通用控件：`GaugeDial`、`ScaleBar`、`AttitudeView`，以及**浮层设置容器 `ModulePopupPanel`**。

**不做**：不改波形帧契约；不给控件新增色板 token；不引第三方渲染库；不做"发现到的组/通道列表"（编号由用户配置）；不持久化发现到的组/通道对象本身。

## 2. 模块拆分与共享边界

| 项 | 值 |
|---|---|
| 包名 / 模块 ID | `gauge-display`、`scale-bar-horizontal`、`scale-bar-vertical`、`attitude-display`（`hyw/…`） |
| 程序集 | `HYWDebugAssistant.Modules.<Name>`，目录 `src/Modules/Business/<Name>/` |
| 依赖 | 只引 `Sdk` + `Sdk.Ui`（AR-1）；模块之间不互引 |
| 窗口尺寸 | 只写在视图根 XAML（AR-18）：仪表 300×300，横向条 440×190，纵向条 180×420，姿态 440×480 |

**为什么横向/纵向是两个模块**：窗口形状不同，而窗口初始/最小尺寸只能由视图根声明一次；拆成两个模块比运行期切换布局更符合"尺寸只有一个事实"。

**为什么控件进 `Sdk.Ui`**：模块只能引用 `Sdk` / `Sdk.Ui`，四个模块要共用同一套绘制与平滑，唯一合法落点是控件库。帧解析与组/通道状态按仓库既有做法每模块一份（与 `WaveformDisplay` 同构），不为共享代码新增程序集。

## 3. 裁决

1. **目标值只有一条规则（唯一事实）**：样本新鲜 → 样本值；超过"回零延时"没有新样本 → 开"自动回零"给 0（姿态为单位姿态）、关则保持最后样本；没有样本（含通道不存在）→ 0。实现是 `ChannelFrameFeed.Resolve` 一处。
2. **控件没有"无数据"分支**：三个数据显示控件只画收到的目标值；"保持还是回零"由模块发布的目标值决定。
3. **平滑在控件、回零在模块**：只有模块知道最后一帧何时到；只有控件有每帧绘制节奏。控件按框架每帧 `Refresh` 的 dt 逼近，settle 后停止重绘，第一帧直接落到当前值。
4. **组/通道是用户输入的持久化值**：`input.group`（0 = 自动跟随第一个出现的组）、`input.channel`（0 起，与帧契约 `0..channelCount-1` 一致）；姿态为 `input.roll/pitch/yaw`。**不钳制**：通道不存在按"没有样本"处理（目标 0），因此用户能提前配置任意编号。
5. **设置界面是统一的"浮层面板标准"**：面板正文是一个独立视图（`Views/*SettingsPanel.axaml`，继承 `ModuleView`、自己声明尺寸），因此预览器能单独打开；模块视图只声明一个 `ModulePopupPanel`（正文类型 + 标题 + 恢复默认命令名）；容器开**无边框不透明独立窗口**（标题行 + 刷新 + 关闭）并负责定位、生命周期与逐帧刷新。详见 [控件文档](../../docs/zh/module-dev/controls.md) 的"浮层面板"一节。
6. **一个值一个编辑器**：视图 XAML 里同名值出现两个输入控件会让包加载失败（`DuplicateValue`）；渲染物这类设置只放在浮层面板里。
7. **角度单位属于输入解释**：姿态的"度/弧度"只影响读到的通道值如何换算，读数始终显示度；三轴反相只翻符号。

## 4. 契约

### 4.1 显示源与输入

| 名称 | 类型 | 方向 | 说明 |
|---|---|---|---|
| `value` | F32 单值 | 模块 → 视图 | 仪表 / 刻度条的目标值（永远是有限实数） |
| `roll` / `pitch` / `yaw` | F32 单值 | 模块 → 视图 | 姿态模块的三个目标角度（度） |
| `meta` | 文本 | 模块 → 视图 | 设置摘要 JSON；视图按变更号解析 |
| `status` | 文本 | 模块 → 视图 | 一句话状态；界面作为**悬停提示**，不占版面 |

没有 `action` 输入：结构选择已由值承担。

### 4.2 `meta` schema

```json
{
  "revision": 12,
  "settings": {
    "group": 0, "channel": 0,
    "min": 0, "max": 100, "autoZero": true, "zeroDelayMs": 800,
    "title": "", "unit": "", "smooth": true, "smoothMs": 120,
    "decimals": 2, "fillFromZero": true, "color": "green"
  }
}
```

- `group`/`channel`（姿态 `roll/pitch/yaw`）供模块外面那行**摘要**用；刻度条多 `fillFromZero` / `color`；姿态另含 `style/unit/zoom/grid/labels`。
- 视图只在变更号变化时解析并应用。

### 4.3 值

| 值名 | 编辑器 | 默认 | 说明 |
|---|---|---|---|
| `input.group` | Number | `0` | 0 = 自动跟随第一个出现的组 |
| `input.channel` | Number | `0` | 通道序号（0 起） |
| `input.roll` / `input.pitch` / `input.yaw` | Number | `0/1/2` | 姿态三通道 |
| `settings.minimum` / `settings.maximum` | Number | `0` / `100` | 量程 |
| `settings.autoZero` / `settings.zeroDelay` | Boolean / Number | `true` / `800` | 统一回零规则 |
| `settings.title` / `settings.unit` | Text | 空 | 标题 / 单位 |
| `settings.smoothing` / `settings.smoothingMs` | Boolean / Number | `true` / `120` | 平滑 |
| `settings.decimals` | Number | `2` | 小数位 |
| `settings.fillFromZero` | Boolean | `true` | 仅刻度条 |
| `settings.color` | Enum 5 档 | `green` | 仅刻度条（绿/蓝/橙/红/紫 → 主题 token） |
| 姿态：`settings.style` / `unit` / `zoom` / `grid` / `labels` / `invertRoll|Pitch|Yaw` / `lostZero` / `lostDelay` | 枚举/布尔/数字 | 见表 | 渲染物只在浮层面板里 |

- 命令 `resetSettings` 只重置 `settings.*`，不动 `input.*`；值被清空（模块"还原"）时按默认重新解析。
- 上下界互逆或数字非法：不应用该设置，`Values.Report` 一条结论，保留上一份有效设置；可归一化项夹紧后生效。

## 5. 浮层面板（标准）

**动机**：模块窗口会裁切视图，视图内浮层出不了模块窗口、还会挡住可视化；平台 `Popup` 的落位/翻转/是否落 overlay 层由平台决定，出现过贴在模块上的残留，透明弹窗还让设置项发灰。

**标准（模块只做前两件，其余归控件库）**

| 件 | 谁做 | 内容 |
|---|---|---|
| 面板正文 | 模块 | `Views/<Name>SettingsPanel.axaml`，继承 `ModuleView`，声明 `InitialWidth/InitialHeight`（表单区尺寸），带 `[ModuleViewDesignData]` ⇒ 预览器可单独打开 |
| 容器声明 | 模块 | 模块视图里一个 `ModulePopupPanel`：`PanelType` / `Title` / `ResetCommandName`；代码后置设置 `Anchor` |
| 外壳与窗口 | `Sdk.Ui` | `ModulePanelSurface`（标题 + 刷新 + 关闭）+ 无边框不透明独立窗口；右侧优先定位、屏幕内夹紧；Esc/关闭图标/模块窗口关闭或隐藏时关闭；内容包进内部 `ModuleViewHost` 按帧刷新 |

- 面板图标固定为 `icon.refresh` / `icon.close`，属主由容器按正文程序集给（正文在模块视图之外）。
- 关闭只是隐藏窗口，正文状态保留；再次打开复用同一窗口。
- 容器铺在模块视图最上层但不参与命中，不吃齿轮与控件的点击。
- 预览：模块视图里只有齿轮；面板正文单独预览，另有 `<模块>-settings.png` 单独截图。
- 不采用：视图内浮层（会被裁切）、`Popup`（平台行为不可控、透明弹窗发灰）。

## 5.1 仪表盘几何（口径修正）

- 半径必须同时满足两条：扣掉"环厚/2 + 边距"；张角 ≥ 180° 时按**半径本身**限宽（弧经过圆的左右极点，不是弧端点）。任一条漏算，环都会从左右被裁，且宽高比一变就"有时裁有时不裁"。算法收敛到 `GaugeGeometry.Compute` 一处，两轮迭代（半径↔环厚），富余高度上下均分。
- 端点数字与主刻度重合时只画一次，避免 `0..100` 的 `100` 重影。
- 不变量：环外沿在控件内且至少留 4 DIP；有单测钉住（不钉具体半径）。

## 5.2 跨实例自动分配组/通道（评估 = 不做）

需求：新建同类模块时沿用上一个实例的组、通道 +1。

- 模块之间不能互查（P12），每实例的值在创建时由 `ModuleRuntime.SeedValueDefaults` 从本模块包 XAML 初值播种，没有"按模块类型共享"的存储 ⇒ 只能在框架层加"播种策略"（`ValueDeclaration` 新策略 + `ModuleRuntime` 找同模块最近实例并末位自增）。
- 代价与语义面：SDK 公开面新增、创建路径改动、顺序/删除/还原/跨工作区/解析失败语义、文档与用例 —— 属于新的框架能力，超出本轮精修。
- 结论：不做；本轮用"面板内可编辑 + 外面常显摘要"缓解。
## 6. 控件职责

| 控件 | 只画什么 | 不做 |
|---|---|---|
| `GaugeDial` | `Value` → 弧线、刻度、红色指针、读数；超量程钉边界、读数转警示色 | 不读显示源、不解析设置、不判断无数据、不起定时器 |
| `ScaleBar` | `Value` → 矩形轨道 + 填充（`FillOrigin`）+ 刻度与数字；`BarThickness` 控制细条厚度；`Color` 角色映射 token | 同上；**不画标题/读数/单位**（由视图标签拼） |
| `AttitudeView` | 三个角度 → 固定世界 + 旋转机体的透视投影；`RenderStyle` 切绘制物 | 同上；不做单位换算 |

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
| 上下界互逆 / 非法数字 | 不应用，红框 + 悬停原因，保留上一份有效设置 |
| 超量程 | 指针/填充钉边界、读数警示色，数值不裁剪 |
| 极小窗口 | 控件退化为只画轴/轨道，不越界、不抛 |
| 窗口隐藏 | 框架不调 `Refresh`，动画停；再现时 dt 上限防止跳变 |

## 8. 待人工验收要点

见 [`../acceptance.md`](../acceptance.md) 终端显示一组条目（A121–A139）：加载不再报错、圆盘不被裁且无重影、摘要与面板内组/通道、面板独立窗口不透明且可放在模块外、预览不被面板挡、刷新图标、刻度条颜色与 Windows 观感、红针、单位生效、姿态渲染物切换。
