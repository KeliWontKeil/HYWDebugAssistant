# 界面

模块界面 = **代码声明 + 模块自己的 XAML**。框架提供视图宿主与控件库，不读任何界面描述文件。

## 视图工厂

```csharp
[ModuleDefinition("vendor", "my-module",
    WindowWidth = 340, WindowHeight = 168,
    ViewFactory = typeof(Views.MyViewFactory))]
public sealed class MyModule : IModule { /* … */ }
```

- 工厂实现 `IModuleViewFactory`，由框架**每个实例调用一次**；工厂必须无状态。
- 视图根继承 `ModuleView`（推荐）：它把控件库主题与设计时 token 带进 XAML 预览器，运行期不会重复挂。
- 普通 `UserControl` 也可以：运行期由宿主包一层 `ModuleViewHost(context, view)` 提供上下文，但预览器里没有主题。
- **不声明视图工厂是合法形态**：窗口只显示身份与状态。

## 绑定键

视图里的键只许用 `{x:Static}` 引用模块自己声明的常量：

```xml
<ui:ModuleButton CommandName="{x:Static local:MyModule.ResetCommand}"
                 Content="{loc:Tr view.reset}" />
<ui:ModuleNumeric Source="{x:Static local:MyModule.CountSource}" />
```

- 名字必须与 `OnRegistered` 里声明的完全一致；界面按名字取数、按名字提交。
- 文案不写在 XAML 里，用 `{loc:Tr 键}` 指向本模块文本表（见[资源与文本](resources.md)）。

## 窗口尺寸

- 尺寸由 `[ModuleDefinition]` 的 `WindowWidth` / `WindowHeight` 与 `MinWidth` / `MinHeight` 声明；用户在界面上拖过的尺寸随组合件保存。
- 内容放不下时内容区滚动，窗口不按内容长高。
- 最小尺寸要容得下端口标记，否则标记会被裁掉。

## 布局

- 内容区尺寸是"窗口减描边"；用 `Grid` 的 `*` 行/列吸收剩余空间，固定行用于动作条与状态行。
- 需要精确摆放时用 `Canvas`；坐标集中写在视图文件里，不散落到代码。
- 单位是普通标签（`ms`、`条`），不占用控件属性。
- 颜色一律取主题 token 或角色词，不写死色值；排印档位用样式类 `caption` / `hint` / 默认。

## 文本

- 界面文案在本模块的 `resources/i18n/<程序集简名>/zh-Hans.json`，XAML 里只写键。
- 缺表或缺键时显示可见标记（键名），不会静默空白；发布前用应用的界面对着看一遍。

## 预览

Avalonia 预览器需要一个**可执行目标**才能预览类库里的 XAML。模块是类库，所以要给它配一个极小的预览宿主工程（只用于预览，不参与发布）：

```powershell
dotnet new console -n MyModule.Preview
dotnet add MyModule.Preview package Avalonia.Desktop
dotnet add MyModule.Preview package Avalonia.Themes.Fluent
dotnet add MyModule.Preview reference ..\MyModule\HYWDebugAssistant.Vendor.MyModule.csproj
```

Avalonia 的版本要与 `Sdk.Ui` 依赖的版本同一条线（`dotnet list package --include-transitive` 能看到）；装成另一条大版本时预览器会无法加载控件。

把生成的 `Program.cs` 换成 Avalonia 的入口，并加一个 `App.axaml`（片段省略 `using`）：

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
public partial class App : Application
{
    public override void Initialize() => AvaloniaXamlLoader.Load(this);
}
```

步骤：

1. 先 `dotnet build` 一次，让预览器能加载程序集；
2. 打开模块视图的 `.axaml`，在预览器右上角把目标选为 `MyModule.Preview`；
3. 看到带样式的控件、样例读数与配置字段标题即为成功。

预览的边界：

| 现象 | 原因 | 处理 |
|---|---|---|
| 控件没有样式、像裸控件 | 视图根不是 `ModuleView`，或预览目标没挂控件主题 | 根改成 `ModuleView`；预览宿主加 `Controls.axaml` 的 `StyleInclude` |
| 文案显示成键名 | 预览器读不到文本表 | 工程里加上[项目与包布局](project-layout.md)中的 `Content` 项 |
| 配置字段是空的 | 预览器没有运行时上下文 | 视图类上加 `[ModuleViewDesignData(typeof(预览配置类型))]` |
| 命令按钮点了没反应 | 设计时上下文不执行命令 | 正常；命令只在产品里生效 |
| 读数显示样例数字 | 设计时上下文给的固定样例 | 正常；真实数据只在运行期 |
| 标准接入的字段画不出来 | 参数类型在框架侧，模块引用不到 | 写一个**预览镜像配置类型**（字段名与运行时一致、带配置标注），只给预览用 |

## 验证

- 出像素：起产品看模块窗口；按钮能触发命令、开关能提交输入。
- 不遮挡：最小窗口下端口标记、状态行、按钮都要完整。
- 本仓库的模块可以用 `.\tools\dev.ps1 -Mode Snapshots` 生成截图；外部模块没有这条工具，靠起应用看。

## 相关

- [控件](controls.md)：每个控件的属性与示例
- [资源与文本](resources.md)：文本表与图标
- [界面外壳与叠加层](../concepts/shell.md)：窗口、端口标记与配色的机制
