# 控件

本页是控件用法与关键属性的唯一清单；怎么把它们组合成一个视图见[界面](ui.md)。完整成员见[接口参考](reference.md)；作者包也带 XML 文档，IDE 里可直接看。

## 共同规则

- 控件全部来自 `HYWDebugAssistant.Sdk.Ui.Controls`，XAML 命名空间写 `xmlns:ui="using:HYWDebugAssistant.Sdk.Ui.Controls"`。
- 引用显示源、值、命令时，一律用模块自己的常量（`{x:Static}`），不写字符串字面量。
- 可编辑控件只绑**值名**：不知道值落在配置还是视图状态里，也不直接读写它们。
- 控件不写死颜色与字号：颜色取主题 token 或角色词，排印用样式类 `caption` / `hint` / `micro`。
- 控件由 `ModuleViewHost` 每帧驱动：**值没变就不碰视觉**，视图不自己起定时器、不在 `Refresh` 里重建控件树。
- **模块文字只有一套实现**：`ModuleLabel` 与 `ModuleNumeric` 都是 `TextBlock` 派生，直接继承换行、对齐、字号等属性；模块视图里不再用裸 `TextBlock` 显示文字。
- **开关不自带文字**：`ModuleToggle` 只画轨道与圆钮；标签是独立的 `ModuleLabel`，放在右侧。
- **勾选框自带文字**：`ModuleCheckBox` 的方框与文字间距固定 2 DIP，文字走 `Content`，不再另加 `ModuleLabel`；控件宽度按内容声明，不要用透明留白占位。
- **预览值写在 XAML**：可输入控件的初始值、只读控件的预览值直接写在控件自己的值属性上；运行期数据到达后覆盖。
- **文字在控件矩形内的垂直对齐**由 `TextVerticalAlignment` 决定（`Top` / `Center` / `Bottom`，默认 `Center`）；它与控件自身的 `VerticalAlignment` 是两个维度。

## 控件一览

