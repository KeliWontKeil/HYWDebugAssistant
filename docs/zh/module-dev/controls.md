# 控件

本页是控件属性的唯一清单；怎么把它们组合成一个视图见[界面](ui.md)。

## 共同规则

- 控件全部来自 `HYWDebugAssistant.Sdk.Ui.Controls`，命名空间在 XAML 里写 `xmlns:ui="using:HYWDebugAssistant.Sdk.Ui.Controls"`。
- 引用显示源、输入源、命令、配置字段时，一律用模块自己的常量（`{x:Static}`），不写字符串字面量。
- 控件不写死颜色与字号：颜色取主题 token 或角色词，排印用样式类 `caption` / `hint` / `micro`。
- 控件在界面刷新时**按变更号重读**，没有变化的源不会重复读；视图不需要自己定时刷新。
- **模块文字只有一套实现**：`ModuleLabel` 与 `ModuleNumeric` 都是 `TextBlock` 派生，直接继承换行、对齐、字号等属性；模块视图里不再用裸 `TextBlock` 显示文字。
- **开关不自带文字**：`ModuleToggle` 只画轨道与圆钮；视图里的裸开关，其标签是独立的 `ModuleLabel`，放在开关右侧。`ModuleConfigField` 的布尔编辑器是字段行，沿用“字段标题在左、编辑器在右”的表单布局。
- **预览值写在 XAML**：可输入控件的初始值、只读控件的预览值直接用控件自己的值属性声明；运行期数据到达后覆盖。详见下文“预览值”。
- **文字在控件矩形内的垂直对齐**由 `TextVerticalAlignment` 决定（`Top` / `Center` / `Bottom`，默认 `Center`）。它与控件自身的 `VerticalAlignment`（控件在父容器里的位置）是两个维度；不要用 `Padding` 或 `VerticalAlignment` 代替。

## 专用控件

| 控件 | 用途 | 关键属性 |
|---|---|---|
| `ModuleLabel` | 一句话：静态文字、配置字段标题、显示源文本；**`TextBlock` 派生** | `Text`、`Field`、`Source`、`TextVerticalAlignment`；样式类 `caption` / `hint` / `micro` / `right` / `wrap` |
| `ModuleNumeric` | 数值读数；**`TextBlock` 派生** | `Source`、`Quantity`（`None` / `Bytes`）、`Text`（预览值）、`TextVerticalAlignment` |
| `ModuleStatusLight` | 状态灯，按外观角色着色 | `Source` |
| `ModuleTextSurface` | 文本面：日志显示或可编辑输入 | `Mode`、`Source`、`Text`（预览/初值）、`Multiline`、`IsReadOnly`、`ShowNewlineMarks`、`TextAlignment`、`MinEditorHeight` |
| `ModuleButton` | 命令按钮 | `CommandName`、`Content`、`Icon` |
| `ModuleToggle` | 开关，提交一个布尔输入；**不自带文字** | `Source`、`IsChecked`、`CheckedChanged` |
| `ModuleConfigField` | 配置字段编辑器（标题另用 `ModuleLabel`） | `Field` |
| `ModuleTabs` | 页签容器 | `StateKey`、`TabItem` 子项 |
| `ModuleSelect` | 紧凑选项下拉 | `Options`、`SelectedKey`、`PreviewOptions`、`OptionSelected` |
| `ModuleSegmentedControl` | 互斥分段选择（文字/图标，可禁用） | `Options`、`SelectedKey`、`PreviewOptions`、`ShowLabels`、`IconSize`、`OptionSelected` |
| `ChartSurface` | 通用多序列绘图（全量点、X/Y 游标、参考线、PNG） | `Series`、`XStart`、`XEnd`、`XStep`、`XAxisKind`、`YMin/Max`、`XTickStep/YTickStep`、`PlotInset`、`ReferenceLines`、`ShowCursors`、`ShowYCursors`、`CapturePng()` |
| `RangeSelector` | 通用线性范围可视化/拖动（单一范围、主题色轨道、细边界、主/次刻度、边缘/块内指针） | `Minimum`、`Maximum`、`RangeStart/End`、`MinimumRangeSpan`、`TickStep`、`ShowTicks`、`ShowTickLabels`、`MinorTickStep`、`XAxisKind`、`TickBrush`、`TickLabelBrush`、`HandleWidth`（仅边缘命中）、`RangeChanged`、`LastChangeKind`；轨道占满自身宽度，不接收图表 inset |
| `Icon` | 模块自己的图标 | `IconKey`、`Owner` |
| `ModuleView` | 视图根（推荐） | 继承 `UserControl`，自动带控件主题 |
| `ModuleViewHost` | 普通 `UserControl` 的宿主（运行期由框架创建） | `Context`、`Refresh()` |

