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
| `ModuleTextSurface` | 文本面：日志显示或可编辑值 | `Mode`、`Value`、`CommitMode`、`Source`（仅显示）、`Text`、`Multiline`、`IsReadOnly`、`ShowNewlineMarks`、`TextAlignment`、`MinEditorHeight`、`Commit()`、`SubmitKeyValue`、`SubmitCommandName`、`EnterNewlineValue` |
| `ModuleButton` | 命令按钮（可选右侧小箭头菜单） | `CommandName`、`Content`、`Icon`、`Appearance`、`MenuValue` |
| `ModuleToggle` | 开关：勾选提交 `"true"` / `"false"` | `Value`、`CommitMode`、`IsChecked`、`CheckedChanged`（程序写回也触发） |
| `ModuleCheckBox` | 勾选框：方框 + 勾形 + 右侧文字（间距 2 DIP），提交 `"true"` / `"false"` | `Value`、`CommitMode`、`IsChecked`、`Content`、`CheckedChanged` |
| `ModuleConfigField` | 值编辑器（文本/数字/布尔/枚举） | `Value`（标题另用 `ModuleLabel`） |
| `ModuleTabs` | 页签容器，选中页写入视图状态 | `StateKey`、`TabItem` 子项 |
| `ModuleSelect` | 紧凑下拉 | `Value`、`CommitMode`、`SelectedKey`、`PreviewOptions`；旧 `Options`/`OptionSelected` 仅供结构选择 |
| `ModuleSegmentedControl` | 互斥分段选择（文字/图标，可禁用） | `Value`、`CommitMode`、`SelectedKey`、`PreviewOptions`、`ShowLabels`、`IconSize`；旧 `Options`/`OptionSelected` 供结构选择 |
| `ChartSurface` | 通用多序列绘图（全量点、X/Y 游标、参考线、PNG、左键平移/右键缩放/滚轮缩放） | `Series`、`DataVersion`、`XStart`、`XEnd`、`XStep`、`XAxisKind`、`YMin/Max`、`XTickStep/YTickStep`、`PlotInset`、`ReferenceLines`、`ShowCursors`、`ShowYCursors`、`WheelZoomStep`、`CapturePng()`、`PanRequested`、`ZoomRequested`、`WheelZoomRequested`；有无 Ctrl 都同时缩放 X/Y（模块负责在任一轴触界时整次拒绝） |
| `RangeSelector` | 通用线性范围可视化/拖动 | `Minimum`、`Maximum`、`RangeStart/End`、`MinimumRangeSpan`、`TickStep`、`ShowTicks`、`ShowTickLabels`、`MinorTickStep`、`XAxisKind`、`HandleWidth`、`RangeChanged`、`LastChangeKind` |
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
- `Display`：只读、跟随尾部，适合日志与结果。`Source` 是显示源名，`ShowNewlineMarks` 决定是否画出数据里的换行字符；`Text` 是预览文本。只读文本里的 `⟨XX⟩` 标记由主题 `IncrementalTextView.MarkerBrush`（默认警示色）着色，模块不逐段上色。
- `Input`：可编辑值。`Value` 是值名，`CommitMode` 决定交值时机；`Multiline` 控制多行，提交键由应用设置里的“多行输入提交键”决定（`Ctrl+Enter` / `Enter` / `None`）；`Commit()` 可主动交值（例如按钮点击时取当前编辑内容）。
- **回车写入哪种换行**：`EnterNewlineValue` 指定一个值名（`crlf` / `cr` / `lf` / `none`），不指定或未知时默认 `crlf`（写 `\r\n`）。TextSender 与转换器用它把 CR/LF 勾选框映射为回车档；单行输入不处理换行。回车事件在输入框的**隧道**阶段处理，避免 TextBox 抢先插入 `\n` 或吃掉提交档。
- **已有换行的归一**：只要输入面设置了 `EnterNewlineValue`，换行档变化时控件会把当前输入框里的全部 `\r\n`/`\r`/`\n` 替换成目标序列；`none` 删除全部换行，并立即交值。首次显示持久化内容时也归一；没有该值名的输入面不参与（例如 HEX 十六进制输入）。
- **换行标记**：只读面与输入覆盖层对数据换行区分 `\r\n`=`↵`、单独 `\r`=`←`、单独 `\n`=`↓`；统一使用 `TextMuted`（可通过 `NewlineMarkBrush` 覆盖）。显示缓冲的内部哨兵不会进入复制/保存文本。

规则：

- `Display` 模式的文本来自 `Display.Append*` / `SetText`；`Input` 模式只读值状态、只按 `CommitMode` 交值。
- **失焦提交**：`OnExplicit` 在提交键、`Commit()` 或**失焦**时交值；`OnChange` 已逐键交值。
- 控件不读写视图状态；模块读值用 `context.Values.ReadText(值名)`。
- 模块要回写“接受后的值”用 `context.Values.AcceptText(值名, 文本)`；控件会在下一帧读到。
- 值的错误由模块 `context.Values.Report` 给出，控件只画结论。