| 控件 | 用途 | 关键属性 |
|---|---|---|
| `ModuleLabel` | 一句话：静态文字、配置字段标题、文本显示源 | `Text`、`Field`、`Source`、`TextVerticalAlignment`；样式类 `caption` / `hint` / `micro` / `right` / `wrap` |
| `ModuleNumeric` | 数值读数 | `Source`、`Quantity`（`None` / `Bytes`）、`Text`（预览值） |
| `ModuleStatusLight` | 状态灯：底色来自模块设的**角色词** | `Source`；配合 `Appearance.SetOutlineRole(Source, role)` |
| `ModuleTextSurface` | **唯一的文本/数字编辑器**：日志显示或可编辑值 | `Mode`、`Value`、`CommitMode`、`Source`（仅显示）、`Text`、`TextFormat`、`Multiline`、`IsReadOnly`、`ShowNewlineMarks`、`ShowControlCharacterMarkers`、`EscapeInput`、`ShowInputHint`、`TextAlignment`、`MinEditorHeight`、`Commit()`、`SubmitKeyValue`、`SubmitCommandName`、`EnterNewlineValue` |
| `ModuleButton` | 命令按钮（可选右侧小箭头菜单） | `CommandName`、`Content`、`Icon`、`IconOwner`、`Appearance`、`MenuValue` |
| `ModuleToggle` | 开关：勾选提交 `"true"` / `"false"` | `Value`、`CommitMode`、`IsChecked`、`CheckedChanged`（程序写回也触发） |
| `ModuleCheckBox` | 勾选框：方框 + 勾形 + 右侧文字（间距 2 DIP），提交 `"true"` / `"false"` | `Value`、`CommitMode`、`IsChecked`、`Content`、`CheckedChanged` |
| `ModuleTabs` | 页签容器，选中页写入视图状态 | `StateKey`、`TabItem` 子项 |
| `ModuleSelect` | **枚举下拉**（配置字段或普通值；选项来自 `ValueState.Choices`） | `Value`、`CommitMode`、`SelectedKey`、`PreviewOptions`；旧 `Options`/`OptionSelected` 仅供结构选择 |
| `ModuleColorField` | **颜色值编辑器**：色块，点开框架取色窗 | `Value`、`DefaultBrushToken`、`DialogTitle`、`ResetText`、`InitialColor` |
| `ModuleSegmentedControl` | 互斥分段选择（文字/图标，可禁用） | `Value`、`CommitMode`、`SelectedKey`、`PreviewOptions`、`ShowLabels`、`IconSize`；旧 `Options`/`OptionSelected` 供结构选择 |
| `ChartSurface` | 通用多序列绘图（全量点、X/Y 游标、参考线、PNG、左键平移/右键缩放/滚轮缩放） | `Series`、`DataVersion`、`XStart`、`XEnd`、`XStep`、`XAxisKind`、`YMin/Max`、`XTickStep/YTickStep`、`PlotInset`、`ReferenceLines`、`ShowCursors`、`ShowYCursors`、`WheelZoomStep`、`CapturePng()`、`PanRequested`、`ZoomRequested`、`WheelZoomRequested`；有无 Ctrl 都同时缩放 X/Y（模块负责在任一轴触界时整次拒绝） |
| `RangeSelector` | 通用线性范围可视化/拖动 | `Minimum`、`Maximum`、`RangeStart/End`、`MinimumRangeSpan`、`TickStep`、`ShowTicks`、`ShowTickLabels`、`MinorTickStep`、`XAxisKind`、`HandleWidth`、`RangeChanged`、`LastChangeKind` |
| `GaugeDial` | 指针仪表盘（单值、弧线刻度 + 指针 + 读数，可平滑） | `Minimum`/`Maximum`/`Value`、`Title`/`Unit`、`Padding`、`SweepAngle`、`ValueDecimals`、`MajorTickStep`、`MinorTickCount`、`ShowValue`、`FillOrigin`（`Minimum`/`Zero`）、`SmoothingEnabled`/`SmoothingTimeMs`、`DialBrush`/`TrackBrush`/`TickBrush`/`TickLabelBrush`/`NeedleBrush`/`HubBrush`/`TitleBrush`/`ValueBrush`/`UnitBrush`/`OutOfRangeBrush` |
| `ScaleBar` | 矩形细条刻度进度条（横向 / 纵向，Windows 进度条观感；**不含文字**） | `Orientation`、`Minimum`/`Maximum`/`Value`、`BarThickness`、`Padding`、`TickStep`、`MinorTickCount`、`ShowLabels`、`FillOrigin`（`Minimum`/`Zero`）、`FillBrush`（未覆盖时主题默认 `DefaultFillToken`）、`SmoothingEnabled`/`SmoothingTimeMs`、`TrackBrush`/`FrameBrush`/`TickBrush`/`TickLabelBrush` |
| `AttitudeView` | 三轴姿态（坐标轴 / 方块 / 无人机）；默认等轴测；左键拖动视角；左上角按钮/空格打开七视角面板；左下角地面参考轴 | `Roll`/`Pitch`/`Yaw`（度）、`RenderStyle`、`Zoom`、`ShowGrid`、`ShowLabels`、`SmoothingEnabled`/`SmoothingTimeMs`、`GridBrush`/`AxisXBrush`/`AxisYBrush`/`AxisZBrush`/`BodyBrush`/`BodyEdgeBrush`/`TextBrush`/`ViewSurfaceBrush`/`ViewAccentBrush` |
| `ModulePopupPanel` | 浮层面板容器（独立窗口里显示，可放在模块之外） | `PanelType`（正文视图类型）、`Title`、`ResetCommandName`、`Anchor`、`IsOpen`、`Open()`/`Close()`/`Toggle()` |
| `ModulePanelSurface` | 面板外壳（标题行 + 刷新/关闭 + 内容区），由容器创建 | `Title`、`ResetCommandName`、`IconOwner`、`CloseRequested`、`HeaderHeight` |
| `Icon` | 模块图标 | `IconKey`、`Owner` |
| `ModuleView` | 视图根（推荐） | 继承 `UserControl`；`InitialWidth/Height`、`MinWidth/Height`（均含外框的实际窗口尺寸）；自动带控件主题 |
| `ModuleViewHost` | 普通 `UserControl` 的宿主（运行期由框架创建） | `Context`、`Refresh()` |
| `ModuleElement` | 自定义元件基类 | `Source`、`Refresh(context)`、`ApplyAppearance` |

## 文字

`ModuleLabel` 的文案来源优先级：**`Field` > `Source` > `Text`**，视图只用其中一条：

- `Field`：取配置字段的标题与说明（配置字段标题不抄进视图）；
- `Source`：取一个**文本显示源**的内容（多行按换行拼接；变更号不变就不重读）；
- `Text`：固定一句话，或预览初值。

`ModuleNumeric` 只显示值本身：`Text` 是预览值，运行期由 `ReadLatest(Source)` 覆盖；`Quantity="Bytes"` 时自动 B/KB/MB/GB 并把单位并入值文本。单位不要写成控件属性，静态单位用另一个 `ModuleLabel`。

## 状态灯

