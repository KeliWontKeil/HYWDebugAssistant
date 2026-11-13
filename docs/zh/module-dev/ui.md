# 界面

模块界面 = **代码声明 + 模块自己的 XAML**。框架提供视图容器与控件库，不读任何界面描述文件。

## 视图工厂

```csharp
[ModuleDefinition("vendor", "my-module", ViewFactory = typeof(Views.MyViewFactory))]
public sealed class MyModule : IModule { }
```

```csharp
public sealed class MyViewFactory : IModuleViewFactory
{
    public Control Create(IModuleViewContext context) => new Views.MyView();
}
```

- 工厂由框架**每个实例调用一次**；工厂必须无状态、有公共无参构造。
- 用类型而不是实例：模块对象有状态、受生命周期约束，视图不能造出它的第二份。
- **不声明视图工厂是合法形态**：窗口只显示身份与状态。
- 视图根继承 `ModuleView`（推荐）：它把控件库主题与设计时 token 带进 XAML 预览器，运行期不重复挂。普通 `UserControl` 也可以，但预览里没有主题。

## 视图拿到什么

`ModuleViewHost` 把 `IModuleViewContext` 设为视图的 `DataContext`，并在每帧调用一次 `Refresh()`。因此：

- XAML 根写 `x:CompileBindings="True" x:DataType="contracts:IModuleViewContext"`（`xmlns:contracts="using:HYWDebugAssistant.Sdk.Ui"`）；
- 代码后置可以用 `DataContext as IModuleViewContext` 取数、提交、调外观；
- 视图**不要自己起定时器**，也不要直接读框架服务；刷新由主应用驱动，窗口不可见时不刷新。
- 视图根本身不会被自动调用 `Refresh`：需要每帧代码逻辑时，让视图类实现 `IModuleViewElement`（框架会对可视树里的每个实现调用），或把逻辑放进自定义 `ModuleElement`。

```csharp
public partial class MyView : ModuleView, IModuleViewElement
{
    public void Refresh(IModuleViewContext context)
    {
        // 每帧一次；值没变就立即返回
    }
}
```

框架每帧遍历可视树：先落外观（`ViewAppearance`），再对每个 `IModuleViewElement` 调 `Refresh(context)`。自写元件继承 `ModuleElement`（或实现 `IModuleViewElement`），在 `Refresh` 里“值没变就不碰视觉、不重建控件树”。

## 绑定键

视图里的键只许用 `{x:Static}` 引用模块自己声明的常量：

```xml
<ui:ModuleButton CommandName="{x:Static local:MyModule.ResetCommand}"
                 Content="{loc:Tr view.reset}" />
<ui:ModuleNumeric Source="{x:Static local:MyModule.CountSource}" />
```

一个最小视图的根如下（命名空间一个都不能少）：

```xml
<ui:ModuleView xmlns="https://github.com/avaloniaui"
               xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
               xmlns:ui="using:HYWDebugAssistant.Sdk.Ui.Controls"
               xmlns:loc="using:HYWDebugAssistant.Sdk.Ui.Controls"
               xmlns:contracts="using:HYWDebugAssistant.Sdk.Ui"
               xmlns:local="using:MyModule"
               x:Class="MyModule.Views.MyView"
               x:CompileBindings="True"
               x:DataType="contracts:IModuleViewContext"
               InitialWidth="360" InitialHeight="144"
               MinWidth="360" MinHeight="144">
  <Grid RowDefinitions="*,Auto" Margin="12">
    <ui:ModuleLabel Grid.Row="1" Source="{x:Static local:MyModule.StateSource}" />
  </Grid>
</ui:ModuleView>
```

- 名字必须与 `OnRegistered` 里声明的完全一致；界面按名字取数、按名字提交。
- 文案不写在 XAML 里，用 `{loc:Tr 键}` 指向本模块文本表（见[资源与文本](resources.md)）。
- `{loc:Tr}` 的属主按 `avares://` 的 host 解析，因此 XAML 放在模块程序集里即可命中本模块的表。

## 窗口尺寸

视图根 `.axaml` 声明**两个独立尺寸**：

