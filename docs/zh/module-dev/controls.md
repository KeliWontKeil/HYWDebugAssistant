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
| `Icon` | 模块自己的图标 | `IconKey`、`Owner` |
| `ModuleView` | 视图根（推荐） | 继承 `UserControl`，自动带控件主题 |
| `ModuleViewHost` | 普通 `UserControl` 的宿主（运行期由框架创建） | `Context`、`Refresh()` |

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