`ModuleStatusLight` 不读文本，只看模块给它设的**边框角色词**：

```csharp
context.Appearance.SetOutlineRole(LinkSource, ViewInkRoles.Success);  // 绿
context.Appearance.SetOutlineRole(LinkSource, ViewInkRoles.Danger);   // 红
context.Appearance.SetOutlineRole(LinkSource, null);                  // 回空闲
```

视图里 `Source` 必须与 `SetOutlineRole` 用的源名一致；没设过角色显示空闲档。

## 文本面

`ModuleTextSurface` 有两种模式：
- `Display`：只读、跟随尾部，适合日志与结果。`Source` 是显示源名，`ShowNewlineMarks` 决定是否画出数据里的换行字符；`Text` 是预览文本。真实不可显示字符由 `ShowControlCharacterMarkers`（默认随应用设置）显示为警示色 `⟨XX⟩` 或方框 `□`，模块不逐段上色；复制/保存得到原始字符。
- `Input`：可编辑值。`Value` 是值名，`CommitMode` 决定交值时机；`Multiline` 控制多行，提交键由应用设置里的“多行输入提交键”决定（`Ctrl+Enter` / `Enter` / `None`）；`Commit()` 可主动交值（例如按钮点击时取当前编辑内容）。
- **C 转义输入**：`EscapeInput="True"` 表示这个输入面的值按 C 字符串转义语法解释；控件只负责识别并把合法转义组画成蓝色，**不解码**。值本身仍是记法文本（例如 `\x0C`），复制/提交得到记法；模块在发送/转换时调用 `TextEscapes.Normalize` 解码。粘贴进来的真实不可显示字符仍按标记/方框显示，复制得到原字符。
- **字符准入**：输入面的允许字符由本控件的 `TextFormat` 决定（`Text` / `Hex` / `SignedWhole` / `NonNegativeWhole` / `PositiveWhole` / `Real` / `IpAddress`），**不写进值声明**。键入、输入法、粘贴、拖放都走同一条过滤：**丢弃不允许的字符、保留允许的字符**；过滤后为空不产生编辑/提交/撤销。程序化写入（`Text=`、值层回写、持久化旧值）不经过准入。准入只挡字符，不判结构/范围/正负；例如 `PositiveWhole` 仍可键入 `0`，由模块判 >0。所有输入面都必须显式声明 `TextFormat`（AR-27）；纯文本写 `TextFormat="Text"`。
- **空值提示**：`ShowInputHint="True"` 时，空且可编辑的输入面显示该格式的浅灰提示；有内容、只读、`Display` 不显示。提示文案在 Sdk.Ui 文本表（`moduleText.format.*.hint`），窄框可不启用；预览与运行期读的是同一个控件属性。
- **回车写入哪种换行**：`EnterNewlineValue` 指定一个值名（`crlf` / `cr` / `lf` / `none`），不指定或未知时默认 `crlf`（写 `\r\n`）。TextSender 与转换器用它把 CR/LF 勾选框映射为回车档；单行输入不处理换行。回车事件在输入框的**隧道**阶段处理，避免 TextBox 抢先插入 `\n` 或吃掉提交档。
- **已有换行的归一**：只要输入面设置了 `EnterNewlineValue`，换行档变化时控件会把当前输入框里的全部真实 `\r\n`/`\r`/`\n` 替换成目标序列；`none` 删除全部换行，并立即交值。首次显示持久化内容时也归一；没有该值名的输入面不参与（例如 HEX 十六进制输入）。C 转义记法里的 `\n` 不是真实换行，不参与归一。
- **换行标记**：只读面与输入覆盖层对数据换行区分 `\r\n`=`↵`、单独 `\r`=`←`、单独 `\n`=`↓`；统一使用 `TextMuted`（可通过 `NewlineMarkBrush` 覆盖）。显示缓冲的内部哨兵不会进入复制/保存文本；复制保留真实换行种类（CRLF / CR / LF），通道换行按平台换行。
- **输入面的复制、剪切与撤销**：复制/剪切取原始值（不是显示标记）；原生 TextBox 撤销关闭，改用 Raw 快照撤销，避免撤销把 `⟨XX⟩` 恢复成字面文本。

规则：