```xml
<ui:ModuleView InitialWidth="360" InitialHeight="144"
               MinWidth="360" MinHeight="144">
```

| 属性 | 语义 |
|---|---|
| `InitialWidth/InitialHeight` | 新建/还原实例时的默认**内容**尺寸；低于最小尺寸时取最小尺寸 |
| `MinWidth/MinHeight` | 运行期允许的最小**内容**尺寸 |

- 尺寸指内容区，不含外框：框架在内容区外再加左侧 20 / 顶部 22 / 右侧 20 / 底部 24 DIP 的端口预留带与 1 DIP 描边。窗口的实际最小尺寸还会被端口标记下限（160 DIP）托底。
- 用户拖过的尺寸随组合件保存；读回旧布局时夹到当前有效区间。
- `InitialWidth/Height` 是唯一声明；`ModuleView` 在 setter 里把**内容尺寸 + 窗口外框**镜像给预览器的 `Design.Width/Height`（运行期 `InitialWidth` 仍是内容尺寸），不要再写 `d:DesignWidth/Height`。
- 内容放不下时内容区滚动，窗口不按内容长高。
- 最小尺寸要容得下端口标记，否则标记会被裁掉。

## 端口声明

端口（名称/方向/类型/默认边/偏移/可拖动性）写在视图 XAML 的 `ModuleView.Ports` 里：

```xml
<ui:ModuleView.Ports>
  <ui:ModulePort Name="{x:Static local:MyModule.InPort}"
                 Direction="Receive"
                 TypeId="{x:Static local:MyModule.TypeId}"
                 Edge="Top" />
</ui:ModuleView.Ports>
```

- `TypeId` 指向 `OnRegistered` 里已声明的数据类型；形状/语义从类型继承，XAML 不重复写；
- 名称与类型标识用 `{x:Static}`，不要在 XAML 里复制字面量；
- 构建会把 `Views/**/*.axaml` 随包复制到 `views/`，Hosting 从同一份源解析端口；预览器直接读 `ModuleView.Ports`；
- 旧模块仍可用 C# `DeclarePort`，但不要与 XAML 双声明；内置模块与模板已全部迁到 XAML。

## 布局

- 内容区用 `Grid`：`*` 行/列吸收剩余空间，固定行放动作条与状态行。
- **没有自适应需求的模块可以整块用一个 `Canvas`**（示例：串口、两个波形桥）：窗口尺寸 = 内容尺寸，`Min` 与 `Initial` 相等。共有输入（组号、采样率这类）保持绝对坐标；只有随协议变化的帧信息行放进固定起点的纵向堆叠（`StackPanel`）：行隐藏即折叠、不留占位；统计与状态按惯例固定在底部——统计一行，状态句单独贴底，不再为它们套第二层行列容器。
- 固定控件集中放在一个 `Canvas` 中，用 `Canvas.Left/Top` 精确摆放；坐标只写在 XAML，代码不写位置、不按内容改行高。
- `Canvas` 是“相对坐标容器”，不是窗口级绝对坐标：窗口变宽时由外层 `*` 区域吸收。
- **结构常显，可选参数可隐藏**：组/通道/页签/动作等结构控件必须在 XAML 中常显；无选中项时用 `IsEnabled` 禁用。仅部分数据适用的参数行允许运行期用 `IsVisible` 隐藏，但 XAML 默认必须全显，保证预览器不调用 `Refresh` 时仍能定位全部控件。
- 单位是普通标签（`ms`、`条`），不占用控件属性。
- 开关文字是独立 `ModuleLabel`，放在轨道右侧。
- 颜色一律取主题 token 或角色词，不写死色值；排印档位用 `caption` / `hint` / `micro`。
- 需要用户选色时走 `IModuleViewContext.ColorPicker`，不要自建色板。

## 颜色与排印

模块视图从主应用的主题资源取颜色，不写死色值；深浅主题与用户改色由资源自动跟随。常用 token：

