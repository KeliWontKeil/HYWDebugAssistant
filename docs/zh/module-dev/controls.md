# 控件

本页是控件用法与关键属性的唯一清单；怎么把它们组合成一个视图见[界面](ui.md)。完整成员见[接口参考](reference.md)；作者包也带 XML 文档，IDE 里可直接看。

## 共同规则

- 控件全部来自 `HYWDebugAssistant.Sdk.Ui.Controls`，XAML 命名空间写 `xmlns:ui="using:HYWDebugAssistant.Sdk.Ui.Controls"`。
- 引用显示源、输入源、命令、配置字段时，一律用模块自己的常量（`{x:Static}`），不写字符串字面量。
- 控件不写死颜色与字号：颜色取主题 token 或角色词，排印用样式类 `caption` / `hint` / `micro`。
- 控件由 `ModuleViewHost` 每帧驱动：**值没变就不碰视觉**，视图不自己起定时器、不在 `Refresh` 里重建控件树。
- **模块文字只有一套实现**：`ModuleLabel` 与 `ModuleNumeric` 都是 `TextBlock` 派生，直接继承换行、对齐、字号等属性；模块视图里不再用裸 `TextBlock` 显示文字。
- **开关不自带文字**：`ModuleToggle` 只画轨道与圆钮；标签是独立的 `ModuleLabel`，放在右侧。
- **预览值写在 XAML**：可输入控件的初始值、只读控件的预览值直接写在控件自己的值属性上；运行期数据到达后覆盖。
- **文字在控件矩形内的垂直对齐**由 `TextVerticalAlignment` 决定（`Top` / `Center` / `Bottom`，默认 `Center`）；它与控件自身的 `VerticalAlignment` 是两个维度。

## 控件一览

| 控件 | 用途 | 关键属性 |
|---|---|---|
| `ModuleLabel` | 一句话：静态文字、配置字段标题、文本显示源 | `Text`、`Field`、`Source`、`TextVerticalAlignment`；样式类 `caption` / `hint` / `micro` / `right` / `wrap` |
| `ModuleNumeric` | 数值读数 | `Source`、`Quantity`（`None` / `Bytes`）、`Text`（预览值） |
| `ModuleStatusLight` | 状态灯：底色来自模块设的**角色词** | `Source`；配合 `Appearance.SetOutlineRole(Source, role)` |
| `ModuleTextSurface` | 文本面：日志显示或可编辑输入 | `Mode`、`Source`、`Text`、`Multiline`、`IsReadOnly`、`ShowNewlineMarks`、`TextAlignment`、`MinEditorHeight`、`Commit()` |
| `ModuleButton` | 命令按钮 | `CommandName`、`Content`、`Icon` |
| `ModuleToggle` | 开关：勾选提交 `"true"` / `"false"` | `Source`、`IsChecked`、`CheckedChanged`（程序写回也触发） |
| `ModuleConfigField` | 配置字段编辑器 | `Field`（标题另用 `ModuleLabel`） |
| `ModuleTabs` | 页签容器，选中页写入视图状态 | `StateKey`、`TabItem` 子项 |
| `ModuleSelect` | 紧凑下拉 | `Options`、`SelectedKey`、`PreviewOptions`、`OptionSelected` |
| `ModuleSegmentedControl` | 互斥分段选择（文字/图标，可禁用） | `Options`、`SelectedKey`、`PreviewOptions`、`ShowLabels`、`IconSize`、`OptionSelected` |
| `ChartSurface` | 通用多序列绘图（全量点、X/Y 游标、参考线、PNG、左键平移/右键缩放） | `Series`、`DataVersion`、`XStart`、`XEnd`、`XStep`、`XAxisKind`、`YMin/Max`、`XTickStep/YTickStep`、`PlotInset`、`ReferenceLines`、`ShowCursors`、`ShowYCursors`、`CapturePng()`、`PanRequested`、`ZoomRequested` |
| `RangeSelector` | 通用线性范围可视化/拖动 | `Minimum`、`Maximum`、`RangeStart/End`、`MinimumRangeSpan`、`TickStep`、`ShowTicks`、`ShowTickLabels`、`MinorTickStep`、`XAxisKind`、`HandleWidth`、`RangeChanged`、`LastChangeKind` |
| `Icon` | 模块图标 | `IconKey`、`Owner` |
| `ModuleView` | 视图根（推荐） | 继承 `UserControl`；`InitialWidth/Height`、`MinWidth/Height`；自动带控件主题 |
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

