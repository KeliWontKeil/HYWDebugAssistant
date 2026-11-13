# 测试与验证

模块的验证分三层：**契约自检**（声明面是否合法）、**行为测试**（模块逻辑是否按约定工作）与**兼容性验证**（能否和既有内置对端建网）。前两层不需要拉起框架；兼容性验证必须在主应用里做。

## 契约自检

在测试项目里引用作者包 `HYWDebugAssistant.Sdk.Testing`：

```csharp
[Fact]
public void 声明面应合法()
{
    // 端口在视图 XAML 的 ModuleView.Ports 里：先把规格读出来再交给 Probe。
    var specs = PortSpecReader.ReadDirectory(
        Path.Combine(AppContext.BaseDirectory, "views"),
        typeof(MyModule).Assembly);

    var probe = ModuleContract.Probe(typeof(MyModule), portSpecs: specs.Specs);

    probe.Problems.ShouldBeEmpty(string.Join("；", probe.Problems));
    probe.Advisories.ShouldBeEmpty();
}
```

- `PortSpecReader` 只依赖 BCL，不需要 Avalonia；它读 `views/*.axaml`（仓库/包内同一份源）。
- 测试输出里没有 `views/` 时，把模块的 `Views/*.axaml` 作为 `Content` 复制到测试输出，或直接把 `ReadDirectory` 指向模块源码目录；不要为了测试再抄一份端口声明。
- 没有 XAML 端口声明的旧模块可省略 `portSpecs`，继续用 C# `DeclarePort`。

- `Problems` 空 = 合法；非空是**必须改**的声明错误（口重名、类型未声明、口形状与类型不一致、角色引用不存在、配置类型缺无参构造……）。
- `Advisories` 是建议（有数据口却零角色、口不属于任何角色、角色口数 > 2）；逐条确认后可以保留，不影响合法性。
- 声明了外部接入时，`Probe` 默认只做结构校验（接入名、单元上限、标准标识非空）；标准标识是否被框架认识，由产品加载时校验。`catalog` 参数只对框架自己的组合级测试有意义，外部作者不需要传。

- 一次校验多个模块：`ModuleContract.CheckAll(params Type[])`，只汇总问题。
- `Probe` 只调用一次 `OnRegistered`，不调用其它钩子，也不启动线程；声明里有副作用的代码（写文件、发网络请求）不应放在声明期。
- `Probe` 按类型名以 `Config` / `Configuration` 结尾扫描配置类型并检查无参构造；其它命名的配置类型不会被它检查，但仍受运行时的 `class + new()` 约束。

## 与内置对端比对

**`Sdk.Testing` 只做自洽性检查，没有“加载另一个模块并比对声明”的 API 或 CLI。** `Probe` 的两个结果列表是 `Ports` 与 `DataTypes`；需要作者侧兼容性保证时，不能用 `Probe` 自己和自己比。

作者侧的兼容性证据只有一条：**在主应用里与至少一个内置对端建网成功并看到数据**。如果模块要交换既有约定，内置对端必须使用同一 `TypeId + Shape`；比对的项目不止尺寸：

- 类型标识；`ContractShape`（字段名、顺序、原语、数组长度）；传递语义；
- 每个端口的 `TypeId`、方向与形状；
- 计量块的长度字段、`SamplesPerUnit`、`ChannelCount`。

契约文字见[数据面](../concepts/data-plane.md)与[波形帧约定](../concepts/waveform-frame.md)。

仓库维护者另有内部手段：用 `Hosting` 的加载/工厂按真实包加载内置模块，跑一次 `OnRegistered`，再对比 `ContractProbeResult` 的 `DataTypes`/`Ports`。这依赖仓库内的 `Hosting`，不在 `Sdk.Testing` 中，**不是外部作者可用的入口**。

已经装过产品的作者可以更早做一次对照：在测试里 `Assembly.LoadFrom` 内置模块的 DLL，用 `Sdk.Testing` 的 `TestModuleContext` 跑一次它的 `OnRegistered`，再把自己与它的 `DataTypes` / `Ports` 逐项比对（形状、`Semantics`、端口语义）。这只是最早的一次契约对照，**不替代**主应用建网实测，也不要把产品 DLL 带进模块包。