- `Display` 模式的文本来自 `Display.Append*` / `SetText`；`Input` 模式只读值状态、只按 `CommitMode` 交值。
- **默认即时**：`CommitMode` 默认 `OnChange`；`OnExplicit` 只在明确的“配置变更会重开会话”等需求下使用，失焦也会提交。
- **失败红框与原因分工**：输入有效性统一走 `InputValidity`：它写 `Danger` 外观轮廓，同时写 `ValueValidity`（是否无效 + 原因）；控件只画轮廓/悬停原因，不每帧擦圈。`ValueValidity.Warning/Info` 只给原因、不画圈。
- 控件不读写视图状态；模块读值用 `context.Values.ReadText(值名)`。
- 模块要回写“接受后的值”用 `context.Values.AcceptText(值名, 文本)`；控件会在下一帧读到。
- 值的结论由模块 `context.Values.ReportValidity` 给出，也可用 `ReadValidity` 读回分类。

## 按钮与开关

- `ModuleButton` 的 `CommandName` 必须是声明过的命令；`Content` 是文案，`Icon` 是图标键（图标按钮加 `Classes="icon"` 并给尺寸）。
- `ModuleButton.Appearance`：`Normal` / `Accent` / `Danger`。模块只选档位，具体色板在宿主按钮壳里。
- `ModuleButton.MenuValue`：非空时按钮右侧出现小箭头；菜单选项来自该值的声明（必须是带 `Choices` 的枚举），当前值打勾，点选**只提交值**、不触发 `CommandName`。
- `MenuValue` 的箭头段是**独立命中段**：整高、贴右缘；悬停/按下只覆盖指针所在的那一段，菜单打开时只有箭头段保持按下观感。点箭头只开菜单，不发送；`F4` / `Alt+Down` 也能打开菜单。不要再用父按钮与箭头区同时响应 `:pointerover` 的写法。
- `ModuleToggle` 绑定一个值：用户拨动后按 `CommitMode` 交值；显示值来自 `ReadValue`，不是显示源。
- `CheckedChanged` 在用户拨动和程序写回时都会触发；需要区分时自行用标志。
- 开关文字是独立的 `ModuleLabel`，放在轨道右侧。
- `ModuleCheckBox` 与 `ModuleToggle` 是同一值语义的两种外观：勾选框把文字放在方框右侧（`Content`），适合需要更接近表单勾选观感的设置；文本发送器/转换器的 CR/LF 使用它。

## 输入控件选择

没有任何“按字段类型自动选编辑器”的宿主；视图按值类型**显式**选控件：

| 值类型 | 控件 |
|---|---|
| 文本 / 数字 | `ModuleTextSurface Mode="Input" Value=... Text=预览值` |
| 布尔 | `ModuleToggle` / `ModuleCheckBox` |
| 枚举（下拉） | `ModuleSelect Value=... SelectedKey=预览值`；少量选项也可用 `ModuleSegmentedControl` |
| 颜色 | `ModuleColorField Value=...` |

```xml
<ui:ModuleLabel Classes="caption right" Field="{x:Static views:MyViewKeys.PortName}" />
<ui:ModuleTextSurface Value="{x:Static views:MyViewKeys.PortName}" Text="COM1" />
<ui:ModuleLabel Classes="caption right" Field="{x:Static views:MyViewKeys.BaudRate}" />
<ui:ModuleSelect Value="{x:Static views:MyViewKeys.BaudRate}" />
```