- `Display`：只读、跟随尾部，适合日志与结果。`ShowNewlineMarks` 决定是否画出数据里的换行字符；`Text` 是预览文本。
- `Input`：可编辑并提交到输入源。`Multiline` 控制多行，提交键由应用设置里的“多行输入提交键”决定（`Ctrl+Enter` / `Enter` / `None`）；`Commit()` 可主动提交（例如按钮点击时取当前编辑内容）。

规则：

- `Display` 模式的文本来自 `Display.Append*` / `SetText`；输入模式的文本由用户逐键写入视图状态。
- **失焦不提交**：提交只由提交键或 `Commit()` 触发；否则“填完点别处”会变成一次发送。
- **程序写入 ≠ 用户编辑**：给 `Text` 赋值只更新显示，不写视图状态；模块要回写输入值请用 `ViewState.SetInputValue`。
- 模块若希望“内容一变就处理”，在 `OnStep(StepReason.Input)` 里回读 `ViewState.GetInputText`。

## 按钮与开关

- `ModuleButton` 的 `CommandName` 必须是声明过的命令；`Content` 是文案，`Icon` 是图标键（图标按钮加 `Classes="icon"` 并给尺寸）。
- `ModuleToggle` 勾选后向 `Source` 提交 `"true"`，取消提交 `"false"`；`IsChecked` 的显示值优先从**输入状态**读（模块写回的值），没有输入状态才回退同名显示源。
- `CheckedChanged` 在用户拨动和程序写回时都会触发；需要区分时自行用标志。
- 开关文字是独立的 `ModuleLabel`，放在轨道右侧。

## 配置字段

`ModuleConfigField` 只写 `Field`，标题、说明、范围、可选项全部来自配置 Schema：

```xml
<ui:ModuleLabel Classes="caption right" Field="{x:Static views:MyViewKeys.PortName}" />
<ui:ModuleConfigField Field="{x:Static views:MyViewKeys.PortName}" />
```

- 字段必须存在于该实例的配置（模块自己的配置类型，或框架登记的标准接入参数类型）。
- 支持的编辑器：布尔、整数、实数、文本、枚举（含动态选项下拉）。
- 标题与说明用配置类型上的 `[ConfigurationField]` / 文本表，不在视图重复。
- **没有自动生成的配置表单**：字段只有视图里画了 `ModuleConfigField` 才出现；不声明视图工厂的模块窗口只显示身份与状态，配置不会自己长出来。
- 标准接入的参数字段用 `ExternalAccessFieldNames` 的常量（命名空间 `HYWDebugAssistant.Sdk.Access`），不写字符串字面量：

```xml
<ui:ModuleConfigField Field="{x:Static access:ExternalAccessFieldNames.PortName}" />
```
- 预览里没有运行时上下文时，视图类加 `[ModuleViewDesignData(typeof(MyConfig))]`；标准接入用**镜像配置类型**（字段名与运行时一致）。`[ModuleViewDesignData("serial")]` 只有预览宿主注册了该标准才生效，外部作者不要依赖它。

## 页签与选择

```xml
<ui:ModuleTabs StateKey="{x:Static local:MyModule.PagesKey}">
  <TabItem Header="{loc:Tr tab.one}">
    <ui:ModuleTextSurface Mode="Input" Source="{x:Static local:MyModule.Text1}" />
  </TabItem>
  <TabItem Header="{loc:Tr tab.two}">
    <ui:ModuleTextSurface Mode="Input" Source="{x:Static local:MyModule.Text2}" />
  </TabItem>
</ui:ModuleTabs>
```

- `ModuleTabs` 的 `StateKey` 是模块与视图共用的常量；用户切页写入视图状态，模块用 `ViewState.GetSelectedPage(StateKey)` 读。`StateKey` 为空时不持久化选中页。
- `ModuleSelect` / `ModuleSegmentedControl` 的 `Options` 是选项记录（`ModuleSelectOption`：`Key` + `Label`；`ModuleSegmentOption` 另有 `IconKey`/`IsEnabled`/`ToolTip`），`SelectedKey` 是当前值；`OptionSelected` 携带新键。
- 选项来自代码构造（例如枚举）；`PreviewOptions` 只在设计环境且 `Options` 为空时提供样例词表。

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

## 相关

- [界面](ui.md)：视图工厂、刷新、尺寸、布局与预览步骤
- [资源与文本](resources.md)：图标键与文本键
- [接口参考](reference.md)：`IModuleViewContext` 的成员
