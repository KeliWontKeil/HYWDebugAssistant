# 从零开始

本页把 SDK 模板变成你自己的模块，并让它在主应用（产品）里出现窗口。全程只用到 SDK 目录与主应用本身。

## 前置

发布产物中有两个独立目录（互不包含）：

```
HYWDebugAssistant-<版本>-win-x64/     # 主应用（产品，双击启动）
HYWDebugAssistant.Sdk-<版本>/         # 作者工具链
  packages/                           # 五个 nupkg
  template/ModuleTemplate/            # 模块模板
  NuGet.config
  README.txt
```

模板默认依赖 `template/ModuleTemplate/../../packages`，所以**先保持 SDK 目录完整**，在 SDK 根内构建；把模板单独复制到别处时，必须改 `NuGet.config` 的本地源。

## 1 复制模板

把 `template/ModuleTemplate` 整个目录复制到你的工作区（例如 `D:\work\MyModule`）。如果它在 SDK 目录之外，编辑 `NuGet.config`：

```xml
<add key="hyw-sdk" value="D:\path\to\HYWDebugAssistant.Sdk-<版本>\packages" />
```

模板包含：一个收发数据的模块类、一个配置字段、一个命令、一个定时器、一个最小视图、文本表与 `module.json`。它不含外部接入；设备接入参考 `hyw/serial` 的行为与[实现](implementation.md)的“外部接入”。

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
  "resources": ["resources/i18n"]
}
```

对应关系：`id` 必须等于 `[ModuleDefinition]` 的标识；`entryAssembly` 必须是实际 DLL 文件名；`entryType` 必须是入口类的完整类型名。任何一处不一致都会被加载器明确拒绝。标识规则与清单字段见[项目与包布局](project-layout.md)。

## 3 模板里已经有什么

```csharp
[ModuleDefinition("template", "module",
    DisplayNameKey = TemplateText.Title,
    DescriptionKey = TemplateText.Description,
    ViewFactory = typeof(Views.TemplateViewFactory))]
public sealed class TemplateModule : IModule, IActivitySource
{
    public const string InPort = "in";
    public const string OutPort = "out";
    public const string TypeId = "TemplateFrame";

    public static readonly ContractShape Shape = ContractShape.OfStruct(
        ("sequence", ContractShape.OfPrimitive(ContractPrimitive.U64)),
        ("value", ContractShape.OfPrimitive(ContractPrimitive.F32)));

    public void OnRegistered(IModuleContext context)
    {
        context.Declaration.DeclareDataType(new DataTypeDeclaration(TypeId, Shape));
        context.Declaration.DeclarePort(new PortDeclaration(InPort, PortDirection.Receive, Shape) { TypeId = TypeId });
        context.Declaration.DeclarePort(new PortDeclaration(OutPort, PortDirection.Send, Shape) { TypeId = TypeId });
        context.Declaration.DeclareRole(RoleDeclaration.Both("forward", TypeId, InPort, OutPort, "转发"));
        context.Ports.SetReceiveHandler(InPort, OnData);
        // 配置、显示源、命令、定时器见模板源码。
    }
}
```

要点：类型、端口、角色、显示源、输入、命令全部在 `OnRegistered` 声明；数据到达由框架回调；发出的长度必须等于发送口契约尺寸。详细规则见[声明面](contracts.md)与[实现](implementation.md)。

## 4 构建

进入模板目录（`template/ModuleTemplate`）构建：

```powershell
dotnet build -c Release
```

成功标志是模块包目录出现：

```
bin/module-package/Release/TemplateModule/
  module.json
  HYWDebugAssistant.Template.Module.dll
  ...（可选：pdb、resources）
```

包内**不应**出现宿主程序集（`HYWDebugAssistant.Sdk*`、`Avalonia*` 等），打包规则会自动排除；手工打包时按[项目与包布局](project-layout.md)的清单剔除。

## 5 契约自检（推荐）

在模板旁边加一个测试工程，引用 `Sdk.Testing` 与你的模块工程，跑一次声明面检查：

```csharp
var probe = ModuleContract.Probe(typeof(MyModule));
probe.Problems.ShouldBeEmpty(string.Join("；", probe.Problems));
```

测试工程的搭法与更多断言见[测试与验证](testing.md)。`Problems` 非空就是声明错误，先改完再继续。

## 6 装载

1. 启动主应用（`HYWDebugAssistant.App.exe`）；
2. 活动栏「模块清单」→「导入模块…」→ 选择 `bin/module-package/Release/TemplateModule` 这一层目录；
3. 列表里出现来源为「外部」的模块；若提示需重启，点「重新加载」；
4. 选中后点「加入工作区」，桌面出现模块窗口。

验证三件事：窗口出现、端口标记在边缘显示、与另一个模块同名端口能连上并看到数据。连法见[连接与组网](../guides/connect.md)。预览宿主只能看 XAML 设计面，**不能代替这一步**；预览通过也不等于可装载。

如果模块使用既有类型约定（例如 `RawBytes`、`WaveformFrame`），把对端换成对应的**内置模块**再验证一次。两个外部新模块互连只能证明它们彼此自洽，不能证明与既有约定兼容。契约细节见[数据面](../concepts/data-plane.md)与[波形帧约定](../concepts/waveform-frame.md)。

## 7 换成自己的逻辑

| 想改 | 改哪里 |
|---|---|
| 标识与显示名 | `[ModuleDefinition]` 与文本表 |
| 数据类型、端口、角色 | `OnRegistered` 里的 `Declare*`，见[声明面](contracts.md) |
| 收到数据做什么 | 接收回调；发送用 `Ports.TrySend`，见[实现](implementation.md) |
| 周期任务 | 模板里的 `IActivitySource` 与 `IModuleTimer`，见[自发活动与定时器](activity.md) |
| 界面 | 视图工厂 + 模块 XAML，见[界面](ui.md) |
| 文案与图标 | `resources/`，见[资源与文本](resources.md) |

## 常见错误

| 现象 | 原因 | 处理 |
|---|---|---|
| 还原失败（找不到包） | `NuGet.config` 的本地源路径不对，或版本写错 | 确认本地源指向 SDK 的 `packages/`；版本取包文件名里的四段 |
| 报平台配置无效、`obj` 路径里出现别的软件的目录 | 机器上的 `Platform` 环境变量被其它软件设置 | 工程文件里加 `<PlatformName>AnyCPU</PlatformName>`（模板已带），或在干净终端里构建 |
| 导入时报「缺少 module.json」 | 选到了包目录的上一层或下一层 | 选**包含 `module.json` 的那一层** |
| 导入时报「清单与代码标识不一致」 | `module.json` 的 `id` 与特性不同 | 统一成同一个标识 |
| 编译通过但窗口空白 | 视图工厂没声明，或 XAML 未被编译 | 先看[界面](ui.md)的视图与预览；装载后仍空白看[发布与装载](publishing.md)的加载说明 |
| 端口连不上 | 两侧类型标识或形状不同 | 让两侧声明同一类型，或插入转换模块 |
| 命令按钮没反应 | 没实现 `IActivitySource`，或没消费收件箱 | 见[实现](implementation.md)的“收件箱” |

## 相关

- [模块结构](anatomy.md)：七层分别是什么
- [发布与装载](publishing.md)：被拒原因全表
- [接口参考](reference.md)：所有可调用的成员