- 值可以是配置字段，也可以是模块声明的值；控件只按名字取 `ValueState`。
- **提交时机**：`ModuleTextSurface` 默认 `OnChange`（逐键交值）；只有**配置字段**这类“改一次就会重开会话”的明确需求才显式写 `OnExplicit`（回车/失焦提交），并在视图注释说明原因。设置面板、普通模块输入不要加显式提交。布尔与枚举点一下即交。
- **提交键**：`ModuleTextSurface` 的 `SubmitKeyValue` 指定保存提交键档位的值名（`Enter` / `CtrlEnter`），`SubmitCommandName` 指定提交后要调的命令；没有 `SubmitKeyValue` 时现读 `context.MultilineSubmitKey`。要让本实例持久化，声明该值并在视图 `Refresh` 里调用 `ModuleViewDefaults.EnsureSubmitKey(context, 值名)`：只有值未知/为空时才从框架默认档位播种一次。
- 值的持久化、可选项与错误由值声明/框架决定，初值由 XAML 声明；控件不直接读写配置或视图状态。
- 标题与说明用配置类型上的 `[ConfigurationField]` / 文本表，不在视图重复。
- 枚举的选项来自 `ValueState.Choices`（`ModuleSelect` 自动读取），视图不再维护第二份选项表；`PreviewOptions` 只服务设计预览。
- **没有自动生成的配置表单**：字段只有视图里画了控件才出现；不声明视图工厂的模块窗口只显示身份与状态。
- 标准接入的参数字段用 `ExternalAccessFieldNames` 的常量（命名空间 `HYWDebugAssistant.Sdk.Access`），不写字符串字面量；文本/数字用 `ModuleTextSurface`，枚举用 `ModuleSelect`，布尔用 `ModuleToggle`。
- **动态可选项**（本机串口、本机网卡地址等 `IConfigurationChoices` 字段）必须用 `ModuleSelect`：选项在运行期来自 `ValueState.Choices`；没有可选项时下拉为空，不要退回普通文本框。
- 预览里没有运行时上下文时，视图类加 `[ModuleViewDesignData(typeof(MyConfig))]`；标准接入用**镜像配置类型**（字段名与运行时一致）。`[ModuleViewDesignData("serial")]` 只有预览宿主注册了该标准才生效，外部作者不要依赖它。
- **预览范围指示**：每个控件在预览期都要有范围指示：无可见边框的控件画虚线控件框；有可见边框的输入控件以自身边框为界，不叠第二条虚线；布局槽位画点线容器框。范围框是控件最终边界，不能靠留白猜。设置面板正文与模块视图一样画范围框；区别只在：它不是模块窗口，预览时不套 `ModulePreviewChrome` 的端口预留带。
- **紧凑布局**：控件按内容报尺寸；`Padding` 只给内部留白、`Margin` 只给外部间隙；固定表单高度 = 最后一行 + 边距，不留无用底部空白。
- **绝对坐标优先**：没有尺寸自适应需求的固定表单/工具区优先根 `Canvas` 绝对坐标；`Grid` 只用于确实需要伸缩的区域。
- **颜色**：用户选色一律走 `ModuleColorField` / 框架 `IModuleColorPicker`，不自建色板；初值取当前值或主题默认色。
- **不随意扩展控件**：先用现有控件组合；不得为单点需求给通用控件加专用开关/字段；确需新能力时抽成通用、带文档与测试的能力。把文本/数字/布尔/枚举混在一个“自动宿主”里就是反例。

## 页签与选择

```xml
<ui:ModuleTabs StateKey="{x:Static local:MyModule.PagesKey}">
  <TabItem Header="{loc:Tr tab.one}">
    <ui:ModuleTextSurface Mode="Input"
                          Value="{x:Static local:MyModule.Text1}"
                          CommitMode="OnChange" />
  </TabItem>
  <TabItem Header="{loc:Tr tab.two}">
    <ui:ModuleTextSurface Mode="Input"
                          Value="{x:Static local:MyModule.Text2}"
                          CommitMode="OnChange" />
  </TabItem>
</ui:ModuleTabs>
```

- `ModuleTabs` 的 `StateKey` 是模块与视图共用的常量；用户切页写入视图状态，模块用 `ViewState.GetSelectedPage(StateKey)` 读。`StateKey` 为空时不持久化选中页。
- `ModuleSelect` / `ModuleSegmentedControl` 绑定值：`Value` + `CommitMode`；用户选择后交值，选项在运行期来自 `ValueState.Choices`（视图给了带图标的静态 `Options` 时优先用视图选项）。
- `ModuleSelect` 也走外观通道：只读时禁用、文字色/字号跟随外观、失败时 `:error` 红框由外观轮廓驱动（Notice 只给悬停原因）；枚举配置字段因此不再需要宿主代管。
- `PreviewOptions` 只在设计环境且没有运行期选项时提供样例词表；它不参与运行期持久化。
- 结构选择（组/通道 chips）仍是动作：用 `OptionSelected` 把选中键交给模块，不当作值。

## 图表

`ChartSurface` 画模块给的全部点，不做降采样、不读业务语义：
```csharp
var series = new ChartSeries { Name = "ch0", Count = values.Length, Values = values };
surface.Series = [series];
surface.XStart = 0;
surface.XStep = 1;
surface.DataVersion++;          // 改了 Values/Count 后抬版本号才会重绘
```

- `Series` 创建一次并复用；只改 `Values` 内容与 `Count`，再抬 `DataVersion`。
- `XAxisKind` 取 `Index`（数据点序号）或 `Time`（秒）；`XStart`/`XStep` 与它同一单位。
- `NaN`/`Inf` 按断线处理；`XEnd` 为 `NaN` 时按点数推导窗口。
- `PlotInset` 是绘图区四边内缩的唯一来源（左/上/右/下，DIP）；刻度文字、游标标签都算在内。
- `ShowCursors` / `ShowYCursors` 控制 X/Y 游标；`ReferenceLines` 画竖向参考线；`CapturePng()` 返回当前图表 PNG。
- 左键拖动绘图区产生 `PanRequested`，右键产生 `ZoomRequested`，事件携带目标轴范围；控件不自己夹紧、不决定跟踪。没有订阅者时对应按键不被接管。
- 滚轮产生 `WheelZoomRequested`：有无 Ctrl 都同时缩放 X/Y；模块负责在任一轴触界时整次拒绝。每格系数由 `WheelZoomStep` 给（模块视图从 `context.WheelZoomStep` 取，默认 1.2）。
- 曲线颜色由调用方显式给 `ChartSeries.Color`；缺省用主题强调色。