## 序列窗口快照（`SeriesWindow`）

波形图表不再使用有界环形缓冲或 UI 侧降采样。模块把选定 X 范围内的**全部点**构造成不可变
`SeriesWindowSnapshot`，通过 `context.Display.SetSeriesWindow(source, snapshot)` 发布；视图通过
`context.ReadSeriesWindow(source)` 读取。

- 声明：`DeclareDisplaySource(new DisplaySourceDeclaration(name, ViewDataSourceKind.SeriesWindow))`。
- 快照中的 `SeriesWindowChannel.Values` 长度就是范围宽度；没有 256/512 上限，也没有抽稀。
- 快照发布后模块不得再改数组；更新时构造新对象整体替换。
- `ChartSeries.Values` 是 `ReadOnlyMemory<float>`，可以直接引用快照数组；`Count` 为点数。
- 旧 `Sequence` 环形缓冲仍可供其他模块使用；波形图表不再走它。
- `ChartSurface` 的曲线颜色由调用方显式给出；缺省兜底是主题强调色，不再按索引取内置七色。
- `RangeSelector.LastChangeKind` 在拖动时标识 `Start/End/Body/New`；调用方据此区分“只改左端宽度”和“移动范围”，程序设置属性不会改变它。
- `ChartSurface.PlotInset` 是绘图区四边内缩的唯一来源（左/上/右/下，DIP）；刻度文字、游标标签和轴标题都算在内，控件不再自动增加顶部/底部预留。波形显示器用 `50,20,4,20`，发生器用 `40,4,4,20`。
- 绘图区左、下边线就是坐标轴，另补上、右边线形成完整外框；外框只框绘图区，不包含刻度文字、游标标签或图例。

## 颜色选择（`IModuleColorPicker`）

模块视图不再自建色板。需要选色时通过 `IModuleViewContext.ColorPicker` 打开框架统一调色窗口：

- `SuggestedColors`：框架推荐色（与调色窗口推荐色同源）；可用于**首次数据出现时**做一次自动分配。
- `PickAsync(owner, initialArgb, title, resetText)`：返回 `Confirmed`、`ColorArgb`、`Reset`。
- `Reset=true` 表示用户点了“恢复”；模块应清空自己的颜色覆盖，由视图重新分配自动色。
- 自动分配只做一次并持久化；不要在每帧按索引重算。`ColorPicker` 为 `null`（设计期/未接线）时不要弹窗，使用主题兜底色。
- 自建 `ChartPalette`/`ModuleColorPalette` 已移除。

## 用法示例

```xml
<!-- 静态标题 + 配置字段标题（文案取自字段元数据） -->
<ui:ModuleLabel Classes="caption" Text="{loc:Tr view.title}" />
<ui:ModuleLabel Classes="caption" Field="{x:Static local:MyModule.ThresholdField}" />

<!-- 数值读数与状态灯：Text 是预览值，运行期被真实读数覆盖 -->
<ui:ModuleNumeric Source="{x:Static local:MyModule.CountSource}" Quantity="Bytes" Text="0 B" />
<ui:ModuleStatusLight Source="{x:Static local:MyModule.StateSource}" />

<!-- 文本面：日志（只读、跟随尾部）与输入（可编辑、多行） -->
<ui:ModuleTextSurface Mode="Display" Source="{x:Static local:MyModule.LogSource}" Text="{loc:Tr view.logPreview}" />
<ui:ModuleTextSurface Mode="Input" Source="{x:Static local:MyModule.TextSource}" Text="10" Multiline="True" />

<!-- 命令按钮与开关：开关文字是独立标签，放在开关右边 -->
<ui:ModuleButton CommandName="{x:Static local:MyModule.ResetCommand}" Content="{loc:Tr view.reset}" />
<ui:ModuleToggle Source="{x:Static local:MyModule.ContinuousSource}" IsChecked="True" />
<ui:ModuleLabel Classes="caption" Text="{loc:Tr view.continuous}" />

<!-- 配置字段与页签 -->
<ui:ModuleConfigField Field="{x:Static local:MyModule.PortField}" />
<ui:ModuleTabs StateKey="{x:Static local:MyModule.PagesKey}">
  <TabItem Header="1"><ui:ModuleTextSurface Mode="Input" Source="{x:Static local:MyModule.Text1}" /></TabItem>
  <TabItem Header="2"><ui:ModuleTextSurface Mode="Input" Source="{x:Static local:MyModule.Text2}" /></TabItem>
</ui:ModuleTabs>
```

## 文本面

`ModuleTextSurface` 有两种模式：