| 用途 | token |
|---|---|
| 模块底色 / 控件区底色 | `BgModule`、`BgChrome` |
| 边框 | `BorderDefault`、`BorderStrong` |
| 文字 | `TextPrimary`、`TextSecondary`、`TextMuted` |
| 主题色 | `AccentPrimary`、`AccentSecondary`、`AccentStrong` |
| 语义色 | `SemanticOk`、`SemanticWarn`、`SemanticError`、`SemanticIdle` |
| 方向色 | `DirectionSend`、`DirectionReceive` |
| 字体 | `FontUi`（界面）、`FontData`（等宽数据） |

```xml
<Border Background="{DynamicResource BgChrome}"
        BorderBrush="{DynamicResource BorderDefault}"
        BorderThickness="1" />
```

- 排印用样式类 `caption` / `hint` / `micro`，不要逐控件写字号；`ControlHeight`、`ControlFontSize` 是控件库的基准值。
- 运行期按状态变色用 `Appearance.SetOutlineRole` / `SetTextRole` 的角色词（`ViewInkRoles`），不要直接改控件资源。
- 代码后置里需要颜色时优先用 `ColorPicker` 或主题资源；确实需要绝对色时才写 ARGB。

## 主应用运行验收

构建、契约自检和预览都不等于模块运行。模块的唯一运行形态是**主应用装载后的实例**：

- 启动主应用（`HYWDebugAssistant.App.exe`），按[发布与装载](publishing.md)导入包并加入现场；
- 检查窗口出现、端口标记与状态行完整、命令/配置写回/读数随数据变化；
- 关闭应用重开后仍能加载，确认不是一次性的预览效果。

本仓库模块可用 `.\tools\dev.ps1 -Mode Snapshots` 生成截图；外部模块没有这条工具，靠主应用看。

预览器不创建实例；它会显示**窗口外框**与**端口占位标记**（位置/尺寸/方向色），但不显示网络名/速率/指示灯/状态行等运行期信息；它不能代替本节。

## 可选：XAML 设计器预览

Avalonia 预览器需要一个**可执行目标**才能加载类库里的 XAML。模块本身仍是类库；下面这个 `MyModule.Preview` 只是**临时的 IDE 预览目标**，不参与发布、不进模块包、不是交付物：

```powershell
dotnet new console -n MyModule.Preview
dotnet add MyModule.Preview package Avalonia.Desktop
dotnet add MyModule.Preview package Avalonia.Themes.Fluent
dotnet add MyModule.Preview reference ..\MyModule\HYWDebugAssistant.Vendor.MyModule.csproj
```

Avalonia 的版本要与 `Sdk.Ui` 依赖的版本同一条线（`dotnet list package --include-transitive` 能看到）；装成另一条大版本时预览器无法加载控件。

把生成的 `Program.cs` 换成 Avalonia 入口，并加一个 `App.axaml`：

```csharp
internal static class Program
{
    [STAThread]
    public static void Main(string[] args) => BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);

    public static AppBuilder BuildAvaloniaApp() =>
        AppBuilder.Configure<App>().UsePlatformDetect().LogToTrace();
}
```

```xml
<Application xmlns="https://github.com/avaloniaui"
             xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
             x:Class="MyModule.Preview.App" RequestedThemeVariant="Light">
  <Application.Styles>
    <FluentTheme />
    <StyleInclude Source="avares://HYWDebugAssistant.Sdk.Ui/Themes/Controls.axaml" />
  </Application.Styles>
</Application>
```

```csharp
using Avalonia;
using Avalonia.Markup.Xaml;
using HYWDebugAssistant.Sdk.Ui.Controls;

public partial class App : Application
{
    public override void Initialize()
    {
        // 显式声明“我在预览”：部分宿主不保证 Design.IsDesignMode 为真。
        // 产品绝不设置它。
        ModuleView.IsPreviewHost = true;
        AvaloniaXamlLoader.Load(this);
    }
}
```

步骤：

1. 先 `dotnet build` 一次，让预览器能加载程序集；
2. 打开模块视图的 `.axaml`，在预览器右上角把目标选为 `MyModule.Preview`；
3. 看到带样式的控件、预览值、范围框与配置字段标题，只说明 **XAML 设计面可加载**，不是模块完成判据。