`RangeSelector` 是独立的范围可视化控件：拖两端/中间触发 `RangeChanged`，`LastChangeKind` 标识 `Start/End/Body/New`；轨道占满自身宽度，不接收图表 inset。

## 数据显示控件（表盘 / 进度条 / 姿态）

`GaugeDial`、`ScaleBar`、`AttitudeView` 是同一条口径：**只画目标值**。

- `Value`（姿态是 `Roll` / `Pitch` / `Yaw`）必须是有限实数；控件**没有"无数据"分支**——"数据停了一会儿该保持还是回零"由模块发布的目标值决定（模块的自动回零设置），控件不自行判断。
- 平滑在控件内：按框架每帧 `Refresh` 的 dt 逼近目标值，差值落到阈值以下就对齐并停止重绘；`SmoothingEnabled` 关闭或 `SmoothingTimeMs <= 0` 时直接对齐。控件不起定时器。
- `GaugeDial` / `ScaleBar` 在数值超出 `Minimum..Maximum` 时把指针/填充停在边界、读数改用 `OutOfRangeBrush`；收到的数值本身不被裁剪。
- `ScaleBar` 的观感是**矩形细条**（Windows 进度条）：`BarThickness` 给条厚度（<= 0 用可用空间），填充内缩一圈；填充色是 `FillBrush`，未覆盖时用主题给的 `DefaultFillToken`（`SemanticOk`，绿），用户选过色后由视图写入本地 ARGB。
- `ScaleBar` **只画轨道、填充与刻度，不画标题/读数/单位**：那些文字由视图用 `ModuleLabel` / `ModuleNumeric` 拼（标题、单位各有标签，读数用 `ModuleNumeric Source=value Decimals=N`）。控件自带文字会逼出一条标题行，细条就缩不下去了。
- `GaugeDial` 的指针与中心轴默认取 `SemanticError`（红）。
- `ScaleBar` 的刻度数字：横向在条下、纵向在条右；**纵向的数字旋转 −90°（竖着写，自下而上）**，所以纵向标签带只需约 14 DIP；刻度线与数字的间距 2 DIP。
- 预览范围框有**两种角色**（`PreviewBoundsRole`）：**控件框**（虚线，控件最终边界）给标签/读数/勾选框与自绘控件（`GaugeDial`/`ScaleBar`/`AttitudeView` 实现 `IModulePreviewBoundsTarget` 声明"边界即绘制范围"）；**容器框**（点线、半透明，布局槽位）给背景不可见的 `Panel`/`Grid`/`Border`，包括模块内容区的根元素。两者线型不同，避免把一整片空白槽位误当成控件边界。自带轮廓的输入控件（`ModuleElement`，勾选框除外）不叠框；个别元素想关掉用 `ui:PreviewBounds.Show="False"`。需要"可见的槽位边框"时，在视图里显式放一个 `PreviewOnly` 边框（见 `WaveformDisplay` 的 `ChartLayoutFrame`），与虚线覆盖层职责不同。设置面板正文也收集范围框；它只是不套模块窗口的端口预留带。
- 两个数据显示控件**按内容报尺寸**（`MeasureOverride`）：仪表报**方形**（圆的直径，圆心即控件中心）、横向条报"条 + 刻度带"的高度、纵向条报"条 + 标签带"的宽度。视图用 `Center`/`Left` 这类**非拉伸对齐**摆放，控件边界（= 预览虚线框）就紧贴绘制内容；`Margin` 是框外的布局间隙、`Padding` 是框内的留白，两者不再互相污染。
- `Padding`（两个数据显示控件都有）：**四周留白（DIP）**，`0` = 刻度环/轨道外沿紧贴控件边框。它不是 `Thickness`：这两个控件只有一圈留白，非对称留白交给视图的布局。竖排进度条的轨道从 `Padding` 处开始、刻度数字在它右侧。
- `GaugeDial` 的圆心恒为控件中心，半径由短边决定：**扣掉环线自身厚度**后内接；标题/读数/单位画在圆内，**不参与尺寸计算**，改文字不会改变表盘大小。标题在圆上部、读数在圆心下方、单位紧贴读数；超宽文本按内圆弦长省略。端点数字用自己的精度（`200.12` 不会退化成 `200`），并且与邻近主刻度标签按文字宽度避让：端点优先，主刻度让位；端点恰好落在主刻度上时只画一次。
- `GaugeDial.FillOrigin` 与 `ScaleBar.FillOrigin` 同口径：`Zero` 从零开始填充（零在量程外时夹到端点），`Minimum` 从下限开始；默认都是 `Zero`。
- `AttitudeView` 的世界（地面网格、机体轴、左下地面参考轴）固定，相机可在世界坐标系里拖动/预设，**默认等轴测**，机体按三个角度旋转；视角与机体姿态互不影响。`RenderStyle` 切换坐标轴 / 方块 / 无人机。
- `AttitudeView` 的视角交互全部在控件内：左键在姿态区拖动（按钮/面板本身除外）；左上角按钮或空格（姿态区有焦点时）打开 4 列 × 3 行七视角面板（等轴测在上视右侧）；点一个立即应用并关闭。`Esc`/点面板外/失焦关闭；视角不写模块设置、`meta` 或视图状态，**不持久化**。
- `AttitudeView` 的七视角图标是**固定等轴的透明线框立方体 + 单面高亮**，不按预设旋转立方体；`ViewSurfaceBrush` 是面板/按钮面底色，`ViewAccentBrush` 是目标面、悬停/选中底与激活边框，默认取 `BgChrome` / `AccentPrimary`。`ShowLabels` 同时控制机体轴与地面参考轴的标签。
- 颜色默认值在 `Themes/Controls.axaml`；视图不写死色值。用户选定的颜色是**数据**（如 `ScaleBar.FillBrush` 来自取色器），由视图把 `AARRGGBB` 变成画刷，而不是在 XAML 里写常量。