失败现象对照（两者都不是加载失败，模块仍会出现在清单里）：

| 现象 | 通常是什么不一致 |
|---|---|
| 建网被拒、端口连不上 | 类型标识、形状，或**传递语义**（`Stream` / `Message` / `Event` 跨语义不判兼容） |
| 连上但数据被丢弃（状态句如"格式不正确"） | 字段级约束不满足：`groupId == 0`、`payloadBytes != 4 × channelCount`、通道数越界等 |

## 新建测试工程

在模块旁边加一个测试工程即可（框架不要求它进任何解决方案）：

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <Nullable>enable</Nullable>
    <IsPackable>false</IsPackable>
    <PlatformName>AnyCPU</PlatformName>
  </PropertyGroup>
  <ItemGroup>
    <PackageReference Include="HYWDebugAssistant.Sdk.Testing" Version="{{version}}" />
    <PackageReference Include="xunit" Version="<与你的测试框架一致>" />
    <PackageReference Include="Shouldly" Version="<可选>" />
    <ProjectReference Include="..\MyModule\HYWDebugAssistant.Vendor.MyModule.csproj" />
  </ItemGroup>
</Project>
```

`Sdk.Testing` 只给测试项目，不能进模块的运行时引用（加载器会拒绝）。

## 行为测试

用 `TestModuleContext` 直接驱动模块的钩子，不起线程、不建网络：

```csharp
[Fact]
public void 收到一个单元应转发并计数()
{
    using var context = new TestModuleContext();
    var module = new MyModule();
    module.OnRegistered(context);

    var payload = new byte[MyModule.Shape.SizeInBytes];
    context.Deliver(MyModule.InPort, payload);

    context.Sent.ShouldContain(entry => entry.PortName == MyModule.OutPort);
    context.Published[MyModule.CountSource].ShouldHaveSingleItem();
}
```

`TestModuleContext` 的主要面：

| 面 | 读法/写法 |
|---|---|
| 声明结果 | `DeclaredPorts`、`DeclaredDataTypes`、`DeclaredRoles`、`DeclaredFlows`、`DeclaredDisplaySources`、`DeclaredInputs`、`DeclaredCommands`、`DeclaredExternalAccesses` |
| 发出的单元 | `Sent`（口名 + 字节） |
| 写入的显示值 | `Published[源名]` |
| 投递数据 | `Deliver(port, payload, layout?)`：有接收回调就直接回调，否则进 Pull 队列 |
| 命令与输入 | `EnqueueCommand(...)` / `EnqueueInput(...)`；`EnqueueInput` 同时写视图状态 |
| 配置 | `SetConfiguration<T>(...)`；模块 `Read<T>` 读到它 |
| 上报 | `ReportedIssues`、`Logs`、`States`、`AppearanceReports` |
| 丢弃与口压力 | `ReportedLosses`、`PortFlowStates`（预置） |
| 显示请求 | `ClearedDisplays`、`SetTexts`、`FullDisplays`（预置） |
| 文件选择 | `FilePickRequests`（只记录，不弹窗） |
| 外部接入 | `Access`（见下） |
| 值 | `Values.ReadText`；`EnqueueInput` 写值/动作输入 |
| 视图状态 | `ViewState`（页签/显示缓冲；控件值请用 `Values`） |
| 定时器 | `Timers`（见下） |

注意：`Deliver` 省略 `layout` 时按“定长字节块”推断形状，Pull 下拿到的 `buffer.Layout` 没有命名字段；需要按字段读取时要显式传 `ContractLayout.FromShape(Shape)`。Pull 模式下要先把数据 `Deliver` 进队列，再调 `OnStep(StepReason.Data)` 自取。

## 定时器与单步

`TestActivityDriver.Run(module, context, token)` 在测试线程上推进测试时钟并调用 `OnStep`：

```csharp
using var context = new TestModuleContext();
var module = new MyModule();
module.OnRegistered(context);
module.OnActivated();

