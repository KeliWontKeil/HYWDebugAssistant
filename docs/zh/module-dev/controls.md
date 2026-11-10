# 控件

本页是控件属性的唯一清单；怎么把它们组合成一个视图见[界面](ui.md)。

## 共同规则

- 控件全部来自 `HYWDebugAssistant.Sdk.Ui.Controls`，命名空间在 XAML 里写 `xmlns:ui="using:HYWDebugAssistant.Sdk.Ui.Controls"`。
- 引用显示源、输入源、命令、配置字段时，一律用模块自己的常量（`{x:Static}`），不写字符串字面量。
- 控件不写死颜色与字号：颜色取主题 token 或角色词，排印用样式类 `caption` / `hint`。
- 控件在界面刷新时**按变更号重读**，没有变化的源不会重复读；视图不需要自己定时刷新。

## 专用控件

| 控件 | 用途 | 关键属性 |
|---|---|---|
| `ModuleLabel` | 一句话：静态文字、配置字段标题、或显示源的整体文本 | `Text`、`Field`、`Source` |
| `ModuleNumeric` | 数值读数 | `Source`、`Quantity`（`None` / `Bytes`） |
| `ModuleStatusLight` | 状态灯，按外观角色着色 | `Source` |
| `ModuleTextSurface` | 文本面：日志显示或可编辑输入 | `Mode`、`Source`、`Multiline`、`IsReadOnly`、`ShowNewlineMarks`、`TextAlignment`、`MinEditorHeight` |
| `ModuleButton` | 命令按钮 | `CommandName`、`Content`、`Icon` |
| `ModuleToggle` | 开关，提交一个布尔输入 | `Source`、`Text`、`IsChecked`、`CheckedChanged` |
| `ModuleConfigField` | 配置字段编辑器（标题另用 `ModuleLabel`） | `Field` |
| `ModuleTabs` | 页签容器 | `StateKey`、`TabItem` 子项 |
| `ModuleSelect` | 紧凑选项下拉 | `Options`、`SelectedKey`、`OptionSelected` |
| `ModuleSegmentedControl` | 互斥分段选择（文字/图标，可禁用） | `Options`、`SelectedKey`、`ShowLabels`、`IconSize`、`OptionSelected` |
| `ChartSurface` | 通用多序列绘图（全量点、X/Y 游标、参考线、PNG） | `Series`、`XStart`、`XEnd`、`XStep`、`XAxisKind`、`YMin/Max`、`XTickStep/YTickStep`、`PlotInset`、`ReferenceLines`、`ShowCursors`、`ShowYCursors`、`CapturePng()` |
| `RangeSelector` | 通用线性范围可视化/拖动（单一范围、主题色轨道、细边界、主/次刻度、边缘/块内指针） | `Minimum`、`Maximum`、`RangeStart/End`、`MinimumRangeSpan`、`TickStep`、`ShowTicks`、`ShowTickLabels`、`MinorTickStep`、`XAxisKind`、`TickBrush`、`TickLabelBrush`、`HandleWidth`（仅边缘命中）、`RangeChanged`；轨道占满自身宽度，不接收图表 inset |
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
- `ChartSurface.PlotInset` 是绘图区四边内缩的唯一来源（左/上/右/下，DIP）；刻度文字、游标标签和轴标题都算在内，控件不再自动增加顶部/底部预留。波形显示器用 `40,20,4,20`，发生器用 `40,4,4,20`。
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

<!-- 数值读数与状态灯 -->
<ui:ModuleNumeric Source="{x:Static local:MyModule.CountSource}" Quantity="Bytes" />
<ui:ModuleStatusLight Source="{x:Static local:MyModule.StateSource}" />

<!-- 文本面：日志（只读、跟随尾部）与输入（可编辑、多行） -->
<ui:ModuleTextSurface Mode="Display" Source="{x:Static local:MyModule.LogSource}" />
<ui:ModuleTextSurface Mode="Input" Source="{x:Static local:MyModule.TextSource}" Multiline="True" />

<!-- 命令按钮与开关 -->
<ui:ModuleButton CommandName="{x:Static local:MyModule.ResetCommand}" Content="{loc:Tr view.reset}" />
<ui:ModuleToggle Source="{x:Static local:MyModule.ContinuousSource}" Text="{loc:Tr view.continuous}" />

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

## 样式类

| 样式类 | 用途 |
|---|---|
| `caption` | 字段标题、单位、次要说明 |
| `hint` | 提示与辅助文字 |
| 默认 | 正文与值 |

## 相关

- [界面](ui.md)：视图结构、布局与预览
- [资源与文本](resources.md)：图标键与文本键
- [界面外壳与叠加层](../concepts/shell.md)：视觉基线与配色纪律