- `Display`：只读、跟随尾部，适合日志与结果；`ShowNewlineMarks` 决定是否画出数据里的换行。
- `Input`：可编辑并提交到输入源；`Multiline` 控制多行，提交键由设置里的「多行输入提交键」决定；`Commit()` 可主动提交。
- 模块若希望“内容一变就处理”（如波形显示器/发生器），可在 `OnStep` 的 `StepReason.Input` 中回读 `ViewState.GetInputText`，与 HEX 转换器同一模型；这不需要给 `ModuleTextSurface` 增加提交属性，也不会影响文本发送等仍按 Enter 提交的模块。
- **程序写入 ≠ 用户编辑**：视图刷新或模块回写时给 `Text` 赋值只更新显示，**不写** `ViewState`；只有用户真的改字才逐键写回。原因是 Avalonia 12 的 `TextBox.TextChanged` 由 `Dispatcher.Post` 异步投递，不能用一次性 `_syncing` 标志拦截；控件按“叶子文本与已知文本相同”识别程序回显。模块要回写输入值请用 `IViewStateReader.SetInputValue`（它不回投模块），不要靠给 `Text` 赋值来伪造一次用户输入。

两个模式都需要在 `OnRegistered` 里声明对应的显示源 / 输入源，否则控件读写都被框架拒收。

## 自定义元素

需要模块特有的显示件（例如自绘曲线）时，继承 `ModuleElement` 并实现刷新：

```csharp
public sealed class LevelBar : ModuleElement
{
    protected override void OnApplyTemplate(TemplateAppliedEventArgs e) { /* 找到模板部件 */ }

    public override void Refresh(IModuleViewContext context)
    {
        var value = context.ReadLatest(Source);
        // 只按值更新视觉，不重建控件树
    }
}
```

`ModuleElement` 提供 `Source`（按名字取数）与 `Refresh(context)`（由宿主按刷新节拍调用）。两条纪律：没有变化不碰视觉；刷新不重建对象。

## 预览值

预览器只实例化 XAML，不调用 `Refresh`。因此可输入控件的初始值、只读控件在预览里的值，都直接写在控件自己的值属性上：

- `ModuleTextSurface.Text`：输入面的初始值 / 显示面的预览文本。
- `ModuleLabel.Text`：带 `Source` 或 `Field` 时，`Text` 是预览/初值；运行期由源或字段标题覆盖。
- `ModuleNumeric.Text`：预览值；运行期由真实读数覆盖。
- `ModuleToggle.IsChecked`：预览初值；运行期由输入状态覆盖。
- `ModuleSelect.SelectedKey` / `ModuleSegmentedControl.SelectedKey`：预览选中项；`PreviewOptions` 只在 `Options` 为空且处于设计环境时提供样例词表。
- `ModuleSelect` / `ModuleSegmentedControl` 的固定词表在代码里构造即可；预览器会看到它们。

两条边界：

- **配置字段例外**：`ModuleConfigField` 的值来自配置元数据 / 默认实例，不在 XAML 重复一遍；视图只写 `Field`，预览显示配置默认值。
- **图表不伪造曲线**：`ChartSurface` / `RangeSelector` 只在 XAML 声明坐标范围、刻度与轴模式；曲线数据属于运行期数据。

设计环境由 `ModuleView.IsDesignEnvironment` 统一判定；不要在自己的视图代码里再拼 `Design.IsDesignMode || ModuleView.IsPreviewHost`。预览期出现的虚线范围框由框架统一附加，运行期不创建、不参与布局与命中。某个元素太大或太密、预览里不想看它的框时，可以在该元素上写 `ui:PreviewBounds.Show="False"`。

## 横向滚动

只有横向滚动条的控件（例如 `ModuleSegmentedControl`）允许直接用鼠标滚轮横向滚动，不需要按住 Shift；纵向可滚动的区域保持默认行为。该行为由框架统一挂在 `ScrollViewer` 上，视图不需要额外代码。

## 下拉高度

`ModuleSelect` 的默认高度是 24，但没有隐藏的最小高度：视图写 `Height="18"` 等更小值会真正生效。不要依赖“某个默认最小高度”来排版。

## 样式类

| 样式类 | 用途 |
|---|---|
| `caption` | 字段标题、单位、次要说明 |
| `hint` | 提示与辅助文字 |
| `micro` | 更紧凑的辅助读数 |
| `right` | 在自己的矩形里右对齐 |
| `wrap` | 显式换行（默认单行、超宽省略） |
| 默认 | 正文与值 |

## 相关

- [界面](ui.md)：视图结构、布局与预览
- [资源与文本](resources.md)：图标键与文本键
- [界面外壳与叠加层](../concepts/shell.md)：视觉基线与配色纪律