using var cancel = new CancellationTokenSource(TimeSpan.FromMilliseconds(120));
TestActivityDriver.Run(module, context, cancel.Token);

context.Published["count"].ShouldNotBeEmpty();
```

它先调一次 `OnStep(Start)`，随后每轮推进定时器并调 `OnStep(Command | Input)`；它**不投递 Data/Session**，也不代表框架的实例线程与计时服务。要精确控时，直接用测试时钟：

```csharp
var timer = context.Timers.Create();
timer.Interval = TimeSpan.FromMilliseconds(10);
timer.Start();

context.Timers.AdvanceTo(TimeSpan.FromMilliseconds(35));
// 触发 3 次 Tick；AdvanceTo 不会倒流
```

`TestModuleTimer` 还有 `FireNow()`（立即触发一次、不改变下次到期）与 `DueAt`。

## 外部接入测试

```csharp
var module = new MySerialModule();
module.OnRegistered(context);

context.Access.QueueReceived("my-serial", bytes);          // Pull：留给 TryRead
context.Access.Deliver("my-serial", bytes);                // Push：交给已设的回调
context.Access.SetSnapshot("my-serial", snapshot);         // 预置状态/计量
context.Access.WriteSucceeds = false;                      // 模拟写失败

module.OnStep(context, StepReason.Session);

context.Access.OpenRequests.ShouldContain("my-serial");
context.Access.Written.ShouldContain(entry => entry.AccessName == "my-serial");
```

`TestExternalAccess` 还记录 `CloseRequests`、`ClearCounterRequests`；`SetParametersType` 可预置参数类型。

## 视图状态与显示

```csharp
context.ViewState.SetSelectedPage(MyModule.PagesKey, 1);
context.EnqueueInput(MyModule.TextValue, "hello");

module.OnStep(context, StepReason.Input);

context.Values.ReadText(MyModule.TextValue).ShouldBe("hello");
context.ClearedDisplays.ShouldContain(MyModule.LogSource);
context.SetTexts.ShouldContain(entry => entry.SourceName == MyModule.StateSource);
context.ViewState.ReadSeriesWindow(MyModule.CurveSource).ShouldNotBeNull();
```

模块的 `Display.SetText` / `Clear` 在测试替身里落到 `ViewState`，所以“写入后回读保存”这条路径在测试里也走得通。

## 应该测什么

- 声明面：合法、端口/类型/角色一致；
- 收：合法载荷、短载荷、非法帧各自的落点；
- 发：一次调用发几个单元、长度是否等于契约尺寸；
- 显示：值变化才写、丢弃时写计数；
- 命令：未知命令不崩、重复命令的行为；
- 配置：改配置后参数是否生效、非法值是否被拒；
- 外部接入：Push/Pull 二选一、写失败与未打开的落点；
- 定时器：到期动作、Stop 后不再触发。

## 不测什么

- 不测框架的线程、队列、绑定与生命周期状态机（那是框架的组合级测试）；
- 不写像素/布局断言：设计期呈现用预览器看，**装载后的观感与交互必须在主应用实例上看**，截图只作人眼材料；
- 历史缺陷不保留为常驻用例；同一事实只断言一处。

## 包检查

构建后列一遍包内文件，确认没有宿主程序集、`module.json` 与声明的资源目录都在：

```powershell
Get-ChildItem -Recurse bin\module-package\Release\MyModule | Select-Object FullName
```

本仓库的 `tools/verify-standalone-module.ps1` 是**仓库门禁专用**：用 SDK 包构建模板，并由仓库内 `StandaloneModuleHost` 装入。它不在 SDK/产品中，外部作者没有这个脚本，也不能把它当成装载入口。

外部作者的装载验证是[发布与装载](publishing.md)里的主应用流程：导入包 → 加入现场 → 看窗口、端口、命令与数据。模板只用于构建与包内容自查；预览器只验证 XAML 设计面，二者都不等于装载。

## 相关

- [契约声明](contracts.md)
- [发布与装载](publishing.md)
- [接口参考](reference.md)：测试类型清单