## 浮层面板（ModulePopupPanel + ModulePanelSurface）

模块窗口会裁切视图，**视图内的浮层出不了模块窗口**；面板还要能在预览器里单独看。这是统一标准（三件套），模块只做前两件：

1. **面板正文 = 一个独立视图**：`Views/<Name>SettingsPanel.axaml`，继承 `ModuleView`，在 XAML 里声明自己的 `InitialWidth`/`InitialHeight`（表单区尺寸），并带 `[ModuleViewDesignData]`。预览器可以直接打开这个文件单独看表单：正文不套模块窗口的 `ModulePreviewChrome` 预留带，因此坐标与运行期正文一致；虚线控件框/点线容器框照常显示。模块视图因此不再被面板挡住。
2. **模块视图只声明容器**：

```xml
<ui:ModulePopupPanel x:Name="SettingsPanel"
                     PanelType="{x:Type views:GaugeSettingsPanel}"
                     Title="{loc:Tr view.settings}"
                     ResetCommandName="{x:Static views:GaugeDisplayViewKeys.ResetSettings}" />
```

代码后置补一行 `SettingsPanel.Anchor = SettingsButton;`，按钮点击调 `Toggle()`。

3. **容器负责其余一切**：无边框、不透明、不可缩放、不进任务栏的独立 `Window`（`Owner` = 模块窗口）；标题行由 `ModulePanelSurface` 统一渲染（标题 + 刷新 + 关闭）；正文包进内部 `ModuleViewHost` 按帧刷新；关闭 = 关闭图标 / 再点齿轮 / `Esc` / 模块窗口关闭或隐藏；窗口复用，正文状态保留。

- **面板必须提供两个图标键**：`icon.refresh`（恢复默认）与 `icon.close`（关闭），属主由容器按正文程序集给。
- **面板正文布局**：固定尺寸表单用根 `Canvas` 绝对坐标（`Canvas.Left/Top`），坐标只写面板 XAML；行高、列宽、间距由面板自己统一，不再用行列容器自动排。面板高度按“最后一行底边 + 8 DIP”收紧，不保留无用底部空白；新增/删除行时同步改高度。
- **尺寸与位置**：窗口 = 正文 `Initial` 尺寸 + 标题行；以 `Anchor` 的屏幕矩形为基准，优先右侧，放不下依次左侧/下方/上方，始终夹在工作区内；打开期间锚点按钮带 `panel-anchor` 高亮，模块窗口移动时面板逐帧跟随。`Anchor` 缺失时回退模块窗口右侧。
- **不要用 `Popup` 或视图内浮层**：Popup 的落位/翻转/是否落 overlay 层由平台决定，还出现过贴在模块上的残留；视图内浮层被窗口裁切。两者都试过，统一用独立窗口。
- **预览**：模块视图里只有齿轮（没有面板布局）；面板正文单独打开时只渲染正文（运行期标题行/外框由容器提供），不套模块窗口预留带，范围框照常显示；另有 `<模块>-settings.png`（不带范围框）与 `<模块>-settings-preview.png`（带范围框）两类截图。
- 面板正文要公共无参构造（与视图工厂同一规则）。
- 容器铺在模块视图最上层但不参与命中（`IsHitTestVisible=false`）：齿轮与表盘的点击照常。
- 标题行的刷新按钮走模块的 `resetSettings` 命令（`ResetCommandName`），行为与原来一致：只重置设置，不动组/通道。**实现约束**：外壳住在独立窗口里，不在正文 `ModuleViewHost` 的祖先链上，容器必须把视图上下文显式交给标题行按钮；只设 `ResetCommandName` 不会触发命令。
## 图标