## 按钮与开关

- `ModuleButton` 的 `CommandName` 必须是声明过的命令；`Content` 是文案，`Icon` 是图标键（图标按钮加 `Classes="icon"` 并给尺寸）。
- `ModuleButton.Appearance`：`Normal` / `Accent` / `Danger`。模块只选档位，具体色板在宿主按钮壳里。
- `ModuleButton.MenuValue`：非空时按钮右侧出现小箭头；菜单选项来自该值的声明（必须是带 `Choices` 的枚举），当前值打勾，点选**只提交值**、不触发 `CommandName`。
- `MenuValue` 的箭头段是**独立命中段**：整高、贴右缘；悬停/按下只覆盖指针所在的那一段，菜单打开时只有箭头段保持按下观感。点箭头只开菜单，不发送；`F4` / `Alt+Down` 也能打开菜单。不要再用父按钮与箭头区同时响应 `:pointerover` 的写法。
- `ModuleToggle` 绑定一个值：用户拨动后按 `CommitMode` 交值；显示值来自 `ReadValue`，不是显示源。
- `CheckedChanged` 在用户拨动和程序写回时都会触发；需要区分时自行用标志。
- 开关文字是独立的 `ModuleLabel`，放在轨道右侧。
- `ModuleCheckBox` 与 `ModuleToggle` 是同一值语义的两种外观：勾选框把文字放在方框右侧（`Content`），适合需要更接近表单勾选观感的设置；文本发送器的转义/CR/LF 使用它。

## 值编辑器

`ModuleConfigField` 只写 `Value`；标题仍是独立的 `ModuleLabel`（配置字段标题可用 `Field` 从配置元数据取）：

```xml
<ui:ModuleLabel Classes="caption right" Field="{x:Static views:MyViewKeys.PortName}" />
<ui:ModuleConfigField Value="{x:Static views:MyViewKeys.PortName}" />
```

- 值可以是配置字段，也可以是模块声明的值；控件只按名字取 `ValueState`。
- 支持的编辑器：布尔、整数、实数、文本、枚举（含动态选项下拉）。
- **提交时机**：布尔与枚举点一下即交；文本 / 数字在回车、显式动作或失焦时交。需要"逐键生效"的内容改用 `ModuleTextSurface CommitMode="OnChange"`。
- **提交键**：`SubmitKeyValue` 指定保存提交键档位的值名（`Enter` / `CtrlEnter`），`SubmitCommandName` 指定提交后要调的命令；没有 `SubmitKeyValue` 时现读 `context.MultilineSubmitKey`。要让本实例持久化，声明该值并在视图 `Refresh` 里调用 `ModuleViewDefaults.EnsureSubmitKey(context, 值名)`：只有值未知/为空时才从框架默认档位播种一次。
- 值的持久化、可选项与错误由值声明/框架决定，初值由 XAML 声明；控件不直接读写配置或视图状态。
- 标题与说明用配置类型上的 `[ConfigurationField]` / 文本表，不在视图重复。
- **没有自动生成的配置表单**：字段只有视图里画了控件才出现；不声明视图工厂的模块窗口只显示身份与状态。
- 标准接入的参数字段用 `ExternalAccessFieldNames` 的常量（命名空间 `HYWDebugAssistant.Sdk.Access`），不写字符串字面量：

```xml
<ui:ModuleConfigField Value="{x:Static access:ExternalAccessFieldNames.PortName}" />
```
- 预览里没有运行时上下文时，视图类加 `[ModuleViewDesignData(typeof(MyConfig))]`；标准接入用**镜像配置类型**（字段名与运行时一致）。`[ModuleViewDesignData("serial")]` 只有预览宿主注册了该标准才生效，外部作者不要依赖它。

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

## 图标

`ModuleButton` 的 `Icon` 会自动用所在 `ModuleView` 的程序集作图标表拥有者，模块视图里只写键即可：

```xml
<ui:ModuleButton Classes="icon" Width="16" Height="16"
                 Icon="{x:Static views:MyViewKeys.Refresh}"
                 CommandName="{x:Static local:MyModule.RefreshCommand}" />
```

独立使用 `Icon` 时要显式给 `Owner`（模块程序集）：

```csharp
public static System.Reflection.Assembly IconOwner => typeof(MyModule).Assembly;
```

```xml
<ui:Icon IconKey="{x:Static views:MyViewKeys.Refresh}"
         Owner="{x:Static local:MyModule.IconOwner}" Width="16" Height="16" />
```

图标来自 `resources/icons/<程序集简名>/icons.json`（见[资源与文本](resources.md)）；缺键显示键名并报诊断。图标颜色跟随 `Foreground`。

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
