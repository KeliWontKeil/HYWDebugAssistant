# 从零开始

这一页把样例变成你自己的模块，并让它在产品里出现窗口。全程只用到发布目录 `sdk/` 里的东西。

## 1 复制样例

把 `sdk/samples/DataModule` 整个目录复制到仓库外的任意位置（例如 `D:\work\MyModule`）。四个样例的用途见[模块开发](index.md)的路线表：纯变换用 `DataModule`，带界面用 `UiModule`。

复制出来的目录里只有四样东西：工程文件、`module.json`、模块代码、`NuGet.config`。`NuGet.config` 已经指向同级的 `sdk/`，包版本也已经写好。

## 2 改身份（三处必须一致）

| 位置 | 要改什么 |
|---|---|
| 工程文件 | 文件名（包名默认取项目名最后一段）、`AssemblyName`、`RootNamespace`；也可用 `<ModulePackageName>` 直接指定包名 |
| `module.json` | `id`、`entryAssembly`、`entryType` |
| 模块类 | 类名与 `[ModuleDefinition("作者", "模块名")]` 的标识 |

```json
{
  "id": "vendor/my-module",
  "entryAssembly": "HYWDebugAssistant.Vendor.MyModule.dll",
  "entryType": "HYWDebugAssistant.Vendor.MyModule.MyModule",
  "dependsOn": [],
  "resources": []
}
```

三处对应关系：`id` 必须等于 `[ModuleDefinition]` 的标识；`entryAssembly` 必须是实际 DLL 文件名；`entryType` 必须是入口类的完整类型名。任何一处不一致都会被加载器明确拒绝。

## 3 构建

```powershell
dotnet build -c Release
```

首次构建会从 `sdk/` 还原作者包。成功的标志是模块包目录出现：

```
bin/module-package/Release/MyModule/
  module.json
  HYWDebugAssistant.Vendor.MyModule.dll
  ...（可选：pdb、resources）
```

包内**不应**出现宿主程序集（`HYWDebugAssistant.Sdk*`、`Avalonia*` 等），打包规则会自动排除；手工打包时按[项目与包布局](project-layout.md)的清单剔除。

## 4 契约自检（推荐）

在同一个解决方案里加一个测试工程，引用 `Sdk.Testing` 与你的模块工程，跑一次声明面检查：

```csharp
var probe = ModuleContract.Probe(typeof(MyModule));
probe.Problems.ShouldBeEmpty();
```

判定标准与测试工程的搭法见[测试与验证](testing.md)。`Problems` 非空就是声明错误，先改完再继续。

## 5 装载

1. 启动产品；
2. 活动栏「模块清单」→「导入模块…」→ 选择 `bin/module-package/Release/MyModule` 这一层目录；
3. 列表里出现来源为「外部」的模块；若提示需重启，点「重新加载」；
4. 选中后点「加入工作区」，桌面出现模块窗口。

验证三件事：窗口出现、端口标记在边缘显示、与另一个模块同名端口能连上并看到数据。连法见[连接与组网](../guides/connect.md)。

## 6 换成自己的逻辑

| 想改 | 改哪里 |
|---|---|
| 标识与显示名 | `[ModuleDefinition]` 与文本表 |
| 数据类型、端口、角色 | `OnRegistered` 里的 `Declare*`，见[声明面](contracts.md) |
| 收到数据做什么 | 接收回调；发送用 `Ports.TrySend`，见[实现](implementation.md) |
| 周期任务 | 实现 `IActivitySource` 并创建定时器，见[自发活动与定时器](activity.md) |
| 界面 | 视图工厂 + 模块 XAML，见[界面](ui.md) |
| 文案与图标 | `resources/`，见[资源与文本](resources.md) |

## 7 换成带界面的样例

复制 `sdk/samples/UiModule`，它演示了视图工厂、`ModuleView`、文本表与一个命令按钮。界面与预览步骤见[界面](ui.md)，控件属性见[控件](controls.md)。

## 常见错误

| 现象 | 原因 | 处理 |
|---|---|---|
| 还原失败（找不到包） | `NuGet.config` 的本地源路径不对，或版本写错 | 确认本地源指向 `sdk/`；版本取包文件名里的四段 |
| 报平台配置无效、`obj` 路径里出现别的软件的目录 | 机器上的 `Platform` 环境变量被其它软件设置 | 工程文件里加 `<PlatformName>AnyCPU</PlatformName>`（样例已带），或在干净终端里构建 |
| 导入时报「缺少 module.json」 | 选到了包目录的上一层或下一层 | 选**包含 `module.json` 的那一层** |
| 导入时报「清单与代码标识不一致」 | `module.json` 的 `id` 与特性不同 | 统一成同一个标识 |
| 编译通过但窗口空白 | 视图工厂没声明，或 XAML 未被编译 | 见[界面](ui.md)的排障 |
| 端口连不上 | 两侧类型标识或形状不同 | 让两侧声明同一类型，或插入转换模块 |

## 相关

- [模块结构](anatomy.md)：七层分别是什么
- [发布与装载](publishing.md)：被拒原因全表
