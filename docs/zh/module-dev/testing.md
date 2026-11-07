# 测试与验证

模块的验证分两层：**契约自检**（声明面是否合法）与**行为测试**（模块逻辑是否按约定工作）。两层都不需要拉起框架。

## 契约自检

引用作者包 `HYWDebugAssistant.Sdk.Testing`，在测试项目里跑：

```csharp
[Fact]
public void 声明面应合法()
{
    var probe = ModuleContract.Probe(typeof(MyModule));

    probe.Problems.ShouldBeEmpty(string.Join("；", probe.Problems));
    probe.Advisories.ShouldBeEmpty();
}
```

- `Problems` 空 = 合法；非空就是**必须改**的声明错误（口重名、类型未声明、口形状与类型不一致、角色引用不存在……）。
- `Advisories` 是建议（有口零角色）；逐条确认，不影响合法性。
- 声明了外部接入时，传入标准目录可以顺带校验标准标识与参数类型：`ModuleContract.Probe(type, catalog: new BuiltInExternalAccessCatalog())`。
- 一次校验多个模块：`ModuleContract.CheckAll(params Type[])`，只汇总问题。

## 行为测试

用 `TestModuleContext` 直接驱动模块的钩子，不起线程、不建网络：

```csharp
[Fact]
public void 收到一个单元应转发并计数()
{
    using var context = new TestModuleContext();
    var module = new SampleDataModule();
    module.OnRegistered(context);

    var payload = new byte[SampleDataModule.Shape.SizeInBytes];
    context.Deliver(SampleDataModule.InPort, payload);

    context.Sent.ShouldContain(entry => entry.PortName == SampleDataModule.OutPort);
    context.Published[SampleDataModule.CountSource].ShouldHaveSingleItem();
}
```

可断言的面：

| 面 | 读法 |
|---|---|
| 发出的单元 | `context.Sent`（口名 + 字节） |
| 发布的显示值 | `context.Published[源名]` |
| 命令与输入 | `context.EnqueueCommand(...)` / `context.EnqueueInput(...)` |
| 外部接入 | `context.Access`（脚本化数据、记录开/关请求与写出） |
| 配置 | `context.Configuration.Read<T>()`（未写入时是默认值） |
| 上报 | `context.ReportedIssues` / `ReportedLosses` / `Logs` |

## 应该测什么

- 声明面：合法、端口/类型/角色一致；
- 收：合法载荷、非法帧（`len` 越界、短载荷）各自的落点；
- 发：一次调用发几个单元、长度是否等于契约尺寸；
- 显示：值变化才写、丢弃时写计数；
- 命令：未知命令不崩、重复命令的行为。

## 独立测试工程

外部作者在模块旁边加一个测试工程即可（框架不要求它进任何解决方案）：

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <Nullable>enable</Nullable>
    <IsPackable>false</IsPackable>
  </PropertyGroup>
  <ItemGroup>
    <PackageReference Include="HYWDebugAssistant.Sdk.Testing" Version="<产品四段版本>" />
    <!-- 测试框架自选；断言目标是 Problems / 上下文记录，不依赖具体框架。 -->
    <PackageReference Include="xunit" Version="<与你的测试框架一致>" />
    <ProjectReference Include="..\MyModule\HYWDebugAssistant.Vendor.MyModule.csproj" />
  </ItemGroup>
</Project>
```

## 定时器与单步

`TestActivityDriver.Run(module, context, cancellation)` 在测试线程上推进测试时钟并触发已启动的定时器：

```csharp
using var context = new TestModuleContext();
var module = new MyModule();
module.OnRegistered(context);
module.OnActivated();

using var cancel = new CancellationTokenSource(TimeSpan.FromMilliseconds(120));
TestActivityDriver.Run(module, context, cancel.Token);

context.Published["count"].ShouldNotBeEmpty();
```

它只用于模块级测试，不代表框架的实例线程与计时服务。

## 包检查

构建后列一遍包内文件，确认没有宿主程序集、`module.json` 与声明的资源目录都在：

```powershell
Get-ChildItem -Recurse bin\module-package\Release\MyModule | Select-Object FullName
```

本仓库的 `tools/verify-standalone-module.ps1` 是这条检查的自动化版本（只靠作者包构建 + 装入宿主），外部作者没有该脚本时按上面两步自查。

## 相关

- [契约声明](contracts.md)
- [发布与独立编译](publishing.md)