**不要双击或 `dotnet run` 这个预览工程来验收模块。** 它没有实例、生命周期与数据面；端口只以占位标记显示。用它截图或 `PrintWindow` 得到的结果也不能代表主应用里的模块。

预览器能验证与不能验证的边界：

| 能验证 | 不能验证 |
|---|---|
| XAML 能否加载、控件主题、文本表、预览值、范围框、带设计数据的配置字段、窗口外框、端口占位标记 | 实例创建与生命周期、`OnRegistered`/`OnStep`、端口连接、命令执行、数据流、外部会话、状态行、端口运行期信息（网络名/速率/指示灯）、导入/重启 |

**预览显示什么由视图声明决定**：输入框/读数/开关/下拉的初始值直接写在控件的 `Text` / `IsChecked` / `SelectedKey` 上；标签与不可见容器由框架附加虚线范围框；配置字段显示配置默认值。运行期数据到达后覆盖这些声明值。不要为了预览在视图代码里写一套默认值，也不要在设计环境调用 `Refresh`。

配置字段在预览里没有运行时上下文，需要告诉预览器“从哪个配置类型反射”：

```csharp
[ModuleViewDesignData(typeof(MyConfig))]
public sealed partial class MyView : ModuleView { }
```

标准接入的参数类型在框架侧，外部作者新建的预览宿主既没有这些类型，也没有注册标准映射，因此 `[ModuleViewDesignData("serial")]` **不会自动生效**。只做预览时，写一个字段名与运行时一致、带配置标注的**镜像配置类型**，用 `[ModuleViewDesignData(typeof(MyMirrorConfig))]`；不要依赖仓库预览宿主的内部注册。

### 预览排障

| 现象 | 原因 | 处理 |
|---|---|---|
| 控件没有样式、像裸控件 | 视图根不是 `ModuleView`，或预览宿主没有进入设计环境 | 根改成 `ModuleView`；预览宿主设置 `ModuleView.IsPreviewHost = true` |
| 文案显示成键名 | 预览器读不到文本表 | 工程里加上[项目与包布局](project-layout.md)中的 `Content` 项 |
| 配置字段只剩虚线框、看不到控件 | 视图缺设计数据：`ModuleConfigField` 没有配置 schema，只能画范围框 | 视图类加 `[ModuleViewDesignData(typeof(配置类型))]`；外部预览宿主用镜像配置类型 |
| 参数控件在预览里消失 | XAML 默认 `IsVisible=False`，只靠运行时 `Refresh` 打开 | 结构控件常显，可选参数行 XAML 默认全显，运行期再按数据隐藏 |
| 预览期出现虚线范围框、运行期消失 | 范围框是设计期定位辅助，由 `ModuleView.IsDesignEnvironment` 决定 | 正常；运行期不创建、不参与布局、命中或 PNG 捕获 |
| 某个装饰件只在预览期出现 | 使用了 `ui:PreviewOnly.IsEnabled="True"` | 正常；运行期自动隐藏 |
| 命令按钮点了没反应 | 设计时上下文不执行命令 | 正常；命令只在主应用里生效 |
| 读数或输入框预览为空 | 视图没有在 XAML 里给预览值 | 在 `Text` / `IsChecked` / `SelectedKey` 等属性上写代表性初值 |
| 端口占位标记不出现 / 位置不对 | 视图没有 `ModuleView.Ports`，或 `Edge`/`Offset` 写错 | 补端口声明；运行期完整标记（网络名/速率/指示灯）仍到主应用实例里看 |
| 端口占位标记出现了、但运行期没有该口 | XAML 与 C# 双声明或 `TypeId` 未声明 | 看 Issue 中心；只保留 XAML 声明，并确认 `TypeId` 已在 `OnRegistered` 声明 |
| 标准接入的字段画不出来 | 参数类型在框架侧，外部预览宿主没有注册 | 写镜像配置类型并用 `typeof` 指定，不要用未注册的标准标识 |

## 相关

- [控件](controls.md)：每个控件的属性、事件与示例
- [资源与文本](resources.md)：文本表与图标
- [接口参考](reference.md)：`IModuleViewContext` 的成员
- [界面外壳与叠加层](../concepts/shell.md)：窗口、端口标记与配色的机制