`ModuleButton` 的 `Icon` 会自动用所在 `ModuleView` 的程序集作图标表拥有者，模块视图里只写键即可；内容被托管到模块视图之外（浮层面板）时用 `IconOwner` 显式给属主：

```xml
<ui:ModuleButton Classes="icon" Width="16" Height="16"
                 Icon="{x:Static views:MyViewKeys.Refresh}"
                 CommandName="{x:Static local:MyModule.RefreshCommand}" />
```

内容在浮层面板里时：`<ui:ModuleButton Classes="icon" Icon="{x:Static views:MyViewKeys.Close}" IconOwner="{x:Static local:MyModule.IconOwner}" />`。独立使用 `Icon` 时要显式给 `Owner`（模块程序集）：

```csharp
public static System.Reflection.Assembly IconOwner => typeof(MyModule).Assembly;
```

```xml
<ui:Icon IconKey="{x:Static views:MyViewKeys.Refresh}"
         Owner="{x:Static local:MyModule.IconOwner}" Width="16" Height="16" />
```

图标来自 `resources/icons/<程序集简名>/icons.json`（见[资源与文本](resources.md)）；缺键显示键名并报诊断。图标颜色与按钮文字共用同一个 `Foreground`（`Danger`/`Accent` 一起变白，指针/按下态一起变），不再为图标单独维护颜色。加、减、删除、刷新这类操作图标使用 `fill="none"` 的中心线路径（线条由 `Icon` 统一描边），不再用实心块。

## 自定义元件

需要模块特有的显示件时，继承 `ModuleElement`：

```csharp
public sealed class LevelBar : ModuleElement
{
    public override void Refresh(IModuleViewContext context)
    {
        var value = context.ReadLatest(Source);
        // 只按值更新视觉，不重建控件树
    }
}
```

`ModuleElement` 提供 `Source`（按名字取数、承接外观）与 `Refresh(context)`；框架每帧调用。实现必须做到“值没变就不碰视觉”。

## 预览辅助

| 成员 | 作用 |
|---|---|
| `[ModuleViewDesignData(typeof(T))]` / `[ModuleViewDesignData("serial")]` | 预览时从哪个配置类型反射配置字段；标准标识只有在预览宿主注册后才有效 |
| `ui:PreviewOnly.IsEnabled="True"` | 只在预览环境出现（运行期不创建、不参与布局/命中/PNG） |
| `ui:PreviewBounds.Show="False"` | 预览时不给该元素画定位虚线框 |
| `ModuleView.IsDesignEnvironment` | 统一的“是否预览/设计环境”判定；不要在视图里另写 `Design.IsDesignMode || IsPreviewHost` |

预览值写在控件的 `Text` / `IsChecked` / `SelectedKey` 等属性上；不要为预览在代码后置里写一套默认值。

预览不调用运行期 `Refresh`，XAML 中的可选参数默认全显；运行期会隐藏的控件因此在预览里仍占位，可能与相邻控件重叠或遮挡。这是有意为之，不能据此判定布局缺陷，也不要把这些控件改成 XAML 默认隐藏。

**勾选框的范围框覆盖整个控件矩形（方框 + 文字）**：14×14 方框只表达勾选态，不表达布局或命中区域；不能只框方框，也不能在方框外留一大段透明宽度却指望范围框去猜。它是“自带可见轮廓的输入控件默认不叠框”规则的明确例外。

## 相关

- [界面](ui.md)：视图工厂、刷新、尺寸、布局与预览步骤
- [资源与文本](resources.md)：图标键与文本键
- [接口参考](reference.md)：`IModuleViewContext` 的成员
