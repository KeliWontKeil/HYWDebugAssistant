# 界面

模块界面 = **代码声明 + 模块自己的 XAML**。框架提供视图宿主与控件库，不读任何界面描述文件。

## 视图工厂

```csharp
[ModuleDefinition("vendor", "my-module",
    DisplayName = "我的模块",
    WindowWidth = 360, WindowHeight = 180,
    ViewFactory = typeof(Views.MyViewFactory))]
public sealed class MyModule : IModule { /* ... */ }
```

- 视图工厂实现 `IModuleViewFactory`，由框架**每个实例造一次**；
- 视图根继承 `ModuleView`（或普通 `UserControl`，由宿主包一层 `ModuleViewHost`）；
- **不声明视图工厂是合法形态**：窗口只显示身份与状态。

## 绑定键

视图里的键**只许**用 `{x:Static}` 引用模块自己声明的常量：

```xml
<controls:ModuleLabel classes="caption" Text="{x:Static local:MyModule.TitleText}" />
<controls:ModuleStatusLight Source="{x:Static local:MyModule.StateSource}" />
<controls:ModuleButton Content="打开" CommandName="{x:Static local:MyModule.OpenCommand}" />
```

键必须与 `OnRegistered` 里声明的名字一致；界面按名字取数、按名字提交。

## 控件

| 控件 | 用途 |
|---|---|
| `ModuleLabel` | 静态 / 动态一句话（`Text`、`Field`、`Source`）；单位也是标签 |
| `ModuleNumeric` | 数值读数（`Source` + `Quantity` 决定字节数等格式） |
| `ModuleStatusLight` | 状态灯（按外观角色着色） |
| `ModuleTextSurface` | 文本面（日志 / 编辑器，可只读） |
| `ModuleButton` | 命令按钮（`CommandName`，可选 `Icon`） |
| `ModuleToggle` | 开关（`IsChecked` + `Text`） |
| `ModuleConfigField` | 配置字段编辑器（`Field` 取配置元数据） |
| `ModuleTabs` | 页签容器（`StateKey` 供视图状态回读） |
| `Icon` | 图标（`IconKey` + `Owner`） |
| `IncrementalTextView` | 增量文本视图（大段日志） |

排印档位用样式类：`caption` / `hint` / 默认。颜色一律取主题 token，不写死色值。

## 设计时

- `[ModuleViewDesignData(typeof(MyConfig))]` 或 `[ModuleViewDesignData("serial")]` 给预览器提供配置来源；
- 预览器没有宿主，动态数据由设计时上下文提供样例值；
- `d:DesignWidth/Height` 在运行期读不到（只会被预览器使用），尺寸判据以模块声明的窗口尺寸与真机渲染为准。

## 验证

- 出像素：起应用看模块窗口（快照用例覆盖仓库内模块）；
- 可点击：按钮触发命令、开关提交输入；
- 不遮挡：窗口最小尺寸要容得下端口标记。

## 相关

- [资源与文本](resources.md)
- [界面外壳与叠加层](../concepts/shell.md)
